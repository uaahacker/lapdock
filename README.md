# LapDock

**Turn your laptop screen into a living dock.**

When you plug an external monitor into your laptop, the external screen becomes
your main workspace — and the laptop screen usually sits there wasted, showing a
duplicate desktop or a random window.

**LapDock lives on that laptop screen.** It is a clean, fullscreen companion
display for Windows with:

- 🧑‍🚀 **Nova — an animated companion head** that *watches your cursor* in real
  time (even when the cursor is on your other monitor). Nova blinks, tilts,
  follows you around, gets sleepy when you stop moving, dances when music
  plays, and reacts when you click it.
- 🎵 **Music controls** — previous / play-pause / next + volume, working with
  Spotify, YouTube, VLC and anything else that listens to Windows media keys.
  Animated equalizer bars while music plays.
- ⏱️ **Focus timer suite** — big clock, Pomodoro (25/5), countdown timer and
  stopwatch in one place.
- 📊 **System vitals** — live CPU, RAM and battery with clean progress bars.
- 🚀 **App dock** — a macOS-style bottom dock with hover magnification for your
  pinned apps and Windows tools (Explorer, Terminal, Settings, Snipping Tool,
  Task Manager…).
- ⚡ **Quick actions** — lock PC, screenshot, focus/do-not-disturb settings,
  display settings, and one-tap links to the controls you always hunt for.
- 🌤️ **Weather** — current conditions for your city via Open-Meteo (free, no
  API key).
- 📝 **Scratch notes** — a tiny notepad that saves automatically.

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
2. Start LapDock. In its top bar, pick your **laptop display** from the screen
   picker — LapDock goes fullscreen there and stays out of your way.
3. Work on the big screen. Nova watches your cursor from the laptop screen,
   your music, timer and stats are always one glance down.

Handy keys: `F11` toggle fullscreen · `Esc` exit fullscreen · click Nova to poke it.

## Project layout

```
LapDock.sln
src/LapDock/
  LapDock.csproj
  App.xaml / App.xaml.cs          app entry + shared styles (glass cards, dock buttons)
  MainWindow.xaml / .xaml.cs      the dock shell: top bar, widget grid, bottom dock
  Controls/
    CompanionHead.xaml(.cs)       Nova: eye tracking, blinking, tilt, moods
  Services/
    CursorService.cs              global cursor position (Win32 GetCursorPos, 60 fps poll)
    MediaService.cs               media keys + volume (works with every player)
    SystemStatsService.cs         CPU / RAM / battery
    ScreenService.cs              enumerate displays, move window to a display
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

**v0.1 starter — working skeleton, all headline features live in basic form.**
See [docs/ROADMAP.md](docs/ROADMAP.md) for what is next: real Now Playing via
Windows SMTC (song title/artist/artwork), per-screen auto-launch, widgets
SDK, themes, tray mode, and more Nova moods/animations.

## License

MIT — see [LICENSE](LICENSE).
