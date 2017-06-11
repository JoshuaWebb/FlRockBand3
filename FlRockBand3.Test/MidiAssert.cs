using System.Linq;
using NAudio.Midi;
using NUnit.Framework;

namespace FlRockBand3.Test
{
    public static class MidiAssert
    {
        private static void BaseEqual(MidiEventCollection expected, MidiEventCollection actual)
        {
            Assert.That(actual.Tracks, Is.EqualTo(expected.Tracks), "Number of tracks not equal.");
            Assert.That(actual.DeltaTicksPerQuarterNote, Is.EqualTo(expected.DeltaTicksPerQuarterNote), "PPQ not equal.");
            Assert.That(actual.MidiFileType, Is.EqualTo(expected.MidiFileType), $"{nameof(actual.MidiFileType)} not equal.");
        }

        public static void Equal(MidiEventCollection expected, MidiEventCollection actual)
        {
            BaseEqual(expected, actual);

            var comparer = new MidiEventEqualityComparer();
            for (var i = 0; i < expected.Tracks; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Using(comparer), $"Track #{i} is different.");
        }

        public static void Equivalent(MidiEventCollection expected, MidiEventCollection actual)
        {
            BaseEqual(expected, actual);

            var comparer = new MidiEventEqualityComparer();
            Assert.That(actual.Select(t => t), Is.EquivalentTo(expected.Select(t => t)).Using(comparer));
        }
    }
}
