using System.Collections.Generic;
using NAudio.Midi;

namespace FlRockBand3.Comparer
{
    public class NoteEventEqualityComparer : IEqualityComparer<NoteEvent>
    {
        public bool Equals(NoteEvent x, NoteEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            var noteOnX = x as NoteOnEvent;
            var noteOnY = y as NoteOnEvent;
            if (noteOnX == null ^ noteOnY == null) return false;
            if (noteOnX?.OffEvent.AbsoluteTime != noteOnY?.OffEvent.AbsoluteTime) return false;

            if (x.AbsoluteTime != y.AbsoluteTime) return false;
            if (x.NoteNumber != y.NoteNumber) return false;
            if (x.Channel != y.Channel) return false;
            if (x.Velocity != y.Velocity) return false;

            return true;
        }

        public int GetHashCode(NoteEvent obj)
        {
            return obj.AbsoluteTime.GetHashCode();
        }
    }
}
