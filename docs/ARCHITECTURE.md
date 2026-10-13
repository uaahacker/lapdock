# LapDock — Architecture

## Idea

Two screens, two jobs:

- **External monitor = work.** IDE, browser, documents.
- **Laptop screen = LapDock.** Ambient, glanceable, alive: companion, music,
  timers, stats, quick actions. Nothing there needs your keyboard focus.

## Stack

- C# 12, WPF, .NET 8 (`net8.0-windows`), x64/AnyCPU.
- No third-party NuGet packages in v0.1 — `dotnet run` works on a clean machine.
- Win32 P/Invoke where WPF has no API: global cursor (`GetCursorPos`),
  media/volume keys (`keybd_event`), memory (`GlobalMemoryStatusEx`),
  CPU (`GetSystemTimes` deltas — no PerformanceCounter dependency),
  exact screen placement (`SetWindowPos` in physical pixels, so 125%/150%
  scaled laptop panels line up correctly).
- `System.Windows.Forms` is referenced only for `Screen` (display enumeration)
  and `SystemInformation.PowerStatus` (battery). No WinForms UI is shown.

## How Nova watches your cursor

`CursorService` polls `GetCursorPos` on a 16 ms `DispatcherTimer` — cheap, and
it sees the cursor **on every monitor**, not just inside the LapDock window
(WPF mouse events would go blind the moment you work on the main screen, which
is exactly when Nova should be watching).

Each tick it raises `CursorMoved(screenPoint)`. `CompanionHead` converts that
to a direction relative to its own on-screen centre, then:

- moves both pupils toward the cursor (clamped to the eye),
- rotates the whole head a few degrees toward it and leans (translate) slightly,
- resets an idle timer — after 12 s without movement Nova gets sleepy
  (half-closed eyes, "Zzz"), any movement wakes it up.

Blinking is an independent random timer (2.2–5.5 s) that scales the eyes
closed for ~120 ms. Clicking Nova triggers a happy bounce (mood system:
`Awake / Sleepy / Happy / Dancing`). `Dancing` is switched on by the music
card while playback is active.

## Music

v0.1 uses Windows **media keys** (`VK_MEDIA_PLAY_PAUSE`, `VK_MEDIA_NEXT_TRACK`,
`VK_MEDIA_PREV_TRACK`, volume keys). Every serious player honours them —
Spotify, YouTube in any browser, VLC, Windows Media Player — so the controls
work everywhere with zero per-app integration.

The proper upgrade (already scoped in ROADMAP) is **SMTC**
(`GlobalSystemMediaTransportControlsSessionManager`) for real track title,
artist, artwork and play state.

## Timers

`MainViewModel` owns one 250 ms ticker driving: wall clock, Pomodoro state
machine (Focus 25 → Break 5 → Focus…), a free countdown and a stopwatch.
All state is plain `INotifyPropertyChanged`; the XAML binds and stays dumb.

## Screens

`ScreenService` lists `Screen.AllScreens`, and `MoveToScreen` positions the
borderless window exactly over the chosen display's bounds and maximises it
there. The choice persists in settings, so LapDock re-opens on the laptop
panel after a reboot.

## Settings

`%AppData%/LapDock/settings.json` — target screen device name, pinned apps,
weather latitude/longitude/city, notes text. Loaded at startup, saved on change.
