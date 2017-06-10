namespace FlRockBand3
{
    public sealed class EventName
    {
        public static readonly EventName CrowdRealtime = new EventName("[crowd_realtime]");
        public static readonly EventName CrowdIntense = new EventName("[crowd_intense]");
        public static readonly EventName CrowdNormal = new EventName("[crowd_normal]");
        public static readonly EventName CrowdMellow = new EventName("[crowd_mellow]");

        public static readonly EventName CrowdClap = new EventName("[crowd_clap]");
        public static readonly EventName CrowdNoClap = new EventName("[crowd_noclap]");

        public static readonly EventName MusicStart = new EventName("[music_start]");
        public static readonly EventName MusicEnd = new EventName("[music_end]");
        public static readonly EventName End = new EventName("[end]");
        public static readonly EventName Coda = new EventName("[coda]");

        private readonly string _name;

        private EventName(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }

        public static string[] SpecialEventNames { get; } = {
            CrowdRealtime.ToString(),
            CrowdIntense.ToString(),
            CrowdNormal.ToString(),
            CrowdMellow.ToString(),

            CrowdClap.ToString(),
            CrowdNoClap.ToString(),

            MusicStart.ToString(),
            MusicEnd.ToString(),
            End.ToString(),
            Coda.ToString()
        };
    }
}
