using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Barnacle
{
    // Explicit opt-in only. Never saves, displays, or logs original clipboard data.
    // This runner accepts exactly the five known image formats, snapshots every
    // format before mutation, and aborts on any unexpected clipboard sequence.
    internal static class PreservedClipboardTestRunner
    {
        const int MaxSnapshotBytes = 128 * 1024 * 1024;
        static readonly List<SavedFormat> saved = new List<SavedFormat>();
        static IntPtr ownerWindow;
        static uint expectedSequence;
        static bool interference, mutated, snapshotComplete;
        static bool preserveImageData;
        static List<uint> originalFormats;
        static string stage = "starting";
        static readonly Queue<string> diagnostics = new Queue<string>();
        static EventWaitHandle recoveryRetry, recoveryDiscard;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length != 1 || (args[0] != "--preserve-known-image" && args[0] != "--preserve-image-data"))
            {
                Console.WriteLine("Not run. Requires --preserve-known-image or explicit --preserve-image-data opt-in.");
                return 2;
            }
            preserveImageData = args[0] == "--preserve-image-data";
#if !CLIPBOARD_TEST_GUARDS
            Console.WriteLine("Not run. Compile with CLIPBOARD_TEST_GUARDS to enable atomic clipboard guards.");
            return 2;
#else
            int result = 3, checks = 0;
            string failure = null, restoreFailure = null;
            bool retainBackup = false;
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                recoveryRetry = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Barnacle.ClipboardRecovery.Retry." + pid);
                recoveryDiscard = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Barnacle.ClipboardRecovery.Discard." + pid);
                ownerWindow = CreateWindowEx(0, "STATIC", "Barnacle preserved clipboard test owner", 0,
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Require(ownerWindow != IntPtr.Zero, "Could not create test owner window");
                stage = "snapshot"; Snapshot();
                NativeClipboard.TestBeforeReadWrite = GuardUnderLock;
                NativeClipboard.TestAfterReadWrite = RecordUnderLock;
                ClipboardIntegrationTests.StageChanged = delegate(string value) { stage = value; };
                stage = "tests";
                checks = ClipboardIntegrationTests.RunOnExistingStation(ownerWindow, GuardUnderLock, RecordUnderLock);
                Require(!interference, "Clipboard changed externally; test stopped");
                result = 0;
            }
            catch (Exception error)
            {
                // All exception text is an operation label; content comparisons omit values.
                failure = error.Message;
                Diagnostic("initial failure");
            }
            finally
            {
                NativeClipboard.TestBeforeReadWrite = null;
                NativeClipboard.TestAfterReadWrite = null;
                ClipboardIntegrationTests.StageChanged = null;
                if (snapshotComplete)
                {
                    try
                    {
                        stage = "restore"; RestoreAndVerify();
                        Console.WriteLine(preserveImageData ?
                            "PASS: original PNG/DIB/DIBV5 bytes and format set verified; Windows regenerated CF_BITMAP (order may differ)." :
                            "PASS: original clipboard formats, order, and in-memory content hashes verified.");
                    }
                    catch (Exception error)
                    {
                        result = 4;
                        restoreFailure = error.Message;
                        retainBackup = true;
                        Diagnostic("restore failure");
                    }
                }
                if (failure != null) Console.WriteLine("INITIAL FAILURE: " + failure);
                if (restoreFailure != null) Console.WriteLine("RESTORE FAILURE: " + restoreFailure);
                if (failure != null || restoreFailure != null)
                    foreach (string line in diagnostics) Console.WriteLine("DIAGNOSTIC: " + line);
                if (retainBackup) RetainForRecovery();
                foreach (SavedFormat item in saved) item.Dispose();
                if (ownerWindow != IntPtr.Zero) DestroyWindow(ownerWindow);
                if (recoveryRetry != null) recoveryRetry.Dispose();
                if (recoveryDiscard != null) recoveryDiscard.Dispose();
            }
            if (result == 0) Console.WriteLine("PASS: " + checks + " native clipboard and listener integration checks.");
            else Console.WriteLine("STOPPED: " + (failure ?? "Native tests unavailable"));
            return result;
#endif
        }

        static void Snapshot()
        {
            Require(NativeClipboard.OpenClipboard(ownerWindow), "Clipboard busy before snapshot; no mutation attempted");
            try
            {
                uint png = NativeClipboard.RegisterClipboardFormat("PNG");
                uint html = NativeClipboard.RegisterClipboardFormat("HTML Format");
                List<uint> formats = Enumerate();
                var allowed = new HashSet<uint>(new uint[] { png, 17, html, 2, 8 });
                var fourAllowed = new HashSet<uint>(new uint[] { png, 17, 2, 8 });
                Require((formats.Count == 5 && new HashSet<uint>(formats).SetEquals(allowed)) ||
                    (formats.Count == 4 && new HashSet<uint>(formats).SetEquals(fourAllowed)),
                    "Clipboard formats differ from the known four/five-format image; no mutation attempted");
                originalFormats = new List<uint>(formats);
                long total = 0;
                foreach (uint format in formats)
                {
                    // Explicit image-data mode preserves all raw image payloads.
                    // Windows regenerates its native CF_BITMAP representation.
                    if (preserveImageData && format == 2) continue;
                    IntPtr original = NativeClipboard.GetClipboardData(format);
                    Require(original != IntPtr.Zero, "Clipboard snapshot could not materialize a format");
                    var item = new SavedFormat { Format = format };
                    saved.Add(item);
                    if (format == 2)
                    {
                        item.Hash = HashBitmap(original);
                        item.Handle = CopyImage(original, 0, 0, 0, 0x2000);
                        Require(item.Handle != IntPtr.Zero && item.Handle != original, "Could not duplicate bitmap for restoration");
                        Require(Same(item.Hash, HashBitmap(item.Handle)), "Bitmap duplicate differs; no mutation attempted");
                    }
                    else
                    {
                        byte[] bytes = ReadGlobal(original);
                        total += bytes.Length;
                        Require(total <= MaxSnapshotBytes, "Snapshot exceeds conservative memory limit");
                        item.Hash = Hash(bytes);
                        item.Handle = Allocate(bytes);
                        Require(Same(item.Hash, Hash(ReadGlobal(item.Handle))), "Memory duplicate differs; no mutation attempted");
                    }
                }
                Require(SameFormats(formats, Enumerate()), "Snapshot changed available formats; no mutation attempted");
                expectedSequence = NativeClipboard.GetClipboardSequenceNumber();
                snapshotComplete = true;
                Diagnostic("snapshot ready");
            }
            finally { NativeClipboard.CloseClipboard(); }
        }

        // Both hooks execute while the native clipboard lock is held.
        static void GuardUnderLock()
        {
            uint now = NativeClipboard.GetClipboardSequenceNumber();
            // Windows may commit the sequence increment only when CloseClipboard
            // releases the write. Accept that increment only for our own owner.
            if (!interference && now != expectedSequence && IsOwnOwner())
            {
                expectedSequence = now;
                mutated = true;
                Diagnostic("accepted own committed sequence");
            }
            if (interference || now != expectedSequence)
            {
                interference = true;
                Diagnostic("external sequence rejected");
                throw new InvalidOperationException("External clipboard change detected; no further test writes permitted");
            }
        }
        static void RecordUnderLock()
        {
            if (interference) return;
            uint now = NativeClipboard.GetClipboardSequenceNumber();
            if (IsOwnOwner())
            {
                expectedSequence = now;
                mutated = true;
                Diagnostic("recorded own transaction");
            }
            else if (now != expectedSequence)
            {
                interference = true;
                Diagnostic("external owner rejected");
                throw new InvalidOperationException("Clipboard ownership changed externally; no further writes permitted");
            }
        }

        static void RestoreAndVerify()
        {
            bool opened = false;
            for (int attempt = 0; attempt < 40 && !opened; attempt++)
            {
                opened = NativeClipboard.OpenClipboard(ownerWindow);
                if (!opened) Thread.Sleep(10);
            }
            Require(opened, "Could not open clipboard for restoration");
            try
            {
                // An earlier conservative sequence mismatch may have been our
                // own commit. Actual ownership makes restoration safe to retry.
                if (IsOwnOwner()) interference = false;
                Require(!interference, "Original clipboard not restored because an external change was detected");
                GuardUnderLock();
                if (mutated)
                {
                    IntPtr currentOwner = GetClipboardOwner();
                    uint pid;
                    GetWindowThreadProcessId(currentOwner, out pid);
                    Require(currentOwner == IntPtr.Zero || pid == (uint)Process.GetCurrentProcess().Id,
                        "Original clipboard not restored because another process owns the current copy");
                    // Never transfer the permanent backup handles. A failed
                    // partial restoration still leaves a complete native copy.
                    var attemptCopies = new List<SavedFormat>();
                    try
                    {
                        foreach (SavedFormat original in saved)
                        {
                            var copy = new SavedFormat { Format = original.Format };
                            attemptCopies.Add(copy);
                            copy.Handle = original.Format == 2 ? CopyImage(original.Handle, 0, 0, 0, 0x2000) : Allocate(ReadGlobal(original.Handle));
                            Require(copy.Handle != IntPtr.Zero, "Could not prepare independent restoration handle");
                        }
                        Require(NativeClipboard.EmptyClipboard(), "Could not clear test clipboard for restoration");
                        foreach (SavedFormat item in attemptCopies)
                        {
                            bool restored = false;
                            for (int attempt = 0; attempt < 3 && !restored; attempt++)
                                restored = NativeClipboard.SetClipboardData(item.Format, item.Handle) != IntPtr.Zero;
                            Require(restored, "A clipboard format could not be restored");
                            item.Handle = IntPtr.Zero;
                        }
                    }
                    finally { foreach (SavedFormat item in attemptCopies) item.Dispose(); }
                }
                var expectedFormats = originalFormats;
                if (!preserveImageData)
                    Require(SameFormats(expectedFormats, Enumerate()), "Restored clipboard format order differs");
                foreach (SavedFormat item in saved)
                {
                    IntPtr handle = NativeClipboard.GetClipboardData(item.Format);
                    Require(handle != IntPtr.Zero, "Restored clipboard format unavailable");
                    byte[] actual = item.Format == 2 ? HashBitmap(handle) : Hash(ReadGlobal(handle));
                    Require(Same(item.Hash, actual), "Restored clipboard content hash differs");
                }
                if (!preserveImageData)
                    Require(SameFormats(expectedFormats, Enumerate()), "Verification altered available clipboard formats");
            }
            finally { NativeClipboard.CloseClipboard(); }
            if (preserveImageData) VerifyRegeneratedImage();
        }

        static void VerifyRegeneratedImage()
        {
            Require(NativeClipboard.OpenClipboard(ownerWindow), "Could not open restored image for format-set verification");
            try
            {
                GuardUnderLock();
                Require(new HashSet<uint>(Enumerate()).SetEquals(originalFormats),
                    "Restored image format set differs after bitmap regeneration");
                Require(NativeClipboard.IsClipboardFormatAvailable(2), "Windows did not expose regenerated CF_BITMAP");
                foreach (SavedFormat item in saved)
                {
                    IntPtr handle = NativeClipboard.GetClipboardData(item.Format);
                    Require(handle != IntPtr.Zero && Same(item.Hash, Hash(ReadGlobal(handle))),
                        "Restored raw image format hash differs after close");
                }
            }
            finally { NativeClipboard.CloseClipboard(); }
        }

        static bool IsOwnOwner()
        {
            IntPtr owner = GetClipboardOwner();
            uint pid;
            GetWindowThreadProcessId(owner, out pid);
            return owner != IntPtr.Zero && pid == (uint)Process.GetCurrentProcess().Id;
        }
        static void Diagnostic(string label)
        {
            IntPtr owner = GetClipboardOwner();
            uint pid;
            GetWindowThreadProcessId(owner, out pid);
            string line = label + "; stage=" + stage + "; expected=" + expectedSequence +
                "; actual=" + NativeClipboard.GetClipboardSequenceNumber() + "; owner=" + owner.ToInt64() +
                "; ownerPid=" + pid + "; mutated=" + mutated + "; interference=" + interference;
            diagnostics.Enqueue(line);
            while (diagnostics.Count > 16) diagnostics.Dequeue();
        }
        static void RetainForRecovery()
        {
            int pid = Process.GetCurrentProcess().Id;
            Console.WriteLine("BACKUP RETAINED in process " + pid + "; formats=" + saved.Count + ". No clipboard content was saved to disk.");
            Console.WriteLine("Signal Local\\Barnacle.ClipboardRecovery.Retry." + pid + " to retry safe restoration.");
            Console.WriteLine("Signal Local\\Barnacle.ClipboardRecovery.Discard." + pid + " to explicitly discard the backup.");
            Console.Out.Flush();
            while (true)
            {
                int signal = WaitHandle.WaitAny(new WaitHandle[] { recoveryRetry, recoveryDiscard }, 60000);
                if (signal == 1) { Console.WriteLine("Backup discarded by explicit recovery signal."); return; }
                if (signal == 0)
                {
                    try { stage = "recovery"; RestoreAndVerify(); Console.WriteLine("PASS: recovery restoration and original hashes verified."); return; }
                    catch (Exception error) { Console.WriteLine("Recovery not applied: " + error.Message); }
                }
                else Console.WriteLine("BACKUP STILL RETAINED in process " + pid + "; awaiting explicit recovery or discard.");
                Console.Out.Flush();
            }
        }

        static List<uint> Enumerate()
        {
            var formats = new List<uint>();
            uint next = 0;
            while ((next = EnumClipboardFormats(next)) != 0)
            {
                Require(formats.Count < 32, "Unexpected clipboard format count");
                formats.Add(next);
            }
            return formats;
        }
        static bool SameFormats(List<uint> first, List<uint> second)
        {
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++) if (first[i] != second[i]) return false;
            return true;
        }
        static byte[] ReadGlobal(IntPtr handle)
        {
            ulong size = NativeClipboard.GlobalSize(handle).ToUInt64();
            Require(size > 0 && size <= MaxSnapshotBytes, "Clipboard format has an unsupported size");
            IntPtr pointer = NativeClipboard.GlobalLock(handle);
            Require(pointer != IntPtr.Zero, "Could not lock clipboard memory for snapshot");
            try
            {
                byte[] bytes = new byte[(int)size];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { NativeClipboard.GlobalUnlock(handle); }
        }
        static IntPtr Allocate(byte[] bytes)
        {
            IntPtr handle = NativeClipboard.GlobalAlloc(0x42, (UIntPtr)bytes.Length);
            Require(handle != IntPtr.Zero, "Could not allocate clipboard restoration buffer");
            IntPtr pointer = NativeClipboard.GlobalLock(handle);
            if (pointer == IntPtr.Zero) { NativeClipboard.GlobalFree(handle); throw new InvalidOperationException("Could not lock restoration buffer"); }
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { NativeClipboard.GlobalUnlock(handle); }
            return handle;
        }
        static byte[] HashBitmap(IntPtr handle)
        {
            BITMAP bitmap;
            Require(GetObject(handle, Marshal.SizeOf(typeof(BITMAP)), out bitmap) != 0,
                "Could not inspect native bitmap attributes");
            long size = (long)bitmap.width * bitmap.height * 4;
            Require(bitmap.width > 0 && bitmap.height > 0 && size > 0 && size <= MaxSnapshotBytes,
                "Bitmap exceeds conservative snapshot limit");
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            Require(dc != IntPtr.Zero, "Could not create bitmap snapshot device context");
            try
            {
                var info = new BITMAPINFO();
                info.header.size = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                info.header.width = bitmap.width;
                info.header.height = -bitmap.height; // Canonical top-down scanlines.
                info.header.planes = 1;
                info.header.bitCount = 32;
                info.header.imageSize = (uint)size;
                byte[] pixels = new byte[(int)size];
                Require(GetDIBits(dc, handle, 0, (uint)bitmap.height, pixels, ref info, 0) == bitmap.height,
                    "Could not copy native bitmap pixels for verification");
                // BI_RGB's fourth byte has no defined alpha semantics for a
                // CF_BITMAP. Exact alpha remains in the raw DIB/DIBV5 snapshots.
                for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 0;
                byte[] bytes = new byte[pixels.Length + 8];
                Buffer.BlockCopy(BitConverter.GetBytes(bitmap.width), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(bitmap.height), 0, bytes, 4, 4);
                Buffer.BlockCopy(pixels, 0, bytes, 8, pixels.Length);
                return Hash(bytes);
            }
            finally { DeleteDC(dc); }
        }
        static byte[] Hash(byte[] bytes) { using (SHA256 algorithm = SHA256.Create()) return algorithm.ComputeHash(bytes); }
        static bool Same(byte[] first, byte[] second)
        {
            if (first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false;
            return true;
        }
        static void Require(bool valid, string label) { if (!valid) throw new InvalidOperationException(label); }
        sealed class SavedFormat : IDisposable
        {
            public uint Format;
            public IntPtr Handle;
            public byte[] Hash;
            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    if (Format == 2) DeleteObject(Handle); else NativeClipboard.GlobalFree(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint previous);
        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CopyImage(IntPtr image, uint type, int width, int height, uint flags);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr image);
        [StructLayout(LayoutKind.Sequential)] struct BITMAP
        {
            public int type, width, height, widthBytes;
            public ushort planes, bitsPixel;
            public IntPtr bits;
        }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
        {
            public uint size;
            public int width, height;
            public ushort planes, bitCount;
            public uint compression, imageSize;
            public int xPixelsPerMeter, yPixelsPerMeter;
            public uint colorsUsed, colorsImportant;
        }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO
        { public BITMAPINFOHEADER header; public uint color; }
        [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)] static extern int GetObject(IntPtr bitmap, int size, out BITMAP attributes);
        [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateCompatibleDC(IntPtr device);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr device);
        [DllImport("gdi32.dll", SetLastError = true)] static extern int GetDIBits(IntPtr device, IntPtr bitmap, uint start, uint lines, [Out] byte[] pixels, ref BITMAPINFO info, uint colorUse);
    }
}
