using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("YouTube Link Cleaner")]
[assembly: AssemblyDescription("Local clipboard cleaner for YouTube si share parameters")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace Barnacle
{
    public static class Program
    {
        const string MutexName = "Local\\Barnacle.YouTubeLinkCleaner.v1";
        [STAThread]
        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            using (var mutex = new Mutex(false, MutexName))
            {
                bool owns;
                try { owns = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { owns = true; }
                try
                {
                    if (command == "--status") return !owns && SendCommand(0x8004) ? 0 : 1;
                    if (command == "--exit" || command == "--pause" || command == "--resume")
                    {
                        if (owns) return command == "--exit" ? 0 : 1;
                        return SendCommand(command == "--exit" ? 0x0010 : command == "--pause" ? 0x8001 : 0x8002) ? 0 : 2;
                    }
                    if (!owns)
                    {
                        if (command != "--quiet") SendCommand(0x8003);
                        return 0;
                    }
                    if (command != "" && command != "--quiet") return 2;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new CleanerContext(command == "--quiet", true)) Application.Run(context);
                    return 0;
                }
                catch (Exception)
                {
                    MessageBox.Show("YouTube Link Cleaner could not start. No clipboard content was logged. Try starting it again.", "YouTube Link Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
                finally { if (owns) mutex.ReleaseMutex(); }
            }
        }

        static bool SendCommand(int command)
        {
            for (int i = 0; i < 50; i++)
            {
                IntPtr hwnd = FindWindow(null, CleanerWindow.WindowTitle);
                if (hwnd != IntPtr.Zero)
                {
                    UIntPtr result;
                    return SendMessageTimeout(hwnd, (uint)command, UIntPtr.Zero, IntPtr.Zero, 2, 3000, out result) != IntPtr.Zero;
                }
                Thread.Sleep(20);
            }
            return false;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string caption);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
    }

    public sealed class CleanerContext : ApplicationContext
    {
        readonly CleanerWindow window;
        readonly System.Windows.Forms.Timer retryTimer;
        readonly System.Windows.Forms.Timer pasteTimer;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem pauseItem;
        readonly Icon activeIcon, pausedIcon;
        int retries, pasteTicks;
        IntPtr pasteTarget;
        bool hotkeyRegistered, disposed;
        public bool Paused { get; private set; }
        public int CleanedCount { get; private set; }
        public CleanOutcome LastOutcome { get; private set; }
        public IntPtr WindowHandle { get { return window.Handle; } }

        // showTray=false is for integration tests on a private Windows window station.
        public CleanerContext(bool quiet, bool showTray)
        {
            window = new CleanerWindow();
            retryTimer = new System.Windows.Forms.Timer { Interval = 20 };
            pasteTimer = new System.Windows.Forms.Timer { Interval = 20 };
            retryTimer.Tick += delegate { AttemptClean(); };
            pasteTimer.Tick += delegate { FinishPaste(); };
            window.ClipboardChanged += delegate { if (!Paused) { retries = 0; AttemptClean(); } };
            window.ExitRequested += delegate { ExitThread(); };
            window.PauseRequested += delegate { SetPaused(true); };
            window.ResumeRequested += delegate { SetPaused(false); };
            window.StatusRequested += delegate { ShowStatus(); };
            window.PasteRequested += delegate { BeginPaste(); };
            if (!AddClipboardFormatListener(window.Handle))
            {
                window.DestroyHandle();
                throw new InvalidOperationException("Cannot register clipboard listener.");
            }
            if (showTray)
            {
                activeIcon = MakeIcon(Color.FromArgb(24, 125, 105));
                pausedIcon = MakeIcon(Color.FromArgb(153, 110, 32));
                menu = new ContextMenuStrip();
                pauseItem = new ToolStripMenuItem("Pause automatic cleaning");
                pauseItem.Click += delegate { SetPaused(!Paused); };
                menu.Items.Add("YouTube Link Cleaner", null, delegate { ShowStatus(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(pauseItem);
                menu.Items.Add("Clean clipboard now", null, delegate { retries = 0; AttemptClean(); ShowStatus(); });
                menu.Items.Add("Status and help", null, delegate { ShowStatus(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Exit", null, delegate { ExitThread(); });
                tray = new NotifyIcon { Icon = activeIcon, Text = "YouTube Link Cleaner - active", ContextMenuStrip = menu, Visible = true };
                tray.DoubleClick += delegate { ShowStatus(); };
                hotkeyRegistered = RegisterHotKey(window.Handle, 1, 0x4003, 0x56); // Ctrl+Alt+V, no repeat.
                if (!quiet) Balloon("Running in the background. YouTube si parameters are removed on copy. Right-click this icon for controls.");
            }
            // Include a link already on the clipboard at launch.
            AttemptClean();
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            retryTimer.Stop();
            pasteTimer.Stop();
            if (pauseItem != null) pauseItem.Text = paused ? "Resume automatic cleaning" : "Pause automatic cleaning";
            if (tray != null)
            {
                tray.Icon = paused ? pausedIcon : activeIcon;
                tray.Text = "YouTube Link Cleaner - " + (paused ? "paused" : "active");
            }
            if (!paused) { retries = 0; AttemptClean(); }
        }

        void AttemptClean()
        {
            retryTimer.Stop();
            LastOutcome = NativeClipboard.TryClean(window.Handle);
            if (LastOutcome == CleanOutcome.Cleaned) CleanedCount++;
            if (LastOutcome == CleanOutcome.Busy && ++retries < 50 && !Paused) retryTimer.Start();
        }

        void BeginPaste()
        {
            if (Paused) { Balloon("Cleaning is paused. Resume it from the tray icon to use Ctrl+Alt+V."); return; }
            pasteTarget = GetForegroundWindow();
            pasteTicks = 0;
            pasteTimer.Start();
        }

        void FinishPaste()
        {
            if (++pasteTicks > 100 || GetForegroundWindow() != pasteTarget || pasteTarget == IntPtr.Zero)
            {
                pasteTimer.Stop();
                Balloon("Clean paste cancelled because focus changed or the shortcut keys stayed down.");
                return;
            }
            // Do not release the user's keys or paste into a newly focused app.
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x56, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return;
            LastOutcome = NativeClipboard.TryClean(window.Handle);
            if (LastOutcome == CleanOutcome.Busy) return;
            pasteTimer.Stop();
            if (LastOutcome == CleanOutcome.Cleaned) CleanedCount++;
            if (LastOutcome != CleanOutcome.Cleaned && LastOutcome != CleanOutcome.Unchanged)
            {
                Balloon("Nothing was pasted. " + OutcomeText(LastOutcome));
                return;
            }
            uint sequence = NativeClipboard.GetClipboardSequenceNumber();
            if (GetForegroundWindow() != pasteTarget || sequence != NativeClipboard.GetClipboardSequenceNumber()) return;
            INPUT[] input = { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
            uint sent = SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT)));
            if (sent > 0 && sent < input.Length)
            {
                // A partially inserted sequence must not leave our synthetic keys down.
                INPUT[] release = sent == 2 ? new[] { Key(0x56, true), Key(0x11, true) } : new[] { Key(0x11, true) };
                SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(INPUT)));
            }
            if (sent != input.Length)
                Balloon("Windows blocked the paste shortcut. The clipboard was checked; paste manually. Elevated apps may block this shortcut.");
        }

        void ShowStatus()
        {
            MessageBox.Show("Automatic cleaning: " + (Paused ? "paused" : "active") + "\r\nCopies cleaned this session: " + CleanedCount +
                "\r\nLast clipboard check: " + OutcomeText(LastOutcome) +
                "\r\nClean-before-paste shortcut: " + (hotkeyRegistered ? "Ctrl+Alt+V" : "unavailable (another app may use it)") +
                "\r\n\r\nRemoves only si from YouTube links, preserving timestamps, playlists and surrounding text. Matching rich text becomes plain text. Files, images, sensitive excluded items and text over 1 MiB are skipped." +
                "\r\n\r\nClipboard notifications are asynchronous. Ctrl+Alt+V checks again before pasting into the focused app. It does not send a message or press Enter." +
                "\r\n\r\nRuns locally. No network requests, telemetry or clipboard logs. This removes a URL parameter; it does not provide anonymity or erase clipboard history.",
                "YouTube Link Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string OutcomeText(CleanOutcome value)
        {
            switch (value)
            {
                case CleanOutcome.Cleaned: return "YouTube si parameter removed.";
                case CleanOutcome.Unchanged: return "Text needed no changes.";
                case CleanOutcome.Busy: return "Clipboard is busy; try copying again.";
                case CleanOutcome.NonText: return "Clipboard has no text.";
                case CleanOutcome.Excluded: return "Sensitive/excluded clipboard item was left alone.";
                case CleanOutcome.TooLarge: return "Clipboard text exceeds the 1 MiB limit.";
                case CleanOutcome.Unsupported: return "File, image or embedded object was left alone.";
                default: return "Clipboard could not be processed; try copying again.";
            }
        }

        void Balloon(string message)
        {
            if (tray != null) { tray.BalloonTipTitle = "YouTube Link Cleaner"; tray.BalloonTipText = message; tray.ShowBalloonTip(4000); }
        }

        static Icon MakeIcon(Color color)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            using (var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.FillEllipse(brush, 1, 1, 30, 30);
                graphics.DrawString("Y", font, Brushes.White, 6, 2);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                disposed = true;
                retryTimer.Dispose(); pasteTimer.Dispose();
                if (hotkeyRegistered) UnregisterHotKey(window.Handle, 1);
                RemoveClipboardFormatListener(window.Handle);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose();
                if (activeIcon != null) activeIcon.Dispose();
                if (pausedIcon != null) pausedIcon.Dispose();
                window.DestroyHandle();
            }
            base.Dispose(disposing);
        }

        static INPUT Key(ushort key, bool up)
        { return new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { wVk = key, dwFlags = up ? 2u : 0u } } }; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] input, int size);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }

    sealed class CleanerWindow : NativeWindow
    {
        public const string WindowTitle = "Barnacle.YouTubeLinkCleaner.v1.Control";
        public event Action ClipboardChanged, ExitRequested, PauseRequested, ResumeRequested, StatusRequested, PasteRequested;
        public CleanerWindow() { CreateHandle(new CreateParams { Caption = WindowTitle, Style = unchecked((int)0x80000000), ExStyle = 0x80 }); }
        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case 0x031D: if (ClipboardChanged != null) ClipboardChanged(); break;
                case 0x0312: if (PasteRequested != null) PasteRequested(); break;
                case 0x0010: if (ExitRequested != null) ExitRequested(); return;
                case 0x8001: if (PauseRequested != null) PauseRequested(); break;
                case 0x8002: if (ResumeRequested != null) ResumeRequested(); break;
                case 0x8003: if (StatusRequested != null) StatusRequested(); break;
                case 0x8004: message.Result = new IntPtr(1); return;
            }
            base.WndProc(ref message);
        }
    }
}
