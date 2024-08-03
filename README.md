# HybridHelper
A small tool to randomize Golden Grinds.

Enable the tool in the settings and collect a golden and be randomly bounced between Checkpoints untill you have beaten all of them!

## How to use!
- [Download](https://gamebanana.com/mods/532610) and enable the mod in the mod settings.
- Enter a map.
- Pick up a golden.
- Enjoy

## Building
simply build with `dotnet build`, to pack it into a zip for export build with `/p:Configuration=Release`

## Customization and skipping a chapter
After entering a map, open the mod menu again, a new option should appear.

In here you can set each chapter to one of 3 states.

- Regular: be bounced around normally around the different chapters
- Ignore: when entering this chapter you will not be teleported and equally you wont be teleported into it
- Skip: when entering this chapter you will be teleported but you wont be teleported into it

## Limitations (Potential TODOs)
- The run always ends on the final checkpoint.
- States are somewhat remembered but not to a large extend, make sure to play with commands to give yourself keys/jellyfish for chapters where you need them
- There can be visual glitches in Checkpoint rooms, e.g. in chapter 2 if you spawn in the second checkpoint immediately the dream blocks will not be animated but will be functional, this fixes itself in the next room
