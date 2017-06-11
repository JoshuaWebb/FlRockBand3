using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Midi;

namespace FlRockBand3
{
    public static class MidiEventExtensions
    {
        public static bool IsSequenceTrackName(this MidiEvent textEvent)
        {
            return IsSequenceTrackName(textEvent as TextEvent);
        }

        public static bool IsSequenceTrackName(this TextEvent textEvent)
        {
            return textEvent != null && textEvent.MetaEventType == MetaEventType.SequenceTrackName;
        }
    }
}
