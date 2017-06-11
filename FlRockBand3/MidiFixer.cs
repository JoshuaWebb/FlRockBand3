using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NAudio.Midi;

namespace FlRockBand3
{
    public class MidiFixer
    {
        private static readonly int Velocity = 96;
        // TODO: is this always this?
        private static readonly int TicksInClick = 24;
        private const int No32ndNotesInQuarterNote = 8;

        // 2 bars worth of 4/4 count-in
        private static readonly int MusicStartTime = 3840;

        private const ushort PulsesPerQuarterNote = 480;
        private const ushort FlPulsesPerQuarterNote = 96;

        /// <summary>The default PulsesPerQuarterNote from FL Studio is different than expected</summary>
        private const int PulsesPerQuarterNoteMultiplier = PulsesPerQuarterNote / FlPulsesPerQuarterNote;

        public void Fix(string midiPath, string outPath)
        {
            var midi = new MidiWrapper(midiPath);
            RemoveInvalidEventTypes(midi.MidiFile.Events);
            ProcessTimeSignature(midi);
            AddDrumMixEvents(midi);
            RemoveEmptyTracks(midi);
            FixNoteData(midi);
            AddEventsTrack(midi);
            AddVenueTrack(midi);
            AddDefaultDifficultyEvents(TrackName.Drums, midi);

            MidiFile.Export(outPath, midi.MidiFile.Events);
        }

        public static MidiEventCollection UpdatePpq(MidiEventCollection midi, int newPpq)
        {
            var newMidi = new MidiEventCollection(midi.MidiFileType, newPpq);
            var multiplier = (double)newPpq / midi.DeltaTicksPerQuarterNote;
            for (var i = 0; i < midi.Tracks; i++)
            {
                var shiftedEvents = midi[i].Select(e =>
                {
                    var shiftedEvent = e.Clone();
                    shiftedEvent.AbsoluteTime = (long)(shiftedEvent.AbsoluteTime * multiplier);
                    return shiftedEvent;
                });

                newMidi.AddTrack(shiftedEvents);
            }

            return newMidi;
        }

        public static void NormaliseVelocities(MidiEventCollection midi, int newVelocity)
        {
            for (var i = 0; i < midi.Tracks; i++)
            {
                foreach (var noteOnEvent in midi[i].OfType<NoteOnEvent>())
                {
                    noteOnEvent.Velocity = newVelocity;
                    noteOnEvent.OffEvent.Velocity = 0;
                }
            }
        }

        private struct Range
        {
            public string Name { get; }
            public long Start { get; }
            public long End { get; }

            public Range(string name, long start, long end)
            {
                Name = name;
                Start = start;
                End = end;
            }
        }

        private static IEnumerable<Range> GetRanges(IEnumerable<TextEvent> events)
        {
            using (var iter = events.GetEnumerator())
            {
                if (!iter.MoveNext()) yield break;
                var last = iter.Current;

                while (iter.MoveNext())
                {
                    yield return new Range(last.Text, last.AbsoluteTime, iter.Current.AbsoluteTime);
                    last = iter.Current;
                }

                yield return new Range(last.Text, last.AbsoluteTime, long.MaxValue);
            }
        }

        /// <summary>
        /// Used to specify TextEvents from DAWs/Midi creators that can only use Track Names.
        /// A SequenceTrackName event is used as the Text, and a NoteOn event is used as the time.
        ///
        /// Upon completion, there will be a single EVENTS track in the required format.
        /// </summary>
        public static IEnumerable<string> ProcessEventTracks(MidiEventCollection midi, IEnumerable<string> practiceEvents)
        {
            var validEventNames = new HashSet<string>(EventName.SpecialEventNames.Concat(practiceEvents));

            var messages = new List<string>();
            var tracksToRemove = new HashSet<int>();
            var existingTextEvents = new List<TextEvent>();
            var exsitingNoteEvents = new List<MidiEvent>();
            var newEvents = new List<TextEvent>();
            for (var i = 0; i < midi.Tracks; i++)
            {
                var textEvents = midi[i].OfType<TextEvent>();
                var trackNameEvents = textEvents.
                    Where(e => e.MetaEventType == MetaEventType.SequenceTrackName).
                    OrderBy(e => e.AbsoluteTime).
                    ToList();

                if (trackNameEvents.Count == 0)
                    continue;

                var eventsRequiringConversion = trackNameEvents.Where(e => validEventNames.Contains(e.Text)).ToList();
                if (trackNameEvents.Any(e => e.Text == TrackName.Events.ToString()))
                {
                    // These don't mix well, so we don't allow it.
                    if (eventsRequiringConversion.Any())
                    {
                        // TODO: probably not the best exception type??
                        throw new NotSupportedException($"You cannot have '{TrackName.Events}' and '[event]' events on the same track");
                    }

                    messages.AddRange(FilterEventNotes(midi, i, exsitingNoteEvents));

                    // These are regular TextEvents already on the events track
                    // (not SequenceTrackName events that would require a conversion)
                    existingTextEvents.AddRange(textEvents.
                        Where(e => e.MetaEventType == MetaEventType.TextEvent && validEventNames.Contains(e.Text)));

                    tracksToRemove.Add(i);
                }

                var ranges = GetRanges(eventsRequiringConversion);
                foreach(var range in ranges)
                {
                    var notes = midi[i].
                        OfType<NoteOnEvent>().
                        Where(n => n.AbsoluteTime >= range.Start && n.AbsoluteTime < range.End);

                    var eventsForRange = notes.
                        Select(n => new TextEvent(range.Name, MetaEventType.TextEvent, n.AbsoluteTime)).
                        ToList();

                    tracksToRemove.Add(i);

                    if (eventsForRange.Count == 0)
                    {
                        messages.Add($"Warning: Cannot convert '{range.Name}' to an EVENT as it has no notes.");
                        continue;
                    }

                    if (eventsForRange.Count > 1)
                    {
                        messages.Add(
                            $"Warning: Cannot have more than one note for '{range.Name}'; " +
                            "only the first will be converted to an EVENT.");
                    }

                    newEvents.Add(eventsForRange.First());
                }
            }

            RemoveTracks(midi, tracksToRemove);

            var textEventsGroups = existingTextEvents.Concat(newEvents).
                GroupBy(e => e.Text).
                ToList();

            var duplicateEvents = textEventsGroups.Where(kvp => kvp.Count() > 1).Select(kvp => $"'{kvp.Key}'");
            var duplicateWarnings = string.Join(", ", duplicateEvents);

            if (!string.IsNullOrEmpty(duplicateWarnings))
                messages.Add($"Warning: Duplicate events {duplicateWarnings}; using first of each.");

            var uniqueTextEvents = textEventsGroups.Select(kvp => kvp.OrderBy(e => e.AbsoluteTime).First());

            var consolidatedEvents = new List<MidiEvent>(uniqueTextEvents);
            consolidatedEvents.AddRange(exsitingNoteEvents);

            midi.AddNamedTrack(TrackName.Events.ToString(), consolidatedEvents);

            return messages;
        }

        private const int KickDrumSample = 24;
        private const int SnareDrumSample = 25;
        private const int HiHatSample = 26;

        private static readonly HashSet<int> AllowedEventNotes = new HashSet<int>(new[]
        {
            KickDrumSample, SnareDrumSample, HiHatSample
        });

        private static IEnumerable<string> FilterEventNotes(MidiEventCollection midi, int track, List<MidiEvent> targetEvents)
        {
            var messages = new List<string>();
            var noteEvents = midi[track].
                OfType<NoteEvent>().
                GroupBy(e => !AllowedEventNotes.Contains(e.NoteNumber)).
                ToList();

            var invalidNotes = noteEvents.Where(kvp => !kvp.Key).ToList();
            if (invalidNotes.Any())
                messages.Add($"Warning: Ignoring {invalidNotes.Count} note(s) on track {TrackName.Events} (#{track})");

            var validNotes = noteEvents.Where(kvp => kvp.Key).SelectMany(e => e);

            targetEvents.AddRange(validNotes);
            return messages;
        }

        private static void RemoveTracks(MidiEventCollection midi, IEnumerable<int> trackNumbersToRemove)
        {
            // Remove in reverse order so the number of the remaining tracks to remove
            // aren't adjusted by the removals as we go
            foreach (var i in trackNumbersToRemove.OrderByDescending(i => i))
                midi.RemoveTrack(i);
        }

        private static void FixNoteData(MidiWrapper midi)
        {
            var rawMidi = midi.MidiFile;
            for (var i = 0; i < rawMidi.Tracks; i++)
            {
                var trackEvents = rawMidi.Events[i];
                foreach (var trackEvent in trackEvents)
                {
                    var noteEvent = trackEvent as NoteEvent;
                    if (noteEvent != null)
                        noteEvent.Velocity = Velocity;

                    if (noteEvent != null || MidiEvent.IsEndTrack(trackEvent) || trackEvent is TimeSignatureEvent)
                        trackEvent.AbsoluteTime *= PulsesPerQuarterNoteMultiplier;
                }
            }

            // TODO: Figure out how you're supposed to do this....
            // TODO: Don't use crazy reflection if we can avoid it.
            var midiFileDeltaTicksField = typeof(MidiFile).GetField("deltaTicksPerQuarterNote", BindingFlags.Instance | BindingFlags.NonPublic);
            var midiEventCollectionDeltaTicksField = typeof(MidiEventCollection).GetField("deltaTicksPerQuarterNote", BindingFlags.Instance | BindingFlags.NonPublic);
            midiFileDeltaTicksField.SetValue(midi.MidiFile, PulsesPerQuarterNote);
            midiEventCollectionDeltaTicksField.SetValue(midi.MidiFile.Events, PulsesPerQuarterNote);
        }

        private void AddVenueTrack(MidiWrapper midi)
        {
            midi.AddTrack(TrackName.Venue);
        }

        private static void AddEventsTrack(MidiWrapper midi)
        {
            var events = new List<MidiEvent>();

            var beatTrack = midi.TrackEvents(TrackName.Beat.ToString());
            var lastBeatOn = beatTrack.OfType<NoteOnEvent>().OrderBy(e => e.AbsoluteTime).Last(MidiEvent.IsNoteOn);

            events.Add(new TextEvent("[crowd_normal]", MetaEventType.TextEvent, 0));
            events.Add(new TextEvent("[music_start]", MetaEventType.TextEvent, MusicStartTime));

            // Convert event midi tracks to events
            var eventTrackNames = midi.TrackNames.Where(n => Regex.IsMatch(n, @"\[[^\]]+\]")).ToArray();
            foreach (var trackName in eventTrackNames)
            {
                var musicEndTrack = midi.TrackEvents(trackName).OfType<NoteOnEvent>().Single(MidiEvent.IsNoteOn);
                events.Add(new TextEvent(trackName, MetaEventType.TextEvent, musicEndTrack.AbsoluteTime));
                midi.RemoveTrack(trackName);
            }

            // Convert last beat On to [end] event
            midi.RemoveNote(TrackName.Beat, lastBeatOn);
            events.Add(new TextEvent("[end]", MetaEventType.TextEvent, lastBeatOn.AbsoluteTime));

            midi.AddTrack(TrackName.Events, events);
        }

        private static void RemoveEmptyTracks(MidiWrapper midi)
        {
            var rawMidi = midi.MidiFile;
            var tracksToRemove = new List<int>();
            for (var i = 0; i < rawMidi.Tracks; i++)
            {
                var trackEvents = rawMidi.Events[i];

                // If a track only has an EndTrack (and optionally a name) then it is "empty"
                if (!trackEvents.Any(e =>
                    {
                        var textEvent = e as TextEvent;
                        var isNameTrack = false;
                        if (textEvent != null)
                            isNameTrack = textEvent.MetaEventType == MetaEventType.SequenceTrackName;

                        return !(isNameTrack || MidiEvent.IsEndTrack(e));
                    }))
                {
                    tracksToRemove.Add(i);
                }
            }
            midi.RemoveTracks(tracksToRemove);
        }

        private static void ProcessTimeSignature(MidiWrapper midi)
        {
            var defaultTimeSignatureTrack = midi.FindFirstTrackWithEventType(typeof(TimeSignatureEvent));
            midi.RemoveTrack(defaultTimeSignatureTrack);

            var defaultTempoTrack = midi.FindFirstTrackWithEventType(typeof(TempoEvent));
            var timeTrack = midi.MidiFile.Events[defaultTempoTrack];
            var timeSigEvents = midi.TrackEvents(TrackName.InputTimeSig.ToString()).OfType<NoteOnEvent>();
            var groups = timeSigEvents.GroupBy(e => e.AbsoluteTime);

            var lastEventTime = 0L;

            foreach (var pair in groups)
            {
                // The higher velocity value is the numerator (top)
                // And the lower velocity value is the denominator (bottom)
                var sorted = pair.OrderByDescending(e => e.Velocity).ToArray();
                var numerator = sorted[0].NoteNumber;
                var denominator = sorted[1].NoteNumber;

                // TimeSig Event expects the exponent of a base 2 number
                var timeSigEvent = new TimeSignatureEvent(pair.Key, numerator, (int)Math.Round(Math.Log(denominator, 2)), TicksInClick, No32ndNotesInQuarterNote);
                timeTrack.Add(timeSigEvent);
                lastEventTime = pair.Key;
            }

            // update end time
            var endTrack = timeTrack.Single(MidiEvent.IsEndTrack);
            endTrack.AbsoluteTime = lastEventTime;

            // Put the name first
            timeTrack.Insert(0, new TextEvent(midi.Name, MetaEventType.SequenceTrackName, 0));

            // Clean up the Note Track
            midi.RemoveTrack(TrackName.InputTimeSig.ToString());
        }

        private static void AddDrumMixEvents(MidiWrapper midi)
        {
            const int mixEventStartTime = 120;
            var newEvents = new List<MidiEvent>();
            for (var i = 0; i < 4; i++)
                newEvents.Add(new TextEvent($"[mix {i} drums0]", MetaEventType.TextEvent, (i + 1) * mixEventStartTime));

            midi.AddEvents(TrackName.Drums, newEvents);
        }

        private static void AddDefaultDifficultyEvents(TrackName track, MidiWrapper midi)
        {
            var newEvents = new List<MidiEvent>();

            var defaultOctaves = new[] { DifficultyOctave.Easy, DifficultyOctave.Medium, DifficultyOctave.Hard };
            var defaultPitches = new[] { Pitch.E, Pitch.DSharp, Pitch.D, Pitch.CSharp, Pitch.C };

            const int duration = 60;
            const int gap = 12;
            const int channel = 1;
            var noteTime = MusicStartTime;
            foreach (var pitch in defaultPitches)
            {
                foreach (var octave in defaultOctaves)
                {
                    var noteOn = new NoteOnEvent(noteTime, channel, NoteHelper.ToNumber(octave, pitch), Velocity, duration);
                    newEvents.Add(noteOn);
                    newEvents.Add(noteOn.OffEvent);
                }

                noteTime += duration + gap;
            }

            midi.AddEvents(track, newEvents);
        }

        public static void RemoveInvalidEventTypes(MidiEventCollection midi)
        {
            for (var i = 0; i < midi.Tracks; i++)
            {
                var trackEvents = midi[i];
                var invalidEvents = trackEvents.Where(IsInvalid).ToList();
                foreach (var invalidEvent in invalidEvents)
                    trackEvents.Remove(invalidEvent);
            }
        }

        private static bool IsInvalid(MidiEvent midiEvent)
        {
            var eventType = midiEvent.GetType();
            if (eventType == typeof(ControlChangeEvent) ||
                eventType == typeof(PitchWheelChangeEvent) ||
                eventType == typeof(PatchChangeEvent))
                return true;

            return false;
        }

        public static void ConsolidateTracks(MidiEventCollection midi)
        {
            // find all of the names
            var nameCounts = new Dictionary<string, int>();
            for (var i = 0; i < midi.Tracks; i++)
            {
                var names = midi[i].
                    OfType<TextEvent>().
                    Where(e => e.MetaEventType == MetaEventType.SequenceTrackName).
                    ToList();

                if (names.Count > 1)
                {
                    var detail = string.Join(", ", names.Select(n => $"'{n.Text}'"));
                    throw new InvalidOperationException($"Multiple names {detail} on the same track.");
                }

                var name = names.FirstOrDefault()?.Text ?? "";

                int count;
                nameCounts.TryGetValue(name, out count);
                nameCounts[name] = count + 1;
            }

            /* For all of the names that appear on more than one track
             * find all the other tracks that have this name and consolidate them.
             * We iterate multiple times because the track numbers will change every
             * time tracks are consolidated. */
            foreach (var kvp in nameCounts.Where(kvp => kvp.Value > 1))
            {
                var name = kvp.Key;
                var list = new List<MidiEvent>();

                // iterate in reverse so track numbers don't change mid iteration
                for (var i = midi.Tracks - 1; i >= 0; i--)
                {
                    if (!midi[i].OfType<TextEvent>().Any(e => e.IsSequenceTrackName() && e.Text == name))
                        continue;

                    var events = midi[i].Where(e => !MidiEvent.IsEndTrack(e) && !e.IsSequenceTrackName());

                    list.AddRange(events);
                    midi.RemoveTrack(i);
                }

                midi.AddNamedTrack(name, list.OrderBy(e => e.AbsoluteTime));
            }
        }
    }
}
