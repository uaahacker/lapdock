using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using LapDock.Services;

namespace LapDock.Views;

public enum SetupMode { Install, Uninstall }

/// <summary>
/// Install: shown when the shared LapDock.exe is opened from Downloads (or anywhere but its install folder).
/// Uninstall: shown by Settings → Apps → LapDock → Uninstall ("LapDock.exe --uninstall").
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupMode _mode;
    private bool _done;

    /// <summary>Install mode, "Try without installing": run LapDock from where it is.</summary>
    public event Action? TryRequested;

    public SetupWindow(SetupMode mode)
    {
        InitializeComponent();
        _mode = mode;
        VersionText.Text = $"Version {InstallService.Version}";
        bool install = mode == SetupMode.Install;
        InstallOptions.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        UninstallOptions.Visibility = install ? Visibility.Collapsed : Visibility.Visible;
        if (install)
        {
            bool update = InstallService.IsInstalled;
            Heading.Text = update ? "Update LapDock" : "Install LapDock";
            Message.Text = "A notch for your laptop screen: quick settings, music, your day, stats, clipboard history, " +
                           "apps, wallpapers and power, one hover away.";
            Note.Text = update
                ? "LapDock is already installed. This replaces it with this copy; your settings and history are kept."
                : "Installs for you only, in your user folder; no admin rights needed. Remove it any time from Settings › Apps.";
            PrimaryButton.Content = update ? "Update" : "Install";
            SecondaryButton.Content = "Try without installing";
        }
        else
        {
            Heading.Text = "Uninstall LapDock";
            Message.Text = "Removes LapDock from this PC, with its Start menu shortcut and its start-at-sign-in entry.";
            Note.Text = "Your settings, clipboard history, to-dos and snippets stay unless you turn that on, so a later reinstall picks up where you left off.";
            PrimaryButton.Content = "Uninstall";
            SecondaryButton.Content = "Cancel";
        }
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Application.Current.Shutdown();
            return;
        }
        PrimaryButton.IsEnabled = SecondaryButton.IsEnabled = false;
        ShowStatus(_mode == SetupMode.Install ? "Installing…" : "Removing…");
        try
        {
            if (_mode == SetupMode.Install)
            {
                bool autostart = AutostartBox.IsChecked == true, desktop = DesktopBox.IsChecked == true;
                await Task.Run(() => InstallService.Install(autostart, desktop));
                // Start the installed copy (it greets with "LapDock is ready") and get out of the way.
                Process.Start(new ProcessStartInfo(InstallService.InstalledExe, "--installed") { UseShellExecute = false });
                Application.Current.Shutdown();
                return;
            }
            bool removeData = RemoveDataBox.IsChecked == true;
            await Task.Run(() => InstallService.Uninstall(removeData));
            _done = true;
            Heading.Text = "LapDock was removed";
            Message.Text = removeData ? "LapDock and your LapDock data are gone from this PC." : "LapDock is gone from this PC. Your settings were kept.";
            UninstallOptions.Visibility = Visibility.Collapsed;
            Note.Visibility = Status.Visibility = Visibility.Collapsed;
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Close";
            PrimaryButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowStatus($"That didn't work: {ex.Message}");
            PrimaryButton.IsEnabled = SecondaryButton.IsEnabled = true;
        }
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == SetupMode.Install)
        {
            TryRequested?.Invoke();
            Close();
        }
        else Application.Current.Shutdown();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = Visibility.Visible;
    }
}
