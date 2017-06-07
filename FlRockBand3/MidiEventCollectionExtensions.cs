using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace FlRockBand3
{
    public static class MidiEventCollectionExtensions
    {
        public static IList<MidiEvent> AddTrack(this MidiEventCollection midiEventCollection, IEnumerable<MidiEvent> intialEvents)
        {
            var newTrack = midiEventCollection.AddTrack();

            var concreteList = newTrack as List<MidiEvent>;
            if (concreteList != null)
            {
                concreteList.AddRange(intialEvents);
            }
            else
            {
                foreach (var midiEvent in intialEvents)
                    newTrack.Add(midiEvent);
            }

            return newTrack;
        }

        public static IList<MidiEvent> AddTrack(this MidiEventCollection midiEventCollection, params MidiEvent[] events)
        {
            return midiEventCollection.AddTrack(events);
        }

        public static MidiEventCollection Clone(this MidiEventCollection original)
        {
            var newCollection = new MidiEventCollection(original.MidiFileType, original.DeltaTicksPerQuarterNote);
            for (var i = 0; i < original.Tracks; i++)
                newCollection.AddTrack(original[i].Select(e => e.Clone()));

            return newCollection;
        }
    }
}
