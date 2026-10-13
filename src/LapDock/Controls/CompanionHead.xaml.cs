using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LapDock.Controls;

public enum NovaMood { Awake, Sleepy, Happy, Dancing, Focused }

/// <summary>
/// Nova — the animated companion head.
///
/// LookAt() is fed the GLOBAL cursor position (physical screen pixels) by
/// MainWindow via CursorService. Nova moves its pupils toward the cursor,
/// tilts and leans a little, blinks on a random timer, gets sleepy when the
/// cursor has been still for a while, and bounces when poked or when music
/// is playing (Dancing mood).
/// </summary>
public partial class CompanionHead : UserControl
{
    private readonly DispatcherTimer _blinkTimer = new();
    private readonly DispatcherTimer _danceTimer = new();
    private readonly DispatcherTimer _reopenTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private bool _paused;
    private readonly Random _rng = new();

    private double _pupilX, _pupilY;       // current (smoothed) pupil offset
    private double _pupilTX, _pupilTY;     // target pupil offset
    private double _tilt, _tiltT;          // head tilt, current + target
    private DateTime _happyUntilUtc = DateTime.MinValue;
    private int _danceTick;

    public NovaMood BaseMood { get; set; } = NovaMood.Awake;

    /// <summary>Effective mood: a poke (Happy) temporarily overrides the base mood.</summary>
    public NovaMood Mood => DateTime.UtcNow < _happyUntilUtc ? NovaMood.Happy : BaseMood;

    public CompanionHead()
    {
        InitializeComponent();

        _blinkTimer.Tick += (_, _) => Blink();
        _reopenTimer.Tick += (_, _) => { _reopenTimer.Stop(); ApplyMood(); }; // eyes open again
        ScheduleNextBlink();
        _blinkTimer.Start();

        _danceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _danceTimer.Tick += (_, _) =>
        {
            if (Mood != NovaMood.Dancing) return;
            _danceTick++;
            HeadLean.Y = -Math.Abs(Math.Sin(_danceTick * 0.9)) * 10; // bounce
            HeadTilt.Angle = Math.Sin(_danceTick * 0.9) * 4;
        };
        // (started by SetMood(Dancing) only: nothing ticks while Nova sits still)

        MouseLeftButtonUp += (_, _) => Poke();
        Loaded += (_, _) => ApplyMood();
    }

    // ------------------------------------------------------------------ gaze

    /// <summary>Feed the global cursor position (physical pixels) here, ~60x/sec.</summary>
    public void LookAt(Point cursorScreenPos, double cursorIdleSeconds)
    {
        if (!IsLoaded || ActualWidth == 0) return;

        // Our centre on the physical screen.
        var centre = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        double dx = cursorScreenPos.X - centre.X;
        double dy = cursorScreenPos.Y - centre.Y;
        double dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double nx = dx / dist, ny = dy / dist;

        // Pupils: max ~9px travel inside the eye.
        _pupilTX = nx * 9;
        _pupilTY = ny * 10;
        _tiltT = Math.Clamp(nx * 7, -7, 7);

        // Smooth (exponential lerp — runs at cursor rate, feels alive, never snappy).
        _pupilX += (_pupilTX - _pupilX) * 0.30;
        _pupilY += (_pupilTY - _pupilY) * 0.30;
        _tilt += (_tiltT - _tilt) * 0.12;

        PupilL.X = PupilR.X = _pupilX;
        PupilL.Y = PupilR.Y = _pupilY;
        SparkL.X = SparkR.X = _pupilX;
        SparkL.Y = SparkR.Y = _pupilY;

        if (Mood != NovaMood.Dancing)
        {
            HeadTilt.Angle = _tilt;
            HeadLean.X = nx * 5;
            HeadLean.Y = Math.Clamp(ny * 4, -4, 6);
        }

        // Sleepiness is driven by cursor stillness.
        var shouldBeSleepy = cursorIdleSeconds > 12;
        if (shouldBeSleepy && BaseMood == NovaMood.Awake) { BaseMood = NovaMood.Sleepy; ApplyMood(); }
        else if (!shouldBeSleepy && BaseMood == NovaMood.Sleepy) { BaseMood = NovaMood.Awake; ApplyMood(); }
    }

    // ----------------------------------------------------------------- moods

    /// <summary>
    /// Nothing moves while Nova can't be seen (notch open, or over a fullscreen app): every repaint of the
    /// notch's transparent window costs CPU, so a hidden blink or dance is pure waste.
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            _blinkTimer.IsEnabled = !value;
            _danceTimer.IsEnabled = !value && BaseMood == NovaMood.Dancing;
        }
    }

    public void SetMood(NovaMood mood)
    {
        BaseMood = mood;
        _danceTimer.IsEnabled = mood == NovaMood.Dancing && !_paused;
        if (mood != NovaMood.Dancing) { HeadLean.Y = 0; HeadTilt.Angle = 0; } // settle after dancing
        ApplyMood();
    }

    private void ApplyMood()
    {
        var mood = Mood;
        ZzzText.Opacity = mood == NovaMood.Sleepy ? 0.9 : 0;

        // Eyes: sleepy = half-mast; otherwise fully open (blink handles the rest).
        double eyeOpen = mood == NovaMood.Sleepy ? 0.45 : 1.0;
        EyeLScale.ScaleY = EyeRScale.ScaleY = eyeOpen;

        // Brows: sleepy droop, happy lift, focused angle-in.
        (BrowLRot.Angle, BrowRRot.Angle) = mood switch
        {
            NovaMood.Sleepy => (14d, -14d),
            NovaMood.Focused => (-16d, 16d),
            _ => (0d, 0d)
        };
        BrowL.Margin = BrowR.Margin = new Thickness(0, mood == NovaMood.Happy ? -4 : 0, 0, 0);

        // Mouth per mood.
        MouthPath.Data = Geometry.Parse(mood switch
        {
            NovaMood.Happy => "M0,0 Q20,20 40,0",     // big smile
            NovaMood.Sleepy => "M8,4 Q20,0 32,4",     // tiny flat yawn line
            NovaMood.Focused => "M6,2 L34,2",         // determined line
            NovaMood.Dancing => "M2,0 Q20,22 38,0",  // grin
            _ => "M0,0 Q20,14 40,0"                   // gentle smile
        });
    }

    /// <summary>Click Nova: it notices you.</summary>
    public void Poke()
    {
        _happyUntilUtc = DateTime.UtcNow.AddSeconds(1.4);
        ApplyMood();

        var bounce = new DoubleAnimation(0, -16, new Duration(TimeSpan.FromMilliseconds(160)))
        {
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        HeadLean.BeginAnimation(TranslateTransform.YProperty, bounce);

        var revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        revert.Tick += (_, _) => { revert.Stop(); ApplyMood(); };
        revert.Start();
    }

    // ----------------------------------------------------------------- blink

    private void ScheduleNextBlink()
    {
        // Now and then, not every few seconds: each blink repaints the notch's transparent window, which was
        // most of LapDock's idle CPU. An occasional blink still reads as alive.
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_rng.Next(20_000, 40_000));
    }

    /// <summary>
    /// Eyes shut, then open again 110 ms later: two repaints. (A smooth 15-frame blink looked the same at
    /// notch size and was most of LapDock's idle CPU.)
    /// </summary>
    private void Blink()
    {
        ScheduleNextBlink();
        if (Mood == NovaMood.Sleepy || _paused) return; // already half closed / nobody can see it
        EyeLScale.ScaleY = EyeRScale.ScaleY = 0.08;
        _reopenTimer.Start();
    }
}
