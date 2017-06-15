using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace FlRockBand3
{
    public static class MidiEventExtensions
    {
        public static bool IsSequenceTrackName(this MidiEvent midiEvent)
        {
            return IsSequenceTrackName(midiEvent as TextEvent);
        }

        public static bool IsSequenceTrackName(this TextEvent textEvent)
        {
            return textEvent != null && textEvent.MetaEventType == MetaEventType.SequenceTrackName;
        }

        public static DrumMixEvent AsDrumMixEvent(this TextEvent textEvent)
        {
            DrumMixEvent drumMixEvent;
            return DrumMixEvent.TryParse(textEvent, out drumMixEvent) ? drumMixEvent : null;
        }

        public static IEnumerable<Range<long>> GetRanges(this IEnumerable<TextEvent> events)
        {
            using (var iter = events.GetEnumerator())
            {
                if (!iter.MoveNext()) yield break;
                var last = iter.Current;

                while (iter.MoveNext())
                {
                    yield return new Range<long>(last.Text, last.AbsoluteTime, iter.Current.AbsoluteTime);
                    last = iter.Current;
                }

                yield return new Range<long>(last.Text, last.AbsoluteTime, long.MaxValue);
            }
        }

        public static TextEvent FindFirstTextEvent(this IEnumerable<MidiEvent> track, string text)
        {
            return track.OfType<TextEvent>().OrderBy(e => e.AbsoluteTime)
                .FirstOrDefault(e => e.MetaEventType == MetaEventType.TextEvent && e.Text == text);
        }
    }
}
