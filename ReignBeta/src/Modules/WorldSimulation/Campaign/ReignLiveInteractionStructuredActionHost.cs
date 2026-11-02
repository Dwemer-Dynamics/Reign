using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static Task<LiveCommandResult> StructuredActionTestAsync(JObject command) => ReignMainThread.InvokeAsync(() =>
        {
            JObject enrollment = command["enrollment"] as JObject;
            string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
            string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
            if (!_armed || enrollment == null || enrollment.Value<string>("source") != "reign_mcp_structured_actions"
                || string.IsNullOrWhiteSpace(enrollment.Value<string>("campaignTestRunId")) || expected.Length == 0 || baseline.Length == 0
                || expected == baseline || expected != ActiveSaveName()
                || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || enrollment.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                || command.Value<string>("gameInstanceId") != _gameInstanceId
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                return LiveCommandResult.Failed("Structured action tests require the exact paused, armed, verified run-owned Current campaign and timeline. A save-name prefix is not authorization.");
            try
            {
                var c = command["testCase"] as JObject ?? throw new InvalidOperationException("Missing structured test case.");
                if (c.ToString(Formatting.None).Length > 65536) throw new InvalidOperationException("Structured case exceeds the input bound.");
                string phase = c.Value<string>("phase") ?? "", kind = c.Value<string>("kind") ?? "";
                string id = c.Value<string>("operationId") ?? "";
                if (new[] { "run", "preview", "status", "fixture" }.Contains(phase)
                    && (id.Length < 1 || id.Length > 80 || id.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')))
                    throw new InvalidOperationException("Supply a stable operation ID.");
                foreach (string field in new[] { "heroIds", "itemIds" })
                    if (c[field] != null && (!(c[field] is JArray list) || list.Count > 16 || list.Any(x => x.Type != JTokenType.String)))
                        throw new InvalidOperationException("Snapshot IDs must be bounded exact string lists.");
                var evidence = new JObject { ["schema"] = "reign-structured-action-evidence-v1", ["caseId"] = c.Value<string>("caseId"),
                    ["phase"] = phase, ["operationId"] = id, ["origin"] = "operator", ["enrollment"] = enrollment.DeepClone(),
                    ["semanticVerification"] = "pending", ["before"] = BuildStructuredActionNativeSnapshot(c) };
                JToken result;
                switch (phase)
                {
                    case "catalog":
                        result = new JObject { ["structured"] = JArray.FromObject(ReignActionCatalog.All), ["native"] = JArray.FromObject(ReignNativeActionCatalog.All) }; break;
                    case "entities":
                        int offset = c.Value<int?>("offset") ?? 0;
                        if (offset < 0 || offset > 100000 || (c.Value<string>("query") ?? "").Length > 128) throw new InvalidOperationException("Invalid search bounds.");
                        string entityKind = c.Value<string>("entityKind") ?? "";
                        string counterpartHeroId = c.Value<string>("counterpartHeroId") ?? "";
                        ActionClause entityClause = null;
                        if (entityKind == "pregnancy")
                        {
                            if (counterpartHeroId.Length == 0 || counterpartHeroId.Length > 128
                                || ReignActionService.HeroById(counterpartHeroId) == null)
                                throw new InvalidOperationException("Pregnancy discovery requires an exact current counterpartHeroId.");
                            entityClause = new ActionClause { RecipientId = counterpartHeroId };
                        }
                        else if (counterpartHeroId.Length > 0)
                            throw new InvalidOperationException("counterpartHeroId is supported only for pregnancy discovery.");
                        var rows = ReignActionEntities.Find(entityKind, c.Value<string>("ownerHeroId"), c.Value<string>("query") ?? "", clause: entityClause);
                        result = new JObject { ["offset"] = offset, ["total"] = rows.Count, ["rows"] = JArray.FromObject(rows.Skip(offset).Take(64)) }; break;
                    case "status":
                        if (kind == "native" || kind == "fixture") result = ReignAgreementBehavior.Instance.NativeOperation(id) ?? throw new InvalidOperationException("No retained native operation has that identity.");
                        else if (kind == "agreement") result = JObject.FromObject(ReignAgreementBehavior.Instance.Status(id));
                        else throw new InvalidOperationException("Choose native or agreement.");
                        break;
                    case "fixture":
                        if (kind != "fixture") throw new InvalidOperationException("Choose a fixed fixture.");
                        if (c.Value<string>("actionId") == "fixture.ship_damage") result = ApplyStructuredShipDamageFixture(c, id);
                        else if (c.Value<string>("actionId") == "fixture.siege_setup")
                        {
                            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true)
                                throw new InvalidOperationException("Siege setup requires a verified retained recovery checkpoint.");
                            result = ApplyStructuredSiegeFixture(c, id);
                        }
                        else if (c.Value<string>("actionId") == "fixture.player_settlement_encounter")
                        {
                            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true)
                                throw new InvalidOperationException("Player settlement encounter setup requires a verified retained recovery checkpoint.");
                            result = ApplyStructuredPlayerSettlementEncounterFixture(c, id);
                        }
                        else if (c.Value<string>("actionId") == "fixture.ruler_setup")
                        {
                            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true) throw new InvalidOperationException("Ruler setup requires a verified recovery checkpoint.");
                            result = (ReignRulerCampaignBehavior.Instance ?? throw new InvalidOperationException("Ruler system unavailable.")).PrepareFixture(c, id, RulerFixtureEnrollment(enrollment));
                        }
                        else if (c.Value<string>("actionId") == "fixture.follow_accepted_guide_step")
                        {
                            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true)
                                throw new InvalidOperationException("Guide following requires a verified retained recovery checkpoint.");
                            result = ApplyStructuredAcceptedGuideStepFixture(c, id);
                        }
                        else if (c.Value<string>("actionId") == "fixture.government_capital_visit"
                            || c.Value<string>("actionId") == "fixture.government_action_composer")
                        {
                            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true)
                                throw new InvalidOperationException("Government player-flow fixtures require a verified retained recovery checkpoint.");
                            result = (ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance
                                ?? throw new InvalidOperationException("Government system unavailable.")).PrepareGovernmentPlayerFlowFixture(c, id);
                        }
                        else throw new InvalidOperationException("Choose a registered fixed structured fixture.");
                        break;
                    case "preview": case "run":
                        if (kind == "native")
                        {
                            var args = (c["arguments"] as JObject ?? throw new InvalidOperationException("Missing native arguments."))
                                .Properties().ToDictionary(p => p.Name, p => p.Value.Type == JTokenType.String ? (string)p.Value : throw new InvalidOperationException("Native arguments are strings."), StringComparer.Ordinal);
                            if (args.ContainsKey("operation")) throw new InvalidOperationException("The case owns operation identity.");
                            args["operation"] = id;
                            result = ReignNativeActionConsole.Execute(c.Value<string>("actionId"), args, phase == "run");
                        }
                        else if (kind == "agreement")
                        {
                            var a = (c["agreement"] as JObject)?.ToObject<ActionAgreement>() ?? throw new InvalidOperationException("Missing typed agreement.");
                            a.Id = id; a.Origin = ActionOrigin.Console; a.State = AgreementState.Draft;
                            a.PlayerAcceptedHash = ""; a.NpcAcceptedHash = "";
                            result = phase == "run" ? JObject.FromObject(ReignActionService.RunOperator(a))
                                : new JObject { ["errors"] = JArray.FromObject(ReignActionService.Preview(a)),
                                    ["termsHash"] = a.TermsHash(), ["agreement"] = JObject.FromObject(a) };
                        }
                        else throw new InvalidOperationException("Choose native or agreement.");
                        break;
                    default: throw new InvalidOperationException("Unsupported structured test phase.");
                }
                evidence["result"] = result; evidence["after"] = BuildStructuredActionNativeSnapshot(c);
                if (kind == "agreement" && id.Length > 0)
                {
                    var journal = ReignAgreementBehavior.Instance.Agreements.FirstOrDefault(j => j.Agreement.Id == id);
                    if (journal != null) evidence["journal"] = JObject.FromObject(journal);
                }
                return LiveCommandResult.Completed("Production adapter evidence captured. Apply a case-specific native effect assertion before recording a pass.", evidence);
            }
            catch (Exception ex)
            {
                string detail = ex.ToString();
                return LiveCommandResult.Failed("Structured action case failed: " + detail.Substring(0, Math.Min(detail.Length, 4096)));
            }
        });

        private static JObject ApplyStructuredAcceptedGuideStepFixture(JObject c, string id)
        {
            var args = c["arguments"] as JObject ?? throw new InvalidOperationException("Missing accepted-guide fixture identifiers.");
            string[] fields = { "agreementId", "clauseId" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value)
                || ((string)p.Value).Length > 80 || ((string)p.Value).Any(ch => ch > 127 || !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')))
                throw new InvalidOperationException("Supply exactly the bounded agreementId and clauseId; destinations and coordinates are not accepted.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                "fixture.follow_accepted_guide_step\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(
                    args.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal)))))
                .Replace("-", "").ToLowerInvariant();
            var behavior = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("Agreement system unavailable.");
            var retained = behavior.NativeOperation(id);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash) throw new InvalidOperationException("Fixture identity already belongs to different guide terms.");
                return retained;
            }
            var journal = behavior.Agreements.FirstOrDefault(j => j.Agreement.Id == args.Value<string>("agreementId"));
            var agreement = journal?.Agreement;
            if (agreement == null || journal.OperatorAuthorized || agreement.Origin != ActionOrigin.Conversation
                || agreement.PlayerId != Hero.MainHero?.StringId || !agreement.HasBothConsents
                || agreement.State != AgreementState.Waiting || journal.CommittedHash != agreement.TermsHash())
                throw new InvalidOperationException("An exact mutually accepted ordinary-chat guide must still be awaiting native fulfillment.");
            var clause = agreement.Clauses.SingleOrDefault(t => t.Id == args.Value<string>("clauseId"));
            var step = journal.Steps.SingleOrDefault(s => s.ClauseId == clause?.Id && s.Installment == 0);
            if (clause?.ActionId != "show_the_way" || clause.ActorId != agreement.NpcId || clause.RecipientId != agreement.PlayerId
                || step == null || !step.Started || step.Completed || step.FulfilledInstallments != 0)
                throw new InvalidOperationException("Choose the exact unfulfilled accepted guide clause.");
            var world = JObject.Parse(step.Snapshot ?? "{}")["worldAction"] as JObject;
            if (world == null || world.Value<string>("Source") != "structured_agreement"
                || world.Value<string>("NegotiationId") != agreement.Id || world.Value<string>("TermsHash") != journal.CommittedHash
                || world.Value<string>("ActorHeroStringId") != clause.ActorId || world.Value<string>("TargetHeroStringId") != agreement.PlayerId
                || world.Value<int?>("TypeValue") != (int)ReignWorldActionType.RegularShowTheWay)
                throw new InvalidOperationException("The accepted clause has no matching native guide identity.");
            var scene = Mission.Current?.GetMissionBehavior<ReignSceneActionMissionBehavior>()
                ?? throw new InvalidOperationException("An actual active native guide scene is required.");
            string worldId = world.Value<string>("ActionId");
            var before = scene.StepPlayerToAcceptedGuideForTest(worldId, clause.ActorId, false);
            var receipt = new JObject { ["schema"] = "reign-accepted-guide-player-step-fixture-v1",
                ["operationId"] = id, ["actionId"] = "fixture.follow_accepted_guide_step", ["requestHash"] = hash,
                ["origin"] = "operator_fixture", ["agreementId"] = agreement.Id, ["clauseId"] = clause.Id,
                ["worldActionId"] = worldId, ["status"] = "started", ["invocations"] = 1, ["before"] = before };
            behavior.SaveNativeOperation(id, receipt);
            try { receipt["after"] = scene.StepPlayerToAcceptedGuideForTest(worldId, clause.ActorId, true); receipt["status"] = "returned"; }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["error"] = ex.GetType().Name + ": " + ex.Message; }
            behavior.SaveNativeOperation(id, receipt);
            return receipt;
        }

        private static JObject ApplyStructuredPlayerSettlementEncounterFixture(JObject c, string id)
        {
            var args = c["arguments"] as JObject ?? throw new InvalidOperationException("Missing player encounter fixture arguments.");
            string[] fields = { "settlementId", "expectedOwnerClanId", "expectedBesiegerPartyId" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value)
                || ((string)p.Value).Length > 80))
                throw new InvalidOperationException("Supply exactly three bounded player encounter fixture identifiers.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                "fixture.player_settlement_encounter\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(
                    args.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal)))))
                .Replace("-", "").ToLowerInvariant();
            var behavior = ReignAgreementBehavior.Instance;
            var retained = behavior.NativeOperation(id);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash)
                    throw new InvalidOperationException("Fixture operation identity already has different arguments.");
                return retained;
            }
            var party = MobileParty.MainParty;
            var settlement = Settlement.Find(args.Value<string>("settlementId"));
            var siege = settlement?.SiegeEvent;
            var besieger = siege?.BesiegerCamp?.LeaderParty;
            if (party == null || !party.IsActive || party.IsDisbanding || party.LeaderHero != Hero.MainHero
                || Hero.MainHero.IsDead || Hero.MainHero.IsPrisoner || Hero.MainHero.IsChild
                || party.Army != null || party.MapEvent != null || party.SiegeEvent != null
                || party.IsCurrentlyAtSea || party.IsTransitionInProgress || Mission.Current != null
                || (PlayerEncounter.Current != null && PlayerEncounter.EncounterSettlement == null))
                throw new InvalidOperationException("Player encounter setup requires a free active independent land player outside missions and combat.");
            if (settlement == null || !settlement.IsTown || settlement.Party.MapEvent != null
                || settlement.OwnerClan?.StringId != args.Value<string>("expectedOwnerClanId")
                || siege == null || besieger == null || !besieger.IsActive || besieger.LeaderHero == null
                || besieger.LeaderHero.IsDead || besieger == party
                || besieger.StringId != args.Value<string>("expectedBesiegerPartyId")
                || besieger.MapEvent != null || !ReferenceEquals(besieger.SiegeEvent, siege)
                || FactionManager.IsAtWarAgainstFaction(party.MapFaction, settlement.MapFaction)
                || !FactionManager.IsAtWarAgainstFaction(party.MapFaction, besieger.MapFaction))
                throw new InvalidOperationException("Player encounter setup requires the exact friendly besieged town, owner and hostile besieger without a battle.");
            var receipt = new JObject { ["schema"] = "reign-player-settlement-encounter-fixture-v1", ["operationId"] = id,
                ["actionId"] = "fixture.player_settlement_encounter", ["requestHash"] = hash, ["origin"] = "operator_fixture",
                ["status"] = "started", ["invocations"] = 1, ["campaignDay"] = CampaignTime.Now.ToDays,
                ["settlementId"] = settlement.StringId, ["ownerClanId"] = settlement.OwnerClan.StringId,
                ["besiegerPartyId"] = besieger.StringId, ["before"] = ObserveStructuredPlayerSettlementEncounter(),
                ["semanticVerification"] = "fixture_only" };
            behavior.SaveNativeOperation(id, receipt);
            try
            {
                if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                else if (party.CurrentSettlement != null) TaleWorlds.CampaignSystem.Actions.LeaveSettlementAction.ApplyForParty(party);
                party.SetPositionAfterMapChange(settlement.GatePosition);
                party.SetMoveModeHold();
                EncounterManager.StartSettlementEncounter(party, settlement);
                if (PlayerEncounter.Current == null || PlayerEncounter.EncounterSettlement != settlement)
                    throw new InvalidOperationException("Native settlement encounter did not initialize for the requested target.");
                if (party.CurrentSettlement != settlement || PlayerEncounter.LocationEncounter == null)
                    PlayerEncounter.EnterSettlement();
                GameMenu.ActivateGameMenu("town");
                TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                bool prepared = party.CurrentSettlement == settlement && PlayerEncounter.EncounterSettlement == settlement
                    && PlayerEncounter.LocationEncounter != null && party.MapEvent == null
                    && ReferenceEquals(settlement.SiegeEvent, siege) && ReferenceEquals(besieger.SiegeEvent, siege);
                receipt["status"] = prepared ? "prepared" : "uncertain";
                if (!prepared) receipt["error"] = "Native player encounter or siege membership did not match the requested fixture.";
                receipt["after"] = ObserveStructuredPlayerSettlementEncounter();
                receipt["settlementAfter"] = ReignNativeActionConsole.ObserveCombat(settlement.Party);
            }
            catch (Exception ex)
            {
                receipt["status"] = "uncertain";
                foreach (var fault in ReignNativeActionConsole.ObserveExceptionFault(ex).Properties()) receipt[fault.Name] = fault.Value;
            }
            finally
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current != null)
                    TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            }
            behavior.SaveNativeOperation(id, receipt);
            return receipt;
        }

        private static JObject ObserveStructuredPlayerSettlementEncounter() => new JObject
        {
            ["partyId"] = MobileParty.MainParty?.StringId,
            ["currentSettlementId"] = MobileParty.MainParty?.CurrentSettlement?.StringId,
            ["encounterSettlementId"] = PlayerEncounter.EncounterSettlement?.StringId,
            ["encounterActive"] = PlayerEncounter.Current != null,
            ["locationEncounterReady"] = PlayerEncounter.LocationEncounter != null,
            ["position"] = MobileParty.MainParty?.Position.ToString(),
            ["combat"] = ReignNativeActionConsole.ObserveCombat(MobileParty.MainParty?.Party)
        };

        private static JObject ApplyStructuredSiegeFixture(JObject c, string id)
        {
            var args = c["arguments"] as JObject ?? throw new InvalidOperationException("Missing siege fixture arguments.");
            string[] fields = { "attackerPartyId", "settlementId", "expectedOwnerClanId", "expectedAttackerFactionId" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value)
                || ((string)p.Value).Length > 80))
                throw new InvalidOperationException("Supply exactly four bounded siege fixture identifiers.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                "fixture.siege_setup\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(
                    args.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal)))))
                .Replace("-", "").ToLowerInvariant();
            var behavior = ReignAgreementBehavior.Instance;
            var retained = behavior.NativeOperation(id);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash)
                    throw new InvalidOperationException("Fixture operation identity already has different arguments.");
                return retained;
            }
            var attacker = MobileParty.All.SingleOrDefault(p => p.StringId == args.Value<string>("attackerPartyId"));
            var settlement = Settlement.Find(args.Value<string>("settlementId"));
            if (attacker == null || attacker == MobileParty.MainParty || !attacker.IsActive
                || attacker.LeaderHero == null || !attacker.LeaderHero.IsLord || attacker.LeaderHero.IsDead
                || attacker.LeaderHero.IsChild || attacker.LeaderHero.IsPrisoner || attacker.Army != null
                || attacker.CurrentSettlement != null || attacker.MapEvent != null || attacker.SiegeEvent != null
                || attacker.IsCurrentlyAtSea || attacker.IsTransitionInProgress
                || attacker.MapFaction?.StringId != args.Value<string>("expectedAttackerFactionId"))
                throw new InvalidOperationException("Siege setup requires the exact free, active, independent land NPC lord party and faction.");
            if (settlement == null || !settlement.IsFortification || settlement.IsUnderSiege
                || settlement.SiegeEvent != null || settlement.Party.MapEvent != null
                || settlement.OwnerClan?.StringId != args.Value<string>("expectedOwnerClanId")
                || !FactionManager.IsAtWarAgainstFaction(attacker.MapFaction, settlement.MapFaction)
                || MobileParty.MainParty?.MapEvent != null || MobileParty.MainParty?.CurrentSettlement == settlement)
                throw new InvalidOperationException("Siege setup requires the exact idle hostile fortification and owner, outside player combat.");
            var receipt = new JObject { ["schema"] = "reign-siege-setup-fixture-v1", ["operationId"] = id,
                ["actionId"] = "fixture.siege_setup", ["requestHash"] = hash, ["origin"] = "operator_fixture",
                ["status"] = "started", ["invocations"] = 1, ["campaignDay"] = CampaignTime.Now.ToDays,
                ["attackerPartyId"] = attacker.StringId, ["settlementId"] = settlement.StringId,
                ["ownerClanId"] = settlement.OwnerClan.StringId, ["beforePosition"] = attacker.Position.ToString(),
                ["before"] = ReignNativeActionConsole.ObserveCombat(attacker.Party),
                ["semanticVerification"] = "fixture_only" };
            behavior.SaveNativeOperation(id, receipt);
            try
            {
                attacker.Position = settlement.GatePosition;
                var siege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager.StartSiegeEvent(settlement, attacker);
                bool prepared = siege != null && ReferenceEquals(settlement.SiegeEvent, siege)
                    && ReferenceEquals(attacker.SiegeEvent, siege) && siege.BesiegerCamp?.LeaderParty == attacker;
                receipt["status"] = prepared ? "prepared" : "uncertain";
                if (!prepared) receipt["error"] = "Native siege membership did not match the requested fixture.";
                receipt["afterPosition"] = attacker.Position.ToString();
                receipt["after"] = ReignNativeActionConsole.ObserveCombat(attacker.Party);
                receipt["settlementAfter"] = ReignNativeActionConsole.ObserveCombat(settlement.Party);
            }
            catch (Exception ex)
            {
                receipt["status"] = "uncertain";
                foreach (var fault in ReignNativeActionConsole.ObserveExceptionFault(ex).Properties())
                    receipt[fault.Name] = fault.Value;
            }
            behavior.SaveNativeOperation(id, receipt);
            return receipt;
        }

        // This fixture is separate from the action catalog and never represents NPC consent.
        private static JObject ApplyStructuredShipDamageFixture(JObject c, string id)
        {
            var args = c["arguments"] as JObject ?? throw new InvalidOperationException("Missing fixture arguments.");
            string[] fields = { "shipId", "ownerPartyId", "expectedHitPoints", "remainingRatio" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || ((string)p.Value).Length < 1 || ((string)p.Value).Length > 128))
                throw new InvalidOperationException("Supply exactly four bounded ship fixture arguments.");
            float expected = float.Parse(args.Value<string>("expectedHitPoints"), NumberStyles.Float, CultureInfo.InvariantCulture);
            float ratio = float.Parse(args.Value<string>("remainingRatio"), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (float.IsNaN(expected) || float.IsInfinity(expected) || expected <= 0f
                || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0.05f || ratio > 0.75f)
                throw new InvalidOperationException("Fixture hull values must be finite; remainingRatio is 0.05 through 0.75.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                "fixture.ship_damage\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(
                    args.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal)))))
                .Replace("-", "").ToLowerInvariant();
            var behavior = ReignAgreementBehavior.Instance;
            var retained = behavior.NativeOperation(id);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash) throw new InvalidOperationException("Fixture operation identity already has different arguments.");
                return retained;
            }
            var owners = MobileParty.All.Where(p => p.Ships.Any(s => ReignAssetTransfer.ShipId(s) == args.Value<string>("shipId"))).ToList();
            if (owners.Count != 1 || owners[0].StringId != args.Value<string>("ownerPartyId"))
                throw new InvalidOperationException("The exact ship must have one verified owner party.");
            var party = owners[0];
            var ship = party.Ships.Single(s => ReignAssetTransfer.ShipId(s) == args.Value<string>("shipId"));
            if (!party.IsActive || party.MapEvent != null || party.SiegeEvent != null || party.IsTransitionInProgress || ship.IsUsedByQuest)
                throw new InvalidOperationException("Damage fixtures require an active, safe party and a ship free of quest duties.");
            float before = ship.HitPoints, maximum = ship.MaxHitPoints, damaged = maximum * ratio;
            if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0f || before != expected || damaged <= 0f || damaged >= before)
                throw new InvalidOperationException("The observed hull must match exactly and the fixture must cause genuine positive damage.");
            var receipt = new JObject { ["schema"] = "reign-ship-damage-fixture-v1", ["operationId"] = id,
                ["actionId"] = "fixture.ship_damage", ["requestHash"] = hash, ["origin"] = "operator_fixture",
                ["campaignDay"] = CampaignTime.Now.ToDays, ["shipId"] = args["shipId"].DeepClone(),
                ["ownerPartyId"] = party.StringId, ["maxHitPoints"] = maximum, ["beforeHitPoints"] = before,
                ["requestedHitPoints"] = damaged, ["status"] = "started", ["invocations"] = 1,
                ["semanticVerification"] = "fixture_only" };
            behavior.SaveNativeOperation(id, receipt);
            try { ship.HitPoints = damaged; receipt["afterHitPoints"] = ship.HitPoints; receipt["status"] = "returned"; }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["error"] = ex.Message; }
            behavior.SaveNativeOperation(id, receipt);
            return receipt;
        }

        private static JObject RulerFixtureEnrollment(JObject enrollment)
        { var copy = (JObject)enrollment.DeepClone(); copy["nativeGameInstanceId"] = _gameInstanceId; return copy; }
        internal static bool MatchesRulerFixture(JObject enrollment) => _armed && enrollment != null
            && enrollment.Value<string>("source") == "reign_mcp_structured_actions"
            && enrollment.Value<bool?>("recoveryCheckpointVerified") == true
            && enrollment.Value<string>("nativeGameInstanceId") == _gameInstanceId
            && enrollment.Value<string>("disposableSaveName") != enrollment.Value<string>("protectedBaselineSaveName")
            && ActiveSaveName() == enrollment.Value<string>("disposableSaveName")
            && ReignCampaignIdentity.CurrentCampaignId() == enrollment.Value<string>("campaignId")
            && (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") == enrollment.Value<string>("timelineId")
            && ReferenceEquals(_observedCampaign, TaleWorlds.CampaignSystem.Campaign.Current);

        private static JObject BuildStructuredActionNativeSnapshot(JObject testCase)
        {
            var snapshot = BuildGauntletNativeSnapshot(testCase);
            snapshot["rulers"] = ReignRulerCampaignBehavior.Instance?.Diagnostics();
            var rows = new JArray();
            foreach (string heroId in (testCase["heroIds"] as JArray ?? new JArray()).Values<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(16))
            {
                var context = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.BuildConversationContext(ReignActionService.HeroById(heroId));
                if (context?.Value<bool?>("enabled") != true) continue;
                var row = new JObject();
                foreach (string key in new[] { "schema", "heroId", "agreementId", "agreementRevision", "phase", "termKind",
                    "startedDay", "reviewDueDay", "returnDueDay", "returnSettlementId", "inMainParty", "observedWorldDay",
                    "wageGold", "wagePeriodDays", "wageArrearsGold", "reviewActionStatus" })
                    if (context[key] != null) row[key] = context[key].DeepClone();
                rows.Add(row);
            }
            snapshot["temporaryPartyGuests"] = new JObject { ["schema"] = "reign-structured-guest-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 16, ["rows"] = rows };
            var ids = new HashSet<string>((testCase["heroIds"] as JArray ?? new JArray()).Values<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(16), StringComparer.Ordinal);
            snapshot["nativeMission"] = BuildStructuredMissionObservation(ids);
            snapshot["rebellionState"] = BuildStructuredRebellionObservation(ids);
            var orders = (ReignCampaignCommandBehavior.Instance?.Orders ?? new ReignCampaignOrderRecord[0])
                .Where(o => o != null && (ids.Contains(o.CommanderHeroStringId) || ids.Contains(o.IssuerHeroStringId)))
                .OrderBy(o => o.OrderId, StringComparer.Ordinal).ToList();
            var orderRows = new JArray();
            foreach (var o in orders.Take(64))
            {
                var party = ReignActionService.HeroById(o.CommanderHeroStringId)?.PartyBelongedTo;
                orderRows.Add(new JObject { ["orderId"] = o.OrderId, ["commanderHeroId"] = o.CommanderHeroStringId,
                    ["issuerHeroId"] = o.IssuerHeroStringId, ["objective"] = o.Objective, ["status"] = o.Status,
                    ["stage"] = o.Stage, ["authorityMode"] = o.AuthorityMode, ["targetSettlementId"] = o.TargetSettlementStringId,
                    ["targetPartyId"] = o.TargetPartyStringId, ["currentStepIndex"] = o.CurrentStepIndex,
                    ["completedStepCount"] = o.CompletedStepCount, ["stepCount"] = o.Steps?.Count ?? 0,
                    ["deadlineDay"] = o.DeadlineDay, ["nextReviewDay"] = o.NextReviewDay, ["lastReviewDay"] = o.LastReviewDay,
                    ["nativeReassertions"] = o.NativeReassertions, ["awaitingGuidance"] = o.AwaitingGuidance,
                    ["actualPartyId"] = party?.StringId, ["actualLeaderHeroId"] = party?.LeaderHero?.StringId,
                    ["actualSettlementId"] = party?.CurrentSettlement?.StringId, ["actualBehavior"] = party?.DefaultBehavior.ToString(),
                    ["actualTargetSettlementId"] = party?.TargetSettlement?.StringId, ["actualTargetPartyId"] = party?.TargetParty?.StringId,
                    ["actualPosition"] = party?.Position.ToString() });
            }
            snapshot["campaignOrders"] = new JObject { ["schema"] = "reign-structured-order-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = orders.Count,
                ["truncated"] = orders.Count > 64, ["rows"] = orderRows };
            var kingdoms = new HashSet<string>(ids.Select(id => ReignActionService.HeroById(id)?.Clan?.Kingdom?.StringId)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
            var treaties = (ReignAICampaignBehavior.Instance?.Agreements ?? new ReignDiplomaticAgreementRecord[0])
                .Where(a => a != null && kingdoms.Contains(a.ActorKingdomStringId) && kingdoms.Contains(a.TargetKingdomStringId))
                .OrderBy(a => a.AgreementId, StringComparer.Ordinal).ToList();
            var treatyRows = new JArray();
            foreach (var a in treaties.Take(64)) treatyRows.Add(new JObject { ["agreementId"] = a.AgreementId,
                ["kind"] = a.Kind, ["actorKingdomId"] = a.ActorKingdomStringId, ["targetKingdomId"] = a.TargetKingdomStringId,
                ["actorClanId"] = a.ActorClanStringId, ["targetClanId"] = a.TargetClanStringId,
                ["targetHeroId"] = a.TargetHeroStringId, ["targetSettlementId"] = a.TargetSettlementStringId,
                ["createdDay"] = a.CreatedDay, ["expireDay"] = a.ExpireDay, ["isActive"] = a.IsActive,
                ["isPublic"] = a.IsPublic, ["endedDay"] = a.EndedDay, ["breakerKingdomId"] = a.BreakerKingdomStringId });
            snapshot["diplomaticAgreements"] = new JObject { ["schema"] = "reign-structured-treaty-observation-v1",
                ["requestedKingdomPairsOnly"] = true, ["maximumRows"] = 64, ["total"] = treaties.Count,
                ["truncated"] = treaties.Count > 64, ["rows"] = treatyRows };
            var nativeAlliance = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var nativeTrade = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var requestedRealms = Kingdom.All.Where(k => k != null && kingdoms.Contains(k.StringId))
                .OrderBy(k => k.StringId, StringComparer.Ordinal).ToList();
            var nativePairs = new JArray();
            int nativePairTotal = requestedRealms.Count * (requestedRealms.Count - 1) / 2;
            for (int i = 0; i < requestedRealms.Count; i++)
                for (int n = i + 1; n < requestedRealms.Count && nativePairs.Count < 64; n++)
                {
                    var first = requestedRealms[i]; var second = requestedRealms[n];
                    bool allied = nativeAlliance?.IsAllyWithKingdom(first, second) == true;
                    TradeAgreementsCampaignBehavior.TradeAgreement existingTrade;
                    bool trading = nativeTrade != null && nativeTrade.HasTradeAgreement(first, second, out existingTrade);
                    nativePairs.Add(new JObject { ["firstKingdomId"] = first.StringId, ["secondKingdomId"] = second.StringId,
                        ["allianceAvailable"] = nativeAlliance != null, ["tradeAvailable"] = nativeTrade != null,
                        ["allied"] = allied, ["trading"] = trading,
                        ["allianceEndDay"] = allied ? (JToken)new JValue(nativeAlliance.GetAllianceEndDate(first, second).ToDays) : JValue.CreateNull(),
                        ["tradeEndDay"] = trading ? (JToken)new JValue(nativeTrade.GetTradeAgreementEndDate(first, second).ToDays) : JValue.CreateNull() });
                }
            snapshot["nativeDiplomaticRelations"] = new JObject { ["schema"] = "reign-structured-native-diplomacy-observation-v1",
                ["requestedKingdomPairsOnly"] = true, ["maximumRows"] = 64, ["total"] = nativePairTotal,
                ["truncated"] = nativePairTotal > 64, ["rows"] = nativePairs };
#if !REIGN_EXCLUDE_COURT
            var court = ReignBeta.Court.ReignCourtCampaignBehavior.Instance;
            var officeRows = new JArray();
            var offices = (court?.Offices ?? new ReignBeta.Court.CourtOfficeAssignment[0])
                .Where(o => o != null && ids.Contains(o.HeroStringId)).OrderBy(o => o.AssignmentId, StringComparer.Ordinal).ToList();
            foreach (var o in offices.Take(64)) officeRows.Add(new JObject { ["assignmentId"] = o.AssignmentId,
                ["office"] = o.Office.ToString(), ["heroId"] = o.HeroStringId, ["isActive"] = o.IsActive,
                ["assignedDay"] = o.AssignedDay, ["dismissedDay"] = o.DismissedDay,
                ["revision"] = o.Revision, ["lastCommandId"] = o.LastCommandId });
            var regents = (court?.Regents ?? new ReignBeta.Court.CourtRegentAssignment[0])
                .Where(r => r != null && ids.Contains(r.HeroStringId)).OrderBy(r => r.AssignmentId, StringComparer.Ordinal).ToList();
            var regentRows = new JArray();
            foreach (var r in regents.Take(64)) regentRows.Add(new JObject { ["assignmentId"] = r.AssignmentId,
                ["heroId"] = r.HeroStringId, ["isActive"] = r.IsActive, ["assignedDay"] = r.AssignedDay,
                ["dismissedDay"] = r.DismissedDay, ["revision"] = r.Revision, ["lastCommandId"] = r.LastCommandId });
            var ambassadors = (court?.Ambassadors ?? new ReignBeta.Court.AmbassadorPosting[0])
                .Where(a => a != null && ids.Contains(a.HeroStringId)).OrderBy(a => a.PostingId, StringComparer.Ordinal).ToList();
            var ambassadorRows = new JArray();
            foreach (var a in ambassadors.Take(64)) ambassadorRows.Add(new JObject { ["postingId"] = a.PostingId,
                ["heroId"] = a.HeroStringId, ["targetKingdomId"] = a.TargetKingdomStringId, ["missionType"] = a.MissionType,
                ["status"] = a.Status, ["assignedDay"] = a.AssignedDay, ["arrivalDay"] = a.ArrivalDay,
                ["lastReportDay"] = a.LastReportDay, ["recalledDay"] = a.RecalledDay,
                ["revision"] = a.Revision, ["lastCommandId"] = a.LastCommandId });
            var chancellor = court?.Chancellor;
            bool includeChancellor = chancellor != null && ids.Contains(chancellor.HeroId);
            var chancellorRow = includeChancellor ? new JObject { ["termId"] = chancellor.TermId,
                ["heroId"] = chancellor.HeroId, ["state"] = chancellor.State.ToString(), ["salary"] = chancellor.Salary,
                ["desiredActive"] = chancellor.DesiredActive, ["effectiveDay"] = chancellor.DesiredActiveEffectiveDay,
                ["appointedDay"] = chancellor.AppointedDay, ["lastPaidDay"] = chancellor.LastPaidDay,
                ["totalPaidGold"] = chancellor.TotalPaidGold, ["paidActiveDays"] = chancellor.PaidActiveDays,
                ["failedPaymentDays"] = chancellor.FailedPaymentDays, ["emergency"] = chancellor.Emergency,
                ["suppressesPetitions"] = chancellor.SuppressesPetitions } : null;
            var serviceHistory = (court?.RulerDocketState?.History ?? new List<ReignBeta.Court.ReignDocketHistoryRecord>())
                .Where(h => h != null && h.Type == "chancellor_service" && ids.Contains(h.ChancellorHeroId))
                .OrderByDescending(h => h.Day).ThenBy(h => h.RecordId, StringComparer.Ordinal).ToList();
            var historyRows = new JArray();
            foreach (var h in serviceHistory.Take(64)) historyRows.Add(new JObject { ["recordId"] = h.RecordId,
                ["heroId"] = h.ChancellorHeroId, ["type"] = h.Type, ["outcome"] = h.Outcome, ["day"] = h.Day });
            snapshot["courtState"] = new JObject { ["schema"] = "reign-structured-court-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRowsPerCollection"] = 64,
                ["available"] = court != null, ["offices"] = officeRows, ["officeTotal"] = offices.Count,
                ["officesTruncated"] = offices.Count > 64, ["regents"] = regentRows, ["regentTotal"] = regents.Count,
                ["regentsTruncated"] = regents.Count > 64, ["ambassadors"] = ambassadorRows,
                ["ambassadorTotal"] = ambassadors.Count, ["ambassadorsTruncated"] = ambassadors.Count > 64,
                ["chancellor"] = chancellorRow, ["chancellorHistory"] = historyRows,
                ["chancellorHistoryTotal"] = serviceHistory.Count, ["chancellorHistoryTruncated"] = serviceHistory.Count > 64 };
            var missions = (court?.SpymasterState?.Missions ?? new List<ReignBeta.Court.ReignSpymasterMission>())
                .Where(m => m != null && ids.Contains(m.SpymasterHeroStringId))
                .OrderBy(m => m.MissionId, StringComparer.Ordinal).ToList();
            var missionRows = new JArray();
            foreach (var m in missions.Take(64)) missionRows.Add(new JObject { ["missionId"] = m.MissionId,
                ["spymasterHeroId"] = m.SpymasterHeroStringId, ["missionType"] = m.MissionType,
                ["targetType"] = m.TargetType, ["targetId"] = m.TargetStringId,
                ["sponsorKingdomId"] = m.SponsorKingdomStringId, ["goldCost"] = m.GoldCost,
                ["startedDay"] = m.StartedDay, ["dueDay"] = m.DueDay, ["resolvedDay"] = m.ResolvedDay,
                ["state"] = m.State.ToString(), ["agreementEffectState"] = m.AgreementEffectState,
                ["harmful"] = m.Harmful, ["targetNoticed"] = m.TargetNoticed, ["playerIdentified"] = m.PlayerIdentified,
                ["attributionPending"] = m.AttributionPending });
            snapshot["spymasterMissions"] = new JObject { ["schema"] = "reign-structured-spymaster-observation-v1",
                ["requestedSpymasterIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = missions.Count,
                ["truncated"] = missions.Count > 64, ["rows"] = missionRows };
            var effects = (court?.SpymasterState?.Effects ?? new List<ReignBeta.Court.ReignSpymasterSettlementEffect>())
                .Where(e => e != null && ids.Contains(e.AgentHeroStringId))
                .OrderBy(e => e.EffectId, StringComparer.Ordinal).ToList();
            var effectRows = new JArray();
            foreach (var e in effects.Take(64))
            {
                var town = TaleWorlds.CampaignSystem.Settlements.Settlement.Find(e.SettlementStringId)?.Town;
                effectRows.Add(new JObject { ["effectId"] = e.EffectId, ["agentHeroId"] = e.AgentHeroStringId,
                    ["settlementId"] = e.SettlementStringId, ["effectType"] = e.EffectType,
                    ["sourceKingdomId"] = e.SourceKingdomStringId, ["startDay"] = e.StartDay,
                    ["endDay"] = e.EndDay, ["magnitude"] = e.Magnitude,
                    ["actualFoodStocks"] = town?.FoodStocks, ["actualSecurity"] = town?.Security,
                    ["actualLoyalty"] = town?.Loyalty, ["actualProsperity"] = town?.Prosperity });
            }
            snapshot["spymasterEffects"] = new JObject { ["schema"] = "reign-structured-spymaster-effects-v1",
                ["requestedAgentIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = effects.Count,
                ["truncated"] = effects.Count > 64, ["rows"] = effectRows };
#endif
            return snapshot;
        }

        private static JObject BuildStructuredRebellionObservation(HashSet<string> ids)
        {
            var behavior = ReignRebellionCampaignBehavior.Instance;
            var plots = (behavior?.Plots ?? new ReignRebellionPlotRecord[0])
                .Where(p => p != null && (ids.Contains(p.PlayerHeroStringId) || ids.Contains(p.RulerHeroStringId)
                    || ids.Contains(p.InformerHeroStringId))).OrderBy(p => p.PlotId, StringComparer.Ordinal).ToList();
            var plotIds = new HashSet<string>(plots.Select(p => p.PlotId), StringComparer.Ordinal);
            var pledges = (behavior?.Pledges ?? new ReignRebellionPledgeRecord[0])
                .Where(p => p != null && plotIds.Contains(p.PlotId) && ids.Contains(p.LordHeroStringId))
                .OrderBy(p => p.PledgeId, StringComparer.Ordinal).ToList();
            var memberships = (behavior?.Memberships ?? new ReignRebellionMembershipRecord[0])
                .Where(m => m != null && ids.Contains(m.LeaderHeroStringId))
                .OrderBy(m => m.MovementId, StringComparer.Ordinal).ThenBy(m => m.ClanStringId, StringComparer.Ordinal).ToList();
            var linkedMovements = new HashSet<string>(plots.Select(p => p.MovementId)
                .Concat(memberships.Select(m => m.MovementId)), StringComparer.Ordinal);
            var movements = (behavior?.Movements ?? new ReignRebellionMovementRecord[0])
                .Where(m => m != null && (ids.Contains(m.LeaderHeroStringId) || ids.Contains(m.OriginalRulerHeroStringId)
                    || linkedMovements.Contains(m.MovementId)))
                .OrderBy(m => m.MovementId, StringComparer.Ordinal).ToList();
            var summonses = (behavior?.Summonses ?? new ReignRebellionSummonsRecord[0])
                .Where(s => s != null && plotIds.Contains(s.PlotId) && (ids.Contains(s.PlayerHeroStringId)
                    || ids.Contains(s.RulerHeroStringId) || ids.Contains(s.InformerHeroStringId)))
                .OrderBy(s => s.SummonsId, StringComparer.Ordinal).ToList();
            var plotRows = new JArray();
            foreach (var p in plots.Take(64)) plotRows.Add(new JObject {
                ["plotId"] = p.PlotId, ["playerHeroId"] = p.PlayerHeroStringId, ["playerClanId"] = p.PlayerClanStringId,
                ["parentKingdomId"] = p.ParentKingdomStringId, ["rulerHeroId"] = p.RulerHeroStringId,
                ["status"] = p.Status, ["revision"] = p.Revision, ["createdDay"] = p.CreatedDay,
                ["updatedDay"] = p.UpdatedDay, ["informerHeroId"] = p.InformerHeroStringId,
                ["reportDispatchDay"] = p.ReportDispatchDay, ["reportDeliveryDay"] = p.ReportDeliveryDay,
                ["summonsId"] = p.SummonsId, ["movementId"] = p.MovementId, ["pledgesActivated"] = p.PledgesActivated,
                ["resolution"] = p.Resolution, ["rulerVerdict"] = p.RulerVerdict });
            var pledgeRows = new JArray();
            foreach (var p in pledges.Take(64)) pledgeRows.Add(new JObject {
                ["pledgeId"] = p.PledgeId, ["plotId"] = p.PlotId, ["lordHeroId"] = p.LordHeroStringId,
                ["clanId"] = p.ClanStringId, ["originalKingdomId"] = p.OriginalKingdomStringId,
                ["decision"] = p.Decision, ["requestChannel"] = p.RequestChannel, ["actionId"] = p.ActionId,
                ["requestedDay"] = p.RequestedDay, ["respondedDay"] = p.RespondedDay,
                ["reportedToRuler"] = p.ReportedToRuler, ["activated"] = p.Activated,
                ["activatedDay"] = p.ActivatedDay, ["movementId"] = p.MovementId });
            var membershipRows = new JArray();
            foreach (var m in memberships.Take(64))
            {
                var clan = ReignObjectResolver.FindClan(m.ClanStringId);
                membershipRows.Add(new JObject { ["movementId"] = m.MovementId, ["clanId"] = m.ClanStringId,
                    ["leaderHeroId"] = m.LeaderHeroStringId, ["side"] = m.Side, ["isCommitted"] = m.IsCommitted,
                    ["originalKingdomId"] = m.OriginalKingdomStringId, ["joinedDay"] = m.JoinedDay,
                    ["switchCount"] = m.SwitchCount, ["lastSwitchDay"] = m.LastSwitchDay,
                    ["fateApplied"] = m.FateApplied, ["fateOutcome"] = m.FateOutcome, ["isDefeated"] = m.IsDefeated,
                    ["playerChoiceRequired"] = m.PlayerChoiceRequired, ["actualClanAvailable"] = clan != null,
                    ["actualKingdomId"] = clan?.Kingdom?.StringId, ["actualLeaderHeroId"] = clan?.Leader?.StringId,
                    ["actualClanEliminated"] = clan == null ? JValue.CreateNull() : JToken.FromObject(clan.IsEliminated) });
            }
            var movementRows = new JArray();
            foreach (var m in movements.Take(64))
            {
                var parent = ReignObjectResolver.FindKingdom(m.ParentKingdomStringId);
                var rebel = ReignObjectResolver.FindKingdom(m.RebelKingdomStringId);
                movementRows.Add(new JObject { ["movementId"] = m.MovementId,
                    ["parentKingdomId"] = m.ParentKingdomStringId, ["rebelKingdomId"] = m.RebelKingdomStringId,
                    ["leaderClanId"] = m.LeaderClanStringId, ["leaderHeroId"] = m.LeaderHeroStringId,
                    ["originalRulerHeroId"] = m.OriginalRulerHeroStringId, ["stage"] = m.Stage,
                    ["isPlayerLed"] = m.IsPlayerLed, ["civilWarStartedDay"] = m.CivilWarStartedDay,
                    ["resolutionApplied"] = m.ResolutionApplied, ["resolution"] = m.Resolution,
                    ["winningSide"] = m.WinningSide, ["winningLeaderHeroId"] = m.WinningLeaderHeroStringId,
                    ["cooldownUntilDay"] = m.CooldownUntilDay, ["judgmentsPending"] = m.JudgmentsPending,
                    ["backingStatus"] = m.BackingStatus, ["backingSponsorKingdomId"] = m.BackingSponsorKingdomStringId,
                    ["backingActionId"] = m.BackingActionId, ["actualParentAvailable"] = parent != null,
                    ["actualRebelAvailable"] = rebel != null, ["actualParentLeaderHeroId"] = parent?.Leader?.StringId,
                    ["actualRebelLeaderHeroId"] = rebel?.Leader?.StringId, ["actualRebelRulingClanId"] = rebel?.RulingClan?.StringId,
                    ["actualRebelEliminated"] = rebel == null ? JValue.CreateNull() : JToken.FromObject(rebel.IsEliminated),
                    ["actualAtWar"] = parent == null || rebel == null ? JValue.CreateNull()
                        : JToken.FromObject(FactionManager.IsAtWarAgainstFaction(parent, rebel)) });
            }
            var summonsRows = new JArray();
            foreach (var s in summonses.Take(64)) summonsRows.Add(new JObject {
                ["summonsId"] = s.SummonsId, ["plotId"] = s.PlotId, ["playerHeroId"] = s.PlayerHeroStringId,
                ["rulerHeroId"] = s.RulerHeroStringId, ["informerHeroId"] = s.InformerHeroStringId,
                ["status"] = s.Status, ["issuedDay"] = s.IssuedDay, ["deliveryDay"] = s.DeliveryDay,
                ["deadlineDay"] = s.DeadlineDay, ["answeredDay"] = s.AnsweredDay, ["playerResponse"] = s.PlayerResponse,
                ["rulerVerdict"] = s.RulerVerdict, ["consequenceApplied"] = s.ConsequenceApplied, ["consequence"] = s.Consequence });
            return new JObject { ["schema"] = "reign-structured-rebellion-observation-v1",
                ["available"] = behavior != null, ["requestedHeroIdsOnly"] = true, ["maximumRowsPerCollection"] = 64,
                ["plots"] = new JObject { ["total"] = plots.Count, ["truncated"] = plots.Count > 64, ["rows"] = plotRows },
                ["pledges"] = new JObject { ["total"] = pledges.Count, ["truncated"] = pledges.Count > 64, ["rows"] = pledgeRows },
                ["memberships"] = new JObject { ["total"] = memberships.Count, ["truncated"] = memberships.Count > 64, ["rows"] = membershipRows },
                ["movements"] = new JObject { ["total"] = movements.Count, ["truncated"] = movements.Count > 64, ["rows"] = movementRows },
                ["summonses"] = new JObject { ["total"] = summonses.Count, ["truncated"] = summonses.Count > 64, ["rows"] = summonsRows } };
        }

        private static JObject BuildStructuredMissionObservation(HashSet<string> ids)
        {
            var mission = Mission.Current;
            var agents = ReadStructuredMissionField(null, "agent-selection", () => mission == null ? new List<Agent>() : mission.AllAgents
                .Where(a => a?.Character != null && ids.Contains(a.Character.StringId))
                .OrderBy(a => a.Index).ToList());
            var playerTeam = ReadStructuredMissionField(null, "player-team", () => mission?.PlayerTeam);
            bool playerTeamValid = ReadStructuredMissionField(null, "player-team-valid", () => playerTeam?.IsValid == true);
            var agentRows = new JArray();
            foreach (var agent in agents.Take(32))
            {
                // AllAgents retains cleared wrappers whose managed identity still
                // exists after Clear zeros the native state/controller/position
                // pointers. Keep those rows, but never dereference their pointers.
                bool nativeStateAvailable = ReadStructuredMissionField(agent, "native-state-availability",
                    () => agent.Mission == mission && agent.HasBeenBuilt);
                var equipment = nativeStateAvailable ? ReadStructuredMissionField(agent, "equipment", () => agent.Equipment) : null;
                var team = nativeStateAvailable ? ReadStructuredMissionField(agent, "team", () => agent.Team) : null;
                bool teamValid = nativeStateAvailable && ReadStructuredMissionField(agent, "team-valid", () => team?.IsValid == true);
                bool? hasMeleeWeapon = equipment == null ? (bool?)null : false;
                var weapons = new JArray();
                // Empty native MissionWeapon structs need not have initialized
                // usage caches. Inspect public item metadata without asking the
                // native equipment cache to traverse those empty structs.
                for (int slot = 0; equipment != null && slot < 5; slot++)
                {
                    var item = ReadStructuredMissionField(agent, "equipment-item-" + slot,
                        () => equipment[(EquipmentIndex)slot].Item);
                    if (ReadStructuredMissionField(agent, "equipment-usages-" + slot,
                        () => item?.Weapons?.Any(usage => usage != null && usage.IsMeleeWeapon) == true))
                        hasMeleeWeapon = true;
                    // The fifth slot participates in the melee observation, as
                    // it does natively; retain the existing four-slot output bound.
                    if (slot < 4 && !string.IsNullOrWhiteSpace(item?.StringId))
                        weapons.Add(new JObject { ["slot"] = slot, ["itemId"] = item.StringId });
                }
                agentRows.Add(new JObject { ["heroId"] = ReadStructuredMissionField(agent, "hero-id", () => agent.Character.StringId),
                    ["agentIndex"] = agent.Index, ["isMainAgent"] = agent == mission.MainAgent,
                    ["nativeStateAvailable"] = nativeStateAvailable,
                    ["active"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "active", () => agent.IsActive())) : JValue.CreateNull(),
                    ["state"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "state", () => agent.State.ToString())) : JValue.CreateNull(),
                    ["controller"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "controller", () => agent.Controller.ToString())) : JValue.CreateNull(),
                    ["aiControlled"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "ai-controlled", () => agent.IsAIControlled)) : JValue.CreateNull(),
                    ["watchState"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "watch-state", () => agent.CurrentWatchState.ToString())) : JValue.CreateNull(),
                    ["aiState"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "ai-state", () => agent.AIStateFlags.ToString())) : JValue.CreateNull(),
                    ["mortality"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "mortality", () => agent.CurrentMortalityState.ToString())) : JValue.CreateNull(),
                    ["health"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "health", () => agent.Health)) : JValue.CreateNull(),
                    ["healthLimit"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, "health-limit", () => agent.HealthLimit)) : JValue.CreateNull(),
                    ["equipmentAvailable"] = equipment != null,
                    // Missing equipment remains unknown, rather than unarmed.
                    ["hasMeleeWeapon"] = hasMeleeWeapon.HasValue
                        ? new JValue(hasMeleeWeapon.Value) : JValue.CreateNull(),
                    ["teamIndex"] = nativeStateAvailable && team != null ? new JValue(ReadStructuredMissionField(agent, "team-index", () => team.TeamIndex)) : JValue.CreateNull(),
                    ["teamSide"] = nativeStateAvailable && team != null ? new JValue(ReadStructuredMissionField(agent, "team-side", () => team.Side.ToString())) : JValue.CreateNull(),
                    // Civilian agents can retain Team.Invalid: its native handle
                    // has index -1 and no mission. Hostility is unknown there.
                    ["teamValid"] = teamValid,
                    ["enemyOfPlayer"] = teamValid && playerTeamValid
                        ? (JToken)new JValue(ReadStructuredMissionField(agent, "enemy-of-player", () => team.IsEnemyOf(playerTeam))) : JValue.CreateNull(),
                    ["position"] = nativeStateAvailable ? (JToken)ReadStructuredMissionField(agent, "position",
                        () => new JObject { ["x"] = agent.Position.x, ["y"] = agent.Position.y, ["z"] = agent.Position.z }) : JValue.CreateNull(),
                    ["weapons"] = weapons });
            }
            var removals = mission?.GetMissionBehavior<ReignArenaDuelEquipmentBehavior>()?.Removals;
            var removalRows = new JArray();
            if (removals != null)
                foreach (var removal in removals.Where(r => ids.Contains(r.HeroId)).Take(2))
                    removalRows.Add(new JObject { ["heroId"] = removal.HeroId, ["agentIndex"] = removal.AgentIndex,
                        ["state"] = removal.State, ["health"] = removal.Health,
                        ["affectorHeroId"] = ids.Contains(removal.AffectorHeroId) ? removal.AffectorHeroId : string.Empty });
            return new JObject { ["schema"] = "reign-structured-mission-observation-v1",
                ["available"] = mission != null, ["mode"] = mission?.Mode.ToString(),
                ["mainAgentIndex"] = mission?.MainAgent?.Index, ["playerTeamIndex"] = mission?.PlayerTeam?.TeamIndex,
                ["playerTeamValid"] = playerTeamValid,
                ["nativeArenaDuelControllerPresent"] = mission?.MissionBehaviors.Any(b =>
                    b.GetType().FullName == "SandBox.Missions.MissionLogics.Arena.ArenaDuelMissionController") == true,
                ["duelAgentRemovals"] = removalRows,
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 32, ["total"] = agents.Count,
                ["truncated"] = agents.Count > 32, ["rows"] = agentRows };
        }

        private static T ReadStructuredMissionField<T>(Agent agent, string field, Func<T> observe)
        {
            try { return observe(); }
            catch (Exception ex)
            {
                // Preserve a failed observation, identifying its exact read without
                // inventing a default, dropping the actor, or rereading a bad field.
                throw new InvalidOperationException("Native mission observation field '" + field
                    + "' failed for agent " + (agent == null ? "none" : agent.Index.ToString(CultureInfo.InvariantCulture)) + ".", ex);
            }
        }
    }
}
