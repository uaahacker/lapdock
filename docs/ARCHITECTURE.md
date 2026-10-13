# LapDock — Architecture (v1.0)

## Idea

When an external monitor is the main workspace, the laptop screen sits mostly unused.
LapDock puts a notch at the top of it: a black pill that shows what matters at a glance
and drops open on hover into seven tabs. It never takes focus unless you type into it,
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
- **States**: *Peek* (collapsed: Nova, time or now playing, privacy dots, battery),
  *Activity* (a few seconds of "Charging", "Volume 60%"…), *Expanded* (hover: status
  chips + the tab bar). Opening: widen, drop from the top edge with a small spring;
  content slides in. Closing: quick, straight up.
- **Cost control**: the window is 600×64 while collapsed and 600×800 only while open
  (a layered window is recomposited whole for every repaint). Live tabs (Stats) only
  sample while open on them. Nova only blinks now and then, and never while hidden.
  Measured idle: well under 1% of one core.
- **Fullscreen**: `FullscreenWatcher` (foreground-change hook + a once-a-second check of
  the front window) detects a fullscreen app on the notch's display. Then the notch is a
  plain black pill: no content, no live messages, nothing animating; hovering ~0.5 s
  still opens it.
- Partial classes: `MainWindow.xaml.cs` (states, hover, placement, fullscreen),
  `MainWindow.Widgets.cs` (clock, battery, privacy, media, volume, screenshot, live
  messages), `MainWindow.Tabs.cs` (tabs, Settings, hotkeys, tray, clipboard paste-back,
  power).

## Tabs (`Views/Tabs`)

| Tab | Built on |
|---|---|
| Controls | `TogglesService` (WinRT `Radio`, network profiles, dark-mode registry + broadcast, night-light state), `AudioService` (Core Audio, speakers and microphone), `MusicCard` |
| Today | `WeatherService` (Open-Meteo + geocoding), `CalendarService` (events.json, ICS import), `Controls/Ring` timer, `TodoService` (todos.json, in-app reminder timer) |
| Stats | `SystemStatsService` (GetSystemTimes, memory, GPU via PDH), `ProcessMemoryService` (PDH private working set per app), `NetworkService` (adapter counters, ping, download test) |
| Clipboard | `ClipboardService` (AddClipboardFormatListener; index.json + PNGs; skips copies marked private) |
| Apps | `AppCatalogService` (Windows Apps folder via shell COM, cached; favourites, snippets, file-name index) |
| Wallpaper | `WallpaperService` (IDesktopWallpaper; thumbnails cached on disk) |
| Power | `PowerService` (LockWorkStation; Modern Standby → display off, else SetSuspendState; shutdown.exe) |

## Settings window

`Views/SettingsWindow`: display, weather city, the two shortcuts (`HotkeyService`,
RegisterHotKey), clipboard history, what the notch shows, start with Windows, version
and uninstall. Opened by Ctrl+Alt+D, the tray icon, the notch's right-click menu or a
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
`snippets.json`, `apps-cache.json`, `clipboard\`, `thumbs\`, `error.log`.
