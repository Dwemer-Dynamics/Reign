using System.Linq;
using ReignBeta.World;

namespace ReignBeta.Platform.Campaign
{
    public sealed partial class ReignAICampaignBehavior
    {
        internal ReignDiplomaticAgreementRecord RecordRecognizedIndependenceAgreement(
            ReignWorldActionRecord action, string agreementId, float observedDay)
        {
            const string kind = "recognized_independence";
            const float duration = 63f;
            if (action == null || string.IsNullOrWhiteSpace(agreementId)) return null;
            string terms = action.TermsJson ?? string.Empty;
            var matches = _agreements.Where(row => row?.AgreementId == agreementId).Take(2).ToArray();
            if (matches.Length > 0)
            {
                var prior = matches[0];
                return matches.Length == 1 && prior.Kind == kind && prior.IsPublic
                    && prior.ActorKingdomStringId == action.ActorKingdomStringId && prior.TargetKingdomStringId == action.TargetKingdomStringId
                    && prior.ActorClanStringId == (action.ActorClanStringId ?? string.Empty)
                    && prior.TargetClanStringId == (action.TargetClanStringId ?? string.Empty)
                    && prior.TargetHeroStringId == (action.TargetHeroStringId ?? string.Empty)
                    && prior.TargetSettlementStringId == (action.TargetSettlementStringId ?? string.Empty)
                    && prior.TermsJson == terms && prior.CreatedDay == observedDay && prior.ExpireDay == observedDay + duration ? prior : null;
            }

            // Recovery may publish an older, already observed agreement. It must
            // not supersede an agreement placed in this pair at or after that day.
            var active = _agreements.Where(row => row?.Kind == kind && row.IsActive && SameAgreementPair(row, action)).ToArray();
            bool publishAsActive = observedDay + duration > CurrentDay() && active.All(row => row.CreatedDay < observedDay);
            if (publishAsActive)
                foreach (var row in active)
                    EndAgreement(row, "superseded", string.Empty, string.Empty, observedDay);

            var agreement = new ReignDiplomaticAgreementRecord
            {
                AgreementId = agreementId, Kind = kind, ActorKingdomStringId = action.ActorKingdomStringId,
                TargetKingdomStringId = action.TargetKingdomStringId, ActorClanStringId = action.ActorClanStringId ?? string.Empty,
                TargetClanStringId = action.TargetClanStringId ?? string.Empty, TargetHeroStringId = action.TargetHeroStringId ?? string.Empty,
                TargetSettlementStringId = action.TargetSettlementStringId ?? string.Empty, Reason = action.Reason ?? string.Empty,
                TermsJson = terms, CreatedDay = observedDay, ExpireDay = observedDay + duration, IsPublic = true, IsActive = publishAsActive
            };
            _agreements.Add(agreement);
            return agreement;
        }
    }
}
