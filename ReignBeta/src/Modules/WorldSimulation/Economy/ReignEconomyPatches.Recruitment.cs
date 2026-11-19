using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Economy
{
    internal static partial class ReignEconomyPatches
    {
        private static bool AiRecruitmentPrefix(
            MobileParty side1Party,
            Settlement settlement,
            Hero individual,
            CharacterObject troop,
            ref int number,
            int bitCode,
            RecruitmentCampaignBehavior.RecruitingDetail detail,
            out EconomyReceipt __state)
        {
            __state = null;
            _recruitmentDepth++;
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            {
                ReignWarEconomyTransactions.Totals("initial_campaign").Exempt += Math.Max(0, number);
                return true;
            }
            if (detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividual
                && detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison)
            {
                bool known = detail == RecruitmentCampaignBehavior.RecruitingDetail.MercenaryFromTavern
                    || (detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromMap && side1Party.MapFaction?.IsMinorFaction == true);
                if (known) ReignWarEconomyTransactions.Totals("native_" + detail).Exempt += Math.Max(0, number);
                else if (ReignWarEconomyTransactions.State != null)
                { ReignWarEconomyTransactions.State.UnsupportedEvents++; ReignWarEconomyTransactions.State.LastError = "Unsupported recruitment detail " + detail; }
                return known;
            }

            if (individual == null || bitCode < 0 || bitCode >= individual.VolunteerTypes.Length
                || individual.VolunteerTypes[bitCode] != troop)
            { ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Ai).Blocked += Math.Max(0, number); return false; }
            int requested = number;
            bool garrison = detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison;
            Hero payer = garrison ? side1Party.ActualClan?.Leader : side1Party.LeaderHero;
            number = ReignWarEconomyTransactions.Allow(ReignWarEconomyTransactions.Ai, settlement, side1Party, payer, troop,
                Math.Min(1, requested), side1Party != MobileParty.MainParty, garrison);
            if (number > 0)
                __state = ReignWarEconomyTransactions.Begin(ReignWarEconomyTransactions.Ai, settlement, side1Party, payer, troop, number, individual);
            return number > 0;
        }

        private static void AiRecruitmentPostfix(MobileParty side1Party, Settlement settlement, CharacterObject troop,
            RecruitmentCampaignBehavior.RecruitingDetail detail, EconomyReceipt __state)
        {
            bool garrison = detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison;
            ReignWarEconomyTransactions.Complete(__state, settlement, side1Party,
                garrison ? side1Party.ActualClan?.Leader : side1Party.LeaderHero, troop, garrison);
        }

        private static Exception AiRecruitmentFinalizer(Exception __exception, EconomyReceipt __state)
        {
            try
            {
                if (__exception != null) ReignWarEconomyTransactions.Rollback(__state, "native recruitment failed");
            }
            finally { _recruitmentDepth = Math.Max(0, _recruitmentDepth - 1); }
            return __exception;
        }

        private static void AiPrisonerTransferPrefix(MobileParty mobileParty, CharacterObject troop, int num,
            out AiPrisonerTransfer __state)
        {
            __state = new AiPrisonerTransfer {
                Previous = _aiPrisonerTransfer,
                Party = mobileParty, Troop = troop, Requested = num,
                PrisonersBefore = troop == null ? 0 : mobileParty?.PrisonRoster.GetTroopCount(troop) ?? 0,
                MembersBefore = troop == null ? 0 : mobileParty?.MemberRoster.GetTroopCount(troop) ?? 0
            };
            _aiPrisonerTransfer = __state;
        }

        private static void AiPrisonerTransferFinalizer(AiPrisonerTransfer __state)
        {
            if (__state != null && ReferenceEquals(_aiPrisonerTransfer, __state))
                _aiPrisonerTransfer = __state.Previous;
        }

        internal static bool TryObserveAiPrisonerTransfer(Hero recruiter, Settlement locality, Hero source,
            CharacterObject troop, int amount)
        {
            var transfer = _aiPrisonerTransfer;
            if (transfer?.Party == null || troop == null || locality != null || source != null || recruiter != transfer.Party.LeaderHero
                || troop != transfer.Troop || amount != transfer.Requested
                || transfer.PrisonersBefore - transfer.Party.PrisonRoster.GetTroopCount(troop) != amount
                || transfer.Party.MemberRoster.GetTroopCount(troop) - transfer.MembersBefore != amount) return false;
            if (!transfer.Reported)
            {
                transfer.Reported = true;
                ReignWarEconomyTransactions.Totals("native_ai_prisoner_transfer").Exempt += amount;
            }
            return true;
        }

        private static Exception PlayerRecruitmentFinalizer(Exception __exception, PlayerRecruitmentCommitState __state)
        {
            if (__exception != null) ReignWarEconomyTransactions.Rollback(__state?.Receipt, "player recruitment failed");
            return __exception;
        }

        private static bool PlayerRecruitmentSelectionPrefix(RecruitmentVM __instance)
        {
            ReignEconomyCampaignBehavior behavior = ReignServices.Economy;
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

        private static bool PlayerRecruitmentCommitPrefix(RecruitmentVM __instance, out PlayerRecruitmentCommitState __state)
        {
            __state = null;
            int count = __instance.TroopsInCart.Count;
            if (ReignWarEconomyTransactions.State?.Pending.Any(x => x.Party == MobileParty.MainParty.StringId) == true
                || count > (ReignServices.Economy?.GetRecruitCapacity(Settlement.CurrentSettlement) ?? 0)
                || __instance.TroopsInCart.Sum(x => x.Cost) > Hero.MainHero.Gold
                || __instance.TroopsInCart.Any(x => x.Owner.OwnerHero.VolunteerTypes[x.Index] != x.Character))
            {
                ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Player).Blocked += count;
                InformationManager.DisplayMessage(new InformationMessage("Recruitment conditions changed. Review the available volunteers and local hearth reserve."));
                return false;
            }
            __state = new PlayerRecruitmentCommitState
            {
                Settlement = Settlement.CurrentSettlement,
                PartyCount = MobileParty.MainParty?.MemberRoster.TotalManCount ?? 0,
                Receipt = ReignWarEconomyTransactions.Begin(ReignWarEconomyTransactions.Player, Settlement.CurrentSettlement,
                    MobileParty.MainParty, Hero.MainHero, null, count)
            };
            __state.Receipt.QuotedCost = -1;
            __state.Receipt.QuotedTotal = __instance.TroopsInCart.Sum(x => x.Cost);
            return true;
        }

        private static void PlayerRecruitmentCommitPostfix(PlayerRecruitmentCommitState __state)
        {
            if (__state?.Settlement == null || MobileParty.MainParty == null)
            {
                return;
            }

            ReignWarEconomyTransactions.Complete(__state.Receipt, __state.Settlement, MobileParty.MainParty, Hero.MainHero, null);
        }

        private static bool LordPartyInitializedPrefix(MobileParty mobileParty, Hero owner)
        {
            if (mobileParty == null || mobileParty == MobileParty.MainParty || owner?.Clan == null) return true;
            if (mobileParty.IsCurrentlyUsedByAQuest)
            { ReignWarEconomyTransactions.Totals("native_quest_party_initialization").Exempt++; return true; }
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_retinue").Exempt++; return true; }
            var state = ReignWarEconomyTransactions.State;
            if (state == null) return true;
            // CreateParty registers MBGUID after this initializer. Use object-local provisional identity until then.
            string key = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (state.PartyGenerations.ContainsKey(key))
            {
                ReignServices.Economy.RecordDuplicateRetinueInitialization();
                return false;
            }
            state.PartyGenerations[key] = "pending";
            return true;
        }

        private static Exception LordPartyInitializationFinalizer(Exception __exception, MobileParty mobileParty)
        {
            if (__exception == null || mobileParty == null) return __exception;
            var state = ReignWarEconomyTransactions.State;
            string key = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (__exception != null && state != null && state.PartyGenerations.TryGetValue(key, out string status) && status == "pending")
            {
                // Native initialization failed before funded replacement could run. Retain no free template troops.
                foreach (var element in mobileParty.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero).ToList())
                    mobileParty.MemberRoster.AddToCounts(element.Character, -element.Number, false, -element.WoundedNumber, -element.Xp);
                state.PartyGenerations[key] = "failed:0";
                state.LastError = "Household initialization failed for " + key;
            }
            if (__exception != null && mobileParty != null) ReignWarEconomyTransactions.OnPartyCreationFailed(mobileParty);
            return __exception;
        }

        private static void NativeWagePaymentPostfix(MobileParty mobileParty, bool applyWithdrawals, int __result)
        {
            if (mobileParty == null || (!mobileParty.IsLordParty && !mobileParty.IsGarrison)) return;
            string payer = ReignWarEconomyLedger.WagePayer(mobileParty.IsGarrison, mobileParty.ActualClan?.StringId,
                mobileParty.GarrisonPartyComponent?.Settlement?.OwnerClan?.StringId);
            ReignWarEconomyLedger.ObserveWages(ReignWarEconomyTransactions.State, payer,
                CampaignTime.Now.ToDays, mobileParty.TotalWage, __result, applyWithdrawals, mobileParty.IsGarrison);
        }

        private static void TournamentPaymentPrefix(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, out Dictionary<Hero, int> __state)
        {
            __state = participants.Where(x => x.IsHero).Select(x => x.HeroObject)
                .Concat(winner.IsHero ? new[] { winner.HeroObject } : Enumerable.Empty<Hero>())
                .Distinct().ToDictionary(x => x, x => x.Gold);
        }

        private static void TournamentPrizePaymentPrefix(Hero winner, out Dictionary<Hero, int> __state)
        {
            __state = new Dictionary<Hero, int>();
            if (winner?.Clan?.Leader != null) __state[winner.Clan.Leader] = winner.Clan.Leader.Gold;
        }

        private static void TournamentPrizePaymentPostfix(Dictionary<Hero, int> __state) => TournamentPaymentPostfix(__state);

        private static void TournamentPaymentPostfix(Dictionary<Hero, int> __state)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || __state == null) return;
            foreach (var pair in __state.Where(x => x.Key.Clan != null))
            {
                string id = pair.Key.Clan.StringId;
                if (!state.Finance.TryGetValue(id, out var totals)) state.Finance[id] = totals = new EconomyFinanceTotals();
                totals.TournamentGold += Math.Max(0, pair.Key.Gold - pair.Value);
            }
        }

        private static void LordPartyInitializedPostfix(LordPartyComponent.InitializationArgs __instance, MobileParty mobileParty, Hero owner)
        {
            if (mobileParty == null
                || owner?.Clan == null
                || mobileParty == MobileParty.MainParty
                || mobileParty.IsCurrentlyUsedByAQuest
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || !TaleWorlds.CampaignSystem.Campaign.Current.GameStarted)
            {
                return;
            }

            var state = ReignWarEconomyTransactions.State;
            string generation = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (state == null || !state.PartyGenerations.TryGetValue(generation, out string status) || status != "pending") return;

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
                    if (stack.Character != null && !stack.Character.IsHero && stack.Character.Tier >= 1 && stack.Character.Tier <= 3
                        && stack.Character.Culture == owner.Clan.Culture)
                    {
                        candidates.Add((stack.Character, Math.Max(1f, (stack.MinValue + stack.MaxValue) * 0.5f)));
                    }
                }
            }

            if (candidates.Count == 0 && owner.Clan.BasicTroop != null && owner.Clan.BasicTroop.Tier >= 1 && owner.Clan.BasicTroop.Tier <= 3)
            {
                candidates.Add((owner.Clan.BasicTroop, 1f));
            }

            int retinueSize = MBRandom.RandomInt(10, 16), enlisted = 0;
            Settlement locality = __instance.SpawnSettlement ?? mobileParty.CurrentSettlement
                ?? (_nativeAiSpawn.Owner == owner ? _nativeAiSpawn.Locality : null);
            // No distant home-village debit when the party actually spawns in the field.
            try
            {
                for (int i = 0; i < retinueSize && candidates.Count > 0; i++)
                {
                    var supported = candidates.Where(candidate => ReignWarEconomyTransactions.Allow(
                        ReignWarEconomyTransactions.Retinue, locality, mobileParty, owner.Clan.Leader,
                        candidate.Troop, 1, true, recordBlocked: false) > 0).ToList();
                    if (supported.Count == 0) break;
                    if (!ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.Retinue, locality,
                        mobileParty, owner.Clan.Leader, ChooseWeightedTroop(supported))) break;
                    enlisted++;
                }
            }
            finally { state.PartyGenerations[generation] = "committed:" + enlisted; }
            ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Retinue).Blocked += retinueSize - enlisted;
            ReignServices.Economy?.RecordRetinue(enlisted);
            ReignLog.Info("Funded " + enlisted + " of " + retinueSize + " household troops for " + owner.StringId + ".");
        }

        private static bool BeginDailyGarrison(string source, Town town)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || town == null) return false;
            string key = source + ":" + town.Settlement.StringId;
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (state.RecruitmentDays.TryGetValue(key, out int last) && last == day) return false;
            state.RecruitmentDays[key] = day;
            return true;
        }

        private static bool GarrisonRecruitmentPrefix(GarrisonRecruitmentCampaignBehavior __instance, Town town)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_garrison").Exempt++; return true; }
            if (town?.OwnerClan?.Leader == null || town.IsUnderSiege || town.FoodChange <= 0
                || !town.GarrisonAutoRecruitmentIsEnabled || town.Settlement.Party.MapEvent != null) return false;
            if (!BeginDailyGarrison(ReignWarEconomyTransactions.Garrison, town)) return false;
            float native = NativeGarrisonChange(__instance, town, automatic: true).ResultNumber;
            var field = AccessTools.Field(typeof(GarrisonRecruitmentCampaignBehavior), "_volunteerListCache");
            // Native 1.5.7 made VolunteerTroop private. Read the audited payload
            // without exposing that type in Reign's compiled assembly references.
            var volunteerType = field.FieldType.GetGenericArguments().Single();
            var ownerField = AccessTools.Field(volunteerType, "OwnerNotable");
            var indexField = AccessTools.Field(volunteerType, "NotableVolunteerArrayIndex");
            if (ownerField?.FieldType != typeof(Hero) || indexField?.FieldType != typeof(int))
                throw new InvalidOperationException("Native garrison volunteer payload changed.");
            var volunteers = ((System.Collections.IEnumerable)field.GetValue(__instance)).Cast<object>().ToList();
            if (native <= 0) return false;
            if (town.GarrisonParty == null) town.Settlement.AddGarrisonParty();
            int remaining = Math.Min(volunteers.Count, (int)Math.Ceiling(native));
            foreach (var volunteer in volunteers)
            {
                if (remaining <= 0) break;
                var notable = (Hero)ownerField.GetValue(volunteer);
                int index = (int)indexField.GetValue(volunteer);
                var troop = notable.VolunteerTypes[index];
                var locality = notable.CurrentSettlement ?? town.Settlement;
                if (ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.Garrison, locality,
                    town.GarrisonParty, town.OwnerClan.Leader, troop, notable, index, true)) remaining--;
            }
            return false;
        }

        private static bool BaseGarrisonRecruitmentPrefix(GarrisonRecruitmentCampaignBehavior __instance, Town town)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_garrison").Exempt++; return true; }
            if (town?.OwnerClan?.Leader == null || town.IsUnderSiege || town.Settlement.Party.MapEvent != null) return false;
            if (!BeginDailyGarrison(ReignWarEconomyTransactions.BaseGarrison, town)) return false;
            int native = (int)NativeGarrisonChange(__instance, town, automatic: false).ResultNumber;
            if (native <= 0) return false;
            if (town.GarrisonParty == null) town.Settlement.AddGarrisonParty();
            for (int i = 0; i < native; i++)
                if (!ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.BaseGarrison, town.Settlement,
                    town.GarrisonParty, town.OwnerClan.Leader, town.MapFaction.BasicTroop, liability: true)) break;
            return false;
        }

        internal static ExplainedNumber NativeGarrisonChange(
            GarrisonRecruitmentCampaignBehavior behavior, Town town, bool automatic)
        {
            string name = automatic ? "GetAutoRecruitmentGarrisonChangeExplainedNumber"
                : "GetBaseGarrisonChangeExplainedNumber";
            var getter = AccessTools.DeclaredMethod(typeof(GarrisonRecruitmentCampaignBehavior), name,
                new[] { typeof(Town), typeof(bool) })
                ?? throw new MissingMethodException(typeof(GarrisonRecruitmentCampaignBehavior).FullName, name);
            // Native daily recruitment requests a quote without UI descriptions.
            // Reflection does not supply optional/new arguments on our behalf.
            return (ExplainedNumber)getter.Invoke(behavior, new object[] { town, false });
        }
    }
}
