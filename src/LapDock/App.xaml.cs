using System.Diagnostics;
using System.IO;
using System.Windows;
using LapDock.Services;
using LapDock.Views;

namespace LapDock;

/// <summary>
/// Starts the right thing:
/// <list type="bullet">
/// <item><c>--uninstall [--quiet] [--remove-data]</c>: Settings → Apps → Uninstall.</item>
/// <item><c>--install --quiet [--autostart] [--desktop] [--no-launch]</c>: silent install (scripts, IT).</item>
/// <item>The shared LapDock.exe opened from anywhere but its install folder: the install window.</item>
/// <item>Otherwise the notch — once per user session; a second launch opens Settings in the running one.</item>
/// </list>
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Services.TodoService.SelfCheck();     // Debug builds only
        Services.CalendarService.SelfCheck();
        Services.ProcessMemoryService.SelfCheck();
        Services.NetworkService.SelfCheck();
        Services.ClipboardService.SelfCheck();
        Services.AppCatalogService.SelfCheck();
        Services.FullscreenWatcher.SelfCheck();
        Services.AppBarService.SelfCheck();
        Services.DownloadWatcher.SelfCheck();
        Services.BatteryHistory.SelfCheck();
        Services.PhoneShareServer.SelfCheck();
        base.OnStartup(e);
        // A widget must not take itself down over one bad moment: log it and keep going.
        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log(ex.ExceptionObject as Exception);

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();
        bool quiet = args.Contains("--quiet");

        if (args.Contains("--uninstall"))
        {
            if (!quiet) { new SetupWindow(SetupMode.Uninstall).Show(); return; }
            Finish(() => InstallService.Uninstall(removeData: args.Contains("--remove-data")));
            return;
        }
        if (args.Contains("--install"))
        {
            Finish(() =>
            {
                InstallService.Install(startWithWindows: args.Contains("--autostart"), desktopShortcut: args.Contains("--desktop"));
                if (!args.Contains("--no-launch"))
                    Process.Start(new ProcessStartInfo(InstallService.InstalledExe, "--installed") { UseShellExecute = false });
            });
            return;
        }
        if (InstallService.IsPackaged && !InstallService.RunningInstalled && !args.Contains("--portable"))
        {
            var setup = new SetupWindow(SetupMode.Install);
            setup.TryRequested += () => RunNotch(greet: true);
            setup.Show();
            return;
        }
        RunNotch(greet: args.Contains("--installed"));
    }

    /// <summary>Silent install/uninstall: do it, exit 0 on success, 1 on failure.</summary>
    private void Finish(Action work)
    {
        try { work(); Shutdown(0); }
        catch (Exception) { Shutdown(1); }
    }

    private void RunNotch(bool greet)
    {
        if (!SingleInstance.TryClaim())
        {
            SingleInstance.AskToShow(); // already running: open its Settings instead of a second notch
            Shutdown();
            return;
        }
        var notch = new MainWindow { GreetOnStart = greet };
        MainWindow = notch;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        notch.Show();
        SingleInstance.Listen(Dispatcher, notch.ShowSettings, notch.Close);
    }

    /// <summary>%AppData%\LapDock\error.log, kept small: what went wrong, for a bug report.</summary>
    private static void Log(Exception? error)
    {
        try
        {
            var path = Path.Combine(SettingsService.Dir, "error.log");
            Directory.CreateDirectory(SettingsService.Dir);
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:u}  LapDock {InstallService.Version}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* nowhere to log to */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }
}
