using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using static ReignBeta.Platform.ReignNumeric;

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

        internal static ReignGovernmentActionKind MapActionKind(ReignWorldActionType type) => GovernmentBusinessRules.ClassifyWorldAction(type.ToString());
    }
}
