using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Midi;

namespace FlRockBand3.Comparer
{
    public class NoteEventComparer : IEqualityComparer<NoteEvent>
    {
        public bool Equals(NoteEvent x, NoteEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            if (MidiEvent.IsNoteOn(x) != MidiEvent.IsNoteOn(y)) return false;
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
