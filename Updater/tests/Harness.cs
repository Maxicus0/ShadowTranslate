using System;
using System.IO;
using XUnity.AutoTranslator.Plugin.Core.Updating;
static class Harness
{
    static int Main(string[] a)
    {
        var fake = Environment.GetEnvironmentVariable("FAKE_GAME_EXE");
        var t0 = DateTime.UtcNow;
        Console.WriteLine("HARNESS start args=[" + string.Join("|", a) + "] justUpdated=" + Environment.GetEnvironmentVariable("SHADOWTRANSLATE_JUST_UPDATED"));
        ShadowUpdateGate.Run(Environment.GetEnvironmentVariable("PLUGIN_VERSION") ?? "0.1.0", Console.WriteLine, fake);
        Console.WriteLine("GAME_CONTINUES after " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
        return 0;
    }
}
