using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static readonly int MaxDrumNoteDuration = 120;

        // TODO: is this always this?
        private static readonly int TicksInClick = 24;
        private const int No32ndNotesInQuarterNote = 8;

        // 2 bars worth of 4/4 count-in
        private static readonly int MusicStartTime = 3840;

        private const ushort PulsesPerQuarterNote = 480;

        public List<string> Messages { get; } = new List<string>();

        public void Fix(string midiPath, string outPath)
        {
            var practiceSections = LoadPracticeSections();
            var midiFile = new MidiFile(midiPath);
            var midi = midiFile.Events;

            midi = UpdatePpq(midi, PulsesPerQuarterNote);

            ProcessEventTracks(midi, practiceSections);

            // Should happen after `ProcessEventTracks` as the above can handle more
            // cases when the tracks haven't been consolidated yet.
            ConsolidateTracks(midi);

            ProcessTimeSignatures(midi);

            ConvertLastBeatToEnd(midi);

            AddMusicEndEvent(midi);

            AddMusicStartEvent(midi);

            ValidateBeatTrack(midi);

            AddDrumMixEvents(midi);

            AddDefaultDifficultyEventsDrums(midi);

            NormaliseVelocities(midi, Velocity);

            RemoveDuplicateNotes(midi);

            // Do towards the end in case other processes require "invalid" events
            RemoveInvalidEventTypes(midi);

            AddVenueTrack(midi);

            MidiFile.Export(outPath, midi);
        }

        public void RemoveDuplicateNotes(MidiEventCollection midi)
        {
            for (var t = 0; t < midi.Tracks; t++)
            {
                var track = midi[t];
                var existingElements = new HashSet<NoteOnEvent>(new NoteEventEqualityComparer());
                var notesToRemove = new List<NoteOnEvent>();
                foreach (var noteOnEvent in track.OfType<NoteOnEvent>())
                {
                    if (!existingElements.Add(noteOnEvent))
                        notesToRemove.Add(noteOnEvent);
                }

                foreach(var note in notesToRemove)
                {
                    track.Remove(note.OffEvent);
                    track.Remove(note);
                }
            }
        }

        public void AddMusicStartEvent(MidiEventCollection midi)
        {
            var time = ThirdBarTime(midi);
            AddEventIfItDoesNotExist(midi, EventName.MusicStart.ToString(), time);
        }

        public void AddMusicEndEvent(MidiEventCollection midi)
        {
            var eventsTrack =  midi.GetTrackByName(TrackName.Events.ToString());
            var endEvent = eventsTrack.FindFirstTextEvent(EventName.End.ToString());
            Debug.Assert(endEvent != null);

            // TODO: move slightly earlier than the end ??
            AddEventIfItDoesNotExist(midi, EventName.MusicEnd.ToString(), endEvent.AbsoluteTime);
        }

        private void AddEventIfItDoesNotExist(MidiEventCollection midi, string eventName, long time)
        {
            var eventsTrack = midi.GetTrackByName(TrackName.Events.ToString());
            var existingEvent = eventsTrack.FindFirstTextEvent(eventName);
            if (existingEvent != null)
            {
                Messages.Add($"Info: {eventName} event already exists at {GetBarInfo(midi, existingEvent)}");
                return;
            }

            var newEvent = new TextEvent(eventName, MetaEventType.TextEvent, time);
            Messages.Add($"Info: Adding {eventName} event at {GetBarInfo(midi, newEvent)}");
            eventsTrack.Add(newEvent);
        }

        public static MidiEventLocation GetBarInfo(MidiEventCollection midi, MidiEvent midiEvent)
        {
            return GetBarInfo(midi, midiEvent.AbsoluteTime);
        }

        private static MidiEventLocation GetBarInfo(MidiEventCollection midi, long absoluteTime)
        {
            var ppq = midi.DeltaTicksPerQuarterNote;
            var totalQuarterNote = absoluteTime / (double)ppq;
            // TODO: also calcualte the actual bar / beat given time signatures?
            var beatsPerBar = 4;
            var quarterNotesPerBar = 4;
            var fourFourBar = ((int)totalQuarterNote / quarterNotesPerBar) + 1;
            var fourFourBeat = ((int)totalQuarterNote % beatsPerBar) + 1;
            var fourFourTicks = (int)(absoluteTime % ppq);
            return new MidiEventLocation(absoluteTime, fourFourBar, fourFourBeat, fourFourTicks);
        }

        public static IEnumerable<string> LoadPracticeSections()
        {
            string practiceSections;
            try
            {
                practiceSections = LoadPracticeSectionsFromFile();
            }
            catch (IOException ioe)
            {
                // TODO: log message that we're using default practice sections
                practiceSections = Resources.All_Practice_Sections;
            }

            var lines = Regex.Split(practiceSections, "\r?\n");
            var sections = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#"))
                    continue;

                // [prc_k9] "K section 9"
                var parts = line.Split(new [] { ' ' }, 2);
                if (parts[0].StartsWith("[") && parts[0].EndsWith("]"))
                {
                    sections.Add(parts[0]);
                }
                else
                {
                    throw new ArgumentException($"line {i + 1} is invalid: '{line}'");
                }
            }

            return sections;
        }

        private static string LoadPracticeSectionsFromFile()
        {
            var appDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (appDirectory == null)
                throw new FileNotFoundException("Can't find application directory");

            var path = Path.Combine(appDirectory, "All_Practice_Secionts.txt");
            return File.ReadAllText(path);
        }

        public MidiEventCollection UpdatePpq(MidiEventCollection midi, int newPpq)
        {
            var comparer = new NoteEventEqualityComparer();
            var newMidi = new MidiEventCollection(midi.MidiFileType, newPpq);
            var multiplier = (double)newPpq / midi.DeltaTicksPerQuarterNote;
            for (var track = 0; track < midi.Tracks; track++)
            {
                var midiEvents = midi[track] as List<MidiEvent> ?? midi[track].ToList();
                var shiftedEvents = new MidiEvent[midiEvents.Count];
                for (var e = 0; e < midiEvents.Count; e++)
                {
                    var midiEvent = midiEvents[e];
                    var shiftedEvent = midiEvent.Clone();

                    var noteEvent = midiEvent as NoteEvent;
                    if (noteEvent != null)
                    {
                        var noteOnEvent = noteEvent as NoteOnEvent;
                        // NoteOff events are shifted when the NoteOn is shifted
                        if (noteOnEvent == null) continue;

                        var clonedNote = (NoteOnEvent) shiftedEvent;
                        var clonedOff = clonedNote.OffEvent;
                        clonedOff.AbsoluteTime = (long) (clonedOff.AbsoluteTime * multiplier);
                        var offEventIndex = midiEvents.FindIndex(m => comparer.Equals(m as NoteEvent, noteOnEvent.OffEvent));
                        shiftedEvents[offEventIndex] = clonedOff;
                    }

                    shiftedEvent.AbsoluteTime = (long)(shiftedEvent.AbsoluteTime * multiplier);
                    shiftedEvents[e] = shiftedEvent;
                }

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

                foreach(var range in eventsRequiringConversion.GetRanges())
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

        public void AddVenueTrack(MidiEventCollection midi)
        {
            var existingTrack = midi.FindTrackNumberByName(TrackName.Venue.ToString());
            if (existingTrack != -1)
                return;

            midi.AddNamedTrack(TrackName.Venue.ToString());
        }

        private static int DownBeat = 12;
        private static int UpBeat = 13;
        public void ValidateBeatTrack(MidiEventCollection midi)
        {
            var beatTrack = midi.GetTrackByName(TrackName.Beat.ToString());
            var invalidBeats = beatTrack.
                OfType<NoteOnEvent>().
                Where(e => e.NoteNumber != DownBeat && e.NoteNumber != UpBeat).
                ToList();

            foreach (var beat in invalidBeats)
                Messages.Add($"Invalid note: {beat.NoteName} ({beat.NoteNumber}) at {GetBarInfo(midi, beat)}");

            if (invalidBeats.Count > 0)
                throw new InvalidBeatTrackException("Invalid beats detected.");
        }

        public void ConvertLastBeatToEnd(MidiEventCollection midi)
        {
            var beatTrack = midi.GetTrackByName(TrackName.Beat.ToString());

            var lastBeatOn = beatTrack.OfType<NoteOnEvent>().OrderBy(e => e.AbsoluteTime).LastOrDefault(MidiEvent.IsNoteOn);
            if (lastBeatOn == null)
                throw new InvalidBeatTrackException($"No notes were found on the {TrackName.Beat} track");

            var eventsTrack = midi.FindTrackByName(TrackName.Events.ToString());
            if (eventsTrack != null)
            {
                var existingEvent = eventsTrack.FindFirstTextEvent(EventName.End.ToString());
                if (existingEvent != null)
                {
                    Messages.Add($"Info: {EventName.End} event already exists at {GetBarInfo(midi, existingEvent)}, left last beat in place.");
                    return;
                }
            }
            else
            {
                eventsTrack = midi.AddNamedTrack(TrackName.Events.ToString());
            }

            beatTrack.Remove(lastBeatOn);
            beatTrack.Remove(lastBeatOn.OffEvent);

            // Fix beat track end
            var newLastEvent = beatTrack.Where(e => !MidiEvent.IsEndTrack(e)).OrderBy(e => e.AbsoluteTime).Last();
            UpdateTrackEnd(beatTrack, newLastEvent.AbsoluteTime);

            eventsTrack.Add(new TextEvent(EventName.End.ToString(), MetaEventType.TextEvent, lastBeatOn.AbsoluteTime));
            UpdateTrackEnd(eventsTrack, lastBeatOn.AbsoluteTime);
        }

        public void ConsolidateTimeTracks(MidiEventCollection midi)
        {
            // Duplicate events will be ignored, events with the same time
            // but other differing properties will not
            var allTimeSignatureEvents = new HashSet<TimeSignatureEvent>(new TimeSignatureEventEqualityComparer());
            var allTempoEvents = new HashSet<TempoEvent>(new TempoEventEqualityComparer());
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

            var hasConflict = false;
            var conflictingTimeSignatures = groupedTimeSignatureEvents.Where(g => g.Count() > 1).ToList();
            if (conflictingTimeSignatures.Any())
            {
                foreach (var conflict in conflictingTimeSignatures)
                {
                    var details = string.Join(", ", conflict.Select(t => $"[{t.TimeSignature}]"));
                    Messages.Add($"Error: Conflicting signatures {details} at {GetBarInfo(midi, conflict.Key)}");
                }
                hasConflict = true;
            }

            var groupedTempoEvents = allTempoEvents.
                GroupBy(e => e.AbsoluteTime).
                ToList();

            var conflictingTempos = groupedTempoEvents.Where(g => g.Count() > 1).ToList();
            if (conflictingTempos.Any())
            {
                foreach (var conflict in conflictingTempos)
                {
                    var details = string.Join(", ", conflict.Select(t => $"[{t.Tempo}]"));
                    Messages.Add($"Error: Conflicting tempos {details} at {GetBarInfo(midi, conflict.Key)}");
                }
                hasConflict = true;
            }

            if (hasConflict)
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
                if (sorted.Length != 2)
                {
                    error = true;
                    var detail = string.Join(", ", sorted.Select(e => $"<{e.NoteName} ({e.NoteNumber}), Velocity: {e.Velocity}>"));
                    Messages.Add($"Error: Incorrect number of time signature notes at {GetBarInfo(midi, time)}: {detail}");
                    continue;
                }

                if (sorted[0].Velocity == sorted[1].Velocity)
                {
                    error = true;
                    var detail = string.Join(", ", sorted.Select(e => $"<{e.NoteName} ({e.NoteNumber}), Velocity: {e.Velocity}>"));
                    Messages.Add($"Error: Multiple notes with the same velocity at {GetBarInfo(midi, time)}: {detail}");
                    continue;
                }

                var numerator = sorted[0].NoteNumber;

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

            UpdateTrackEnd(timeEvents, timeEvents.OrderBy(e => e.AbsoluteTime).Last().AbsoluteTime);

            // TODO: If there is no TimeSignatureEvent or TempoEvent at 0, wig out
        }

        private static void UpdateTrackEnd(ICollection<MidiEvent> events, long? newTime = null)
        {
            var endTrack = events.SingleOrDefault(MidiEvent.IsEndTrack);
            long absoluteTime;
            if (newTime == null)
            {
                var lastEvent = events.Where(e => !MidiEvent.IsEndTrack(e)).OrderBy(e => e.AbsoluteTime).LastOrDefault();
                absoluteTime = lastEvent?.AbsoluteTime ?? 0;
            }
            else
            {
                absoluteTime = newTime.Value;
            }

            if (endTrack == null)
            {
                events.Add(new MetaEvent(MetaEventType.EndTrack, 0, absoluteTime));
            }
            else
            {
                events.Remove(endTrack);
                endTrack.AbsoluteTime = absoluteTime;
                events.Add(endTrack);
            }
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

        public void AddDrumMixEvents(MidiEventCollection midi)
        {
            var drumTrack = midi.GetTrackByName(TrackName.Drums.ToString());
            var existingMixEvents = drumTrack.
                OfType<TextEvent>().
                Where(e => e.AbsoluteTime == 0 && e.MetaEventType == MetaEventType.TextEvent).
                Select(e => e.AsDrumMixEvent()).
                Where(e => e != null).
                ToList();

            for (var difficulty = 0; difficulty < 4; difficulty++)
            {
                if (existingMixEvents.Any(e => e.Difficulty == difficulty))
                    continue;

                // Try to keep them in order
                drumTrack.Insert(difficulty + 1, DrumMixEvent.DefaultFor(difficulty));
            }
        }

        private static int ThirdBarTime(MidiEventCollection midi)
        {
            return 2 * 4 * midi.DeltaTicksPerQuarterNote;
        }

        public void AddDefaultDifficultyEventsDrums(MidiEventCollection midi)
        {
            var track = midi.GetTrackByName(TrackName.Drums.ToString());

            var notes  = track.OfType<NoteOnEvent>().ToList();
            var expert = new Range<int>("Expert", 96, 100);
            var hard   = new Range<int>("Hard", 84, 88);
            var medium = new Range<int>("Medium", 72, 76);
            var easy   = new Range<int>("Easy", 60, 64);

            foreach (var range in new[] {easy, medium, hard, expert})
            {
                if (notes.Any(n => n.NoteNumber >= range.Start && n.NoteNumber <= range.End))
                {
                    Messages.Add($"Info: {TrackName.Drums} already has at least one '{range.Name}' note.");
                    continue;
                }

                var note = new NoteOnEvent(ThirdBarTime(midi), 1, range.Start, Velocity, MaxDrumNoteDuration);
                track.Add(note);
                track.Add(note.OffEvent);
            }

            UpdateTrackEnd(track);
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
