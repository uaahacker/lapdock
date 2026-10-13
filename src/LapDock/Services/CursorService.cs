using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// Polls the GLOBAL cursor position with Win32 GetCursorPos.
/// This is the heart of LapDock: WPF mouse events only fire inside our own
/// window, but Nova must keep watching you while you work on the OTHER screen.
/// </summary>
public sealed class CursorService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly DispatcherTimer _timer;
    private Point _last;
    private DateTime _lastMoveUtc = DateTime.UtcNow;

    /// <summary>Raised ~60x/sec on the UI thread with the cursor position in physical screen pixels.</summary>
    public event Action<Point>? CursorMoved;

    /// <summary>Seconds since the cursor last actually moved.</summary>
    public double IdleSeconds => (DateTime.UtcNow - _lastMoveUtc).TotalSeconds;

    public CursorService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) =>
        {
            if (!GetCursorPos(out var p)) return;
            var pos = new Point(p.X, p.Y);
            if (pos != _last)
            {
                _last = pos;
                _lastMoveUtc = DateTime.UtcNow;
            }
            CursorMoved?.Invoke(pos);
        };
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void Dispose() => _timer.Stop();
}
