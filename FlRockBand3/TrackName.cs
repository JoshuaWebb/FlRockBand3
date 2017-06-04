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