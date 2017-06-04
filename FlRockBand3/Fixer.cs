using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace FlRockBand3
{
    public class Fixer
    {
        private static readonly int Velocity = 96;
        // TODO: is this always this?
        private static readonly int TicksInClick = 24;
        private const int NumberOfThirtySecondNotesInQuarterNote = 8;

        /// <summary>
        /// The name of the note track containing the encoded time signature data
        /// </summary>
        private const string TimeSigNoteTrackName = "timesig";

        public void Fix(string midiPath, string outPath)
        {
            var midi = new MidiWrapper(midiPath);
            RemoveInvalidEvents(midi);
            AddDefaultDifficultyEvents(TrackName.Drums, midi);
            AddDrumMixEvents(midi);
            ProcessTimeSignature(midi);
            RemoveEmptyTracks(midi);
            AddEventsTrack(midi);
            AddVenueTrack(midi);

            MidiFile.Export(outPath, midi.MidiFile.Events);
        }

        private void AddVenueTrack(MidiWrapper midi)
        {
            midi.AddTrack(TrackName.Venue);
        }

        private void AddEventsTrack(MidiWrapper midi)
        {
            var events = new List<MidiEvent>();
            // TODO: build events track events
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

            foreach (var trackNo in tracksToRemove.OrderByDescending(i => i))
            {
                midi.RemoveTrack(trackNo);
            }
        }

        private static void ProcessTimeSignature(MidiWrapper midi)
        {
            var defaultTimeSignatureTrack = midi.FindFirstTrackWithEventType(typeof(TimeSignatureEvent));
            midi.RemoveTrack(defaultTimeSignatureTrack);

            var defaultTempoTrack = midi.FindFirstTrackWithEventType(typeof(TempoEvent));
            var timeTrack = midi.MidiFile.Events[defaultTempoTrack];
            var timeSigEvents = midi.TrackEvents(TimeSigNoteTrackName).OfType<NoteOnEvent>();
            var groups = timeSigEvents.GroupBy(e => e.AbsoluteTime);

            foreach (var pair in groups)
            {
                var sorted = pair.OrderByDescending(e => e.Velocity).ToArray();
                var numerator = sorted[0].NoteNumber;
                // TimeSig Event expects the exponent of a base 2 number
                var denominator = (int)Math.Round(Math.Log(sorted[1].NoteNumber, 2));

                var timeSigEvent = new TimeSignatureEvent(pair.Key, numerator, denominator, TicksInClick, NumberOfThirtySecondNotesInQuarterNote);
                timeTrack.Add(timeSigEvent);
            }

            timeTrack.Add(new TextEvent(midi.Name, MetaEventType.SequenceTrackName, 0));

            midi.RemoveTrack(TimeSigNoteTrackName);
        }

        private static void AddDrumMixEvents(MidiWrapper midi)
        {
            var mixEventStartTime = 120;
            var newEvents = new List<MidiEvent>();
            for (var i = 0; i < 4; i++)
                newEvents.Add(new TextEvent($"[mix {i} drums0]", MetaEventType.TextEvent, (i + 1) * mixEventStartTime));

            midi.AddEvents(TrackName.Drums, newEvents);
        }

        private static void AddDefaultDifficultyEvents(TrackName track, MidiWrapper midi)
        {
            var newEvents = new List<MidiEvent>();

            var defaultOcataves = new[] { DifficultyOctave.Easy, DifficultyOctave.Medium, DifficultyOctave.Hard };
            var defaultPitches = new[] { Pitch.E, Pitch.DSharp, Pitch.D, Pitch.CSharp, Pitch.C };

            var startTime = 3840;
            var duration = 60;
            var gap = 30;
            var channel = 1;
            foreach (var pitch in defaultPitches)
            {
                foreach (var octave in defaultOcataves)
                {
                    var noteOn = new NoteOnEvent(startTime, channel, NoteHelper.ToNumber(octave, pitch), Velocity, duration);
                    newEvents.Add(noteOn);
                    newEvents.Add(noteOn.OffEvent);
                }

                startTime += duration + gap;
            }

            midi.AddEvents(track, newEvents);
        }

        private void RemoveInvalidEvents(MidiWrapper midi)
        {
            var rawMidi = midi.MidiFile;
            for (var i = 0; i < rawMidi.Tracks; i++)
            {
                var trackEvents = rawMidi.Events[i];
                var invalidEvents = trackEvents.Where(IsInvalid).ToList();
                foreach (var invalidEvent in invalidEvents)
                {
                    if (!trackEvents.Remove(invalidEvent))
                    {
                        Console.WriteLine("Could not remove invalid event: " + invalidEvent);
                    }
                }
            }
        }

        private bool IsInvalid(MidiEvent midiEvent)
        {
            var eventType = midiEvent.GetType();
            if (eventType == typeof(ControlChangeEvent) || 
                eventType == typeof(PitchWheelChangeEvent) ||
                eventType == typeof(PatchChangeEvent))
                return true;

            return false;
        }
    }
}
