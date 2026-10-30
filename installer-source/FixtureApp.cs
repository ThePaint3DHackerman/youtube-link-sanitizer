using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
class FixtureApp
{
    static int Main(string[] args)
    {
        string name;
        using (SHA256 hash = SHA256.Create()) name = "Local\\BarnacleSetupFixture." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(AppDomain.CurrentDomain.BaseDirectory))).Replace("-", "");
        if (args.Length != 1) return 2;
        if (args[0] == "--quiet")
        {
            bool created;
            using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, name, out created))
            { if (created) stop.WaitOne(30000); }
            return 0;
        }
        try
        {
            using (EventWaitHandle stop = EventWaitHandle.OpenExisting(name))
            { if (args[0] == "--exit") stop.Set(); return 0; }
        }
        catch (WaitHandleCannotBeOpenedException) { return args[0] == "--exit" ? 0 : 1; }
    }
}
