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
        private static readonly int DefaultVelocity = 96;

        // TODO: is this always this?
        private static readonly int TicksInClick = 24;
        private const int No32ndNotesInQuarterNote = 8;

        // Drum notes should be 16th notes at most
        public int MaxDrumNoteLength(MidiEventCollection midi) => midi.DeltaTicksPerQuarterNote / 4;

        private const ushort PulsesPerQuarterNote = 480;

        public delegate void MessageHandler(object sender, MessageHandlerArgs args);
        public event MessageHandler AddMessage;

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

            AddDrumMixEvents(midi);

            AddDefaultDifficultyEventsDrums(midi);

            NormaliseVelocities(midi, DefaultVelocity);

            CapDrumTrackDurations(midi);

            RemoveDuplicateNotes(midi);

            ConvertLastBeatToEnd(midi);

            ValidateBeatTrack(midi);

            AddMusicEndEvent(midi);

            AddMusicStartEvent(midi);

            // Do towards the end in case other processes require "invalid" events
            RemoveInvalidEventTypes(midi);

            midi = ReorderTracks(midi);

            RemoveEmptyTracks(midi);

            AddVenueTrack(midi);

            MidiFile.Export(outPath, midi);
        }

        public void AddInfo(string message)
        {
            AddMessage?.Invoke(this, new MessageHandlerArgs(MessageHandlerArgs.MessageType.Info, message));
        }

        public void AddWarning(string message)
        {
            AddMessage?.Invoke(this, new MessageHandlerArgs(MessageHandlerArgs.MessageType.Warning, message));
        }

        public void AddError(string message)
        {
            AddMessage?.Invoke(this, new MessageHandlerArgs(MessageHandlerArgs.MessageType.Error, message));
        }

        public MidiEventCollection ReorderTracks(MidiEventCollection midi)
        {
            var newMidi = new MidiEventCollection(midi.MidiFileType, midi.DeltaTicksPerQuarterNote);
            var tempoMapIndex = midi.FindTrackNumberByName(TrackName.TempoMap.ToString());

            newMidi.AddTrack(midi[tempoMapIndex]);

            for (var t = 0; t < midi.Tracks; t++)
            {
                if (t != tempoMapIndex)
                    newMidi.AddTrack(midi[t]);
            }

            return newMidi;
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
                return;

            var newEvent = new TextEvent(eventName, MetaEventType.TextEvent, time);
            AddInfo($"Adding {eventName} event at {GetBarInfo(midi, newEvent)}");
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
            // TODO: log which practice sections we're using
            string practiceSections;
            try
            {
                practiceSections = LoadPracticeSectionsFromFile();
            }
            catch (IOException ioe)
            {
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

                // example: [prc_k9] "K section 9"
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
            var newMidi = new MidiEventCollection(midi.MidiFileType, newPpq);
            var multiplier = (double)newPpq / midi.DeltaTicksPerQuarterNote;
            for (var track = 0; track < midi.Tracks; track++)
            {
                var newTrack = newMidi.AddTrackCopy(midi[track]);
                var shifted = new HashSet<MidiEvent>();
                foreach (var clonedEvent in newTrack)
                {
                    if (shifted.Add(clonedEvent))
                        clonedEvent.AbsoluteTime = (long) (clonedEvent.AbsoluteTime * multiplier);

                    // Make sure that we shift the off event whether it is in the track or not.
                    var offEvent = (clonedEvent as NoteOnEvent)?.OffEvent;
                    if (offEvent != null && shifted.Add(offEvent))
                        offEvent.AbsoluteTime = (long)(offEvent.AbsoluteTime * multiplier);
                }
            }

            return newMidi;
        }

        public void NormaliseVelocities(MidiEventCollection midi, int newVelocity)
        {
            for (var t = 0; t < midi.Tracks; t++)
            {
                foreach (var noteOnEvent in midi[t].OfType<NoteOnEvent>())
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
            for (var t = 0; t < midi.Tracks; t++)
            {
                var textEvents = midi[t].OfType<TextEvent>();
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

                    FilterEventNotes(midi, t, exsitingNoteEvents);

                    // These are regular TextEvents already on the events track
                    // (not SequenceTrackName events that would require a conversion)
                    existingTextEvents.AddRange(textEvents.
                        Where(e => e.MetaEventType == MetaEventType.TextEvent && validEventNames.Contains(e.Text)));

                    tracksToRemove.Add(t);
                }

                foreach(var range in eventsRequiringConversion.GetRanges())
                {
                    var notes = midi[t].
                        OfType<NoteOnEvent>().
                        Where(n => n.AbsoluteTime >= range.Start && n.AbsoluteTime < range.End);

                    var eventsForRange = notes.
                        Select(n => new TextEvent(range.Name, MetaEventType.TextEvent, n.AbsoluteTime)).
                        ToList();

                    tracksToRemove.Add(t);

                    if (eventsForRange.Count == 0)
                    {
                        AddWarning($"Cannot convert '{range.Name}' to an EVENT as it has no notes.");
                        continue;
                    }

                    if (eventsForRange.Count > 1)
                    {
                        AddWarning($"Cannot have more than one note for '{range.Name}'; " +
                                   "only the first will be converted to an EVENT.");
                    }

                    var convertedEvent = eventsForRange.First();
                    AddInfo($"{range.Name} event converted at {GetBarInfo(midi, convertedEvent)}");
                    newEvents.Add(convertedEvent);
                }
            }

            RemoveTracks(midi, tracksToRemove);

            var textEventsGroups = existingTextEvents.Concat(newEvents).
                GroupBy(e => e.Text).
                ToList();

            var duplicateEvents = textEventsGroups.Where(kvp => kvp.Count() > 1).Select(kvp => $"'{kvp.Key}'");
            var duplicateWarnings = string.Join(", ", duplicateEvents);

            if (!string.IsNullOrEmpty(duplicateWarnings))
                AddWarning($"Duplicate events {duplicateWarnings}; using first of each.");

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
                AddWarning($"Ignoring {invalidNotes.Count} note(s) on track {TrackName.Events} (#{track})");

            var validNotes = noteEvents.Where(kvp => kvp.Key).SelectMany(e => e);

            targetEvents.AddRange(validNotes);
        }

        private static void RemoveTracks(MidiEventCollection midi, IEnumerable<int> trackNumbersToRemove)
        {
            // Remove in reverse order so the number of the remaining tracks to remove
            // aren't adjusted by the removals as we go
            foreach (var t in trackNumbersToRemove.OrderByDescending(i => i))
                midi.RemoveTrack(t);
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
                AddError($"Invalid note: {beat.NoteName} ({beat.NoteNumber}) at {GetBarInfo(midi, beat)}");

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
                    AddInfo($"{EventName.End} event already exists at {GetBarInfo(midi, existingEvent)}, left last beat in place.");
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
            var newLastEvent = beatTrack.OfType<NoteEvent>().OrderBy(e => e.AbsoluteTime).Last();
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

                if (timeSignatureEvents.Any() || tempoEvents.Any())
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
                    AddError($"Conflicting signatures {details} at {GetBarInfo(midi, conflict.Key)}");
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
                    AddError($"Conflicting tempos {details} at {GetBarInfo(midi, conflict.Key)}");
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

        public void ProcessTimeSignatures(MidiEventCollection midi)
        {
            // This is way easier if these have already been consolidated
            ConsolidateTimeTracks(midi);

            var timeSigTrackNo = midi.FindTrackNumberByName(TrackName.InputTimeSig.ToString());
            if (timeSigTrackNo == -1)
            {
                AddInfo($"No '{TrackName.InputTimeSig}' track");
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
                    AddError($"Incorrect number of time signature notes at {GetBarInfo(midi, time)}: {detail}");
                    continue;
                }

                if (sorted[0].Velocity == sorted[1].Velocity)
                {
                    error = true;
                    var detail = string.Join(", ", sorted.Select(e => $"<{e.NoteName} ({e.NoteNumber}), Velocity: {e.Velocity}>"));
                    AddError($"Multiple notes with the same velocity at {GetBarInfo(midi, time)}: {detail}");
                    continue;
                }

                var numerator = sorted[0].NoteNumber;

                int denominator;
                if (!TryConvertToDenominator(sorted[1].NoteNumber, out denominator))
                {
                    error = true;
                    AddError($"Invalid denominator note '{sorted[1].NoteNumber}' at {time}");
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
                    AddInfo($"{TrackName.Drums} already has at least one '{range.Name}' note.");
                    continue;
                }

                var note = new NoteOnEvent(ThirdBarTime(midi), 1, range.Start, DefaultVelocity, MaxDrumNoteLength(midi));
                track.Add(note);
                track.Add(note.OffEvent);
            }

            UpdateTrackEnd(track);
        }

        public void CapDrumTrackDurations(MidiEventCollection midi)
        {
            var drumPart = midi.GetTrackByName(TrackName.Drums.ToString());

            foreach (var noteOn in drumPart.OfType<NoteOnEvent>())
                noteOn.OffEvent.AbsoluteTime = noteOn.AbsoluteTime + 1;
        }

        public void RemoveInvalidEventTypes(MidiEventCollection midi)
        {
            for (var t = 0; t < midi.Tracks; t++)
            {
                var trackEvents = midi[t];
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
            for (var t = 0; t < midi.Tracks; t++)
            {
                var names = midi[t].
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
                for (var t = midi.Tracks - 1; t >= 0; t--)
                {
                    if (!midi[t].OfType<TextEvent>().Any(e => e.IsSequenceTrackName() && e.Text == name))
                        continue;

                    var events = midi[t].Where(e => !MidiEvent.IsEndTrack(e) && !e.IsSequenceTrackName());

                    list.AddRange(events);
                    midi.RemoveTrack(t);
                }

                midi.AddNamedTrack(name, list.OrderBy(e => e.AbsoluteTime));
            }
        }

        private void RemoveEmptyTracks(MidiEventCollection midi)
        {
            for (var t = midi.Tracks - 1; t >= 0; t--)
                RemoveTrackIfEmpty(midi, t);
        }

        private bool RemoveTrackIfEmpty(MidiEventCollection midi, int trackNumber)
        {
            // If a track only has an EndTrack (and optionally a name) then it is "empty"
            if (midi[trackNumber].All(e => e.IsSequenceTrackName() || MidiEvent.IsEndTrack(e)))
            {
                midi.RemoveTrack(trackNumber);
                return true;
            }

            return false;
        }
    }
}
