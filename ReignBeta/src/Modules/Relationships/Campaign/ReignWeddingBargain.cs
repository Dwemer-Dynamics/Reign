using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    internal static class ReignWeddingBargain
    {
        internal const string ReleasePrisoner = "marriage.release_prisoner";
        internal static bool IsAsset(string action) => action == "give.gold" || action == "give.item"
            || action == "give.prisoner" || action == "give.settlement" || action == ReleasePrisoner;

        internal static ActionClause Asset(string action, Hero giver, Hero receiver, int quantity, string asset = "", string modifier = "")
        {
            var clause = new ActionClause { ActionId = action, ActorId = giver.StringId, RecipientId = receiver.StringId };
            clause.Arguments["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (asset.Length != 0) clause.Arguments["assetId"] = asset;
            if (modifier.Length != 0) clause.Arguments["variantId"] = modifier;
            return clause;
        }

        internal static Hero ReleaseCandidate(ActionClause leg)
        {
            var captor = ReignActionService.HeroById(leg.ActorId);
            var prisoner = ReignActionService.HeroById(ReignActionService.Arg(leg, "assetId"));
            var holdingParty = prisoner?.PartyBelongedToAsPrisoner;
            string custody = ReignActionService.Arg(leg, "custodySettlementId");
            bool lawfulCustody = custody.Length == 0
                ? captor?.PartyBelongedTo?.LeaderHero == captor && holdingParty == captor?.PartyBelongedTo?.Party
                : holdingParty?.Settlement?.StringId == custody && holdingParty.Owner == captor;
            if (prisoner?.IsPrisoner != true || holdingParty == null || !lawfulCustody
                || !holdingParty.PrisonRoster.Contains(prisoner.CharacterObject) || prisoner == Hero.MainHero)
                throw new InvalidOperationException("The promised captive is no longer held by the agreed party.");
            return prisoner;
        }

        internal static void Check(ActionAgreement terms, Hero first, Hero second, bool trustedConsideration = false)
        {
            if (terms == null) return;
            if (terms.Clauses.Any(c => c == null || !IsAsset(c.ActionId) && !(trustedConsideration && c.ActionId == "give.ship") || c.Installments != 1
                || c.ActorId == c.RecipientId || !trustedConsideration
                    && ((c.ActorId != terms.PlayerId && c.ActorId != terms.NpcId)
                        || (c.RecipientId != terms.PlayerId && c.RecipientId != terms.NpcId))))
                throw new InvalidOperationException("The bargain contains an unsupported transfer.");
            var repeated = terms.Clauses.Where(c => c.ActionId == ReleasePrisoner || c.ActionId == "give.prisoner")
                .GroupBy(c => ReignActionService.Arg(c, "assetId")).Any(g => g.Count() > 1);
            if (repeated) throw new InvalidOperationException("The same captive appears twice in this bargain.");
            var errors = ReignAssetTransfer.CheckAggregate(terms).ToList();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join(" ", errors));
            foreach (var leg in terms.Clauses)
            {
                var giver = ReignActionService.HeroById(leg.ActorId);
                var receiver = ReignActionService.HeroById(leg.RecipientId);
                if (giver?.IsAlive != true || receiver?.IsAlive != true || giver.IsPrisoner || receiver.IsPrisoner)
                    throw new InvalidOperationException("A participant cannot deliver the agreed consideration.");
                var finalClan = TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
                bool GiverLeaves() => (giver == first || giver == second) && giver.Clan != finalClan;
                bool ReceiverLeaves() => (receiver == first || receiver == second) && receiver.Clan != finalClan;
                if ((leg.ActionId == "give.prisoner" || leg.ActionId == "give.settlement" || leg.ActionId == "give.ship" || leg.ActionId == ReleasePrisoner)
                    && (GiverLeaves() || ReceiverLeaves()))
                    throw new InvalidOperationException("The wedding would change custody of a promised asset.");
                if (leg.ActionId == ReleasePrisoner) ReleaseCandidate(leg);
            }
        }

        internal static void Settle(ReignWedding wedding, Hero first, Hero second)
        {
            var terms = wedding.Consideration;
            if ((terms?.TermsHash() ?? "") != wedding.TermsHash)
                throw new InvalidOperationException("The saved marriage terms changed after agreement.");
            Check(terms, first, second, wedding.TrustedConsideration);
            var legs = terms?.Clauses ?? new List<ActionClause>();
            var captures = legs.Where(c => c.ActionId != ReleasePrisoner)
                .ToDictionary(c => c.Id, c => ReignAssetTransfer.Capture(terms, c));
            wedding.Custody = captures;
            wedding.SettlementEvidence.Clear();
            var reversible = legs.Where(c => c.ActionId == "give.gold" || c.ActionId == "give.item").ToList();
            bool NativeChanged() => first.Spouse != null || second.Spouse != null;
            bool VerifiedUnion() => first.Spouse == second && second.Spouse == first
                && first.Clan?.StringId == wedding.ReceivingClanId && second.Clan?.StringId == wedding.ReceivingClanId;
            void Marry()
            {
                try { MarriageAction.Apply(first, second, true); }
                catch (Exception ex) when (VerifiedUnion())
                {
                    wedding.SettlementEvidence["native_marriage"] = new JObject { ["callbackError"] = ex.Message };
                }
                if (!VerifiedUnion())
                    throw new InvalidOperationException("The native union or clan placement could not be verified.");
            }

            wedding.State = WeddingState.Applying;
            try
            {
                ReignAssetTransfer.DeliverWeddingReversible(reversible, captures, Marry, NativeChanged,
                    (clause, receipt) => wedding.SettlementEvidence[clause.Id] = receipt);
            }
            catch (Exception ex)
            {
                if (!NativeChanged() && !(ex is AgreementRecoveryException))
                {
                    wedding.State = WeddingState.Ready;
                    wedding.SettlementEvidence.Clear();
                }
                else if (NativeChanged())
                    wedding.Settled.AddRange(reversible.Where(c => wedding.SettlementEvidence.ContainsKey(c.Id)).Select(c => c.Id));
                throw;
            }
            wedding.Settled.AddRange(reversible.Select(c => c.Id));
            wedding.Settled.Add("native_marriage");
            foreach (var leg in legs.Where(c => !reversible.Contains(c)))
            {
                var receipt = new JObject();
                wedding.SettlementEvidence[leg.Id] = receipt;
                if (leg.ActionId == ReleasePrisoner)
                {
                    var prisoner = ReleaseCandidate(leg);
                    receipt["prisonerId"] = prisoner.StringId;
                    try { EndCaptivityAction.ApplyByRansom(prisoner, ReignActionService.HeroById(leg.RecipientId)); }
                    catch (Exception ex) when (!prisoner.IsPrisoner) { receipt["callbackError"] = ex.Message; }
                    receipt["verified"] = !prisoner.IsPrisoner;
                    if (prisoner.IsPrisoner) throw new InvalidOperationException("Native release did not free the agreed captive.");
                }
                else ReignAssetTransfer.DeliverWeddingCustody(leg, captures[leg.Id], receipt);
                wedding.Settled.Add(leg.Id);
            }
        }
    }
}
