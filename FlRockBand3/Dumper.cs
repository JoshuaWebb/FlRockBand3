using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Midi;

namespace FlRockBand3
{
    public class Dumper
    {
        public void Dump(string midiPath, Stream outStream)
        {
            var midi = new MidiFile(midiPath);

            using (var sw = new StreamWriter(outStream))
            {
                for (var i = 0; i < midi.Tracks; i++)
                {
                    var trackEvents = midi.Events.GetTrackEvents(i);
                    var textEvents = trackEvents.OfType<TextEvent>();
                    var trackNameEvent = textEvents.FirstOrDefault(e => e.MetaEventType == MetaEventType.SequenceTrackName);
                    var trackName = trackNameEvent?.Text;
                    if (string.IsNullOrEmpty(trackName))
                        trackName = $"Unnamed Track: {i}";

                    Console.WriteLine(trackName);

                    foreach (var trackEvent in trackEvents.OrderBy(e => e.AbsoluteTime))
                    {
                        var str = trackEvent.ToString();
                        var noteEvent = trackEvent as NoteEvent;
                        if (noteEvent != null)
                            str += $" (NoteNumber: {noteEvent.NoteNumber})";

                        sw.WriteLine(str);
                    }
                }
            }
        }
    }
}
