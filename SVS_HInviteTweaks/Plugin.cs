#nullable enable
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SVS_HInviteTweaks
{
    [BepInProcess("SamabakeScramble")]
    [BepInPlugin(GUID, PLUGIN_NAME, VERSION)]
    public class Plugin : BasePlugin
    {
        public const string PLUGIN_NAME = "SVS_HInviteTweaks";
        public const string COPYRIGHT = "Copyright © TerminalHash 2026";
        public const string COMPANY = "SVS_HInviteTweaks";

        public const string GUID = "th.SVS.HInviteTweaks";
        public const string VERSION = "1.0.0";

        internal static new ManualLogSource Log = null!;

        // logging
        public static ConfigEntry<bool> VerboseLog = null!;
        public static ConfigEntry<bool> AggressiveVerboseLog = null!;
        // behaviour
        public static ConfigEntry<int> SelectionChance = null!;
        public static ConfigEntry<TargetingType> TargetingTypeVar = null!;
        public static ConfigEntry<bool> IgnoreTargetBusyState = null!;
        public static ConfigEntry<bool> OrientationCheckBypass = null!;
        public static ConfigEntry<int> OrientationRandomChance = null!;

        public override void Load()
        {
            Log = base.Log;

            VerboseLog = Config.Bind("Logging", "Debug log", false,
                "Toggle additional lines for debug purpose.");

            AggressiveVerboseLog = Config.Bind("Logging", "Aggressive debug logging", false, new ConfigDescription("More debug lines, developer-only", null, new ConfigurationManagerAttributes { IsAdvanced = true}));

            SelectionChance = Config.Bind("Behavior", "NPC home H-invite chance", 25,
                new ConfigDescription(
                    "Chance (0-100%) to force action when it is eligible " +
                    "for the current direction. On success the returned command is replaced " +
                    "with 96; on failure the vanilla result is kept. 0 = vanilla, " +
                    "100 = always force.",
                    new AcceptableValueRange<int>(0, 100)));
            
            TargetingTypeVar = Config.Bind("Behavior", "Character Targeting Type", TargetingType.Fixed,
                new ConfigDescription("Vanilla: Check for characters, who on map with action initiator | Fixed: Check all characters", null));

            IgnoreTargetBusyState = Config.Bind("Behavior", "Ignore target busy state", false,
                "When enabled, target picking does not skip characters who are in contact " +
                "(actionMode 2) or marked DontTouch (like, when they work, eating, etc.).");

            OrientationCheckBypass = Config.Bind("Behavior", "Bypass orientation check", false,
                "Enables more chaotic behaviour of that action, allowing to ignore actual orientation of target character");

            OrientationRandomChance = Config.Bind("Behavior", "Leans orientation inviting chance", 50,
                new ConfigDescription(
                    "Chance, what that character with a leans hetero or leans homo orientation accepts " +
                    "the non-preferred sex (50% is default). Set to 0% to disable that feature.",
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
        
        internal static void DebugInfo(string msg)
        {
            try { if (AggressiveVerboseLog.Value) Log.LogInfo(msg); } catch { }
        }

        internal static bool Verbose
        {
            get { try { return VerboseLog != null && VerboseLog.Value; } catch { return false; } }
        }

        internal static bool AggressiveVerbose
        {
            get { try { return AggressiveVerboseLog != null && AggressiveVerboseLog.Value; } catch { return false; } }
        }
        
        internal static bool GetOrientationCheckBypass
        {
            get { try { return OrientationCheckBypass != null && OrientationCheckBypass.Value; } catch { return false; } }
        }

        internal static bool IgnoreTargetBusy
        {
            get { try { return IgnoreTargetBusyState != null && IgnoreTargetBusyState.Value; } catch { return false; } }
        }

        internal static int GetOrientationRandomChance
        {
            get { try { return OrientationRandomChance != null ? OrientationRandomChance.Value : 50; } catch { return 50; } }
        }

        //////////////////////////////
        // utility
        public enum TargetingType
        {
            Vanilla = 0,
            Fixed = 1,
        }

        public static TargetingType GetTargetType()
        {
            return TargetingTypeVar.Value;
        }
        //////////////////////////////
        //////////////////////////////
    }
}
