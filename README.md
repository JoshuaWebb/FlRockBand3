# FlRockBand3

A utility for massaging MIDIs from Fruity Loops to Magma for Rock Band 3
custom songs.

While reaper is very cheap, using your existing copy of Fruity Loops is
cheaper; this is intended to allow you to create custom songs from the comfort
of your existing DAW that has limited support for the extended features of the
MIDI format.

This utility currently only supports fixing up the authoring of Drum sections,
however, it should not impact other sections/instruments.

## Time Signatures

Fruity Loops lacks the ability to directly control MIDI `Time Signature`
events, at least as far as exporting them goes. In order to produce
valid `Time Signature` events for the Magma compile you can do the following:

1. Create a Track named `timesig`
2. Anywhere you want to change the time signature of the song, place a pair
   of notes in the position of the desired change.

   Set the velocity of one of the notes to a velocity higher than the other.

   The note with the higher velocity will be treated as the "numerator" and
   the lower note will be the "denominator".

   Set the note number of each note to the desired numeral.

   e.g. From FL Studio

    C1 - velocity at 100%

   G♯0 - velocity at 50%

   Corresponds to a time signature of  12/8

## (Text) Events

Fruity Loops lacks the ability to directly control MIDI `Text` events,
at least as far as exporting them goes. In order to annotate your practice
sections (and control the crowds) Rock Band 3 expects `Text` events on a
track named `EVENTS`.

To produce text events for Magma:

1. Create a track with the name of the event you wish to create

   e.g. `[prc_intro]`

2. Place **one** note on the track where you want the event/section to start.

## Miscelaneous MIDI fixes

- The `Pulses Per Quater-note` (PPQ) is fixed to 480 as Magma expects.
- A `Venue` track is added automatically if it isn't present.
- Some token notes are added for other difficulties that have not been
  authored. _This suppresses a warning from Magma, but is only recommended
  for personal use._
- Duplicate notes are removed.
- Normalizes note velocities.
- Music start and end events are added automatically.
- Removes invalid/unexpected MIDI events from FL Studio.
