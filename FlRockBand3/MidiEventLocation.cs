namespace FlRockBand3
{
    public struct MidiEventLocation
    {
        public long AbsoluteTime { get; }

        public int FourFourBar { get; }
        public int FourFourBeat { get; }
        public int FourFourTicks { get; }

        public MidiEventLocation(long absoluteTime,
            int fourFourBar, int fourFourBeat, int fourFourTicks)
        {
            AbsoluteTime = absoluteTime;
            FourFourBar = fourFourBar;
            FourFourBeat = fourFourBeat;
            FourFourTicks = fourFourTicks;
        }

        public override string ToString()
        {
            return $"[{FourFourBar}:{FourFourBeat} in 4/4 ({AbsoluteTime} ticks)]";
        }
    }
}
