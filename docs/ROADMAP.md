# LapDock — Progress

## v1.0 — the notch, tab for tab ✅ (13 Oct 2026)

Built in phases from [HITODAMA-ROADMAP.md](HITODAMA-ROADMAP.md):

- **Shell**: the notch opens on hover into seven tabs; Ctrl+Alt+D, the tray icon or a
  second launch open **Settings**. Bezel-style shape (curved shoulders), drop-from-the-top
  animation, content clipped to the shape.
- **Controls**: Wi-Fi, Bluetooth, audio, microphone, dark mode, airplane (real toggles),
  night light and Do not disturb (open Settings — no public API), volume, music player (SMTC).
- **Today**: time, week range, weather (city set in Settings), week strip with events
  (ICS import), ring timer, to-dos with `@time` reminders.
- **Stats**: CPU / memory / GPU rings, top apps by memory (Task Manager's measure), live
  network, ping, download speed test.
- **Clipboard**: history of text and images, search, filters, pin, QR, snippets,
  Enter pastes into the previous app, Ctrl+Shift+V.
- **Apps**: launcher over every Start app, files (Documents/Desktop), snippets, favourites.
- **Wallpaper**: picture grid with instant apply, fit options, add folders.
- **Power**: lock, sleep (Modern Standby aware), restart / shut down with hold-to-confirm.
- **Fullscreen apps**: the notch turns into a plain black pill and stays quiet.
- **Look**: black, greys and one green; LapDock's own icon set and app icon.
- **Distribution**: one self-contained `LapDock.exe` that installs itself per user
  (Start menu, Settings → Apps uninstall, start at sign-in), single instance.
- Removed: the pinned-app dock row (Apps tab + favourites replace it).

### How v1.0 was checked

- Release build 0 errors / 0 warnings; 7 self-checks (reminder parsing, ICS reader, top-apps
  ordering, speed maths, clipboard classification, launcher matching, fullscreen rule).
- Every tab, Settings and the setup windows rendered and reviewed.
- Live on a Windows 11 laptop: fullscreen detection on/off; clipboard capture (text, image,
  password-manager copy skipped); hold-to-confirm (short press does nothing).
- End-to-end with the published exe, 23/23: install window, silent install, Start menu,
  Settings → Apps entry, sign-in entry, both shortcuts held, single instance, idle CPU,
  uninstall removes everything and keeps user data, no errors logged.

### Known limits

- The exe isn't code-signed: SmartScreen asks to confirm on first run.
- Night light and Do not disturb can't be toggled (no public Windows API).
- Sleep on Modern Standby laptops turns the display off, the way the power button does.
- Clipboard history skips files copied in Explorer and text over 1 million characters.
- Repeating calendar events import once; ICS time zones are read as local time.
- File search covers names only, in the chosen folders (default Documents + Desktop).
- One wallpaper for all displays.

## Earlier

- **v0.3** — the notch as a system utility: battery, mic/camera indicators, now playing,
  volume, screenshots, wallpaper, live messages.
- **v0.2** — the dock became a notch; SMTC music; real app icons.
- **v0.1** — fullscreen dock window with Nova, timers, stats, weather, notes.

## Next

- **Voice** (roadmap Phase 9, waiting for the go-ahead): hold Ctrl+Win to dictate with
  AI clean-up, voice commands, routines. Needs a Gemini API key.
- Code signing; a `winget` / Microsoft Store listing.
- A keyboard shortcut for the Apps launcher; launcher ranking by use.
- Per-display wallpapers; Settings for clipboard exclusions and index folders.
