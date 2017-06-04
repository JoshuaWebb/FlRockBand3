namespace FlRockBand3
{
    public static class NoteHelper
    {
        public static int ToNumber(DifficultyOctave octave, Pitch pitch)
        {
            return (int) octave * 12 + (int) pitch;
        }
    }

    public enum Pitch
    {
        C = 0,
        CSharp = 1,
        D = 2,
        DSharp = 3,
        E = 4
    }

    public enum DifficultyOctave
    {
        Easy = 5,
        Medium = 6,
        Hard = 7
    }
}