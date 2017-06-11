namespace FlRockBand3
{
    public sealed class TrackName
    {
        private readonly string _name;

        public static readonly TrackName Drums = new TrackName("PART DRUMS");
        public static readonly TrackName Venue = new TrackName("VENUE");
        public static readonly TrackName Beat = new TrackName("BEAT");
        public static readonly TrackName Events = new TrackName("EVENTS");

        /// <summary>
        /// The name of the note track containing the encoded time signature data
        /// </summary>
        public static readonly TrackName InputTimeSig = new TrackName("timesig");

        /// <summary>
        /// The processed tempo events track
        /// </summary>
        public static readonly TrackName TempoMap = new TrackName("TEMPO MAP");

        private TrackName(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }
    }
}
