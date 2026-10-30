using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BarnacleSetup
{
    class InstallerTests
    {
        static int checks;
        static string root, registry;
        [STAThread]
        static int Main()
        {
            root = Path.Combine(Environment.CurrentDirectory, "work", "Setup test with spaces " + Guid.NewGuid().ToString("N"));
            registry = @"Software\BarnacleSetupTests\" + Guid.NewGuid().ToString("N");
            string install = Path.Combine(root, "Install"), startup = Path.Combine(root, "Startup", "Cleaner.lnk"), menu = Path.Combine(root, "Menu");
            byte[] app = File.ReadAllBytes("work/setup/YouTubeLinkCleaner.exe");
            byte[] uninstall = File.ReadAllBytes("work/setup/Uninstall.exe");
            byte[] readme = File.ReadAllBytes("work/setup/QuickGuide.md");
            try
            {
                InstallerEngine.ConfigureForTests(install, startup, menu, registry);
                InstallerEngine.Install(app, uninstall, readme, true, false);
                Assert(InstallerEngine.IsInstalled, "install present");
                Assert(Same(app, File.ReadAllBytes(InstallerEngine.AppPath)), "app payload exact");
                Assert(Same(uninstall, File.ReadAllBytes(InstallerEngine.UninstallerPath)), "uninstaller payload exact");
                Assert(Same(readme, File.ReadAllBytes(Path.Combine(install, "README.md"))), "guide exact");
                Assert(ShortcutTarget(startup) == InstallerEngine.AppPath, "startup target");
                Assert(ShortcutTarget(Path.Combine(menu, "YouTube Link Cleaner.lnk")) == InstallerEngine.AppPath, "start menu target");
                Assert(ShortcutTarget(Path.Combine(menu, "Uninstall YouTube Link Cleaner.lnk")) == InstallerEngine.UninstallerPath, "uninstall target");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registry))
                {
                    Assert(key != null, "uninstall entry present");
                    Assert((string)key.GetValue("InstallLocation") == install, "registry install path");
                    Assert((string)key.GetValue("UninstallString") == "\"" + InstallerEngine.UninstallerPath + "\"", "quoted uninstall command");
                    Assert((string)key.GetValue("QuietUninstallString") == "\"" + InstallerEngine.UninstallerPath + "\" --silent", "quiet uninstall command");
                }
                string note = Path.Combine(install, "keep-me.txt");
                File.WriteAllText(note, "Unrelated user file.");
                InstallerEngine.Install(app, uninstall, readme, false, false);
                Assert(!File.Exists(startup), "startup can be disabled on reinstall");
                Assert(File.Exists(note), "reinstall keeps unrelated file");
                Assert(Directory.GetFiles(install, "*.new-*").Length == 0, "no staging files left");

                // Fixture implements the same command protocol without reading the clipboard.
                byte[] fixture = File.ReadAllBytes("work/setup/FixtureApp.exe");
                InstallerEngine.Install(fixture, uninstall, readme, true, true);
                Assert(Same(fixture, File.ReadAllBytes(InstallerEngine.AppPath)), "run-now verifies exact fixture process");
                InstallerEngine.Install(fixture, uninstall, readme, false, true);
                Assert(!File.Exists(startup), "running upgrade clears disabled startup");
                InstallerEngine.Uninstall();
                Assert(!File.Exists(InstallerEngine.AppPath), "running app stopped and removed");
                Assert(!File.Exists(InstallerEngine.UninstallerPath), "uninstaller removed");
                Assert(!File.Exists(startup), "startup removed");
                Assert(!Directory.Exists(menu), "empty menu folder removed");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registry)) Assert(key == null, "registry entry removed");
                Assert(File.ReadAllText(note) == "Unrelated user file.", "unknown file retained");
                File.Delete(note); Directory.Delete(install);

                Directory.CreateDirectory(Path.GetDirectoryName(startup));
                WriteShortcut(startup, Path.Combine(root, "OtherProgram.exe"));
                bool refused = false;
                try { InstallerEngine.Install(app, uninstall, readme, true, false); } catch (IOException) { refused = true; }
                Assert(refused && !InstallerEngine.IsInstalled, "foreign shortcut refuses install before files change");
                Assert(File.Exists(startup), "foreign shortcut left alone");
                File.Delete(startup);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registry)) key.SetValue("InstallLocation", Path.Combine(root, "OtherProgram"));
                refused = false;
                try { InstallerEngine.Install(app, uninstall, readme, true, false); } catch (IOException) { refused = true; }
                Assert(refused && !InstallerEngine.IsInstalled, "foreign uninstall key refuses install");
                Registry.CurrentUser.DeleteSubKey(registry);
                refused = false;
                try { InstallerEngine.Install(new byte[80], uninstall, readme, true, false); } catch (ArgumentException) { refused = true; }
                Assert(refused && !InstallerEngine.IsInstalled, "invalid executable rejected before install");
                refused = false;
                try { InstallerEngine.ConfigureForTests(Path.GetTempPath(), startup, menu, registry); } catch (ArgumentException) { refused = true; }
                Assert(refused, "test redirection restricted to workspace work directory");
                Console.WriteLine("PASS: " + checks + " isolated installer checks. Existing utility and clipboard untouched.");
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
            finally
            {
                // Every cleanup target is an explicit test-owned item within the verified root.
                try { InstallerEngine.ConfigureForTests(install, startup, menu, registry); InstallerEngine.Uninstall(); } catch { }
                foreach (string dir in new[] { Path.Combine(root, "Startup"), menu, install, root })
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir, false);
            }
        }
        static void Assert(bool result, string label) { checks++; if (!result) throw new Exception(label); }
        static bool Same(byte[] a, byte[] b)
        {
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(a)) == Convert.ToBase64String(sha.ComputeHash(b));
        }
        static string ShortcutTarget(string path)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")), link = null;
            try { link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path }); return (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null); }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        }
        static void WriteShortcut(string path, string target)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")), link = null;
            try
            {
                link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        }
    }
}
