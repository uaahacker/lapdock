# LapDock

**A notch for your laptop screen.** A small black pill sits at the top-centre of the
display. Hover it and it drops open with everything one reach away: quick settings,
music, your day, system stats, clipboard history, apps, wallpapers and power.
When a video or game goes fullscreen, it shrinks to a plain black pill and stays out
of the way.

## Install (Windows 10/11)

1. Download `LapDock-1.0.0.exe` and open it.
   Windows SmartScreen may say *"Windows protected your PC"* because the file isn't
   code-signed yet: choose **More info → Run anyway**.
2. Click **Install** (or **Try without installing** to run it once from where it is).

It installs for you only, in `%LocalAppData%\Programs\LapDock`, with no admin rights.
You get a Start menu entry, optionally a desktop shortcut and start-at-sign-in.

**Uninstall:** Settings → Apps → LapDock → Uninstall, or LapDock Settings → *Uninstall…*.
Your settings and history are kept unless you choose to delete them.

For scripts: `LapDock-1.0.0.exe --install --quiet [--autostart] [--desktop] [--no-launch]`
and `LapDock.exe --uninstall --quiet [--remove-data]`.

## What's in the notch

| Tab | What it does |
|---|---|
| **Controls** | Wi-Fi, Bluetooth, audio, microphone, dark mode, airplane (real toggles); night light and Do not disturb open their Settings page; volume; the music player |
| **Today** | Time, date and weather, this week with calendar events (import `.ics`), a quick ring timer, to-dos with `@18:30` reminders |
| **Stats** | CPU, memory and GPU rings; top apps by memory (counted like Task Manager); network speed, ping and a speed test |
| **Clipboard** | Searchable history of copied text and images; pin, QR code, save as snippet; **Enter pastes** into the app you were in |
| **Apps** | Launcher for every Start app, files in Documents/Desktop and your snippets; favourites |
| **Wallpaper** | Your pictures in a grid: click one and the desktop changes; fit options |
| **Power** | Lock, sleep, and restart / shut down (hold to confirm) |

When collapsed it shows the time (or what's playing), battery and microphone/camera
dots, plus short messages like *Charging*, *Volume 60%* or *Screenshot saved*.

## Shortcuts

- **Ctrl+Alt+D**: Settings (display, weather city, shortcuts, what the notch shows,
  start with Windows, uninstall)
- **Ctrl+Shift+V**: clipboard history (changeable in Settings; while it's set, apps
  don't get their own Ctrl+Shift+V "paste as plain text")
- Opening LapDock again (Start menu) also opens Settings.

## Privacy

Everything stays on your PC (`%AppData%\LapDock`). The only network calls are the
weather (Open-Meteo, no account), the optional speed test and ping (Cloudflare).
Clipboard history skips copies that password managers mark as private.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/LapDock          # run (dev builds skip the installer)
dotnet publish src/LapDock -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o release/publish
```

The published `LapDock.exe` is the shareable installer and the app in one file.

Icons: `assets/icons/svg` (LapDock's own set). After editing one, run
`python tools/make_line_icons.py` to regenerate `src/LapDock/Controls/LineIcons.cs`.

## License

MIT — see [LICENSE](LICENSE).
