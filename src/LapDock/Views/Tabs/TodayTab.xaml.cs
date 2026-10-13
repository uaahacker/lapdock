using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LapDock.Models;
using LapDock.Services;
using Microsoft.Win32;

namespace LapDock.Views.Tabs;

/// <summary>
/// Today: big time + date range + weather, this week's strip with event dots and the next few
/// events, a quick ring timer (1/5/10/25 min) and to-dos with "@18:30" reminders.
/// </summary>
public partial class TodayTab : UserControl
{
    private static readonly int[] PresetMinutes = { 1, 5, 10, 25 };

    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly TodoService _todos = new();
    private AppSettings? _settings;
    private Func<Func<bool?>, bool?>? _modal;
    private DateTime _weatherAt = DateTime.MinValue;
    private (double, double) _weatherFor;

    // Timer: counts down to an end time, so a late tick can never make it drift.
    private TimeSpan _total = TimeSpan.FromMinutes(1), _left = TimeSpan.FromMinutes(1);
    private DateTime? _endsAtUtc;

    /// <summary>Timer finished or reminder due: (title, detail). The notch shows it and raises a Windows notification.</summary>
    public event Action<string, string>? Alert;

    public TodayTab()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => TickClock();
        _ticker.Tick += (_, _) => TickTimer();
        _todos.ReminderDue += item => Alert?.Invoke("Reminder", item.Text);
        BuildPresets();
        UpdateTimer();
        RenderTodos();
        // Clock, week and weather only refresh while the tab is on screen.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _clock.Stop(); return; }
            TickClock();
            _ = RefreshWeatherAsync();
        };
    }

    /// <param name="modal">Runs a dialog without the notch collapsing underneath it.</param>
    public void Attach(AppSettings settings, Func<Func<bool?>, bool?> modal)
    {
        _settings = settings;
        _modal = modal;
    }

    /// <summary>The notch opened on this tab.</summary>
    public void Refresh()
    {
        TickClock();
        _ = RefreshWeatherAsync();
    }

    // ------------------------------------------------------------ date, week
    private void TickClock()
    {
        var now = DateTime.Now;
        bool twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
        BigTime.Text = now.ToString(twelveHour ? "h:mm" : "HH:mm");
        DayLine.Text = twelveHour ? $"{now:tt} • {now:ddd d}" : $"{now:ddd d MMMM}";
        RenderWeek(now);
        // Wake right after the minute turns over, not every second.
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second + 0.05);
        _clock.Start();
    }

    private void RenderWeek(DateTime now, string? status = null)
    {
        var today = now.Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var sunday = monday.AddDays(6);
        RangeText.Text = monday.Month == sunday.Month ? $"{monday:MMM d} – {sunday:%d}" : $"{monday:MMM d} – {sunday:MMM d}";

        var events = CalendarService.Load();
        WeekStrip.Children.Clear();
        for (int i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            bool isToday = day == today;
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(new TextBlock
            {
                Text = day.ToString("ddd")[..2], FontSize = 10.5, Foreground = Res("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            cell.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 3, 0, 3),
                Background = isToday ? Res("AccentBrush") : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = day.Day.ToString(), FontSize = 12.5, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isToday ? Brushes.Black : Res("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
            cell.Children.Add(new Border
            {
                Width = 4, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center,
                Background = events.Any(e => e.Start.Date == day) ? Res("AccentBrush") : Brushes.Transparent,
            });
            WeekStrip.Children.Add(cell);
        }

        Agenda.Children.Clear();
        var upcoming = events
            .Where(e => (e.AllDay ? e.Start.Date >= today : e.Start >= now) && e.Start < today.AddDays(7))
            .OrderBy(e => e.Start).Take(3).ToList();
        if (upcoming.Count == 0)
        {
            Agenda.Children.Add(new TextBlock { Text = "Nothing coming up", FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            Agenda.Children.Add(new TextBlock { Text = "Your calendar is clear", FontSize = 11.5, Foreground = Res("MutedBrush") });
        }
        foreach (var e in upcoming)
        {
            var when = $"{e.Start:ddd d} · {(e.AllDay ? "All day" : e.Start.ToString("t"))}  ";
            var line = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 2), ToolTip = e.Title };
            line.Inlines.Add(new Run(when) { Foreground = Res("MutedBrush") });
            line.Inlines.Add(new Run(e.Title));
            Agenda.Children.Add(line);
        }
        if (status is not null)
            Agenda.Children.Add(new TextBlock { Text = status, FontSize = 11.5, Foreground = Res("AccentBrush"), Margin = new Thickness(0, 4, 0, 0) });
    }

    private async Task RefreshWeatherAsync()
    {
        if (_settings is null) return;
        var place = (_settings.Latitude, _settings.Longitude);
        if (place == _weatherFor && DateTime.Now - _weatherAt < TimeSpan.FromMinutes(15)) return; // fresh, same city
        var w = await WeatherService.GetCurrentAsync(_settings.Latitude, _settings.Longitude);
        if (w is { } weather)
        {
            _weatherAt = DateTime.Now;
            _weatherFor = place;
            WeatherLine.Text = $"{Math.Round(weather.TempC)}°C {weather.Summary} · {_settings.CityName}";
        }
        else if (WeatherLine.Text.Length == 0) WeatherLine.Text = "Weather unavailable";
        WeatherRow.Visibility = Visibility.Visible; // the icon only appears with its text
    }

    private void ImportIcs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import a calendar", Filter = "Calendar files|*.ics|All files|*.*" };
        bool? ok = _modal is null ? dialog.ShowDialog() : _modal(() => dialog.ShowDialog(Window.GetWindow(this)));
        if (ok != true) return;
        string status;
        try
        {
            int added = CalendarService.Import(dialog.FileName);
            status = added == 1 ? "Imported 1 event" : $"Imported {added} events";
        }
        catch (Exception ex) { status = $"Couldn't import: {ex.Message}"; }
        RenderWeek(DateTime.Now, status);
    }

    // ----------------------------------------------------------------- timer
    private void BuildPresets()
    {
        foreach (var minutes in PresetMinutes)
        {
            var pill = new Button { Content = $"{minutes}m", Style = (Style)FindResource("OptionPill"), Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 3, 8, 3) };
            pill.Click += (_, _) =>
            {
                StopTimer();
                _total = _left = TimeSpan.FromMinutes(minutes);
                UpdateTimer();
            };
            Presets.Children.Add(pill);
        }
    }

    private void StartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_endsAtUtc is { } end)
        {
            _left = end - DateTime.UtcNow;
            StopTimer();
        }
        else
        {
            _endsAtUtc = DateTime.UtcNow + _left;
            _ticker.Start();
        }
        UpdateTimer();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();
        _left = _total;
        UpdateTimer();
    }

    private void StopTimer()
    {
        _ticker.Stop();
        _endsAtUtc = null;
    }

    private void TickTimer()
    {
        if (_endsAtUtc is not { } end) return;
        _left = end - DateTime.UtcNow;
        if (_left <= TimeSpan.Zero)
        {
            StopTimer();
            _left = _total;
            Alert?.Invoke("Timer done", $"{_total.TotalMinutes:0} min timer finished");
        }
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, _left.TotalSeconds)); // 00:01 until it is really over
        TimerText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        TimerRing.Progress = _left.TotalSeconds / _total.TotalSeconds;
        StartButton.Content = _endsAtUtc is not null ? "Pause" : _left < _total ? "Resume" : "Start";
        foreach (Button pill in Presets.Children)
            pill.Tag = (string)pill.Content == $"{_total.TotalMinutes:0}m" ? "on" : null;
    }

    // ----------------------------------------------------------------- to-dos
    private void RenderTodos()
    {
        TodoList.Children.Clear();
        foreach (var item in _todos.Items.OrderBy(i => i.Done).ThenBy(i => i.Created))
        {
            var check = new CheckBox { Style = (Style)FindResource("TodoCheck"), IsChecked = item.Done, Margin = new Thickness(0, 0, 8, 0) };
            check.Click += (_, _) => { _todos.SetDone(item, check.IsChecked == true); RenderTodos(); };
            var remove = new Button { Content = "", Style = (Style)FindResource("ChromeButton"), Width = 22, Height = 22, FontSize = 9, ToolTip = "Delete" };
            remove.Click += (_, _) => { _todos.Remove(item); RenderTodos(); };

            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            DockPanel.SetDock(check, Dock.Left);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(check);
            row.Children.Add(remove);
            if (item.Due is { } due)
            {
                var when = new TextBlock
                {
                    Text = due.Date == DateTime.Today ? due.ToString("t") : due.ToString("ddd t"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = item.Reminded || item.Done ? Res("MutedBrush") : Res("AccentBrush"), Margin = new Thickness(6, 0, 2, 0),
                };
                DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(when);
            }
            row.Children.Add(new TextBlock
            {
                Text = item.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = item.Text, Foreground = item.Done ? Res("MutedBrush") : Res("TextBrush"),
                TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
            });
            TodoList.Children.Add(row);
        }
        if (_todos.Items.Count == 0)
            TodoList.Children.Add(new TextBlock
            {
                Text = "Nothing on your list. Type below and press Enter, or add \"@18:30\" to be reminded.",
                FontSize = 12, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
        TodoCount.Text = $"{_todos.Items.Count(i => i.Done)}/{_todos.Items.Count}";
    }

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_todos.Add(TodoInput.Text) is not null) RenderTodos();
            TodoInput.Clear();
        }
        else if (e.Key == Key.Escape)
        {
            TodoInput.Clear();
            Keyboard.ClearFocus(); // lets the notch close again
        }
    }

    private void TodoInput_Changed(object sender, RoutedEventArgs e) =>
        TodoHint.Visibility = TodoInput.Text.Length == 0 && !TodoInput.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Starts a to-do with " @next-hour" filled in; type the text in front of it.</summary>
    private void NewReminder_Click(object sender, RoutedEventArgs e)
    {
        TodoInput.Text = $" @{DateTime.Now.AddHours(1):HH}:00";
        TodoInput.Focus();
        TodoInput.CaretIndex = 0;
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
