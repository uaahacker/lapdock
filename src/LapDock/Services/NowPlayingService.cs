using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LapDock.Services;

/// <summary>
/// Observes Windows' current media session. All observable state and subscriptions
/// are owned by the WPF dispatcher; stale asynchronous artwork reads are discarded.
/// </summary>
public sealed class NowPlayingService : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _positionTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private Task? _initialization;
    private bool _disposed;
    private int _metadataRevision;
    private string _title = "Nothing playing";
    private string _artist = "Media keys still work";
    private string _album = "";
    private BitmapImage? _artwork;
    private bool _hasSession;
    private bool _isPlaying;
    private bool _canSeek;
    private bool _supportsSeeking;
    private TimeSpan _position;
    private TimeSpan _duration;
    private TimeSpan _timelineStart;
    private TimeSpan _timelinePosition;
    private TimeSpan _minimumSeek;
    private TimeSpan _maximumSeek;
    private DateTimeOffset _timelineUpdatedAt;
    private double _playbackRate = 1;

    public NowPlayingService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionTimer.Tick += OnPositionTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public string Album { get => _album; private set => Set(ref _album, value); }
    public BitmapImage? Artwork { get => _artwork; private set => Set(ref _artwork, value); }
    public bool HasSession { get => _hasSession; private set => Set(ref _hasSession, value); }
    public bool IsPlaying
    {
        get => _isPlaying;
        // The position only moves while playing, so only tick then (LapDock runs all day).
        private set { if (Set(ref _isPlaying, value)) _positionTimer.IsEnabled = value && !_disposed; }
    }
    public bool CanSeek { get => _canSeek; private set => Set(ref _canSeek, value); }
    public TimeSpan Position
    {
        get => _position;
        private set
        {
            if (!Set(ref _position, value)) return;
            Notify(nameof(PositionSeconds));
            Notify(nameof(PositionText));
        }
    }
    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (!Set(ref _duration, value)) return;
            Notify(nameof(DurationSeconds));
            Notify(nameof(DurationText));
        }
    }
    public double PositionSeconds => Position.TotalSeconds;
    public double DurationSeconds => Duration.TotalSeconds;
    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);

    public Task InitializeAsync() => OnDispatcherAsync(() => _initialization ??= InitializeCoreAsync());
    public Task TogglePlayPauseAsync() => TransportAsync(async session =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await session.TryPauseAsync()
            : await session.TryPlayAsync(), MediaService.PlayPause);
    public Task NextAsync() => TransportAsync(async session => await session.TrySkipNextAsync(), MediaService.Next);
    public Task PreviousAsync() => TransportAsync(async session => await session.TrySkipPreviousAsync(), MediaService.Previous);

    public Task SeekAsync(TimeSpan position) => OnDispatcherAsync(async () =>
    {
        var session = _session;
        if (_disposed || session is null || !CanSeek) return;
        try
        {
            long target = _timelineStart.Ticks + Math.Clamp(position.Ticks, 0, Duration.Ticks);
            if (_maximumSeek > _minimumSeek)
                target = Math.Clamp(target, _minimumSeek.Ticks, _maximumSeek.Ticks);
            if (await session.TryChangePlaybackPositionAsync(target) && IsCurrent(session))
            {
                // Only an accepted seek may update the visible position optimistically.
                _timelinePosition = TimeSpan.FromTicks(target) - _timelineStart;
                _timelineUpdatedAt = DateTimeOffset.UtcNow;
                UpdatePosition();
            }
        }
        catch (Exception)
        {
            await RefreshCurrentSessionAsync();
        }
    });

    private async Task InitializeCoreAsync()
    {
        if (_disposed) return;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await RefreshCurrentSessionAsync();
        }
        catch (Exception)
        {
            // SMTC can be unavailable on a locked-down Windows installation.
            // Transport buttons still use the existing system media keys.
            ResetSession();
        }
    }

    private async Task RefreshCurrentSessionAsync()
    {
        if (_disposed || _manager is null) return;
        try
        {
            var current = _manager.GetCurrentSession() ?? _manager.GetSessions().FirstOrDefault();
            if (!ReferenceEquals(current, _session))
            {
                DetachSession();
                ResetValues();
                _session = current;
                if (current is not null)
                {
                    current.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    current.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    current.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
            }
            if (current is null)
            {
                ResetValues();
                return;
            }
            HasSession = true;
            ReadPlaybackAndTimeline(current);
            await RefreshMetadataAsync(current);
        }
        catch (Exception)
        {
            ResetSession();
        }
    }

    private async Task RefreshMetadataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        if (!IsCurrent(session)) return;
        int revision = ++_metadataRevision;
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (!IsCurrent(session) || revision != _metadataRevision) return;
            Title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown track" : media.Title;
            Artist = media.Artist ?? "";
            Album = media.AlbumTitle ?? "";
            Artwork = null;
            if (media.Thumbnail is not null)
            {
                BitmapImage? image = await ReadArtworkAsync(media.Thumbnail);
                if (IsCurrent(session) && revision == _metadataRevision) Artwork = image;
            }
        }
        catch (Exception)
        {
            // A player can disappear between its change event and its async reply.
            // Do not let that reply clear a newer session or escape an event handler.
            if (IsCurrent(session) && revision == _metadataRevision)
            {
                Title = "Media player";
                Artist = "";
                Album = "";
                Artwork = null;
            }
        }
    }

    private static async Task<BitmapImage?> ReadArtworkAsync(IRandomAccessStreamReference thumbnail)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            // Ignore unreasonable or empty images supplied by third-party players.
            if (stream.Size is 0 or > 16 * 1024 * 1024) return null;
            using var reader = new DataReader(stream);
            uint size = (uint)stream.Size;
            if (await reader.LoadAsync(size) != size) return null;
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            using var memory = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 512;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ReadPlaybackAndTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        if (!IsCurrent(session)) return;
        try
        {
            var playback = session.GetPlaybackInfo();
            IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _supportsSeeking = playback.Controls.IsPlaybackPositionEnabled;
            _playbackRate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(_playbackRate)) _playbackRate = 1;
            var timeline = session.GetTimelineProperties();
            _timelineStart = timeline.StartTime;
            Duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero;
            _timelinePosition = timeline.Position - timeline.StartTime;
            _timelineUpdatedAt = timeline.LastUpdatedTime;
            _minimumSeek = timeline.MinSeekTime;
            _maximumSeek = timeline.MaxSeekTime;
            CanSeek = _supportsSeeking && Duration > TimeSpan.Zero;
            UpdatePosition();
        }
        catch (Exception)
        {
            IsPlaying = false;
            CanSeek = false;
        }
    }

    private void UpdatePosition()
    {
        double seconds = _timelinePosition.TotalSeconds;
        if (IsPlaying && _timelineUpdatedAt != default)
            seconds += Math.Max(0, (DateTimeOffset.UtcNow - _timelineUpdatedAt).TotalSeconds) * _playbackRate;
        Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration.TotalSeconds));
    }

    private Task TransportAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command, Action fallback) =>
        OnDispatcherAsync(async () =>
        {
            if (_disposed) return;
            var session = _session;
            if (session is null)
            {
                fallback();
                return;
            }
            try
            {
                await command(session);
                if (IsCurrent(session)) ReadPlaybackAndTimeline(session);
            }
            catch (Exception)
            {
                await RefreshCurrentSessionAsync();
                if (!_disposed && _session is null) fallback();
            }
        });

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        Queue(() => _ = RefreshCurrentSessionAsync());
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        Queue(() => _ = RefreshCurrentSessionAsync());
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        Queue(() => _ = RefreshMetadataAsync(sender));
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        Queue(() => ReadPlaybackAndTimeline(sender));
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        Queue(() => ReadPlaybackAndTimeline(sender));
    private void OnPositionTick(object? sender, EventArgs args) => UpdatePosition();

    private bool IsCurrent(GlobalSystemMediaTransportControlsSession session) => !_disposed && ReferenceEquals(_session, session);

    private Task OnDispatcherAsync(Func<Task> action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return Task.CompletedTask;
        if (_dispatcher.CheckAccess()) return action();
        return _dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void Queue(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        try
        {
            _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
        }
        catch (InvalidOperationException) { /* dispatcher is shutting down */ }
    }

    private void ResetSession()
    {
        DetachSession();
        ResetValues();
    }

    private void ResetValues()
    {
        Title = "Nothing playing";
        Artist = "Media keys still work";
        Album = "";
        Artwork = null;
        HasSession = IsPlaying = CanSeek = false;
        Position = Duration = TimeSpan.Zero;
        _timelineStart = _timelinePosition = _minimumSeek = _maximumSeek = TimeSpan.Zero;
        _timelineUpdatedAt = default;
        _supportsSeeking = false;
    }

    private void DetachSession()
    {
        ++_metadataRevision;
        var session = _session;
        _session = null;
        if (session is null) return;
        // Event removal itself can fail if the out-of-process player has gone away.
        try { session.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch (Exception) { }
        try { session.PlaybackInfoChanged -= OnPlaybackInfoChanged; } catch (Exception) { }
        try { session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged; } catch (Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.Invoke(Dispose);
            return;
        }
        _disposed = true;
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTick;
        DetachSession();
        if (_manager is not null)
        {
            try { _manager.SessionsChanged -= OnSessionsChanged; } catch (Exception) { }
            try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch (Exception) { }
            _manager = null;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
