using System.IO;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace LapDock.Services;

/// <summary>Another app's Windows notification: "Ali: are you coming?" from WhatsApp.</summary>
/// <param name="AppId">AppUserModelID: opens the app through shell:AppsFolder and gives its icon.</param>
/// <param name="Count">How many arrived from this app at once (shown as "3 new").</param>
public sealed record AppNotice(string App, string AppId, string Title, string Body, int Count);

/// <summary>
/// Other apps' notifications (Teams, WhatsApp, Discord, Outlook…), for the notch to show as a banner.
/// Read with UserNotificationListener — the user can revoke it in Settings → Privacy → Notifications.
/// Its change event isn't available to desktop apps, and reading the list costs ~30 ms of CPU, so it isn't
/// polled: Windows writes its notification database the moment a notification arrives, and a once-a-second
/// look at that file's timestamp (practically free) says when to read. The list is compared by id; what was
/// already there when LapDock started is never announced.
/// </summary>
public sealed class AppNotificationService : IDisposable
{
    private static readonly string Database = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db-wal");

    private readonly DispatcherTimer _check = new() { Interval = TimeSpan.FromSeconds(1) };
    private UserNotificationListener? _listener;
    private HashSet<uint>? _seen;
    private string? _stamp;
    private DateTime _readAt;
    private bool _busy;

    /// <summary>UI thread.</summary>
    public event Action<AppNotice>? Arrived;

    /// <summary>false = Windows said no (access turned off): nothing will arrive.</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            if (await _listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed) return false;
        }
        catch (Exception) { return false; } // older Windows, or the platform isn't there
        _check.Tick += async (_, _) => await CheckAsync();
        _stamp = Stamp();
        await PollAsync();
        _check.Start();
        return true;
    }

    /// <summary>
    /// Reads the list when the database changed — and anyway once a minute (every 5 s if that file isn't there),
    /// in case a future Windows keeps it somewhere else.
    /// </summary>
    private async Task CheckAsync()
    {
        var stamp = Stamp();
        if (stamp is not null && stamp == _stamp && DateTime.UtcNow - _readAt < TimeSpan.FromMinutes(1)) return;
        if (stamp is null && DateTime.UtcNow - _readAt < TimeSpan.FromSeconds(5)) return;
        _stamp = stamp;
        await PollAsync();
    }

    private static string? Stamp()
    {
        try
        {
            var file = new FileInfo(Database);
            return file.Exists ? $"{file.LastWriteTimeUtc.Ticks}/{file.Length}" : null;
        }
        catch (Exception) { return null; }
    }

    private async Task PollAsync()
    {
        if (_busy || _listener is null) return;
        _busy = true;
        _readAt = DateTime.UtcNow;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (_seen is not null)
                foreach (var app in list.Where(n => !_seen.Contains(n.Id)).GroupBy(n => AppId(n)))
                    if (Read(app.MaxBy(n => n.CreationTime)!, app.Count()) is { } notice) Arrived?.Invoke(notice);
            _seen = list.Select(n => n.Id).ToHashSet();
        }
        catch (Exception) { /* access revoked, or the platform hiccupped: try again next time */ }
        finally { _busy = false; }
    }

    private static string AppId(UserNotification n)
    {
        try { return n.AppInfo?.AppUserModelId ?? ""; } catch (Exception) { return ""; }
    }

    private static AppNotice? Read(UserNotification n, int count)
    {
        try
        {
            var app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            if (app.Contains("LapDock", StringComparison.OrdinalIgnoreCase)) return null; // our own reminders are already in the notch
            var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                .Select(t => t.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Cast<string>().ToList();
            if (texts is not { Count: > 0 }) return null;
            return Make(app, AppId(n), texts, count);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The first text is the title (sender, subject…); the rest is the message.</summary>
    public static AppNotice Make(string app, string appId, IReadOnlyList<string> texts, int count) =>
        new(app, appId, texts[0], string.Join(" ", texts.Skip(1)).ReplaceLineEndings(" "), count);

    public void Dispose() => _check.Stop();
}
