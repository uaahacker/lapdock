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

`NowPlayingService` reads **SMTC**
(`GlobalSystemMediaTransportControlsSessionManager`): track title, artist,
album, artwork, timeline and play state, and drives play/pause/skip/seek on
the current session. With no session (or SMTC unavailable) the transport
buttons fall back to Windows **media keys**; volume always uses media keys.

## Dock

Pins are `Label|Target` strings in settings. `ShellIconService` asks the
Windows shell (`SHCreateItemFromParsingName` + `IShellItemImageFactory`) for
each target's real high-res icon and display name — exe/lnk paths, bare exe
names on PATH, and `shell:AppsFolder\<AUMID>` store apps. The HBITMAP is copied
with `GetDIBits` into a premultiplied WPF bitmap (WPF's own HBITMAP import
drops alpha). No icon → an iOS-style squircle with the label's initial.

Hover magnification scales the icon (1.3×, neighbours 1.1×) in place, sized
so icons never overlap or leave the pill; launching hops it downward, a
failed launch shakes it. Drop files/shortcuts to pin, right-click to remove.

## Timers

`MainViewModel` owns one 200 ms ticker driving: wall clock, Pomodoro state
machine (Focus 25 → Break 5 → Focus…), a free countdown and a stopwatch.
Each tick advances timers by the *real* elapsed time (`Stopwatch`), so a busy
UI thread delaying ticks can't make them drift.
All state is plain `INotifyPropertyChanged`; the XAML binds and stays dumb.

## Screens / the notch

The window is a fixed-size, fully transparent layered window
(`AllowsTransparency`), topmost, `WS_EX_TOOLWINDOW` (no taskbar, no Alt+Tab).
Transparent pixels are click-through, so only the pill itself takes the mouse.
`ScreenService.PlaceTopCenter` pins it flush to the top-centre of the chosen
display with `SetWindowPos` in physical pixels: it first lands on the display
(so a scaling change fires `WM_DPICHANGED` and rescales the window), then
reads the real width and centres.

Hover opens the pill (width/height animation with a slight spring, contents
cross-fade); leaving starts a 350 ms grace timer before closing. While any
right-click menu is open the timer keeps re-checking instead of closing.

The chosen display persists in settings; on first run LapDock picks the first
non-primary display (the laptop panel when an external monitor is main).
`SystemEvents.DisplaySettingsChanged` puts it back when monitors come and go.

## System widgets

One service per integration in `Services/`, each optional (settings flags), started in
isolation (a failure hides that widget only), and **event-driven — nothing polls**:

| Widget | Mechanism | Change signal |
| --- | --- | --- |
| Battery | WinRT `Windows.System.Power.PowerManager` (fallback: WinForms `PowerStatus`) | `RemainingChargePercentChanged`, `BatteryStatusChanged`, `PowerSupplyStatusChanged` |
| Mic / camera | `HKCU\…\CapabilityAccessManager\ConsentStore\{microphone,webcam}` — per-app `LastUsedTimeStart/Stop`; Stop = 0 means in use. Desktop apps are only trusted while their exe runs (a crash can leave Stop = 0). Names: exe `FileDescription` / package `DisplayName` | `RegNotifyChangeKeyValue` on a background thread |
| Media | SMTC `GlobalSystemMediaTransportControlsSessionManager` (`NowPlayingService`) | session/media/playback/timeline events; position ticks only while playing |
| Volume | Core Audio `IMMDeviceEnumerator` → default render endpoint → `IAudioEndpointVolume` | `IAudioEndpointVolumeCallback` (own changes tagged with an event-context GUID), `IMMNotificationClient` for default-device changes |
| Screenshot | Rectangle/Window: `ms-screenclip:?clippingMode=…` (legacy but honoured; the new `ms-screenclip://capture` needs an MSIX package). Full screen: Win+PrintScreen. Open: Snipping Tool AUMID | `FileSystemWatcher` on the Screenshots known folder |
| Wallpaper | shell `IDesktopWallpaper` (get/set on all displays, fit style); thumbnails decoded small off-thread, cached | `SystemEvents.UserPreferenceChanged` (Desktop) |

The notch has three states sharing one pill — **Peek**, **Activity** (a few seconds,
de-duplicated, skipped while expanded) and **Expanded**. `ResizePill` runs the pending
layout pass, then animates the pill only when the target size actually changes (each
animation repaints the whole layered window). The only free-running animation is the
music pulse, and only while something plays; the clock wakes once a minute.

## Settings

`%AppData%/LapDock/settings.json` — target screen device name, pinned apps,
weather latitude/longitude/city, notes text. Loaded at startup, saved on change.
