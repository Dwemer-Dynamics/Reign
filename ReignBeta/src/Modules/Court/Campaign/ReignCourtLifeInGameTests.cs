using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.UI;
using ReignBeta.UI.Calibration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Court.Campaign.ReignCourtCampaignBehavior;
using ReignBeta.Court.UI;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using TaleWorlds.Core;

namespace ReignBeta.Court.Campaign
{
    internal static partial class ReignCourtTestControls
    {
        private const string PaidSupplyAcceptanceSchema = "reign-court-life-paid-supply-v1";

        internal sealed class PaidSupplyAcceptance
        {
            internal ReignSupplyTransferRequest Request;
            internal CourtSession Session;
            internal long SessionRevision;
            internal Func<bool> OwnsNativeLeg;
            internal Func<bool> OwnsExpectedResult;
            internal Func<bool> MarkInvoked;
            internal JObject Marker;
            internal JObject Before;
            internal JObject SavedReceipt;
            internal bool Verify;
        }

        internal static JObject PreparePaidSupplyAcceptance(ReignCourtCampaignBehavior court, string runId, string phase,
            JObject grant, string saveName, string instance, Func<bool> ownsLoad, out PaidSupplyAcceptance transfer)
        {
            transfer = null;
            var session = court.Session;
            string campaign = ReignCampaignIdentity.CurrentCampaignId();
            string timeline = ReignServices.WorldHistory?.TimelineId;
            string point = ReignServices.SaveSync?.CurrentSavePointId;
            string owner = grant.Value<string>("campaignTestRunId");
            string checkpoint = grant.Value<string>("checkpointAttemptId");
            JObject loadOwner = (JObject)grant["owner"];
            string processHash = ReignBeta.WorldSimulation.Campaign.ReignRelationshipNativePullWire.LoadIdentitySha256(
                loadOwner.Value<string>("clientProcessInstanceId"));
            int processId = loadOwner.Value<int>("nativeProcessId");
            var source = court.CurrentCapital;
            var payer = Hero.MainHero;
            var grain = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>("grain");
            var result = new JObject
            {
                ["schema"] = PaidSupplyAcceptanceSchema, ["runId"] = runId, ["phase"] = phase,
                ["saveName"] = saveName, ["gameInstanceId"] = instance, ["campaignId"] = campaign,
                ["timelineId"] = timeline, ["savePointId"] = point, ["campaignTestRunId"] = owner,
                ["dispatchRunId"] = grant.Value<string>("runId"),
                ["sessionId"] = session?.SessionId, ["sessionRevisionBefore"] = session?.Revision,
                ["worldDay"] = CurrentCourtLifeDay(), ["ok"] = false,
                ["nativeProcessId"] = processId, ["clientProcessInstanceSha256"] = processHash
            };
            if (!ownsLoad() || !PaidSupplyScopeCurrent(court, result) || string.IsNullOrEmpty(processHash)
                || !court.HasRoyalCommandAccess || !court.ServerAvailable || !court.ServerSessionOpened
                || session?.Authority != ReignCourtAuthority.Royal || session.State != ReignCourtSessionState.Sitting
                || session.CampaignId != campaign || session.TimelineId != timeline
                || source?.IsTown != true || source.IsUnderSiege || source.ItemRoster == null
                || Clan.PlayerClan == null || source.OwnerClan != Clan.PlayerClan || source.StringId != session.HostSettlementStringId
                || payer == null || payer.IsDead || grain == null)
                return PaidSupplyFailure(result, "An aligned, paused disposable Royal Court with a player-owned capital and native grain is required.");

            long initialRevision = session.Revision;
            var stock = new EquipmentElement(grain);
            JObject marker = FindRulerDocketMarker(court, runId);
            bool verify = phase == "court_life_paid_supply_verify";
            if (verify && (marker?.Value<string>("schema") != PaidSupplyAcceptanceSchema || marker.Value<string>("state") != "verified"
                || marker.Value<string>("campaignTestRunId") != owner || marker.Value<string>("campaignId") != campaign
                || marker.Value<string>("timelineId") != timeline || marker.Value<string>("saveName") != saveName
                || string.IsNullOrEmpty(marker.Value<string>("gameInstanceId")) || marker.Value<string>("gameInstanceId") == instance
                || !Guid.TryParseExact(marker.Value<string>("checkpointAttemptId"), "N", out _)
                || marker.Value<string>("checkpointAttemptId") == checkpoint
                || !Guid.TryParseExact(marker.Value<string>("savePointId"), "N", out _) || marker.Value<string>("savePointId") == point
                || !ReignBeta.WorldSimulation.Campaign.ReignRelationshipNativePullWire.IsPaidCourtIdentifier(marker.Value<string>("dispatchRunId"))
                || marker.Value<string>("clientProcessInstanceSha256")?.Length != 64
                || marker.Value<string>("clientProcessInstanceSha256") == processHash))
                return PaidSupplyFailure(result, "Verification requires the post-transfer registered checkpoint in a different native process and game instance.");

            string targetId = grant.Value<string>("targetSettlementId");
            if (verify && targetId != marker["terms"]?.Value<string>("TargetSettlementStringId"))
                return PaidSupplyFailure(result, "Verification must name the original saved target.");
            var candidates = Settlement.All.Where(target => target != source && target.IsFortification && !target.IsUnderSiege
                    && target.OwnerClan == Clan.PlayerClan && target.SettlementComponent != null
                    && target.ItemRoster != null && !ReferenceEquals(target.ItemRoster, source.ItemRoster)
                    && (verify || (PaidSupplyStackCount(target, stock) < int.MaxValue
                        && target.SettlementComponent.Gold >= 0 && target.SettlementComponent.Gold < int.MaxValue)))
                .OrderBy(target => target.StringId, StringComparer.Ordinal);
            if (phase == "court_life_paid_supply_preflight")
            {
                result["sourceSettlementId"] = source.StringId;
                result["itemId"] = grain.StringId;
                result["itemAmount"] = 1;
                result["goldAmount"] = 1;
                result["payerHeroId"] = payer.StringId;
                result["sourceStock"] = PaidSupplyStackCount(source, stock);
                result["payerGold"] = payer.Gold;
                result["sessionRevisionAfter"] = session.Revision;
                result["eligibleTargets"] = new JArray(candidates.Take(32)
                    .Select(target => new JObject { ["settlementId"] = target.StringId,
                        ["stock"] = PaidSupplyStackCount(target, stock), ["gold"] = target.SettlementComponent.Gold }));
                result["ok"] = true;
                return result;
            }

            var selected = (verify ? candidates : candidates.Take(32)).FirstOrDefault(target => target.StringId == targetId);
            if (selected == null)
                return PaidSupplyFailure(result, "Use an exact eligible player-clan target from paid supply preflight.");

            var clan = Clan.PlayerClan;
            var sourceOwner = source.OwnerClan?.Leader;
            var targetOwner = selected.OwnerClan?.Leader;
            var sourceRoster = source.ItemRoster;
            var targetRoster = selected.ItemRoster;
            var targetWallet = selected.SettlementComponent;
            double day = CurrentCourtLifeDay();
            if (!verify && initialRevision == long.MaxValue)
                return PaidSupplyFailure(result, "The exact Court revision cannot advance for the paid fixture.");
            long expectedResultRevision = verify ? initialRevision : initialRevision + 1;
            bool OwnsNativeState(long revision) => ownsLoad() && PaidSupplyScopeCurrent(court, result) && CurrentCourtLifeDay() == day
                && ReferenceEquals(court.Session, session) && session.State == ReignCourtSessionState.Sitting
                && session.Revision == revision && ReferenceEquals(court.CurrentCapital, source)
                && clan != null && ReferenceEquals(Clan.PlayerClan, clan)
                && ReferenceEquals(source.OwnerClan, clan) && ReferenceEquals(selected.OwnerClan, clan)
                && ReferenceEquals(source.OwnerClan?.Leader, sourceOwner) && ReferenceEquals(selected.OwnerClan?.Leader, targetOwner)
                && ReferenceEquals(Settlement.Find(source.StringId), source) && ReferenceEquals(Settlement.Find(selected.StringId), selected)
                && !source.IsUnderSiege && !selected.IsUnderSiege
                && ReferenceEquals(source.ItemRoster, sourceRoster) && ReferenceEquals(selected.ItemRoster, targetRoster)
                && ReferenceEquals(selected.SettlementComponent, targetWallet)
                && ReferenceEquals(Hero.MainHero, payer) && !payer.IsDead;
            bool OwnsNativeLeg() => OwnsNativeState(initialRevision);
            bool OwnsExpectedResult() => OwnsNativeState(expectedResultRevision);
            if (!OwnsNativeLeg())
                return PaidSupplyFailure(result, "The loaded Court no longer owns both exact native transfer legs.");

            string ownedCommandId = "court_paid_supply_" + CourtLifeSnapshotFingerprint(
                new JObject { ["campaign"] = campaign, ["timeline"] = timeline, ["owner"] = owner, ["fixture"] = runId });
            string commandId = verify ? marker["terms"]?.Value<string>("CommandId") : ownedCommandId;
            if (commandId != ownedCommandId)
                return PaidSupplyFailure(result, "The saved command does not belong to this exact fixture.");
            var request = new ReignSupplyTransferRequest
            {
                CommandId = commandId, SourceSettlementStringId = source.StringId, TargetSettlementStringId = selected.StringId,
                ItemStringId = grain.StringId, ItemAmount = 1, GoldAmount = 1, GoldPayerKind = "hero",
                GoldPayerStringId = payer.StringId, GoldRecipientKind = "settlement", GoldRecipientStringId = selected.StringId,
                VassalConsentGranted = false
            };
            JObject holdings = PaidSupplyHoldings(request);
            JObject terms = JObject.FromObject(request);
            JObject saved = court.ReadPaidSupplyFixtureReceipt(request);
            result["terms"] = terms;
            result["before"] = holdings;
            if (verify)
            {
                if (!JToken.DeepEquals(marker["terms"], terms) || saved?.Value<bool>("exactVerifiedReceipt") != true
                    || !JToken.DeepEquals(marker["receipt"], saved) || !JToken.DeepEquals(marker["after"], holdings))
                    return PaidSupplyFailure(result, "The exact saved receipt, original terms or restored native holdings do not match. Verification will not dispatch.");
            }
            else
            {
                if (marker != null || saved != null)
                    return PaidSupplyFailure(result, "A confirmed, unused paid supply fixture and command are required. Inspect an existing attempt before rollback.");
                if (holdings == null || holdings.Value<int>("Source") < 1 || holdings.Value<int>("Payer") < 1
                    || holdings.Value<int>("Target") == int.MaxValue || holdings.Value<int>("Recipient") == int.MaxValue
                    || holdings.Value<int>("Target") < 0 || holdings.Value<int>("Recipient") < 0)
                    return PaidSupplyFailure(result, "The exact native grain and denar legs cannot be funded.");
                marker = (JObject)result.DeepClone();
                marker["state"] = "invoked";
                marker["checkpointAttemptId"] = checkpoint;
            }

            bool MarkInvoked()
            {
                if (verify || !OwnsNativeLeg() || FindRulerDocketMarker(court, runId) != null
                    || court.ReadPaidSupplyFixtureReceipt(request) != null)
                    return false;
                StoreRulerDocketMarker(court, marker);
                return true;
            }

            transfer = new PaidSupplyAcceptance
            {
                Request = request, Session = session, SessionRevision = initialRevision, OwnsNativeLeg = OwnsNativeLeg,
                OwnsExpectedResult = OwnsExpectedResult, MarkInvoked = MarkInvoked, Marker = marker,
                Before = holdings, SavedReceipt = saved, Verify = verify
            };
            return result;
        }

        internal static void FinishPaidSupplyAcceptance(ReignCourtCampaignBehavior court, PaidSupplyAcceptance transfer,
            JObject result, string error)
        {
            bool scope = transfer.OwnsExpectedResult()
                && JToken.DeepEquals(FindRulerDocketMarker(court, result.Value<string>("runId")), transfer.Marker);
            if (!scope)
            {
                PaidSupplyFailure(result, string.IsNullOrWhiteSpace(error)
                    ? "The disposable scope changed while observing the fixture. Inspect the original attempt before retrying." : error);
                return;
            }

            JObject holdings = PaidSupplyHoldings(transfer.Request);
            JObject receipt = court.ReadPaidSupplyFixtureReceipt(transfer.Request);
            result["after"] = holdings;
            result["receipt"] = receipt;
            result["sessionRevisionAfter"] = transfer.Session.Revision;
            bool exact = receipt?.Value<bool>("exactVerifiedReceipt") == true;
            bool proof = transfer.Verify
                ? exact && JToken.DeepEquals(transfer.SavedReceipt, receipt) && JToken.DeepEquals(transfer.Before, holdings)
                    && transfer.Session.Revision == transfer.SessionRevision
                : exact && transfer.Before != null && holdings != null
                    && holdings.Value<int>("Source") == transfer.Before.Value<int>("Source") - 1
                    && holdings.Value<int>("Target") == transfer.Before.Value<int>("Target") + 1
                    && holdings.Value<int>("Payer") == transfer.Before.Value<int>("Payer") - 1
                    && holdings.Value<int>("Recipient") == transfer.Before.Value<int>("Recipient") + 1
                    && JToken.DeepEquals(receipt["observations"]?["Before"], transfer.Before)
                    && JToken.DeepEquals(receipt["observations"]?["AfterPayment"], holdings);
            result["ok"] = proof && error == string.Empty;
            result["exactReloadReplay"] = transfer.Verify && result.Value<bool>("ok");
            if (transfer.Verify)
            {
                result["originalDispatchRunId"] = transfer.Marker["dispatchRunId"];
                result["originalGameInstanceId"] = transfer.Marker["gameInstanceId"];
                result["originalNativeProcessId"] = transfer.Marker["nativeProcessId"];
                result["originalClientProcessInstanceSha256"] = transfer.Marker["clientProcessInstanceSha256"];
                result["originalSavePointId"] = transfer.Marker["savePointId"];
            }
            if (!result.Value<bool>("ok"))
                PaidSupplyFailure(result, string.IsNullOrWhiteSpace(error) ? "The exact native holdings and saved receipt did not prove the fixture." : error);
            if (!transfer.Verify)
            {
                result["state"] = result.Value<bool>("ok") ? "verified" : "unverified";
                var saved = (JObject)result.DeepClone();
                saved["checkpointAttemptId"] = transfer.Marker["checkpointAttemptId"];
                StoreRulerDocketMarker(court, saved);
            }
        }

        private static bool PaidSupplyScopeCurrent(ReignCourtCampaignBehavior court, JObject scope)
            => ReferenceEquals(ReignServices.Court, court) && !ReignCampaignInitializationGate.IsPending
                && ReignSaveSyncCoordinator.IsReadyForCampaign(scope.Value<string>("campaignId"))
                && !ReignSaveSyncCoordinator.IsAlignmentPending
                && TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop
                && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress && !ReignSaveSyncCoordinator.IsSaveFinalizationPending
                && !string.IsNullOrWhiteSpace(scope.Value<string>("savePointId"))
                && !string.IsNullOrWhiteSpace(scope.Value<string>("timelineId"))
                && !string.IsNullOrWhiteSpace(scope.Value<string>("gameInstanceId"))
                && ReignCampaignIdentity.CurrentCampaignId() == scope.Value<string>("campaignId")
                && ReignServices.WorldHistory?.TimelineId == scope.Value<string>("timelineId")
                && string.Equals(ReignServerClient.ActiveNativeSaveName().Trim(), scope.Value<string>("saveName"), StringComparison.OrdinalIgnoreCase);

        private static JObject PaidSupplyHoldings(ReignSupplyTransferRequest request)
        {
            var source = Settlement.Find(request.SourceSettlementStringId);
            var target = Settlement.Find(request.TargetSettlementStringId);
            var payer = Hero.MainHero;
            var grain = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>(request.ItemStringId);
            if (source?.ItemRoster == null || target?.ItemRoster == null || target.SettlementComponent == null
                || payer?.StringId != request.GoldPayerStringId || grain == null) return null;
            var stock = new EquipmentElement(grain);
            return JObject.FromObject(new CourtPaidSupplyBalances(PaidSupplyStackCount(source, stock),
                PaidSupplyStackCount(target, stock), payer.Gold, target.SettlementComponent.Gold));
        }

        private static int PaidSupplyStackCount(Settlement settlement, EquipmentElement stock)
        {
            var roster = settlement.ItemRoster;
            int index = roster?.FindIndexOfElement(stock) ?? -1;
            return index < 0 ? 0 : roster.GetElementNumber(index);
        }

        private static JObject PaidSupplyFailure(JObject result, string error)
        {
            result["error"] = error;
            result["ok"] = false;
            return result;
        }

        private static JObject RunCourtLifeTestPhase(ReignCourtCampaignBehavior court, string runId, string phase, JObject options, string save, string instance)
        {
            if (!new[] { "court_life_check", "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse",
                "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify" }.Contains(phase))
                return RulerDocketTestFailure(runId, phase, "Unsupported court-life evidence phase.", save, save);
            if (phase == "court_life_check") return new JObject { ["ok"] = true, ["saveName"] = save };
            if (phase == "court_life_preflight") return new JObject { ["ok"] = court.HasRoyalCommandAccess && court.CurrentCapital?.IsUnderSiege == false,
                ["schema"] = "reign-court-life-evidence-v1", ["runId"] = runId, ["phase"] = phase,
                ["saveName"] = save, ["capitalId"] = court.CurrentCapital?.StringId, ["playerGold"] = Hero.MainHero?.Gold,
                ["familyInventory"] = CourtLifeFamilyFixtureInventory(court),
                ["captiveInventory"] = CourtLifeCaptiveInventory(court),
                ["patronageEligibility"] = CourtLifePatronageEligibility(court, options.Value<string>("courtLifeTemplateId")),
                ["pendingMatterInventory"] = CourtLifePendingMatterInventory(court, options),
                ["sources"] = new JArray("International", "Family", "Patronage", "Visitor"),
                ["internationalTemplateCount"] = ReignInternationalDocketCatalog.Templates.Count,
                ["patronageTemplateCount"] = ReignPatronageCatalog.Templates.Count };
            if (phase == "court_life_family_fixture") return PrepareCourtLifeFamilyFixture(court, runId, options, save, instance);
            if (phase == "court_life_prepare") return PrepareCourtLifeFixture(court, runId, options, save, instance);
            JObject marker = FindRulerDocketMarker(court, runId);
            ReignCourtLifeMatter matter = FindCourtLifeTestMatter(court, runId);
            if (marker == null || matter == null) return RulerDocketTestFailure(runId, phase, "No exact court-life fixture is bound to this run.", save, save);
            if (phase == "court_life_snapshot")
            {
                bool saved = ReignUiCalibrationService.TrySaveSnapshot("ReignCourtPetitionScreen", out string path, out string error);
                JObject snapshot = CourtLifeEvidence(court, runId, phase, save, matter);
                snapshot["audienceArt"] = ReignCourtPetitionScreenManager.AutomationSceneEvidence;
                snapshot["ok"] = saved; snapshot["snapshotPath"] = path; snapshot["error"] = error; return snapshot;
            }
            if (phase == "court_life_open")
            {
                bool opened = ReignCourtPetitionScreenManager.TryOpenForAutomation(court, matter, out string error);
                JObject result = CourtLifeEvidence(court, runId, phase, save, matter);
                result["ok"] = opened; result["error"] = error; return result;
            }
            if (phase == "court_life_choose_confirm")
            {
                string selected = options.Value<string>("courtLifeOptionId") ?? string.Empty;
                if (!matter.Options.Any(x => x.OptionId == selected))
                    return RulerDocketTestFailure(runId, phase, "The requested exact option is absent from this production quote.", save, save);
                if (!ReignCourtPetitionScreenManager.AutomationNaturalConversationComplete)
                    return RulerDocketTestFailure(runId, phase, "A completed natural player turn is required before confirmation.", save, save);
                marker["beforeDecision"] = CourtLifeNativeState(court, matter); StoreRulerDocketMarker(court, marker);
                bool chosen = ReignCourtPetitionScreenManager.TryExecuteAutomationAction("choose", selected, out string chooseError);
                bool confirmed = chosen && ReignCourtPetitionScreenManager.TryExecuteAutomationAction("confirm", "", out chooseError);
                JObject result = CourtLifeEvidence(court, runId, phase, save, matter);
                JObject payload = ParseObject(matter.PayloadJson);
                JObject receipt = payload["nativeReceipt"] as JObject;
                JObject native = result["nativeState"] as JObject;
                JObject international = native?["international"] as JObject;
                JObject action = international?["nativeDiplomaticAction"] as JObject;
                string decisionExpectation = options.Value<string>("courtLifeExpectation") ?? "snapshot";
                bool expectedPending = decisionExpectation == "pending" && chosen && !confirmed
                    && selected == "accept" && payload.Value<string>("nativeOptionId") == selected
                    && matter.Source == ReignDocketSource.International && matter.IsPending && !matter.EffectsCommitted
                    && string.IsNullOrEmpty(matter.ResolutionReceiptId) && payload.Value<bool?>("nativeEffectCommitted") != true
                    && receipt?.Value<bool?>("Success") == true && receipt.Value<bool?>("Completed") == false
                    && action?.Value<bool?>("serviceAvailable") == true && action.Value<int?>("count") == 1
                    && action.Value<string>("status") == "Executing"
                    && native.Value<int?>("rulerGold") == marker["beforeDecision"]?.Value<int?>("rulerGold")
                    && (native["relations"] as JArray)?.Count == 0 && (native["history"] as JArray)?.Count == 0;
                result["ok"] = confirmed || expectedPending;
                result["usedProductionChooseAndConfirm"] = chosen;
                result["productionConfirmationCompleted"] = confirmed;
                result["expectedPendingDiplomaticAction"] = expectedPending;
                result["error"] = expectedPending ? string.Empty : chooseError;
                if (options.Value<string>("courtLifeExpectation") == "pending")
                {
                    result["ok"] = expectedPending;
                    result["assertions"] = new JArray(Assertion("pending_diplomacy_confirmation_has_no_settlement_effects", expectedPending));
                }
                else if (decisionExpectation == "resolved")
                {
                    var decisionAssertions = new JArray(Assertion("matter_resolved_with_receipt", chosen && confirmed
                        && matter.EffectsCommitted && matter.State == ReignCourtLifeMatterState.Resolved
                        && !string.IsNullOrWhiteSpace(matter.ResolutionReceiptId)));
                    AddCourtLifeDecisionAssertions(court, decisionAssertions, marker, matter);
                    result["assertions"] = decisionAssertions;
                    result["ok"] = decisionAssertions.All(a => a.Value<bool?>("passed") == true);
                }
                return result;
            }
            if (phase == "court_life_save_prepare")
            {
                if (court.HasPendingFamilyVisitWork || court.HasPendingInternationalCourtLifeWork || court.HasPendingCourtLifeDeliveryWork
                    || court.EnsureRulerDocketState().PendingRelationAdjustments.Any(x => !x.Applied))
                    return RulerDocketTestFailure(runId, phase, "Wait for family, international and relation delivery queues to drain before checkpoint preparation.", save, save);
                marker["courtLifeSaveFingerprint"] = CourtLifeFingerprint(court, matter); marker["courtLifeSaveInstance"] = instance;
                StoreRulerDocketMarker(court, marker);
            }
            JObject evidence = CourtLifeEvidence(court, runId, phase, save, matter);
            var assertions = new JArray { Assertion("exact_matter_bound", marker.Value<string>("courtLifeMatterId") == matter.MatterId),
                Assertion("no_hidden_statistics_in_npc_speech", matter.ConversationLines.Where(x => x.Role == "assistant")
                    .All(x => !ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(x.Text))) };
            if (phase == "court_life_save_prepare") AddCourtLifeFingerprintContractAssertions(assertions);
            if (phase == "court_life_save_verify")
            {
                assertions.Add(Assertion("different_game_instance", !string.IsNullOrWhiteSpace(marker.Value<string>("courtLifeSaveInstance")) && marker.Value<string>("courtLifeSaveInstance") != instance));
                assertions.Add(Assertion("state_fingerprint_survived_reload", marker.Value<string>("courtLifeSaveFingerprint") == CourtLifeFingerprint(court, matter)));
            }
            if (phase == "court_life_clock_observe") assertions.Add(Assertion("native_campaign_time_advanced", CurrentCourtLifeDay() > marker.Value<double>("preparedDay")));
            string expectation = options.Value<string>("courtLifeExpectation") ?? "snapshot";
            if (phase == "court_life_clock_observe") assertions.Add(Assertion("clock_observation_names_required_behavior", expectation != "snapshot"));
            int expectedTurns = options.Value<int?>("courtLifeExpectedTurns") ?? -1;
            if (expectedTurns >= 0) assertions.Add(Assertion("exact_completed_player_turns", matter.CompletedPlayerTurnIds.Count == expectedTurns));
            if (expectation == "pending") assertions.Add(Assertion("matter_pending_without_effects", matter.IsPending && !matter.EffectsCommitted));
            else if (expectation == "resolved")
            {
                assertions.Add(Assertion("matter_resolved_with_receipt", matter.EffectsCommitted && matter.State == ReignCourtLifeMatterState.Resolved && !string.IsNullOrWhiteSpace(matter.ResolutionReceiptId)));
                AddCourtLifeDecisionAssertions(court, assertions, marker, matter);
            }
            else if (expectation == "delivery_complete")
            {
                ReignPatronageCommission c = court.EnsureRulerDocketState().PatronageCommissions.FirstOrDefault(x => x.MatterId == matter.MatterId);
                assertions.Add(Assertion("commission_completed_after_due_day", c != null && c.Paid && c.Completed && CurrentCourtLifeDay() >= c.DueDay));
                if (c != null)
                {
                    int expected = c.Objective == ReignPatronageObjective.Loyalty ? c.SettlementIds.Count : c.Objective == ReignPatronageObjective.Praise ? c.HeroIds.Count : 0;
                    assertions.Add(Assertion("all_recipients_have_one_delivery_or_exclusion", c.DeliveryReceipts.Distinct().Count() == c.DeliveryReceipts.Count && c.DeliveryReceipts.Count + c.ExcludedRecipientIds.Count == expected));
                    assertions.Add(Assertion("durable_work_and_single_completion_history", court.EnsureRulerDocketState().PatronageWorks.Count(w => w.CommissionId == c.CommissionId) == 1
                        && court.EnsureRulerDocketState().History.Count(h => h.RecordId == c.CommissionId + "_complete") == 1));
                    assertions.Add(Assertion("praise_adjustments_have_real_receipts", c.Objective != ReignPatronageObjective.Praise || court.EnsureRulerDocketState().PendingRelationAdjustments
                        .Where(a => a.AdjustmentId.StartsWith(c.CommissionId + "_praise_", StringComparison.Ordinal)).All(a => a.Applied && !string.IsNullOrWhiteSpace(a.ReceiptId) && a.Delta == 3 && a.SubjectHeroId == c.RulerHeroId)));
                    assertions.Add(Assertion("loyalty_native_before_after_exactly_plus_five", c.Objective != ReignPatronageObjective.Loyalty
                        || (c.NativeDeliveryEffects.Count == c.DeliveryReceipts.Count && c.NativeDeliveryEffects.Select(x => x.ReceiptId).Distinct().Count() == c.NativeDeliveryEffects.Count
                            && c.NativeDeliveryEffects.All(x => x.Stat == "settlement_loyalty" && Math.Abs(x.After - Math.Min(100, x.Before + 5)) < 0.001))));
                }
            }
            else if (expectation == "arrived")
            {
                assertions.Add(Assertion("all_visitors_physically_resident", court.AreNobleVisitorsAvailable(matter, out _)));
                ReignNobleVisitorStay stay = court.EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matter.MatterId);
                IReadOnlyList<CastleRoomSessionRecord> schedule = court.EnsureCastleSchedule();
                List<string> visitorIds = (stay?.Members ?? new List<ReignNobleVisitorMember>())
                    .Where(x => x.Arrived && !x.Released).Select(x => x.HeroId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                CastleRoomSessionRecord guestBedrooms = schedule.FirstOrDefault(x => x.Room == (int)CastleChat.CastleRoom.GuestBedrooms);
                List<string> guestIds = SplitIds(guestBedrooms?.OccupantHeroIdsCsv);
                assertions.Add(Assertion("active_visitors_exposed_once_in_guest_bedrooms", visitorIds.Count > 0
                    && visitorIds.All(id => guestIds.Contains(id, StringComparer.OrdinalIgnoreCase)
                        && schedule.Sum(room => SplitIds(room.OccupantHeroIdsCsv).Count(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase))) == 1)));
            }
            else if (expectation == "departed")
            {
                ReignNobleVisitorStay stay = court.EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matter.MatterId);
                assertions.Add(Assertion("all_visitors_released_to_native_ai", stay != null && stay.Departed && stay.Members.All(m => m.Released && !court.IsNobleVisitorReserved(FindHero(m.HeroId)))));
                if (stay != null) assertions.Add(Assertion("recorded_native_return_locations", stay.Members.Where(m => !string.IsNullOrWhiteSpace(m.ReturnSettlementId))
                    .All(m => FindHero(m.HeroId)?.CurrentSettlement?.StringId == m.ReturnSettlementId)));
            }
            else if (expectation == "dismissed" || expectation == "neglected" || expectation == "reconciled")
            {
                AddCourtLifeFamilyTransitionAssertions(court, assertions, marker, matter, expectation, evidence);
            }
            else if (expectation != "snapshot") assertions.Add(Assertion("known_expectation", false));
            evidence["assertions"] = assertions; evidence["ok"] = assertions.All(a => a.Value<bool?>("passed") == true);
            return evidence;
        }

        private static JObject PrepareCourtLifeFixture(ReignCourtCampaignBehavior court, string runId, JObject options, string save, string instance)
        {
            if (options.Value<string>("confirmation") != RulerDocketFixtureConfirmation || !court.HasRoyalCommandAccess)
                return RulerDocketTestFailure(runId, "court_life_prepare", "The armed disposable fixture confirmation and production capital Court are required.", save, save);
            if (FindCourtLifeTestMatter(court, runId) != null) return RulerDocketTestFailure(runId, "court_life_prepare", "This fixtureRunId already has a matter. Reuse it for observations or choose a new run ID.", save, save);
            if (!Enum.TryParse(options.Value<string>("courtLifeSource"), true, out ReignDocketSource source)
                || source == ReignDocketSource.Notable || source == ReignDocketSource.DomesticNoble)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Select International, Family, Patronage, or Visitor.", save, save);
            string template = options.Value<string>("courtLifeTemplateId") ?? "";
            string existingId = options.Value<string>("courtLifeExistingMatterId") ?? "";
            if (!string.IsNullOrEmpty(existingId))
                return BindExactExistingCourtLifeFixture(court, runId, source, existingId, template, options, save, instance);
            if (source == ReignDocketSource.Patronage && !string.IsNullOrWhiteSpace(template))
            {
                JObject existing = TryBindExistingPatronageFixture(court, runId, template, options, save, instance);
                if (existing != null) return existing;
            }
            int slot = ReignCourtLifeRules.StableRoll(runId, 1000000);
            string reason = "No eligible production participants or required historical facts were available for the exact requested template.";
            double preparedWorldDay = CurrentCourtLifeDay();
            string captiveHeroId = options.Value<string>("courtLifeFixtureCaptiveHeroId") ?? string.Empty;
            if (!string.IsNullOrEmpty(captiveHeroId) && source != ReignDocketSource.International)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Captive setup is restricted to International captive templates.", save, save);
            JObject captiveReceipt = null;
            ReignCourtLifeMatter matter = !string.IsNullOrEmpty(captiveHeroId)
                ? PrepareCourtLifeCaptiveMatter(court, runId, captiveHeroId, template, slot, save, instance, out captiveReceipt, out reason)
                : source == ReignDocketSource.Patronage ? court.TryCreatePatronageMatterForSlot(court.Session.CampaignId, court.Session.TimelineId, CurrentDay(), slot, template)
                : source == ReignDocketSource.Visitor ? court.TryCreateNobleVisitorMatterForSlot(court.Session.CampaignId, court.Session.TimelineId, CurrentDay(), slot, template)
                : source == ReignDocketSource.Family ? court.TryCreateFamilyVisitMatterForSlot(court.Session.CampaignId, court.Session.TimelineId, CurrentDay(), slot, options.Value<string>("courtLifeHeroId"), template)
                : court.TryCreateInternationalTemplateFixture(template, court.Session.CampaignId, court.Session.TimelineId, CurrentDay(), slot, out reason);
            if (matter == null)
            {
                JObject failure = RulerDocketTestFailure(runId, "court_life_prepare", reason, save, save);
                failure["captiveFixture"] = captiveReceipt ?? FindRulerDocketMarker(court, runId)?["captiveFixture"];
                if (source == ReignDocketSource.Patronage)
                    failure["patronageEligibility"] = CourtLifePatronageEligibility(court, template);
                return failure;
            }
            court.EnsureRulerDocketState().CourtLifeMatters.Add(matter);
            int gold = options.Value<int?>("courtLifeFixtureGold") ?? -1;
            if (gold >= 0)
            {
                int delta = gold - Hero.MainHero.Gold;
                if (delta > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, delta, true);
                if (delta < 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, -delta, true);
            }
            int loyalty = options.Value<int?>("courtLifeFixtureLoyalty") ?? -1;
            if (source == ReignDocketSource.Patronage && loyalty >= 0 && loyalty <= 100)
                foreach (Settlement settlement in EligiblePatronageSettlements(matter.KingdomId, court.CurrentCapital)) settlement.Town.Loyalty = loyalty;
            var marker = new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId, ["courtLifeSource"] = source.ToString(),
                ["preparedDay"] = CurrentCourtLifeDay(), ["preparedGameInstance"] = instance, ["beforeDecision"] = CourtLifeNativeState(court, matter) };
            if (captiveReceipt != null) marker["captiveFixture"] = captiveReceipt;
            if (source == ReignDocketSource.Family) marker["familyBaseline"] = CourtLifeFamilyObservation(court, matter);
            StoreRulerDocketMarker(court, marker); court.RaiseStateChanged();
            JObject result = CourtLifeEvidence(court, runId, "court_life_prepare", save, matter);
            result["ok"] = true; result["productionEligibility"] = true; result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["gold"] = gold, ["kingdomSettlementLoyalty"] = loyalty,
                ["captive"] = captiveReceipt,
                ["templateWasForcedWithinEligibility"] = !string.IsNullOrWhiteSpace(template), ["nativeTimeAdvanced"] = false };
            if (source == ReignDocketSource.Visitor)
            {
                ReignNobleVisitorStay stay = court.EnsureRulerDocketState().NobleVisitorStays.SingleOrDefault(s => s.MatterId == matter.MatterId);
                bool immediate = stay != null && stay.Arrived && !stay.Departed
                    && stay.ActualArrivalDay == preparedWorldDay && stay.ArrivalDay == preparedWorldDay
                    && stay.DepartureDay == preparedWorldDay + stay.StayDays
                    && matter.State == ReignCourtLifeMatterState.Pending && matter.AvailableDay == preparedWorldDay
                    && CurrentCourtLifeDay() == preparedWorldDay && stay.Members.Count > 0
                    && stay.Members.All(m => m.Arrived && !m.Released
                        && FindHero(m.HeroId)?.CurrentSettlement?.StringId == stay.HostSettlementId);
                result["assertions"] = new JArray(new JObject { ["id"] = "visitors_placed_immediately_without_time_advance", ["passed"] = immediate });
                result["ok"] = immediate;
                if (!immediate) result["error"] = "Visitor preparation must immediately establish the complete group at court and start its full stay without advancing time.";
            }
            return result;
        }

        private static JObject CourtLifePendingMatterInventory(ReignCourtCampaignBehavior court, JObject options)
        {
            string source = options.Value<string>("courtLifeSource") ?? "";
            string template = options.Value<string>("courtLifeTemplateId") ?? "";
            var matches = court.EnsureRulerDocketState().CourtLifeMatters.Where(m => m.IsPending
                && (string.IsNullOrEmpty(source) || string.Equals(m.Source.ToString(), source, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(template) || m.TemplateId == template)).OrderBy(m => m.MatterId, StringComparer.Ordinal).ToList();
            return new JObject { ["totalCount"] = matches.Count, ["limit"] = 100, ["truncated"] = matches.Count > 100,
                ["sourceFilter"] = source, ["templateFilter"] = template,
                ["matters"] = new JArray(matches.Take(100).Select(m => new JObject {
                    ["matterId"] = m.MatterId, ["templateId"] = m.TemplateId, ["source"] = m.Source.ToString(),
                    ["title"] = m.Title, ["state"] = m.State.ToString(), ["kingdomId"] = m.KingdomId,
                    ["effectsCommitted"] = m.EffectsCommitted, ["completedPlayerTurns"] = m.CompletedPlayerTurnIds.Count,
                    ["boundFixtureRunIds"] = new JArray(court._rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                        .Where(x => x?.Value<string>("courtLifeMatterId") == m.MatterId).Select(x => x.Value<string>("runId"))) })) };
        }

        private static JObject BindExactExistingCourtLifeFixture(ReignCourtCampaignBehavior court, string runId, ReignDocketSource source, string matterId,
            string templateId, JObject options, string save, string instance)
        {
            var state = court.EnsureRulerDocketState();
            var matches = state.CourtLifeMatters.Where(m => m.MatterId == matterId).ToList();
            if (matches.Count != 1)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Exactly one existing matter must match the supplied ID; no fixture was created.", save, save);
            ReignCourtLifeMatter matter = matches[0];
            JObject payload = ParseObject(matter.PayloadJson);
            string error = null;
            if (source != ReignDocketSource.International && source != ReignDocketSource.Patronage)
                error = "Exact existing binding supports International and Patronage only.";
            if (!matter.IsPending || matter.EffectsCommitted || !string.IsNullOrEmpty(matter.ResolutionReceiptId)
                || !string.IsNullOrEmpty(matter.PendingPlayerTurnId) || payload.Value<bool?>("nativeActionStarted") == true
                || payload.Value<bool?>("nativeEffectCommitted") == true || court.HasPendingInternationalCourtLifeWork || court.HasPendingCourtLifeDeliveryWork)
                error = "The existing matter must be pending with no committed or in-flight effects or conversation.";
            bool kingdomMatches = source == ReignDocketSource.International
                ? payload["player"]?.Value<string>("kingdomId") == Clan.PlayerClan?.Kingdom?.StringId
                    && payload["player"]?.Value<string>("leaderHeroId") == Hero.MainHero?.StringId
                    && payload["foreign"]?.Value<string>("kingdomId") == matter.KingdomId
                    && !string.IsNullOrEmpty(matter.KingdomId) && matter.KingdomId != Clan.PlayerClan?.Kingdom?.StringId
                : matter.KingdomId == Clan.PlayerClan?.Kingdom?.StringId;
            if (matter.Source != source || matter.CampaignId != court.Session.CampaignId || matter.TimelineId != court.Session.TimelineId
                || matter.ReignId != court.CurrentReignId() || matter.SettlementId != court.CurrentCapital?.StringId || !kingdomMatches
                || (!string.IsNullOrEmpty(templateId) && matter.TemplateId != templateId))
                error = "The existing matter does not match the exact source, template, ruler, kingdom, campaign, timeline and capital.";
            if ((options.Value<int?>("courtLifeFixtureGold") ?? -1) >= 0 || (options.Value<int?>("courtLifeFixtureLoyalty") ?? -1) >= 0
                || options.Value<bool?>("courtLifeFixtureAgeRulerTo34") == true
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeFixtureCaptiveHeroId"))
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeHeroId")))
                error = "Existing matter binding cannot be combined with fixture mutations or participant selection.";
            if (FindRulerDocketMarker(court, runId) != null || court._rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                .Any(m => m?.Value<string>("courtLifeMatterId") == matterId))
                error = "The fixture ID or existing matter is already bound. Continue its original fixture.";
            if (error != null) return RulerDocketTestFailure(runId, "court_life_prepare", error, save, save);
            string before = CourtLifeFingerprint(court, matter);
            int count = state.CourtLifeMatters.Count;
            StoreRulerDocketMarker(court, new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId,
                ["courtLifeSource"] = source.ToString(), ["preparedDay"] = CurrentCourtLifeDay(), ["preparedGameInstance"] = instance,
                ["beforeDecision"] = CourtLifeNativeState(court, matter), ["reusedExistingMatter"] = true, ["existingMatterFingerprint"] = before });
            bool preserved = before == CourtLifeFingerprint(court, matter) && count == state.CourtLifeMatters.Count;
            JObject result = CourtLifeEvidence(court, runId, "court_life_prepare", save, matter);
            result["ok"] = preserved; result["reusedExistingMatter"] = true; result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["markerOnly"] = true, ["nativeTimeAdvanced"] = false };
            result["assertions"] = new JArray(Assertion("existing_matter_binding_preserves_matter_and_native_state", preserved));
            return result;
        }

        private static JObject TryBindExistingPatronageFixture(ReignCourtCampaignBehavior court, string runId, string templateId, JObject options, string save, string instance)
        {
            var state = court.EnsureRulerDocketState();
            var pending = state.CourtLifeMatters.Where(m => m.IsPending && m.TemplateId == templateId).ToList();
            if (pending.Count == 0) return null;
            string error = null;
            if (pending.Count != 1) error = "The exact template has multiple pending matters; automatic fixture binding is ambiguous.";
            ReignCourtLifeMatter matter = pending.Count == 1 ? pending[0] : null;
            if (matter != null && (matter.Source != ReignDocketSource.Patronage || matter.EffectsCommitted
                || matter.CampaignId != court.Session.CampaignId || matter.TimelineId != court.Session.TimelineId
                || matter.ReignId != court.CurrentReignId() || matter.KingdomId != Clan.PlayerClan?.Kingdom?.StringId
                || matter.SettlementId != court.CurrentCapital?.StringId))
                error = "The pending matter does not belong to this ruler, campaign, timeline and capital.";
            if ((options.Value<int?>("courtLifeFixtureGold") ?? -1) >= 0
                || (options.Value<int?>("courtLifeFixtureLoyalty") ?? -1) >= 0
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeFixtureCaptiveHeroId")))
                error = "Existing matters cannot be combined with fixture gold, loyalty or captive mutations.";
            if (matter != null && court._rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                .Any(m => m?.Value<string>("courtLifeMatterId") == matter.MatterId))
                error = "This pending matter is already bound to a fixture. Continue that fixture instead.";
            if (error != null)
            {
                JObject failure = RulerDocketTestFailure(runId, "court_life_prepare", error, save, save);
                failure["patronageEligibility"] = CourtLifePatronageEligibility(court, templateId);
                return failure;
            }
            string before = CourtLifeFingerprint(court, matter);
            int matterCount = state.CourtLifeMatters.Count;
            var marker = new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId,
                ["courtLifeSource"] = ReignDocketSource.Patronage.ToString(), ["preparedDay"] = CurrentCourtLifeDay(),
                ["preparedGameInstance"] = instance, ["beforeDecision"] = CourtLifeNativeState(court, matter),
                ["reusedExistingMatter"] = true, ["existingMatterFingerprint"] = before };
            StoreRulerDocketMarker(court, marker);
            bool preserved = before == CourtLifeFingerprint(court, matter) && state.CourtLifeMatters.Count == matterCount;
            JObject result = CourtLifeEvidence(court, runId, "court_life_prepare", save, matter);
            result["ok"] = preserved;
            result["reusedExistingMatter"] = true;
            result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["markerOnly"] = true, ["nativeTimeAdvanced"] = false };
            result["assertions"] = new JArray(Assertion("existing_patronage_binding_preserves_matter_and_native_state", preserved));
            return result;
        }

        private static JObject CourtLifePatronageEligibility(ReignCourtCampaignBehavior court, string requestedTemplateId)
        {
            var state = court.EnsureRulerDocketState();
            Hero ruler = Hero.MainHero;
            Settlement host = court.FindCurrentCapital();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            var templates = new JArray();
            foreach (ReignPatronageTemplate template in ReignPatronageCatalog.Templates.Where(t =>
                string.IsNullOrWhiteSpace(requestedTemplateId) || t.Id == requestedTemplateId))
            {
                var missing = new JArray();
                if (host == null) missing.Add("capital_missing");
                if (kingdom == null || ruler == null || kingdom.Leader != ruler) missing.Add("player_not_ruler");
                if (template.RequiresSpouse && ruler?.Spouse?.IsAlive != true) missing.Add("living_spouse_required");
                if (template.RequiresChild && (ruler == null || !ruler.Children.Any(c => c != null && c.IsAlive))) missing.Add("living_child_required");
                if (template.Id == "patronage-coming-of-age" && (ruler == null || !PatronageComingOfAgeChildren(ruler).Any())) missing.Add("coming_of_age_child_required");
                if ((template.Id == "patronage-returning-artist" || template.Id == "patronage-popular-performer")
                    && !state.PatronageCreators.Any(c => c.Role == template.CreatorRole && c.HomeSettlementId == host?.StringId
                        && state.PatronageWorks.Any(w => w.CreatorId == c.ActorId))) missing.Add("prior_creator_work_required");
                var subjects = court.PatronageSubjectHistory(template).Take(5).ToList();
                if (template.RequiresHistoricalSubject && subjects.Count == 0) missing.Add("public_historical_subject_required");
                var pending = state.CourtLifeMatters.Where(m => m.IsPending && m.TemplateId == template.Id).ToList();
                if (pending.Count > 0) missing.Add("same_template_pending");
                templates.Add(new JObject { ["templateId"] = template.Id, ["eligible"] = missing.Count == 0,
                    ["unmetPrerequisites"] = missing, ["requiredContext"] = template.RequiredContext,
                    ["historicalSubjects"] = new JArray(subjects.Select(s => s.Summary)),
                    ["pendingCount"] = pending.Count, ["pendingMatterIds"] = new JArray(pending.Take(5).Select(m => m.MatterId)) });
            }
            return new JObject { ["readOnly"] = true, ["requestedTemplateId"] = requestedTemplateId ?? "",
                ["templateKnown"] = string.IsNullOrWhiteSpace(requestedTemplateId) || templates.Count > 0,
                ["capitalId"] = host?.StringId, ["templates"] = templates };
        }

        internal static ReignCourtLifeMatter FindCourtLifeTestMatter(ReignCourtCampaignBehavior court, string runId)
        {
            string id = FindRulerDocketMarker(court, runId)?.Value<string>("courtLifeMatterId");
            return court.EnsureRulerDocketState().CourtLifeMatters.FirstOrDefault(x => x.MatterId == id);
        }

        private static JObject CourtLifeEvidence(ReignCourtCampaignBehavior court, string runId, string phase, string save, ReignCourtLifeMatter matter)
        {
            return new JObject { ["schema"] = "reign-court-life-evidence-v1", ["ok"] = true, ["runId"] = runId, ["phase"] = phase,
                ["saveName"] = save, ["worldDay"] = CurrentCourtLifeDay(), ["matterId"] = matter.MatterId,
                ["source"] = matter.Source.ToString(), ["matter"] = JObject.FromObject(matter),
                ["nativeState"] = CourtLifeNativeState(court, matter), ["fixture"] = FindRulerDocketMarker(court, runId),
                ["nativeTimeAdvancedByHarness"] = false, ["savedByHarness"] = false };
        }

        private static JObject CourtLifeNativeState(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter)
        {
            var state = court.EnsureRulerDocketState();
            JObject family = court.FamilyMirror();
            return new JObject { ["rulerGold"] = Hero.MainHero?.Gold ?? 0,
                ["international"] = matter.Source == ReignDocketSource.International ? CourtLifeInternationalNativeState(court, matter) : null,
                ["settlements"] = new JArray(Settlement.All.Where(s => s.Town != null && s.OwnerClan?.Kingdom?.StringId == matter.KingdomId)
                    .OrderBy(s => s.StringId).Select(s => new JObject { ["id"] = s.StringId, ["loyalty"] = s.Town.Loyalty, ["kingdomId"] = s.OwnerClan?.Kingdom?.StringId })),
                ["participants"] = new JArray(matter.Participants.Select(p => new JObject { ["heroId"] = p.HeroId, ["actorId"] = p.ActorId,
                    ["age"] = FindHero(p.HeroId)?.Age ?? p.Age, ["settlementId"] = FindHero(p.HeroId)?.CurrentSettlement?.StringId,
                    ["partyId"] = FindHero(p.HeroId)?.PartyBelongedTo?.StringId, ["isPrisoner"] = FindHero(p.HeroId)?.IsPrisoner ?? false })),
                ["commissions"] = JArray.FromObject(state.PatronageCommissions.Where(c => c.MatterId == matter.MatterId)),
                ["works"] = JArray.FromObject(state.PatronageWorks.Where(w => state.PatronageCommissions.Any(c => c.MatterId == matter.MatterId && c.CommissionId == w.CommissionId))),
                ["stays"] = JArray.FromObject(state.NobleVisitorStays.Where(s => s.MatterId == matter.MatterId)),
                ["relations"] = JArray.FromObject(state.PendingRelationAdjustments.Where(a => a.AdjustmentId.Contains(matter.MatterId))),
                ["familyAttention"] = family,
                ["familyNativeAges"] = new JArray((family["members"] as JArray ?? new JArray()).OfType<JObject>()
                    .OrderBy(m => m.Value<string>("hero_id"), StringComparer.Ordinal).Select(m => {
                        string id = m.Value<string>("hero_id"); Hero hero = FindHero(id);
                        return new JObject { ["heroId"] = id, ["exists"] = hero != null, ["age"] = hero == null ? JValue.CreateNull() : new JValue(hero.Age) };
                    })),
                ["familyOutboxCount"] = state.FamilyVisitOutbox?.Count ?? 0,
                ["familyAttentionPending"] = court.HasPendingFamilyVisitWork,
                ["history"] = JArray.FromObject(state.History.Where(h => h.PetitionId == matter.MatterId || h.RecordId.Contains(matter.MatterId))) };
        }

        private static string CourtLifeFingerprint(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter)
            => CourtLifeSnapshotFingerprint(new JObject { ["matter"] = JObject.FromObject(matter), ["native"] = CourtLifeNativeState(court, matter) });

        private static string CourtLifeSnapshotFingerprint(JObject snapshot)
        {
            JObject stable = (JObject)snapshot.DeepClone();
            // Compare native ages separately from the family mirror's last-observed age.
            // Keep all durable family values; observation time is not saved game state.
            JObject family = stable["native"]?["familyAttention"] as JObject;
            if (stable["native"]?["familyNativeAges"] is JArray)
            {
                family?.Remove("worldDay");
                foreach (JObject member in (family?["members"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string raw = member.Value<string>("profile_json");
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    try { JObject profile = JObject.Parse(raw); profile.Remove("age"); member["profile_json"] = profile.ToString(Formatting.None); }
                    catch (JsonException) { /* Preserve malformed data verbatim so it cannot disappear from the comparison. */ }
                }
            }
            foreach (var collection in new[] { new { Name = "members", Id = "hero_id" }, new { Name = "visits", Id = "visit_id" } })
                if (family?[collection.Name] is JArray rows)
                    family[collection.Name] = new JArray(rows.OrderBy(row => (row as JObject)?.Value<string>(collection.Id) ?? "", StringComparer.Ordinal)
                        .ThenBy(row => row.ToString(Formatting.None), StringComparer.Ordinal).Select(row => row.DeepClone()));
            return ReignCourtTerms.Hash(stable.ToString(Formatting.None));
        }

        private static void AddCourtLifeFingerprintContractAssertions(JArray assertions)
        {
            var snapshot = new JObject { ["native"] = new JObject { ["familyAttention"] = new JObject {
                ["members"] = new JArray(new JObject { ["hero_id"] = "child", ["dismissals"] = 1 }, new JObject { ["hero_id"] = "spouse", ["dismissals"] = 2 }),
                ["visits"] = new JArray(new JObject { ["visit_id"] = "visit_b", ["status"] = "dismissed" }, new JObject { ["visit_id"] = "visit_a", ["status"] = "pending" }) } } };
            string original = snapshot.ToString(Formatting.None), fingerprint = CourtLifeSnapshotFingerprint(snapshot);
            JObject reversed = (JObject)snapshot.DeepClone();
            foreach (string key in new[] { "members", "visits" })
                reversed["native"]["familyAttention"][key] = new JArray(((JArray)reversed["native"]["familyAttention"][key]).Reverse().Select(row => row.DeepClone()));
            assertions.Add(Assertion("family_row_order_does_not_change_fingerprint", fingerprint == CourtLifeSnapshotFingerprint(reversed)
                && snapshot.ToString(Formatting.None) == original));
            reversed["native"]["familyAttention"]["members"][0]["dismissals"] = 3;
            assertions.Add(Assertion("family_value_change_changes_fingerprint", fingerprint != CourtLifeSnapshotFingerprint(reversed)));
            var observed = (JObject)snapshot.DeepClone();
            observed["native"]["familyNativeAges"] = new JArray(new JObject { ["heroId"] = "child", ["exists"] = true, ["age"] = 4.5 });
            observed["native"]["familyAttention"]["worldDay"] = 100.1;
            observed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.49,\"isChild\":true}";
            string observedFingerprint = CourtLifeSnapshotFingerprint(observed);
            JObject refreshed = (JObject)observed.DeepClone();
            refreshed["native"]["familyAttention"]["worldDay"] = 100.2;
            refreshed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.5,\"isChild\":true}";
            assertions.Add(Assertion("family_cached_age_refresh_does_not_change_fingerprint", observedFingerprint == CourtLifeSnapshotFingerprint(refreshed)));
            refreshed["native"]["familyNativeAges"][0]["age"] = 4.6;
            assertions.Add(Assertion("family_native_age_change_changes_fingerprint", observedFingerprint != CourtLifeSnapshotFingerprint(refreshed)));
            refreshed = (JObject)observed.DeepClone();
            refreshed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.49,\"isChild\":false}";
            assertions.Add(Assertion("family_profile_value_change_changes_fingerprint", observedFingerprint != CourtLifeSnapshotFingerprint(refreshed)));
        }

        private static JObject CourtLifeFamilyObservation(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter)
        {
            JObject mirror = court.FamilyMirror();
            string hero = matter.Participants.FirstOrDefault()?.HeroId ?? "", player = Hero.MainHero?.StringId ?? "";
            JObject member = (mirror["members"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(x => x.Value<string>("hero_id") == hero);
            JObject visit = (mirror["visits"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(x => x.Value<string>("visit_id") == matter.MatterId);
            bool scoped = matter.Source == ReignDocketSource.Family && !string.IsNullOrWhiteSpace(hero) && !string.IsNullOrWhiteSpace(player)
                && mirror.Value<string>("campaignId") == matter.CampaignId && mirror.Value<string>("timelineId") == matter.TimelineId
                && mirror.Value<string>("playerId") == player;
            return new JObject { ["visitId"] = matter.MatterId, ["heroId"] = hero, ["playerId"] = player,
                ["campaignId"] = matter.CampaignId, ["timelineId"] = matter.TimelineId, ["scoped"] = scoped,
                ["worldDay"] = mirror.Value<double?>("worldDay"), ["member"] = member?.DeepClone(), ["visit"] = visit?.DeepClone(),
                ["quiescent"] = !court.HasPendingFamilyVisitWork && (court.EnsureRulerDocketState().FamilyVisitOutbox?.Count ?? 0) == 0 };
        }

        private static bool SameCourtLifeFamilyObservationScope(JObject left, JObject right)
            => left?.Value<bool?>("scoped") == true && right?.Value<bool?>("scoped") == true
                && new[] { "campaignId", "timelineId", "playerId", "heroId", "visitId" }.All(key =>
                    !string.IsNullOrWhiteSpace(left.Value<string>(key)) && left.Value<string>(key) == right.Value<string>(key));

        private static void AddCourtLifeFamilyTransitionAssertions(ReignCourtCampaignBehavior court, JArray assertions, JObject marker, ReignCourtLifeMatter matter, string expectation, JObject evidence)
        {
            JObject baseline = marker["familyBaseline"] as JObject, current = CourtLifeFamilyObservation(court, matter);
            JObject before = baseline?["member"] as JObject, member = current["member"] as JObject, visit = current["visit"] as JObject;
            bool bound = SameCourtLifeFamilyObservationScope(baseline, current) && before != null && member != null;
            bool ready = bound && current.Value<bool?>("quiescent") == true;
            bool exactVisit = ready && visit?.Value<string>("visit_id") == matter.MatterId
                && visit.Value<string>("hero_id") == current.Value<string>("heroId") && visit.Value<string>("player_id") == current.Value<string>("playerId")
                && visit.Value<string>("campaign_id") == matter.CampaignId && visit.Value<string>("timeline_id") == matter.TimelineId;
            string beforeStatus = (baseline?["visit"] as JObject)?.Value<string>("status");
            bool newlyDismissed = exactVisit && (string.IsNullOrEmpty(beforeStatus) || beforeStatus == "pending")
                && visit.Value<string>("status") == "dismissed" && visit.Value<int?>("player_turns") < ReignFamilyVisitRules.RequiredPlayerTurns
                && current.Value<double?>("worldDay") >= visit.Value<double?>("deadline")
                && before.Value<int?>("dismissals") >= 0 && member.Value<int?>("dismissals") == before.Value<int?>("dismissals") + 1;
            int patience = member?.Value<int?>("patiencePercent") ?? -1;
            bool newlyNeglected = newlyDismissed && before.Value<int?>("neglected") == 0 && member.Value<int?>("neglected") == 1
                && patience >= 0 && patience <= 100 && !ReignFamilyVisitRules.IsNeglected(before.Value<int>("dismissals"), patience)
                && ReignFamilyVisitRules.IsNeglected(member.Value<int>("dismissals"), patience);
            assertions.Add(Assertion("family_exact_scoped_baseline_and_quiescent_mirror", ready));
            if (expectation == "dismissed" || expectation == "neglected")
                assertions.Add(Assertion("exact_visit_new_dismissal_and_counter_increment", newlyDismissed));
            if (expectation == "neglected")
            {
                assertions.Add(Assertion("exact_visit_crossed_patience_into_neglect", newlyNeglected));
                if (newlyNeglected && assertions.All(a => a.Value<bool?>("passed") == true))
                { marker["familyNeglectEvidence"] = current.DeepClone(); StoreRulerDocketMarker(court, marker); }
            }
            if (expectation == "reconciled")
            {
                JObject neglected = marker["familyNeglectEvidence"] as JObject;
                bool priorNeglect = SameCourtLifeFamilyObservationScope(neglected, current)
                    && (neglected?["member"] as JObject)?.Value<int?>("neglected") == 1
                    && (neglected?["visit"] as JObject)?.Value<string>("status") == "dismissed";
                assertions.Add(Assertion("reconciliation_follows_proven_neglect_for_exact_visit", ready && priorNeglect
                    && current.Value<double?>("worldDay") >= neglected.Value<double?>("worldDay")
                    && member.Value<int?>("neglected") == 0 && member.Value<int?>("dismissals") == 0));
            }
            evidence["familyTransition"] = new JObject { ["baseline"] = baseline?.DeepClone(), ["current"] = current,
                ["verifiedNeglect"] = marker["familyNeglectEvidence"]?.DeepClone() };
            evidence["fixture"] = marker.DeepClone();
        }

        private static void AddCourtLifeDecisionAssertions(ReignCourtCampaignBehavior court, JArray assertions, JObject marker, ReignCourtLifeMatter matter)
        {
            if (matter.Source == ReignDocketSource.International)
            {
                JObject payload = ParseObject(matter.PayloadJson);
                string remedy = payload.Value<string>("remedy");
                if (matter.SelectedOptionId == "accept" && remedy == "prisoner")
                {
                    JObject terms = ParseObject(matter.Options.FirstOrDefault(o => o.OptionId == "accept")?.TermsJson);
                    AddDirectCourtPrisonerAssertions(assertions, terms, payload["nativeReceipt"] as JObject,
                        marker["beforeDecision"] as JObject, CourtLifeNativeState(court, matter),
                        matter.ResolutionReceiptId, Hero.MainHero?.StringId);
                }
                else if (matter.SelectedOptionId == "accept" && (remedy == "sign_trade_agreement"
                    || (remedy == "proposal" && payload["normalizedAction"] is JObject)))
                {
                    JObject action = (JObject)CourtLifeInternationalNativeState(court, matter)["nativeDiplomaticAction"];
                    assertions.Add(Assertion("accepted_diplomacy_has_one_completed_native_action",
                        action.Value<bool?>("serviceAvailable") == true && action.Value<int?>("count") == 1
                        && action.Value<string>("status") == "Completed"));
                    JObject receipt = payload["nativeReceipt"] as JObject;
                    assertions.Add(Assertion("accepted_diplomacy_receipt_is_completed",
                        receipt?.Value<string>("status") == "completed"
                        || (receipt?.Value<bool?>("Success") == true && receipt.Value<bool?>("Completed") == true)));
                }
                return;
            }
            if (matter.Source != ReignDocketSource.Patronage) return;
            ReignPatronageCommission c = court.EnsureRulerDocketState().PatronageCommissions.FirstOrDefault(x => x.MatterId == matter.MatterId);
            int before = marker["beforeDecision"]?.Value<int?>("rulerGold") ?? -1;
            assertions.Add(Assertion("gold_matches_actual_commission", before >= 0 && before - (Hero.MainHero?.Gold ?? 0) == (c?.GoldPaid ?? 0)));
            if (matter.SelectedOptionId == "decline") assertions.Add(Assertion("decline_creates_no_commission", c == null));
            else assertions.Add(Assertion("one_paid_commission_exact_cost", c != null && c.Paid && c.GoldPaid == ReignPatronageRules.Cost(c.Reach)
                && court.EnsureRulerDocketState().PatronageCommissions.Count(x => x.MatterId == matter.MatterId) == 1));
        }

        private static void AddDirectCourtPrisonerAssertions(JArray assertions, JObject terms, JObject receipt,
            JObject before, JObject after, string resolutionId, string rulerId)
        {
            string captiveId = terms.Value<string>("captiveId");
            JObject beforeCaptive = before?["international"]?["captive"] as JObject;
            JObject afterCaptive = after["international"]?["captive"] as JObject;
            assertions.Add(Assertion("direct_court_prisoner_released_from_original_roster",
                !string.IsNullOrWhiteSpace(captiveId)
                && receipt?.Value<string>("schema") == "reign-direct-court-prisoner-v1"
                && receipt.Value<string>("releasedHeroId") == captiveId
                && !string.IsNullOrWhiteSpace(receipt.Value<string>("originalCaptorId"))
                && receipt.Value<int?>("originalRosterCountBefore") == 1
                && receipt.Value<int?>("originalRosterCountAfter") == 0
                && receipt.Value<bool?>("isPrisoner") == false && receipt.Value<bool?>("hasCaptor") == false
                && beforeCaptive?.Value<string>("heroId") == captiveId && beforeCaptive.Value<bool?>("heldByPlayer") == true
                && afterCaptive?.Value<string>("heroId") == captiveId && afterCaptive.Value<bool?>("alive") == true
                && afterCaptive.Value<bool?>("isPrisoner") == false && afterCaptive.Value<bool?>("heldByPlayer") == false
                && string.IsNullOrEmpty(afterCaptive.Value<string>("custodianHeroId"))));

            bool? ransom = terms.Value<bool?>("ransom");
            int? gold = ransom == false ? 0 : terms.Value<int?>("gold");
            JObject beforePayer = before?["international"]?["foreignRuler"] as JObject;
            JObject afterPayer = after["international"]?["foreignRuler"] as JObject;
            JObject payment = receipt?["payment"] as JObject;
            int? payerBefore = beforePayer?.Value<int?>("gold"), payerAfter = afterPayer?.Value<int?>("gold");
            int? recipientBefore = before?.Value<int?>("rulerGold"), recipientAfter = after.Value<int?>("rulerGold");
            bool balancesMatch = gold.HasValue && gold.Value >= 0
                && payerBefore.HasValue && payerAfter.HasValue && recipientBefore.HasValue && recipientAfter.HasValue
                && (long)payerBefore.Value - payerAfter.Value == gold.Value
                && (long)recipientAfter.Value - recipientBefore.Value == gold.Value;
            bool exactPayment = ransom == false ? receipt?["payment"] == null : payment != null
                && payment.Value<int?>("gold") == gold
                && payment.Value<string>("payerId") == terms.Value<string>("payerHeroId")
                && payment.Value<string>("payerId") == beforePayer?.Value<string>("heroId")
                && payment.Value<string>("payerId") == afterPayer?.Value<string>("heroId")
                && payment.Value<string>("recipientId") == terms.Value<string>("recipientHeroId")
                && payment.Value<string>("recipientId") == rulerId
                && payment.Value<int?>("payerBefore") == payerBefore && payment.Value<int?>("payerAfter") == payerAfter
                && payment.Value<int?>("recipientBefore") == recipientBefore && payment.Value<int?>("recipientAfter") == recipientAfter;
            assertions.Add(Assertion("direct_court_prisoner_payment_matches_native_balances",
                ransom.HasValue && receipt?.Value<bool?>("ransom") == ransom
                && receipt?.Value<int?>("ransomGold") == gold && balancesMatch && exactPayment));

            JObject action = after["international"]?["nativeDiplomaticAction"] as JObject;
            var history = (after["history"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            assertions.Add(Assertion("direct_court_prisoner_has_one_history_and_no_diplomatic_action",
                !string.IsNullOrWhiteSpace(resolutionId) && history.Count == 1
                && history[0].Value<string>("RecordId") == resolutionId
                && action?.Value<bool?>("serviceAvailable") == true && action.Value<int?>("count") == 0));
        }
    }
}
