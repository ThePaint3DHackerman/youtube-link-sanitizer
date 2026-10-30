using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace BarnacleSetup
{
    // All mutations are per-user and restricted to the explicit paths below.
    public static class InstallerEngine
    {
        static string installDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeLinkCleaner");
        static string startupLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "YouTube Link Cleaner.lnk");
        static string startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "YouTube Link Cleaner");
        static string registryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\YouTubeLinkCleaner";

        public static string InstallDirectory { get { return installDirectory; } }
        public static string AppPath { get { return Child(InstallDirectory, "YouTubeLinkCleaner.exe"); } }
        public static string UninstallerPath { get { return Child(InstallDirectory, "Uninstall.exe"); } }
        public static string StartupLink { get { return startupLink; } }
        public static string StartMenuDirectory { get { return startMenuDirectory; } }
        public static string RegistryKeyPath { get { return registryKeyPath; } }
        static string AppMenuLink { get { return Child(StartMenuDirectory, "YouTube Link Cleaner.lnk"); } }
        static string UninstallMenuLink { get { return Child(StartMenuDirectory, "Uninstall YouTube Link Cleaner.lnk"); } }
        public static bool IsInstalled { get { return File.Exists(AppPath); } }

#if SETUP_TESTS
        public static void ConfigureForTests(string testInstallDirectory, string testStartupLink, string testStartMenuDirectory, string testRegistryKeyPath)
        {
            string workRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "work"));
            string nextInstall = Full(testInstallDirectory);
            string nextStartup = Full(testStartupLink);
            string nextMenu = Full(testStartMenuDirectory);
            if (!IsWithin(nextInstall, workRoot) || !IsWithin(nextStartup, workRoot) || !IsWithin(nextMenu, workRoot))
                throw new ArgumentException("Test paths must be strictly below the current workspace's work directory.");
            if (!nextStartup.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The test startup link must have a .lnk extension.");
            const string prefix = @"Software\BarnacleSetupTests\";
            if (String.IsNullOrEmpty(testRegistryKeyPath) || !testRegistryKeyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                testRegistryKeyPath.Length <= prefix.Length || testRegistryKeyPath.IndexOf('/') >= 0 ||
                testRegistryKeyPath.IndexOf("..", StringComparison.Ordinal) >= 0 || testRegistryKeyPath.EndsWith("\\", StringComparison.Ordinal))
                throw new ArgumentException("The test registry key must be below HKCU Software\\BarnacleSetupTests.");
            CheckDirectory(nextInstall);
            CheckFile(nextStartup);
            CheckDirectory(nextMenu);
            installDirectory = nextInstall;
            startupLink = nextStartup;
            startMenuDirectory = nextMenu;
            registryKeyPath = testRegistryKeyPath;
        }
#endif

        public static void Install(byte[] app, byte[] uninstaller, byte[] readme, bool startWithWindows, bool runNow)
        {
            CheckPe(app, "application");
            CheckPe(uninstaller, "uninstaller");
            if (readme == null || readme.Length == 0) throw new ArgumentException("The readme payload is missing.");
            ValidatePaths();
            CheckRegistryOwnership();
            CheckShortcutOwnership(StartupLink, AppPath);
            CheckShortcutOwnership(AppMenuLink, AppPath);
            CheckShortcutOwnership(UninstallMenuLink, UninstallerPath);
            StopInstalledApp();

            Directory.CreateDirectory(InstallDirectory);
            // Recheck after creation, and before each operation, rather than trusting a prior existence check.
            CheckDirectory(InstallDirectory);
            AtomicWrite(AppPath, app);
            AtomicWrite(UninstallerPath, uninstaller);
            AtomicWrite(Child(InstallDirectory, "README.md"), readme);
            Directory.CreateDirectory(StartMenuDirectory);
            WriteShortcut(AppMenuLink, AppPath, "", "Clean YouTube share links on the clipboard");
            WriteShortcut(UninstallMenuLink, UninstallerPath, "", "Remove YouTube Link Cleaner");
            if (startWithWindows)
                WriteShortcut(StartupLink, AppPath, "--quiet", "Clean YouTube share links on the clipboard");
            else
                DeleteOwnedShortcut(StartupLink, AppPath);

            WriteRegistry();
            if (runNow) StartAndVerify();
        }

        public static void Uninstall()
        {
            // A caller removing the installed uninstaller itself must run from a temporary helper copy.
            ValidatePaths();
            CheckRegistryOwnership();
            CheckShortcutOwnership(StartupLink, AppPath);
            CheckShortcutOwnership(AppMenuLink, AppPath);
            CheckShortcutOwnership(UninstallMenuLink, UninstallerPath);
            StopInstalledApp();
            DeleteOwnedShortcut(StartupLink, AppPath);
            DeleteOwnedShortcut(AppMenuLink, AppPath);
            DeleteOwnedShortcut(UninstallMenuLink, UninstallerPath);

            foreach (string name in new string[] { "YouTubeLinkCleaner.exe", "README.md", "Uninstall.exe", "Uninstall.ps1" })
            {
                string target = Child(InstallDirectory, name);
                CheckFile(target);
                if (File.Exists(target)) File.Delete(target);
            }
            DeleteDirectoryIfEmpty(StartMenuDirectory);
            DeleteDirectoryIfEmpty(InstallDirectory);
            CheckRegistryOwnership();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key == null) return;
            }
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, false);
        }

        static void ValidatePaths()
        {
            CheckDirectory(InstallDirectory);
            CheckDirectory(StartMenuDirectory);
            CheckDirectory(Path.GetDirectoryName(StartupLink));
            CheckFile(StartupLink);
            CheckFile(AppMenuLink);
            CheckFile(UninstallMenuLink);
            foreach (string name in new string[] { "YouTubeLinkCleaner.exe", "README.md", "Uninstall.exe", "Uninstall.ps1" })
                CheckFile(Child(InstallDirectory, name));
        }

        static void CheckPe(byte[] bytes, string description)
        {
            if (bytes == null || bytes.Length < 64 || bytes[0] != 0x4d || bytes[1] != 0x5a)
                throw new ArgumentException("The " + description + " payload is not a Windows executable.");
            int offset = BitConverter.ToInt32(bytes, 0x3c);
            if (offset < 64 || offset > bytes.Length - 4 || bytes[offset] != 0x50 || bytes[offset + 1] != 0x45 ||
                bytes[offset + 2] != 0 || bytes[offset + 3] != 0)
                throw new ArgumentException("The " + description + " payload has an invalid PE header.");
        }

        static string Full(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("An absolute path is required.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        static bool SamePath(string left, string right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
            try { return String.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        static bool IsWithin(string path, string directory)
        {
            return Full(path).StartsWith(Full(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        static string Child(string directory, string fileName)
        {
            string target = Full(Path.Combine(directory, fileName));
            if (!SamePath(Path.GetDirectoryName(target), directory)) throw new IOException("A target escaped its expected directory.");
            return target;
        }

        static bool Attributes(string path, out FileAttributes attributes)
        {
            try { attributes = File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { attributes = 0; return false; }
            catch (DirectoryNotFoundException) { attributes = 0; return false; }
        }

        static void CheckDirectory(string path)
        {
            string current = Full(path);
            while (!String.IsNullOrEmpty(current))
            {
                FileAttributes attributes;
                if (Attributes(current, out attributes))
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                        throw new IOException("Refusing a redirected path or a file used as a directory: " + current);
                }
                string parent = Path.GetDirectoryName(current);
                if (String.IsNullOrEmpty(parent) || String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
        }

        static void CheckFile(string path)
        {
            string target = Full(path);
            CheckDirectory(Path.GetDirectoryName(target));
            FileAttributes attributes;
            if (Attributes(target, out attributes) && ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) != 0))
                throw new IOException("Refusing a redirected file or a directory used as a file: " + target);
        }

        static void AtomicWrite(string path, byte[] data)
        {
            CheckFile(path);
            string temporary = Child(Path.GetDirectoryName(path), Path.GetFileName(path) + ".new-" + Guid.NewGuid().ToString("N"));
            try
            {
                CheckFile(temporary);
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                CheckFile(path);
                CheckFile(temporary);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                CheckFile(temporary);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        static List<Process> OwnedProcesses()
        {
            List<Process> owned = new List<Process>();
            foreach (Process process in Process.GetProcessesByName("YouTubeLinkCleaner"))
            {
                bool keep = false;
                try { keep = SamePath(process.MainModule.FileName, AppPath); }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                if (keep) owned.Add(process);
                else process.Dispose();
            }
            return owned;
        }

        static Process Start(string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(AppPath, arguments);
            info.WorkingDirectory = InstallDirectory;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            Process process = Process.Start(info);
            if (process == null) throw new IOException("The cleaner could not be launched.");
            return process;
        }

        static int Remaining(Stopwatch watch)
        {
            return Math.Max(0, 5000 - (int)Math.Min(5000L, watch.ElapsedMilliseconds));
        }

        static void StopInstalledApp()
        {
            List<Process> owned = OwnedProcesses();
            try
            {
                if (owned.Count == 0) return;
                Stopwatch deadline = Stopwatch.StartNew();
                using (Process command = Start("--exit"))
                {
                    if (!command.WaitForExit(Remaining(deadline)))
                        throw new IOException("The cleaner did not respond to an exit request. Exit it through its tray menu and try again.");
                    if (command.ExitCode != 0)
                        throw new IOException("The cleaner could not be stopped. Exit it through its tray menu and try again.");
                }
                foreach (Process process in owned)
                    if (!process.WaitForExit(Remaining(deadline)))
                        throw new IOException("The installed cleaner is still running. Exit it through its tray menu and try again.");
            }
            finally { foreach (Process process in owned) process.Dispose(); }
        }

        static void StartAndVerify()
        {
            using (Process launched = Start("--quiet"))
            {
                Stopwatch deadline = Stopwatch.StartNew();
                while (Remaining(deadline) > 0)
                {
                    bool ready = false;
                    using (Process probe = Start("--status"))
                    {
                        if (!probe.WaitForExit(Remaining(deadline))) break;
                        ready = probe.ExitCode == 0;
                    }
                    if (ready)
                    {
                        List<Process> processes = OwnedProcesses();
                        try { if (processes.Count > 0) return; }
                        finally { foreach (Process process in processes) process.Dispose(); }
                    }
                    Thread.Sleep(Math.Min(100, Remaining(deadline)));
                }
            }
            throw new IOException("Installation files are present, but the installed cleaner did not confirm it is running. Another copy may already be running from a different folder. Exit that copy through its tray menu, then launch the installed app.");
        }

        static object Get(object target, string property)
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
        }

        static void Set(object target, string property, object value)
        {
            target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new object[] { value });
        }

        static object OpenShortcut(string path, out object shell)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) throw new IOException("Windows Script Host is unavailable; shortcuts could not be created.");
            shell = Activator.CreateInstance(type);
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        }

        static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }

        static void CheckShortcutOwnership(string path, string expectedTarget)
        {
            CheckFile(path);
            if (!File.Exists(path)) return;
            object shell = null, shortcut = null;
            try
            {
                shortcut = OpenShortcut(path, out shell);
                if (!SamePath(Convert.ToString(Get(shortcut, "TargetPath")), expectedTarget))
                    throw new IOException("A different program owns this shortcut: " + path);
            }
            finally { Release(shortcut); Release(shell); }
        }

        static void WriteShortcut(string path, string target, string arguments, string description)
        {
            CheckShortcutOwnership(path, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            CheckFile(path);
            object shell = null, shortcut = null;
            try
            {
                shortcut = OpenShortcut(path, out shell);
                Set(shortcut, "TargetPath", target);
                Set(shortcut, "Arguments", arguments);
                Set(shortcut, "WorkingDirectory", InstallDirectory);
                Set(shortcut, "Description", description);
                Set(shortcut, "IconLocation", target + ",0");
                Set(shortcut, "WindowStyle", 7);
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { Release(shortcut); Release(shell); }
        }

        static void DeleteOwnedShortcut(string path, string target)
        {
            CheckShortcutOwnership(path, target);
            if (File.Exists(path)) File.Delete(path);
        }

        static void CheckRegistryOwnership()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key == null) return;
                string location = key.GetValue("InstallLocation") as string;
                string command = key.GetValue("UninstallString") as string;
                bool hasLocation = !String.IsNullOrWhiteSpace(location);
                bool hasCommand = !String.IsNullOrWhiteSpace(command);
                bool commandMatches = hasCommand && SamePath(command.Trim().Trim('"'), UninstallerPath);
                if ((!hasLocation && !hasCommand) || (hasLocation && !SamePath(location, InstallDirectory)) || (hasCommand && !commandMatches))
                    throw new IOException("A different program owns this uninstall registry key: HKCU\\" + RegistryKeyPath);
            }
        }

        static void WriteRegistry()
        {
            CheckRegistryOwnership();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
            {
                if (key == null) throw new IOException("The per-user uninstall entry could not be created.");
                key.SetValue("DisplayName", "YouTube Link Cleaner");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("UninstallString", "\"" + UninstallerPath + "\"");
                key.SetValue("QuietUninstallString", "\"" + UninstallerPath + "\" --silent");
                key.SetValue("DisplayIcon", AppPath + ",0");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Min(Int32.MaxValue, (new FileInfo(AppPath).Length + new FileInfo(UninstallerPath).Length + 1023) / 1024), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        static void DeleteDirectoryIfEmpty(string path)
        {
            CheckDirectory(path);
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path, false);
        }
    }
}
