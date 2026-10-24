using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRulerCampaignBehavior
    {
        private static bool PrisonerFixture(string profile) => profile == "prison_guard" || profile == "ransom_offer"
            || profile == "barter_release" || profile == "barter_transfer";

        // Fixed profiles only: no reflected target, arbitrary perk, consent or clock input.
        internal static void ValidatePrisonerFixtureInput(string profile, int amount, string heroId, string otherHeroId)
        {
            if (!PrisonerFixture(profile) || amount < 1 || amount > 4000 || (profile == "prison_guard" && amount != 1)
                || string.IsNullOrWhiteSpace(heroId) || string.IsNullOrWhiteSpace(otherHeroId)
                || heroId == otherHeroId || heroId.Length > 128 || otherHeroId.Length > 128)
                throw new ArgumentException("Prisoner fixtures require distinct bounded heroes, guard amount one or payment 1–4000.");
        }

        private static void ValidatePrisonerFixture(string profile, Hero hero, Hero other, int amount)
        {
            if (other?.IsAlive != true || other.IsChild || other.PartyBelongedTo?.MapEvent != null
                || other.PartyBelongedTo?.SiegeEvent != null)
                throw new InvalidOperationException("The second fixture hero must be a living, adult, idle participant.");
            if (profile == "prison_guard")
            {
                var party = hero.PartyBelongedTo;
                if (hero.IsPrisoner || party?.LeaderHero != hero || party.IsCurrentlyAtSea
                    || !other.IsPrisoner || other.PartyBelongedToAsPrisoner != party.Party || !BlocksRelease(other)
                    || party.PrisonRoster.TotalManCount > party.Party.PrisonerSizeLimit
                    || hero.GetPerkValue(DefaultPerks.Riding.CavalryTactics) || hero.GetPerkValue(DefaultPerks.Scouting.VantagePoint))
                    throw new InvalidOperationException("Prepare a free land-party leader holding the sentenced ruler within native prisoner capacity.");
                return;
            }
            if (hero == Hero.MainHero || !hero.IsPrisoner || !BlocksRelease(hero)
                || hero.PartyBelongedToAsPrisoner != MobileParty.MainParty?.Party || Hero.MainHero.IsPrisoner
                || MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.SiegeEvent != null || MobileParty.MainParty.IsCurrentlyAtSea
                || other.IsPrisoner || other.PartyBelongedTo?.LeaderHero != other
                || other.MapFaction != hero.MapFaction || other.MapFaction == Hero.MainHero.MapFaction)
                throw new InvalidOperationException("Paid release probes require a sentenced NPC in the player party and a free enemy party leader from that prisoner's realm.");
            if (other.Gold < amount + 1000) throw new InvalidOperationException("Fund the payer before the probe; the fixture never creates gold.");
            if (profile == "ransom_offer")
            {
                var behavior = NativeCampaign.Current.CampaignBehaviorManager.GetBehavior<RansomOfferCampaignBehavior>()
                    ?? throw new InvalidOperationException("Native ransom behavior unavailable.");
                if (AccessTools.Field(typeof(RansomOfferCampaignBehavior), "_currentRansomHero").GetValue(behavior) != null)
                    throw new InvalidOperationException("An existing native ransom offer must be resolved before this fixture.");
            }
        }

        private static void PreparePrisonGuard(Hero hero)
        {
            foreach (var perk in new[] { DefaultPerks.Riding.MountedPatrols, DefaultPerks.Scouting.KeenSight })
            {
                int required = Convert.ToInt32(perk.RequiredSkillValue);
                if (hero.GetSkillValue(perk.Skill) < required)
                {
                    hero.HeroDeveloper.SetInitialSkillLevel(perk.Skill, required);
                    hero.HeroDeveloper.InitializeSkillXp(perk.Skill);
                }
                hero.HeroDeveloper.AddPerk(perk);
            }
        }

        private static JObject ObservePrisonGuard(Hero guard, Hero prisoner) => new JObject {
            ["guardId"] = guard.StringId, ["prisonerId"] = prisoner.StringId,
            ["partyId"] = guard.PartyBelongedTo.StringId,
            ["mountedPatrols"] = guard.GetPerkValue(DefaultPerks.Riding.MountedPatrols),
            ["keenSight"] = guard.GetPerkValue(DefaultPerks.Scouting.KeenSight),
            ["ridingSkill"] = guard.GetSkillValue(DefaultSkills.Riding),
            ["scoutingSkill"] = guard.GetSkillValue(DefaultSkills.Scouting),
            ["mountedPatrolsPrimary"] = DefaultPerks.Riding.MountedPatrols.PrimaryBonus,
            ["keenSightSecondary"] = DefaultPerks.Scouting.KeenSight.SecondaryBonus,
            ["atSea"] = guard.PartyBelongedTo.IsCurrentlyAtSea,
            ["prisonerCount"] = guard.PartyBelongedTo.PrisonRoster.TotalManCount,
            ["prisonerLimit"] = guard.PartyBelongedTo.Party.PrisonerSizeLimit,
            ["prisonerHeld"] = prisoner.IsPrisoner && prisoner.PartyBelongedToAsPrisoner == guard.PartyBelongedTo.Party
        };

        private static JObject ObservePrisonerPayment(Hero prisoner, Hero payer) => new JObject {
            ["prisonerId"] = prisoner.StringId, ["payerId"] = payer.StringId,
            ["payerGold"] = payer.Gold, ["captorGold"] = Hero.MainHero.Gold,
            ["prisonerHeld"] = prisoner.IsPrisoner,
            ["custodyFaction"] = prisoner.PartyBelongedToAsPrisoner?.MapFaction?.StringId,
            ["sourceCount"] = MobileParty.MainParty.PrisonRoster.GetTroopRoster()
                .Where(r => r.Character == prisoner.CharacterObject).Sum(r => r.Number),
            ["destinationCount"] = payer.PartyBelongedTo.PrisonRoster.GetTroopRoster()
                .Where(r => r.Character == prisoner.CharacterObject).Sum(r => r.Number)
        };

        private static void RunPrisonerPaymentFixture(string profile, Hero prisoner, Hero payer, int amount, JObject receipt)
        {
            if (payer.Gold < amount + 1000) throw new InvalidOperationException("Fund the payer before the probe; the fixture never creates gold.");
            receipt["paidBefore"] = ObservePrisonerPayment(prisoner, payer);
            receipt["operatorProbe"] = true;
            receipt["paymentAmount"] = amount;
            receipt["goldFirst"] = profile != "ransom_offer";
            receipt["nativeHandler"] = profile == "ransom_offer" ? "RansomOfferCampaignBehavior.AcceptRansomOffer" : "BarterManager.ApplyBarterOffer";
            try
            {
                if (profile == "ransom_offer") RunRansomOfferFixture(prisoner, payer, amount);
                else RunBarterFixture(profile, prisoner, payer, amount);
                receipt["nativeHandlerReturned"] = true;
            }
            finally { receipt["paidAfter"] = ObservePrisonerPayment(prisoner, payer); }
        }

        private static void RunRansomOfferFixture(Hero prisoner, Hero payer, int amount)
        {
            var behavior = NativeCampaign.Current.CampaignBehaviorManager.GetBehavior<RansomOfferCampaignBehavior>();
            var heroField = AccessTools.Field(typeof(RansomOfferCampaignBehavior), "_currentRansomHero");
            var payerField = AccessTools.Field(typeof(RansomOfferCampaignBehavior), "_currentRansomPayer");
            object oldHero = heroField.GetValue(behavior), oldPayer = payerField.GetValue(behavior);
            try
            {
                heroField.SetValue(behavior, prisoner); payerField.SetValue(behavior, payer);
                AccessTools.Method(typeof(RansomOfferCampaignBehavior), "AcceptRansomOffer", new[] { typeof(int) })
                    .Invoke(behavior, new object[] { amount });
            }
            finally { heroField.SetValue(behavior, oldHero); payerField.SetValue(behavior, oldPayer); }
        }

        private static void RunBarterFixture(string profile, Hero prisoner, Hero payer, int amount)
        {
            var source = MobileParty.MainParty.Party; var destination = payer.PartyBelongedTo.Party;
            var gold = new GoldBarterable(payer, Hero.MainHero, destination, source, amount) { CurrentAmount = amount };
            Barterable release = profile == "barter_release"
                ? (Barterable)new SetPrisonerFreeBarterable(prisoner, Hero.MainHero, source, payer)
                : new TransferPrisonerBarterable(prisoner, Hero.MainHero, source, payer, destination);
            release.CurrentAmount = 1;
            // Deliberately put real gold first: blocking only EndCaptivity would lose money.
            var offered = new List<Barterable> { gold, release };
            foreach (var entry in offered) entry.SetIsOffered(true);
            AccessTools.Method(typeof(BarterManager), "ApplyBarterOffer", new[] { typeof(Hero), typeof(Hero), typeof(List<Barterable>) })
                .Invoke(NativeCampaign.Current.BarterManager, new object[] { payer, Hero.MainHero, offered });
        }
    }
}
