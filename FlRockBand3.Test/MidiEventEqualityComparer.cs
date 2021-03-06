using System.Collections.Generic;
using FlRockBand3.Comparer;
using NAudio.Midi;

namespace FlRockBand3.Test
{
    public class MidiEventEqualityComparer : IEqualityComparer<MidiEvent>
    {
        private readonly TimeSignatureEventEqualityComparer _timeSignatureEventEqualityComparer = new TimeSignatureEventEqualityComparer();
        private readonly TempoEventEqualityComparer _tempoEventEqualityComparer = new TempoEventEqualityComparer();
        private readonly NoteEventEqualityComparer _noteEventEqualityComparer = new NoteEventEqualityComparer();

        public bool Equals(MidiEvent x, MidiEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            if (x.GetType() != y.GetType()) return false;

            var timeSignatureEventX = x as TimeSignatureEvent;
            var timeSignatureEventY = y as TimeSignatureEvent;
            if (timeSignatureEventX != null)
                return _timeSignatureEventEqualityComparer.Equals(timeSignatureEventX, timeSignatureEventY);

            var tempoEventX = x as TempoEvent;
            var tempoEventY = y as TempoEvent;
            if (tempoEventX != null)
                return _tempoEventEqualityComparer.Equals(tempoEventX, tempoEventY);

            var noteEventX = x as NoteEvent;
            var noteEventY = y as NoteEvent;
            if (noteEventX != null)
                return _noteEventEqualityComparer.Equals(noteEventX, noteEventY);

            if (x.AbsoluteTime != y.AbsoluteTime) return false;
            if (x.Channel != y.Channel) return false;
            if (x.CommandCode != y.CommandCode) return false;
            if (x.DeltaTime != y.DeltaTime) return false;

            var textEventX = x as TextEvent;
            var textEventY = y as TextEvent;
            if (textEventX != null && textEventY != null)
            {
                if (textEventX.Text != textEventY.Text) return false;
                if (textEventX.MetaEventType != textEventY.MetaEventType) return false;
            }

            return true;
        }

        public int GetHashCode(MidiEvent obj)
        {
            return obj.AbsoluteTime.GetHashCode();
        }
    }
}
