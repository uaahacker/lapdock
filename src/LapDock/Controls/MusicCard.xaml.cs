using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LapDock.Services;

namespace LapDock.Controls;

/// <summary>
/// Now playing card. With no media session the transport buttons still work — NowPlayingService
/// falls back to Windows media keys — so the card stays useful ("Nothing playing · Media keys still work").
/// </summary>
public partial class MusicCard : UserControl
{
    private NowPlayingService? _player;
    private bool _seeking;

    public MusicCard() => InitializeComponent();

    public void Attach(NowPlayingService player)
    {
        _player = player;
        DataContext = player;
        player.PropertyChanged += OnPlayerChanged;
        Update();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Position) or nameof(NowPlayingService.PositionSeconds)
            or nameof(NowPlayingService.PositionText))
        {
            if (!_seeking) SeekBar.Value = _player!.PositionSeconds; // the 2×/s tick: cheap path only
            return;
        }
        Update();
    }

    private void Update()
    {
        if (_player is null) return;
        PlayIcon.Icon = _player.IsPlaying ? "pause" : "play";
        SeekRow.Visibility = _player.HasSession ? Visibility.Visible : Visibility.Collapsed;
        if (!_seeking)
        {
            SeekBar.Maximum = Math.Max(1, _player.DurationSeconds);
            SeekBar.Value = _player.PositionSeconds;
        }
        SeekBar.IsEnabled = _player.CanSeek;
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) { if (_player is not null) await _player.TogglePlayPauseAsync(); }
    private async void Prev_Click(object sender, RoutedEventArgs e) { if (_player is not null) await _player.PreviousAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (_player is not null) await _player.NextAsync(); }

    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private async void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await FinishSeekAsync();
    private async void SeekBar_LostMouseCapture(object sender, MouseEventArgs e) => await FinishSeekAsync();

    private async Task FinishSeekAsync()
    {
        if (!_seeking || _player is null) return;
        _seeking = false;
        await _player.SeekAsync(TimeSpan.FromSeconds(SeekBar.Value));
    }
}
