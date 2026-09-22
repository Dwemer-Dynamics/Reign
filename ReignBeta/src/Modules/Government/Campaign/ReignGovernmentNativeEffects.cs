using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private void VerifyNativeBusinessEffect(ReignGovernmentBusinessRecord record, KingdomDecision decision, string optionId)
        {
            if (optionId == "reject") return;
            bool applied;
            if (decision is KingdomPolicyDecision policy)
                applied = decision.Kingdom.HasPolicy(policy.Policy) == !record.IsRepeal;
            else if (decision is DeclareWarDecision war) applied = decision.Kingdom.IsAtWarWith(war.FactionToDeclareWarOn);
            else if (decision is MakePeaceKingdomDecision peace) applied = !decision.Kingdom.IsAtWarWith(peace.FactionToMakePeaceWith)
                && decision.Kingdom.GetStanceWith(peace.FactionToMakePeaceWith).GetDailyTributeToPay(decision.Kingdom) == peace.DailyTributeToBePaid
                && (peace.DailyTributeToBePaid == 0 || decision.Kingdom.GetStanceWith(peace.FactionToMakePeaceWith).DailyTributeInstallments == peace.DailyTributeDurationInDays);
            else if (decision is SettlementClaimantDecision allocation)
                applied = Same("clan:" + allocation.Settlement.OwnerClan?.StringId, optionId);
            else if (decision is SettlementClaimantPreliminaryDecision preliminary)
                applied = _business.Any(x => x.Kind == "fief_allocation" && Same(x.TargetId, preliminary.Settlement.StringId)
                    && Same(x.KingdomStringId, record.KingdomStringId) && x.CreatedDay >= record.CreatedDay);
            else if (decision is ExpelClanFromKingdomDecision expulsion) applied = expulsion.ClanToExpel.Kingdom != decision.Kingdom;
            else if (decision is StartAllianceDecision alliance)
                applied = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>()?.IsAllyWithKingdom(decision.Kingdom, alliance.KingdomToStartAllianceWith) == true;
            else if (decision is TradeAgreementDecision trade)
            {
                var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                applied = behavior != null && behavior.HasTradeAgreement(decision.Kingdom, trade.TargetKingdom, out _);
            }
            else if (decision is ProposeCallToWarAgreementDecision call)
                applied = VerifyCallToWar(decision.Kingdom, call.CalledKingdom, call.KingdomToCallToWarAgainst);
            else if (decision is AcceptCallToWarAgreementDecision accept)
                applied = VerifyCallToWar(accept.CallingKingdom, decision.Kingdom, accept.KingdomToCallToWarAgainst);
            else throw new NotSupportedException("Missing native government effect verification.");
            if (!applied) throw new InvalidOperationException("Native government effects did not establish the approved outcome.");
        }

        private static bool VerifyCallToWar(Kingdom calling, Kingdom called, Kingdom enemy)
        {
            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            return behavior != null && called.IsAtWarWith(enemy)
                && behavior.IsAtWarByCallToWarAgreement(called, enemy, out Kingdom recordedCalling) && recordedCalling == calling;
        }

        private void RecordNativeDiplomaticRelationReceipt(ReignGovernmentBusinessRecord record, KingdomDecision decision)
        {
            // Native call-to-war effects already change the two rulers' relationship. Do not add it twice.
            Kingdom other = decision is ProposeCallToWarAgreementDecision call ? call.CalledKingdom
                : decision is AcceptCallToWarAgreementDecision accept ? accept.CallingKingdom : null;
            if (other?.Leader == null) return;
            var receipts = JArray.Parse(record.ConsequenceReceiptsJson);
            if (!receipts.Values<string>().Contains(other.Leader.StringId)) receipts.Add(other.Leader.StringId);
            record.ConsequenceReceiptsJson = receipts.ToString(Formatting.None);
        }
    }
}
