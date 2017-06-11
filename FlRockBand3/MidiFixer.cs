using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FlRockBand3.Comparer;
using FlRockBand3.Exceptions;
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

        public List<string> Messages { get; } = new List<string>();

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

        public MidiEventCollection UpdatePpq(MidiEventCollection midi, int newPpq)
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
        public void ProcessEventTracks(MidiEventCollection midi, IEnumerable<string> practiceEvents)
        {
            var validEventNames = new HashSet<string>(EventName.SpecialEventNames.Concat(practiceEvents));

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

                    FilterEventNotes(midi, i, exsitingNoteEvents);

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
                        Messages.Add($"Warning: Cannot convert '{range.Name}' to an EVENT as it has no notes.");
                        continue;
                    }

                    if (eventsForRange.Count > 1)
                    {
                        Messages.Add(
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
                Messages.Add($"Warning: Duplicate events {duplicateWarnings}; using first of each.");

            var uniqueTextEvents = textEventsGroups.Select(kvp => kvp.OrderBy(e => e.AbsoluteTime).First());

            var consolidatedEvents = new List<MidiEvent>(uniqueTextEvents);
            consolidatedEvents.AddRange(exsitingNoteEvents);

            midi.AddNamedTrack(TrackName.Events.ToString(), consolidatedEvents);
        }

        private const int KickDrumSample = 24;
        private const int SnareDrumSample = 25;
        private const int HiHatSample = 26;

        private static readonly HashSet<int> AllowedEventNotes = new HashSet<int>(new[]
        {
            KickDrumSample, SnareDrumSample, HiHatSample
        });

        private void FilterEventNotes(MidiEventCollection midi, int track, List<MidiEvent> targetEvents)
        {
            var noteEvents = midi[track].
                OfType<NoteEvent>().
                GroupBy(e => !AllowedEventNotes.Contains(e.NoteNumber)).
                ToList();

            var invalidNotes = noteEvents.Where(kvp => !kvp.Key).ToList();
            if (invalidNotes.Any())
                Messages.Add($"Warning: Ignoring {invalidNotes.Count} note(s) on track {TrackName.Events} (#{track})");

            var validNotes = noteEvents.Where(kvp => kvp.Key).SelectMany(e => e);

            targetEvents.AddRange(validNotes);
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


            midi.AddTrack(TrackName.Events, events);
        }

        public void ConsolidateTimeTracks(MidiEventCollection midi)
        {
            // Duplicate events will be ignored, events with the same time
            // but other differing properties will not
            var allTimeSignatureEvents = new HashSet<TimeSignatureEvent>(new TimeSignatureEventComparer());
            var allTempoEvents = new HashSet<TempoEvent>(new TempoEventComparer());
            for (var t = midi.Tracks - 1; t >= 0; t--)
            {
                var timeSignatureEvents = midi[t].OfType<TimeSignatureEvent>().ToList();
                allTimeSignatureEvents.UnionWith(timeSignatureEvents);
                foreach (var midiEvent in timeSignatureEvents)
                    midi[t].Remove(midiEvent);

                var tempoEvents = midi[t].OfType<TempoEvent>().ToList();
                allTempoEvents.UnionWith(tempoEvents);
                foreach (var midiEvent in tempoEvents)
                    midi[t].Remove(midiEvent);

                RemoveTrackIfEmpty(midi, t);
            }

            var groupedTimeSignatureEvents = allTimeSignatureEvents.
                GroupBy(e => e.AbsoluteTime).
                ToList();

            var conflict = false;
            var conflictingTimeSignatures = groupedTimeSignatureEvents.Where(g => g.Count() > 1).ToList();
            if (conflictingTimeSignatures.Any())
            {
                // TODO: give details...
                // Messages.Add(details);
                conflict = true;
            }

            var groupedTempoEvents = allTempoEvents.
                GroupBy(e => e.AbsoluteTime).
                ToList();

            var conflictingTempos = groupedTempoEvents.Where(g => g.Count() > 1).ToList();
            if (conflictingTempos.Any())
            {
                // TODO: give details...
                // Messages.Add(details);
                conflict = true;
            }

            if (conflict)
                throw new InvalidOperationException("Conflicting time signature/tempo events");

            var events = new List<MidiEvent>();
            events.AddRange(groupedTimeSignatureEvents.Select(g => g.First()));
            events.AddRange(groupedTempoEvents.Select(g => g.First()));

            midi.AddNamedTrack(TrackName.TempoMap.ToString(), events);
        }

        private static void RemoveTrackIfEmpty(MidiEventCollection midi, int trackNumber)
        {
            // If a track only has an EndTrack (and optionally a name) then it is "empty"
            if (midi[trackNumber].All(e => e.IsSequenceTrackName() || MidiEvent.IsEndTrack(e)))
                midi.RemoveTrack(trackNumber);
        }

        public void ProcessTimeSignatures(MidiEventCollection midi)
        {
            // This is way easier if these have already been consolidated
            ConsolidateTimeTracks(midi);

            var timeSigTrackNo = midi.FindTrackNumberByName(TrackName.InputTimeSig.ToString());
            if (timeSigTrackNo == -1)
            {
                Messages.Add($"Info: No '{TrackName.InputTimeSig}' track");
                return;
            }

            var timeEvents = midi[midi.FindTrackNumberByName(TrackName.TempoMap.ToString())];

            var inputTimeSignatureEvents = midi[timeSigTrackNo].OfType<NoteOnEvent>();
            var groups = inputTimeSignatureEvents.GroupBy(e => e.AbsoluteTime);

            var error = false;
            foreach (var pair in groups)
            {
                var time = pair.Key;
                // The higher velocity value is the numerator (top)
                // And the lower velocity value is the denominator (bottom)
                var sorted = pair.OrderByDescending(e => e.Velocity).ToArray();

                // TODO: throw instead/as well... want to report all of the issues at once
                if (sorted.Length != 2)
                {
                    error = true;
                    // TODO: convert to proper bar/time info... absolute time in ticks doesn't really help.
                    var detail = string.Join(", ", sorted.Select(e => e.ToString()));
                    Messages.Add($"Error: Incorrect number of time signature notes at {time}, {detail}");
                    continue;
                }

                var numerator = sorted[0].NoteNumber;

                // TODO: throw instead/as well... want to report all of the issues at once
                int denominator;
                if (!TryConvertToDenominator(sorted[1].NoteNumber, out denominator))
                {
                    error = true;
                    Messages.Add($"Error: Invalid denominator note '{sorted[1].NoteNumber}' at {time}");
                    continue;
                }

                var timeSigEvent = new TimeSignatureEvent(time, numerator, denominator, TicksInClick, No32ndNotesInQuarterNote);
                var existingTimeSigEvent = timeEvents.OfType<TimeSignatureEvent>().SingleOrDefault(e => e.AbsoluteTime == time);
                if (existingTimeSigEvent != null)
                    timeEvents.Remove(existingTimeSigEvent);

                timeEvents.Add(timeSigEvent);
            }

            if (error)
                throw new InvalidOperationException("Invalid time signature input");

            // Clean up input track
            midi.RemoveTrack(timeSigTrackNo);

            SetTrackEnd(timeEvents, timeEvents.OrderBy(e => e.AbsoluteTime).Last().AbsoluteTime);

            // TODO: If there is no TimeSignatureEvent or TempoEvent at 0, wig out
        }

        private static void SetTrackEnd(ICollection<MidiEvent> events, long absoluteTime)
        {
            var endTrack = events.SingleOrDefault(MidiEvent.IsEndTrack);
            if (endTrack == null)
                events.Add(new MetaEvent(MetaEventType.EndTrack, 0, absoluteTime));
            else
                endTrack.AbsoluteTime = absoluteTime;
        }

        private static bool TryConvertToDenominator(int noteNumber, out int denominator)
        {
            // We could use Log2(noteNumber) but given the limited valid inputs/outputs
            // it makes more sense to do this explicitly.
            switch (noteNumber)
            {
                case 2:  denominator = 1; return true;
                case 4:  denominator = 2; return true;
                case 8:  denominator = 3; return true;
                case 16: denominator = 4; return true;
                case 32: denominator = 5; return true;
                default: denominator = 0; return false;
            }
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

        public void RemoveInvalidEventTypes(MidiEventCollection midi)
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

        public void ConsolidateTracks(MidiEventCollection midi)
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
