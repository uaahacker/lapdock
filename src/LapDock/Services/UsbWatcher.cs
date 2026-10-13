using System.IO;
using System.Windows.Threading;
using Windows.Devices.Enumeration;

namespace LapDock.Services;

/// <summary>
/// USB devices being plugged in — a phone, a drive, a keyboard — named the way Settings → Bluetooth &amp; devices
/// names them (the device's container). Devices inside the PC belong to the computer's own container and are
/// skipped; a hub or dock reporting several interfaces at once is one message. A removable drive that mounts with
/// it is passed along, so the notch can offer to open it.
/// </summary>
public sealed class UsbWatcher : IDisposable
{
    private const string UsbDevices =
        "System.Devices.InterfaceClassGuid:=\"{A5DCBF10-6530-11D2-901F-00C04FB951ED}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
    private const string ContainerKey = "System.Devices.ContainerId";
    private static readonly Guid ThisPc = new("00000000-0000-0000-ffff-ffffffffffff");

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, DateTime> _recent = new();
    private DeviceWatcher? _watcher;
    private volatile bool _ready;

    /// <summary>UI thread: (device name, the drive it brought, if any).</summary>
    public event Action<string, DriveInfo?>? Connected;

    public UsbWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        _watcher = DeviceInformation.CreateWatcher(UsbDevices, new[] { ContainerKey });
        _watcher.Added += OnAdded;
        _watcher.Updated += (_, _) => { }; // a watcher only reports reliably with all three handlers attached
        _watcher.Removed += (_, _) => { };
        _watcher.EnumerationCompleted += (_, _) => _ready = true; // everything before this was already plugged in
        _watcher.Start();
    }

    private async void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            if (!_ready || !info.Properties.TryGetValue(ContainerKey, out var value) || value is not Guid container || container == ThisPc) return;
            var drivesBefore = RemovableDrives().Select(d => d.Name).ToHashSet(); // the letter mounts a moment after the device appears
            lock (_recent)
            {
                if (_recent.TryGetValue(container, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(10)) return;
                _recent[container] = DateTime.UtcNow;
            }
            string name;
            try { name = (await DeviceInformation.CreateFromIdAsync("{" + container + "}", null, DeviceInformationKind.DeviceContainer)).Name; }
            catch (Exception) { name = info.Name; }
            await Task.Delay(2500);
            var drive = RemovableDrives().FirstOrDefault(d => !drivesBefore.Contains(d.Name));
            _ = _dispatcher.BeginInvoke(() => Connected?.Invoke(string.IsNullOrWhiteSpace(name) ? "USB device" : name, drive));
        }
        catch (Exception) { /* a device that vanished mid-way: nothing to say */ }
    }

    private static List<DriveInfo> RemovableDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).ToList(); }
        catch (Exception) { return new(); }
    }

    public void Dispose()
    {
        try { if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop(); }
        catch (Exception) { }
    }
}
