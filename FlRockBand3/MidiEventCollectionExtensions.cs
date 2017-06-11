using System;
using System.Collections.Generic;
using System.Linq;
using FlRockBand3.Exceptions;
using NAudio.Midi;

namespace FlRockBand3
{
    public static class MidiEventCollectionExtensions
    {
        public static int FindTrackNumberByName(this MidiEventCollection midiEventCollection, string name)
        {
            var trackNumber = -1;
            for (var t = 0; t < midiEventCollection.Tracks; t++)
            {
                var isMatch = midiEventCollection[t].OfType<TextEvent>().Any(e => e.IsSequenceTrackName() && e.Text == name);
                if (!isMatch)
                    continue;

                if (trackNumber != -1)
                    throw new InvalidOperationException("Multiple tracks named: " + name);

                trackNumber = t;
            }

            return trackNumber;
        }

        public static IList<MidiEvent> FindTrackByName(this MidiEventCollection midiEventCollection, string name)
        {
            var trackNumber = FindTrackNumberByName(midiEventCollection, name);
            return trackNumber == -1 ? null : midiEventCollection[trackNumber];
        }

        public static IList<MidiEvent> GetTrackByName(this MidiEventCollection midiEventCollection, string name)
        {
            var track = midiEventCollection.FindTrackByName(name);
            if (track == null)
                throw new TrackNotFoundException(name);

            return track;
        }

        public static IList<MidiEvent> AddNamedTrackCopy(this MidiEventCollection midiEventCollection, string name, params MidiEvent[] initialEvents)
        {
            return AddNamedTrackCopy(midiEventCollection, name, (IEnumerable<MidiEvent>)initialEvents);
        }

        public static IList<MidiEvent> AddNamedTrackCopy(this MidiEventCollection midiEventCollection, string name, IEnumerable<MidiEvent> initialEvents)
        {
            return AddNamedTrack(midiEventCollection, name, initialEvents.Select(e => e.Clone()));
        }

        public static IList<MidiEvent> AddNamedTrack(this MidiEventCollection midiEventCollection, string name, params MidiEvent[] initialEvents)
        {
            return midiEventCollection.AddNamedTrack(name, (IEnumerable<MidiEvent>)initialEvents);
        }

        public static IList<MidiEvent> AddNamedTrack(this MidiEventCollection midiEventCollection, string name, IEnumerable<MidiEvent> initialEvents)
        {
            var nameEvent = new TextEvent(name, MetaEventType.SequenceTrackName, 0);
            var track = midiEventCollection.AddTrack(nameEvent);
            foreach(var midiEvent in initialEvents)
                track.Add(midiEvent);

            var endEvent = track.OfType<MetaEvent>().Where(MidiEvent.IsEndTrack).SingleOrDefault();
            if (endEvent == null)
            {
                var lastEvent = track.OrderBy(e => e.AbsoluteTime).Last();
                track.Add(new MetaEvent(MetaEventType.EndTrack, 0, lastEvent.AbsoluteTime));
            }

            return track;
        }

        public static IList<MidiEvent> AddTrackCopy(this MidiEventCollection midiEventCollection, params MidiEvent[] initialEvents)
        {
            return midiEventCollection.AddTrackCopy((IList<MidiEvent>)initialEvents);
        }

        public static IList<MidiEvent> AddTrackCopy(this MidiEventCollection midiEventCollection, IEnumerable<MidiEvent> initialEvents)
        {
            return midiEventCollection.AddTrack(initialEvents.Select(e => e.Clone()));
        }

        public static IList<MidiEvent> AddTrack(this MidiEventCollection midiEventCollection, IEnumerable<MidiEvent> initialEvents)
        {
            var newTrack = midiEventCollection.AddTrack();
            foreach (var midiEvent in initialEvents)
                newTrack.Add(midiEvent);

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
                newCollection.AddTrackCopy(original[i]);

            return newCollection;
        }
    }
}
