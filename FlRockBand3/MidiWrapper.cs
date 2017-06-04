using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Midi;

namespace FlRockBand3
{
    public class MidiWrapper
    {
        public MidiFile MidiFile { get; set; }

        private readonly Dictionary<string, int> _tracksNumbersByName = new Dictionary<string, int>();

        public string Name { get; private set; }

        public MidiWrapper(string filePath)
        {
            MidiFile = new MidiFile(filePath);
            Name = Path.GetFileNameWithoutExtension(filePath);
            IndexTrackNames();
        }

        public void RemoveTrack(int trackNumber)
        {
            MidiFile.Events.RemoveTrack(trackNumber);
            IndexTrackNames();
        }

        public int FindFirstTrackWithEventType(Type eventType)
        {
            for (var i = 0; i < MidiFile.Tracks; i++)
            {
                var trackEvents = MidiFile.Events.GetTrackEvents(i);
                if (trackEvents.Any(trackEvent => trackEvent.GetType() == eventType))
                    return i;
            }

            throw new InvalidOperationException("Can't find track with event type: " + eventType.Name);
        }

        private void IndexTrackNames()
        {
            _tracksNumbersByName.Clear();
            for (var i = 0; i < MidiFile.Tracks; i++)
            {
                var trackEvents = MidiFile.Events.GetTrackEvents(i);
                var textEvents = trackEvents.OfType<TextEvent>();
                var trackNameEvent = textEvents.FirstOrDefault(e => e.MetaEventType == MetaEventType.SequenceTrackName);
                var trackName = trackNameEvent?.Text;
                if (string.IsNullOrEmpty(trackName))
                    trackName = $"Track {i}: [Unnamed]";

                _tracksNumbersByName.Add(trackName, i);
            }
        }

        public IEnumerable<string> TrackNames => _tracksNumbersByName.Keys;

        public IEnumerable<MidiEvent> TrackEvents(string trackName)
        {
            int trackNumber;
            return _tracksNumbersByName.TryGetValue(trackName, out trackNumber) 
                ? MidiFile.Events.GetTrackEvents(trackNumber) 
                : null;
        }

        public void AddEvent(TrackName trackName, MidiEvent newEvent)
        {
            AddEvents(trackName, new[] {newEvent});
        }

        public void AddEvents(TrackName trackName, IEnumerable<MidiEvent> newEvents)
        {
            int trackNumber;
            if (!_tracksNumbersByName.TryGetValue(trackName.ToString(), out trackNumber))
                throw new ArgumentException($"No track named: {trackName}", nameof(trackName));

            foreach(var newEvent in newEvents)
                MidiFile.Events.AddEvent(newEvent, trackNumber);
        }

        public void RemoveTrack(string trackName)
        {
            int trackNumber;
            if (!_tracksNumbersByName.TryGetValue(trackName, out trackNumber))
                throw new ArgumentException($"No track named: {trackName}", nameof(trackName));

            MidiFile.Events.RemoveTrack(trackNumber);
            IndexTrackNames();
        }

        public void RemoveNote(TrackName trackName, NoteOnEvent noteEvent)
        {
            int trackNumber;
            if (!_tracksNumbersByName.TryGetValue(trackName.ToString(), out trackNumber))
                throw new ArgumentException($"No track named: {trackName}", nameof(trackName));

            MidiFile.Events[trackNumber].Remove(noteEvent);
            MidiFile.Events[trackNumber].Remove(noteEvent.OffEvent);
        }

        public void AddTrack(TrackName trackName, List<MidiEvent> midiEvents = null)
        {
            var trackNameEvent = new TextEvent(trackName.ToString(), MetaEventType.SequenceTrackName, 0);
            midiEvents = midiEvents ?? new List<MidiEvent>();
            midiEvents.Insert(0, trackNameEvent);
            var lastEvent = midiEvents.OrderBy(e => e.AbsoluteTime).Last();
            midiEvents.Add(new MetaEvent(MetaEventType.EndTrack, 0, lastEvent.AbsoluteTime));

            MidiFile.Events.AddTrack(midiEvents);
        }
    }
}
