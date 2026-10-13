using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LapDock.Controls;
using LapDock.Services;
using LapDock.Views;
using LapDock.Views.Tabs;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LapDock;

/// <summary>
/// The expanded notch's tabs (Controls, Today, Stats, Clipboard, Shelf, Apps, Wallpaper, Power), and the
/// Settings window with its ways in: the settings hotkey, the tray icon and the right-click menu.
/// </summary>
public partial class MainWindow
{
    private SettingsWindow? _settingsWindow;
    private HotkeyService? _hotkeys;
    private int? _settingsHotkeyId, _clipboardHotkeyId;
    private Forms.NotifyIcon? _tray;
    private ClipboardService? _clipboard;
    private IntPtr _lastForeground; // the app the user was in before the notch opened: focus goes back there

    private void StartTabs()
    {
        ControlsPage.Attach(_audio, _settings.MediaControls ? _nowPlaying : null);
        TodayPage.Attach(_settings, Modal);
        TodayPage.Alert += OnTodayAlert;
        TodayPage.CountdownChanged += OnCountdownChanged;
        ShelfPage.DragOutStarted += () => SetMode(Mode.Peek); // out of the way of where the item is going
        StatsPage.Attach(_settings);
        AppsPage.Attach(_settings);
        WallpaperPage.Attach(_settings, Modal);
        PowerPage.Run += RunPower;
        AppsPage.Launched += () => SetMode(Mode.Peek); // the launched app takes the focus
        AppsPage.PasteRequested += PasteText;
        AppsPage.CloseRequested += CollapseAndRestoreFocus;
        _settingsWindow = new SettingsWindow(_settings, ApplySettings, SetHotkey);

        Try(() =>
        {
            _clipboard = new ClipboardService(this, _settings);
            ClipboardPage.Attach(_clipboard, _settings.ClipboardHotkey);
            ClipboardPage.PasteRequested += PasteClip;
            ClipboardPage.CloseRequested += CollapseAndRestoreFocus;
        });

        Try(() =>
        {
            _hotkeys = new HotkeyService(this);
            _settingsHotkeyId = _hotkeys.Register(_settings.SummonHotkey, ToggleSettings);
            _clipboardHotkeyId = _hotkeys.Register(_settings.ClipboardHotkey, ToggleClipboard);
            var taken = new[] { (_settingsHotkeyId, _settings.SummonHotkey), (_clipboardHotkeyId, _settings.ClipboardHotkey) }
                .Where(h => h.Item1 is null).Select(h => h.Item2).ToList();
            if (taken.Count > 0)
                Notify("hotkey", "settings", "Shortcut unavailable", $"{string.Join(", ", taken)}: taken or invalid. Change it in Settings.");
        });

        Try(() =>
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Settings", null, (_, _) => ShowSettings());
            menu.Items.Add("Quit LapDock", null, (_, _) => Close());
            _tray = new Forms.NotifyIcon
            {
                Icon = AppIcon(),
                ContextMenuStrip = menu,
                Visible = true,
            };
            UpdateTrayText();
            _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowSettings(); };
        });
    }

    private void StopTabs()
    {
        _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); } // else its icon lingers until hovered
        ControlsPage.Dispose();
        _clipboard?.Dispose();
        StatsPage.Dispose();
        _settingsWindow?.Exit();
    }

    // ------------------------------------------------------------------ tabs
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return; // a list inside a tab changed its selection — not ours
        RefreshTab();
        UpdateActiveTab();
        if (_mode == Mode.Expanded) ResizePill();
        // Picked a search tab by clicking (which activated the notch): ready to type.
        if (IsActive && Tabs.SelectedItem == AppsTabItem) AppsPage.FocusSearch();
        else if (IsActive && Tabs.SelectedItem == ClipboardTabItem) ClipboardPage.FocusSearch();
    }

    private void OnTabsModeChanged()
    {
        if (_mode == Mode.Expanded)
        {
            var foreground = GetForegroundWindow();
            if (foreground != new WindowInteropHelper(this).Handle) _lastForeground = foreground;
            RefreshTab();
        }
        UpdateActiveTab();
    }

    /// <summary>Live tabs (Stats) only sample while the notch is open on them.</summary>
    private void UpdateActiveTab() => StatsPage.Active = _mode == Mode.Expanded && Tabs.SelectedItem == StatsTabItem;

    /// <summary>Brings the visible tab up to date (on hover-open and on tab switch).</summary>
    private void RefreshTab()
    {
        if (Tabs.SelectedItem == WallpaperTabItem) WallpaperPage.Refresh();
        else if (Tabs.SelectedItem == TodayTabItem) TodayPage.Refresh();
        else if (Tabs.SelectedItem == AppsTabItem) AppsPage.Refresh();
        else if (Tabs.SelectedItem == PowerTabItem) PowerPage.Refresh();
        else if (Tabs.SelectedItem == ShelfTabItem) ShelfPage.Refresh();
        else if (Tabs.SelectedIndex == 0) ControlsPage.Refresh();
    }

    /// <summary>Timer finished / reminder due: in the notch, and as a Windows notification (via the tray icon).</summary>
    private void OnTodayAlert(string title, string detail)
    {
        if (!TodayPage.Focusing && _heldNotices > 0) // a focus session just ended: what came in meanwhile
        {
            detail += _heldNotices == 1 ? " · 1 notification came in" : $" · {_heldNotices} notifications came in";
            _heldNotices = 0;
        }
        Notify("today", TodayPage.Countdown?.Icon ?? "timer", title, detail, accent: Res("AccentBrush"), seconds: 5);
        _tray?.ShowBalloonTip(8000, title, detail, Forms.ToolTipIcon.None);
    }

    // ----------------------------------------------------------------- power
    /// <summary>Close the notch first (so it isn't hanging open on wake / sign-in), then act.</summary>
    private async void RunPower(PowerAction action)
    {
        SetMode(Mode.Peek);
        await Task.Delay(250); // let the notch finish closing
        var hwnd = new WindowInteropHelper(this).Handle;
        bool ok = action switch
        {
            PowerAction.Lock => PowerService.Lock(),
            PowerAction.Sleep => PowerService.Sleep(hwnd),
            PowerAction.Restart => PowerService.Restart(),
            _ => PowerService.ShutDown(),
        };
        if (!ok) PowerPage.ShowStatus($"Windows didn't allow that ({action}). It may be blocked by a policy on this PC.", problem: true);
    }

    // ------------------------------------------------------------- clipboard
    /// <summary>The clipboard shortcut: open the notch on Clipboard, ready to type — or close it again.</summary>
    private void ToggleClipboard()
    {
        if (_mode == Mode.Expanded && Tabs.SelectedItem == ClipboardTabItem)
        {
            CollapseAndRestoreFocus();
            return;
        }
        _collapseTimer.Stop();
        Tabs.SelectedItem = ClipboardTabItem;
        SetMode(Mode.Expanded);
        Activate(); // a hotkey press lets us take the foreground; Deactivated (clicking away) closes it again
        ClipboardPage.FocusSearch();
    }

    /// <summary>Enter on an item: copy it, hand focus back to the previous app and paste there (like Win+V).</summary>
    private async void PasteClip(ClipItem item)
    {
        _clipboard?.Copy(item);
        CollapseAndRestoreFocus();
        await Task.Delay(120); // let the target window take focus first
        if (GetForegroundWindow() != new WindowInteropHelper(this).Handle) ClipboardService.SendPaste();
    }

    /// <summary>A snippet: same as pasting a clipboard item.</summary>
    private void PasteText(string text) => PasteClip(new ClipItem { Text = text });

    private void CollapseAndRestoreFocus()
    {
        Keyboard.ClearFocus();
        SetMode(Mode.Peek);
        if (_lastForeground != IntPtr.Zero && IsWindow(_lastForeground)) SetForegroundWindow(_lastForeground);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    // -------------------------------------------------------------- settings
    private void ToggleSettings() => _settingsWindow?.Toggle(ScreenService.ScreenOf(this));

    /// <summary>Also called when LapDock is launched a second time.</summary>
    internal void ShowSettings()
    {
        SetMode(Mode.Peek);
        _settingsWindow?.Summon(ScreenService.ScreenOf(this));
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ApplySettings()
    {
        _ = StartNoticesAsync(); // switched on in Settings
        ApplyNotchSpace();
        PlaceOnScreen();
        UpdateClock();
        RefreshAll();
    }

    /// <summary>Settings → a new shortcut. The old one keeps working unless the new one registers.</summary>
    private bool SetHotkey(HotkeyKind kind, string gesture)
    {
        if (_hotkeys is null) return false;
        bool clipboard = kind == HotkeyKind.Clipboard;
        if (_hotkeys.Register(gesture, clipboard ? ToggleClipboard : ToggleSettings) is not { } id) return false;
        if (clipboard)
        {
            _hotkeys.Unregister(_clipboardHotkeyId);
            _clipboardHotkeyId = id;
            _settings.ClipboardHotkey = gesture;
            ClipboardPage.SetHotkey(gesture);
        }
        else
        {
            _hotkeys.Unregister(_settingsHotkeyId);
            _settingsHotkeyId = id;
            _settings.SummonHotkey = gesture;
            UpdateTrayText();
        }
        return true;
    }

    /// <summary>LapDock's own icon at the tray's size for this DPI (the .ico carries 16–256 px).</summary>
    private static Drawing.Icon AppIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/LapDock;component/Assets/LapDock.ico")).Stream;
            return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception) { return Drawing.SystemIcons.Application; }
    }

    private void UpdateTrayText()
    {
        if (_tray is not null) _tray.Text = $"LapDock — Settings: {_settings.SummonHotkey}";
    }

    /// <summary>Nova lives in the island: dancing while music plays (still while a fullscreen app is up), otherwise awake and blinking.</summary>
    private void UpdateNova() =>
        PeekNova.SetMood(_settings.MediaControls && _nowPlaying.IsPlaying && !Away ? NovaMood.Dancing : NovaMood.Awake);
}
