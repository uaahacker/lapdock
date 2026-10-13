using System.Runtime.InteropServices;

namespace LapDock.Services;

/// <summary>
/// Music control via Windows media keys. Every serious player honours them —
/// Spotify, YouTube/Netflix in any browser, VLC, Media Player — so these
/// controls work everywhere with zero per-app integration.
/// (Real track info comes later via SMTC — see docs/ROADMAP.md.)
/// </summary>
public static class MediaService
{
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;

    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    private static void Tap(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
    }

    public static void PlayPause() => Tap(VK_MEDIA_PLAY_PAUSE);
    public static void Next() => Tap(VK_MEDIA_NEXT_TRACK);
    public static void Previous() => Tap(VK_MEDIA_PREV_TRACK);
    public static void VolumeUp() => Tap(VK_VOLUME_UP);
    public static void VolumeDown() => Tap(VK_VOLUME_DOWN);
    public static void Mute() => Tap(VK_VOLUME_MUTE);
}
