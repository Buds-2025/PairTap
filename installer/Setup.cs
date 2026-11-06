using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using PairTap;

[assembly: AssemblyTitle(Product.DisplayName + " 安装程序")]
[assembly: AssemblyProduct(Product.Name)]
[assembly: AssemblyVersion(Product.Version)]
[assembly: AssemblyFileVersion(Product.Version)]

internal static class Setup
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string LegacyDisplayName = "包裹选中文本";
    private const string LegacyProductName = "Surround Selection";
    private const string LegacySetupLock = @"Local\" + Product.LegacyId + ".Setup.63BA74EF";
    private const string LegacyInstanceName = @"Local\" + Product.LegacyId + ".63BA74EF";
    private const string LegacyExitEventName = @"Local\" + Product.LegacyId + ".Exit.63BA74EF";

    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet = args.Length == 1 && args[0] == "--quiet";
        try
        {
            using (var installer = new Mutex(false, Product.SetupLock))
            using (var legacyInstaller = new Mutex(false, LegacySetupLock))
            {
                EnterSetupLock(installer);
                try
                {
                    EnterSetupLock(legacyInstaller);
                    try { Install(); }
                    finally { legacyInstaller.ReleaseMutex(); }
                }
                finally { installer.ReleaseMutex(); }
            }
            return 0;
        }
        catch (Exception error)
        {
            if (!quiet) MessageBox.Show(error.Message, "无法安装" + Product.ChineseName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void EnterSetupLock(Mutex installer)
    {
        try
        {
            if (!installer.WaitOne(0)) throw new InvalidOperationException("安装或卸载正在进行。");
        }
        catch (AbandonedMutexException) { }
    }

    private static void Install()
    {
        string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        string directory = Path.Combine(programs, Product.Name);
        string legacyDirectory = Path.Combine(programs, Product.LegacyId);
        string exe = Path.Combine(directory, Product.Name + ".exe");
        string uninstaller = Path.Combine(directory, "Uninstall.ps1");
        Directory.CreateDirectory(directory);
        if (IsReparsePoint(directory) || IsReparsePoint(legacyDirectory))
            throw new InvalidOperationException("安装目录是重解析点。请先恢复原安装目录。");

        StopRunningApp(Product.InstanceName, Product.ExitEventName);
        StopRunningApp(LegacyInstanceName, LegacyExitEventName);
        MigrateSettings();
        Extract("app", exe);
        Extract("uninstaller", uninstaller);
        MakeShortcut(exe, directory);

        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRoot + Product.Name))
        {
            key.SetValue("DisplayName", Product.DisplayName);
            key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", directory);
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
            key.SetValue("UninstallString", "\"" + powershell + "\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + uninstaller + "\"");
        }

        RemoveLegacyInstallation(legacyDirectory);
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = true });
    }

    private static bool IsReparsePoint(string path)
    {
        return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static void MigrateSettings()
    {
        using (RegistryKey legacy = Registry.CurrentUser.OpenSubKey(Product.LegacySettingsKey, true))
        {
            if (legacy == null) return;
            var previous = legacy.GetValue("Shortcuts") as string[];
            if (previous != null)
            {
                using (RegistryKey current = Registry.CurrentUser.CreateSubKey(Product.SettingsKey))
                {
                    object settings = current.GetValue("Shortcuts");
                    if (settings == null)
                    {
                        current.SetValue("Shortcuts", previous, RegistryValueKind.MultiString);
                        settings = previous;
                    }
                    var saved = settings as string[];
                    if (saved != null && saved.SequenceEqual(previous)) legacy.DeleteValue("Shortcuts", false);
                }
            }
            if (legacy.ValueCount != 0 || legacy.SubKeyCount != 0) return;
        }
        Registry.CurrentUser.DeleteSubKey(Product.LegacySettingsKey, false);
    }

    private static void RemoveLegacyInstallation(string directory)
    {
        string exe = Path.Combine(directory, Product.LegacyId + ".exe");
        string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LegacyDisplayName + ".lnk");
        if (File.Exists(link))
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic shortcut = shell.CreateShortcut(link);
            if (string.Equals((string)shortcut.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) File.Delete(link);
        }

        string keyName = UninstallRoot + Product.LegacyId;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyName))
        {
            if (key != null && string.Equals(key.GetValue("InstallLocation") as string, directory, StringComparison.OrdinalIgnoreCase))
                Registry.CurrentUser.DeleteSubKey(keyName, false);
        }

        if (!Directory.Exists(directory)) return;
        File.Delete(exe);
        File.Delete(Path.Combine(directory, "Uninstall.ps1"));
        foreach (string backup in Directory.EnumerateFiles(directory, Product.LegacyId + ".exe~RF*.TMP"))
        {
            if (string.Equals(FileVersionInfo.GetVersionInfo(backup).ProductName, LegacyProductName, StringComparison.Ordinal))
                File.Delete(backup);
        }
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static void Extract(string resource, string destination)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".installing";
        try
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(file);

            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void MakeShortcut(string exe, string directory)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(Path.Combine(programs, Product.ChineseName + ".lnk"));
        shortcut.TargetPath = exe;
        shortcut.WorkingDirectory = directory;
        shortcut.Description = Product.DisplayName;
        shortcut.Save();
    }

    private static void StopRunningApp(string instanceName, string exitEventName)
    {
        bool signalled = false;
        for (int i = 0; i < 100; i++)
        {
            Mutex app;
            if (!Mutex.TryOpenExisting(instanceName, out app)) return;
            app.Dispose();
            EventWaitHandle signal;
            if (!signalled && EventWaitHandle.TryOpenExisting(exitEventName, out signal))
            {
                using (signal) signalled = signal.Set();
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("请先从系统托盘退出程序，再运行安装程序。");
    }
}
