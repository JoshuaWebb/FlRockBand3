using System.Collections.Generic;
using NAudio.Midi;

namespace FlRockBand3.Comparer
{
    public class TempoEventComparer : IEqualityComparer<TempoEvent>
    {
        public bool Equals(TempoEvent x, TempoEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            if (x.AbsoluteTime != y.AbsoluteTime) return false;
            if (x.MicrosecondsPerQuarterNote != y.MicrosecondsPerQuarterNote) return false;

            return true;
        }

        public int GetHashCode(TempoEvent obj)
        {
            return obj.AbsoluteTime.GetHashCode();
        }
    }
}
