using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public bool TryAuthorizeAction(ReignWorldActionRecord action, out ReignActionResult blockedResult)
        {
            return TryAuthorizeBusinessAction(action, out blockedResult);
        }

        public bool SeekActionApproval(string actionCorrelationId, out string result)
        {
            var hearing = _business.FirstOrDefault(x => Same(x.ActionCorrelationId, actionCorrelationId));
            result = "That government hearing is unavailable.";
            return hearing != null && VoteBusiness(hearing.BusinessId, Hero.MainHero, out result);
        }

        public bool OverrideActionPressure(string actionCorrelationId, out string result)
        {
            var hearing = _business.FirstOrDefault(x => Same(x.ActionCorrelationId, actionCorrelationId));
            result = "That government hearing is unavailable.";
            return hearing != null && ResolveBusiness(hearing.BusinessId, "accept", Hero.MainHero, true, out result);
        }

        private void ApplyActionOverrideConsequences(Kingdom kingdom, ReignGovernmentStateRecord state,
            ReignGovernmentPressureRecord pressure, int trustLoss, int loyaltyLoss, int relationLoss)
        {
            if (loyaltyLoss > 0) ApplySettlementChange(kingdom, -loyaltyLoss, 0, 0);
            foreach (ReignGovernmentSeatRecord seat in GetSeats(kingdom.StringId))
            {
                ReignGovernmentPartyRecord party = FindParty(kingdom.StringId, seat.PartyId);
                int alignment = ActionPlankAlignment((ReignGovernmentActionKind)pressure.ActionKindValue,
                    ParsePlanks(party?.PlanksCsv));
                if (alignment >= 0) continue;
                ApplyRelation(kingdom.Leader, FindHero(seat.HeroStringId), -relationLoss);
                seat.GovernmentLoyalty = Clamp(seat.GovernmentLoyalty - relationLoss, 0, 100);
                seat.Revision++;
            }
            pressure.Revision++;
            state.Revision++;
        }

        private static int CalculateActionSupport(Kingdom kingdom, ReignGovernmentStateRecord state,
            ReignGovernmentActionKind kind, ReignWorldActionRecord action)
        {
            ReignGovernmentCampaignBehavior behavior = Instance;
            IReadOnlyList<ReignGovernmentPartyRecord> coalition = behavior?.GetGoverningCoalition(kingdom.StringId)
                ?? Array.Empty<ReignGovernmentPartyRecord>();
            int coalitionSeats = coalition.Sum(x => x.SeatCount);
            int alignment = coalitionSeats <= 0
                ? ActionPlankAlignment(kind, ParsePlanks(behavior?.FindParty(kingdom.StringId, state.DominantPartyId)?.PlanksCsv))
                : (int)Math.Round(coalition.Sum(x => ActionPlankAlignment(kind, ParsePlanks(x.PlanksCsv)) * x.SeatCount)
                    / (double)coalitionSeats, MidpointRounding.AwayFromZero);
            int support = 50 + state.StanceTowardRuler / 2 + alignment;
            if (kind == ReignGovernmentActionKind.War && kingdom.Leader != null)
                support += kingdom.Leader.GetTraitLevel(DefaultTraits.Valor) * 5;
            if (kind == ReignGovernmentActionKind.Peace && kingdom.Leader != null)
                support += kingdom.Leader.GetTraitLevel(DefaultTraits.Mercy) * 5;
            if (action != null && !string.IsNullOrWhiteSpace(action.Reason)
                && action.Reason.IndexOf("defen", StringComparison.OrdinalIgnoreCase) >= 0
                && kind == ReignGovernmentActionKind.War)
                support += 10;
            return Clamp(support, 0, 100);
        }

        private static int ActionPlankAlignment(ReignGovernmentActionKind kind, IReadOnlyList<ReignGovernmentPlank> planks)
        {
            int score = 0;
            foreach (ReignGovernmentPlank plank in planks ?? Array.Empty<ReignGovernmentPlank>())
            {
                if (plank == ReignGovernmentPlank.RoyalAuthority) score += 20;
                if (plank == ReignGovernmentPlank.RepresentativeAuthority) score -= 10;
                if (kind == ReignGovernmentActionKind.War)
                {
                    if (plank == ReignGovernmentPlank.MilitaryStrength || plank == ReignGovernmentPlank.Expansion) score += 20;
                    if (plank == ReignGovernmentPlank.Peace || plank == ReignGovernmentPlank.PopularWelfare) score -= 20;
                }
                else if (kind == ReignGovernmentActionKind.Peace)
                {
                    if (plank == ReignGovernmentPlank.Peace || plank == ReignGovernmentPlank.Trade) score += 20;
                    if (plank == ReignGovernmentPlank.Expansion) score -= 20;
                }
                else if (kind == ReignGovernmentActionKind.Treaty)
                {
                    if (plank == ReignGovernmentPlank.Trade || plank == ReignGovernmentPlank.Peace) score += 15;
                    if (plank == ReignGovernmentPlank.LocalAutonomy) score -= 10;
                }
                else if (kind == ReignGovernmentActionKind.MajorSpending || kind == ReignGovernmentActionKind.Taxation)
                {
                    if (plank == ReignGovernmentPlank.PopularWelfare || plank == ReignGovernmentPlank.Infrastructure) score += 15;
                    if (plank == ReignGovernmentPlank.Trade || plank == ReignGovernmentPlank.LocalAutonomy) score -= 10;
                }
                else if (kind == ReignGovernmentActionKind.MajorJustice || kind == ReignGovernmentActionKind.FiefTransfer)
                {
                    if (plank == ReignGovernmentPlank.Justice) score += 15;
                    if (plank == ReignGovernmentPlank.NoblePrivilege || plank == ReignGovernmentPlank.ClanPrivilege) score -= 15;
                }
            }
            return Clamp(score, -40, 40);
        }

        private static bool NpcWillOverride(Kingdom kingdom, ReignGovernmentStateRecord state,
            ReignGovernmentPressureRecord pressure, ReignGovernmentActionKind kind)
        {
            Hero ruler = kingdom?.Leader;
            int chance = 25 + (ruler?.GetTraitLevel(DefaultTraits.Calculating) ?? 0) * 8
                + (ruler?.GetTraitLevel(DefaultTraits.Valor) ?? 0) * (kind == ReignGovernmentActionKind.War ? 8 : 2)
                + state.StanceTowardRuler / 5 - Math.Max(0, 50 - pressure.SupportPercent);
            int roll = (int)(ReignGovernmentRules.StableHash("npc-government-override|" + pressure.PressureId) % 100u);
            return roll < Clamp(chance, 5, 85);
        }

        private static string BuildPressureReason(ReignGovernmentStateRecord state,
            ReignGovernmentActionKind kind, int support)
        {
            string control = state.Level == 1 ? "an advisory role"
                : state.Level == 2 ? "limited formal influence"
                : state.Level == 3 ? "substantial influence"
                : state.Level == 4 ? "decisive governing influence"
                : "near-total governing control";
            return state.InstitutionName + ", holding " + control + ", must consider the proposed action through its recorded hearing.";
        }

        private static Kingdom ResolveActorKingdom(ReignWorldActionRecord action)
        {
            if (action == null) return null;
            Kingdom direct = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            if (direct != null) return direct;
            Hero hero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            if (hero?.Clan?.Kingdom != null) return hero.Clan.Kingdom;
            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            return clan?.Kingdom;
        }

        private static ReignGovernmentActionKind MapActionKind(ReignWorldActionType type) => GovernmentBusinessRules.ClassifyWorldAction(type.ToString());
    }
}
