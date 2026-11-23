using System;
using System.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using ReignBeta.Platform.Integration;

namespace ReignBeta.Characters.Campaign
{
    public sealed partial class ReignTavernHouseCampaignBehavior
    {
        internal TavernHouseActionContext VisitActionContext(Settlement town, string sessionId)
        {
            if (!_ready || town == null || Hero.MainHero?.CurrentSettlement != town || GetOpenVisit(town) != null) return null;
            Hero madam = GetMadam(town);
            if (madam == null || !IsAvailable(madam, town)) return null;
            return new TavernHouseActionContext { CampaignId = ReignServerClient.GetCampaignId(), TimelineId = Timeline,
                TownId = town.StringId, SessionId = sessionId, PlayerId = Hero.MainHero.StringId, MadamId = madam.StringId,
                Roster = GetStaff(town).Select(h => new TavernHouseActionPerson { HeroId = h.StringId, Name = h.Name.ToString(),
                    Available = IsAvailable(h, town), Adult = h.Age >= 18 && !h.IsChild }).ToList() };
        }
        internal TavernHouseVisitReceipt VisitForAgreement(string id, string hash) => _state.Visits.SingleOrDefault(v =>
            v.CampaignId == ReignServerClient.GetCampaignId() && v.TimelineId == Timeline && v.AgreementId == id && v.ActionTermsHash == hash);
        internal void RetainUnconfirmedVisit(string id, string hash)
        {
            var receipt = VisitForAgreement(id, hash);
            if (receipt != null && !receipt.ServerConfirmed && receipt.PaymentState == "applying") receipt.PaymentState = "recovery_required";
        }
        internal bool PayPreparedVisit(TavernHouseVisitQuote quote, string hash, string preparation, string payee, string session,
            out string reason)
        {
            reason = "";
            var prior = _state.Visits.SingleOrDefault(v => v.CampaignId == quote.CampaignId && v.TimelineId == quote.TimelineId && v.AgreementId == quote.AgreementId);
            if (prior != null)
            {
                if (!TavernHouseRules.Matches(prior, quote) || prior.ActionTermsHash != hash || prior.PreparationId != preparation || prior.PayeeHeroId != payee)
                { reason = "This visit identity already has different saved terms."; return false; }
                if (prior.Paid && prior.PaymentState == "paid") return true;
                reason = "This saved payment requires recovery. It will not be charged again."; return false;
            }
            var scope = VisitActionContext(Settlement.Find(quote.TownId), session);
            if (scope == null || scope.MadamId != payee || scope.PlayerId != quote.PlayerHeroId || scope.CampaignId != quote.CampaignId || scope.TimelineId != quote.TimelineId
                || quote.Charges.Any(c => !scope.Roster.Any(p => p.HeroId == c.HeroId && p.Available && p.Adult))
                || Hero.MainHero.IsChild || !Hero.MainHero.IsAlive || Hero.MainHero.IsPrisoner)
            { reason = "The player, house or agreed participants are no longer available."; return false; }
            if (!TavernHouseRules.ValidateQuote(quote, out reason)) return false;
            if (Hero.MainHero.Gold < quote.TotalGold) { reason = "You do not have the agreed payment."; return false; }
            var receipt = new TavernHouseVisitReceipt { VisitId = Guid.NewGuid().ToString("N"), AgreementId = quote.AgreementId,
                CampaignId = quote.CampaignId, TimelineId = quote.TimelineId, TownId = quote.TownId, PlayerHeroId = quote.PlayerHeroId,
                Charges = quote.Charges, ParticipantHeroIds = quote.Charges.Select(c => c.HeroId).ToList(), QuoteRevision = quote.QuoteRevision,
                PaidGold = quote.TotalGold, StartedDay = Day, ActionTermsHash = hash, PreparationId = preparation, PayeeHeroId = payee, ConversationId = session };
            _state.Visits.Add(receipt);
            bool paid = TavernHouseVisitAction.TryPay(receipt, () => Hero.MainHero.Gold,
                () => { if (quote.TotalGold > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, Hero.Find(payee), quote.TotalGold, true); });
            if (!paid) reason = "Payment needs recovery; this visit is saved and will not be charged again.";
            return paid;
        }
    }
}
