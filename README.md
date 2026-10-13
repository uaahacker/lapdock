# LapDock

**Turn your laptop screen into a living dock.**

When you plug an external monitor into your laptop, the external screen becomes
your main workspace — and the laptop screen usually sits there wasted, showing a
duplicate desktop or a random window.

**LapDock lives on that laptop screen** — as a notch. A small black pill sits
flush with the top-centre of the display, like a phone's Dynamic Island.
**Hover it** and it springs open; move away and it closes again. No window, no
resizing, no fullscreen, nothing in the taskbar or Alt+Tab, and the empty
space around it never blocks your clicks.

**Collapsed**, it shows what matters right now: the time (or the song that's
playing, with a green pulse), 🟢 camera / 🟠 microphone privacy dots, and the
battery. **Short live messages** appear for a few seconds when something
happens — *Charging · 54%*, *Low battery*, *Microphone in use · Discord*,
*Volume 72%*, *Screenshot saved*, *Now playing*, *Wallpaper changed*.

**Expanded:**

- 🚀 **Dock** — your apps' *real* icons, gentle magnification, launch bounce.
  Drag an app/shortcut onto the notch to pin it (a drop slot shows where);
  right-click an icon → *Remove from dock*.
- 🎵 **Now Playing** — artwork, title, artist, seek bar, previous/play/next
  for whatever Windows' media session reports (Spotify, browsers, Netflix…).
- 📸 **Screenshot** — Rectangle, Window, Full screen, or open Snipping Tool.
- 🔊 **Volume** — system volume slider and mute.
- 🖼️ **Wallpaper** — preview, *Change…* (file picker), *Next*, recent
  wallpapers, and Fill / Fit / Stretch / Tile / Center / Span.
- 🔋 **Battery** and 🛡️ **Privacy** — tap the chips in the top row for details
  (which apps are using the mic/camera).
- 🖥️ **Right-click the notch** → *Move to next display* or *Quit LapDock*.

Every widget can be switched off in `%AppData%\LapDock\settings.json`
(`ShowClock`, `ShowBattery`, `MicrophoneIndicator`, `CameraIndicator`,
`MediaControls`, `DynamicNotifications`) until the settings panel lands.

> The earlier dashboard widgets (Nova companion, Pomodoro/timers, weather,
> notes) are parked; their code is kept in `Controls/`, `ViewModels/` and
> `Services/` to bring back into the notch.

## Why C# + WPF (.NET 8)?

Native Windows, single small executable, zero web stack, direct Win32 access
for global cursor tracking and media keys, and it builds with one command on
any Windows machine. Same family as a WinUI app, but with no Windows App SDK
packaging pain for a v1.

## Run it (Windows)

```powershell
git clone https://github.com/uaahacker/lapdock.git
cd lapdock
dotnet run --project src/LapDock
```

Or open `LapDock.sln` in Visual Studio 2022 and press F5.

**Requirements:** Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Use it the way it is meant to be used

1. Plug in your external monitor and set it as your **main display**
   (Settings → System → Display).
2. Start LapDock. The notch appears at the top of your **laptop display**
   (the non-main one). Right-click it → *Move to next display* to change
   screens; it remembers your choice, even across unplug/replug.
3. Hover the notch whenever you want your apps.

## Project layout

```
LapDock.sln
src/LapDock/
  LapDock.csproj
  App.xaml / App.xaml.cs          app entry + shared styles (glass cards, dock buttons)
  MainWindow.xaml / .xaml.cs      the notch shell: peek / activity / expanded states, hover, placement
  MainWindow.Dock.cs              pinned apps: icons, magnify, launch, drag-to-pin, unpin
  MainWindow.Widgets.cs           clock, battery, privacy, media, volume, screenshot, wallpaper, live messages
  Controls/
    CompanionHead.xaml(.cs)       Nova: eye tracking, blinking, tilt, moods
  Services/
    CursorService.cs              global cursor position (Win32 GetCursorPos, 60 fps poll)
    MediaService.cs               media keys + volume (works with every player)
    SystemStatsService.cs         CPU / RAM / battery
    NowPlayingService.cs          SMTC: title, artist, artwork, seek, play state
    BatteryService.cs             battery %, charging, low/critical/full (WinRT PowerManager events)
    PrivacyService.cs             mic/camera in use + which app (consent store, registry change notify)
    AudioService.cs               system volume + mute (Core Audio, change callbacks)
    ScreenshotService.cs          Snipping Tool capture modes, Win+PrtScn, "screenshot saved" watcher
    WallpaperService.cs           wallpaper get/set/fit/next + cached thumbnails (IDesktopWallpaper)
    ShellIconService.cs           real app icons + names for dock pins (Windows shell)
    ScreenService.cs              pick the display, pin the notch to its top-centre
    SettingsService.cs            %AppData%/LapDock/settings.json
    WeatherService.cs             Open-Meteo, no key needed
  ViewModels/
    MainViewModel.cs              clock, Pomodoro, countdown, stopwatch, notes
  Models/
    AppSettings.cs
docs/
  ARCHITECTURE.md
  ROADMAP.md
```

## Status

**v0.3 — the notch is a system utility** (battery, privacy, media, volume,
screenshots, wallpaper, live messages). See [docs/ROADMAP.md](docs/ROADMAP.md)
for everything done so far, how it was checked, known limits, and suggestions
for what could come next.

## License

MIT — see [LICENSE](LICENSE).
