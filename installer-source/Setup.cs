using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

[assembly: AssemblyTitle("YouTube Link Cleaner Setup")]
[assembly: AssemblyDescription("Per-user installer for YouTube Link Cleaner")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace BarnacleSetup
{
    internal static class SetupMain
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // Verification is read-only and does not access the clipboard.
                if (args.Length == 1 && args[0] == "--verify") { Payload.ReadAll(); return 0; }
                if (args.Length == 1 && args[0] == "--silent")
                {
                    byte[][] files = Payload.ReadAll();
                    InstallerEngine.Install(files[0], files[1], files[2], true, true);
                    return 0;
                }
                if (args.Length != 0) return 2;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length == 0) MessageBox.Show(error.Message, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class Payload
    {
        internal static byte[][] ReadAll()
        {
            return new[] { Read("payload.app", PayloadHashes.App), Read("payload.uninstaller", PayloadHashes.Uninstaller), Read("payload.readme", PayloadHashes.Readme) };
        }
        static byte[] Read(string name, string expected)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (input == null || input.Length < 1 || input.Length > 4 * 1024 * 1024) throw new InvalidDataException("The installer payload is missing or damaged. Download a fresh copy.");
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    byte[] bytes = output.ToArray();
                    using (SHA256 algorithm = SHA256.Create())
                    {
                        string hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
                        if (!String.Equals(hash, expected, StringComparison.Ordinal)) throw new InvalidDataException("Installer integrity check failed. Download a fresh copy.");
                    }
                    return bytes;
                }
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        readonly CheckBox startup, run;
        readonly Button install, cancel;
        readonly Label status;
        readonly ProgressBar progress;
        bool working, finished;

        public SetupForm()
        {
            Text = "YouTube Link Cleaner Setup";
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(570, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.icon"))
                if (icon != null) Icon = new Icon(icon);
            Controls.Add(new Label { Text = "YouTube Link Cleaner", Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = Color.FromArgb(24, 125, 105), AutoSize = true, Location = new Point(28, 22) });
            Controls.Add(new Label { Text = "Clean YouTube links and fix X embeds in Discord and other apps.\r\nRemoves YouTube si; rewrites x.com links to fixupx.com.", Location = new Point(30, 80), Size = new Size(512, 48) });
            startup = new CheckBox { Text = "Start automatically when I sign in to Windows", Checked = true, AutoSize = true, Location = new Point(30, 146) };
            run = new CheckBox { Text = "Run YouTube Link Cleaner after installation", Checked = true, AutoSize = true, Location = new Point(30, 177) };
            Controls.Add(startup); Controls.Add(run);
            Controls.Add(new Label { Text = "Runs locally with a tray icon. No account or administrator access needed.\r\nCleaned rich text becomes plain text. Ctrl+Alt+V checks before pasting.", ForeColor = Color.DimGray, Location = new Point(30, 222), Size = new Size(512, 49), Font = new Font("Segoe UI", 9) });
            status = new Label { Text = "Installs for your Windows account. You can remove it in Installed apps.", Location = new Point(30, 283), Size = new Size(512, 22), Font = new Font("Segoe UI", 9) };
            progress = new ProgressBar { Location = new Point(30, 309), Size = new Size(512, 7), Style = ProgressBarStyle.Marquee, Visible = false };
            cancel = new Button { Text = "Cancel", Location = new Point(309, 335), Size = new Size(104, 34), DialogResult = DialogResult.Cancel };
            install = new Button { Text = "Install and run", Location = new Point(425, 335), Size = new Size(117, 34) };
            Controls.Add(status); Controls.Add(progress); Controls.Add(cancel); Controls.Add(install);
            AcceptButton = install; CancelButton = cancel;
            install.Click += delegate { if (finished) Close(); else BeginInstall(); };
            run.CheckedChanged += delegate { if (!working && !finished) install.Text = run.Checked ? "Install and run" : "Install"; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) e.Cancel = true; };
        }

        void BeginInstall()
        {
            bool autoStart = startup.Checked, runNow = run.Checked;
            working = true; startup.Enabled = run.Enabled = install.Enabled = cancel.Enabled = false;
            progress.Visible = true; status.Text = "Verifying and installing...";
            var worker = new BackgroundWorker();
            worker.DoWork += delegate { byte[][] files = Payload.ReadAll(); InstallerEngine.Install(files[0], files[1], files[2], autoStart, runNow); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                working = false; progress.Visible = false; worker.Dispose();
                install.Enabled = cancel.Enabled = true;
                if (e.Error != null)
                {
                    startup.Enabled = run.Enabled = true;
                    status.Text = "Installation did not finish.";
                    MessageBox.Show(this, e.Error.Message, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finished = true; install.Text = "Close"; cancel.Visible = false;
                status.Text = runNow ? "Installed and running. Look for the Y icon in your tray." : "Installed. Launch it from your Windows Start menu.";
            };
            worker.RunWorkerAsync();
        }
    }
}
