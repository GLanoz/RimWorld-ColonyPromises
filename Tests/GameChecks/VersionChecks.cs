using System;
using System.Linq;
using ColonyPromises;
using HarmonyLib;
using Verse;

public static class VersionChecks
{
    public static int PatchBindings()
    {
        var game = typeof(Pawn).Assembly.GetName().Version;
        var harmony = typeof(Harmony).Assembly.GetName().Version;
        if (game.Minor != 7) throw new NotSupportedException("Harmony 2 patch installation requires the 1.6 game host. Use Verify-Content for static binding checks.");
        int expected = 3;
        if (harmony.Major != expected) throw new Exception("Unexpected Harmony runtime: " + harmony);
        var instance = new Harmony("colonypromises.compatibility-fixture");
        try
        {
            instance.PatchAll(typeof(PromiseRecord).Assembly);
            typeof(PromiseRecord).Assembly.GetType("ColonyPromises.AdaptivePromisesButtonPatch")
                .GetMethod("Install", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { instance });
            int count = instance.GetPatchedMethods().Count();
            if (count != 20) throw new Exception("Not all production patches were installed: " + count);
            Console.WriteLine("Installed and removed " + count + " production Harmony patches in an isolated host; no game was started.");
            return count;
        }
        finally { instance.UnpatchAll(instance.Id); }
    }
}
