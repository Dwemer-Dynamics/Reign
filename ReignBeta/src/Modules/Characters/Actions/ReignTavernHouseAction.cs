using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Campaign;

namespace ReignBeta.Actions
{
    internal static class ReignTavernHouseAction
    {
        private static object _owner;
        private static Func<TavernHouseActionContext> _context;
        private static readonly Dictionary<string, JObject> Preparations = new Dictionary<string, JObject>(StringComparer.Ordinal);
        internal static TavernHouseActionContext Context => _context?.Invoke();
        internal static void Open(object owner, Func<TavernHouseActionContext> context) { _owner = owner; _context = context; Preparations.Clear(); }
        internal static void Close(object owner) { if (!ReferenceEquals(owner, _owner)) return; _owner = null; _context = null; Preparations.Clear(); }
        internal static bool ContextMatches(TavernHouseActionContext scope, ActionAgreement agreement)
            => scope != null && Context != null && scope.CampaignId == Context.CampaignId && scope.TimelineId == Context.TimelineId
                && scope.TownId == Context.TownId && scope.SessionId == Context.SessionId
                && (agreement.NpcId.Length == 0 || agreement.NpcId == scope.MadamId);
        internal static List<string> Check(ActionAgreement agreement)
        {
            var scope = Context;
            if (scope == null) return new List<string> { "Private visit actions are available only while negotiating in Visit the Madam." };
            return TavernHouseVisitAction.Validate(agreement, scope);
        }
        internal static string Sentence(ActionAgreement agreement) => Context == null ? "Private visit agreement" : TavernHouseVisitAction.Sentence(agreement, Context);
        internal static void RegisterPrepared(JObject response)
        {
            if (response?.Value<bool?>("ok") != true || !(response["preparedVisit"] is JObject prepared)) return;
            if (prepared.Value<string>("schema") != "reign-tavern-prepared-visit-v1" || Context == null)
                throw new InvalidOperationException("The prepared visit has no active Tavern House context.");
            string id = prepared.Value<string>("agreementId");
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("The prepared visit has no agreement identity.");
            Preparations[id] = (JObject)prepared.DeepClone();
        }
        internal static List<string> CheckPrepared(ActionAgreement agreement)
        {
            var errors = Check(agreement);
            if (errors.Count > 0) return errors;
            if (!agreement.HasBothConsents) errors.Add("Both parties must accept the exact visit terms.");
            if (!Preparations.TryGetValue(agreement.Id, out var prepared)
                || prepared.Value<string>("termsHash") != agreement.TermsHash()
                || prepared.Value<string>("fingerprint") != TavernHouseVisitAction.Fingerprint(agreement)
                || prepared.Value<string>("madamId") != agreement.NpcId
                || prepared.Value<string>("preparationId") != TavernHouseVisitAction.Hash(new[] { agreement.Id, agreement.TermsHash(), Context.CampaignId, Context.TimelineId, Context.SessionId }))
                errors.Add("The server has not prepared this exact accepted visit. Retry the conversation before paying.");
            return errors;
        }
        internal static JObject Capture(ActionAgreement agreement)
        {
            var errors = CheckPrepared(agreement); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            return (JObject)Preparations[agreement.Id].DeepClone();
        }
        internal static void Apply(ActionAgreement agreement)
        {
            var errors = CheckPrepared(agreement); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            var quote = TavernHouseVisitAction.Quote(agreement, Context);
            var house = ReignTavernHouseCampaignBehavior.Instance;
            if (house == null) throw new InvalidOperationException("The Tavern House is unavailable.");
            if (!house.PayPreparedVisit(quote, agreement.TermsHash(), Preparations[agreement.Id].Value<string>("preparationId"), agreement.NpcId, Context.SessionId, out string reason))
                throw new InvalidOperationException(reason);
        }
        internal static bool Observe(ActionAgreement agreement) => ReignTavernHouseCampaignBehavior.Instance?.VisitForAgreement(agreement.Id, agreement.TermsHash())?.ServerConfirmed == true;
        internal static void Return(ActionAgreement agreement)
        {
            // A paid or uncertain native transfer remains recoverable. Cancellation must never
            // manufacture a refund or repeat the debit based on a later wallet balance.
            var house = ReignTavernHouseCampaignBehavior.Instance;
            house?.RetainUnconfirmedVisit(agreement.Id, agreement.TermsHash());
            var receipt = house?.VisitForAgreement(agreement.Id, agreement.TermsHash());
            if (receipt != null && !receipt.ServerConfirmed)
                throw new AgreementRecoveryException("The saved visit requires payment recovery; no second debit is permitted.", JObject.FromObject(receipt).ToString(), null);
        }
    }
}
