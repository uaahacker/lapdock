using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LapDock.Services;

/// <summary>
/// System-wide hotkeys (RegisterHotKey) delivered as WM_HOTKEY to the owner window.
/// Gestures are WPF strings like "Ctrl+Alt+Space" — WPF's ModifierKeys flags have the same
/// values as Win32's MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN, so they pass straight through.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyService(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(Hook);
    }

    /// <summary>Returns the hotkey's id, or null: not a valid gesture, or another app already owns it.</summary>
    public int? Register(string gesture, Action action)
    {
        KeyGesture g;
        try { g = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(gesture)!; }
        catch (Exception) { return null; }

        int id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, (uint)g.Modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(g.Key)))
            return null;
        _actions[id] = action;
        return id;
    }

    public void Unregister(int? id)
    {
        if (id is not { } i || !_actions.Remove(i)) return;
        UnregisterHotKey(_hwnd, i);
    }

    /// <summary>Releases every hotkey.</summary>
    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
