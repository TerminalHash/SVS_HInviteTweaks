#nullable enable
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SVS_HInviteTweaks
{
    // NOTE: no [BepInProcess] on purpose — the game exe name varies by build,
    // and a wrong name would silently prevent loading.
    [BepInPlugin(GUID, PLUGIN_NAME, VERSION)]
    public class Plugin : BasePlugin
    {
        public const string PLUGIN_NAME = "SVS_HInviteTweaks";
        public const string COPYRIGHT = "Copyright © TerminalHash 2026";
        public const string COMPANY = "SVS_HInviteTweaks";

        public const string GUID = "th.SVS.HInviteTweaks";
        public const string VERSION = "1.0.0";

        internal static new ManualLogSource Log = null!;

        public static ConfigEntry<bool> VerboseLog = null!;
        public static ConfigEntry<int> SelectionChance = null!;

        public override void Load()
        {
            Log = base.Log;

            VerboseLog = Config.Bind("Logging", "Debug log", false,
                "Toggle additional lines for debug purpose.");

            SelectionChance = Config.Bind("Behavior", "NPC home H-invite chance", 25,
                new ConfigDescription(
                    "Chance (0-100%) to force action when it is eligible " +
                    "for the current direction. On success the returned command is replaced " +
                    "with 96; on failure the vanilla result is kept. 0 = vanilla, " +
                    "100 = always force.",
                    new AcceptableValueRange<int>(0, 100)));

            try { HInviteHooks.PatchAll(); }
            catch (Exception ex) { Log.LogWarning($"[HInvite] Hook setup failed: {ex}"); }

            Log.LogInfo($"{PLUGIN_NAME} v{VERSION} loaded.");
        }

        public override bool Unload()
        {
            HInviteHooks.Unpatch();
            return true;
        }

        internal static void Info(string msg)
        {
            try { if (VerboseLog.Value) Log.LogInfo(msg); } catch { }
        }

        internal static bool Verbose
        {
            get { try { return VerboseLog != null && VerboseLog.Value; } catch { return false; } }
        }
    }
}
