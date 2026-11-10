using System;
using System.IO;
using HarmonyLib;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// Lab only (-coop-savedir): keeps a test copy's saves, user_config.ini and crash db out of the
    /// owner's %USERPROFILE%\AppData\LocalLow folder. Every save path goes through
    /// PlatformSettings.SaveDataPath / PersistentDataPath (see docs/NOTES.md).
    /// The getters are tiny auto-properties that Mono may inline, so we set the backing values through the
    /// setters instead: now, and again after PlatformSettings.Initialize() (Main.cs) rewrites them.
    /// </summary>
    internal static class SaveRedirect
    {
        static string sDir;
        static Type sPlatform;

        public static void Apply(string dir, Harmony harmony)
        {
            sDir = Path.GetFullPath(dir);
            Directory.CreateDirectory(sDir);
            sPlatform = AccessTools.TypeByName("BBI.Unity.Game.PlatformSettings")
                        ?? throw new MissingMemberException("PlatformSettings not found");
            var init = AccessTools.Method(sPlatform, "Initialize", Type.EmptyTypes)
                       ?? throw new MissingMethodException("PlatformSettings.Initialize not found");
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(SaveRedirect), nameof(AfterInitialize)));
            SetPaths();
            DisableUnityAnalytics();

            // If the options were already loaded from the real folder, point them at ours too.
            var lynx = AccessTools.TypeByName("BBI.Unity.Game.LynxUserOptions");
            if (lynx != null && (bool)(AccessTools.Property(lynx, "Initialized")?.GetValue(null) ?? false))
            {
                AccessTools.Field(lynx, "sINIWritePath")?.SetValue(null, sDir);
                AccessTools.Field(lynx, "sINIReadPath")?.SetValue(null, Path.Combine(sDir, "user_config.ini"));
                Plugin.Log.LogWarning("LynxUserOptions was already initialized; its paths were re-pointed (it read the owner's ini once).");
            }
        }

        static void AfterInitialize() => SetPaths();

        /// <summary>Unity Analytics writes Unity\...\Analytics under the real persistentDataPath from native
        /// code (seen in the lab, after plugins load). There is no runtime way to move persistentDataPath,
        /// so lab copies switch analytics off before it starts.</summary>
        static void DisableUnityAnalytics()
        {
            try
            {
                var a = AccessTools.TypeByName("UnityEngine.Analytics.Analytics");
                foreach (var name in new[] { "initializeOnStartup", "enabled", "deviceStatsEnabled" })
                    AccessTools.Property(a, name)?.SetValue(null, false);
                AccessTools.Property(a, "limitUserTracking")?.SetValue(null, true);
                var pr = AccessTools.TypeByName("UnityEngine.Analytics.PerformanceReporting");
                AccessTools.Property(pr, "enabled")?.SetValue(null, false);
                Plugin.Log.LogInfo("lab copy: Unity Analytics disabled");
            }
            catch (Exception e) { Plugin.Log.LogWarning("could not disable Unity Analytics: " + e.Message); }
        }

        static void SetPaths()
        {
            AccessTools.Property(sPlatform, "SaveDataPath").SetValue(null, sDir);
            AccessTools.Property(sPlatform, "PersistentDataPath").SetValue(null, sDir);
            Plugin.Log.LogInfo($"save/settings path redirected to {sDir}");
        }
    }
}
