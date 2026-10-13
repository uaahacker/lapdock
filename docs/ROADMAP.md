# LapDock — Roadmap

## v0.1 (this starter) ✅

- Fullscreen borderless dock window, pick which display it lives on
- Nova companion: cursor-tracking eyes, blink, head tilt, sleepy/happy/dancing moods
- Music: media keys + volume, animated equalizer while playing
- Pomodoro + countdown + stopwatch + big clock
- CPU / RAM / battery live stats
- Weather via Open-Meteo (no API key)
- App dock with hover magnification, quick actions, scratch notes
- Settings persisted to %AppData%/LapDock/settings.json

## v0.2 — Real music

- SMTC integration: song title, artist, album art, real play/pause state,
  seek bar (`Windows.Media.Control` via CsWinRT)
- Nova dances only when SMTC says music is actually playing

## v0.3 — Smarter placement

- Auto-detect "laptop panel vs external" and offer one-click move
- Start with Windows (optional), tray icon, wake when second screen attaches
  (`WM_DISPLAYCHANGE`)

## v0.4 — More life for Nova

- More faces/moods (focused during Pomodoro, celebrating on timer finish)
- Speech bubble tips, occasional idle animations (look around, yawn)
- Themes/skins for Nova + dock (dark, light, neon)

## v0.5 — Widgets

- Calendar/agenda (Outlook/Google via local ICS), GitHub notifications,
  to-do list sync, clipboard history peek, network speed
- Drag-to-rearrange widget grid, per-widget settings

## Later

- Plugin/widget SDK (drop a DLL in a folder, it appears as a card)
- Keyboard shortcuts overlay (global hotkey to summon mini-dock on main screen)
- MSIX packaging + Microsoft Store
