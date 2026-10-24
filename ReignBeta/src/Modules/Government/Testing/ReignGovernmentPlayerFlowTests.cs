using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Actions;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        // Called only behind the structured host's exact paused Current/campaign/timeline/
        // process-instance and retained recovery checkpoint gates. Never a player cheat API.
        internal JObject PrepareGovernmentPlayerFlowFixture(JObject testCase, string operationId)
        {
            string action = testCase.Value<string>("actionId");
            bool capitalVisit = action == "fixture.government_capital_visit";
            if (!capitalVisit && action != "fixture.government_action_composer")
                throw new InvalidOperationException("Unknown government player-flow fixture.");
            var args = testCase["arguments"] as JObject ?? throw new InvalidOperationException("Missing fixture identifiers.");
            string[] fields = capitalVisit
                ? new[] { "expectedKingdomId", "expectedCapitalSettlementId", "expectedOwnerClanId" }
                : new[] { "expectedHeroId", "businessId" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value) || ((string)p.Value).Length > 80))
                throw new InvalidOperationException("Supply exactly the bounded government fixture identifiers.");
            string hash = GovernmentFingerprintDigest(new JObject { ["action"] = action,
                ["arguments"] = new JObject(args.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new JProperty(p.Name, p.Value.DeepClone()))) });
            var journal = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("Action journal unavailable.");
            var retained = journal.NativeOperation(operationId);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash) throw new InvalidOperationException("Operation identity already has different fixture terms.");
                return retained; // Including uncertain/started: never retry a partial relocation.
            }

            var party = MobileParty.MainParty;
            var hero = Hero.MainHero;
            var kingdom = Clan.PlayerClan?.Kingdom;
            if (party == null || hero == null || kingdom == null || !party.IsActive || party.IsDisbanding
                || party.LeaderHero != hero || hero.IsDead || hero.IsPrisoner || hero.IsChild
                || party.Army != null || party.MapEvent != null || party.SiegeEvent != null
                || party.IsCurrentlyAtSea || party.IsTransitionInProgress || Mission.Current != null
                || TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode != CampaignTimeControlMode.Stop)
                throw new InvalidOperationException("Government fixture requires a free adult independent land player, paused outside missions and combat.");

            Settlement target = null;
            if (capitalVisit)
            {
                target = GovernmentCapital(kingdom);
                var origin = party.CurrentSettlement;
                if (kingdom.StringId != args.Value<string>("expectedKingdomId") || target == null
                    || target.StringId != args.Value<string>("expectedCapitalSettlementId")
                    || target.OwnerClan?.StringId != args.Value<string>("expectedOwnerClanId")
                    || !target.IsTown || target.IsUnderSiege || target.Party.MapEvent != null
                    || target.OwnerClan?.Kingdom != kingdom || FactionManager.IsAtWarAgainstFaction(party.MapFaction, target.MapFaction)
                    || origin == null || !origin.IsTown || origin.IsUnderSiege || origin.Party.MapEvent != null
                    || FactionManager.IsAtWarAgainstFaction(party.MapFaction, origin.MapFaction)
                    || (PlayerEncounter.Current != null && PlayerEncounter.EncounterSettlement != origin)
                    || ReignIndividualChatScreenManager.IsOpen || ReignActionBuilder.IsOpen)
                    throw new InvalidOperationException("Capital visit requires the exact designated friendly safe capital and an existing safe friendly town encounter without chat.");
            }
            else
            {
                var business = _business.FirstOrDefault(x => x.BusinessId == args.Value<string>("businessId"));
                var npc = ReignIndividualChatScreenManager.ActiveHero;
                if (!ReignIndividualChatScreenManager.IsOpen || ReignIndividualChatScreenManager.AutomationCalibrationFixture
                    || ReignIndividualChatScreenManager.AutomationBusy || ReignActionBuilder.IsOpen
                    || ReignIndividualChatScreenManager.ActiveViewModel?.ActionBusy != false
                    || npc == null || npc == hero || npc.IsDead || npc.IsChild || npc.IsPrisoner
                    || npc.StringId != args.Value<string>("expectedHeroId")
                    || business == null || GovernmentBusinessRules.IsClosed(business.Status)
                    || business.KingdomStringId != kingdom.StringId
                    || !GetSeats(kingdom.StringId).Any(x => x.HeroStringId == npc.StringId))
                    throw new InvalidOperationException("Open an idle ordinary production conversation with the exact current member and an open matter in the player's kingdom.");
            }
            JObject before = ObserveGovernmentPlayerFlowConservation();
            var receipt = new JObject { ["schema"] = "reign-government-player-flow-fixture-v1", ["actionId"] = action,
                ["operationId"] = operationId, ["requestHash"] = hash, ["status"] = "started", ["invocations"] = 1,
                ["origin"] = "operator_fixture", ["semanticVerification"] = "fixture_only",
                ["before"] = before, ["originalSettlementId"] = party.CurrentSettlement?.StringId,
                ["arguments"] = args.DeepClone(), ["cleanup"] = "Cancel the unsent builder/close chat; restore the enrolled Current if relocation must be undone. Never overwrite the baseline." };
            journal.SaveNativeOperation(operationId, receipt);
            try
            {
                bool prepared;
                if (capitalVisit)
                {
                    if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                    else if (party.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(party);
                    party.SetPositionAfterMapChange(target.GatePosition);
                    party.SetMoveModeHold();
                    EncounterManager.StartSettlementEncounter(party, target);
                    if (PlayerEncounter.Current == null || PlayerEncounter.EncounterSettlement != target)
                        throw new InvalidOperationException("Native capital encounter did not initialize.");
                    if (party.CurrentSettlement != target || PlayerEncounter.LocationEncounter == null) PlayerEncounter.EnterSettlement();
                    GameMenu.ActivateGameMenu("town");
                    TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                    prepared = party.CurrentSettlement == target && PlayerEncounter.EncounterSettlement == target
                        && PlayerEncounter.LocationEncounter != null && party.MapEvent == null && !target.IsUnderSiege;
                }
                else prepared = ReignActionBuilder.TryOpenForHost(ScreenManager.TopScreen, ReignIndividualChatScreenManager.ActiveViewModel)
                    && ReignActionBuilder.IsOpen && !ReignActionBuilder.IsCalibrationFixture;
                JObject after = ObserveGovernmentPlayerFlowConservation();
                receipt["after"] = after;
                receipt["conserved"] = JToken.DeepEquals(before, after);
                receipt["actualSettlementId"] = party.CurrentSettlement?.StringId;
                receipt["status"] = prepared && JToken.DeepEquals(before, after) ? "prepared" : "uncertain";
            }
            catch (Exception ex)
            {
                receipt["status"] = "uncertain";
                receipt["error"] = ex.Message;
                receipt["after"] = ObserveGovernmentPlayerFlowConservation();
            }
            journal.SaveNativeOperation(operationId, receipt);
            return receipt;
        }

        private JObject ObserveGovernmentPlayerFlowConservation()
        {
            var party = MobileParty.MainParty;
            return new JObject {
                ["campaignDay"] = CampaignTime.Now.ToDays,
                ["timeMode"] = TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode.ToString(),
                ["governmentFingerprint"] = GovernmentFeatureFingerprint(),
                ["walletDigest"] = GovernmentFingerprintDigest(new JArray(Hero.AllAliveHeroes.OrderBy(x => x.StringId, StringComparer.Ordinal)
                    .Select(x => new JObject { ["heroId"] = x.StringId, ["gold"] = x.Gold }))),
                ["playerInfluence"] = Clan.PlayerClan.Influence,
                ["membersDigest"] = GovernmentFingerprintDigest(JArray.FromObject(party.MemberRoster.GetTroopRoster()
                    .Select(x => new { id = x.Character.StringId, number = x.Number, wounded = x.WoundedNumber, xp = x.Xp }))),
                ["captivesDigest"] = GovernmentFingerprintDigest(JArray.FromObject(party.PrisonRoster.GetTroopRoster()
                    .Select(x => new { id = x.Character.StringId, number = x.Number, wounded = x.WoundedNumber, xp = x.Xp }))),
                ["itemsDigest"] = GovernmentFingerprintDigest(JArray.FromObject(party.ItemRoster.Select(x => new {
                    id = x.EquipmentElement.Item?.StringId, modifier = x.EquipmentElement.ItemModifier?.StringId, number = x.Amount }))),
                ["chatTextDigest"] = GovernmentFingerprintDigest(new JValue(ReignIndividualChatScreenManager.AutomationInputText)),
                ["chatDraftDigest"] = GovernmentFingerprintDigest(new JValue(JsonConvert.SerializeObject(
                    ReignIndividualChatScreenManager.ActiveViewModel?.ActionDraft)))
            };
        }
    }
}
