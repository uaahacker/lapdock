using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace LapDock.ViewModels;

/// <summary>
/// All the time-based state for the dock: wall clock, Pomodoro, countdown,
/// stopwatch. One 200 ms ticker drives everything; XAML only binds.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainViewModel()
    {
        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
        Tick();
    }

    // ------------------------------------------------------------- wall clock
    private string _clockText = "";
    public string ClockText { get => _clockText; private set => Set(ref _clockText, value); }

    private string _dateText = "";
    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

    // -------------------------------------------------------------- pomodoro
    public const int FocusMinutes = 25;
    public const int BreakMinutes = 5;

    private bool _pomoIsFocus = true;
    private bool _pomoRunning;
    private TimeSpan _pomoLeft = TimeSpan.FromMinutes(FocusMinutes);
    private int _pomoDone;

    public string PomoPhase => _pomoIsFocus ? "🎯 Focus" : "☕ Break";
    public string PomoDisplay => Format(_pomoLeft);
    public string PomoButtonText => _pomoRunning ? "Pause" : (_pomoLeft.TotalSeconds < (_pomoIsFocus ? FocusMinutes : BreakMinutes) * 60 ? "Resume" : "Start focus");
    public double PomoProgress => 1 - _pomoLeft.TotalSeconds / ((_pomoIsFocus ? FocusMinutes : BreakMinutes) * 60.0);
    public string PomoDoneText => _pomoDone == 0 ? "" : $"✔ {_pomoDone} pomodoro{(_pomoDone > 1 ? "s" : "")} done";
    public bool PomoIsRunning => _pomoRunning;

    public event Action<bool>? PomodoroPhaseChanged; // true = focus started

    public void PomoStartPause()
    {
        _pomoRunning = !_pomoRunning;
        RaisePomo();
    }

    public void PomoReset()
    {
        _pomoRunning = false;
        _pomoIsFocus = true;
        _pomoLeft = TimeSpan.FromMinutes(FocusMinutes);
        RaisePomo();
    }

    private void TickPomo()
    {
        if (!_pomoRunning) return;
        _pomoLeft -= _ticker.Interval;
        if (_pomoLeft <= TimeSpan.Zero)
        {
            if (_pomoIsFocus) _pomoDone++;
            _pomoIsFocus = !_pomoIsFocus;
            _pomoLeft = TimeSpan.FromMinutes(_pomoIsFocus ? FocusMinutes : BreakMinutes);
            PomodoroPhaseChanged?.Invoke(_pomoIsFocus);
        }
        RaisePomo();
    }

    private void RaisePomo()
    {
        OnPropertyChanged(nameof(PomoPhase));
        OnPropertyChanged(nameof(PomoDisplay));
        OnPropertyChanged(nameof(PomoButtonText));
        OnPropertyChanged(nameof(PomoProgress));
        OnPropertyChanged(nameof(PomoDoneText));
        OnPropertyChanged(nameof(PomoIsRunning));
    }

    // ------------------------------------------------------------- countdown
    private TimeSpan _countLeft = TimeSpan.FromMinutes(10);
    private bool _countRunning;
    public string CountdownInput { get; set; } = "10"; // minutes, typed by the user
    public string CountDisplay => Format(_countLeft);
    public string CountButtonText => _countRunning ? "Pause" : "Start";

    public void CountStartPause()
    {
        if (!_countRunning && _countLeft <= TimeSpan.Zero)
        {
            if (int.TryParse(CountdownInput, out var mins) && mins > 0)
                _countLeft = TimeSpan.FromMinutes(Math.Min(mins, 999));
        }
        _countRunning = !_countRunning;
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(CountButtonText));
    }

    public void CountReset()
    {
        _countRunning = false;
        _countLeft = TimeSpan.Zero;
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(CountButtonText));
    }

    private void TickCountdown()
    {
        if (!_countRunning) return;
        _countLeft -= _ticker.Interval;
        if (_countLeft <= TimeSpan.Zero)
        {
            _countLeft = TimeSpan.Zero;
            _countRunning = false;
            System.Media.SystemSounds.Exclamation.Play();
            OnPropertyChanged(nameof(CountButtonText));
        }
        OnPropertyChanged(nameof(CountDisplay));
    }

    // ------------------------------------------------------------- stopwatch
    private TimeSpan _watchElapsed = TimeSpan.Zero;
    private bool _watchRunning;
    public string WatchDisplay => _watchElapsed.ToString(@"hh\:mm\:ss\.f");
    public string WatchButtonText => _watchRunning ? "Pause" : "Start";

    public void WatchStartPause()
    {
        _watchRunning = !_watchRunning;
        OnPropertyChanged(nameof(WatchButtonText));
    }

    public void WatchReset()
    {
        _watchRunning = false;
        _watchElapsed = TimeSpan.Zero;
        OnPropertyChanged(nameof(WatchDisplay));
        OnPropertyChanged(nameof(WatchButtonText));
    }

    private void TickWatch()
    {
        if (!_watchRunning) return;
        _watchElapsed += _ticker.Interval;
        OnPropertyChanged(nameof(WatchDisplay));
    }

    // ------------------------------------------------------------------ tick
    private void Tick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm");
        DateText = now.ToString("dddd, d MMMM yyyy");
        TickPomo();
        TickCountdown();
        TickWatch();
    }

    private static string Format(TimeSpan t) => t.ToString(@"mm\:ss");

    // ----------------------------------------------------------------- infra
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
