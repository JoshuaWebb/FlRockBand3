using System;

namespace FlRockBand3.Exceptions
{
    public class TrackNotFoundException : Exception
    {
        public string TrackName { get; }

        public TrackNotFoundException(string trackName)
            : base($"A track named '{trackName}' is required, but cannot be found.")
        {
            TrackName = trackName;
        }
    }
}
