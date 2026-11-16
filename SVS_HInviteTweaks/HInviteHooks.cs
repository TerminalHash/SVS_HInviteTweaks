#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using SV;

namespace SVS_HInviteTweaks
{
    internal static class HInviteHooks
    {
        // SV.SimulationDefine.CommandNo.ComeToMyRoomWithYouHVer = 96
        private const int CommandID = 96;

        private static Harmony? _harmony;

        // Actors whose command-96 selection was forced and whose target call is
        // still pending. Keyed by actor so an unrelated target call cannot
        // consume the boost.
        private static readonly HashSet<int> BoostedActors = new();

        public static void PatchAll()
        {
            try { SelectionDetour.Install(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[HInvite] Selection detour setup failed: {ex.Message}"); }

            _harmony = new Harmony(Plugin.GUID + ".hinvite");
            _harmony.PatchAll(typeof(HInviteHooks));
            Plugin.Log.LogInfo("[HInvite] Hooks applied.");
        }

        public static void Unpatch()
        {
            SelectionDetour.Uninstall();
            ClearBoost();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static void MarkBoosted(int actorIndex)
        {
            if (actorIndex >= 0) BoostedActors.Add(actorIndex);
        }

        internal static void ClearBoost() => BoostedActors.Clear();

        internal static void ClearBoost(int actorIndex)
        {
            if (actorIndex >= 0) BoostedActors.Remove(actorIndex);
        }

        internal static bool IsBoostPending(int actorIndex)
            => actorIndex >= 0 && BoostedActors.Contains(actorIndex);

        internal static void ConsumeBoost(int actorIndex)
        {
            if (actorIndex >= 0) BoostedActors.Remove(actorIndex);
        }

        internal static string ActorLabel(SaveData.Actor? actor)
        {
            if (actor == null) return "null";
            int index = -1;
            try { index = actor.charasGameParam?.Index ?? -1; } catch { }
            string name = "";
            try { name = actor.Name ?? ""; } catch { }
            string role = "";
            try { role = actor.charasGameParam?.isPC == true ? "(PC)" : "(NPC)"; } catch { }
            return string.IsNullOrEmpty(name) ? $"#{index}{role}" : $"{name}#{index}{role}";
        }

        internal static string TableText(Il2CppSystem.Collections.Generic.Dictionary<int, float> table)
        {
            var sb = new StringBuilder();
            foreach (var kv in table)
            {
                if (sb.Length != 0) sb.Append(", ");
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##"));
            }
            return sb.ToString();
        }

        internal static int CurrentDirection()
        {
            try
            {
                return ThinkingManager.Instance?.interpersonalActionInfo?.actor
                    ?.charasGameParam?.thinkingPropertyTemp?.directionInterpersonalBehavior ?? -1;
            }
            catch { return -1; }
        }

        // target picking
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ThinkingManager), nameof(ThinkingManager.InterpersonalCommandSelectionTarget))]
        private static void TargetPostfix(ThinkingManager __instance, SaveData.Actor _actor, int _commandID, ref int __result)
        {
            try
            {
                if (_commandID != CommandID) return;

                int actorIndex = _actor?.charasGameParam?.Index ?? -1;
                if (!IsBoostPending(actorIndex)) return;
                ConsumeBoost(actorIndex);

                string who = ActorLabel(_actor);
                int rolled = __result;

                var eligible = new List<(SaveData.Actor Actor, string Reason)>();
                var seen = new HashSet<int>();

                void Consider(SaveData.Actor? cand)
                {
                    if (cand == null) return;
                    int ci = cand.charasGameParam?.Index ?? -1;
                    if (ci < 0 || ci == actorIndex) return;
                    if (!seen.Add(ci)) return;
                    if (TargetAllowed(_actor!, cand, out string candReason))
                        eligible.Add((cand, candReason));
                }

                // Same candidate set as the vanilla reaction target picker
                var sim = Manager.SimulationManager.Instance;
                if (sim != null && _actor?.charaBase != null)
                {
                    var candidates = sim.GetWithMapCharactor(_actor.charaBase);
                    if (candidates != null)
                    {
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            var ai = candidates[i];
                            if (ai == null) continue;
                            var aiCtrl = ai.BehaviourCtrl;
                            if (aiCtrl == null) continue;
                            var tree = aiCtrl.behaviorTreeCtrl;
                            if (tree == null) continue;
                            if ((int)tree.actionMode == 2) continue;
                            if (SV.GameChara.IsDontTouch(ai.charaData)) continue;
                            Consider(ai.charaData);
                        }
                    }
                }

                SaveData.Actor? chosen = null;
                string reason;
                if (eligible.Count != 0)
                {
                    int pick = Random.Shared.Next(eligible.Count);
                    chosen = eligible[pick].Actor;
                    reason = $"{ActorLabel(chosen)}: {eligible[pick].Reason}; pool={eligible.Count}";
                }
                else
                {
                    reason = "no eligible target";
                }

                int chosenIndex = chosen?.charasGameParam?.Index ?? -1;
                bool allowed = chosen != null && chosenIndex >= 0;

                if (allowed)
                {
                    // Rebuild the target-side context for the chosen target so
                    // later stages see the same target the judgement used.
                    try
                    {
                        __instance.InitInterpersonalCommandSelectionTargetConditions(_actor);
                        __instance.InterpersonalCommandSelectionTargetConditions(chosen, CommandID, -1);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[HInvite] Target context failed: {ex.Message}");
                    }
                }

                __result = allowed ? chosenIndex : -1;

                string table = Plugin.Verbose && __instance.calcTable != null
                    && __instance.calcTable.Count != 0
                    ? $"; targetTable={TableText(__instance.calcTable)}"
                    : "";
                Plugin.Info($"[HInvite] Target {who}: rolled={rolled} -> {__result} " +
                            $"[{reason}]{table}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HInvite] Target hook failed: {ex.Message}");
            }
        }

        // target judgement
        private static bool TargetAllowed(SaveData.Actor actor, SaveData.Actor target, out string reason)
        {
            reason = "";
            try
            {
                int actorIndex = actor.charasGameParam.Index;
                int targetIndex = target.charasGameParam.Index;

                bool lover = actor.charasGameParam.memory.IsLoversContainsID(targetIndex)
                          || target.charasGameParam.memory.IsLoversContainsID(actorIndex);

                var fav = actor.charasGameParam.sensitivity.GetInfoFromID(targetIndex);
                var counts = fav?.longSensitivityCounts;
                int love = counts != null && counts.Length > 0 ? counts[0] : -1;
                int friend = counts != null && counts.Length > 1 ? counts[1] : -1;
                int virtue = target.charFile?.GameParameter?.lvChastity ?? -1;

                bool relationshipOk;
                string rule;
                if (virtue >= 2)
                {
                    relationshipOk = lover && love >= 20;
                    rule = "virtue>=2: lover & love>=20";
                }
                else if (virtue == 1)
                {
                    relationshipOk = love >= 15 || friend >= 20;
                    rule = "virtue=1: love>=15 | friend>=20";
                }
                else if (virtue == 0)
                {
                    relationshipOk = love > 5 || friend > 10;
                    rule = "virtue=0: love>5 | friend>10";
                }
                else
                {
                    relationshipOk = false;
                    rule = "virtue unknown";
                }

                bool undressed = target.chaCtrl?.data?.Status?.coordinateType == 2;
                int actorSex = actor.charFile?.Parameter?.sex ?? -1;
                int targetSex = target.charFile?.Parameter?.sex ?? -1;
                bool sexOk = actorSex != 0 || targetSex != 0;

                bool allowed = relationshipOk && !undressed && sexOk;
                reason = $"virtue={virtue} love={love} friend={friend} lover={lover} [{rule}] " +
                         $"undressed={undressed} sex={actorSex}/{targetSex} sexOk={sexOk} " +
                         $"-> {(allowed ? "allowed" : "blocked")}";
                return allowed;
            }
            catch (Exception ex)
            {
                reason = $"check failed: {ex.Message}";
                return false;
            }
        }
    }
}
