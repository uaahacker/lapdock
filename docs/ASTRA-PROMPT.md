# Prompt for Astra (Codex) — finish LapDock in one go

Copy everything inside the code block below and paste it as one message to Astra
in the opened LapDock folder.

```text
You are Astra, an expert C# / WPF desktop engineer. You are working in the LapDock repository. Your mission: take this app from its working v0.1 starter to a finished, polished v1.0 — in ONE continuous run. Do not stop to ask me questions. Where a choice is needed, pick the sensible default I would pick, note it in your final report, and keep going.

════════════════════════════════════════════════════════════
WHAT LAPDOCK IS
════════════════════════════════════════════════════════════
LapDock is a Windows app that turns a laptop's built-in screen into a living "dock" when an external monitor is plugged in as the main display. The external screen is for work; the laptop screen runs LapDock fullscreen: an animated companion head ("Nova") that watches the user's cursor, music controls, focus timers, system stats, weather, notes, quick actions, and a macOS-style app dock.

Stack (DO NOT change it): C# 12, WPF, .NET 8, single project src/LapDock, MVVM-lite, Win32 P/Invoke where WPF has no API. Read README.md, docs/ARCHITECTURE.md and docs/ROADMAP.md first — they describe the design, the file map, and why things are built the way they are.

ALREADY WORKING IN v0.1 (keep all of it, build on it):
- Borderless dock window + top-bar display picker; ScreenService.MoveToScreen uses SetWindowPos in PHYSICAL pixels on purpose (laptop panels are usually 125%/150% scaled — WPF DIP coordinates land wrong). Do not "simplify" this back to Left/Top.
- Nova companion (Controls/CompanionHead): global cursor tracking via CursorService (Win32 GetCursorPos polling — sees the cursor on ALL monitors), smoothed pupils + head tilt/lean, random blinks, moods Awake/Sleepy/Happy/Focused/Dancing, speech bubble.
- Music via Windows media keys (MediaService), Pomodoro/countdown/stopwatch/big clock (ViewModels/MainViewModel), CPU via GetSystemTimes + RAM + battery (SystemStatsService), weather via Open-Meteo with no API key (WeatherService), auto-saving notes, quick actions, magnifying bottom dock, settings in %AppData%/LapDock/settings.json (SettingsService).

KNOWN v0.1 LIMITS YOU MUST FIX: music play/pause state is a guessed toggle boolean (not real), equalizer bars animate randomly instead of reflecting real playback, weather never refreshes, no settings UI (JSON only), no tray/autostart, ComboBox is unstyled on the dark theme.

════════════════════════════════════════════════════════════
HARD RULES
════════════════════════════════════════════════════════════
1. Keep the stack and project structure. Solutions-wide rename/rewrite is forbidden.
2. In LapDock.csproj, the <Using Remove="System.Windows.Forms"/> and <Using Remove="System.Drawing"/> items and the WFAC010 NoWarn are LOAD-BEARING (they fix real WPF/WinForms name collisions). Do not remove them.
3. settings.json must stay backward compatible: loading a v0.1 settings file must not crash or lose data — add new fields with defaults.
4. No paid services, no API keys, no accounts. Everything local/offline except Open-Meteo weather and an ICS URL the user pastes themselves.
5. Every button/ toggle you ship must actually work. No placeholder UI, no TODO stubs, no dead menu items.
6. After EVERY phase below: `dotnet build -c Release` must pass with 0 errors and 0 warnings. Fix warnings, don't suppress them silently (existing WFAC010 suppression excepted).
7. Work phase by phase in the order given. Commit after each phase with a clear message (e.g. "Phase A: real Now Playing via SMTC").

════════════════════════════════════════════════════════════
PHASE A — REAL MUSIC (SMTC)
════════════════════════════════════════════════════════════
- Change the target framework to net8.0-windows10.0.19041.0 so the built-in WinRT projections are available (no CsWinRT package needed).
- Implement NowPlayingService using Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager: current session, track title, artist, album, playback state (Playing/Paused), position/duration, and album-art thumbnail converted to a WPF BitmapImage.
- Subscribe to SessionsChanged / MediaPropertiesChanged / PlaybackInfoChanged / TimelinePropertiesChanged; update the music card UI: artwork, title, artist, real play/pause icon state, seek bar (click/drag to seek via TryChangePlaybackPositionAsync).
- Transport buttons must control the SMTC session (TryPlayAsync/TryPauseAsync/TrySkipNextAsync/TrySkipPreviousAsync); fall back to the existing media-key MediaService when no session exists. Volume controls stay as they are.
- Nova dances ONLY when SMTC reports Playing. Equalizer bars animate only while actually playing, and freeze flat when paused.
- Handle gracefully: no player open, player closes mid-run, session switches from Spotify to browser. Nothing may crash the dock.

════════════════════════════════════════════════════════════
PHASE B — PLACEMENT, TRAY, AUTOSTART
════════════════════════════════════════════════════════════
- Listen for display changes (WM_DISPLAYCHANGE via HwndSource hook): re-enumerate screens; if LapDock's saved screen disappears, move to the primary screen and update the picker; if a second screen appears and LapDock was on the laptop panel before, offer via speech bubble (not a modal) to move back.
- Auto-suggest the laptop panel on first run: default the picker to the non-primary display if one exists.
- System tray icon (WinForms NotifyIcon is already referenced): menu = Show/Hide, Move to ▸ (each screen), separator, Exit. Double-click tray = show. Add a "Minimise to tray instead of taskbar" setting (default off).
- "Start with Windows" toggle using the HKCU\...\Run registry key pointing at the current exe path. Default off.
- Single-instance: a named Mutex; a second launch brings the existing window forward instead of opening a duplicate.

════════════════════════════════════════════════════════════
PHASE C — NOVA, MORE ALIVE
════════════════════════════════════════════════════════════
- New moods wired to real events: Celebrating (any timer finishes — bounce + confetti-simple star burst using shapes, 2s), Dizzy (cursor moves very fast for >1.5s — spiral/swirl eyes briefly), Curious idle behaviour: every 15–30s of no interaction Nova glances around on its own and occasionally shows a random tip/fact in the speech bubble.
- Antenna tip glows/pulses while music plays; cheeks brighten in Happy/Celebrating.
- Speech bubble lines: expand the pool, group by mood, never repeat the same line twice in a row.
- Add a user setting "Reduce motion" that calms all Nova animation (no dance bounce, slower pupil lerp, no bursts) for accessibility.
- Keep Nova 100% vector-drawn XAML shapes — no image assets, no emoji as the face.

════════════════════════════════════════════════════════════
PHASE D — TIMERS, PROPER
════════════════════════════════════════════════════════════
- Pomodoro durations configurable in settings (focus minutes, break minutes, long break after 4 pomodoros — 15 min default, with correct phase rotation and UI labels).
- On any timer finish: sound (system sound is fine), Nova Celebrating, speech bubble message. Optional setting: keep the LapDock screen awake while a Pomodoro runs (SetThreadExecutionState), default off.
- Countdown accepts hh:mm:ss as well as plain minutes; invalid input shakes/flags the box instead of silently doing nothing.
- Persist nothing about running timers across restarts, but DO restore the configured durations.

════════════════════════════════════════════════════════════
PHASE E — NEW WIDGETS (each one a card, each hideable in settings)
════════════════════════════════════════════════════════════
1. Network speed: live up/down via NetworkInterface byte counters (1s delta), small sparkline optional — numbers required, sparkline only if simple.
2. To-do list: add/check/delete items, persisted in settings.json, unfinished count badge on the card title.
3. Calendar: user pastes an ICS file path or URL in settings; parse VEVENTs (handle all-day + timed, today + next 3 events shown). Refresh hourly and on demand. If unset, card shows a one-line "Add an ICS link in Settings" hint — not an error.
4. Clipboard peek: shows the current clipboard text (truncated, monospace), click to re-copy, auto-updates on clipboard change (AddClipboardFormatListener). Never store clipboard history to disk.

Widget layout: keep the current 2×2 grid as the default arrangement; hidden widgets collapse cleanly with no empty holes (rebalance the grid).

════════════════════════════════════════════════════════════
PHASE F — THEMES + SETTINGS WINDOW
════════════════════════════════════════════════════════════
- Themes as merged ResourceDictionaries: "Midnight" (current palette), "Deep Purple", "Light". Switching is instant, no restart, persisted. Nova's accent colours follow the theme. Fix the dark ComboBox styling as part of this (all controls must be legible in every theme).
- Real Settings window (gear in top bar + right-click Nova): sections General (screen picker mirror, autostart, tray behaviour, reduce motion, keep-awake), Timers (the Phase D durations), Weather (city name + lat/lon fields with a "find my city" lookup via the free Open-Meteo geocoding API), Dock (edit pinned apps: add via file picker, remove, move up/down — writes the existing PinnedApps format), Widgets (show/hide toggles), About (version, repo link).
- Dock pins must also be removable via right-click on a dock icon (context menu: Remove, Open file location where sensible). The "+" add button at the end of the dock opens the file picker.

════════════════════════════════════════════════════════════
PHASE G — FINISHING
════════════════════════════════════════════════════════════
- Global hotkey Ctrl+Alt+L toggles LapDock visibility (RegisterHotKey), so it can be summoned/hidden without touching the mouse.
- Weather auto-refresh every 30 minutes + refresh button on the card.
- Graceful single-monitor behaviour: everything still works windowed; Nova watches the cursor on the same screen.
- Add an xUnit test project LapDock.Tests covering: MainViewModel timer state machines (pomodoro rotation incl. long break, countdown finish, stopwatch), SettingsService save/load round-trip incl. loading a v0.1-format JSON sample, ICS parser with a sample feed. Extract pure logic where needed, without breaking the UI. `dotnet test` must pass.
- Bump Version to 1.0.0. Write CHANGELOG.md (v0.1 → v1.0, by phase). Rewrite README.md to match reality: final feature list, install/run instructions incl. `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` producing one LapDock.exe, a "How it works" short section, and a screenshots placeholder section.
- Final gate: clean clone-style check — `dotnet build -c Release` 0 errors / 0 warnings AND `dotnet test` all green, from a fresh obj/bin (delete both and rebuild to prove it).

════════════════════════════════════════════════════════════
FINAL REPORT (end your run with exactly this)
════════════════════════════════════════════════════════════
1. Phase-by-phase summary: what was built, main files touched.
2. Every default you chose where this prompt left a choice.
3. Verification evidence: exact build + test results (error/warning/test counts).
4. Anything you deliberately left out and why (must be things outside this prompt — everything in Phases A–G is mandatory).
5. A 5-line "try it" script I can follow to see each new feature working.
```
