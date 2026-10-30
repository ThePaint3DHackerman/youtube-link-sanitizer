using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Barnacle
{
    public enum CleanOutcome { Unchanged, Cleaned, Busy, NonText, Excluded, TooLarge, Unsupported, Failed }

    public static class NativeClipboard
    {
        public const int MaxBytes = 1024 * 1024;
        const uint UnicodeText = 13;
        static readonly uint ExcludedFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        static readonly uint HistoryFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
        static readonly uint CloudFormat = RegisterClipboardFormat("CanUploadToCloudClipboard");
        static readonly uint[] ObjectFormats = { 2, 3, 8, 9, 14, 15, 17 };
#if CLIPBOARD_TEST_GUARDS
        // Test-only hooks check/update clipboard sequence while its native lock is held.
        public static Action TestBeforeReadWrite;
        public static Action TestAfterReadWrite;
#endif

        // One exclusive clipboard transaction. No cached text can overwrite a newer copy.
        public static CleanOutcome TryClean(IntPtr owner)
        {
            if (owner == IntPtr.Zero) return CleanOutcome.Failed;
            if (!OpenClipboard(owner)) return CleanOutcome.Busy;
            IntPtr replacement = IntPtr.Zero, fallback = IntPtr.Zero;
            var metadata = new Dictionary<uint, IntPtr>();
            try
            {
#if CLIPBOARD_TEST_GUARDS
                if (TestBeforeReadWrite != null) TestBeforeReadWrite();
#endif
                if (IsClipboardFormatAvailable(ExcludedFormat)) return CleanOutcome.Excluded;
                foreach (uint format in ObjectFormats)
                    if (IsClipboardFormatAvailable(format)) return CleanOutcome.Unsupported;
                // Embedded OLE/file objects sometimes also advertise a text description.
                foreach (string name in new[] { "FileContents", "FileGroupDescriptorW", "FileGroupDescriptor", "Embed Source", "Embedded Object", "Object Descriptor", "Link Source", "PNG", "JFIF", "GIF", "image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp", "WebP", "TIFF" })
                    if (IsClipboardFormatAvailable(RegisterClipboardFormat(name))) return CleanOutcome.Unsupported;
                if (!IsClipboardFormatAvailable(UnicodeText)) return CleanOutcome.NonText;
                IntPtr handle = GetClipboardData(UnicodeText);
                if (handle == IntPtr.Zero) return CleanOutcome.Failed;
                ulong size = GlobalSize(handle).ToUInt64();
                if (size > MaxBytes) return CleanOutcome.TooLarge;
                if (size < 2 || (size & 1) != 0) return CleanOutcome.Failed;
                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) return CleanOutcome.Failed;
                string original;
                try
                {
                    byte[] bytes = new byte[(int)size];
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    int end = -1;
                    for (int i = 0; i + 1 < bytes.Length; i += 2)
                        if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
                    if (end < 0) return CleanOutcome.Failed;
                    // Reject malformed UTF-16 rather than replace unrelated characters.
                    original = new UnicodeEncoding(false, false, true).GetString(bytes, 0, end);
                }
                finally { GlobalUnlock(handle); }
                string clean = YouTubeSanitizer.Clean(original);
                if (String.Equals(clean, original, StringComparison.Ordinal)) return CleanOutcome.Unchanged;

                // Allocate everything before EmptyClipboard, including an original-text fallback.
                replacement = Allocate(Encoding.Unicode.GetBytes(clean + "\0"));
                fallback = Allocate(Encoding.Unicode.GetBytes(original + "\0"));
                foreach (uint format in new[] { HistoryFormat, CloudFormat })
                {
                    if (!IsClipboardFormatAvailable(format)) continue;
                    IntPtr data = GetClipboardData(format);
                    if (data == IntPtr.Zero || GlobalSize(data).ToUInt64() < 4) return CleanOutcome.Failed;
                    IntPtr ptr = GlobalLock(data);
                    if (ptr == IntPtr.Zero) return CleanOutcome.Failed;
                    byte[] flag = new byte[4];
                    try { Marshal.Copy(ptr, flag, 0, 4); }
                    finally { GlobalUnlock(data); }
                    metadata.Add(format, Allocate(flag));
                }
                if (!EmptyClipboard()) return CleanOutcome.Failed;
                // Changed rich text becomes plain text: stale HTML/RTF URLs cannot bypass cleaning.
                if (SetClipboardData(UnicodeText, replacement) == IntPtr.Zero)
                {
                    if (SetClipboardData(UnicodeText, fallback) != IntPtr.Zero) fallback = IntPtr.Zero;
                    PutMetadata(metadata);
                    return CleanOutcome.Failed;
                }
                replacement = IntPtr.Zero; // Windows now owns it.
                PutMetadata(metadata);
                return CleanOutcome.Cleaned;
            }
            catch (Exception) { return CleanOutcome.Failed; }
            finally
            {
                if (replacement != IntPtr.Zero) GlobalFree(replacement);
                if (fallback != IntPtr.Zero) GlobalFree(fallback);
                foreach (IntPtr value in metadata.Values) if (value != IntPtr.Zero) GlobalFree(value);
                try
                {
#if CLIPBOARD_TEST_GUARDS
                    if (TestAfterReadWrite != null) TestAfterReadWrite();
#endif
                }
                finally { CloseClipboard(); }
            }
        }

        static void PutMetadata(Dictionary<uint, IntPtr> values)
        {
            foreach (uint key in new List<uint>(values.Keys))
                if (SetClipboardData(key, values[key]) != IntPtr.Zero) values[key] = IntPtr.Zero;
        }

        static IntPtr Allocate(byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(0x0042, (UIntPtr)bytes.Length);
            if (memory == IntPtr.Zero) throw new OutOfMemoryException();
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) { GlobalFree(memory); throw new OutOfMemoryException(); }
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            return memory;
        }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr memory);
    }
}
