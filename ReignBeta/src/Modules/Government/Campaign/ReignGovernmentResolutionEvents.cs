using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using ReignBeta.World;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private void RegisterResolutionProgressEvents()
        {
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnResolutionWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnResolutionPeaceMade);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnResolutionMapEventEnded);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnResolutionVillageRaidStarted);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnResolutionSettlementOwnerChanged);
            CampaignEvents.OnGovernorChangedEvent.AddNonSerializedListener(this, OnResolutionGovernorChanged);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnResolutionPrisonerReleased);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnResolutionTroopsRecruited);
            CampaignEvents.OnBuildingLevelChangedEvent.AddNonSerializedListener(this, OnResolutionBuildingCompleted);
            CampaignEvents.OnQuestCompletedEvent.AddNonSerializedListener(this, OnResolutionQuestCompleted);
            CampaignEvents.OnIssueUpdatedEvent.AddNonSerializedListener(this, OnResolutionIssueUpdated);
        }

        private void OnResolutionWarDeclared(IFaction first, IFaction second,
            DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom actor = ResolutionKingdom(first);
            Kingdom target = ResolutionKingdom(second);
            if (actor == null || target == null) return;
            RecordResolutionProgress(actor.StringId, ReignGovernmentResolutionAction.DeclareWar,
                target.StringId, 1, "Native war declaration: " + detail + ".");
        }

        private void OnResolutionPeaceMade(IFaction first, IFaction second,
            MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom firstKingdom = ResolutionKingdom(first);
            Kingdom secondKingdom = ResolutionKingdom(second);
            if (firstKingdom == null || secondKingdom == null) return;
            RecordResolutionProgress(firstKingdom.StringId, ReignGovernmentResolutionAction.MakePeace,
                secondKingdom.StringId, 1, "Native peace concluded: " + detail + ".");
            RecordResolutionProgress(secondKingdom.StringId, ReignGovernmentResolutionAction.MakePeace,
                firstKingdom.StringId, 1, "Native peace concluded: " + detail + ".");
        }

        private void OnResolutionMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || mapEvent.Winner == null) return;
            PartyBase winner = mapEvent.GetLeaderParty(mapEvent.WinningSide);
            PartyBase loser = mapEvent.GetLeaderParty(mapEvent.DefeatedSide);
            Kingdom winnerKingdom = ResolutionKingdom(winner?.MapFaction);
            if (winnerKingdom == null) return;

            Kingdom loserKingdom = ResolutionKingdom(loser?.MapFaction);
            if (loserKingdom != null && loserKingdom != winnerKingdom)
            {
                RecordResolutionProgress(winnerKingdom.StringId,
                    ReignGovernmentResolutionAction.WinEnemyEngagements, loserKingdom.StringId, 1,
                    "Verified native battle victory over " + loserKingdom.Name + ".");
            }

            Clan defeatedClan = loser?.MapFaction as Clan;
            if (defeatedClan?.IsBanditFaction == true)
            {
                RecordResolutionProgress(winnerKingdom.StringId,
                    ReignGovernmentResolutionAction.DefeatBanditParties, string.Empty, 1,
                    "Verified defeat of bandit party " + (loser?.Name?.ToString() ?? defeatedClan.Name.ToString()) + ".");
            }

            Hero defeatedLeader = loser?.LeaderHero;
            if (defeatedLeader != null)
            {
                RecordResolutionProgress(winnerKingdom.StringId,
                    ReignGovernmentResolutionAction.HuntRaiderLord, defeatedLeader.StringId, 1,
                    "Verified defeat of the named hostile lord.");
                if (defeatedLeader.IsPrisoner)
                {
                    RecordResolutionProgress(winnerKingdom.StringId,
                        ReignGovernmentResolutionAction.CaptureCulprit, defeatedLeader.StringId, 1,
                        "Verified capture after a native battle.");
                }
            }
        }

        private void OnResolutionVillageRaidStarted(Village village)
        {
            Kingdom kingdom = ResolutionKingdom(village?.Settlement?.MapFaction);
            if (kingdom == null) return;
            ResetResolutionProgress(kingdom.StringId,
                ReignGovernmentResolutionAction.EndVillageRaidsDays,
                village?.Settlement?.StringId,
                "A native raid began and reset the unraided-day count.");
        }

        private void OnResolutionSettlementOwnerChanged(Settlement settlement, bool openToClaim,
            Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            Kingdom kingdom = newOwner?.Clan?.Kingdom ?? ResolutionKingdom(settlement?.MapFaction);
            if (kingdom == null || settlement == null) return;
            string evidence = "Native fief transfer: " + detail + ".";
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.GrantFief,
                settlement.StringId, 1, evidence);
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.GrantFief,
                newOwner?.Clan?.StringId, 1, evidence);
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ReturnFief,
                settlement.StringId, 1, evidence);
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ReturnFief,
                newOwner?.Clan?.StringId, 1, evidence);
        }

        private void OnResolutionGovernorChanged(Town town, Hero newGovernor, Hero oldGovernor)
        {
            Kingdom kingdom = ResolutionKingdom(town?.Settlement?.MapFaction);
            if (kingdom == null || newGovernor == oldGovernor) return;
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ReplaceGovernor,
                town?.Settlement?.StringId, 1,
                "Native governor changed from " + (oldGovernor?.Name?.ToString() ?? "vacant")
                + " to " + (newGovernor?.Name?.ToString() ?? "vacant") + ".");
        }

        private void OnResolutionPrisonerReleased(Hero prisoner, PartyBase captor, IFaction faction,
            EndCaptivityDetail detail, bool showNotification)
        {
            if (prisoner == null) return;
            var kingdoms = new HashSet<Kingdom>();
            Kingdom captorKingdom = ResolutionKingdom(captor?.MapFaction);
            Kingdom subjectKingdom = prisoner.Clan?.Kingdom;
            if (captorKingdom != null) kingdoms.Add(captorKingdom);
            if (subjectKingdom != null) kingdoms.Add(subjectKingdom);
            string detailText = detail.ToString();
            foreach (Kingdom kingdom in kingdoms)
            {
                RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ReleasePrisoners,
                    prisoner.StringId, 1, "Native prisoner release: " + detailText + ".");
                if (detailText.IndexOf("ransom", StringComparison.OrdinalIgnoreCase) >= 0)
                    RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.RansomPrisoners,
                        prisoner.StringId, 1, "Native ransom release: " + detailText + ".");
                if (detailText.IndexOf("exchange", StringComparison.OrdinalIgnoreCase) >= 0)
                    RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ExchangePrisoners,
                        prisoner.StringId, 1, "Native prisoner exchange: " + detailText + ".");
            }
        }

        private void OnResolutionTroopsRecruited(Hero recruiter, Settlement settlement, Hero source,
            CharacterObject troop, int amount)
        {
            Kingdom kingdom = recruiter?.Clan?.Kingdom;
            if (kingdom == null || amount <= 0) return;
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.RecruitTroops,
                settlement?.StringId, amount,
                "Native recruitment of " + amount + " " + (troop?.Name?.ToString() ?? "troops") + ".");
        }

        private void OnResolutionBuildingCompleted(Town town, Building building, int levelChange)
        {
            Kingdom kingdom = ResolutionKingdom(town?.Settlement?.MapFaction);
            if (kingdom == null || levelChange <= 0) return;
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.CompleteConstruction,
                town?.Settlement?.StringId, 1,
                "Native construction completed: " + (building?.Name?.ToString() ?? "building") + ".");
        }

        private void OnResolutionQuestCompleted(QuestBase quest, QuestBase.QuestCompleteDetails detail)
        {
            Hero solver = Hero.MainHero;
            Kingdom kingdom = solver?.Clan?.Kingdom;
            if (kingdom == null) return;
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ResolveIssueCount,
                quest?.QuestGiver?.CurrentSettlement?.StringId, 1,
                "Native quest completed: " + (quest?.Title?.ToString() ?? quest?.StringId ?? "unknown") + ".");
        }

        private void OnResolutionIssueUpdated(IssueBase issue, IssueBase.IssueUpdateDetails detail, Hero solver)
        {
            string value = detail.ToString();
            if (value.IndexOf("complete", StringComparison.OrdinalIgnoreCase) < 0
                && value.IndexOf("success", StringComparison.OrdinalIgnoreCase) < 0
                && value.IndexOf("resolve", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            Kingdom kingdom = solver?.Clan?.Kingdom;
            if (kingdom == null) return;
            RecordResolutionProgress(kingdom.StringId, ReignGovernmentResolutionAction.ResolveIssueCount,
                issue?.IssueOwner?.CurrentSettlement?.StringId, 1,
                "Native issue resolved for " + (issue?.IssueOwner?.Name?.ToString() ?? "a notable") + ".");
        }

        private void ResetResolutionProgress(string kingdomStringId,
            ReignGovernmentResolutionAction action, string targetId, string evidence)
        {
            foreach (ReignGovernmentResolutionRecord record in _resolutions.Where(x =>
                Same(x.KingdomStringId, kingdomStringId) && Same(x.Status, "active")
                && x.RouteActionValue == (int)action).ToList())
            {
                if (!string.IsNullOrWhiteSpace(targetId) && !ResolutionTargets(record, targetId)) continue;
                record.CurrentValue = 0f;
                record.EvidenceJson = MergeEvidence(record.EvidenceJson, evidence);
                record.Revision++;
            }
        }

        private static Kingdom ResolutionKingdom(IFaction faction)
        {
            return faction as Kingdom ?? (faction as Clan)?.Kingdom;
        }

        public void RecordCompletedWorldAction(ReignWorldActionRecord action)
        {
            if (action != null) CompleteGovernmentBusinessAction(action);
            if (action == null || string.IsNullOrWhiteSpace(action.ActorKingdomStringId)) return;
            ReignGovernmentResolutionAction resolutionAction;
            switch (action.Type)
            {
                case ReignWorldActionType.DiplomacySignTradeAgreement:
                    resolutionAction = ReignGovernmentResolutionAction.SignTradeAgreement;
                    break;
                case ReignWorldActionType.DiplomacySignNonAggressionPact:
                    resolutionAction = ReignGovernmentResolutionAction.SignNonAggressionPact;
                    break;
                default:
                    return;
            }
            RecordResolutionProgress(action.ActorKingdomStringId, resolutionAction,
                action.TargetKingdomStringId, 1,
                "Completed Reign world action " + action.Type + " (" + action.ActionId + ").");
        }
    }
}
