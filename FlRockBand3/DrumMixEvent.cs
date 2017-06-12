using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NAudio.Midi;

namespace FlRockBand3
{
    public class DrumMixEvent
    {
        public int Difficulty { get; }
        public string Configuration { get; }
        public TextEvent MidiEvent { get; }
        public long AbsoluteTime => MidiEvent.AbsoluteTime;

        private DrumMixEvent(int difficulty, string configuration, TextEvent textEvent)
        {
            Difficulty = difficulty;
            Configuration = configuration;
            MidiEvent = textEvent;
        }

        private static readonly HashSet<string> ValidConfigurations = new HashSet<string>
        {
            "drums0", "drums0d", "drums0dnoflip",
            "drums1", "drums1d", "drums1dnoflip",
            "drums2", "drums2d", "drums2dnoflip",
            "drums3", "drums3d", "drums3dnoflip",
            "drums4", "drums4d", "drums4dnoflip",
        };

        public static TextEvent DefaultFor(int difficulty)
        {
            if (difficulty < 0 || difficulty > 3)
                throw new ArgumentOutOfRangeException(nameof(difficulty), @"Difficulty must be between 0 and 3 inclusive.");

            var textEvent = new TextEvent($"[mix {difficulty} drums0]", MetaEventType.TextEvent, 0);

            DrumMixEvent validated;
            Debug.Assert(TryParse(textEvent, out validated));

            return textEvent;
        }

        public static bool TryParse(TextEvent textEvent, out DrumMixEvent drumMixEvent)
        {
            drumMixEvent = null;
            if (textEvent.MetaEventType != MetaEventType.TextEvent)
                return false;

            var match = Regex.Match(textEvent.Text, @"^\[mix (?<difficulty>[0-3]) (?<configuration>.*)\]$");
            if (!match.Success)
                return false;

            var configuration = match.Groups["configuration"].Value;
            if (!ValidConfigurations.Contains(configuration))
                return false;

            var difficulty = int.Parse(match.Groups["difficulty"].Value);

            drumMixEvent = new DrumMixEvent(difficulty, configuration, textEvent);
            return true;
        }

        /* TODO: Text Events for Drum Animations
         * [idle_realtime] = char is idling in real time, not synced to the beat.
         *                   Use this for the intro & ends of songs or anywhere else
         *                   you don't want a non-playing character bopping to the beat.
         * [idle] = char is idling normally (not playing)
         * [idle_intense] = char is idling in an intense manner.
         * [play] = char is playing
         * [mellow] = char is playing in a mellow manner, might hit softer.
         * [intense] = char is playing in an intense manner.
         * [ride_side_true] = drummer uses Max Weinberg's side swipe when hitting the ride slowly
         * [ride_side_false] = drummer hits the ride normally always.
         * */
    }
}
