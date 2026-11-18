using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.Shared.Rulers;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;
using static ReignBeta.WorldSimulation.Rulers.ReignRulerCampaignBehavior;
using ReignBeta.WorldSimulation.Actions;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.News;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Rulers
{
    // Harness-owned ruler fixture controls. One-use control state stays on ReignRulerCampaignBehavior.
    internal static partial class ReignRulerFixtureControls
    {
        // Called only after the enclosing guarded structured-action host has checked
        // exact enrollment, native save identity, paused time and recovery checkpoint.
        internal static void ValidateJudgmentDrawInput(int amount, string prisoner, string victor)
        {
            if (amount < 0 || amount >= 4000 || string.IsNullOrWhiteSpace(prisoner) || string.IsNullOrWhiteSpace(victor)
                || prisoner == victor || prisoner.Length > 128 || victor.Length > 128)
                throw new ArgumentException("Judgment draws require distinct bounded heroes and amount zero through 3999.");
        }
        internal static JObject TakeJudgmentDrawControl(System.Collections.Generic.IDictionary<string, JObject> controls, string prisoner)
        {
            if (!controls.TryGetValue(prisoner, out var control)) return null;
            controls.Remove(prisoner); return control;
        }
        internal static bool JudgmentDrawEligible(RulerJudgment judgment, string prisoner, string victor,
            bool enrollmentMatches, bool custodyMatches, bool aiSovereign, double draw) => judgment != null
            && judgment.Stage == RulerJudgmentStage.AwaitingDecision && judgment.Draw < 0
            && judgment.PrisonerId == prisoner && judgment.VictorId == victor
            && enrollmentMatches && custodyMatches && aiSovereign && ReignRulerPolicy.Finite(draw) && draw >= 0 && draw < 1;
        internal static bool TryConsumeJudgmentDraw(ReignRulerCampaignBehavior rulers, RulerJudgment judgment, out double draw)
        {
            draw = 0;
            var control = TakeJudgmentDrawControl(rulers._judgmentControls, judgment.PrisonerId);
            if (control == null) return false;
            var prisoner = HeroById(judgment.PrisonerId); var victor = HeroById(judgment.VictorId);
            double selected = control.Value<double>("draw");
            bool matches = JudgmentDrawEligible(judgment, control.Value<string>("prisonerId"), control.Value<string>("victorId"),
                ReignLiveInteractionTestHost.MatchesRulerFixture(control["enrollment"] as JObject),
                prisoner?.IsPrisoner == true && prisoner.PartyBelongedToAsPrisoner?.MapFaction == victor?.Clan?.Kingdom,
                Sovereign(victor) && victor != Hero.MainHero, selected);
            string operation = control.Value<string>("operationId");
            var receipt = ReignServices.Agreement.NativeOperation(operation) ?? new JObject();
            receipt["status"] = matches ? "consumed" : "invalidated";
            receipt["judgmentId"] = judgment.Id; receipt["actualVictorId"] = judgment.VictorId;
            receipt["observedDay"] = Day; receipt["civilWar"] = judgment.CivilWar; receipt["rebelVictory"] = judgment.RebelVictory;
            if (matches) { receipt["selectedDraw"] = selected; draw = selected; }
            else receipt["reason"] = "The enrolled AI ruler, custody or pending decision changed.";
            ReignServices.Agreement.SaveNativeOperation(operation, receipt);
            return matches;
        }
        internal static bool TryConsumeCaptureRoll(ReignRulerCampaignBehavior rulers, Hero hero, out double roll)
        {
            roll = 0;
            if (!rulers._captureControls.TryGetValue(hero.StringId, out var control)) return false;
            rulers._captureControls.Remove(hero.StringId);
            if (!ReignLiveInteractionTestHost.MatchesRulerFixture(control["enrollment"] as JObject)) return false;
            roll = control.Value<double>("roll");
            string operation = control.Value<string>("operationId");
            var receipt = ReignServices.Agreement.NativeOperation(operation);
            if (receipt == null) return false;
            receipt["status"] = "consumed"; receipt["selectedRoll"] = roll;
            receipt["observedWounded"] = hero.IsWounded; receipt["consumedDay"] = Day;
            receipt["expectedCapture"] = ReignRulerPolicy.Capture(hero.IsWounded, roll);
            ReignServices.Agreement.SaveNativeOperation(operation, receipt);
            return true;
        }
        internal static JObject PrepareFixture(ReignRulerCampaignBehavior rulers, JObject testCase, string operation, JObject enrollment)
        {
            var a = testCase["arguments"] as JObject ?? throw new InvalidOperationException("Fixture arguments are required.");
            string[] keys = { "profile", "heroId", "settlementId", "otherHeroId", "amount" };
            if (a.Count != keys.Length || a.Properties().Any(p => !keys.Contains(p.Name) || p.Value.Type != JTokenType.String || ((string)p.Value).Length > 128))
                throw new InvalidOperationException("Supply the five bounded ruler fixture fields.");
            string profile = a.Value<string>("profile"), heroId = a.Value<string>("heroId"), settlementId = a.Value<string>("settlementId");
            if (!new[] { "ruler", "garrison", "army", "party", "snapshot", "captivity", "ownership", "loyalty", "wound", "capture_roll", "judgment_draw", "prison_guard", "ransom_offer", "barter_release", "barter_transfer", "mortality", "news" }.Contains(profile)) throw new InvalidOperationException("Unsupported ruler fixture profile.");
            if (!int.TryParse(a.Value<string>("amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 0 || amount > 4000)
                throw new InvalidOperationException("Ruler fixture amount must be between zero and 4000.");
            if (PrisonerFixture(profile)) ValidatePrisonerFixtureInput(profile, amount, heroId, a.Value<string>("otherHeroId"));
            if (profile == "judgment_draw") ValidateJudgmentDrawInput(amount, heroId, a.Value<string>("otherHeroId"));
            if (profile == "mortality" && (amount > 1 || heroId != "main_hero" || a.Value<string>("otherHeroId") != ""))
                throw new InvalidOperationException("Mortality probes require the main hero, empty other hero and amount zero or one.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(profile + "\n" + JsonConvert.SerializeObject(new System.Collections.Generic.SortedDictionary<string,string>(a.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal))))).Replace("-", "").ToLowerInvariant();
            var retained = ReignServices.Agreement.NativeOperation(operation);
            if (retained != null) { if (retained.Value<string>("requestHash") != hash) throw new InvalidOperationException("Ruler fixture operation arguments changed."); return retained; }
            Hero hero = HeroById(heroId), other = HeroById(a.Value<string>("otherHeroId")); var settlement = Holding(settlementId);
            if (profile == "news" && (amount > 7 || other?.IsAlive != true || other.IsChild || hero == other))
                throw new InvalidOperationException("News samples require distinct living adults and amount zero through seven.");
            if (hero?.IsAlive != true || hero.IsChild || hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.SiegeEvent != null || settlement?.IsFortification != true || settlement.IsUnderSiege)
                throw new InvalidOperationException("Prepare an adult, living, idle hero and a safe fortification.");
            if (profile == "capture_roll" && rulers._captureControls.ContainsKey(hero.StringId)) throw new InvalidOperationException("Consume the existing one-use capture control before preparing another.");
            if (profile == "judgment_draw")
            {
                if (rulers._judgmentControls.ContainsKey(hero.StringId)) throw new InvalidOperationException("Consume the existing one-use judgment draw before preparing another.");
                var custody = other?.PartyBelongedTo?.Party ?? settlement.Party;
                if (!Sovereign(hero) || hero.IsPrisoner || !Sovereign(other) || other == Hero.MainHero || other.IsPrisoner
                    || other.IsChild || other.Clan.Kingdom == hero.Clan.Kingdom || other.PartyBelongedTo?.MapEvent != null
                    || other.PartyBelongedTo?.SiegeEvent != null || custody.MapFaction != other.Clan.Kingdom)
                    throw new InvalidOperationException("Prepare two free opposing sovereigns and safe native custody for the AI deciding ruler.");
            }
            if (profile == "snapshot" && amount != 0) throw new InvalidOperationException("Snapshot requires amount zero.");
            if ((profile == "army" || profile == "party") && (hero.IsPrisoner || hero.PartyBelongedTo?.LeaderHero != hero))
                throw new InvalidOperationException("Roster setup requires the free leader of an existing party.");
            if ((profile == "army" || profile == "party") && amount < hero.PartyBelongedTo.MemberRoster.GetTroopRoster().Where(r => r.Character.IsHero).Sum(r => r.Number-r.WoundedNumber))
                throw new InvalidOperationException("Requested army is smaller than its preserved healthy hero roster.");
            if (PrisonerFixture(profile)) ValidatePrisonerFixture(profile, hero, other, amount);
            var receipt = new JObject { ["schema"] = "reign-ruler-fixture-v1", ["operationId"] = operation, ["requestHash"] = hash, ["status"] = "started", ["invocations"] = 1,
                ["profile"] = profile, ["before"] = ObserveNative(hero, settlement), ["semanticVerification"] = "fixture_only" };
            ReignServices.Agreement.SaveNativeOperation(operation, receipt);
            try
            {
                rulers.ApplyingNative = true;
                switch (profile)
                {
                    case "snapshot": break;
                    case "news":
                        var news = ReignServices.News ?? throw new InvalidOperationException("Town-crier news is unavailable.");
                        receipt["newsBefore"] = news.Diagnostics();
                        news.PreparePresentationFixture(amount, hero, other, operation);
                        receipt["newsAfter"] = news.Diagnostics();
                        receipt["presentationOnly"] = true; break;
                    case "judgment_draw":
                        rulers._judgmentControls[hero.StringId] = new JObject { ["draw"] = amount/4000d, ["operationId"] = operation,
                            ["prisonerId"] = hero.StringId, ["victorId"] = other.StringId, ["enrollment"] = enrollment.DeepClone() };
                        receipt["selectedDraw"] = amount/4000d; receipt["oneShot"] = true;
                        receipt["prisonerId"] = hero.StringId; receipt["victorId"] = other.StringId; break;
                    case "mortality":
                        // The real setter must pass through the ordinary protection hook.
                        rulers.ApplyingNative = false;
                        receipt["lifeDeathBefore"] = CampaignOptions.IsLifeDeathCycleDisabled;
                        CampaignOptions.IsLifeDeathCycleDisabled = amount == 1;
                        receipt["lifeDeathAfter"] = CampaignOptions.IsLifeDeathCycleDisabled;
                        receipt["nativeSetter"] = "CampaignOptions.IsLifeDeathCycleDisabled"; break;
                    case "prison_guard":
                        receipt["guardBefore"] = ObservePrisonGuard(hero, other);
                        PreparePrisonGuard(hero);
                        receipt["guardAfter"] = ObservePrisonGuard(hero, other); break;
                    case "ransom_offer": case "barter_release": case "barter_transfer":
                        // Exercise the ordinary protection hooks, including before-payment guards.
                        rulers.ApplyingNative = false;
                        RunPrisonerPaymentFixture(profile, hero, other, amount, receipt); break;
                    case "party": ResizeFixtureParty(hero.PartyBelongedTo, hero, amount); break;
                    case "ruler":
                        if (hero.Clan?.Kingdom == null || hero.Clan.Leader != hero) throw new InvalidOperationException("Ruler fixture requires an existing noble clan leader.");
                        ChangeRulingClanAction.Apply(hero.Clan.Kingdom, hero.Clan); break;
                    case "garrison":
                        var garrison = settlement.Town.GarrisonParty ?? throw new InvalidOperationException("This holding has no native garrison party.");
                        foreach (var row in garrison.MemberRoster.GetTroopRoster().Where(r => !r.Character.IsHero).ToList()) garrison.MemberRoster.RemoveTroop(row.Character, row.Number);
                        garrison.MemberRoster.AddToCounts(settlement.Culture.BasicTroop, amount); break;
                    case "army":
                        var party = hero.PartyBelongedTo ?? throw new InvalidOperationException("Army fixture requires an existing lord party.");
                        if (!Sovereign(hero) || settlement.OwnerClan?.Kingdom != hero.Clan.Kingdom || party.Army != null) throw new InvalidOperationException("Prepare a ruler's independent party and own holding.");
                        if (party.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(party);
                        party.SetPositionAfterMapChange(settlement.GatePosition); EnterSettlementAction.ApplyForParty(party, settlement);
                        ResizeFixtureParty(party, hero, amount);
                        new Army(hero.Clan.Kingdom, party, Army.ArmyTypes.Besieger); break;
                    case "captivity":
                        if (hero.IsPrisoner || other?.Clan?.Kingdom == null || other.Clan.Kingdom == hero.Clan?.Kingdom) throw new InvalidOperationException("Captivity fixture requires free enemies.");
                        var custody = other.PartyBelongedTo?.Party ?? settlement.Party;
                        if (custody.MapFaction != other.MapFaction) throw new InvalidOperationException("Captor faction must match native custody.");
                        if (!FactionManager.IsAtWarAgainstFaction(other.MapFaction, hero.MapFaction)) DeclareWarAction.ApplyByDefault(other.MapFaction, hero.MapFaction);
                        rulers.ApplyingNative = false; TakePrisonerAction.Apply(custody, hero); break;
                    case "ownership": ChangeOwnerOfSettlementAction.ApplyByGift(settlement, hero); break;
                    case "loyalty": if (amount > 100) throw new InvalidOperationException("Loyalty must be between zero and 100."); settlement.Town.Loyalty = amount; break;
                    case "capture_roll":
                        if (!Sovereign(hero)) throw new InvalidOperationException("Capture control requires a current ruler.");
                        rulers._captureControls[hero.StringId] = new JObject { ["roll"] = amount/4000d, ["operationId"] = operation, ["enrollment"] = enrollment.DeepClone() };
                        receipt["selectedRoll"] = amount/4000d; receipt["oneShot"] = true; break;
                    case "wound": hero.HitPoints = amount == 0 ? hero.MaxHitPoints : 1; break;
                }
                receipt["status"] = profile == "snapshot" ? "observed" : "prepared"; receipt["after"] = ObserveNative(hero, settlement);
            }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["error"] = ex.ToString(); }
            finally { rulers.ApplyingNative = false; ReignServices.Agreement.SaveNativeOperation(operation, receipt); }
            return receipt;
        }
        private static void ResizeFixtureParty(MobileParty party, Hero hero, int healthy)
        {
            int heroes = party.MemberRoster.GetTroopRoster().Where(r => r.Character.IsHero).Sum(r => r.Number-r.WoundedNumber);
            if (healthy < heroes) throw new InvalidOperationException("Preserve all healthy heroes in a roster fixture.");
            foreach (var row in party.MemberRoster.GetTroopRoster().Where(r => !r.Character.IsHero).ToList()) party.MemberRoster.RemoveTroop(row.Character, row.Number);
            party.MemberRoster.AddToCounts(hero.Culture.BasicTroop, healthy-heroes);
        }
        private static JObject ObserveLoyaltyModel(Town town)
        {
            if (town == null || NativeCampaign.Current?.Models?.SettlementLoyaltyModel == null)
                return new JObject { ["available"] = false };
            var model = NativeCampaign.Current.Models.SettlementLoyaltyModel;
            var number = model.CalculateLoyaltyChange(town, true); var lines = number.GetLines().ToList();
            return new JObject { ["available"] = true, ["modelType"] = model.GetType().FullName,
                // Preserve the observation field for existing evidence consumers.
                ["governorDifferentCultureEffect"] = 0f,
                ["result"] = number.ResultNumber, ["lineCount"] = lines.Count, ["truncated"] = lines.Count > 64,
                ["lines"] = new JArray(lines.Take(64).Select(line => new JObject {
                    ["name"] = line.name == null ? "" : line.name.Substring(0, Math.Min(256, line.name.Length)), ["number"] = line.number })) };
        }
        internal static JObject ObserveNative(Hero hero, Settlement settlement) => new JObject {
            ["heroId"] = hero?.StringId, ["alive"] = hero?.IsAlive, ["clanId"] = hero?.Clan?.StringId, ["ruler"] = Sovereign(hero),
            ["prisoner"] = hero?.IsPrisoner, ["custodyFaction"] = hero?.PartyBelongedToAsPrisoner?.MapFaction?.StringId,
            ["partyId"] = hero?.PartyBelongedTo?.StringId, ["presentHealthy"] = ArmyHealthy(hero?.PartyBelongedTo),
            ["partyHealthy"] = hero?.PartyBelongedTo?.Party.NumberOfHealthyMembers, ["partyTotal"] = hero?.PartyBelongedTo?.Party.NumberOfAllMembers,
            ["armyLeaderPartyId"] = hero?.PartyBelongedTo?.Army?.LeaderParty?.StringId,
            ["wounded"] = hero?.IsWounded, ["fugitive"] = hero?.IsFugitive, ["hitPoints"] = hero?.HitPoints,
            ["settlementId"] = hero?.CurrentSettlement?.StringId, ["observedHolding"] = settlement?.StringId,
            ["ownerClanId"] = settlement?.OwnerClan?.StringId, ["loyalty"] = settlement?.Town?.Loyalty,
            ["security"] = settlement?.Town?.Security, ["prosperity"] = settlement?.Town?.Prosperity,
            ["cultureId"] = settlement?.Culture?.StringId, ["ownerCultureId"] = settlement?.OwnerClan?.Culture?.StringId,
            ["governorId"] = settlement?.Town?.Governor?.StringId, ["governorCultureId"] = settlement?.Town?.Governor?.Culture?.StringId,
            ["loyaltyModel"] = ObserveLoyaltyModel(settlement?.Town),
            ["loyaltyChange"] = settlement?.Town?.LoyaltyChange, ["securityChange"] = settlement?.Town?.SecurityChange,
            ["prosperityChange"] = settlement?.Town?.ProsperityChange,
            ["garrisonStrength"] = settlement?.Town?.GarrisonParty?.Party.CalculateCurrentStrength(), ["worldDay"] = Day,
            ["nativeHooks"] = ReignRulerNativePatches.HookCount, ["nativeHookError"] = ReignRulerNativePatches.Error,
            ["news"] = ReignServices.News?.Diagnostics()
        };
    }
}
