using System;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace ReignBeta.PartyAgency
{
    public sealed partial class ReignTemporaryPartyGuestCampaignBehavior
    {
        private static Hero GuestWageRecipient(JObject terms, Hero guest)
        {
            string relation = ReadString(terms, "wageRecipientRelation", "").ToLowerInvariant();
            Hero relative = relation == "father" ? guest?.Father : relation == "mother" ? guest?.Mother : null;
            string id = ReadString(terms, "wageRecipientHeroStringId", "");
            if (!string.IsNullOrWhiteSpace(id)) return FindHero(id);
            return relative ?? (relation.Length == 0 || relation == "self" ? guest : null);
        }

        private static bool ValidateGuestWageTerms(JObject terms, Hero hero, out string reason)
        {
            reason = string.Empty;
            if (terms["wageGold"] == null && terms["wagePeriodDays"] == null) return true;
            if (terms["wageGold"]?.Type != JTokenType.Integer || terms.Value<long>("wageGold") <= 0
                || terms.Value<long>("wageGold") > int.MaxValue)
            { reason = "Guest wages require an exact positive integer amount."; return false; }
            float period = ReadFloat(terms, "wagePeriodDays", 0f);
            if (float.IsNaN(period) || float.IsInfinity(period) || period < 1f || period > ReignCalendarService.DaysPerYear)
            { reason = "Guest wages require an explicit period from one campaign day through one year."; return false; }
            Hero recipient = GuestWageRecipient(terms, hero);
            if (recipient == null || !recipient.IsAlive || recipient == Hero.MainHero)
            { reason = "Guest wages require a resolved living recipient other than the payer."; return false; }
            string relation = ReadString(terms, "wageRecipientRelation", "").ToLowerInvariant();
            if ((relation == "father" && recipient != hero.Father) || (relation == "mother" && recipient != hero.Mother))
            { reason = "The wage recipient does not match the agreed native family relationship."; return false; }
            return true;
        }

        private static void SetGuestWageTerms(ReignTemporaryPartyGuestRecord record, JObject terms, Hero hero, float now)
        {
            record.WageGold = terms.Value<int?>("wageGold") ?? 0;
            if (record.WageGold == 0) return;
            record.WagePeriodDays = ReadFloat(terms, "wagePeriodDays", 0f);
            record.WageRecipientHeroStringId = GuestWageRecipient(terms, hero).StringId;
            record.WageNextDueDay = now + record.WagePeriodDays;
            record.WageArrearsGold = 0;
        }

        private static void PayGuestWages(ReignTemporaryPartyGuestRecord record, Hero hero, float now)
        {
            if (record.WageGold <= 0 || record.WagePeriodDays <= 0f || record.WageNextDueDay <= 0f) return;
            float end = record.WageAccrualEndDay;
            if (record.TermKind == ReignTemporaryGuestTermKind.Fixed)
                end = end > 0 ? Math.Min(end, record.ReviewDueDay) : record.ReviewDueDay;
            if (!ReignGuestWagePolicy.TryDue(record.WageGold, record.WagePeriodDays, record.WageNextDueDay,
                now, end, out int periods, out long due) || due <= 0) return;
            Hero recipient = FindHero(record.WageRecipientHeroStringId);
            if (due > int.MaxValue || recipient == null || !recipient.IsAlive || Hero.MainHero.Gold < due)
            {
                record.WageArrearsGold = (int)Math.Min(int.MaxValue, due);
                if (record.Phase == ReignTemporaryGuestPhase.Active) record.Phase = ReignTemporaryGuestPhase.ReviewDue;
                record.LastReviewReason = "The agreed service wages are due but cannot be paid to the agreed recipient.";
                return;
            }
            int payerBefore = Hero.MainHero.Gold, recipientBefore = recipient.Gold;
            if ((long)recipientBefore + due > int.MaxValue) return;
            try
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, recipient, (int)due, true);
                if (Hero.MainHero.Gold != payerBefore - due || recipient.Gold != recipientBefore + due)
                    throw new InvalidOperationException("Guest wage payment did not match the agreed native balances.");
            }
            catch (Exception ex)
            {
                Hero.MainHero.ChangeHeroGold(payerBefore - Hero.MainHero.Gold);
                recipient.ChangeHeroGold(recipientBefore - recipient.Gold);
                record.WageArrearsGold = (int)due;
                if (record.Phase == ReignTemporaryGuestPhase.Active) record.Phase = ReignTemporaryGuestPhase.ReviewDue;
                record.LastReviewReason = "The native wage transfer failed; the unpaid balance remains due.";
                ReignLog.Warn("Guest wage transfer rolled back for " + record.HeroStringId + ": " + ex);
                return;
            }
            record.WageNextDueDay += periods * record.WagePeriodDays;
            record.WageLastPaidDay = now;
            record.WageArrearsGold = 0;
            InformationManager.DisplayMessage(new InformationMessage("Paid " + due + " denars to " + recipient.Name + " for " + hero.Name + "'s service."));
        }
    }
}
