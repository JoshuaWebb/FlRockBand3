﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NAudio.Midi;

namespace FlRockBand3
{
    public class Fixer
    {
        private static readonly int Velocity = 96;
        // TODO: is this always this?
        private static readonly int TicksInClick = 24;
        private const int NumberOfThirtySecondNotesInQuarterNote = 8;

        // 2 bars worth of 4/4 count-in
        private static readonly int MusicStartTime = 3840;

        private const ushort PulsesPerQuarterNote = 480;
        private const ushort FlPulsesPerQuarterNote = 96;

        /// <summary>The default PulsesPerQuarterNote from FL Studio is different than expected</summary>
        private const int PulsesPerQuarterNoteMultiplier = PulsesPerQuarterNote / FlPulsesPerQuarterNote;


        public void Fix(string midiPath, string outPath)
        {
            var midi = new MidiWrapper(midiPath);
            RemoveInvalidEvents(midi);
            ProcessTimeSignature(midi);
            AddDrumMixEvents(midi);
            RemoveEmptyTracks(midi);
            FixNoteData(midi);
            AddEventsTrack(midi);
            AddVenueTrack(midi);
            AddDefaultDifficultyEvents(TrackName.Drums, midi);

            MidiFile.Export(outPath, midi.MidiFile.Events);
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
            foreach (var trackName in midi.TrackNames.Where(n => Regex.IsMatch(n, @"\[[^\]]+\]")))
            {
                var musicEndTrack = midi.TrackEvents(trackName).OfType<NoteOnEvent>().Single(MidiEvent.IsNoteOn);
                events.Add(new TextEvent(trackName, MetaEventType.TextEvent, musicEndTrack.AbsoluteTime));
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
            var timeSigEvents = midi.TrackEvents(TrackName.InputTimeSig.ToString()).OfType<NoteOnEvent>();
            var groups = timeSigEvents.GroupBy(e => e.AbsoluteTime);

            var lastEventTime = 0L;

            foreach (var pair in groups)
            {
                var sorted = pair.OrderByDescending(e => e.Velocity).ToArray();
                var numerator = sorted[0].NoteNumber;
                // TimeSig Event expects the exponent of a base 2 number
                var denominator = (int)Math.Round(Math.Log(sorted[1].NoteNumber, 2));

                var timeSigEvent = new TimeSignatureEvent(pair.Key, numerator, denominator, TicksInClick, NumberOfThirtySecondNotesInQuarterNote);
                timeTrack.Add(timeSigEvent);
                lastEventTime = pair.Key;
            }

            // update end time
            var endTrack = timeTrack.Single(MidiEvent.IsEndTrack);
            endTrack.AbsoluteTime = lastEventTime;

            // Put the name first
            timeTrack.Insert(0, new TextEvent(midi.Name, MetaEventType.SequenceTrackName, 0));

            midi.RemoveTrack(TrackName.InputTimeSig.ToString());
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


            var duration = 60;
            var gap = 12;
            var channel = 1;
            var noteTime = MusicStartTime;
            foreach (var pitch in defaultPitches)
            {
                foreach (var octave in defaultOcataves)
                {
                    var noteOn = new NoteOnEvent(noteTime, channel, NoteHelper.ToNumber(octave, pitch), Velocity, duration);
                    newEvents.Add(noteOn);
                    newEvents.Add(noteOn.OffEvent);
                }

                noteTime += duration + gap;
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
