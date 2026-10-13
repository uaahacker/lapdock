using System.ComponentModel;
using System.Diagnostics;
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

    // Timers advance by REAL elapsed time, not by the nominal tick interval:
    // a DispatcherTimer fires late whenever the UI is busy, which made them drift.
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private TimeSpan _dt;

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

    public string PomoPhase => _pomoIsFocus ? "Focus session" : "Break — stretch, breathe";
    public string PomoDisplay => Format(_pomoLeft);
    public string PomoButtonText => _pomoRunning ? "Pause" : (_pomoLeft.TotalSeconds < (_pomoIsFocus ? FocusMinutes : BreakMinutes) * 60 ? "Resume" : "Start focus");
    public double PomoProgress => 1 - _pomoLeft.TotalSeconds / ((_pomoIsFocus ? FocusMinutes : BreakMinutes) * 60.0);
    public string PomoDoneText => _pomoDone == 0 ? "" : $"{_pomoDone} session{(_pomoDone > 1 ? "s" : "")} completed";
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
        _pomoLeft -= _dt;
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
    private bool _countFresh = true; // not started since the last reset/finish → follows the input box
    private string _countdownInput = "10";

    /// <summary>Minutes, typed by the user. While idle the display follows it live.</summary>
    public string CountdownInput
    {
        get => _countdownInput;
        set
        {
            _countdownInput = value;
            if (_countFresh) LoadCountdownInput();
        }
    }
    public string CountDisplay => Format(_countLeft);
    public string CountButtonText => _countRunning ? "Pause" : _countFresh ? "Start" : "Resume";

    public event Action? CountdownFinished;

    public void CountStartPause()
    {
        if (_countFresh) LoadCountdownInput();
        if (_countLeft <= TimeSpan.Zero) return; // nothing valid typed
        _countFresh = false;
        _countRunning = !_countRunning;
        RaiseCount();
    }

    public void CountReset()
    {
        _countRunning = false;
        _countFresh = true;
        LoadCountdownInput();
    }

    private void LoadCountdownInput()
    {
        _countLeft = int.TryParse(CountdownInput, out var mins) && mins > 0
            ? TimeSpan.FromMinutes(Math.Min(mins, 999))
            : TimeSpan.Zero;
        RaiseCount();
    }

    private void RaiseCount()
    {
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(CountButtonText));
    }

    private void TickCountdown()
    {
        if (!_countRunning) return;
        _countLeft -= _dt;
        if (_countLeft <= TimeSpan.Zero)
        {
            _countLeft = TimeSpan.Zero;
            _countRunning = false;
            _countFresh = true; // next Start re-reads the input
            System.Media.SystemSounds.Exclamation.Play();
            OnPropertyChanged(nameof(CountButtonText));
            CountdownFinished?.Invoke();
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
        _watchElapsed += _dt;
        OnPropertyChanged(nameof(WatchDisplay));
    }

    // ------------------------------------------------------------------ tick
    private void Tick()
    {
        var elapsed = _clock.Elapsed;
        _dt = elapsed - _lastTick;
        _lastTick = elapsed;

        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm");
        DateText = now.ToString("dddd, d MMMM yyyy");
        TickPomo();
        TickCountdown();
        TickWatch();
    }

    // "mm" is only the minutes COMPONENT — a 90-minute countdown used to show 30:00.
    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

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
