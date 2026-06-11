# Patch notes — v0.5.3 display topology guard

## Problem

When Windows display topology changes, for example because a monitor turns off, a game changes resolution, or display scaling changes, Explorer can temporarily rearrange Desktop icons. If DeskRealm saves during that phase, the wrong positions can become the saved layout.

## Fix

DeskRealm now separates icon layout variants by display topology and blocks icon saves while a topology change is still settling. It restores the active realm after the topology stabilizes.

## Data model

Each virtual desktop layout can now contain multiple topology variants. Each variant stores:

- topology key;
- topology family key;
- active monitor list;
- bounds / working area;
- effective DPI and scale percentage;
- icon positions with screen-relative coordinates and ratios.

## Compatibility

Legacy layouts remain readable. New saves write v3 layout files.
