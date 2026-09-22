using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignAICampaignBehavior
    {
        private sealed class WarDeclarationContext
        {
            public string AggressorId;
            public string DefenderId;
            public string Kind;
            public string Cause;
            public string SourceActionId;
            public string ParentWarId;
        }

        private WarDeclarationContext _warDeclarationContext;

        private void RegisterWarOriginEvents()
        {
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnAuthoritativeWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnAuthoritativePeaceMade);
        }

        public void DeclareWarWithOrigin(Kingdom aggressor, Kingdom defender, string originKind,
            string cause, string sourceActionId, string parentWarId, bool rebellion)
        {
            if (aggressor == null || defender == null || aggressor == defender || aggressor.IsAtWarWith(defender)) return;
            WarDeclarationContext previous = _warDeclarationContext;
            _warDeclarationContext = new WarDeclarationContext
            {
                AggressorId = aggressor.StringId,
                DefenderId = defender.StringId,
                Kind = string.IsNullOrWhiteSpace(originKind) ? "direct" : originKind,
                Cause = cause ?? string.Empty,
                SourceActionId = sourceActionId ?? string.Empty,
                ParentWarId = parentWarId ?? string.Empty
            };
            try
            {
                if (rebellion) DeclareWarAction.ApplyByRebellion(aggressor, defender);
                else if (string.Equals(originKind, "defensive_pact", StringComparison.OrdinalIgnoreCase))
                    DeclareWarAction.ApplyByCallToWarAgreement(aggressor, defender);
                else DeclareWarAction.ApplyByKingdomDecision(aggressor, defender);
            }
            finally
            {
                _warDeclarationContext = previous;
            }
        }

        private void OnAuthoritativeWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom aggressor = first as Kingdom;
            Kingdom defender = second as Kingdom;
            if (aggressor == null || defender == null) return;

            WarDeclarationContext context = _warDeclarationContext;
            bool contextMatches = context != null
                && Same(context.AggressorId, aggressor.StringId)
                && Same(context.DefenderId, defender.StringId);
            string kind = contextMatches ? context.Kind
                : detail == DeclareWarAction.DeclareWarDetail.CausedByRebellion ? "rebellion" : "direct";
            ReignWarOriginRecord origin = new ReignWarOriginRecord
            {
                AggressorKingdomStringId = aggressor.StringId,
                DefenderKingdomStringId = defender.StringId,
                AggressorRulerHeroStringId = aggressor.Leader?.StringId ?? string.Empty,
                DefenderRulerHeroStringId = defender.Leader?.StringId ?? string.Empty,
                DeclarationDay = CurrentDay(),
                Cause = contextMatches ? context.Cause : detail.ToString(),
                SourceActionId = contextMatches ? context.SourceActionId : string.Empty,
                ParentWarId = contextMatches ? context.ParentWarId : string.Empty,
                OriginKind = kind,
                IsActive = true
            };
            _warOrigins.Add(origin);
            PurgeExpiredAgreements();

            List<ReignDiplomaticAgreementRecord> defensivePacts = _agreements
                .Where(x => x != null && x.IsActive
                    && string.Equals(x.Kind, "defensive_pact", StringComparison.OrdinalIgnoreCase)
                    && (Same(x.ActorKingdomStringId, defender.StringId)
                        || Same(x.TargetKingdomStringId, defender.StringId)))
                .Where(x => x.CreatedDay <= origin.DeclarationDay)
                .ToList();

            foreach (ReignDiplomaticAgreementRecord agreement in _agreements
                .Where(x => x != null && x.IsActive && SameKingdomPair(
                    x.ActorKingdomStringId, x.TargetKingdomStringId, aggressor.StringId, defender.StringId)).ToList())
            {
                if (IsWarIncompatibleAgreement(agreement.Kind))
                    EndAgreement(agreement, "war_declaration", aggressor.StringId, origin.WarId, origin.DeclarationDay);
            }

            if (!string.Equals(kind, "defensive_pact", StringComparison.OrdinalIgnoreCase))
                ActivateDefensivePacts(origin, aggressor, defender, defensivePacts);
        }

        private void ActivateDefensivePacts(ReignWarOriginRecord origin, Kingdom aggressor, Kingdom defender,
            IEnumerable<ReignDiplomaticAgreementRecord> pacts)
        {
            foreach (ReignDiplomaticAgreementRecord pact in pacts ?? Enumerable.Empty<ReignDiplomaticAgreementRecord>())
            {
                string allyId = OtherParty(pact, defender.StringId);
                Kingdom ally = Kingdom.All.FirstOrDefault(x => x != null && Same(x.StringId, allyId));
                ReignTreatyObligationRecord obligation = new ReignTreatyObligationRecord
                {
                    AgreementId = pact.AgreementId,
                    TriggeringWarId = origin.WarId,
                    AllyKingdomStringId = allyId,
                    AllyRulerHeroStringId = ally?.Leader?.StringId ?? string.Empty,
                    DefendedKingdomStringId = defender.StringId,
                    DefendedRulerHeroStringId = defender.Leader?.StringId ?? string.Empty,
                    AggressorKingdomStringId = aggressor.StringId,
                    TriggeredDay = origin.DeclarationDay
                };
                _treatyObligations.Add(obligation);
                if (ally == null || ally.IsEliminated || ally == aggressor || ally == defender)
                {
                    obligation.Status = "invalidated";
                    obligation.Reason = "ally_unavailable";
                    continue;
                }

                if (ally.IsAtWarWith(aggressor))
                {
                    obligation.Status = "honored_existing_war";
                    obligation.Reason = "ally_already_at_war_with_aggressor";
                    ReignWarOriginRecord existing = FindActiveWarOrigin(ally.StringId, aggressor.StringId);
                    obligation.ResultingWarId = existing?.WarId ?? string.Empty;
                    continue;
                }

                DeclareWarWithOrigin(ally, aggressor, "defensive_pact",
                    "Defensive pact obligation to " + defender.StringId, pact.AgreementId, origin.WarId, false);
                ReignWarOriginRecord child = FindActiveWarOrigin(ally.StringId, aggressor.StringId);
                obligation.ResultingWarId = child?.WarId ?? string.Empty;
                obligation.Status = ally.IsAtWarWith(aggressor) ? "honored_joined_war" : "failed";
                obligation.Reason = ally.IsAtWarWith(aggressor) ? "joined_same_day" : "native_declaration_failed";
            }
        }

        private void OnAuthoritativePeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom left = first as Kingdom;
            Kingdom right = second as Kingdom;
            if (left == null || right == null) return;
            foreach (ReignWarOriginRecord origin in _warOrigins.Where(x => x != null && x.IsActive
                && SameKingdomPair(x.AggressorKingdomStringId, x.DefenderKingdomStringId, left.StringId, right.StringId)))
            {
                origin.IsActive = false;
                origin.EndedDay = CurrentDay();
            }
        }

        private void InitializeWarOrigins()
        {
            List<Kingdom> live = Kingdom.All.Where(x => x != null && !x.IsEliminated).OrderBy(x => x.StringId).ToList();
            for (int i = 0; i < live.Count; i++)
            {
                for (int j = i + 1; j < live.Count; j++)
                {
                    Kingdom left = live[i];
                    Kingdom right = live[j];
                    if (!left.IsAtWarWith(right) || FindActiveWarOrigin(left.StringId, right.StringId) != null) continue;
                    _warOrigins.Add(new ReignWarOriginRecord
                    {
                        AggressorKingdomStringId = left.StringId,
                        DefenderKingdomStringId = right.StringId,
                        AggressorRulerHeroStringId = left.Leader?.StringId ?? string.Empty,
                        DefenderRulerHeroStringId = right.Leader?.StringId ?? string.Empty,
                        DeclarationDay = CurrentDay(),
                        Cause = "Active war discovered at timeline initialization.",
                        OriginKind = "preexisting_unknown",
                        IsActive = true
                    });
                }
            }
        }

        private ReignWarOriginRecord FindActiveWarOrigin(string leftId, string rightId)
        {
            return _warOrigins.LastOrDefault(x => x != null && x.IsActive
                && SameKingdomPair(x.AggressorKingdomStringId, x.DefenderKingdomStringId, leftId, rightId));
        }

        private static bool IsWarIncompatibleAgreement(string kind)
        {
            string value = (kind ?? string.Empty).Trim().ToLowerInvariant();
            return value == "trade_agreement" || value == "non_aggression_pact"
                || value == "defensive_pact" || value == "alliance"
                || value == "guarantee_independence" || value == "demilitarized_border"
                || value == "recognized_independence";
        }

        private static string OtherParty(ReignDiplomaticAgreementRecord agreement, string kingdomId)
        {
            return Same(agreement?.ActorKingdomStringId, kingdomId)
                ? agreement?.TargetKingdomStringId ?? string.Empty
                : agreement?.ActorKingdomStringId ?? string.Empty;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
