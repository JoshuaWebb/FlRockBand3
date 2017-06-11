using System;

namespace FlRockBand3.Exceptions
{
    public class InvalidBeatTrackException : Exception
    {
        public InvalidBeatTrackException(string message) : base(message)
        {
        }
    }
}
