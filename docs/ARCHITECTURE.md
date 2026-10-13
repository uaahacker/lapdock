# LapDock — Architecture (v1.2)

## Idea

When an external monitor is the main workspace, the laptop screen sits mostly unused.
LapDock puts a notch at the top of it: a black pill that shows what matters at a glance
and drops open on hover into eight tabs. It never takes focus unless you type into it,
and it stays out of the way of fullscreen apps.

## Stack

- C# 12, WPF, .NET 8, `net8.0-windows10.0.22621.0` (WinRT for SMTC, radios, network, power).
- One NuGet package: **QRCoder** (clipboard QR codes). Everything else is Windows itself:
  Win32 / COM / WinRT through P/Invoke.
- WinForms is referenced only for `Screen` and the tray `NotifyIcon`
  (aliased `using Forms = System.Windows.Forms;`; its global usings are removed in the csproj).
- PerMonitorV2 (`app.manifest`); windows are placed with `SetWindowPos` in physical pixels.

## Startup (`App.xaml.cs`)

| Command line | What happens |
|---|---|
| `--uninstall [--quiet] [--remove-data]` | uninstall window (or silent uninstall) |
| `--install --quiet [--autostart] [--desktop] [--no-launch]` | silent install |
| published exe, not in its install folder | install window (Install / Try without installing) |
| otherwise | the notch, once per session (`SingleInstance`: a second launch opens Settings) |

`InstallService` installs per user: exe in `%LocalAppData%\Programs\LapDock`, Start menu
shortcut (IShellLink), an uninstall entry under HKCU (Settings → Apps), HKCU Run for
start-at-sign-in. Uninstall closes the running copy, removes all of that, and a hidden
`cmd` deletes the folder after the exe exits. The installer path only exists in the
published single-file build (`SINGLE_FILE` is defined when `PublishSingleFile=true`).

Unhandled UI errors are logged to `%AppData%\LapDock\error.log` and the notch keeps running.

## The notch (`MainWindow`)

A fixed-width, fully transparent, topmost tool window (no taskbar / Alt+Tab entry);
transparent pixels are click-through, so only the black shape takes the mouse.

- **Shape**: `Controls/NotchShape` draws the black pill with concave "shoulders" at the
  top edge and bottom corners that soften as it grows. Width/height are animated; all
  content is clipped to the shape every frame, so nothing draws outside it.
- **States**: *Peek* (collapsed: Nova, a live activity — call, timer / focus, download — or now playing
  with level-driven music bars, or the time; privacy dots, battery), *Activity* (a few seconds of
  "Charging", "Volume 60%", a download finished, a device connected, or another app's notification —
  clickable ones stay up while hovered), *Expanded* (hover: status chips + the tab bar). Opening: widen, drop from the top edge with a small spring;
  content slides in. Closing: quick, straight up.
- **Cost control**: the window is 600×64 while collapsed, 600×120 while a banner shows and 600×800
  only while open (a layered window is recomposited whole for every repaint). Live tabs (Stats) only
  sample while open on them. Nova only blinks now and then, and never while hidden. The music bars
  (`Controls/MusicBars`, drawn without layout, ~15 fps) run only while music plays and nothing hides
  the pill; everything that animates stops while the session is locked. Measured: under 1% of one
  core at rest, ~4.7% with music bars.
- **Apps under the notch** (setting `NotchSpace`): *Reserve* — `AppBarService` registers an
  appbar (SHAppBarMessage) reserving a strip the height of the pill across the top of the
  display, so maximised windows start below it (appbar notifications are ignored on purpose:
  answering them re-reserved in a loop, except `TaskbarCreated` after an Explorer restart; a circuit
  breaker gives the strip back and falls back to *Overlay* after more than 5 changes in 30 s); *AutoHide* — `FullscreenWatcher.IsTopCovered` walks
  the windows below the notch at the top edge; while one is there the pill shrinks to nothing
  and a 100 ms cursor check opens it when the mouse is pushed against the top-centre edge;
  *Overlay* — float on top.
- **Fullscreen**: `FullscreenWatcher` (foreground-change hook + a once-a-second check of
  the front window) detects a fullscreen app on the notch's display. Then the notch is a
  plain black pill: no content, no live messages, nothing animating; hovering ~0.5 s
  still opens it.
- **Notifications from apps**: `AppNotificationService` reads other apps' toasts with
  `UserNotificationListener` (allowed for desktop apps; its change event is not, and a read costs ~30 ms
  of CPU). It reads only when `wpndatabase.db-wal` in `%LocalAppData%\Microsoft\Windows\Notifications`
  changes — Windows writes it within ~0.1 s of a notification; its timestamp is checked once a second —
  plus once a minute as a safety net. New ids are grouped per app; the app's icon comes from
  `shell:AppsFolder\<AUMID>` (which also opens the app on click). During a focus session they're held.
- **Live activities**: a call = an app from a known list (Teams, Zoom, Discord, WhatsApp, browsers…) in
  `PrivacyService`'s microphone list; timer / focus from `TodayTab.Countdown`; downloads from
  `DownloadWatcher` (Downloads folder: `.crdownload` / `.part` appear, grow, and are renamed when done).
  `UsbWatcher` (WinRT DeviceWatcher on USB device interfaces, named by device container, the PC's own
  container skipped) reports new devices and a removable drive that mounts with them. `AudioService`
  raises `OutputChanged` when Windows switches the default output (headphones, by endpoint form factor).
- **Drop target**: the island accepts files / links / text; dragging over it opens the Shelf tab.
- Partial classes: `MainWindow.xaml.cs` (states, hover, drag and drop, placement, fullscreen, lock),
  `MainWindow.Widgets.cs` (clock, battery, privacy, media, volume, screenshot, live activities,
  notifications, downloads, devices), `MainWindow.Tabs.cs` (tabs, Settings, hotkeys, tray, clipboard
  paste-back, power).

## Tabs (`Views/Tabs`)

| Tab | Built on |
|---|---|
| Controls | `TogglesService` (WinRT `Radio`, network profiles, dark-mode registry + broadcast, night-light state), `AudioService` (Core Audio, speakers and microphone), `MusicCard` |
| Today | `WeatherService` (Open-Meteo + geocoding), `CalendarService` (events.json, ICS import), `Controls/Ring` timer or focus sessions (25 + 5 min, counted per day in settings), `TodoService` (todos.json, in-app reminder timer) |
| Stats | `SystemStatsService` (GetSystemTimes, memory, GPU via PDH), `ProcessMemoryService` (PDH private working set per app), `NetworkService` (adapter counters, ping, download test) |
| Clipboard | `ClipboardService` (AddClipboardFormatListener; index.json + PNGs; skips copies marked private) |
| Shelf | `ShelfService` (shelf.json; files by path), drag out with `DoDragDrop`, Windows share sheet (`DataTransferManagerInterop`), QR (`QRCoder`; files via `PhoneShareServer`: a one-file HTTP server on the LAN address with a random token, 10 minutes) |
| Apps | `AppCatalogService` (Windows Apps folder via shell COM, cached; favourites, snippets, file-name index) |
| Wallpaper | `WallpaperService` (IDesktopWallpaper; thumbnails cached on disk) |
| Power | `PowerService` (LockWorkStation; Modern Standby → display off, else SetSuspendState; shutdown.exe) |

## Battery panel

Battery chip → panel: level and time left, `Controls/BatteryChart` (48 half-hour bars from
`BatteryHistory`: one line per change plus one every 10 minutes while running, kept 7 days), health and
power from WinRT `Battery.AggregateBattery.GetReport()` (full-charge vs design capacity, charge rate).

## Settings window

`Views/SettingsWindow`: display, weather city, the two shortcuts (`HotkeyService`,
RegisterHotKey), clipboard history, what the notch shows (including live messages and notifications
from apps), start with Windows, version and uninstall. Opened by Ctrl+Alt+D, the tray icon, the notch's right-click menu or a
second launch.

## Look

Black, neutral greys and one green (`AccentBrush`); orange/red only for low battery and
the microphone-in-use dot. One button system in `App.xaml` (`PillButton`, `PrimaryPill`,
`OptionPill` chips, `RoundButton`). Icons are LapDock's own (`assets/icons/svg`), drawn
as vectors by `Controls/LineIcon` from `LineIcons.cs` (generated by
`tools/make_line_icons.py`); a few system glyphs (mic, camera, close…) still come from
Segoe Fluent Icons.

## Data

`%AppData%\LapDock`: `settings.json`, `todos.json`, `events.json`, `favourites.json`,
`snippets.json`, `apps-cache.json`, `shelf.json`, `battery-history.csv`, `clipboard\`, `thumbs\`, `error.log`.
