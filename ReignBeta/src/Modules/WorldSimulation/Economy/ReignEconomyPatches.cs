using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    internal static class ReignEconomyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.economy";
        private const int MinimumPatrolSize = 5;
        private static Harmony _harmony;
        private static readonly HashSet<string> PatchedHooks = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> InitializedPartyIds = new HashSet<string>(StringComparer.Ordinal);
        private static string _lastPatchError = string.Empty;

        private sealed class PlayerRecruitmentCommitState
        {
            internal Settlement Settlement;
            internal int PartyCount;
        }

        internal static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                PatchedHooks.Clear();
                _lastPatchError = string.Empty;
                _harmony = new Harmony(HarmonyId);
                Patch(typeof(RecruitmentCampaignBehavior), "ApplyInternal", nameof(AiRecruitmentPrefix), prefix: true);
                Patch(typeof(RecruitmentVM), "OnRecruit", nameof(PlayerRecruitmentSelectionPrefix), prefix: true);
                Patch(typeof(RecruitmentVM), "OnDone", nameof(PlayerRecruitmentCommitPrefix), prefix: true);
                Patch(typeof(RecruitmentVM), "OnDone", nameof(PlayerRecruitmentCommitPostfix), prefix: false);
                Patch(typeof(LordPartyComponent.InitializationArgs), "InitializeLordPartyProperties", nameof(LordPartyInitializedPostfix), prefix: false);
                Patch(typeof(PatrolPartyComponent), nameof(PatrolPartyComponent.CreatePatrolParty), nameof(PatrolCreationPrefix), prefix: true);
                Patch(typeof(PatrolPartyComponent), nameof(PatrolPartyComponent.CreatePatrolParty), nameof(PatrolCreationPostfix), prefix: false);
                Patch(typeof(PatrolPartiesCampaignBehavior), "ReplenishParty", nameof(PatrolReplenishmentPrefix), prefix: true);
                Patch(typeof(PatrolPartiesCampaignBehavior), "RemoveSettlementParties", nameof(PatrolRemovalPrefix), prefix: true);
                ReignLog.Info("Reign economy, manpower, food, and security patches loaded.");
            }
            catch (Exception ex)
            {
                _lastPatchError = ex.ToString();
                ReignLog.Warn("Reign economy patch setup failed: " + ex);
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static void Patch(Type targetType, string targetName, string patchName, bool prefix)
        {
            var target = AccessTools.Method(targetType, targetName);
            if (target == null)
            {
                throw new MissingMethodException(targetType.FullName, targetName);
            }

            HarmonyMethod patch = new HarmonyMethod(typeof(ReignEconomyPatches), patchName);
            if (prefix)
            {
                _harmony.Patch(target, prefix: patch);
            }
            else
            {
                _harmony.Patch(target, postfix: patch);
            }
            PatchedHooks.Add(patchName);
        }

        internal static JObject BuildDiagnostics()
        {
            return new JObject
            {
                ["active"] = _harmony != null,
                ["allHooksHealthy"] = _harmony != null && PatchedHooks.Count == 9,
                ["expectedHookCount"] = 9,
                ["patchedHookCount"] = PatchedHooks.Count,
                ["patchedHooks"] = new JArray(PatchedHooks.OrderBy(x => x)),
                ["lastPatchError"] = _lastPatchError ?? string.Empty
            };
        }

        private static bool AiRecruitmentPrefix(
            Settlement settlement,
            ref int number,
            RecruitmentCampaignBehavior.RecruitingDetail detail)
        {
            if (detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividual
                && detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison)
            {
                return true;
            }

            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            if (behavior == null || settlement == null)
            {
                return true;
            }

            int requested = number;
            number = Math.Min(number, behavior.GetRecruitCapacity(settlement));
            behavior.RecordRecruitmentLimit(requested, number);
            return number > 0;
        }

        private static bool PlayerRecruitmentSelectionPrefix(RecruitmentVM __instance)
        {
            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            Settlement settlement = Settlement.CurrentSettlement;
            if (behavior == null || settlement == null)
            {
                return true;
            }

            int alreadySelected = __instance.TroopsInCart?.Count ?? 0;
            if (alreadySelected < behavior.GetRecruitCapacity(settlement))
            {
                return true;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                "The local villages cannot provide more recruits without exhausting their recovered hearth reserve."));
            return false;
        }

        private static void PlayerRecruitmentCommitPrefix(out PlayerRecruitmentCommitState __state)
        {
            __state = new PlayerRecruitmentCommitState
            {
                Settlement = Settlement.CurrentSettlement,
                PartyCount = MobileParty.MainParty?.MemberRoster.TotalManCount ?? 0
            };
        }

        private static void PlayerRecruitmentCommitPostfix(PlayerRecruitmentCommitState __state)
        {
            if (__state?.Settlement == null || MobileParty.MainParty == null)
            {
                return;
            }

            int recruited = Math.Max(0, MobileParty.MainParty.MemberRoster.TotalManCount - __state.PartyCount);
            if (recruited > 0)
            {
                ReignEconomyCampaignBehavior.Instance?.ConsumeRecruitHearth(__state.Settlement, recruited);
            }
        }

        private static void LordPartyInitializedPostfix(MobileParty mobileParty, Hero owner)
        {
            if (mobileParty == null
                || owner?.Clan == null
                || owner.Clan == Clan.PlayerClan
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || !TaleWorlds.CampaignSystem.Campaign.Current.GameStarted)
            {
                return;
            }

            string partyId = mobileParty.StringId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(partyId)
                && !InitializedPartyIds.Add(partyId))
            {
                ReignEconomyCampaignBehavior.Instance
                    ?.RecordDuplicateRetinueInitialization();
                ReignLog.Warn("Skipped duplicate household-retinue initialization for "
                    + partyId + " (" + owner.StringId + ").");
                return;
            }

            TroopRoster roster = mobileParty.MemberRoster;
            for (int index = roster.Count - 1; index >= 0; index--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(index);
                if (!element.Character.IsHero)
                {
                    roster.AddToCounts(element.Character, -element.Number, false, -element.WoundedNumber, -element.Xp);
                }
            }

            PartyTemplateObject template = owner.Clan.IsRebelClan
                ? owner.Clan.Culture.RebelsPartyTemplate
                : owner.Clan.DefaultPartyTemplate;
            List<(CharacterObject Troop, float Weight)> candidates = new List<(CharacterObject, float)>();
            if (template != null)
            {
                foreach (PartyTemplateStack stack in template.Stacks)
                {
                    if (stack.Character != null && !stack.Character.IsHero && stack.Character.Tier <= 3)
                    {
                        candidates.Add((stack.Character, Math.Max(1f, (stack.MinValue + stack.MaxValue) * 0.5f)));
                    }
                }
            }

            if (candidates.Count == 0 && owner.Clan.BasicTroop != null)
            {
                candidates.Add((owner.Clan.BasicTroop, 1f));
            }

            int retinueSize = MBRandom.RandomInt(10, 16);
            for (int i = 0; i < retinueSize && candidates.Count > 0; i++)
            {
                mobileParty.AddElementToMemberRoster(ChooseWeightedTroop(candidates), 1);
            }

            ReignEconomyCampaignBehavior.Instance?.RecordRetinue(retinueSize);
            ReignLog.Info("Created " + retinueSize + "-troop household retinue for " + owner.StringId + ".");
        }

        private static bool PatrolCreationPrefix(Settlement homeSettlement)
        {
            return homeSettlement?.Town?.GarrisonParty != null
                && homeSettlement.Town.GarrisonParty.MemberRoster.TotalHealthyCount >= MinimumPatrolSize;
        }

        private static void PatrolCreationPostfix(ref MobileParty __result)
        {
            if (__result?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return;
            }

            int targetSize = __result.MemberRoster.TotalRegulars;
            __result.MemberRoster.Clear();
            int transferred = TransferHealthyGarrisonTroops(
                __result.PatrolPartyComponent.HomeSettlement,
                __result,
                targetSize);
            if (transferred < MinimumPatrolSize)
            {
                ReignLog.Warn("Patrol creation produced fewer than " + MinimumPatrolSize + " transferred garrison troops.");
            }
        }

        private static bool PatrolReplenishmentPrefix(MobileParty party)
        {
            if (party?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return true;
            }

            Settlement home = party.PatrolPartyComponent.HomeSettlement;
            PartyTemplateObject template = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementPatrolModel
                .GetPartyTemplateForPatrolParty(home, party.PatrolPartyComponent.IsNaval);
            int targetSize = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel
                .FindAppropriateInitialRosterForMobileParty(party, template)
                .TotalManCount;

            ReturnPatrolTroopsToGarrison(party, home);
            TransferHealthyGarrisonTroops(home, party, targetSize);
            party.PatrolPartyComponent.SortRoster();
            return false;
        }

        private static void PatrolRemovalPrefix(Settlement settlement)
        {
            MobileParty patrol = settlement?.PatrolParty?.MobileParty;
            if (patrol != null && patrol.MapEvent == null)
            {
                ReturnPatrolTroopsToGarrison(patrol, settlement);
            }
        }

        private static int TransferHealthyGarrisonTroops(Settlement home, MobileParty patrol, int targetSize)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (garrison == null || patrol == null || targetSize <= 0)
            {
                return 0;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            int remaining = targetSize;
            List<TroopRosterElement> candidates = garrison.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero && element.Number > element.WoundedNumber)
                .OrderBy(element => element.Character.Tier)
                .ToList();
            foreach (TroopRosterElement element in candidates)
            {
                int healthy = element.Number - element.WoundedNumber;
                int transfer = Math.Min(healthy, remaining);
                if (transfer <= 0)
                {
                    continue;
                }

                garrison.MemberRoster.AddToCounts(element.Character, -transfer);
                patrol.MemberRoster.AddToCounts(element.Character, transfer);
                remaining -= transfer;
                if (remaining == 0)
                {
                    break;
                }
            }

            int transferred = targetSize - remaining;
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignEconomyCampaignBehavior.Instance?.RecordPatrolTransfer(
                transferred, totalBefore != totalAfter);
            return transferred;
        }

        private static void ReturnPatrolTroopsToGarrison(MobileParty patrol, Settlement home)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (patrol == null || garrison == null)
            {
                return;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            List<TroopRosterElement> returning = patrol.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero)
                .ToList();
            int returned = 0;
            foreach (TroopRosterElement element in returning)
            {
                returned += element.Number;
                garrison.MemberRoster.AddToCounts(
                    element.Character,
                    element.Number,
                    false,
                    element.WoundedNumber,
                    element.Xp);
                patrol.MemberRoster.AddToCounts(
                    element.Character,
                    -element.Number,
                    false,
                    -element.WoundedNumber,
                    -element.Xp);
            }
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignEconomyCampaignBehavior.Instance?.RecordPatrolReturn(
                returned, totalBefore != totalAfter);
        }

        private static CharacterObject ChooseWeightedTroop(List<(CharacterObject Troop, float Weight)> candidates)
        {
            float total = candidates.Sum(candidate => candidate.Weight);
            float roll = MBRandom.RandomFloat * total;
            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll <= 0f)
                {
                    return candidate.Troop;
                }
            }

            return candidates[candidates.Count - 1].Troop;
        }
    }
}
