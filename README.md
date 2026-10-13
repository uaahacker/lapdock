# LapDock

**A notch for your laptop screen.** A small black pill sits at the top-centre of the
display. Hover it and it drops open with everything one reach away: quick settings,
music, your day, system stats, clipboard history, a shelf for files, apps, wallpapers and
power. Like the iPhone's Dynamic Island it shows what's going on — a call, a timer, a download,
music — and other apps' notifications (Teams, WhatsApp, Discord…) as a banner.
When a video or game goes fullscreen, it shrinks to a plain black pill and stays out
of the way.

## Install (Windows 10/11)

1. Download `LapDock-1.2.0.exe` and open it.
   Windows SmartScreen may say *"Windows protected your PC"* because the file isn't
   code-signed yet: choose **More info → Run anyway**.
2. Click **Install** (or **Try without installing** to run it once from where it is).

It installs for you only, in `%LocalAppData%\Programs\LapDock`, with no admin rights.
You get a Start menu entry, optionally a desktop shortcut and start-at-sign-in.

**Uninstall:** Settings → Apps → LapDock → Uninstall, or LapDock Settings → *Uninstall…*.
Your settings and history are kept unless you choose to delete them.

For scripts: `LapDock-1.2.0.exe --install --quiet [--autostart] [--desktop] [--no-launch]`
and `LapDock.exe --uninstall --quiet [--remove-data]`.

## What's in the notch

| Tab | What it does |
|---|---|
| **Controls** | Wi-Fi, Bluetooth, audio, microphone, dark mode, airplane (real toggles); night light and Do not disturb open their Settings page; volume; the music player |
| **Today** | Time, date and weather, this week with calendar events (import `.ics`), a quick ring timer or **Focus** (25 min focus, 5 min break, sessions counted), to-dos with `@18:30` reminders |
| **Stats** | CPU, memory and GPU rings; top apps by memory (counted like Task Manager); network speed, ping and a speed test |
| **Clipboard** | Searchable history of copied text and images; pin, QR code, save as snippet; **Enter pastes** into the app you were in |
| **Shelf** | Drag files, links or text onto the notch to keep them for later; drag them back out into any app, share them, or scan a QR code to get them on your phone |
| **Apps** | Launcher for every Start app, files in Documents/Desktop and your snippets; favourites |
| **Wallpaper** | Your pictures in a grid: click one and the desktop changes; fit options |
| **Power** | Lock, sleep, and restart / shut down (hold to confirm) |

**Apps under the notch** (Settings → *When an app reaches the top of the screen*):

- **Make room** (default): a thin strip at the top stays free for the notch, the way the taskbar
  reserves its edge, so maximised apps (Chrome's tabs, title-bar buttons) start just below it.
  If Windows ever keeps moving that strip, LapDock gives it back and switches to *Stay on top*.
- **Hide when covered**: while an app's window is under the notch it slides away and clicks go to
  the app; push the mouse against the top edge, in the middle, to open it.
- **Stay on top**: the notch floats over everything.

When collapsed it shows the time, battery and microphone/camera dots — or what's going on:

- **Live activities**: a call and how long it's been going (Teams, Zoom, Discord, WhatsApp, Meet…),
  the timer or a focus session counting down, a download and its size, music with bars that move with
  the sound.
- **Banners**: other apps' notifications with their icon and two lines of the message (click to open the
  app); *Downloaded* (click to show it), a USB device or drive connected (click to open the drive),
  headphones connected, *Charging*, *Volume 60%*, *Screenshot saved*.
- **Battery** (click the battery in the open notch): health, power draw, and the last 24 hours as a chart.

While you focus, notifications wait and are summed up when the session ends. To see app notifications only
in the notch, turn off that app's banners in Windows Settings → System → Notifications.

## Shortcuts

- **Ctrl+Alt+D**: Settings (display, weather city, shortcuts, what the notch shows,
  start with Windows, uninstall)
- **Ctrl+Shift+V**: clipboard history (changeable in Settings; while it's set, apps
  don't get their own Ctrl+Shift+V "paste as plain text")
- Opening LapDock again (Start menu) also opens Settings.

## Privacy

Everything stays on your PC (`%AppData%\LapDock`). The only network calls are the
weather (Open-Meteo, no account), the optional speed test and ping (Cloudflare).
Clipboard history skips copies that password managers mark as private. Notifications from other apps are
read on your PC and never stored (Settings → Privacy → Notifications can revoke it). A shelf file's QR code
opens a download link on your own network only, for that one file, until you close it (10 minutes at most);
Windows Firewall asks once whether to allow it.

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
