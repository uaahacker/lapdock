# LapDock — Progress & Suggestions

## Done so far

### v0.1 ✅

- Fullscreen borderless dock window, pick which display it lives on
- Nova companion: cursor-tracking eyes, blink, head tilt, sleepy/happy/dancing moods
- Music: media keys + volume, animated equalizer while playing
- Pomodoro + countdown + stopwatch + big clock
- CPU / RAM / battery live stats
- Weather via Open-Meteo (no API key)
- App dock with hover magnification, quick actions, scratch notes
- Settings persisted to %AppData%/LapDock/settings.json

### v0.2 — Real music + redesign ✅

- **LapDock becomes a notch**: a black pill at the top-centre of the display
  that opens into the dock on hover (dashboard widgets parked for now)
- SMTC integration: song title, artist, album art, real play/pause state, seek bar
- Black minimalist redesign (iOS dark palette, Fluent icons, no emoji)
- iPhone-style dock: real shell icons, magnification, launch bounce,
  drag-to-pin, right-click to remove
- First run opens on the non-main display (the laptop panel); re-placed
  automatically when a monitor is plugged/unplugged
- Fixes: timers no longer drift, countdown follows its input, hours display,
  notes save debounced, weather refresh + invariant-culture URL

### v0.3 — System utility ✅

- Battery: %, charging, plugged in, low (≤20%), critical (≤10%), full; hidden without a battery
- Microphone / camera in-use indicators with the app's name; privacy panel
- Now Playing in the notch (collapsed title + pulse, expanded controls + seek)
- Volume slider + mute; changes made elsewhere (keyboard keys) shown as a live message
- Screenshot: rectangle, window, full screen, open Snipping Tool; "Screenshot saved"
- Wallpaper: preview, change (file picker), next, recent, fit style
- Live notch messages for system events, de-duplicated
- Hover: stays open while a menu, slider drag, file picker or drag-and-drop is active
- Drag-and-drop: drop slot + highlight, only apps/shortcuts, real names, no duplicates
- Every widget can be switched off in settings.json; a failing widget never takes the notch down

### How v0.3 was checked (on a real Windows 11 laptop + external monitor, 125% scaling)

Tested on the running app:
- Transparent area around the notch is click-through (clicks reach the app below)
- Battery reading and panel; volume message from the keyboard volume keys
- Rectangle snip opens Snipping Tool's overlay in rectangle mode
- Full screen hides the notch, Windows saves the screenshot, "Screenshot saved" appears
- All panels; media card and collapsed media title with Spotify and Netflix
- Wallpaper change / next / recent through real use
- Idle CPU ≈ 0% (event-driven, nothing polls), ~68 MB private memory

Still to be confirmed by hand:
- Mic / camera indicators (start a call, or open the Camera app)
- Battery messages on plug / unplug
- Dragging a shortcut onto the notch

### Known limits

- Snipping Tool's modern capture protocol only works for packaged (MSIX) apps, so
  rectangle/window use the older `ms-screenclip:` URI (deprecated by Microsoft, still
  working). Full screen uses Win+PrintScreen and captures every display.
- Low/critical battery levels are fixed at 20% / 10% rather than read from the power plan.
- The notch is always on top, so it shows over fullscreen games and video.
- Widget switches are only in settings.json for now (no settings panel yet).
- "Move to next display" cycles through displays rather than listing them.

## Suggestions

Ideas for where LapDock could go next — not commitments, pick what is useful.

### System widgets

- Brightness, shown only on displays that support software control
- Network: Wi-Fi name, signal, connected / no internet; open network settings
- Bluetooth: on/off state and connected devices; open Bluetooth settings
- Audio output picker (or Windows' own output selector where switching isn't reliable)
- Power actions: lock, sleep; restart / shut down only after a confirmation step
- Shortcuts: Settings, Display, Sound, Bluetooth, Network, Personalization,
  Task Manager, File Explorer, Terminal
- Clipboard history button (opens Windows' own history, stores nothing)
- Do Not Disturb / notifications state, if Windows exposes it reliably

### Dock & apps

- Better right-click menu: open file location, run as administrator — only where valid
- Running indicator under pinned apps; list an app's windows to switch between them
- Optional recent apps next to the pinned ones
- *Move to display* submenu listing every display

### Customization

- Settings panel: General, Appearance, Dock, Widgets, Media, Privacy, Displays, Behavior
- Start with Windows
- Fullscreen behaviour: always show / auto-hide / hide during games and video
- Position (left / centre / right), size (small / medium / large)
- Background (solid / acrylic / opacity), light / dark / follow Windows, accent colour

### Nova & extra widgets

- Bring back Nova, timers, weather and notes as optional notch sections
- More Nova moods (focused during Pomodoro, celebrating on timer finish)
- Calendar/agenda (local ICS), GitHub notifications, network speed

### Distribution

- MSIX packaging (would also unlock Snipping Tool's modern capture protocol)
- Microsoft Store listing
