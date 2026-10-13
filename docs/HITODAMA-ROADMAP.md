# LapDock → "Hitodama-style" Roadmap

**Goal:** rebuild LapDock's shell and feature set to match, tab for tab, the app
shown in the Instagram reel Ubaid picked (Hitodama, hitodama.in, reel by
@otism_dev, posted 12 Oct 2026) — clean-room, in our own C#/WPF code.
Working name for the target UX: **Island mode**.

This document is written to be executed by Muse, Astra (Codex), or Claude —
one phase at a time, in order. Every phase ends at a **gate**: the Release
build must pass with 0 errors / 0 warnings AND Ubaid smoke-tests on Windows
before the next phase starts. (The Astra run of Oct 2026 stalled because its
environment never ran the build — do not repeat that: build first, then code
the next phase.)

## Status — v1.0 (13 Oct 2026)

| Phase | State |
|---|---|
| 0 Shell + theme | ✅ — tabs live inside the notch (see design change in §4); Ctrl+Alt+D opens Settings |
| 1 Controls | ✅ — plus a Microphone tile; night light / DND open Settings |
| 2 Music (SMTC) | ✅ — shared `MusicCard` |
| 3 Today | ✅ — weather city settable in Settings |
| 4 Stats | ✅ |
| 5 Clipboard | ✅ — Enter pastes into the previous app; snippets |
| 6 Apps | ✅ — Windows Apps folder instead of .lnk parsing (includes Store apps) |
| 7 Wallpaper | ✅ |
| 8 Power | ✅ — Modern Standby aware sleep |
| 9 Voice + AI | ⏸ parked until Ubaid's go-ahead |
| 10 Polish & packaging | ✅ shape/animation, fullscreen quiet mode, grey+green palette, own icon set, start with Windows, single instance, self-installing `LapDock.exe`, self-checks, end-to-end test (23/23). Not done: xUnit project (self-checks instead), code signing |

Progress details and known limits: [ROADMAP.md](ROADMAP.md). Architecture: [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 1. Evidence base (what we actually know, and from where)

### 1a. The reel (watched in full, 12 s loop)
One floating, dark, rounded-corner panel centred over the desktop, with a top
tab bar. The user clicks through 7 tabs:

| Tab | What's in it (as seen) |
|---|---|
| **Controls** | Toggle tiles: Wi-Fi (network name shown), Bluetooth (On), Audio (headphone device shown), Night Light (Off), Dark Mode (Off), DND Mode, Airplane; a slider below; music player widget — dark red/maroon panel, album art, title "KICK BACK" — Kenshi Yonezu, prev/play/next, progress bar (1:19 / 3:17) |
| **Today** | "Oct 12 – 18" header, large date numerals, "PM • Mon 12", "33°C Clear sky"; "This week" strip (Tu 13 highlighted, event dots); "Nothing coming up — Your calendar is clear"; circular 1-minute timer "01:00", orange progress ring, Reset button; to-do panel ("0/4", "New Reminder…", "+ Add a to-do") |
| **Stats** | Big circular gauges: RAM 70% (11.2 / 15.9 GB), CPU 2%; "Top apps – RAM" list (VS Code 2.5 GB, Opera GX 1.1 GB, Edge 571 MB, Chrome 508 MB); "Internet speed" — "Finding a server…", up/down readouts, ping |
| **Clipboard** | Clipboard manager: "Search 522 items…", filter chips Text / Images / Links, scrollable history list, right-side preview with Pin / Copy / Delete / QR buttons, status bar "Ctrl+Shift+V • 522 items • pinned" |
| **Apps** | Spotlight-style launcher: "Search apps, files and snippets…", app list (Edge, Discord, Opera GX, Chrome, VS Code, Hitodama, ShareX, Epic Games Launcher…), star (favourites) icon |
| **Wallpaper** | "5 pictures" picker, thumbnail grid; clicking a thumbnail changes the desktop wallpaper instantly, with a live preview above |
| **Power** | Four large round buttons: Lock, Sleep, Restart, Shut down |

Theme: near-black/navy panel, rounded corners everywhere; accents — mint-green
toggles, green gauge rings, orange timer ring, dark-red music panel. Smooth
tab transitions, live gauge values, instant wallpaper swap. On-video text:
"This is not linux / This is windows + Hitodama".

### 1b. hitodama.in (fetched 13 Oct 2026)
The panel is one half of the product. The site describes the other half:

- Hitodama is a **voice assistant**: hold `Ctrl+Win` anywhere and talk; on
  release, a cleaned-up sentence is inserted into the current text box
  ("Talk badly. Send it clean."). It also opens apps, reads the screen and
  runs tasks by voice ("Circle it. Ask."), and runs routines you dictate once
  ("Say it once. Then forget it." — it "wakes itself, does the thing, and
  tells you what it did").
- The panel is summoned from an **island**: "Scroll down on the island and
  your music, settings, plans, clipboard, apps and wallpapers are right
  there." The island is customisable ("four slots": Soul, Personality,
  Voice, Abilities).
- AI text cleanup runs on the **user's own Google Gemini API key** (free tier
  by default; billing-enabled key needed in EEA/UK/CH). One-time purchase:
  Founders Edition **$9**, currently open; no subscription.
- It is **closed source** (per the reel's comments).

### 1c. REA audit of our own build (13 Oct 2026)
Fresh Release build of this repo: **0 errors / 0 warnings** (Linux VM,
.NET 8 SDK, `-p:EnableWindowsTargeting=true`). REA managed inspection of the
compiled `LapDock.dll` (Evidence `ev_3b301dbb…`, members `ev_59cd2d63…`):
25 types, 203 methods, 793 call edges — exactly the v0.1 source set
(`App`, `MainWindow`, `MainViewModel`, `CursorService`, `MediaService`,
`ScreenService`, `SettingsService`, `SystemStatsService`, `WeatherService`,
`AppSettings`, `CompanionHead`/`NovaMood` + compiler-generated). **No
NowPlaying/SMTC type exists in this repo** — the earlier Astra Phase A work
lives only on Ubaid's Windows machine and must be treated as non-existent
here. Start every phase from this clean v0.1.

### 1d. What we do NOT know
Hitodama's internals (framework, storage formats, exact behaviours) are
unknown — the binary is paid and was not obtained, so no REA pass on the
original has been run. This roadmap therefore matches **observed behaviour
and UI**, not internal design. If Ubaid buys/owns Hitodama and shares the
installer, run REA on it (`inspect-artifact` → route by format: Electron/ASAR
→ `analyze-javascript-application`; .NET → `inspect-managed-*`; native →
Ghidra via `rea decompile`) and append findings to §6 before Phase 1.

---

## 2. Gap map — reel vs LapDock v0.1

| Reel feature | LapDock v0.1 state | Verdict |
|---|---|---|
| Floating centred panel + tab bar | Full 1340×820 dock window, fixed 2-column layout | **New shell needed (Phase 0)** |
| Quick-settings toggles (Wi-Fi/BT/audio/night light/dark/DND/airplane) + slider | Only shortcut buttons (Lock, settings pages) | **Missing (Phase 1)** |
| Music player w/ artwork, title, progress, seek | Media keys + volume + fake equaliser only | **Upgrade (Phase 2, SMTC)** |
| Big date + week strip + calendar state | Big clock only | **Partial (Phase 3)** |
| Weather line | ✅ Open-Meteo service exists | Reuse in Phase 3 |
| Circular quick timer w/ ring + reset | Pomodoro/countdown/stopwatch (text) | Restyle + presets (Phase 3) |
| To-do list + reminders | Scratch notes only | **Missing (Phase 3)** |
| CPU/RAM circular gauges | CPU/RAM/battery as bars/text | Restyle (Phase 4) |
| Top apps by RAM | — | **Missing (Phase 4)** |
| Internet speed test + ping | — | **Missing (Phase 4)** |
| Clipboard history, search, filters, pin, QR, Ctrl+Shift+V | — (was a later ASTRA-PROMPT item) | **Missing (Phase 5)** |
| App launcher w/ search (+files/snippets) | Pinned magnifying dock, no search | **Missing (Phase 6)** |
| Wallpaper picker w/ instant apply + preview | — | **Missing (Phase 7)** |
| Power menu (Lock/Sleep/Restart/Shutdown) | Lock only (quick action) | **Extend (Phase 8)** |
| Island (compact pill, scroll/click to expand) | — | **New (Phase 0)** |
| Nova companion | ✅ Exists — becomes the island's "soul" face | Reuse in Phase 0 |
| Voice dictation + AI cleanup + voice commands + routines | — | **New, biggest (Phase 9)** |

---

## 3. Hard rules (carry-overs — breaking these broke builds before)

1. Keep the csproj `<Using Remove="System.Windows.Forms"/>` and
   `<Using Remove="System.Drawing"/>` entries and the `WFAC010` suppression.
   WinForms is used only for screen enumeration + battery, aliased
   `using Forms = System.Windows.Forms;`.
2. Keep `app.manifest` PerMonitorV2. All window placement uses
   `SetWindowPos` in **physical pixels** (see `ScreenService`) — scaled
   laptop panels (125%/150%) must line up.
3. TFM change is required for WinRT APIs (SMTC, Radios, clipboard history):
   retarget to `net8.0-windows10.0.22621.0` in Phase 0 and never go back.
   This is the change Astra attempted; it is correct — the failure was the
   missing build, not the idea.
4. NuGet discipline: current count is **zero**. Allowed additions, only in
   their phase: **QRCoder** (Phase 5, offline QR). Everything else must use
   framework/WinRT/P-Invoke. Any other package needs Ubaid's approval noted
   in the phase PR description.
5. Settings stay in `%AppData%/LapDock/settings.json` (extend
   `AppSettings`); new stores get their own files next to it
   (`todos.json`, `clipboard/`, `snippets.json`, `favourites.json`).
6. Every phase: build on the VM with
   `dotnet build -c Release -p:EnableWindowsTargeting=true` → 0/0, run any
   tests, commit with message `Phase N: <name>`, push. Then STOP for
   Ubaid's Windows smoke test.

---

## 4. Phases

> **Design change (Ubaid, 13 Oct 2026):** the tabs live **inside the notch** — hover the
> pill and it opens into status chips, pinned apps and the tab bar (Controls, Today, Stats,
> Clipboard, Apps, Wallpaper, Power), all in `MainWindow.xaml` with pages under
> `Views/Tabs/`. There is no separate floating feature panel: the hotkey (`Ctrl+Alt+D`),
> tray icon and the notch's right-click menu open **Settings** (`Views/SettingsWindow`).
> Later phases build their tab as a page in the notch, sized for its ~536 px content width.

### Phase 0 — Island shell + theme system
**Goal:** LapDock opens as a small island pill at the top-centre of the
chosen screen; clicking it (or pressing the summon hotkey, or scrolling down
on it) expands the floating panel with the 7-tab bar. The old full dock
remains available as "Dock mode" in settings.

Build:
- Retarget csproj → `net8.0-windows10.0.22621.0` (Hard rule 3). Build gate
  immediately, before any UI work.
- New `Views/IslandWindow.xaml` (compact pill: Nova face mini, clock,
  now-playing ticker slot) and `Views/PanelWindow.xaml` (the tabbed panel,
  ~980×640, `CornerRadius` 24, near-black `#0B0E14` background to match
  current theme).
- `Controls/TabBar` with tabs: Controls, Today, Stats, Clipboard, Apps,
  Wallpaper, Power — content pages as `UserControl`s under `Views/Tabs/`,
  empty shells in this phase except Today (move existing clock in).
- Theme tokens in `App.xaml`: `MintBrush #3DDC97` (toggles on),
  `GaugeGreenBrush #34D399`, `TimerOrangeBrush #FF9F43`,
  `MusicPanelBrush #4A1420` (dark red), tile brush `#171E2E`, radius 16/24.
  These are sampled from the reel's look — our own palette, same family.
- Nova (existing `CompanionHead`) sits in the island; mood logic unchanged.
- Global hotkey service `Services/HotkeyService.cs` (RegisterHotKey):
  default summon = `Ctrl+Alt+D` (was `Ctrl+Alt+Space` — taken by Claude desktop's quick entry), configurable; also mouse-wheel-on-
  island expand, `Esc` collapse. Tray icon with Show/Quit (uses existing
  settings for the target screen; reuse `ScreenService.MoveToScreen`).

Accept: island appears on the laptop screen only; expand/collapse animates
(opacity + scale 0.97→1, 150 ms); all 7 tabs switch; build 0/0.

### Phase 1 — Controls tab (quick settings)
Build `Views/Tabs/ControlsTab.xaml` + `Services/TogglesService.cs`:
- Tile grid (2 columns): Wi-Fi (shows connected SSID via
  `Windows.Devices.Radios` + `NetworkInformation`), Bluetooth, Audio
  (current output device name via MMDevice enumerator P-Invoke),
  Night Light, Dark Mode, DND, Airplane.
- Toggles: Radios (Wi-Fi/Bluetooth/Airplane) via WinRT
  `Radio.RequestAccessAsync` + `SetStateAsync` — real toggles. Dark Mode:
  registry `HKCU\...\Themes\Personalize\AppsUseLightTheme` +
  `SystemUsesLightTheme`, broadcast `WM_SETTINGCHANGE` ("ImmersiveColorSet").
  Night Light & DND have **no public toggle API**: tile reads state where
  possible and otherwise opens the exact Settings page
  (`ms-settings:nightlight`, `ms-settings:quiethours`) — mark these two
  tiles "opens Settings" in code comments and UI subtitle; do not fake a
  toggle that doesn't toggle.
- Slider row: master volume (`IAudioEndpointVolume` P-Invoke), live %.
- Music strip placeholder at the bottom (filled in Phase 2).

Accept: Wi-Fi/BT/airplane tiles flip the real radios; dark mode flips the
whole OS theme; volume slider audibly works; tile subtitles show live
SSID / device names.

### Phase 2 — Real music player (SMTC)
Build `Services/NowPlayingService.cs` (fresh — ignore any external copy):
- `GlobalSystemMediaTransportControlsSessionManager.RequestAsync`, current
  session `TryGetMediaPropertiesAsync` (title, artist, album, thumbnail →
  `BitmapImage`), timeline properties (position/end) with a 500 ms ticker,
  `PlaybackInfo.PlaybackStatus`; transport via session
  `TryPlayAsync/TryPauseAsync/TrySkipNextAsync/TrySkipPreviousAsync`,
  seek via `TryChangePlaybackPositionAsync`.
- UI: maroon card (`MusicPanelBrush`), 56×56 artwork, title/artist,
  progress bar with elapsed/total `m:ss`, prev/play-pause/next round
  buttons. No session → fall back to existing `MediaService` media keys and
  show "Nothing playing — media keys still work".
- Reuse in island ticker (Phase 0 slot) and Controls tab.

Accept: with Spotify/YouTube playing, panel shows real art + moving
progress; pause/seek work; closing the player app degrades gracefully.

### Phase 3 — Today tab
- Header: date-range line ("Oct 12 – 18"), giant day numeral, "PM • Mon 12"
  line, weather line from existing `WeatherService` ("33°C Clear sky"
  format), refresh 15 min.
- Week strip: 7 cells Mon→Sun, today highlighted, dots under days that have
  events. Events store: `Services/CalendarService.cs` reading local events
  from `events.json` + optional ICS file import (parse `DTSTART`/`SUMMARY`
  only). Empty state text: "Nothing coming up — Your calendar is clear".
- Circular timer: `Controls/RingTimer` (Arc path), presets 1/5/10/25 min,
  orange ring, `mm:ss` centre, Reset button; on finish, toast + sound.
  Reuse `MainViewModel` timer plumbing; Pomodoro stays in Dock mode.
- To-dos: `Services/TodoService.cs` + `todos.json` ({text, done, due?,
  created}); UI shows `done/total` ("0/4"), "+ Add a to-do" inline entry,
  checkbox rows, "New Reminder…" = to-do with a due time → Windows toast at
  due time (in-app scheduler while running).

Accept: week strip correct for the real week; timer ring animates and
finishes cleanly; to-dos persist across restart; reminder toast fires.

### Phase 4 — Stats tab
- `Controls/Gauge` (ring + % centre + label): CPU (existing
  `SystemStatsService` GetSystemTimes method), RAM (used/total GB text
  inside, e.g. "11.2 / 15.9 GB"). GPU: attempt PerformanceCounter
  "GPU Engine" utilisation — if it fails on the test machine, ship CPU/RAM
  only and log it; do not block the phase.
- Top apps by RAM: `Process.GetProcesses()` → WorkingSet64 desc, top 5,
  app icon (`Icon.ExtractAssociatedIcon` → `ImageSource`), name + MB/GB.
  Refresh 2 s, keep list stable (no jumping: only re-sort on change >5%).
- Internet speed: live throughput from `NetworkInterface.GetIPv4Statistics`
  deltas (up/down KB/s), Ping readout (`System.Net.NetworkInformation.Ping`
  to the speed host). "Test speed" button: time a 10 MB download from a
  configurable test URL (default Cloudflare speed endpoint), show
  "Finding a server…" state first, then Mbps result. No external API keys.

Accept: gauges track Task Manager within a few %; top-apps list matches
Task Manager order; speed test returns a plausible number on Ubaid's line.

### Phase 5 — Clipboard tab
- `Services/ClipboardService.cs`: `AddClipboardFormatListener` +
  `WM_CLIPBOARDUPDATE` (HwndSource hook in the panel) capturing text and
  images; store in `%AppData%/LapDock/clipboard/` (`index.json` + PNG per
  image item); cap by count setting (default 500 — the reel showed 522, so
  the original clearly keeps hundreds); dedupe consecutive duplicates;
  skip formats from password managers where detectable (`CanIncludeInClipboardHistory`
  / exclude-list by source process name setting).
- UI: search box filters live; chips All/Text/Images/Links (Links = text
  matching URL regex); list (time, 1-line preview / image thumb); preview
  pane right with full text or image; buttons Pin (pinned items survive
  cap + sort first), Copy (writes back to clipboard), Delete, **QR**
  (QRCoder NuGet → QR of text/link shown in preview).
- Global hotkey `Ctrl+Shift+V` summons panel straight to Clipboard tab
  (matches the reel's status bar). Status bar: "Ctrl+Shift+V • N items •
  P pinned".

Accept: copy text/image anywhere → appears in history within a second;
pin/copy/delete/QR all work; history survives restart; hotkey opens tab.

### Phase 6 — Apps tab (launcher)
- `Services/AppCatalogService.cs`: enumerate Start Menu shortcuts
  (`%ProgramData%\...\Start Menu\Programs` + per-user), resolve `.lnk`
  targets (ShellLink COM P-Invoke or `WScript.Shell` via COM — pick
  ShellLink), extract icons; cache to `apps-cache.json` with folder-watch
  invalidation (simple: rebuild on panel open if cache >24 h or folder
  mtime changed).
- UI: search box ("Search apps, files and snippets…") fuzzy-filters apps;
  **files**: search only user-chosen index folders (default Documents +
  Desktop, name match, top 8) — full-disk indexing is out of scope;
  **snippets**: `snippets.json` text entries, Enter copies to clipboard.
- Star toggles favourite (persist `favourites.json`), favourites pinned to
  top with the star icon at panel top-right, as in the reel. Enter/click
  launches via `Process.Start` (UseShellExecute).

Accept: typing "code" finds VS Code; launch works; favourites persist;
cold open < 1 s from cache.

### Phase 7 — Wallpaper tab
- `Services/WallpaperService.cs`: folders = Pictures library + custom
  folders from settings; thumbnails via `BitmapImage` DecodePixelWidth 240
  async + disk cache; header "N pictures".
- Click thumbnail → `SystemParametersInfo(SPI_SETDESKWALLPAPER)` with the
  chosen fit (default Fill; fit setting in tab overflow menu), applied to
  the **main display desktop** (as in the reel, the desktop behind changes);
  preview pane above the grid shows the selected image large. Note in code:
  per-monitor wallpaper (SetDesktopWallpaper via `IDesktopWallpaper` COM)
  is a stretch goal — add if Phase 7 finishes early.

Accept: click changes the real wallpaper in <1 s; preview matches applied
image; grid scrolls smoothly with 100+ pictures.

### Phase 8 — Power tab
Four big round buttons, hold-to-confirm (600 ms press-and-hold ring) for
Restart/Shut down to prevent accidents:
- Lock → `LockWorkStation()`.
- Sleep → `SetSuspendState` (`PowrProf.dll`, `Application.SetSuspendState`
  is WinForms — use P-Invoke to stay clean).
- Restart → `shutdown.exe /r /t 0`; Shut down → `shutdown.exe /s /t 0`
  (simple, reliable; ExitWindowsEx token route only if shutdown.exe is
  blocked by policy on the test machine).

Accept: all four do exactly what they say on the test machine; hold-to-
confirm prevents single-click accidents.

### Phase 9 — Voice + AI layer (the site's other half; owner-gated)
**Do not start without Ubaid's explicit go** — needs his free Gemini API key
in Settings (stored in settings.json, never committed, never logged).
- 9a Dictation: hold `Ctrl+Win` (RegisterHotKey + key-up detection via
  low-level hook) → record mic (NAudio is a NuGet — needs approval; else
  WinRT `Windows.Media.Capture` / `SpeechRecognizer` for offline STT) →
  transcript → Gemini cleanup call (user's key, `gemini-2.0-flash` class
  model, tiny prompt: fix grammar/fill false starts, return only the
  sentence) → insert at caret via `SendInput` Unicode typing into the
  focused app.
- 9b Voice commands: local intent parser first (open <app> → Phase 6
  catalog; set timer N minutes → Phase 3; add to-do <text> → Phase 3;
  wallpaper <name> → Phase 7; lock/sleep → Phase 8); Gemini only as
  fallback classifier returning one of those intents as JSON.
- 9c Routines: "remind me at 6 to …" style one-shot scheduled actions
  stored in `routines.json`, in-app scheduler, completion reported by toast
  + island speech bubble ("done: …"), matching the site's promise.
- Nova is the face of all of this: listening mood (Phase 0 hooks), bubble
  shows the transcript while dictating.

Accept: dictation lands clean text in Notepad and VS Code; the five local
intents work with the network cable pulled (except Gemini cleanup, which
degrades to raw transcript); routine fires on time and reports.

### Phase 10 — Polish & v1.0 packaging
- Tab transition animations, gauge easing, island micro-animations.
- Settings window: screen choice, summon hotkey, clipboard cap, wallpaper
  folders, index folders, Gemini key (Phase 9), start-with-Windows
  (registry Run key), Dock/Island default mode.
- Auto behaviour: island appears when the external monitor is primary
  (display-change listener, `WM_DISPLAYCHANGE`); hide on fullscreen app on
  the laptop screen.
- Tests: xUnit project for TodoService, ClipboardService store,
  AppCatalog filter, speed-test math (pure parts only).
- Publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`,
  GitHub Release `v1.0.0` with the exe + this roadmap's checklist signed
  off phase by phase.

---

## 5. Definition of "same to same" (final check against the reel)

- [ ] Floating dark panel, 7 tabs in order: Controls, Today, Stats,
      Clipboard, Apps, Wallpaper, Power
- [ ] Controls: 7 toggle tiles w/ live state + slider + music strip
- [ ] Music: real artwork/title/progress/transport (SMTC)
- [ ] Today: big date, week strip, weather line, ring timer, to-dos
- [ ] Stats: CPU/RAM rings, top-apps-by-RAM, speed test w/ ping
- [ ] Clipboard: search, Text/Images/Links chips, preview, pin/copy/delete/QR,
      Ctrl+Shift+V
- [ ] Apps: search launcher w/ favourites, files, snippets
- [ ] Wallpaper: grid → instant apply + preview
- [ ] Power: Lock / Sleep / Restart / Shut down (hold-to-confirm)
- [ ] Island summon + Nova as the face
- [ ] (Site layer, Phase 9) Ctrl+Win dictation w/ AI cleanup, voice
      commands, routines

## 6. REA appendix (to be filled if the original binary is obtained)

- Target: _pending — Hitodama installer, if Ubaid provides it_
- Route: `inspect-artifact` → Electron/.NET/native workflow per format
- Findings: _none yet — no claim in this roadmap depends on internals_
