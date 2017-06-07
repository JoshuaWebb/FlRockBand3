using System;
using System.Collections.Generic;
using NAudio.Midi;

namespace FlRockBand3.Test
{
    public class MidiEventEqualityComparer : IEqualityComparer<MidiEvent>
    {
        public bool Equals(MidiEvent x, MidiEvent y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            if (x.GetType() != y.GetType()) return false;

            if (x.AbsoluteTime != y.AbsoluteTime) return false;
            if (x.Channel != y.Channel) return false;
            if (x.CommandCode != y.CommandCode) return false;
            if (x.DeltaTime != y.DeltaTime) return false;

            if (MidiEvent.IsNoteOn(x) != MidiEvent.IsNoteOn(y)) return false;
            var noteEventX = x as NoteEvent;
            var noteEventY = y as NoteEvent;
            if (noteEventX != null && noteEventY != null)
            {
                if (noteEventX.NoteNumber != noteEventY.NoteNumber) return false;
                if (noteEventX.Velocity != noteEventY.Velocity) return false;
            }

            var textEventX = x as TextEvent;
            var textEventY = y as TextEvent;
            if (textEventX != null && textEventY != null)
            {
                if (textEventX.Text != textEventY.Text) return false;
                if (textEventX.MetaEventType != textEventY.MetaEventType) return false;
            }

            var timeSignatureEventX = x as TimeSignatureEvent;
            var timeSignatureEventY = y as TimeSignatureEvent;
            if (timeSignatureEventX != null && timeSignatureEventY != null)
            {
                if (timeSignatureEventX.Denominator != timeSignatureEventY.Denominator) return false;
                if (timeSignatureEventX.Numerator != timeSignatureEventY.Numerator) return false;
                if (timeSignatureEventX.No32ndNotesInQuarterNote != timeSignatureEventY.No32ndNotesInQuarterNote) return false;
                if (timeSignatureEventX.TicksInMetronomeClick != timeSignatureEventY.TicksInMetronomeClick) return false;
            }

            var tempoEventX = x as TempoEvent;
            var tempoEventY = y as TempoEvent;
            if (tempoEventX != null && tempoEventY != null)
            {
                if (tempoEventX.MicrosecondsPerQuarterNote != tempoEventY.MicrosecondsPerQuarterNote) return false;
                // TODO: Magic epsilon
                if (Math.Abs(tempoEventX.Tempo - tempoEventY.Tempo) > 0.00005f) return false;
            }

            return true;
        }

        public int GetHashCode(MidiEvent obj)
        {
            return obj.GetHashCode();
        }
    }
}
