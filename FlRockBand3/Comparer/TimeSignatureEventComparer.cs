using System.Collections.Generic;
using NAudio.Midi;

namespace FlRockBand3.Comparer
{
    public class TimeSignatureEventComparer : IEqualityComparer<TimeSignatureEvent>
    {
        public bool Equals(TimeSignatureEvent x, TimeSignatureEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            if (x.AbsoluteTime != y.AbsoluteTime) return false;
            if (x.Numerator != y.Numerator) return false;
            if (x.Denominator != y.Denominator) return false;
            if (x.TicksInMetronomeClick != y.TicksInMetronomeClick) return false;
            if (x.No32ndNotesInQuarterNote != y.No32ndNotesInQuarterNote) return false;

            return true;
        }

        public int GetHashCode(TimeSignatureEvent obj)
        {
            return obj.AbsoluteTime.GetHashCode();
        }
    }
}
