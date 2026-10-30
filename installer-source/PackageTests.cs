using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
class PackageTests
{
    static int checks;
    static int Main()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "work", "Friend standalone test " + Guid.NewGuid().ToString("N"));
        string solo = Path.Combine(root, "YouTubeLinkCleaner-Setup.exe"), damaged = Path.Combine(root, "Damaged-Setup.exe");
        Directory.CreateDirectory(root);
        try
        {
            File.Copy("work/YouTubeLinkCleaner-Setup.exe", solo);
            Assert(Directory.GetFiles(root).Length == 1, "only one file required");
            Assert(Run(solo) == 0, "relocated standalone resource verification");
            Assembly assembly = Assembly.LoadFile(solo);
            Assert(assembly.GetManifestResourceNames().Length == 4, "four self-contained resources");
            byte[] app = Read(assembly, "payload.app");
            Assert(Same(app, File.ReadAllBytes("outputs/YouTubeLinkCleaner/YouTubeLinkCleaner.exe")), "exact previously verified clipboard app embedded");
            Assert(Same(Read(assembly, "payload.uninstaller"), File.ReadAllBytes("work/setup/Uninstall.exe")), "uninstaller embedded exactly");
            Assert(Same(Read(assembly, "payload.readme"), File.ReadAllBytes("work/setup/QuickGuide.md")), "guide embedded exactly");
            Assert(Read(assembly, "payload.icon").Length > 100, "icon embedded");
            byte[] all = File.ReadAllBytes(solo);
            int match = -1;
            for (int index = 0; index <= all.Length - app.Length; index++)
            {
                if (all[index] != app[0] || all[index + 1] != app[1]) continue;
                int offset = 0;
                while (offset < app.Length && all[index + offset] == app[offset]) offset++;
                if (offset == app.Length) { match = index; break; }
            }
            Assert(match >= 0, "embedded binary located for corruption check");
            all[match + 1024] ^= 1;
            File.WriteAllBytes(damaged, all);
            Assert(Run(damaged) == 1, "corrupted payload rejected without installing");
            Console.WriteLine("PASS: " + checks + " standalone packaging/integrity checks. No installation or clipboard changes.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
        // Assembly.LoadFile keeps the inspected EXE mapped until this process exits.
    }
    static int Run(string path)
    {
        using (Process process = Process.Start(new ProcessStartInfo(path, "--verify") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        { if (!process.WaitForExit(10000)) throw new Exception("Verification timeout"); return process.ExitCode; }
    }
    static byte[] Read(Assembly assembly, string name)
    {
        using (Stream input = assembly.GetManifestResourceStream(name))
        using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
    }
    static bool Same(byte[] a, byte[] b)
    { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(a)) == Convert.ToBase64String(hash.ComputeHash(b)); }
    static void Assert(bool result, string label) { checks++; if (!result) throw new Exception(label); }
}
