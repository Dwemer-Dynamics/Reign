using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ReignBeta.World
{
    internal static class ReignRulerNativePatches
    {
        private const string Id = "com.bannerlordreign.reignbeta.rulers";
        private static Harmony _harmony;
        private static MapEvent _battle;
        private static bool _siegeResidents;
        private static readonly Dictionary<Hero, bool> Rolls = new Dictionary<Hero, bool>();
        internal static string Error { get; private set; } = "";
        internal static int HookCount { get; private set; }
        internal static void Apply()
        {
            if (_harmony != null) return;
            try {
                _harmony = new Harmony(Id); HookCount = 0;
                Patch(typeof(MapEvent), "CaptureDefeatedPartyMembers", nameof(BattlePrefix), nameof(BattlePostfix), nameof(CaptureTranspiler), nameof(CaptureFinalizer));
                Patch(typeof(TakePrisonerAction), "ApplyInternal", nameof(TakePrefix));
                Patch(typeof(PrisonerCaptureCampaignBehavior), "OnSettlementOwnerChanged", nameof(SettlementPrefix), nameof(SettlementPostfix), null, nameof(CaptureFinalizer));
                Patch(typeof(EndCaptivityAction), "ApplyInternal", nameof(ReleasePrefix));
                Patch(typeof(SellPrisonersAction), "ApplyInternal", nameof(SellPrefix));
                Patch(typeof(RansomOfferCampaignBehavior), "AcceptRansomOffer", nameof(AcceptRansomPrefix));
                Patch(typeof(RansomOfferCampaignBehavior), "ConsiderRansomPrisoner", nameof(RansomPrefix));
                Patch(typeof(MakePeaceAction), "Apply", nameof(PeacePrefix));
                Patch(typeof(KillCharacterAction), "ApplyByExecution", nameof(ExecutionPrefix), nameof(ExecutionPostfix), null, nameof(ExecutionFinalizer));
                Patch(typeof(KillCharacterAction), "ApplyByExecutionAfterMapEvent", nameof(ExecutionPrefix), nameof(ExecutionPostfix), null, nameof(ExecutionFinalizer));
                Patch(typeof(KillCharacterAction), "ApplyByDeathMark", nameof(DeferredExecutionPrefix), nameof(DeferredExecutionPostfix), null, nameof(DeferredExecutionFinalizer));
                Patch(typeof(KillCharacterAction), "ApplyByDeathMarkForced", nameof(DeferredExecutionPrefix), nameof(DeferredExecutionPostfix), null, nameof(DeferredExecutionFinalizer));
                Patch(typeof(Army), "FinishArmyObjective", null, nameof(ObjectivePostfix));
                Patch(typeof(LeaveSettlementAction), "ApplyForParty", nameof(LeavePrefix));
                foreach (var method in typeof(MobileParty).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("SetMove", StringComparison.Ordinal) && m.Name != "SetMoveModeHold"))
                { _harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReignRulerNativePatches), nameof(MovePrefix))); HookCount++; }
                foreach (string method in new[] { "ApplyByJoinToKingdom", "ApplyByJoinToKingdomByDefection", "ApplyByJoinFactionAsMercenary", "ApplyByCreateKingdom" })
                    Patch(typeof(ChangeKingdomAction), method, nameof(JoinPrefix));
                Patch(typeof(DefaultKingdomCreationModel), "IsPlayerKingdomCreationPossible", null, nameof(CreationPostfix));
                var mortality = AccessTools.PropertySetter(typeof(CampaignOptions), nameof(CampaignOptions.IsLifeDeathCycleDisabled));
                if (mortality == null) throw new MissingMethodException("Life and death option setter unavailable.");
                _harmony.Patch(mortality, prefix: new HarmonyMethod(typeof(ReignRulerNativePatches), nameof(MortalityPrefix))); HookCount++;
                var barter = AccessTools.TypeByName("TaleWorlds.CampaignSystem.BarterSystem.BarterManager");
                Patch(barter, "ApplyBarterOffer", nameof(BarterPrefix));
                Error = "";
            }
            catch (Exception ex) { Error = ex.ToString(); ReignLog.Warn("Ruler native hooks unavailable: " + ex); _harmony?.UnpatchAll(Id); _harmony = null; HookCount = 0; }
        }
        internal static void Unapply() { _harmony?.UnpatchAll(Id); _harmony = null; HookCount = 0; _battle = null; _siegeResidents = false; Rolls.Clear(); }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
        {
            var method = type == null ? null : AccessTools.Method(type, name); if (method == null) throw new MissingMethodException(type?.FullName, name);
            _harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(ReignRulerNativePatches), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(ReignRulerNativePatches), postfix),
                transpiler == null ? null : new HarmonyMethod(typeof(ReignRulerNativePatches), transpiler),
                finalizer == null ? null : new HarmonyMethod(typeof(ReignRulerNativePatches), finalizer)); HookCount++;
        }
        private static void BattlePrefix(MapEvent __instance) { _battle = __instance; Rolls.Clear(); }
        private static Exception CaptureFinalizer(Exception __exception) { _battle = null; _siegeResidents = false; Rolls.Clear(); return __exception; }
        private static Exception ExecutionFinalizer(Exception __exception) { ReignRulerCampaignBehavior.Instance?.EndExecutionRelations(); return __exception; }
        private static void BattlePostfix() { _battle = null; Rolls.Clear(); }
        private static IEnumerable<CodeInstruction> CaptureTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var native = AccessTools.Method(typeof(MapEvent), "FindWinnerPartyToGetCurrentLootObjectBasedOnChances");
            var hero = original.GetMethodBody().LocalVariables.FirstOrDefault(v => v.LocalType == typeof(Hero));
            if (hero == null) throw new InvalidOperationException("Native capture hero local was not found.");
            int changed = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native) && changed == 0)
                {
                    var load = CodeInstruction.LoadLocal(hero.LocalIndex); load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear(); yield return load;
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ReignRulerNativePatches), nameof(SelectCaptureWinner)); changed++;
                }
                yield return instruction;
            }
            if (changed != 1) throw new InvalidOperationException("Native per-hero capture selection could not be patched exactly once.");
        }
        private static MapEventParty SelectCaptureWinner(MBReadOnlyList<KeyValuePair<MapEventParty, float>> chances, Hero hero)
        {
            if (!Protected(hero)) return (MapEventParty)AccessTools.Method(typeof(MapEvent), "FindWinnerPartyToGetCurrentLootObjectBasedOnChances").Invoke(null, new object[] { chances });
            if (!Roll(hero)) return null;
            float total = chances.Sum(c => Math.Max(0, c.Value)); if (total <= 0) return null;
            double target = MBRandom.RandomFloat * total;
            foreach (var c in chances) { target -= Math.Max(0, c.Value); if (target < 0) return c.Key; }
            return chances.Last().Key;
        }
        private static bool Surrendered(MapEvent battle) => (bool)(AccessTools.Property(typeof(MapEventSide), "IsSurrendered")?.GetValue(battle.GetMapEventSide(battle.DefeatedSide), null) ?? AccessTools.Field(typeof(MapEventSide), "IsSurrendered")?.GetValue(battle.GetMapEventSide(battle.DefeatedSide)) ?? false);
        private static bool Protected(Hero hero) => (_battle == null || !Surrendered(_battle))
            && ReignRulerCampaignBehavior.Enabled(RulerOptions.Capture) && ReignRulerCampaignBehavior.Sovereign(hero)
            && ReignRulerCampaignBehavior.Instance?.ApplyingNative != true;
        private static bool Roll(Hero hero)
        {
            if (Rolls.TryGetValue(hero, out bool prior)) return prior;
            double roll = 0;
            if (ReignRulerCampaignBehavior.Instance == null || !ReignRulerCampaignBehavior.Instance.TryConsumeCaptureRoll(hero, out roll)) roll = MBRandom.RandomFloat;
            bool capture = ReignRulerPolicy.Capture(hero.IsWounded, roll); Rolls[hero] = capture; return capture;
        }
        private static bool TakePrefix(PartyBase capturerParty, Hero prisonerCharacter)
        {
            if (!Protected(prisonerCharacter)) return true;
            if (_battle == null && !_siegeResidents) return true;
            if (_battle != null && (Surrendered(_battle) || _battle.RetreatingSide != TaleWorlds.Core.BattleSideEnum.None)) return true;
            if (Roll(prisonerCharacter)) return true;
            if (prisonerCharacter != Hero.MainHero) MakeHeroFugitiveAction.Apply(prisonerCharacter);
            return false;
        }
        private static void SettlementPrefix(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        { _siegeResidents = detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege; if (_siegeResidents) Rolls.Clear(); }
        private static void SettlementPostfix() { _siegeResidents = false; Rolls.Clear(); }
        private static bool ReleasePrefix(Hero prisoner, EndCaptivityDetail detail)
        {
            var behavior = ReignRulerCampaignBehavior.Instance;
            // Civil-war settlement makes peace and restores clans. Native peace
            // release must not undo the imprisonment being applied to this leader.
            if (behavior?.ApplyingNative == true && detail == EndCaptivityDetail.ReleasedAfterPeace
                && prisoner != null && behavior.State.Judgments.Any(j => j.PrisonerId == prisoner.StringId
                    && j.Stage == RulerJudgmentStage.Applying && j.Verdict == RulerVerdict.Imprisonment
                    && j.CivilWar && !j.RebelVictory && !j.DefeatedLord)) return false;
            return behavior?.ApplyingNative == true || !ReignRulerCampaignBehavior.BlocksRelease(prisoner)
                || detail == EndCaptivityDetail.Death || detail == EndCaptivityDetail.ReleasedAfterEscape
                || detail == EndCaptivityDetail.ReleasedAfterBattle;
        }
        private static bool RansomPrefix(Hero hero) => !ReignRulerCampaignBehavior.BlocksRelease(hero);
        private static bool AcceptRansomPrefix(RansomOfferCampaignBehavior __instance) => !ReignRulerCampaignBehavior.BlocksRelease((Hero)AccessTools.Field(typeof(RansomOfferCampaignBehavior), "_currentRansomHero").GetValue(__instance));
        private static bool SellPrefix(TroopRoster prisoners) => prisoners == null || !prisoners.GetTroopRoster().Any(r => r.Character.IsHero && ReignRulerCampaignBehavior.BlocksRelease(r.Character.HeroObject));
        private static bool BarterPrefix(object[] __args)
        {
            foreach (var list in __args.OfType<IEnumerable>()) foreach (var entry in list)
            {
                if (entry == null) continue;
                var field = AccessTools.Field(entry.GetType(), "_prisonerCharacter");
                if (field?.GetValue(entry) is Hero hero && ReignRulerCampaignBehavior.BlocksRelease(hero)) return false;
            }
            return true;
        }
        private static bool PeacePrefix(object[] __args) => ReignRulerCampaignBehavior.Instance?.ApplyingNative == true
            || !ReignRulerCampaignBehavior.BlocksPeace(__args.OfType<IFaction>().FirstOrDefault(), __args.OfType<IFaction>().Skip(1).FirstOrDefault());
        private static bool ExecutionPrefix(Hero __0, Hero __1)
        {
            Hero victim = __0, executor = __1;
            var behavior = ReignRulerCampaignBehavior.Instance; if (behavior == null) return true;
            if (behavior.ApplyingNative) { behavior.BeginExecutionRelations(victim, executor); return true; }
            if (ReignRulerCampaignBehavior.BlocksRelease(victim)) return false;
            if (behavior.AwaitsDecision(victim))
            {
                if (executor == Hero.MainHero && ReignRulerCampaignBehavior.Sovereign(executor)) behavior.RequestPlayerSentence(victim);
                return false;
            }
            if (executor == Hero.MainHero && ReignRulerCampaignBehavior.Sovereign(executor) && ReignRulerCampaignBehavior.Sovereign(victim) && ReignRulerCampaignBehavior.Enabled(RulerOptions.Judgments))
            { behavior.RequestPlayerSentence(victim); return false; }
            behavior.BeginExecutionRelations(victim, executor); return true;
        }
        private static void ExecutionPostfix() => ReignRulerCampaignBehavior.Instance?.EndExecutionRelations();
        private sealed class DeferredExecutionScope
        {
            internal ReignRulerCampaignBehavior Behavior;
            internal bool PriorApplying;
        }
        private static void DeferredExecutionPrefix(Hero __0, out DeferredExecutionScope __state)
        {
            __state = null;
            var behavior = ReignRulerCampaignBehavior.Instance;
            if (behavior?.CompletesDeferredExecution(__0) != true) return;
            __state = new DeferredExecutionScope { Behavior = behavior, PriorApplying = behavior.ApplyingNative };
            behavior.ApplyingNative = true;
            behavior.BeginExecutionRelations(__0, __0.DeathMarkKillerHero);
        }
        private static void DeferredExecutionPostfix(DeferredExecutionScope __state)
        {
            if (__state == null) return;
            try { __state.Behavior.EndExecutionRelations(); }
            finally { __state.Behavior.ApplyingNative = __state.PriorApplying; }
        }
        private static Exception DeferredExecutionFinalizer(Exception __exception, DeferredExecutionScope __state)
        { DeferredExecutionPostfix(__state); return __exception; }
        private static bool MovePrefix(MobileParty __instance, object[] __args) => ReignRulerCampaignBehavior.Instance?.MayMove(__instance, __args.OfType<Settlement>().FirstOrDefault()) != false;
        private static bool LeavePrefix(MobileParty __0) => ReignRulerCampaignBehavior.Instance?.MayLeave(__0) != false;
        private static void ObjectivePostfix(Army __instance) => ReignRulerCampaignBehavior.Instance?.ObjectiveCompleted(__instance);
        private static bool JoinPrefix(Clan clan, object[] __args) => ReignRulerCampaignBehavior.Instance?.ApplyingNative == true || !ReignRulerCampaignBehavior.Restricted(clan, __args.OfType<Kingdom>().LastOrDefault());
        private static void CreationPostfix(ref bool __result, ref List<TextObject> explanations)
        {
            if (!ReignRulerCampaignBehavior.Restricted(Clan.PlayerClan)) return;
            __result = false;
            if (explanations == null) explanations = new List<TextObject>();
            explanations.Add(new TextObject("{=!}Your exile prevents founding a kingdom until its term ends."));
        }
        private static bool MortalityPrefix(bool value) => !value || !ReignRulerCampaignBehavior.Enabled(RulerOptions.Mortality);
    }
}
