using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Runtime
{
    /// <summary>
    /// Gives NPC-owned castle and town dungeons the same native hold-rate bonus
    /// that Bannerlord normally reserves for player-owned settlements.
    /// </summary>
    internal static class ReignDungeonEscapePatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.dungeon_escape";
        private static Harmony _harmony;

        internal static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(PrisonerReleaseCampaignBehavior),
                    "DailyHeroTick",
                    new[] { typeof(Hero) });
                if (target == null)
                {
                    throw new MissingMethodException(
                        typeof(PrisonerReleaseCampaignBehavior).FullName,
                        "DailyHeroTick");
                }

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(
                        typeof(ReignDungeonEscapePatches),
                        nameof(EqualizeDungeonHoldRateTranspiler)));
                ReignLog.Info(
                    "NPC castle and town dungeons now use the native player-owned prisoner hold rate.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                ReignLog.Warn("Dungeon escape-rate patch failed; native behavior remains active: " + ex);
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static IEnumerable<CodeInstruction> EqualizeDungeonHoldRateTranspiler(
            IEnumerable<CodeInstruction> source)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(source);
            MethodInfo partySettlementGetter = AccessTools.PropertyGetter(
                typeof(PartyBase),
                nameof(PartyBase.Settlement));
            MethodInfo ownerClanGetter = AccessTools.PropertyGetter(
                typeof(Settlement),
                nameof(Settlement.OwnerClan));
            MethodInfo playerClanGetter = AccessTools.PropertyGetter(
                typeof(Clan),
                nameof(Clan.PlayerClan));
            MethodInfo replacement = AccessTools.Method(
                typeof(ReignDungeonEscapePatches),
                nameof(GetDungeonOwnerForHoldRate));

            if (partySettlementGetter == null
                || ownerClanGetter == null
                || playerClanGetter == null
                || replacement == null)
            {
                throw new MissingMemberException(
                    "Bannerlord dungeon escape-rate members could not be resolved.");
            }

            int replacementIndex = -1;
            int matchingSequences = 0;
            for (int index = 1; index < instructions.Count - 1; index++)
            {
                if (instructions[index - 1].Calls(partySettlementGetter)
                    && instructions[index].Calls(ownerClanGetter)
                    && instructions[index + 1].Calls(playerClanGetter))
                {
                    replacementIndex = index;
                    matchingSequences++;
                }
            }

            if (matchingSequences != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one direct settlement-owner hold-rate check, found "
                    + matchingSequences + ".");
            }

            instructions[replacementIndex].opcode = OpCodes.Call;
            instructions[replacementIndex].operand = replacement;
            return instructions;
        }

        private static Clan GetDungeonOwnerForHoldRate(Settlement settlement)
        {
            if (settlement != null
                && (settlement.IsTown || settlement.IsCastle)
                && Clan.PlayerClan != null)
            {
                return Clan.PlayerClan;
            }

            return settlement?.OwnerClan;
        }
    }
}
