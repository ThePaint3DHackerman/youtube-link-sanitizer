using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Uninstall YouTube Link Cleaner")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace BarnacleSetup
{
    internal static class UninstallMain
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = Array.IndexOf(args, "--silent") >= 0;
            try
            {
                if (args.Length == 1 && args[0] == "--verify") return 0;
                bool helper = args.Length >= 2 && args[0] == "--remove-helper";
                if (helper)
                {
                    string current = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                    string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                    string name = Path.GetFileName(current);
                    if (!String.Equals(Path.GetDirectoryName(current), temp, StringComparison.OrdinalIgnoreCase) || !name.StartsWith("Barnacle-Uninstall-", StringComparison.Ordinal) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return 2;
                    int parentId;
                    if (!Int32.TryParse(args[1], out parentId) || parentId == Process.GetCurrentProcess().Id) return 2;
                    try
                    {
                        using (Process parent = Process.GetProcessById(parentId))
                        {
                            if (!parent.HasExited)
                            {
                                try { if (!String.Equals(parent.MainModule.FileName, InstallerEngine.UninstallerPath, StringComparison.OrdinalIgnoreCase)) return 2; }
                                catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                            }
                            if (!parent.WaitForExit(10000)) throw new InvalidOperationException("The uninstaller is still closing. Try again.");
                        }
                    }
                    catch (ArgumentException) { } // Parent has already exited.
                    InstallerEngine.Uninstall();
                    if (!silent) MessageBox.Show("YouTube Link Cleaner has been removed.", "YouTube Link Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (args.Length > (silent ? 1 : 0)) return 2;
                if (!silent && MessageBox.Show("Remove YouTube Link Cleaner and its Windows startup entry?", "Uninstall YouTube Link Cleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                string copy = Path.Combine(Path.GetTempPath(), "Barnacle-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                // The helper runs outside the installation so the installed EXEs can be deleted.
                File.Copy(Assembly.GetExecutingAssembly().Location, copy, false);
                var start = new ProcessStartInfo(copy, "--remove-helper " + Process.GetCurrentProcess().Id + (silent ? " --silent" : "")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (Process child = Process.Start(start)) if (child == null) throw new InvalidOperationException("Could not start the uninstall helper.");
                return 0;
            }
            catch (Exception error)
            {
                if (!silent) MessageBox.Show(error.Message, "Uninstall could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
