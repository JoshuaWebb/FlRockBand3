using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
