# LapDock Icon Pack — original icons, made for LapDock

33 UI icons + 1 full-colour app icon, drawn from scratch in code (SVG paths
written for this pack on 2026-10-13). No icon fonts, no stock sets, no
third-party artwork — so there are no copyright or licence problems using
them in LapDock, commercially or otherwise. © 2026 Ubaid Ullah / Djangix;
you own them with the software.

## Style
- 48×48 grid, 3 px rounded strokes, single colour per icon (recolourable)
- Matches LapDock's theme: dark navy screens, mint `#3DDC97`, green
  `#34D399`, orange `#FF9F43`, sky `#7DD3FC`, dark-red music panel `#4A1420`

## What's inside
- `app-icon/` — the LapDock app icon: `LapDock.ico` (Windows icon with
  16/20/24/32/40/48/64/128/256 px inside — point the csproj
  `<ApplicationIcon>` at it), `lapdock-1024.png`, `lapdock-512.png`,
  `lapdock-256.png`, and the `lapdock-app-icon.svg` source.
- `svg/<light|dark|mint>/` — all 33 icons as SVG sources.
  light `#EAF1FA` (for dark UI), dark `#182238` (for light UI),
  mint `#3DDC97` (accent state).
- `png/<variant>/<size>/` — ready-to-use PNGs at 16/24/32/48/64/128/256 px.
- `preview.png` — contact sheet of the whole set.

## The icons
Tabs: controls, today, stats, clipboard, apps, wallpaper, power.
Features: nova, music, play, pause, next, prev, weather, sun, moon, timer,
todo, settings, lock, wifi, bluetooth, volume, plane, search, star, pin,
trash, copy, qr, battery, chip, restart.

## Using them in WPF
Simplest: use the PNGs as `Image Source` (the 32 or 48 px size fits tab
buttons; 256 for big tiles). Or convert any SVG path into a WPF `Geometry`
— the paths use a 48×48 viewBox, stroke-based, so set `Stroke` to your
brush, `StrokeThickness` 3, `StrokeStartLineCap/EndLineCap` Round, and
`Fill` transparent (a few icons have small filled dots — those shapes are
separate elements in the SVG).

Regenerate or tweak everything with `build_icons.py` (kept next to this
pack in the workspace): edit a path, re-run, all sizes/variants rebuild.
