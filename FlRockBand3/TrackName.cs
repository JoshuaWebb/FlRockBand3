namespace FlRockBand3
{
    public sealed class TrackName
    {
        private readonly string _name;

        public static readonly TrackName Drums = new TrackName("PART DRUMS");
        public static readonly TrackName Venue = new TrackName("VENUE");
        public static readonly TrackName Beat = new TrackName("BEAT");
        public static readonly TrackName Events = new TrackName("EVENTS");
        public static readonly TrackName Time = new TrackName("__TIME");

        public static readonly TrackName Name = new TrackName("__NAME");
        /// <summary>
        /// The name of the note track containing the encoded time signature data
        /// </summary>
        public static readonly TrackName InputTimeSig = new TrackName("timesig");
        public static readonly TrackName MusicEnd = new TrackName("[music_end]");

        private TrackName(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }

        public static bool IsNameTrack(TrackName trackName)
        {
            return trackName == Name;
        }
    }
}