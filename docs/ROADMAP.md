# LapDock — Progress

## v1.2 — live activities, app notifications, the shelf ✅

- **Notifications from apps**: Teams, WhatsApp, Discord, Outlook… appear in the notch as a banner with the
  app's own icon, the sender and two lines of the message; click it to open the app ("3 new" when several
  arrive). Read with Windows' notification listener, and only when Windows' notification database changes
  (checked once a second, practically free) — reading the list costs ~30 ms of CPU, so it is never polled.
  Settings switch: *Notifications from apps*.
- **Live activities** in the collapsed pill: a call (Teams, Zoom, Discord, WhatsApp, Meet in a browser…)
  with its duration, the timer or a focus session counting down, a browser download with its size.
  Banners for: download finished (click: show in folder), USB device connected (a drive: click to open it),
  headphones / sound output switched.
- **Music visualizer**: the bars follow the real output level (Core Audio peak meter), five bars drawn
  without layout at ~15 frames a second, only while music plays and the pill is on screen (not over
  fullscreen apps, not while locked).
- **Focus mode** (Today → Focus): 25 minutes of focus, then a 5-minute break that starts by itself; sessions
  counted per day. Notifications wait during focus and are summed up when it ends. Windows' own Do not
  disturb has no public switch (Microsoft keeps it to a limited-access API), so Focus links to it.
- **Shelf** tab: drag files, links or text onto the notch to keep them (files by reference). Drag them back
  out into any app, open, show in folder, copy, share with Windows' share sheet, or show a QR code — links and
  text directly, files through a one-file download link for a phone on the same Wi-Fi (random 128-bit
  token, 404 for anything else, stops when closed or after 10 minutes).
- **Battery**: health (full-charge vs design capacity), power draw / charge rate, and a 24-hour chart (one bar
  per half hour, green while plugged in) from LapDock's own log (`battery-history.csv`, 7 days).
- New icons: shelf, download, headphones, USB, phone, bell, focus, share, folder, link.

### How v1.2 was checked

- Release build 0 errors / 0 warnings; 11 self-checks (adds download names, battery history, and the phone
  download server over loopback: right bytes for the link, 404 otherwise).
- End-to-end on a Windows 11 laptop: a real Windows notification reached the notch in under 2 s and showed
  as a banner; a browser-style download (`.crdownload` growing, then renamed) showed live in the pill and
  as "Downloaded"; a focus session ended → break started, session counted, held notifications summed up;
  shelf add / de-duplicate; the share sheet's plumbing on the notch window; USB watcher enumerated.
- CPU, measured: at rest under 1% of one core; with music playing ~4.7% of one core for the bars
  (≈0.6% of an 8-thread CPU).
- Not exercised with hardware here: plugging a USB device, switching headphones, scanning a file's QR from a
  phone (Windows Firewall asks once whether LapDock may receive connections).

## v1.1.2 — icons ✅

- Bluetooth icon redrawn as the real Bluetooth rune; Stats tab icon enlarged to match the other tabs.

## v1.1.1 — Make room can't run away ✅

- Circuit breaker: if the reserved strip changes more than 5 times in 30 seconds (each change makes
  Windows resize every maximised app), LapDock gives the space back, switches to **Stay on top** and
  says so once. Checked: the strip is claimed once and stays put while running.
- After Explorer restarts (it forgets appbars), the strip is claimed again once.

## v1.1 — the notch no longer covers apps ✅

- New setting **When an app reaches the top of the screen**:
  - **Make room** (default) registers the notch as an appbar, so Windows reserves a 32 px strip and
    maximised apps (Chrome's tab strip, title-bar buttons) start below it. Checked live: work area
    reserved while running, given back on quit, and released by Windows even if LapDock is killed.
  - **Hide when covered** slides the pill away while an app window lies under it (front-to-back
    window walk at the top edge, skipping cloaked and click-through windows); pushing the mouse
    against the top-centre edge for ~0.3 s opens it.
  - **Stay on top**: the old behaviour.
- Fixed: the "quit" signal used by install/uninstall now reaches a running LapDock.

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
- Per-app mute for notification banners; headphone battery level.
- Code signing; a `winget` / Microsoft Store listing.
- A keyboard shortcut for the Apps launcher; launcher ranking by use.
- Per-display wallpapers; Settings for clipboard exclusions and index folders.
