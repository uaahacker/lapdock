using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace LapDock.Services;

/// <summary>
/// LapDock installs itself, for the current user, without admin rights:
/// <list type="bullet">
/// <item>the exe goes to %LocalAppData%\Programs\LapDock (like VS Code's user installer);</item>
/// <item>a Start menu shortcut (and optionally a desktop one) points at it;</item>
/// <item>an entry in Settings → Apps runs "LapDock.exe --uninstall";</item>
/// <item>"Start when I sign in" is the usual HKCU Run value.</item>
/// </list>
/// Settings and history in %AppData%\LapDock are kept on uninstall unless the user asks to delete them.
/// </summary>
public static class InstallService
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LapDock";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "LapDock";

    public static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LapDock");
    public static readonly string InstalledExe = Path.Combine(InstallDir, "LapDock.exe");
    private static readonly string StartMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LapDock.lnk");
    private static readonly string DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LapDock.lnk");

    public static string Version => (typeof(InstallService).Assembly.GetName().Version ?? new Version(1, 0, 0)).ToString(3);
    private static string CurrentExe => Environment.ProcessPath ?? InstalledExe;

    /// <summary>The published single-file LapDock.exe, which offers to install itself. Dev builds just run.</summary>
    public static bool IsPackaged =>
#if SINGLE_FILE
        true;
#else
        false;
#endif

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key is not null && File.Exists(InstalledExe);
        }
    }

    public static bool RunningInstalled => string.Equals(Path.GetFullPath(CurrentExe), InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{(IsInstalled ? InstalledExe : CurrentExe)}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>Installs (or updates) this copy. A running LapDock is closed first so its file can be replaced.</summary>
    public static void Install(bool startWithWindows, bool desktopShortcut)
    {
        SingleInstance.StopRunning(TimeSpan.FromSeconds(5));
        Directory.CreateDirectory(InstallDir);
        if (!RunningInstalled) CopyWithRetry(CurrentExe, InstalledExe);

        CreateShortcut(StartMenuShortcut);
        if (desktopShortcut) CreateShortcut(DesktopShortcut);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "LapDock");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Ubaid Ullah");
            key.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
            key.SetValue("URLInfoAbout", "https://github.com/uaahacker/lapdock");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        StartWithWindows = startWithWindows;
    }

    /// <summary>
    /// Removes LapDock: closes it, deletes the sign-in entry, shortcuts and the Settings → Apps entry, and
    /// (optionally) the user's LapDock data. The exe can't delete itself while it runs, so a hidden cmd
    /// waits for this process to end, then removes the install folder and the files .NET unpacked for it.
    /// </summary>
    public static void Uninstall(bool removeData)
    {
        SingleInstance.StopRunning(TimeSpan.FromSeconds(5));
        StartWithWindows = false;
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (removeData) DeleteLapDockFolder(SettingsService.Dir);

        var unpacked = Path.Combine(Path.GetTempPath(), ".net", "LapDock");
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c ping -n 4 127.0.0.1 > nul & rmdir /s /q \"{InstallDir}\" & rmdir /s /q \"{unpacked}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
        });
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (attempt < 15) { Thread.Sleep(200); } // the old copy is still letting go of the file
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { /* already gone */ }
    }

    /// <summary>Only ever deletes a folder named LapDock (guards against a bad path deleting something else).</summary>
    private static void DeleteLapDockFolder(string dir)
    {
        if (!Path.GetFileName(dir.TrimEnd('\\')).Equals("LapDock", StringComparison.OrdinalIgnoreCase)) return;
        try { Directory.Delete(dir, recursive: true); } catch (Exception) { /* in use: leave it */ }
    }

    private static void CreateShortcut(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(InstalledExe);
            link.SetWorkingDirectory(InstallDir);
            link.SetDescription("LapDock: a notch for your laptop screen");
            link.SetIconLocation(InstalledExe, 0);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(IntPtr path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
