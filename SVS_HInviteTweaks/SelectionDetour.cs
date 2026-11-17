#nullable enable
using System;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using SV;

namespace SVS_HInviteTweaks
{
    // Native detour used because InterpersonalCommandSelection returns a
    // ValueTuple<int,float>, which cannot be safely rewritten with a Harmony
    // postfix.
    internal static unsafe class SelectionDetour
    {
        private static INativeDetour? _detour; // keep alive
        private static SelectionDelegate? _orig;

        private static readonly System.Random Rng = new();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate ulong SelectionDelegate(IntPtr self, IntPtr actor);

        public static void Install()
        {
            var method = AccessTools.Method(
                typeof(ThinkingManager),
                nameof(ThinkingManager.InterpersonalCommandSelection),
                new[] { typeof(SaveData.Actor) });
            if (method == null)
                throw new Exception("ThinkingManager.InterpersonalCommandSelection not found");

            var ptrField = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            if (ptrField == null)
                throw new Exception($"Couldn't obtain NativeMethodInfoPtr field for {method}");

            var rawMethodInfoPtr = (IntPtr)(ptrField.GetValue(null) ?? IntPtr.Zero);
            if (rawMethodInfoPtr == IntPtr.Zero)
                throw new Exception($"NativeMethodInfoPtr for {method} is null");

            var nativeMethodInfo = UnityVersionHandler.Wrap((Il2CppMethodInfo*)(void*)rawMethodInfoPtr)
                ?? throw new Exception($"Wrapped method info for {method} is invalid");

            IntPtr methodPtr = nativeMethodInfo.MethodPointer;
            _detour = INativeDetour.CreateAndApply(methodPtr, (SelectionDelegate)SelectionDetourHook, out _orig);
            Plugin.Info($"[HInvite] Selection detour installed at 0x{methodPtr.ToInt64():X}.");
        }

        public static void Uninstall()
        {
            try { _detour?.Dispose(); } catch { }
            _detour = null;
            _orig = null;
        }

        private static ulong SelectionDetourHook(IntPtr self, IntPtr actorPtr)
        {
            int actorIndex = -1;
            try
            {
                if (_orig == null)
                    throw new InvalidOperationException("original detour target is null");

                ulong raw = _orig(self, actorPtr);

                int id = (int)(raw & 0xFFFFFFFF);
                float weight = BitConverter.Int32BitsToSingle((int)(raw >> 32));
                int chance = Plugin.SelectionChance.Value;

                var actor = actorPtr != IntPtr.Zero
                    ? new Il2CppObjectBase(actorPtr).Cast<SaveData.Actor>()
                    : null;
                actorIndex = actor?.charasGameParam?.Index ?? -1;
                string who = HInviteHooks.ActorLabel(actor);
                int dir = HInviteHooks.CurrentDirection();

                if (actor?.charasGameParam?.isPC == true)
                {
                    HInviteHooks.ClearBoost(actorIndex);
                    Plugin.Info($"[HInvite] Selection {who} dir={dir}: actor is PC, not boosted.");
                    return raw;
                }

                var tm = ThinkingManager.Instance;
                if (tm == null || chance <= 0)
                {
                    HInviteHooks.ClearBoost(actorIndex);
                    if (chance <= 0) Plugin.Info("[HInvite] Selection detour: disabled (chance=0%).");
                    return raw;
                }

                if (tm.calcTable == null || !tm.calcTable.TryGetValue(96, out float own) || own <= 0f)
                {
                    HInviteHooks.ClearBoost(actorIndex);
                    Plugin.Info($"[HInvite] Selection {who} dir={dir}: 96 not eligible (not in command table or weight=0).");
                    return raw;
                }

                if (id == 96)
                {
                    // The invite is already being selected; still let the target
                    // hook supply a target, because the vanilla table is empty.
                    HInviteHooks.MarkBoosted(actorIndex);
                    Plugin.Info($"[HInvite] Selection {who} dir={dir}: vanilla already picked 96 " +
                                $"(weight={weight:0.##}); target judgement applies.");
                    return raw;
                }

                int roll = Rng.Next(100);
                if (roll >= chance)
                {
                    HInviteHooks.ClearBoost(actorIndex);
                    Plugin.Info($"[HInvite] Selection {who} dir={dir}: vanilla id={id} weight={weight:0.##}; " +
                                $"96 weight={own:0.##}; roll={roll} chance={chance}% -> no force.");
                    return raw;
                }

                HInviteHooks.MarkBoosted(actorIndex);
                Plugin.Info($"[HInvite] Selection {who} dir={dir}: vanilla id={id} weight={weight:0.##}; " +
                            $"96 weight={own:0.##}; roll={roll} chance={chance}% -> forced 96.");
                return Pack(96, own);
            }
            catch (Exception ex)
            {
                HInviteHooks.ClearBoost(actorIndex);
                Plugin.Log.LogWarning($"[HInvite] Selection detour failed: {ex.Message}");
                return Pack(-1, 0f);
            }
        }

        private static ulong Pack(int id, float weight)
        {
            uint lo = unchecked((uint)id);
            uint hi = unchecked((uint)BitConverter.SingleToInt32Bits(weight));
            return ((ulong)hi << 32) | lo;
        }
    }
}
