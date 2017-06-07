using System.Linq;
using NAudio.Midi;
using NUnit.Framework;

namespace FlRockBand3.Test
{
    [TestFixture]
    public class MidiFixerTests
    {
        [Test]
        public void TestUpdatePpq()
        {
            // TODO: this is probably more complex than the code it is testing
            //       We need some abstractions for creating midi files with expected properties
            const int originalPpq = 96;
            const int newPpq = 480;

            const int originalDuration = 16;
            const int timeShift = (newPpq / originalPpq);
            const int newDuration = originalDuration * timeShift;

            const int channel = 3;
            const int velocity = 76;

            const int originalNote1Time = 0;
            const int originalNote2Time = 60;

            const int originalText1Time = 60;
            const string text1Text = "Text 1";

            const int originalText2Time = 90;
            const string text2Text = "Text 2";

            const int endTime1 = 120;
            const int endTime2 = 400;

            var note1 = new NoteOnEvent(originalNote1Time, channel, 1, velocity, originalDuration);
            var note2 = new NoteOnEvent(originalNote2Time, channel, 2, velocity, originalDuration);
            var end1 = new MetaEvent(MetaEventType.EndTrack, 0, endTime1);

            var originalTrack0 = new MidiEvent[] {note1, note2, note1.OffEvent, note2.OffEvent, end1};
            var originalTrack1 = new MidiEvent[]
            {
                new TextEvent(text1Text, MetaEventType.TextEvent, originalText1Time),
                new TextEvent(text2Text, MetaEventType.TextEvent, originalText2Time),
                new MetaEvent(MetaEventType.EndTrack, 0, endTime2)
            };

            var midi = new MidiEventCollection(1, originalPpq);
            midi.AddTrack(originalTrack0.Select(e => e.Clone()).ToList());
            midi.AddTrack(originalTrack1.Select(e => e.Clone()).ToList());

            var expectedNote1 = new NoteOnEvent(originalNote1Time * timeShift, channel, note1.NoteNumber, velocity, newDuration);
            var expectedNote2 = new NoteOnEvent(originalNote2Time * timeShift, channel, note2.NoteNumber, velocity, newDuration);
            var expectedText1 = new TextEvent(text1Text, MetaEventType.TextEvent, originalText1Time * timeShift);
            var expectedText2 = new TextEvent(text2Text, MetaEventType.TextEvent, originalText2Time * timeShift);
            var expectedEnd1 = new MetaEvent(MetaEventType.EndTrack, 0, endTime1 * timeShift);
            var expectedEnd2 = new MetaEvent(MetaEventType.EndTrack, 0, endTime2 * timeShift);

            var expectedTrack0 = new MidiEvent[] {expectedNote1, expectedNote2, expectedNote1.OffEvent, expectedNote2.OffEvent, expectedEnd1};
            var expectedTrack1 = new MidiEvent[] {expectedText1, expectedText2, expectedEnd2};

            var numTracks = midi.Tracks;
            var newMidi = MidiFixer.UpdatePpq(midi, newPpq);

            var comparer = new MidiEventEqualityComparer();

            // Check original midi is not modified
            Assert.AreEqual(originalPpq, midi.DeltaTicksPerQuarterNote);
            Assert.AreEqual(numTracks, midi.Tracks);
            Assert.That(midi[0], Is.EqualTo(originalTrack0).Using(comparer));
            Assert.That(midi[1], Is.EqualTo(originalTrack1).Using(comparer));

            // Check new midi has properties we want
            Assert.AreEqual(newPpq, newMidi.DeltaTicksPerQuarterNote);
            Assert.AreEqual(midi.Tracks, newMidi.Tracks);
            Assert.That(newMidi[0], Is.EqualTo(expectedTrack0).Using(comparer));
            Assert.That(newMidi[1], Is.EqualTo(expectedTrack1).Using(comparer));
        }

    }
}
