using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Barnacle
{
    internal static class ClipboardIntegrationTests
    {
        static int checks;
        static IntPtr testWindow;
        internal static Action BeforeClipboardAccess, AfterClipboardAccess;
        internal static Action<string> StageChanged { get; set; }
        static int operationIndex;
        const string Dirty = "https://youtu.be/dQw4w9WgXcQ?si=private&t=69";
        const string Clean = "https://youtu.be/dQw4w9WgXcQ?t=69";

        [STAThread]
        public static int Main()
        {
            IntPtr originalStation = GetProcessWindowStation();
            IntPtr originalDesktop = GetThreadDesktop(GetCurrentThreadId());
            IntPtr station = IntPtr.Zero, desktop = IntPtr.Zero;
            try
            {
                string name = "BarnacleTests_" + Guid.NewGuid().ToString("N");
                station = CreateWindowStation(name, 0, 0x0000003F, IntPtr.Zero);
                RequireNative(station != IntPtr.Zero, "create isolated window station");
                RequireNative(SetProcessWindowStation(station), "select isolated window station");
                RequireNative(GetProcessWindowStation() == station, "verify isolated station");
                desktop = CreateDesktop("BarnacleTests", IntPtr.Zero, IntPtr.Zero, 0, 0x000001FF, IntPtr.Zero);
                RequireNative(desktop != IntPtr.Zero, "create isolated desktop");
                RequireNative(SetThreadDesktop(desktop), "select isolated desktop");
                testWindow = CreateWindowEx(0, "STATIC", "Barnacle clipboard test owner", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                RequireNative(testWindow != IntPtr.Zero, "create isolated clipboard owner window");
                RunNativeTests();
                RunListenerTests();
                Console.WriteLine("PASS: " + checks + " integration checks on an isolated Windows window station; live clipboard untouched.");
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL: " + error.Message);
                Console.WriteLine("No fallback to the user's window station was attempted.");
                return 3;
            }
            finally
            {
                if (testWindow != IntPtr.Zero) DestroyWindow(testWindow);
                // Restore only thread/process associations after destroying test windows.
                // Clipboard data belongs to the private station and is never copied out.
                if (originalDesktop != IntPtr.Zero) SetThreadDesktop(originalDesktop);
                if (originalStation != IntPtr.Zero) SetProcessWindowStation(originalStation);
                if (desktop != IntPtr.Zero) CloseDesktop(desktop);
                if (station != IntPtr.Zero) CloseWindowStation(station);
            }
        }

        internal static int RunOnExistingStation(IntPtr owner, Action before, Action after)
        {
            testWindow = owner;
            BeforeClipboardAccess = before; AfterClipboardAccess = after;
            try { RunNativeTests(); RunListenerTests(); return checks; }
            finally { BeforeClipboardAccess = null; AfterClipboardAccess = null; }
        }

        static void RunNativeTests()
        {
            WriteText(Dirty);
            Equal(CleanOutcome.Cleaned, TryCleanEventually(), "native cleaning");
            Equal(Clean, ReadText(), "timestamp survives native cleaning");

            string message = "\uD83E\uDD80 hello \u65E5\u672C\u8A9E\r\n" + Dirty + "\nSecond https://www.youtube.com/watch?v=abc&list=PL1&si=x&index=2";
            string expected = "\uD83E\uDD80 hello \u65E5\u672C\u8A9E\r\n" + Clean + "\nSecond https://www.youtube.com/watch?v=abc&list=PL1&index=2";
            WriteText(message);
            Equal(CleanOutcome.Cleaned, TryCleanEventually(), "unicode message cleaning");
            Equal(expected, ReadText(), "unicode, message, multiple links preserved");

            WriteText("ordinary text https://example.com/?si=leave");
            uint unchanged = NativeClipboard.GetClipboardSequenceNumber();
            Equal(CleanOutcome.Unchanged, TryCleanEventually(), "foreign URL unchanged");
            Equal(unchanged, NativeClipboard.GetClipboardSequenceNumber(), "unchanged text does not replace clipboard");

            WriteText(Dirty, new FormatData(8, new byte[44]));
            uint imageSequence = NativeClipboard.GetClipboardSequenceNumber();
            Equal(CleanOutcome.Unsupported, TryCleanEventually(), "image copy skipped");
            Equal(Dirty, ReadText(), "image text left unchanged");
            Equal(imageSequence, NativeClipboard.GetClipboardSequenceNumber(), "image formats not rewritten");

            uint png = NativeClipboard.RegisterClipboardFormat("PNG");
            WriteText(Dirty, new FormatData(png, new byte[8]));
            Equal(CleanOutcome.Unsupported, TryCleanEventually(), "registered PNG with text skipped");
            Equal(Dirty, ReadText(), "registered PNG text left unchanged");

            uint html = NativeClipboard.RegisterClipboardFormat("HTML Format");
            WriteText(Dirty, new FormatData(html, Encoding.UTF8.GetBytes("<a href=\"" + Dirty + "\">video</a>\0")));
            Equal(CleanOutcome.Cleaned, TryCleanEventually(), "dirty text plus HTML cleaned");
            Equal(Clean, ReadText(), "rich text converted to clean plain text");
            Equal(false, NativeClipboard.IsClipboardFormatAvailable(html), "stale HTML removed");

            WriteText(Dirty, new FormatData(15, new byte[24]));
            Equal(CleanOutcome.Unsupported, TryCleanEventually(), "file copy skipped");
            Equal(Dirty, ReadText(), "file text left unchanged");

            uint history = NativeClipboard.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            uint cloud = NativeClipboard.RegisterClipboardFormat("CanUploadToCloudClipboard");
            WriteText(Dirty, new FormatData(history, BitConverter.GetBytes(0)), new FormatData(cloud, BitConverter.GetBytes(0)));
            Equal(CleanOutcome.Cleaned, TryCleanEventually(), "privacy metadata copy cleaned");
            Equal(0, ReadDword(history), "history exclusion preserved");
            Equal(0, ReadDword(cloud), "cloud exclusion preserved");

            uint excluded = NativeClipboard.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            WriteText(Dirty, new FormatData(excluded, new byte[] { 0 }));
            Equal(CleanOutcome.Excluded, TryCleanEventually(), "sensitive excluded copy skipped");
            Equal(Dirty, ReadText(), "sensitive copy unchanged");

            WriteText(Dirty + new string('a', NativeClipboard.MaxBytes));
            Equal(CleanOutcome.TooLarge, TryCleanEventually(), "large text cap enforced");

            WriteRawUnterminated();
            // Windows may add a terminator while materializing CF_UNICODETEXT.
            // Both rejecting the raw block and leaving normalized text alone
            // are safe; OS normalization may itself increment the sequence.
            CleanOutcome unterminated = TryCleanEventually();
            Equal(true, unterminated == CleanOutcome.Failed || unterminated == CleanOutcome.Unchanged,
                "unterminated or OS-normalized text is never cleaned");

            byte[] validTail = Encoding.Unicode.GetBytes(Dirty + "\0");
            byte[] invalidUtf16 = new byte[validTail.Length + 2];
            invalidUtf16[0] = 0; invalidUtf16[1] = 0xD8; // Unpaired high surrogate.
            Buffer.BlockCopy(validTail, 0, invalidUtf16, 2, validTail.Length);
            WriteUnicodeBytes(invalidUtf16);
            uint invalidSequence = NativeClipboard.GetClipboardSequenceNumber();
            Equal(CleanOutcome.Failed, TryCleanEventually(), "invalid UTF16 rejected without lossy replacement");
            Equal(invalidSequence, NativeClipboard.GetClipboardSequenceNumber(), "invalid UTF16 not rewritten");

            WriteText(Dirty);
            using (var acquired = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                Exception threadError = null;
                var locker = new Thread(delegate()
                {
                    bool opened = false;
                    try
                    {
                        RequireNative(SetThreadDesktop(GetThreadDesktop(GetCurrentThreadId())), "locker desktop available");
                        opened = NativeClipboard.OpenClipboard(IntPtr.Zero);
                        RequireNative(opened, "locker opens isolated clipboard");
                        acquired.Set();
                        release.WaitOne(3000);
                    }
                    catch (Exception e) { threadError = e; acquired.Set(); }
                    finally { if (opened) NativeClipboard.CloseClipboard(); }
                });
                locker.Start();
                RequireNative(acquired.WaitOne(3000), "clipboard locker ready");
                try
                {
                    if (threadError != null) throw threadError;
                    Equal(CleanOutcome.Busy, NativeClipboard.TryClean(testWindow), "locked clipboard returns busy");
                }
                finally { release.Set(); locker.Join(3000); }
                if (threadError != null) throw threadError;
            }
            Equal(CleanOutcome.Cleaned, TryCleanEventually(), "cleaning recovers after contention");
        }

        static void RunListenerTests()
        {
            WriteText("listener starting");
            using (var context = new CleanerContext(true, false))
            {
                WriteText(Dirty);
                PumpUntil(delegate { return context.CleanedCount >= 1; }, "listener event cleans copy");
                Equal(Clean, ReadText(), "listener cleaned clipboard");
                int count = context.CleanedCount;

                WriteText(Dirty);
                WriteText("latest ordinary copy");
                PumpFor(150);
                Equal("latest ordinary copy", ReadText(), "queued old notification cannot overwrite newer copy");
                Equal(count, context.CleanedCount, "obsolete dirty content not resurrected");

                context.SetPaused(true);
                WriteText(Dirty);
                PumpFor(100);
                Equal(Dirty, ReadText(), "pause prevents automatic cleaning");
                context.SetPaused(false);
                PumpUntil(delegate { return context.CleanedCount > count; }, "resume checks current contents");
                Equal(Clean, ReadText(), "resume cleaned clipboard");
                // Keep ownership with the runner while destroying the listener window.
                WriteText("listener disposal sentinel");
            }
            WriteText(Dirty);
            PumpFor(80);
            Equal(Dirty, ReadText(), "disposed listener no longer processes copies");
        }

        static void PumpUntil(Func<bool> done, string name)
        {
            int start = Environment.TickCount;
            while (!done() && unchecked(Environment.TickCount - start) < 2500)
            { Application.DoEvents(); Thread.Sleep(5); }
            Equal(true, done(), name);
        }
        static void PumpFor(int milliseconds)
        {
            int start = Environment.TickCount;
            while (unchecked(Environment.TickCount - start) < milliseconds)
            { Application.DoEvents(); Thread.Sleep(5); }
        }
        static void Equal<T>(T expected, T actual, string name)
        {
            checks++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(name + ": assertion failed (clipboard contents omitted)");
        }
        static void RequireNative(bool valid, string operation)
        {
            if (!valid) throw new InvalidOperationException(operation + " (Win32 " + Marshal.GetLastWin32Error() + ")");
        }
        static bool OpenTestClipboard()
        {
            int start = Environment.TickCount;
            do
            {
                if (NativeClipboard.OpenClipboard(testWindow)) return true;
                Thread.Sleep(5);
            }
            while (unchecked(Environment.TickCount - start) < 1000);
            return false;
        }
        static CleanOutcome TryCleanEventually()
        {
            int start = Environment.TickCount;
            CleanOutcome result;
            do
            {
                result = NativeClipboard.TryClean(testWindow);
                if (result != CleanOutcome.Busy) return result;
                Thread.Sleep(5);
            }
            while (unchecked(Environment.TickCount - start) < 1000);
            return result;
        }
        sealed class FormatData
        {
            public uint Format; public byte[] Bytes;
            public FormatData(uint format, byte[] bytes) { Format = format; Bytes = bytes; }
        }
        static void WriteText(string text, params FormatData[] extra)
        {
            if (StageChanged != null) StageChanged("write-text-" + (++operationIndex));
            RequireNative(OpenTestClipboard(), "open isolated clipboard for test write");
            try
            {
                if (BeforeClipboardAccess != null) BeforeClipboardAccess();
                RequireNative(NativeClipboard.EmptyClipboard(), "empty isolated clipboard");
                PutPrivacyFlags(extra);
                Put(13, Encoding.Unicode.GetBytes(text + "\0"));
                foreach (FormatData format in extra) Put(format.Format, format.Bytes);
            }
            finally { try { if (AfterClipboardAccess != null) AfterClipboardAccess(); } finally { NativeClipboard.CloseClipboard(); } }
        }
        static void WriteUnicodeBytes(byte[] bytes)
        {
            if (StageChanged != null) StageChanged("write-raw-" + (++operationIndex));
            RequireNative(OpenTestClipboard(), "open raw isolated clipboard");
            try
            {
                if (BeforeClipboardAccess != null) BeforeClipboardAccess();
                RequireNative(NativeClipboard.EmptyClipboard(), "empty raw isolated clipboard");
                PutPrivacyFlags(new FormatData[0]);
                Put(13, bytes);
            }
            finally { try { if (AfterClipboardAccess != null) AfterClipboardAccess(); } finally { NativeClipboard.CloseClipboard(); } }
        }
        static void PutPrivacyFlags(FormatData[] extra)
        {
            foreach (string name in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            {
                uint format = NativeClipboard.RegisterClipboardFormat(name);
                bool supplied = false;
                foreach (FormatData value in extra) if (value.Format == format) supplied = true;
                if (!supplied) Put(format, BitConverter.GetBytes(0));
            }
        }
        static void Put(uint format, byte[] bytes)
        {
            IntPtr memory = NativeClipboard.GlobalAlloc(0x42, (UIntPtr)bytes.Length);
            RequireNative(memory != IntPtr.Zero, "allocate test memory");
            try
            {
                IntPtr ptr = NativeClipboard.GlobalLock(memory);
                RequireNative(ptr != IntPtr.Zero, "lock test memory");
                try { Marshal.Copy(bytes, 0, ptr, bytes.Length); }
                finally { NativeClipboard.GlobalUnlock(memory); }
                RequireNative(NativeClipboard.SetClipboardData(format, memory) != IntPtr.Zero, "put isolated clipboard format");
                memory = IntPtr.Zero;
            }
            finally { if (memory != IntPtr.Zero) NativeClipboard.GlobalFree(memory); }
        }
        static string ReadText()
        {
            if (StageChanged != null) StageChanged("read-text-" + (++operationIndex));
            RequireNative(OpenTestClipboard(), "open isolated clipboard for read");
            try
            {
                if (BeforeClipboardAccess != null) BeforeClipboardAccess();
                IntPtr memory = NativeClipboard.GetClipboardData(13);
                RequireNative(memory != IntPtr.Zero, "get test text");
                IntPtr pointer = NativeClipboard.GlobalLock(memory);
                RequireNative(pointer != IntPtr.Zero, "lock test text");
                try { return Marshal.PtrToStringUni(pointer); }
                finally { NativeClipboard.GlobalUnlock(memory); }
            }
            finally { try { if (AfterClipboardAccess != null) AfterClipboardAccess(); } finally { NativeClipboard.CloseClipboard(); } }
        }
        static int ReadDword(uint format)
        {
            if (StageChanged != null) StageChanged("read-metadata-" + (++operationIndex));
            RequireNative(OpenTestClipboard(), "open metadata");
            try
            {
                if (BeforeClipboardAccess != null) BeforeClipboardAccess();
                IntPtr memory = NativeClipboard.GetClipboardData(format);
                RequireNative(memory != IntPtr.Zero, "get preserved metadata");
                IntPtr pointer = NativeClipboard.GlobalLock(memory);
                RequireNative(pointer != IntPtr.Zero, "lock metadata");
                try { return Marshal.ReadInt32(pointer); }
                finally { NativeClipboard.GlobalUnlock(memory); }
            }
            finally { try { if (AfterClipboardAccess != null) AfterClipboardAccess(); } finally { NativeClipboard.CloseClipboard(); } }
        }
        static void WriteRawUnterminated()
        {
            if (StageChanged != null) StageChanged("write-unterminated-" + (++operationIndex));
            RequireNative(OpenTestClipboard(), "open malformed test clipboard");
            IntPtr memory = IntPtr.Zero;
            try
            {
                if (BeforeClipboardAccess != null) BeforeClipboardAccess();
                RequireNative(NativeClipboard.EmptyClipboard(), "empty malformed test clipboard");
                PutPrivacyFlags(new FormatData[0]);
                memory = NativeClipboard.GlobalAlloc(0x42, (UIntPtr)64);
                RequireNative(memory != IntPtr.Zero, "allocate malformed test memory");
                int size = checked((int)NativeClipboard.GlobalSize(memory).ToUInt64());
                byte[] bytes = new byte[size];
                for (int i = 0; i < size; i++) bytes[i] = 65;
                IntPtr pointer = NativeClipboard.GlobalLock(memory);
                RequireNative(pointer != IntPtr.Zero, "lock malformed test memory");
                try { Marshal.Copy(bytes, 0, pointer, size); }
                finally { NativeClipboard.GlobalUnlock(memory); }
                RequireNative(NativeClipboard.SetClipboardData(13, memory) != IntPtr.Zero, "put malformed test format");
                memory = IntPtr.Zero;
            }
            finally
            {
                if (memory != IntPtr.Zero) NativeClipboard.GlobalFree(memory);
                try { if (AfterClipboardAccess != null) AfterClipboardAccess(); } finally { NativeClipboard.CloseClipboard(); }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowStation(string name, uint flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetProcessWindowStation(IntPtr station);
        [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr deviceMode, uint flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll")] static extern bool CloseWindowStation(IntPtr station);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    }
}
