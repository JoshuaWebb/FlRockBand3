using System.Linq;
using NAudio.Midi;
using NUnit.Framework;

namespace FlRockBand3.Test
{
    public static class MidiAssert
    {
        public static void Equal(MidiEventCollection expected, MidiEventCollection actual)
        {
            var comparer = new MidiEventEqualityComparer();

            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks));
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote));
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType));
            for (var i = 0; i < expected.Tracks; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Using(comparer));
        }

        public static void Equivalent(MidiEventCollection expected, MidiEventCollection actual)
        {
            var comparer = new MidiEventEqualityComparer();

            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks));
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote));
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType));
            Assert.That(actual.Select(t => t), Is.EquivalentTo(expected.Select(t => t)).Using(comparer));
        }
    }
}
