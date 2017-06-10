using System;
using System.Linq;
using NAudio.Midi;
using NUnit.Framework;

namespace FlRockBand3.Test
{
    [TestFixture]
    public class MidiFixerTests
    {
        [Test]
        public void TestUpdatePpqMultipleEventsOnMultipleTracks()
        {
            const int midiFileType = 1;
            const int originalPpq = 96;
            const int newPpq = 480;

            // TODO: hardcode multiplications in to avoid potential bugs?
            var originalNote = new NoteOnEvent(0, 3, 2, 76, 32);
            var expectedNote = new NoteOnEvent(0 * 5, 3, 2, 76, 32 * 5);
            var originalEnd1 = new MetaEvent(MetaEventType.EndTrack, 0, 1200);
            var expectedEnd1 = new MetaEvent(MetaEventType.EndTrack, 0, 1200 * 5);

            var originalText = new TextEvent("Text 1", MetaEventType.TextEvent, 60);
            var expectedText = new TextEvent("Text 1", MetaEventType.TextEvent, 60 * 5);
            var originalTimeSignature = new TimeSignatureEvent(200, 2, 2, 24, 8);
            var expectedTimeSignature = new TimeSignatureEvent(200 * 5, 2, 2, 24, 8);
            var originalTempo = new TempoEvent(120, 300);
            var expectedTempo = new TempoEvent(120, 300 * 5);
            var originalEnd2 = new MetaEvent(MetaEventType.EndTrack, 0, 4000);
            var expectedEnd2 = new MetaEvent(MetaEventType.EndTrack, 0, 4000 * 5);

            var originalMidi = new MidiEventCollection(midiFileType, originalPpq);
            originalMidi.AddTrack(originalNote, originalNote.OffEvent, originalEnd1);
            originalMidi.AddTrack(originalText, originalTimeSignature, originalTempo, originalEnd2);

            var inputMidi = originalMidi.Clone();

            var expectedMidi = new MidiEventCollection(midiFileType, newPpq);
            expectedMidi.AddTrack(expectedNote, expectedNote.OffEvent, expectedEnd1);
            expectedMidi.AddTrack(expectedText, expectedTimeSignature, expectedTempo, expectedEnd2);

            var actualMidi = MidiFixer.UpdatePpq(inputMidi, newPpq);

            AssertMidiEqual(originalMidi, inputMidi);
            AssertMidiEqual(expectedMidi, actualMidi);
        }

        [TestCase(50, 75, 4, 6)]
        [TestCase(50, 100, 3, 6)]
        [TestCase(50, 100, 0, 0)]
        [TestCase(100, 50, 0, 0)]
        [TestCase(100, 50, 6, 3)]
        [TestCase(100, 75, 6, 4)]
        public void TestUpdatePpqTextEvent(int originalPpq, int newPpq, int originalTime, int newTime)
        {
            var originalMidi = new MidiEventCollection(1, originalPpq);
            var expectedMidi = new MidiEventCollection(1, newPpq);

            originalMidi.AddTrack(new TextEvent("Text 1", MetaEventType.TextEvent, originalTime));
            expectedMidi.AddTrack(new TextEvent("Text 1", MetaEventType.TextEvent, newTime));

            // Create a brand new copy of the original midi so we can verify it wasn't modified
            var inputMidi = originalMidi.Clone();
            var actualMidi = MidiFixer.UpdatePpq(inputMidi, newPpq);

            AssertMidiEqual(originalMidi, inputMidi);
            AssertMidiEqual(expectedMidi, actualMidi);
        }

        [TestCase(50, 75, 4, 6, 8, 12, 5, 20, 30)]
        [TestCase(50, 100, 3, 6, 6, 12, 5, 53, 30)]
        [TestCase(50, 100, 0, 0, 5, 10, 5, 19, 2)]
        [TestCase(100, 75, 6, 4, 12, 9, 5, 20, 19)]
        [TestCase(100, 50, 6, 3, 12, 6, 7, 20, 30)]
        [TestCase(100, 50, 0, 0, 10, 5, 5, 20, 30)]
        public void TestUpdatePpqNoteEvent(int originalPpq, int newPpq, int onTime, int newTime,
            int offTime, int newOffTime, int channel, int noteNumber, int velocity)
        {
            var originalMidi = new MidiEventCollection(1, originalPpq);
            var expectedMidi = new MidiEventCollection(1, newPpq);

            var originalNoteOn = new NoteOnEvent(onTime, channel, noteNumber, velocity, offTime - onTime);
            var expectedNoteOn = new NoteOnEvent(newTime, channel, noteNumber, velocity, newOffTime - newTime);
            originalMidi.AddTrack(originalNoteOn, originalNoteOn.OffEvent);
            expectedMidi.AddTrack(expectedNoteOn, expectedNoteOn.OffEvent);

            // Create a brand new copy of the original midi so we can verify it wasn't modified
            var inputMidi = originalMidi.Clone();

            var actualMidi = MidiFixer.UpdatePpq(inputMidi, newPpq);

            AssertMidiEqual(originalMidi, inputMidi);
            AssertMidiEqual(expectedMidi, actualMidi);
        }

        [Test]
        public void TestNormaliseVelocities()
        {
            const int maxVelocity = sbyte.MaxValue;
            var originalMidi = new MidiEventCollection(1, 200);

            var random = new Random(1000);
            var notes = Enumerable.Range(1, maxVelocity).
                Select(velocity =>
                {
                    // the other properties shouldn't impact the result so
                    // we use a "random" assortment of values to get some
                    // sort of indication that it isn't depending on them
                    var time = random.Next(1000);
                    var channel = random.Next(0, 16) + 1;
                    var number = random.Next(sbyte.MaxValue + 1);
                    var duration = random.Next(100) + 1;
                    return new NoteOnEvent(time, channel, number, velocity, duration);
                }).
                OrderBy(e => e.AbsoluteTime).
                ToList();

            // split over multiple tracks
            originalMidi.AddTrack(notes.Take(maxVelocity / 2));
            originalMidi.AddTrack(notes.Skip(maxVelocity / 2));

            const int normalisedVelocity = 100;
            MidiFixer.NormaliseVelocities(originalMidi, normalisedVelocity);

            Assert.That(originalMidi.OfType<NoteOnEvent>(), Has.All.Property(nameof(NoteEvent.Velocity)).EqualTo(normalisedVelocity));
            Assert.That(originalMidi.SelectMany(t => t).Where(MidiEvent.IsNoteOff), Has.All.Property(nameof(NoteEvent.Velocity)).EqualTo(0));
        }

        [Test]
        public void TestProcessEventNoteTracksExistingEvent()
        {
            const string newEventText = "[existing]";
            var newEventTime = 200;
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(newEventTime, 1, 2, 3, 4);
            originalMidi.AddTrack(
                 new TextEvent(newEventText, MetaEventType.SequenceTrackName, 0),
                noteOn,
                noteOn.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var existingEvent = new TextEvent(newEventText, MetaEventType.TextEvent, 100);
            originalMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                existingEvent.Clone()
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                existingEvent.Clone(),
                new TextEvent(newEventText, MetaEventType.TextEvent, newEventTime),
                new MetaEvent(MetaEventType.EndTrack, 0, Math.Max(existingEvent.AbsoluteTime, newEventTime))
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);
            Assert.That(result, Is.Empty);

            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [TestCase("[x]", 1, 2, 3, 4, 5)]
        [TestCase("[x]", 0, 1, 2, 3, 4)]
        [TestCase("[y]", 101, 2, 3, 4, 5)]
        public void TestProcessEventNoteTracks(string eventText, int t, int c, int n, int v, int d)
        {
            var originalMidi = new MidiEventCollection(1, 200);
            // c, n, v, d should not impact the result
            var noteOn = new NoteOnEvent(t, c, n, v, d);
            originalMidi.AddTrack(
                new TextEvent(eventText, MetaEventType.SequenceTrackName, 0),
                noteOn,
                noteOn.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.TextEvent, t),
                new MetaEvent(MetaEventType.EndTrack, 0, t)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);
            Assert.That(result, Is.Empty);

            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksIgnoreOtherTracks()
        {
            const string eventText = "[Some Event]";
            const int t = 50;
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(t, 1, 2, 3, 4);
            originalMidi.AddTrack(
                new TextEvent(eventText, MetaEventType.SequenceTrackName, 0),
                noteOn,
                noteOn.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            // This track name has no square brackets (so it doesn't count as an EVENT)
            var regularNote = new NoteOnEvent(t, 1, 2, 3, 4);
            var notEventNoteTrack = new MidiEvent[] {
                new TextEvent("not an event track", MetaEventType.SequenceTrackName, 0),
                regularNote,
                regularNote.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            };
            originalMidi.AddTrack(notEventNoteTrack);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.TextEvent, t),
                new MetaEvent(MetaEventType.EndTrack, 0, t)
            );
            expectedMidi.AddTrackCopy(notEventNoteTrack);

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);
            Assert.That(result, Is.Empty);

            // don't care about the order of the tracks
            AssertMidiEquivalent(expectedMidi, originalMidi);
        }

        [TestCase(0, 25, 50, 100)]
        [TestCase(0, 0, 50, 50)]
        public void TestProcessEventNoteTracksMultipleNameEvents(int nameTimeA, int noteTimeA, int nameTimeB, int noteTimeB)
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var nameOneNote = new NoteOnEvent(noteTimeA, 1, 2, 3, 4);
            var nameTwoNote = new NoteOnEvent(noteTimeB, 1, 2, 3, 4);
            originalMidi.AddTrack(
                new TextEvent("[Name A]", MetaEventType.SequenceTrackName, nameTimeA),
                nameOneNote,
                nameOneNote.OffEvent,
                new TextEvent("[Name B]", MetaEventType.SequenceTrackName, nameTimeB),
                nameTwoNote,
                nameTwoNote.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, nameOneNote.OffEvent.AbsoluteTime)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent("[Name A]", MetaEventType.TextEvent, nameOneNote.AbsoluteTime),
                new TextEvent("[Name B]", MetaEventType.TextEvent, nameTwoNote.AbsoluteTime),
                new MetaEvent(MetaEventType.EndTrack, 0, nameTwoNote.AbsoluteTime)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);
            Assert.That(result, Is.Empty);

            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksMultipleNameEventsWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddTrack(
                new TextEvent(eventText, MetaEventType.SequenceTrackName, 1),
                noteOn,
                noteOn.OffEvent,
                new TextEvent("[No Notes]", MetaEventType.SequenceTrackName, noteOn.AbsoluteTime + 1),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.AbsoluteTime)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);

            Assert.That(result, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an event as it has no notes" }));
            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksOverlappingNameEventsWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddTrack(
                new TextEvent("[No Notes]", MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.SequenceTrackName, 0),
                noteOn,
                noteOn.OffEvent,
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.AbsoluteTime)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);

            Assert.That(result, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an event as it has no notes" }));
            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddTrack(
                new TextEvent("[No Notes]", MetaEventType.SequenceTrackName, 0),
                new MetaEvent(MetaEventType.EndTrack, 0, 0)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new MetaEvent(MetaEventType.EndTrack, 0, 0)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);

            Assert.That(result, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an event as it has no notes" }));
            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksIgnoreNonEventTrackNames()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddTrack(
                new TextEvent(eventText, MetaEventType.SequenceTrackName, 1),
                noteOn,
                noteOn.OffEvent,
                // This track has square brackets, but is just a regular TextEvent
                new TextEvent("[Not A Name]", MetaEventType.TextEvent, 2),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.AbsoluteTime)
            );

            var result = MidiFixer.ProcessEventNoteTracks(originalMidi);

            Assert.That(result, Is.Empty);
            AssertMidiEqual(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventNoteTracksMixed()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            originalMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent("[Name One]", MetaEventType.SequenceTrackName, 1),
                noteOn,
                noteOn.OffEvent,
                new TextEvent("[Not A Name]", MetaEventType.TextEvent, 2),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            Assert.Throws<NotSupportedException>(() => MidiFixer.ProcessEventNoteTracks(originalMidi));
        }

        [Test]
        public void TestRemoveInvalidEventTypes()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            const string eventText = "[Name One]";

            var bad1 = new ControlChangeEvent(50, 2, MidiController.Sustain, 20);
            var bad2 = new PatchChangeEvent(29, 4, 1);
            var bad3 = new PitchWheelChangeEvent(50, 5, 50);

            var stn = new TextEvent(eventText, MetaEventType.SequenceTrackName, 1);
            var txt = new TextEvent("Some Text", MetaEventType.TextEvent, 2);
            var tse = new TimeSignatureEvent(0, 2, 2, 24, 8);
            var tpo = new TempoEvent(600000, 20);
            var end = new MetaEvent(MetaEventType.EndTrack, 0, 1000);
            originalMidi.AddTrackCopy(stn, noteOn, bad1, noteOn.OffEvent, txt, tse, bad2, tpo, bad3, end);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrack(stn, noteOn, noteOn.OffEvent, txt, tse, tpo, end);

            MidiFixer.RemoveInvalidEventTypes(originalMidi);
            AssertMidiEqual(expectedMidi, originalMidi);
        }

        // TODO: Should multiple EVENTS be allowed at the same time?
        //       it's a valid MIDI, but what about RB3?
        public void TestMultipleEventsAtSameTime()
        {

        }

        private static void AssertMidiEqual(MidiEventCollection expected, MidiEventCollection actual)
        {
            var comparer = new MidiEventEqualityComparer();

            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks));
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote));
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType));
            for (var i = 0; i < expected.Tracks; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Using(comparer));
        }

        private static void AssertMidiEquivalent(MidiEventCollection expected, MidiEventCollection actual)
        {
            var comparer = new MidiEventEqualityComparer();

            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks));
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote));
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType));
            Assert.That(actual.Select(t => t), Is.EquivalentTo(expected.Select(t => t)).Using(comparer));
        }
    }
}
