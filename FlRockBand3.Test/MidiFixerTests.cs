using System;
using System.Collections.Generic;
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

        private static void AssertMidiEqual(MidiEventCollection expected, MidiEventCollection actual)
        {
            var comparer = new MidiEventEqualityComparer();

            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks));
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote));
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType));
            for (var i = 0; i < expected.Tracks; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Using(comparer));
        }
    }
}
