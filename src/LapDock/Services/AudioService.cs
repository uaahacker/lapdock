using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// System master volume + mute through Windows Core Audio (IAudioEndpointVolume) on the
/// default output device. Change notifications are pushed by Windows — no polling — and
/// the service re-attaches when the default output device changes (e.g. headphones).
/// </summary>
public sealed class AudioService : IDisposable
{
    // Tags our own changes, so the notch can tell "user moved our slider" from "volume keys".
    private static readonly Guid OurContext = Guid.NewGuid();

    private readonly Dispatcher _dispatcher;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceCallback _deviceCallback;

    public bool Available => _endpoint is not null;
    /// <summary>0..1</summary>
    public float Volume { get; private set; }
    public bool Muted { get; private set; }

    /// <summary>UI thread. true = changed outside LapDock (keyboard keys, other apps).</summary>
    public event Action<bool>? Changed;

    public AudioService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _volumeCallback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
        }
        catch (Exception) { _enumerator = null; }
        Attach();
    }

    private void Attach()
    {
        Detach();
        if (_enumerator is null) return;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var obj);
            Marshal.ReleaseComObject(device);
            _endpoint = (IAudioEndpointVolume)obj;
            _endpoint.RegisterControlChangeNotify(_volumeCallback);
            _endpoint.GetMasterVolumeLevelScalar(out var v);
            _endpoint.GetMute(out var m);
            Volume = v;
            Muted = m;
        }
        catch (Exception) { _endpoint = null; } // no output device at all
        Changed?.Invoke(false);
    }

    private void Detach()
    {
        if (_endpoint is null) return;
        try { _endpoint.UnregisterControlChangeNotify(_volumeCallback); } catch (Exception) { }
        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    public void SetVolume(float level)
    {
        if (_endpoint is null) return;
        var ctx = OurContext;
        try
        {
            _endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx);
            if (Muted && level > 0) _endpoint.SetMute(false, ref ctx); // dragging up unmutes, like Windows
        }
        catch (Exception) { }
    }

    public void ToggleMute()
    {
        if (_endpoint is null) return;
        var ctx = OurContext;
        try { _endpoint.SetMute(!Muted, ref ctx); } catch (Exception) { }
    }

    // Called on a Core Audio worker thread.
    private void OnNotify(IntPtr data)
    {
        var n = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(data);
        bool external = n.guidEventContext != OurContext;
        _dispatcher.BeginInvoke(() =>
        {
            Volume = n.fMasterVolume;
            Muted = n.bMuted != 0;
            Changed?.Invoke(external);
        });
    }

    public void Dispose()
    {
        Detach();
        if (_enumerator is null) return;
        try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback); } catch (Exception) { }
        Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    // ------------------------------------------------------------------ interop
    private sealed class VolumeCallback(AudioService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr pNotify) { owner.OnNotify(pNotify); return 0; }
    }

    private sealed class DeviceCallback(AudioService owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? id)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia) owner._dispatcher.BeginInvoke(owner.Attach);
            return 0;
        }
        public int OnDeviceStateChanged(string id, uint state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnPropertyValueChanged(string id, PROPERTYKEY key) => 0;
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIO_VOLUME_NOTIFICATION_DATA { public Guid guidEventContext; public int bMuted; public float fMasterVolume; public uint nChannels; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr pNotify);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
    }
}
