using System;
using System.Linq;
using FlRockBand3.Exceptions;
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

            var fixer = new MidiFixer();
            var actualMidi = fixer.UpdatePpq(inputMidi, newPpq);

            MidiAssert.Equal(originalMidi, inputMidi);
            MidiAssert.Equal(expectedMidi, actualMidi);
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

            var fixer = new MidiFixer();
            var actualMidi = fixer.UpdatePpq(inputMidi, newPpq);

            MidiAssert.Equal(originalMidi, inputMidi);
            MidiAssert.Equal(expectedMidi, actualMidi);
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

            var fixer = new MidiFixer();
            var actualMidi = fixer.UpdatePpq(inputMidi, newPpq);

            MidiAssert.Equal(originalMidi, inputMidi);
            MidiAssert.Equal(expectedMidi, actualMidi);

            // Verify the NoteOnEvent.OffEvent link
            Assert.AreEqual(actualMidi[0][1], ((NoteOnEvent)actualMidi[0][0]).OffEvent);
        }

        [Test]
        public void TestUpdatePpqNoOffEvent()
        {
            var originalMidi = new MidiEventCollection(1, 100);
            var expectedMidi = new MidiEventCollection(1, 200);

            var originalNoteOn = new NoteOnEvent(10, 1, 1, 50, 40);
            var expectedNoteOn = new NoteOnEvent(20, 1, 1, 50, 80);
            originalMidi.AddTrack(originalNoteOn);
            expectedMidi.AddTrack(expectedNoteOn);

            // Create a brand new copy of the original midi so we can verify it wasn't modified
            var inputMidi = originalMidi.Clone();

            var fixer = new MidiFixer();
            var actualMidi = fixer.UpdatePpq(inputMidi, 200);

            MidiAssert.Equal(originalMidi, inputMidi);
            MidiAssert.Equal(expectedMidi, actualMidi);
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
            originalMidi.AddTrackCopy(notes.Take(maxVelocity / 2).SelectMany(n => new[] { n, n.OffEvent }));
            originalMidi.AddTrackCopy(notes.Skip(maxVelocity / 2).SelectMany(n => new[] { n, n.OffEvent }));

            const int normalisedVelocity = 100;
            MidiFixer.NormaliseVelocities(originalMidi, normalisedVelocity);

            Assert.That(originalMidi.OfType<NoteOnEvent>(), Has.All.Property(nameof(NoteEvent.Velocity)).EqualTo(normalisedVelocity));
            Assert.That(originalMidi.SelectMany(t => t).Where(MidiEvent.IsNoteOff), Has.All.Property(nameof(NoteEvent.Velocity)).EqualTo(0));
        }

        [Test]
        public void TestProcessEventTracksExistingEvent()
        {
            const string existingEventText = "[existing]";
            const int existingEventTime = 100;

            const string newEventText = "[new]";
            const int newEventTime = 200;

            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(newEventTime, 1, 2, 3, 4);
            originalMidi.AddNamedTrack(newEventText, noteOn, noteOn.OffEvent);

            var existingEvent = new TextEvent(existingEventText, MetaEventType.TextEvent, existingEventTime);
            originalMidi.AddNamedTrack(TrackName.Events.ToString(), existingEvent.Clone());

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                existingEvent.Clone(),
                new TextEvent(newEventText, MetaEventType.TextEvent, newEventTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new [] {newEventText, existingEventText});
            Assert.That(fixer.Messages, Is.Empty);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [TestCase("[x]", 0, 1, 2, 3, 4)]
        [TestCase("[x]", 1, 2, 3, 4, 5)]
        [TestCase("[y]", 101, 2, 3, 4, 5)]
        public void TestProcessEventTracks(string eventText, int t, int c, int n, int v, int d)
        {
            var originalMidi = new MidiEventCollection(1, 200);
            // c, n, v, d should not impact the result
            var noteOn = new NoteOnEvent(t, c, n, v, d);
            originalMidi.AddNamedTrack(eventText, noteOn, noteOn.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, t)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] {eventText});
            Assert.That(fixer.Messages, Is.Empty);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksIgnoreOtherTracks()
        {
            const string eventText = "[Some Event]";
            const int t = 50;

            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(t, 1, 2, 3, 4);
            originalMidi.AddNamedTrack(eventText, noteOn, noteOn.OffEvent);

            // This track name is not in the list (so it doesn't count as an EVENT)
            var regularNote = new NoteOnEvent(t, 1, 2, 3, 4);
            var regularTrack = originalMidi.AddNamedTrack("not an event track", regularNote, regularNote.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(), new TextEvent(eventText, MetaEventType.TextEvent, t));
            expectedMidi.AddTrackCopy(regularTrack);

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new [] {eventText});
            Assert.That(fixer.Messages, Is.Empty);

            // don't care about the order of the tracks
            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [TestCase(0, 25, 50, 100)]
        [TestCase(0, 0, 50, 50)]
        public void TestProcessEventTracksMultipleNameEvents(int nameTimeA, int noteTimeA, int nameTimeB, int noteTimeB)
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
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent("[Name A]", MetaEventType.TextEvent, nameOneNote.AbsoluteTime),
                new TextEvent("[Name B]", MetaEventType.TextEvent, nameTwoNote.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name A]", "[Name B]" });
            Assert.That(fixer.Messages, Is.Empty);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksMultipleNameEventsOneEmptyWarning()
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
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]", "[No Notes]" });

            Assert.That(fixer.Messages, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an EVENT as it has no notes." }));
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksDuplicatesDifferentTracksWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            var noteOn2 = new NoteOnEvent(60, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddNamedTrack(eventText, noteOn, noteOn.OffEvent);
            originalMidi.AddNamedTrack(eventText, noteOn2, noteOn2.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]", "[No Notes]" });

            Assert.That(fixer.Messages, Is.EqualTo(new[] { "Warning: Duplicate events '[Name One]'; using first of each." }));
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        // TODO: test sample notes (24 - 26) and invalid notes (everything else) on EVENTS track

        [Test]
        public void TestProcessEventTracksOverlappingNameEventsWarning()
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
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]", "[No Notes]" });

            Assert.That(fixer.Messages, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an EVENT as it has no notes." }));
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksMultipleNotesWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            var noteOn2 = new NoteOnEvent(100, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddNamedTrack(eventText, noteOn, noteOn.OffEvent, noteOn2, noteOn2.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]" });

            Assert.That(fixer.Messages, Is.EqualTo(new[] { "Warning: Cannot have more than one note for '[Name One]'; only the first will be converted to an EVENT." }));
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksEmptyWarning()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrack("[No Notes]");

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString());

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[No Notes]" });

            Assert.That(fixer.Messages, Is.EqualTo(new[] { "Warning: Cannot convert '[No Notes]' to an EVENT as it has no notes." }));
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksIgnoreNonEventTrackNames()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            const string eventText = "[Name One]";
            originalMidi.AddNamedTrack(eventText,
                noteOn,
                noteOn.OffEvent,
                // This track has a valid name, but is just a regular TextEvent
                new TextEvent("[Name Two]", MetaEventType.TextEvent, 2)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(eventText, MetaEventType.TextEvent, noteOn.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]", "[Name Two]" });

            Assert.That(fixer.Messages, Is.Empty);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessEventTracksMixedError()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn = new NoteOnEvent(50, 1, 2, 3, 4);
            originalMidi.AddTrack(
                new TextEvent(TrackName.Events.ToString(), MetaEventType.SequenceTrackName, 0),
                new TextEvent("[Name One]", MetaEventType.SequenceTrackName, 1),
                noteOn,
                noteOn.OffEvent,
                new TextEvent("[Name Two]", MetaEventType.TextEvent, 2),
                new MetaEvent(MetaEventType.EndTrack, 0, noteOn.OffEvent.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            Assert.Throws<NotSupportedException>(() => fixer.ProcessEventTracks(originalMidi, new[] { "[Name One]", "[Name Two]" }));
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

            var fixer = new MidiFixer();
            fixer.RemoveInvalidEventTypes(originalMidi);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTracksMultipleTrackNames()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddTrack(
                new TextEvent("[Name One]", MetaEventType.SequenceTrackName, 1),
                new TextEvent("[Name Two]", MetaEventType.SequenceTrackName, 2),
                new MetaEvent(MetaEventType.EndTrack, 0, 3)
            );

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ConsolidateTracks(originalMidi));
            Assert.AreEqual("Multiple names '[Name One]', '[Name Two]' on the same track.", ex.Message);
        }

        [Test]
        public void TestConsolidateTracks()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn1 = new NoteOnEvent(60, 1, 2, 3, 4);
            var noteOn2 = new NoteOnEvent(50, 1, 2, 3, 4);
            const string trackName = "[Name One]";

            originalMidi.AddNamedTrackCopy(trackName, noteOn1, noteOn1.OffEvent);
            originalMidi.AddNamedTrackCopy(trackName, noteOn2, noteOn2.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(trackName, noteOn2, noteOn2.OffEvent, noteOn1, noteOn1.OffEvent);

            var fixer = new MidiFixer();
            fixer.ConsolidateTracks(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTracksNothingToDo()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn1 = new NoteOnEvent(60, 1, 2, 3, 4);
            var noteOn2 = new NoteOnEvent(50, 1, 2, 3, 4);
            const string trackName1 = "[Name One]";
            const string trackName2 = "[Name Two]";

            var track1 = originalMidi.AddNamedTrack(trackName1, noteOn1, noteOn1.OffEvent);
            var track2 = originalMidi.AddNamedTrack(trackName2, noteOn2, noteOn2.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddTrackCopy(track1);
            expectedMidi.AddTrackCopy(track2);

            var fixer = new MidiFixer();
            fixer.ConsolidateTracks(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateMultipleTracks()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var noteOn1 = new NoteOnEvent(60, 1, 2, 3, 4);
            var noteOn2 = new NoteOnEvent(50, 1, 2, 3, 4);
            var text1 = new TextEvent("Text 1", MetaEventType.TextEvent, 20);
            var text2 = new TextEvent("Text 2", MetaEventType.TextEvent, 30);
            const string trackName1 = "[Name One]";
            const string trackName2 = "[Name Two]";

            originalMidi.AddNamedTrackCopy(trackName1, noteOn1, noteOn1.OffEvent);
            originalMidi.AddNamedTrackCopy(trackName2, noteOn2, noteOn2.OffEvent);
            originalMidi.AddNamedTrackCopy(trackName1, text1);
            originalMidi.AddNamedTrackCopy(trackName2, text2);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(trackName1, text1, noteOn1, noteOn1.OffEvent);
            expectedMidi.AddNamedTrack(trackName2, text2, noteOn2, noteOn2.OffEvent);

            var fixer = new MidiFixer();
            fixer.ConsolidateTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksSimple()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var timeSig = new TimeSignatureEvent(10, 1, 1, 24, 8);
            var tempoEvent = new TempoEvent(600000, 20);
            originalMidi.AddTrackCopy(timeSig);
            originalMidi.AddTrackCopy(tempoEvent);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(), timeSig, tempoEvent);

            var fixer = new MidiFixer();
            fixer.ConsolidateTimeTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksKeep1()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var timeSig = new TimeSignatureEvent(10, 1, 1, 24, 8);
            var text = new TextEvent("Keep track", MetaEventType.TextEvent, 0);
            originalMidi.AddTrackCopy(timeSig, text);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(), timeSig);
            expectedMidi.AddTrackCopy(text);

            var fixer = new MidiFixer();
            fixer.ConsolidateTimeTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksKeep2()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var tempoEvent = new TempoEvent(600000, 20);
            var text = new TextEvent("Keep track", MetaEventType.TextEvent, 0);
            originalMidi.AddTrackCopy(tempoEvent, text);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(), tempoEvent);
            expectedMidi.AddTrackCopy(text);

            var fixer = new MidiFixer();
            fixer.ConsolidateTimeTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksIgnoreDuplicateTempoEvents()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var tempoEvent1 = new TempoEvent(600000, 20);
            var tempoEvent2 = new TempoEvent(600000, 20);
            originalMidi.AddTrackCopy(tempoEvent1, tempoEvent2);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.TempoMap.ToString(), tempoEvent1);

            var fixer = new MidiFixer();
            fixer.ConsolidateTimeTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksIgnoreDuplicateTimeSignatures()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var timeSig1 = new TimeSignatureEvent(10, 1, 1, 24, 8);
            var timeSig2 = new TimeSignatureEvent(10, 1, 1, 24, 8);
            originalMidi.AddTrackCopy(timeSig1, timeSig2);

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.TempoMap.ToString(), timeSig1);

            var fixer = new MidiFixer();
            fixer.ConsolidateTimeTracks(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestConsolidateTimeTracksConflictingTimeSignatures()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var tempo1 = new TempoEvent(600000, 2);
            var tempo2 = new TempoEvent(500000, 2);
            originalMidi.AddTrackCopy(tempo1, tempo2);

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ConsolidateTimeTracks(originalMidi));
            Assert.AreEqual("Conflicting time signature/tempo events", ex.Message);
            Assert.AreEqual(
                new[] {"Error: Conflicting tempos [100], [120] at [1:1 in 4/4 (2 ticks)]"},
                fixer.Messages
            );
        }

        [Test]
        public void TestConsolidateTimeTracksConflictingTempos()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var timeSig1 = new TimeSignatureEvent(10, 4, 2, 24, 8);
            var timeSig2 = new TimeSignatureEvent(10, 6, 3, 24, 8);
            originalMidi.AddTrackCopy(timeSig1, timeSig2);

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ConsolidateTimeTracks(originalMidi));
            Assert.AreEqual("Conflicting time signature/tempo events", ex.Message);
            Assert.AreEqual(
                new[] { "Error: Conflicting signatures [4/4], [6/8] at [1:1 in 4/4 (10 ticks)]" },
                fixer.Messages
            );
        }

        [Test]
        public void TestConvertBeatToEndMissingBeatTrack()
        {
            var originalMidi = new MidiEventCollection(1, 200);

            var fixer = new MidiFixer();
            var ex = Assert.Throws<TrackNotFoundException>(() => fixer.ConvertLastBeatToEnd(originalMidi));

            Assert.AreEqual(TrackName.Beat.ToString(), ex.TrackName);
            Assert.AreEqual("A track named 'BEAT' is required, but cannot be found.", ex.Message);
        }

        [Test]
        public void TestConvertBeatToEndNoBeats()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrack(TrackName.Beat.ToString());

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidBeatTrackException>(() => fixer.ConvertLastBeatToEnd(originalMidi));

            Assert.AreEqual("No notes were found on the BEAT track", ex.Message);
        }

        [Test]
        public void TestConvertBeatToEndNoEvents()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var beatNote1 = new NoteOnEvent(400, 1, 12, 100, 60);
            var beatNote2 = new NoteOnEvent(500, 1, 13, 100, 60);
            originalMidi.AddNamedTrack(TrackName.Beat.ToString(),
                beatNote1, beatNote1.OffEvent,
                beatNote2, beatNote2.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.Beat.ToString(),
                beatNote1, beatNote1.OffEvent
            );

            expectedMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(EventName.End.ToString(), MetaEventType.TextEvent, beatNote2.AbsoluteTime)
            );

            var fixer = new MidiFixer();
            fixer.ConvertLastBeatToEnd(originalMidi);

            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestValidateBeatTrackBadNotes()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var goodNote1 = new NoteOnEvent(300, 1, 12, 100, 60);
            var badNote1 = new NoteOnEvent(400, 1, 2, 100, 60);
            var goodNote2 = new NoteOnEvent(500, 1, 13, 100, 60);
            var badNote2 = new NoteOnEvent(600, 1, 4, 100, 60);
            originalMidi.AddNamedTrack(TrackName.Beat.ToString(),
                goodNote1, goodNote1.OffEvent,
                badNote1, badNote1.OffEvent,
                goodNote2, goodNote2.OffEvent,
                badNote2, badNote2.OffEvent
            );

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidBeatTrackException>(() => fixer.ValidateBeatTrack(originalMidi));

            Assert.AreEqual(new []
            {
                "Invalid note: D0 (2) at [1:3 in 4/4 (400 ticks)]",
                "Invalid note: E0 (4) at [1:4 in 4/4 (600 ticks)]",
            }, fixer.Messages);

            Assert.AreEqual("Invalid beats detected.", ex.Message);
        }

        [Test]
        public void TestValidateBeatTrackGoodNotes()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var goodNote1 = new NoteOnEvent(400, 1, 12, 100, 60);
            var goodNote2 = new NoteOnEvent(500, 1, 13, 100, 60);
            originalMidi.AddNamedTrack(TrackName.Beat.ToString(),
                goodNote1, goodNote1.OffEvent,
                goodNote2, goodNote2.OffEvent
            );

            var fixer = new MidiFixer();
            fixer.ValidateBeatTrack(originalMidi);

            Assert.That(fixer.Messages, Is.Empty);
        }

        [Test]
        public void TestConvertBeatToEndExistingEnd()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            var beatNote1 = new NoteOnEvent(400, 1, 12, 100, 60);

            originalMidi.AddNamedTrack(TrackName.Beat.ToString(), beatNote1, beatNote1.OffEvent);

            var eventsTrack = originalMidi.AddNamedTrack(TrackName.Events.ToString(),
                new TextEvent(EventName.End.ToString(), MetaEventType.TextEvent, 500)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.Beat.ToString(), beatNote1, beatNote1.OffEvent);
            expectedMidi.AddTrackCopy(eventsTrack);

            var fixer = new MidiFixer();
            fixer.ConvertLastBeatToEnd(originalMidi);

            Assert.AreEqual(new [] { "Info: [end] event already exists at [1:3 in 4/4 (500 ticks)], left last beat in place." }, fixer.Messages);
            MidiAssert.Equivalent(expectedMidi, originalMidi);
        }

        [Test]
        public void TestAddDrumMixEventsMissingTrack()
        {
            var originalMidi = new MidiEventCollection(1, 200);

            var fixer = new MidiFixer();
            var ex = Assert.Throws<TrackNotFoundException>(() => fixer.AddDrumMixEvents(originalMidi));
            Assert.AreEqual(TrackName.Drums.ToString(), ex.TrackName);
        }

        [Test]
        public void TestAddDrumMixEventsNonePresent()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrack(TrackName.Drums.ToString());

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Drums.ToString(),
                new TextEvent("[mix 0 drums0]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 1 drums0]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 2 drums0]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 3 drums0]", MetaEventType.TextEvent, 0)
            );

            var fixer = new MidiFixer();
            fixer.AddDrumMixEvents(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestAddDrumMixEventsSomePresent()
        {
            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrack(TrackName.Drums.ToString(),
                new TextEvent("[mix 0 drums0d]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 2 drums2d]", MetaEventType.TextEvent, 0)
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.Drums.ToString(),
                new TextEvent("[mix 0 drums0d]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 1 drums0]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 2 drums2d]", MetaEventType.TextEvent, 0),
                new TextEvent("[mix 3 drums0]", MetaEventType.TextEvent, 0)
            );

            var fixer = new MidiFixer();
            fixer.AddDrumMixEvents(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestAddDefaultDifficultyEventsDrumsMissingTrack()
        {
            var originalMidi = new MidiEventCollection(1, 200);

            var fixer = new MidiFixer();
            var ex = Assert.Throws<TrackNotFoundException>(() => fixer.AddDefaultDifficultyEventsDrums(originalMidi));
            Assert.AreEqual(TrackName.Drums.ToString(), ex.TrackName);
        }

        [Test]
        public void TestAddDefaultDifficultyEventsDrumsNonePresent()
        {
            var ppq = 200;
            // 2 bars of 4 quarter notes * ppq
            var countIn = 1600;

            var easyNote = new NoteOnEvent(countIn, 1, 60, 96, 120);
            var mediumNote = new NoteOnEvent(countIn, 1, 72, 96, 120);
            var hardNote = new NoteOnEvent(countIn, 1, 84, 96, 120);
            var expertNote = new NoteOnEvent(countIn, 1, 96, 96, 120);

            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrack(TrackName.Drums.ToString());

            var expectedMidi = new MidiEventCollection(1, 200);

            expectedMidi.AddNamedTrack(TrackName.Drums.ToString(),
                easyNote, easyNote.OffEvent,
                mediumNote, mediumNote.OffEvent,
                hardNote, hardNote.OffEvent,
                expertNote, expertNote.OffEvent
            );

            var fixer = new MidiFixer();
            fixer.AddDefaultDifficultyEventsDrums(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestAddDefaultDifficultyEventsDrumsSomePresent()
        {
            var ppq = 200;
            // 2 bars of 4 quarter notes * ppq
            var countIn = 1600;

            var easyNote = new NoteOnEvent(countIn, 1, 60, 96, 120);
            var mediumNote = new NoteOnEvent(6840, 1, 73, 96, 120);
            var hardNote = new NoteOnEvent(countIn, 1, 84, 96, 120);
            var expertNote = new NoteOnEvent(6840, 1, 98, 96, 120);

            var originalMidi = new MidiEventCollection(1, 200);
            originalMidi.AddNamedTrackCopy(TrackName.Drums.ToString(),
                mediumNote, mediumNote.OffEvent,
                expertNote, expertNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.Drums.ToString(),
                mediumNote, mediumNote.OffEvent,
                expertNote, expertNote.OffEvent,
                easyNote, easyNote.OffEvent,
                hardNote, hardNote.OffEvent
            );

            var fixer = new MidiFixer();
            fixer.AddDefaultDifficultyEventsDrums(originalMidi);

            Assert.AreEqual(new []
            {
                "Info: PART DRUMS already has at least one 'Medium' note.",
                "Info: PART DRUMS already has at least one 'Expert' note.",
            }, fixer.Messages);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [TestCase(   0, 1, 1,   0)]
        [TestCase( 240, 1, 1, 240)]
        [TestCase( 479, 1, 1, 479)]
        [TestCase( 480, 1, 2,   0)]
        [TestCase( 959, 1, 2, 479)]
        [TestCase( 960, 1, 3,   0)]
        [TestCase(1439, 1, 3, 479)]
        [TestCase(1440, 1, 4,   0)]
        [TestCase(1680, 1, 4, 240)]
        [TestCase(1919, 1, 4, 479)]
        [TestCase(1920, 2, 1,   0)]
        [TestCase(2399, 2, 1, 479)]
        [TestCase(2400, 2, 2,   0)]
        public void TestGetBarInfo(int absoluteTime, int fourFourBar, int fourFourBeat, int fourFourTicks)
        {
            var originalMidi = new MidiEventCollection(1, 480);
            var textEvent = new TextEvent("event", MetaEventType.TextEvent, absoluteTime);

            var location = MidiFixer.GetBarInfo(originalMidi, textEvent);

            Assert.AreEqual(fourFourBar, location.FourFourBar, "Incorrect bar");
            Assert.AreEqual(fourFourBeat, location.FourFourBeat, "Incorrect beat");
            Assert.AreEqual(fourFourTicks, location.FourFourTicks, "Incorrect ticks");
        }

        [TestCase(1,  2, 1)]
        [TestCase(1,  4, 2)]
        [TestCase(1,  8, 3)]
        [TestCase(1, 16, 4)]
        [TestCase(1, 32, 5)]
        [TestCase(2,  4, 2)]
        [TestCase(3,  4, 2)]
        [TestCase(4,  4, 2)]
        [TestCase(5,  4, 2)]
        [TestCase(6,  4, 2)]
        [TestCase(7,  4, 2)]
        [TestCase(8,  4, 2)]
        [TestCase(1,  4, 2)]
        public void TestProcessTimeSignaturesValid(int numerator, int denominator, int expectedDenominator)
        {
            var originalMidi = new MidiEventCollection(1, 200);

            // channel and duration are irrelevant
            var numeratorNote   = new NoteOnEvent(50, 1, numerator, 10, 4);
            var denominatorNote = new NoteOnEvent(50, 1, denominator, 5, 4);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote,
                numeratorNote.OffEvent,
                denominatorNote,
                denominatorNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(),
                new TimeSignatureEvent(50, numerator, expectedDenominator, 24, 8)
            );

            var fixer = new MidiFixer();
            fixer.ProcessTimeSignatures(originalMidi);

            Assert.That(fixer.Messages, Is.Empty);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessTimeSignaturesOverride()
        {
            var originalMidi = new MidiEventCollection(1, 200);

            var existingTimeSig = new TimeSignatureEvent(50, 4, 2, 24, 8);

            // channel and duration are irrelevant
            var numeratorNote = new NoteOnEvent(50, 1, 6, 10, 4);
            var denominatorNote = new NoteOnEvent(50, 1, 8, 5, 4);

            originalMidi.AddNamedTrack(TrackName.TempoMap.ToString(), existingTimeSig);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote,
                numeratorNote.OffEvent,
                denominatorNote,
                denominatorNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(),
                new TimeSignatureEvent(50, 6, 3, 24, 8)
            );

            var fixer = new MidiFixer();
            fixer.ProcessTimeSignatures(originalMidi);

            Assert.That(fixer.Messages, Is.Empty);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessTimeSignaturesMixed()
        {
            var originalMidi = new MidiEventCollection(1, 200);

            var existingTimeSig = new TimeSignatureEvent(50, 4, 2, 24, 8);
            // channel and duration are irrelevant
            var numeratorNote = new NoteOnEvent(100, 1, 6, 10, 4);
            var denominatorNote = new NoteOnEvent(100, 1, 8, 5, 4);

            originalMidi.AddNamedTrackCopy(TrackName.TempoMap.ToString(), existingTimeSig);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote,
                numeratorNote.OffEvent,
                denominatorNote,
                denominatorNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrackCopy(TrackName.TempoMap.ToString(),
                existingTimeSig,
                new TimeSignatureEvent(100, 6, 3, 24, 8)
            );

            var fixer = new MidiFixer();
            fixer.ProcessTimeSignatures(originalMidi);

            Assert.That(fixer.Messages, Is.Empty);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [TestCase(1, 1, 1)]
        [TestCase(1, 2, 4)]
        [TestCase(2, 1, 3)]
        public void TestProcessTimeSignaturesIrrelevant(int channel1, int channel2, int duration)
        {
            var originalMidi = new MidiEventCollection(1, 200);

            // channel and duration are irrelevant
            var numeratorNote   = new NoteOnEvent(50, channel1, 4, 4, duration);
            var denominatorNote = new NoteOnEvent(50, channel2, 4, 2, duration);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote,
                numeratorNote.OffEvent,
                denominatorNote,
                denominatorNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 200);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(),
                new TimeSignatureEvent(50, 4, 2, 24, 8)
            );

            var fixer = new MidiFixer();
            fixer.ProcessTimeSignatures(originalMidi);

            Assert.That(fixer.Messages, Is.Empty);
            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessTimeSignaturesIncorrectNotEnoughNotes()
        {
            var originalMidi = new MidiEventCollection(1, 10);

            // channel, and duration are irrelevant
            var numeratorNote1   = new NoteOnEvent(50, 1, 7, 1, 1);
            var denominatorNote2 = new NoteOnEvent(100, 1, 4, 2, 1);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote1,
                numeratorNote1.OffEvent,
                denominatorNote2,
                denominatorNote2.OffEvent
            );

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ProcessTimeSignatures(originalMidi));

            Assert.AreEqual(new []
            {
                "Error: Incorrect number of time signature notes at [2:2 in 4/4 (50 ticks)]: <G0 (7), Velocity: 1>",
                "Error: Incorrect number of time signature notes at [3:3 in 4/4 (100 ticks)]: <E0 (4), Velocity: 2>"
            }, fixer.Messages);

            Assert.AreEqual("Invalid time signature input", ex.Message);
        }

        [Test]
        public void TestProcessTimeSignaturesNoInput()
        {
            var tempoEvent = new TempoEvent(600000, 20);
            var timeSig = new TimeSignatureEvent(10, 1, 1, 24, 8);

            var originalMidi = new MidiEventCollection(1, 10);
            originalMidi.AddNamedTrackCopy("", tempoEvent);
            originalMidi.AddNamedTrackCopy("", timeSig);

            var expectedMidi = new MidiEventCollection(1, 10);
            expectedMidi.AddNamedTrack(TrackName.TempoMap.ToString(), timeSig, tempoEvent);

            var fixer = new MidiFixer();
            fixer.ProcessTimeSignatures(originalMidi);

            Assert.AreEqual(new[] { "Info: No 'timesig' track"}, fixer.Messages);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestProcessTimeSignaturesSameVelocity()
        {
            var originalMidi = new MidiEventCollection(1, 10);

            // channel, and duration are irrelevant
            var numeratorNote1 = new NoteOnEvent(50, 1, 7, 40, 1);
            var denominatorNote1 = new NoteOnEvent(50, 1, 4, 40, 1);
            var numeratorNote2 = new NoteOnEvent(100, 1, 7, 40, 1);
            var denominatorNote2 = new NoteOnEvent(100, 1, 4, 40, 1);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote1,
                numeratorNote1.OffEvent,
                denominatorNote1,
                denominatorNote1.OffEvent,
                numeratorNote2,
                numeratorNote2.OffEvent,
                denominatorNote2,
                denominatorNote2.OffEvent
            );

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ProcessTimeSignatures(originalMidi));

            Assert.AreEqual(new[]
            {
                "Error: Multiple notes with the same velocity at [2:2 in 4/4 (50 ticks)]:"
                    + " <G0 (7), Velocity: 40>, <E0 (4), Velocity: 40>",
                "Error: Multiple notes with the same velocity at [3:3 in 4/4 (100 ticks)]:"
                    + " <G0 (7), Velocity: 40>, <E0 (4), Velocity: 40>"
            }, fixer.Messages);

            Assert.AreEqual("Invalid time signature input", ex.Message);
        }

        [Test]
        public void TestProcessTimeSignaturesIncorrectTooManyNotes()
        {
            var originalMidi = new MidiEventCollection(1, 10);

            // channel, and duration are irrelevant
            var numeratorNote1   = new NoteOnEvent(50, 1, 7, 3, 1);
            var denominatorNote1 = new NoteOnEvent(50, 1, 4, 2, 1);
            var thirdNote1       = new NoteOnEvent(50, 1, 5, 1, 1);

            var numeratorNote2   = new NoteOnEvent(100, 1, 7, 3, 1);
            var denominatorNote2 = new NoteOnEvent(100, 1, 4, 2, 1);
            var thirdNote2       = new NoteOnEvent(100, 1, 5, 1, 1);

            originalMidi.AddNamedTrack(TrackName.InputTimeSig.ToString(),
                numeratorNote1,
                numeratorNote1.OffEvent,
                denominatorNote1,
                denominatorNote1.OffEvent,
                thirdNote1,
                thirdNote1.OffEvent,

                numeratorNote2,
                numeratorNote2.OffEvent,
                denominatorNote2,
                denominatorNote2.OffEvent,
                thirdNote2,
                thirdNote2.OffEvent
            );

            var fixer = new MidiFixer();
            var ex = Assert.Throws<InvalidOperationException>(() => fixer.ProcessTimeSignatures(originalMidi));

            Assert.AreEqual(new[]
            {
                "Error: Incorrect number of time signature notes at [2:2 in 4/4 (50 ticks)]:"
                    + " <G0 (7), Velocity: 3>, <E0 (4), Velocity: 2>, <F0 (5), Velocity: 1>",
                "Error: Incorrect number of time signature notes at [3:3 in 4/4 (100 ticks)]:"
                    + " <G0 (7), Velocity: 3>, <E0 (4), Velocity: 2>, <F0 (5), Velocity: 1>"
            }, fixer.Messages);

            Assert.AreEqual("Invalid time signature input", ex.Message);
        }

        [Test]
        public void TestRemoveDuplicateNotesIgnoreOnDifferentTracks()
        {
            var originalMidi = new MidiEventCollection(1, 10);
            var note = new NoteOnEvent(100, 1, 1, 10, 10);
            originalMidi.AddNamedTrackCopy("", note, note.OffEvent);
            originalMidi.AddNamedTrackCopy("", note, note.OffEvent);

            var expectedMidi = new MidiEventCollection(1, 10);
            expectedMidi.AddNamedTrackCopy("", note, note.OffEvent);
            expectedMidi.AddNamedTrackCopy("", note, note.OffEvent);

            var fixer = new MidiFixer();
            fixer.RemoveDuplicateNotes(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [Test]
        public void TestRemoveDuplicateNotesRemoveOnSameTrack()
        {
            var originalMidi = new MidiEventCollection(1, 10);
            var note = new NoteOnEvent(100, 1, 1, 10, 10);
            var duplicateNote = (NoteOnEvent)note.Clone();
            originalMidi.AddNamedTrackCopy("",
                note, note.OffEvent,
                duplicateNote, duplicateNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 10);
            expectedMidi.AddNamedTrackCopy("", note, note.OffEvent);

            var fixer = new MidiFixer();
            fixer.RemoveDuplicateNotes(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        [TestCase(200, 1, 1, 10, 10)]
        [TestCase(100, 2, 1, 10, 10)]
        [TestCase(100, 1, 2, 10, 10)]
        [TestCase(100, 1, 1, 20, 10)]
        [TestCase(100, 1, 1, 10, 20)]
        [TestCase(200, 2, 2, 20, 20)]
        public void TestRemoveDuplicateNotesKeepDifferentNotes(
            int time, int channel, int number, int velocity, int duration)
        {
            var originalMidi = new MidiEventCollection(1, 10);
            var name = "name";
            var note = new NoteOnEvent(100, 1, 1, 10, 10);
            var otherNote = new NoteOnEvent(time, channel, number, velocity, duration);
            originalMidi.AddNamedTrackCopy(name,
                note, note.OffEvent,
                otherNote, otherNote.OffEvent
            );

            var expectedMidi = new MidiEventCollection(1, 10);
            expectedMidi.AddNamedTrackCopy(name,
                note, note.OffEvent,
                otherNote, otherNote.OffEvent
            );

            var fixer = new MidiFixer();
            fixer.RemoveDuplicateNotes(originalMidi);

            MidiAssert.Equal(expectedMidi, originalMidi);
        }

        // TODO: multiple events are allowed, but not multiple events of the same type
        //       e.g.
        //       ok:
        //          10 crowd_normal
        //          10 prc_intro_a
        //       bad:
        //          20 crowd_normal
        //          20 crowd_intense
        //       bad:
        //          30 prc_intro_a
        //          30 prc_intro_b
        public void TestMultipleEventsAtSameTime()
        {

        }
    }
}
