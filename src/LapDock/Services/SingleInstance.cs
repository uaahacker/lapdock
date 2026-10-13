using System.Diagnostics;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// One LapDock per user session. Launching it again (Start menu, desktop, sign-in) asks the running one to
/// show Settings, then exits. The installer and uninstaller ask the running one to quit before touching its files.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\LapDock.Running", ShowName = @"Local\LapDock.Show", QuitName = @"Local\LapDock.Quit";

    private static Mutex? _mutex;
    private static readonly List<RegisteredWaitHandle> Waits = new();

    /// <summary>true = this is the only LapDock; the claim lasts until <see cref="Release"/> or exit.</summary>
    public static bool TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created)
        {
            mutex.Dispose();
            return false;
        }
        _mutex = mutex;
        return true;
    }

    public static void Release()
    {
        foreach (var wait in Waits) wait.Unregister(null);
        Waits.Clear();
        if (_mutex is null) return;
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned on this thread any more */ }
        _mutex.Dispose();
        _mutex = null;
    }

    public static bool IsRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    /// <summary>A second launch: tell the running LapDock to open Settings.</summary>
    public static void AskToShow()
    {
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        show.Set();
    }

    /// <summary>The running LapDock answers "show Settings" and "quit" (on the UI thread).</summary>
    public static void Listen(Dispatcher dispatcher, Action show, Action quit)
    {
        Register(ShowName, () => dispatcher.BeginInvoke(show));
        Register(QuitName, () => dispatcher.BeginInvoke(quit));
    }

    private static void Register(string name, Action action)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        Waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => action(), null, Timeout.Infinite, executeOnlyOnce: false));
    }

    /// <summary>
    /// Asks a running LapDock to quit and waits for it. Any LapDock process still left after that (an older
    /// copy that can't hear the request) is ended, so its files can be replaced or removed.
    /// </summary>
    public static void StopRunning(TimeSpan timeout)
    {
        if (IsRunning())
        {
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName)) quit.Set();
            var clock = Stopwatch.StartNew();
            while (IsRunning() && clock.Elapsed < timeout) Thread.Sleep(100);
        }
        foreach (var other in Process.GetProcessesByName("LapDock").Where(p => p.Id != Environment.ProcessId))
        {
            using (other)
            {
                try { other.Kill(); other.WaitForExit(3000); }
                catch (Exception) { /* already gone, or not ours to end */ }
            }
        }
    }
}
