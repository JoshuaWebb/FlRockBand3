using NAudio.Midi;
using NUnit.Framework;

namespace FlRockBand3.Test
{
    [TestFixture]
    public class MidiEventCollectionExtensionsTests
    {
        [Test]
        public void TestClone()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent1 = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName1 = "name";
            var trackNameEvent1 = new TextEvent(trackName1, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent1 = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent1.OffEvent.AbsoluteTime);

            var track1 = manualMidi.AddTrack();
            track1.Add(trackNameEvent1);
            track1.Add(noteEvent1);
            track1.Add(noteEvent1.OffEvent);
            track1.Add(endTrackEvent1);

            var noteEvent2 = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName2 = "name";
            var trackNameEvent2 = new TextEvent(trackName2, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent2 = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent2.OffEvent.AbsoluteTime);

            var track2 = manualMidi.AddTrack();
            track2.Add(trackNameEvent2);
            track2.Add(noteEvent2);
            track2.Add(noteEvent2.OffEvent);
            track2.Add(endTrackEvent2);

            var clone = manualMidi.Clone();
            MidiAssert.Equal(manualMidi, clone);

            for (var t = 0; t < manualMidi.Tracks; t++)
            {
                var manualTrack = manualMidi[t];
                var extensionTrack = clone[t];
                for (var e = 1; e < manualTrack.Count - 1; e++)
                    Assert.That(extensionTrack[e], Is.Not.SameAs(manualTrack[e]));
            }
        }

        [Test]
        public void TestAddNamedTrackParams()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent.OffEvent.AbsoluteTime);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            extensionMidi.AddNamedTrack(trackName, noteEvent, noteEvent.OffEvent);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert events (not name / end) are the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 1; e < manualTrack.Count - 1; e++)
                Assert.That(extensionTrack[e], Is.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrack()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent.OffEvent.AbsoluteTime);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            var events = new MidiEvent[] { noteEvent, noteEvent.OffEvent };
            extensionMidi.AddNamedTrack(trackName, events);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert events (not name / end) are the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 1; e < manualTrack.Count - 1; e++)
                Assert.That(extensionTrack[e], Is.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackWithOwnEnd()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, 90);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            var events = new MidiEvent[] { noteEvent, noteEvent.OffEvent, endTrackEvent };
            extensionMidi.AddNamedTrack(trackName, events);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert events (not name) are the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 1; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackParamsWithOwnEnd()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, 90);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            extensionMidi.AddNamedTrack(trackName, noteEvent, noteEvent.OffEvent, endTrackEvent);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert events (not name) are the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 1; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackCopyParams()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent.OffEvent.AbsoluteTime);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            extensionMidi.AddNamedTrackCopy(trackName, noteEvent, noteEvent.OffEvent);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert they aren't the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 0; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.Not.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackCopyParamsWithOwnEnd()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, 90);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            extensionMidi.AddNamedTrackCopy(trackName, noteEvent, noteEvent.OffEvent, endTrackEvent);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert they aren't the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 0; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.Not.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackCopy()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, noteEvent.OffEvent.AbsoluteTime);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            var events = new MidiEvent[] {noteEvent, noteEvent.OffEvent};
            extensionMidi.AddNamedTrackCopy(trackName, events);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert they aren't the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 0; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.Not.SameAs(manualTrack[e]));
        }

        [Test]
        public void TestAddNamedTrackCopyWithOwnEnd()
        {
            var manualMidi = new MidiEventCollection(1, 200);
            var noteEvent = new NoteOnEvent(0, 1, 1, 1, 1);
            const string trackName = "name";
            var trackNameEvent = new TextEvent(trackName, MetaEventType.SequenceTrackName, 0);
            var endTrackEvent = new MetaEvent(MetaEventType.EndTrack, 0, 90);

            var track = manualMidi.AddTrack();
            track.Add(trackNameEvent);
            track.Add(noteEvent);
            track.Add(noteEvent.OffEvent);
            track.Add(endTrackEvent);

            var extensionMidi = new MidiEventCollection(1, 200);
            var events = new MidiEvent[] { noteEvent, noteEvent.OffEvent, endTrackEvent };
            extensionMidi.AddNamedTrackCopy(trackName, events);

            MidiAssert.Equal(manualMidi, extensionMidi);

            // Assert they aren't the same objects
            var manualTrack = manualMidi[0];
            var extensionTrack = extensionMidi[0];
            for (var e = 0; e < manualTrack.Count; e++)
                Assert.That(extensionTrack[e], Is.Not.SameAs(manualTrack[e]));
        }
    }
}
