namespace FlRockBand3
{
    public struct Range<T>
    {
        public string Name { get; }
        public T Start { get; }
        public T End { get; }

        public Range(string name, T start, T end)
        {
            Name = name;
            Start = start;
            End = end;
        }
    }
}
