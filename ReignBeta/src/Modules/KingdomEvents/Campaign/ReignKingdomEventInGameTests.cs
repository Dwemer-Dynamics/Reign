using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Shared;
using ReignBeta.Events;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.KingdomEvents.Campaign.ReignKingdomEventsCampaignBehavior;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.WorldSimulation.Economy;

namespace ReignBeta.KingdomEvents.Campaign
{
    // Harness-owned kingdom-event acceptance controls. Saved test ledgers and the one-tick organic trigger stay on ReignKingdomEventsCampaignBehavior.
    internal static partial class ReignKingdomEventTestControls
    {
        private const string KingdomEventTestConfirmation = "force Reign kingdom event on disposable save";

        internal static JObject RunKingdomEventTestProfile(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, string phase, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string normalizedPhase = (phase ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            switch (normalizedPhase)
            {
                case "preflight": return KingdomEventTestPreflight(kingdomEvents, normalizedRunId, options);
                case "force": return ForceKingdomEventTest(kingdomEvents, normalizedRunId, options);
                case "observe": return ObserveKingdomEventTest(kingdomEvents, normalizedRunId, options, false);
                case "verify_reload": return ObserveKingdomEventTest(kingdomEvents, normalizedRunId, options, true);
                case "mark_reload": return MarkKingdomEventReloadTest(kingdomEvents, normalizedRunId, options);
                case "no_target": return NoTargetKingdomEventTest(kingdomEvents, normalizedRunId, options);
                case "organic_prepare": return PrepareOrganicKingdomEventTest(kingdomEvents, normalizedRunId, options);
                case "organic_verify": return VerifyOrganicKingdomEventTest(kingdomEvents, normalizedRunId, options);
                case "cleanup_marker": return CleanupKingdomEventTest(kingdomEvents, normalizedRunId, options);
                default: return KingdomEventTestFailure(normalizedRunId, normalizedPhase, "Unsupported kingdom-event test phase.");
            }
        }

        private static JObject KingdomEventTestPreflight(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            JArray assertions = new JArray();
            bool exactSave = TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out string activeSaveName, out string saveError);
            AddKingdomEventAssertion(assertions, "kingdom_event_exact_disposable_save", exactSave,
                "The client independently matched Bannerlord's active save to the exact MCP-authorized campaign-test Current.",
                new JObject { ["expectedSaveName"] = expectedSaveName, ["activeSaveName"] = activeSaveName,
                    ["error"] = saveError });
            AddKingdomEventAssertion(assertions, "kingdom_event_campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null,
                "A Bannerlord campaign is loaded.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_production_catalog", Definitions.Count == 15
                && Definitions.Count(x => x.Beneficial) == 8 && Definitions.Count(x => !x.Beneficial) == 7,
                "The production catalog contains all fifteen approved archetypes with the expected polarity split.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_no_duplicate_ids",
                Definitions.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Definitions.Count,
                "Every production archetype ID is unique.", null);
            JArray catalog = new JArray(Definitions.Select(definition => new JObject
            {
                ["archetypeId"] = definition.Id,
                ["title"] = definition.Title,
                ["polarity"] = definition.Beneficial ? "beneficial" : "harmful",
                ["timed"] = definition.Timed,
                ["magnitude"] = definition.Magnitude,
                ["eligibleTargets"] = new JArray(kingdomEvents.EligibleKingdoms(definition).Select(kingdom => new JObject
                {
                    ["kingdomId"] = kingdom.StringId,
                    ["name"] = kingdom.Name?.ToString() ?? kingdom.StringId,
                    ["isPlayerKingdom"] = kingdom.RulingClan == Clan.PlayerClan,
                    ["fiefCount"] = kingdom.Fiefs.Count
                }))
            }));
            JObject result = KingdomEventTestResult(runId, "preflight", assertions);
            result["catalog"] = catalog;
            result["lastRolledDay"] = kingdomEvents._lastRolledDay;
            result["activeTimedEvents"] = new JArray(kingdomEvents._events.Where(x => x?.IsActiveAt(CurrentDay()) == true).Select(x => JObject.FromObject(x)));
            result["safety"] = "Force only after loading a verified disposable baseline; immediate events change diplomacy or succession.";
            return result;
        }

        private static JObject ForceKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "force", saveError);
            if (!string.Equals((string)options["confirmation"], KingdomEventTestConfirmation, StringComparison.Ordinal))
                return KingdomEventTestFailure(runId, "force", "The exact disposable-save force confirmation is required.");
            ReignKingdomEventTestLedger replay = FindKingdomEventLedger(kingdomEvents, runId);
            if (replay != null)
            {
                JObject existing = ObserveKingdomEventTest(kingdomEvents, runId, options, false);
                existing["idempotent"] = true;
                return existing;
            }

            string archetypeId = ((string)options["archetypeId"] ?? string.Empty).Trim().ToLowerInvariant();
            string targetVariant = ((string)options["targetVariant"] ?? "first").Trim().ToLowerInvariant();
            ReignKingdomEventDefinition definition = FindDefinition(archetypeId);
            if (definition == null) return KingdomEventTestFailure(runId, "force", "Unknown kingdom event archetype.");
            List<Kingdom> eligible = kingdomEvents.EligibleKingdoms(definition);
            List<Kingdom> orderedEligible = eligible.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            Kingdom target = SelectKingdomEventTestTarget(eligible, targetVariant, (string)options["kingdomId"]);
            if (target == null) return KingdomEventTestFailure(runId, "force",
                "No eligible " + targetVariant + " target exists for " + definition.Title + ".");

            HashSet<string> beforeIds = new HashSet<string>(kingdomEvents._events.Select(x => x.EventId), StringComparer.OrdinalIgnoreCase);
            int previousLastRolledDay = kingdomEvents._lastRolledDay;
            JObject before = KingdomEventTestSnapshot(kingdomEvents, target);
            before["eligibleTargetOrder"] = new JArray(orderedEligible.Select(x => x.StringId));
            int expectedTargetIndex = targetVariant == "last" ? orderedEligible.Count - 1
                : targetVariant == "middle" ? orderedEligible.Count / 2 : 0;
            before["selectedTargetIndex"] = expectedTargetIndex;
            string originalRuler = target.Leader?.StringId ?? string.Empty;
            bool forced = kingdomEvents.ForceEvent(definition.Id, target.StringId, out string forceResult);
            ReignKingdomEventRecord record = kingdomEvents._events.LastOrDefault(x => !beforeIds.Contains(x.EventId));
            if (!forced || record == null)
                return KingdomEventTestFailure(runId, "force", forceResult ?? "The production force path did not create an event record.");

            ReignKingdomEventTestLedger ledger = new ReignKingdomEventTestLedger
            {
                RunId = runId,
                CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                ArchetypeId = definition.Id,
                TargetVariant = targetVariant,
                EventId = record.EventId,
                KingdomStringId = target.StringId,
                OriginalRulerHeroStringId = originalRuler,
                PreviousLastRolledDay = previousLastRolledDay,
                StartedDay = CurrentDay(),
                BeforeSnapshotJson = before.ToString(Formatting.None),
                DisposableSaveConfirmed = true
            };
            kingdomEvents._testLedgers.Add(ledger);
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_forced_record_created", record.WasForced
                && record.ArchetypeId == definition.Id && record.KingdomStringId == target.StringId,
                "The production force path created the requested event for the selected eligible kingdom.", JObject.FromObject(record));
            AddKingdomEventAssertion(assertions, "kingdom_event_stable_target_order",
                expectedTargetIndex >= 0 && expectedTargetIndex < orderedEligible.Count
                && orderedEligible[expectedTargetIndex] == target,
                "The first, middle, or last target came from stable ordinal kingdom-ID ordering.",
                new JObject { ["variant"] = targetVariant, ["selectedIndex"] = expectedTargetIndex,
                    ["selectedKingdomId"] = target.StringId,
                    ["eligibleTargetOrder"] = new JArray(orderedEligible.Select(x => x.StringId)) });
            AddKingdomEventAssertion(assertions, "kingdom_event_daily_roll_untouched", kingdomEvents._lastRolledDay == previousLastRolledDay
                && record.TriggerRollBasisPoints == -1,
                "Forcing the test event did not consume, advance, or rewrite the production daily roll.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_record_exactly_once", kingdomEvents._events.Count(x => x.EventId == record.EventId) == 1,
                "The event ID occurs exactly once.", null);
            JObject result = KingdomEventTestResult(runId, "force", assertions);
            result["record"] = JObject.FromObject(record);
            result["before"] = before;
            result["after"] = KingdomEventTestSnapshot(kingdomEvents, target);
            result["forceResult"] = forceResult;
            result["requiresAnnouncementScreenshot"] = true;
            result["worldHistoryCorrelationId"] = record.EventId;
            return result;
        }

        private static JObject ObserveKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options, bool verifyReload)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out string activeSaveName, out string saveError))
                return KingdomEventTestFailure(runId, verifyReload ? "verify_reload" : "observe", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(kingdomEvents, runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return KingdomEventTestFailure(runId, verifyReload ? "verify_reload" : "observe", "No persisted forced-event ledger exists for this run.");
            ReignKingdomEventRecord record = kingdomEvents._events.FirstOrDefault(x => string.Equals(x.EventId, ledger.EventId, StringComparison.OrdinalIgnoreCase));
            Kingdom target = FindKingdom(ledger.KingdomStringId);
            ReignKingdomEventDefinition definition = FindDefinition(ledger.ArchetypeId);
            JObject snapshot = KingdomEventTestSnapshot(kingdomEvents, target);
            snapshot["capturedUtc"] = DateTime.UtcNow.ToString("o");
            ledger.ObservationJson.Add(snapshot.ToString(Formatting.None));
            if (ledger.ObservationJson.Count > 64) ledger.ObservationJson.RemoveRange(0, ledger.ObservationJson.Count - 64);

            bool expectExpired = (bool?)options["expectExpired"] == true;
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_ledger_persisted", !verifyReload
                || string.Equals(ledger.CampaignId, ReignCampaignIdentity.CurrentCampaignId(), StringComparison.OrdinalIgnoreCase),
                "The forced-event ledger belongs to this campaign and survived the requested save/load boundary.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_record_persisted", record != null
                && kingdomEvents._events.Count(x => x.EventId == ledger.EventId) == 1,
                "The production event record remains present exactly once.", record == null ? null : JObject.FromObject(record));
            AddKingdomEventAssertion(assertions, "kingdom_event_daily_roll_marker_monotonic",
                kingdomEvents._lastRolledDay >= ledger.PreviousLastRolledDay && kingdomEvents._lastRolledDay <= CurrentDayIndex(),
                "The production daily-roll marker remains monotonic and bounded by the current day; ordinary time advancement may move it after the force-path assertion.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_exact_disposable_save",
                string.Equals(expectedSaveName, activeSaveName, StringComparison.OrdinalIgnoreCase),
                "Observation remained bound to the exact MCP-authorized campaign-test Current.",
                new JObject { ["expectedSaveName"] = expectedSaveName, ["activeSaveName"] = activeSaveName });
            if (verifyReload)
            {
                JObject reloadMarker = FindKingdomEventReloadMarker(ledger);
                string preparedGameInstanceId = reloadMarker?.Value<string>("gameInstanceId") ?? string.Empty;
                string currentGameInstanceId = options.Value<string>("gameInstanceId") ?? string.Empty;
                string expectedFingerprint = reloadMarker?.Value<string>("featureFingerprint") ?? string.Empty;
                string actualFingerprint = KingdomEventFeatureFingerprint(kingdomEvents, ledger, record);
                AddKingdomEventAssertion(assertions, "kingdom_event_different_game_instance",
                    preparedGameInstanceId.Length > 0 && currentGameInstanceId.Length > 0
                    && !string.Equals(preparedGameInstanceId, currentGameInstanceId, StringComparison.OrdinalIgnoreCase),
                    "Save verification ran in a different native game instance from save preparation.",
                    new JObject { ["preparedGameInstanceId"] = preparedGameInstanceId,
                        ["currentGameInstanceId"] = currentGameInstanceId });
                AddKingdomEventAssertion(assertions, "kingdom_event_reload_feature_fingerprint",
                    expectedFingerprint.Length == 64
                    && string.Equals(expectedFingerprint, actualFingerprint, StringComparison.OrdinalIgnoreCase),
                    "The event ledger, production record, immediate outcome, and daily-roll marker survived reload byte-stably.",
                    new JObject { ["expected"] = expectedFingerprint, ["actual"] = actualFingerprint });
            }
            if (record != null && definition != null && target != null)
            {
                if (definition.Timed)
                {
                    bool active = record.IsActiveAt(CurrentDay());
                    bool contribution = KingdomEventContributionsMatch(kingdomEvents, definition, target, expectExpired ? 0f : definition.Magnitude);
                    AddKingdomEventAssertion(assertions, "kingdom_event_timed_duration_state", expectExpired
                        ? !active && CurrentDay() + 0.001f >= record.EndDay
                        : active && Math.Abs((record.EndDay - record.StartDay) - TimedEventDurationDays) < 0.001f,
                        "The timed event has the expected 30-day active or expired state.", null);
                    AddKingdomEventAssertion(assertions, "kingdom_event_production_modifier", contribution,
                        "The timed getter returns the exact configured contribution for the current target holdings.", snapshot["contributions"]);
#if !REIGN_EXCLUDE_VERIFICATION
                    JObject economy = KingdomEventNativeEconomy(kingdomEvents, ledger, record, definition, target, expectExpired);
                    AddKingdomEventAssertion(assertions, "kingdom_event_native_economy_model", economy.Value<bool>("passed"),
                        "The registered native economy model was sampled with the exact event present, omitted, and restored.", economy);
#endif
                }
                else
                {
                    bool immediate = false;
                    Kingdom secondary = FindKingdom(record.SecondaryKingdomStringId);
                    JObject before = JObject.Parse(string.IsNullOrWhiteSpace(ledger.BeforeSnapshotJson)
                        ? "{}" : ledger.BeforeSnapshotJson);
                    JArray priorWars = before["atWarWith"] as JArray ?? new JArray();
                    if (definition.Id == "border_crisis")
                    {
                        bool origin = ReignServices.AI?.WarOrigins.Any(x => x != null
                            && Same(x.OriginKind, "kingdom_event") && Same(x.SourceActionId, record.EventId)
                            && SamePair(x.AggressorKingdomStringId, x.DefenderKingdomStringId,
                                target.StringId, secondary?.StringId)) == true;
                        immediate = secondary != null && target.IsAtWarWith(secondary)
                            && !priorWars.Any(x => Same((string)x, secondary.StringId))
                            && !HasProtectedAgreement(target, secondary) && origin;
                    }
                    else if (definition.Id == "grand_reconciliation")
                        immediate = secondary != null && !target.IsAtWarWith(secondary)
                            && priorWars.Any(x => Same((string)x, secondary.StringId))
                            && (before["eligiblePeacePartnerIds"] as JArray)?.Any(x => Same((string)x, secondary.StringId)) == true;
                    else if (definition.Id == "orderly_succession")
                    {
                        Hero oldRuler = Hero.FindFirst(x => x != null && Same(x.StringId, ledger.OriginalRulerHeroStringId));
                        Hero heir = Hero.FindFirst(x => x != null && Same(x.StringId, record.HeirHeroStringId));
                        string priorRulingClanId = before.Value<string>("rulingClanId") ?? string.Empty;
                        string[] priorFiefs = (before["rulingClanFiefIds"] as JArray ?? new JArray())
                            .Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToArray();
                        string[] currentFiefs = target.RulingClan?.Fiefs.Select(x => x.StringId)
                            .Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToArray()
                            ?? new string[0];
                        immediate = target.Leader == heir && heir != null && heir.IsAlive && !heir.IsChild
                            && heir != Hero.MainHero && heir.Clan == target.RulingClan
                            && oldRuler != null && oldRuler.IsAlive
                            && target.RulingClan != Clan.PlayerClan
                            && Same(target.RulingClan?.StringId, priorRulingClanId)
                            && priorFiefs.SequenceEqual(currentFiefs, StringComparer.Ordinal);
                    }
                    AddKingdomEventAssertion(assertions, "kingdom_event_immediate_outcome", immediate && record.Status == "completed",
                        "The native war, peace, or player-safe NPC succession outcome matches the production record.", null);
                }
            }
#if !REIGN_EXCLUDE_VERIFICATION
            else if (definition?.Timed == true)
                AddKingdomEventAssertion(assertions, "kingdom_event_native_economy_model", false,
                    "An exact timed record and its current target are required for native economy evidence.", null);
#endif
            JObject result = KingdomEventTestResult(runId, verifyReload ? "verify_reload" : "observe", assertions);
            result["record"] = record == null ? JValue.CreateNull() : JObject.FromObject(record);
            result["before"] = JObject.Parse(string.IsNullOrWhiteSpace(ledger.BeforeSnapshotJson) ? "{}" : ledger.BeforeSnapshotJson);
            result["after"] = snapshot;
            result["worldHistoryCorrelationId"] = ledger.EventId;
            return result;
        }

        private static JObject NoTargetKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "no_target", saveError);
            if (!string.Equals((string)options["confirmation"], KingdomEventTestConfirmation, StringComparison.Ordinal))
                return KingdomEventTestFailure(runId, "no_target", "The exact disposable-save force confirmation is required.");
            string archetypeId = ((string)options["archetypeId"] ?? string.Empty).Trim().ToLowerInvariant();
            ReignKingdomEventDefinition definition = FindDefinition(archetypeId);
            if (definition == null)
                return KingdomEventTestFailure(runId, "no_target", "Unknown kingdom event archetype.");
            string missingKingdomId = ((string)options["kingdomId"] ?? string.Empty).Trim();
            if (missingKingdomId.Length == 0)
                missingKingdomId = "kingdom_event_no_target_fixture";
            int beforeCount = kingdomEvents._events.Count;
            int beforeMarker = kingdomEvents._lastRolledDay;
            HashSet<string> beforeIds = new HashSet<string>(kingdomEvents._events.Select(x => x.EventId), StringComparer.OrdinalIgnoreCase);
            bool forced = kingdomEvents.ForceEvent(definition.Id, missingKingdomId, out string forceResult);
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_refused", !forced,
                "The production force path refused an exact kingdom that was absent from the eligible set.",
                new JObject { ["archetypeId"] = definition.Id, ["kingdomId"] = missingKingdomId,
                    ["forceResult"] = forceResult });
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_no_record",
                kingdomEvents._events.Count == beforeCount && kingdomEvents._events.All(x => beforeIds.Contains(x.EventId)),
                "A no-target refusal created no production event record.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_marker_unchanged",
                kingdomEvents._lastRolledDay == beforeMarker,
                "A no-target refusal did not consume or rewrite the production daily-roll marker.", null);
            return KingdomEventTestResult(runId, "no_target", assertions);
        }

        private static JObject PrepareOrganicKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out _, out string saveError))
                return KingdomEventTestFailure(runId, "organic_prepare", saveError);
            ReignKingdomEventTestLedger existing = FindKingdomEventLedger(kingdomEvents, runId);
            if (existing != null)
            {
                JObject replay = new JObject
                {
                    ["ok"] = true, ["runId"] = runId, ["profile"] = "kingdom_event",
                    ["phase"] = "organic_prepare", ["idempotent"] = true,
                    ["prepare"] = JObject.Parse(existing.BeforeSnapshotJson ?? "{}")
                };
                return replay;
            }
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || ReignSaveSyncCoordinator.IsAlignmentPending
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || !ReignCampaignPreparationCampaignBehavior
                    .AreAutonomousWorldSystemsUnlocked(CurrentDay()))
            {
                return KingdomEventTestFailure(runId, "organic_prepare",
                    "Organic production readiness is not yet available; wait for campaign initialization and Save Sync alignment before arming the next-day trigger.");
            }
            int startDay = Math.Max(CurrentDayIndex() + 1, kingdomEvents._lastRolledDay + 1);
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            int targetDay = -1;
            bool beneficial = false;
            bool accelerated = options.Value<bool?>("accelerateOrganicTrigger") == true;
            if (accelerated)
            {
                targetDay = startDay;
                beneficial = ReignKingdomEventRollerCore.IsBeneficial(campaignId, targetDay);
                if (!Definitions.Where(x => x.Beneficial == beneficial).Any(x => kingdomEvents.EligibleKingdoms(x).Count > 0))
                    beneficial = !beneficial;
                if (!Definitions.Where(x => x.Beneficial == beneficial).Any(x => kingdomEvents.EligibleKingdoms(x).Count > 0))
                    return KingdomEventTestFailure(runId, "organic_prepare",
                        "No eligible polarity exists for the accelerated next-day organic observation.");
            }
            for (int day = startDay; !accelerated && day <= startDay + 400; day++)
            {
                if (!ReignKingdomEventRollerCore.IsDailyTrigger(campaignId, day))
                    continue;
                bool candidateBeneficial = ReignKingdomEventRollerCore.IsBeneficial(campaignId, day);
                bool hasEligibleAdapter = Definitions.Where(x => x.Beneficial == candidateBeneficial)
                    .Any(x => kingdomEvents.EligibleKingdoms(x).Count > 0);
                if (!hasEligibleAdapter)
                    continue;
                targetDay = day;
                beneficial = candidateBeneficial;
                break;
            }
            if (targetDay < 0)
                return KingdomEventTestFailure(runId, "organic_prepare",
                    "No eligible natural 1% trigger was found in the bounded 400-day production window.");
            int basisPoints = (int)(ReignKingdomEventRollerCore.StableHash(
                campaignId, targetDay, "daily_trigger") % ReignKingdomEventRollerCore.BasisPointScale);
            JObject prepare = new JObject
            {
                ["kind"] = "organic_prepare",
                ["expectedSaveName"] = expectedSaveName,
                ["gameInstanceId"] = options.Value<string>("gameInstanceId") ?? string.Empty,
                ["preparedDay"] = CurrentDayIndex(),
                ["targetDay"] = targetDay,
                ["daysToAdvance"] = targetDay - CurrentDayIndex(),
                ["expectedPolarity"] = beneficial ? "beneficial" : "harmful",
                ["triggerRollBasisPoints"] = basisPoints,
                ["accelerated"] = accelerated,
                ["productionDailyTriggerBasisPoints"] = ReignKingdomEventRollerCore.DailyTriggerBasisPoints,
                ["effectiveTestTriggerBasisPoints"] = accelerated
                    ? ReignKingdomEventRollerCore.BasisPointScale
                    : ReignKingdomEventRollerCore.DailyTriggerBasisPoints,
                ["eventIdsBefore"] = new JArray(kingdomEvents._events.Select(x => x.EventId))
            };
            var ledger = new ReignKingdomEventTestLedger
            {
                RunId = runId,
                CampaignId = campaignId,
                ArchetypeId = "organic",
                TargetVariant = "natural",
                EventId = string.Empty,
                PreviousLastRolledDay = kingdomEvents._lastRolledDay,
                StartedDay = CurrentDay(),
                BeforeSnapshotJson = prepare.ToString(Formatting.None),
                DisposableSaveConfirmed = true
            };
            kingdomEvents._testLedgers.Add(ledger);
            if (accelerated)
            {
                kingdomEvents._testOrganicTriggerDay = targetDay;
                kingdomEvents._testOrganicTriggerRunId = runId;
            }
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_unchanged_one_percent",
                basisPoints >= 0 && basisPoints < ReignKingdomEventRollerCore.BasisPointScale
                && ReignKingdomEventRollerCore.DailyTriggerBasisPoints == 100
                && ReignKingdomEventRollerCore.BasisPointScale == 10000,
                "The production model remains exactly 1%; any requested acceleration is isolated to one disposable-save daily tick.", prepare);
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_acceleration_guard",
                !accelerated || (kingdomEvents._testOrganicTriggerDay == targetDay
                    && Same(kingdomEvents._testOrganicTriggerRunId, runId) && targetDay == startDay),
                "The optional probability override is bound to this run and the next day only.", null);
            JObject result = KingdomEventTestResult(runId, "organic_prepare", assertions);
            result["prepare"] = prepare;
            return result;
        }

        private static JObject VerifyOrganicKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "organic_verify", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(kingdomEvents, runId);
            if (ledger == null || !Same(ledger.ArchetypeId, "organic"))
                return KingdomEventTestFailure(runId, "organic_verify", "No organic production observation was prepared.");
            JObject prepare = JObject.Parse(ledger.BeforeSnapshotJson ?? "{}");
            int targetDay = prepare.Value<int?>("targetDay") ?? -1;
            bool accelerated = prepare.Value<bool?>("accelerated") == true;
            HashSet<string> beforeIds = new HashSet<string>(
                (prepare["eventIdsBefore"] as JArray ?? new JArray()).Select(x => (string)x),
                StringComparer.OrdinalIgnoreCase);
            List<ReignKingdomEventRecord> natural = kingdomEvents._events.Where(x => x != null && !x.WasForced
                && x.RollDay == targetDay && !beforeIds.Contains(x.EventId)).ToList();
            ReignKingdomEventRecord record = natural.Count == 1 ? natural[0] : null;
            if (record != null)
            {
                ledger.EventId = record.EventId;
                ledger.ArchetypeId = record.ArchetypeId;
                ledger.KingdomStringId = record.KingdomStringId;
            }
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_record_exactly_once",
                natural.Count == 1 && record != null,
                "The ordinary daily tick created exactly one new non-forced production event on the predicted trigger day.",
                record == null ? null : JObject.FromObject(record));
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_roll_receipt",
                record != null && record.TriggerRollBasisPoints >= 0
                && record.TriggerRollBasisPoints < ReignKingdomEventRollerCore.BasisPointScale
                && (!accelerated || record.TriggerRollBasisPoints == prepare.Value<int>("triggerRollBasisPoints")),
                "The organic event retained its original stable production roll rather than a force sentinel.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_marker_advanced_once",
                kingdomEvents._lastRolledDay >= targetDay && kingdomEvents._lastRolledDay <= CurrentDayIndex(),
                "The ordinary production marker advanced monotonically through the natural trigger day.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_acceleration_consumed",
                !accelerated || (kingdomEvents._testOrganicTriggerDay == -1 && string.IsNullOrEmpty(kingdomEvents._testOrganicTriggerRunId)),
                "The next-day probability override was consumed before production selection and is no longer armed.", null);
            JObject result = KingdomEventTestResult(runId, "organic_verify", assertions);
            result["prepare"] = prepare;
            result["record"] = record == null ? JValue.CreateNull() : JObject.FromObject(record);
            result["worldHistoryCorrelationId"] = record?.EventId ?? string.Empty;
            return result;
        }

        private static JObject MarkKingdomEventReloadTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out string activeSaveName, out string saveError))
                return KingdomEventTestFailure(runId, "mark_reload", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(kingdomEvents, runId);
            ReignKingdomEventRecord record = ledger == null ? null
                : kingdomEvents._events.FirstOrDefault(x => Same(x.EventId, ledger.EventId));
            if (ledger == null || record == null)
                return KingdomEventTestFailure(runId, "mark_reload", "No forced event ledger and production record exist for reload preparation.");
            JObject marker = new JObject
            {
                ["kind"] = "reload_prepare",
                ["expectedSaveName"] = expectedSaveName,
                ["activeSaveName"] = activeSaveName,
                ["gameInstanceId"] = options.Value<string>("gameInstanceId") ?? string.Empty,
                ["featureFingerprint"] = KingdomEventFeatureFingerprint(kingdomEvents, ledger, record),
                ["eventId"] = record.EventId,
                ["preparedUtc"] = DateTime.UtcNow.ToString("o")
            };
            ledger.ObservationJson.RemoveAll(x =>
            {
                try { return Same(JObject.Parse(x).Value<string>("kind"), "reload_prepare"); }
                catch { return false; }
            });
            ledger.ObservationJson.Add(marker.ToString(Formatting.None));
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_reload_marker_staged",
                marker.Value<string>("featureFingerprint")?.Length == 64
                && marker.Value<string>("gameInstanceId")?.Length > 0,
                "Reload preparation staged only a save-backed feature fingerprint and native game-instance identity.", marker);
            AddKingdomEventAssertion(assertions, "kingdom_event_reload_no_fixed_save",
                string.Equals(expectedSaveName, activeSaveName, StringComparison.OrdinalIgnoreCase),
                "The feature profile created no fixed save; the guarded campaign-test Current owns checkpointing.", null);
            JObject result = KingdomEventTestResult(runId, "mark_reload", assertions);
            result["reloadMarker"] = marker;
            return result;
        }

        private static JObject CleanupKingdomEventTest(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "cleanup_marker", saveError);
            bool accelerationCleared = Same(kingdomEvents._testOrganicTriggerRunId, runId);
            if (accelerationCleared)
            {
                kingdomEvents._testOrganicTriggerDay = -1;
                kingdomEvents._testOrganicTriggerRunId = string.Empty;
            }
            int removed = kingdomEvents._testLedgers.RemoveAll(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
            return new JObject
            {
                ["ok"] = true, ["runId"] = runId, ["profile"] = "kingdom_event", ["phase"] = "cleanup_marker",
                ["ledgersRemoved"] = removed, ["organicAccelerationCleared"] = accelerationCleared,
                ["requiresSaveRollback"] = true,
                ["message"] = "Restore or delete only the exact guarded campaign-test Current to undo the production event; the enrolled baseline remains immutable."
            };
        }

        private static Kingdom SelectKingdomEventTestTarget(List<Kingdom> eligible, string variant, string exactKingdomId)
        {
            if (eligible == null || eligible.Count == 0) return null;
            if (!string.IsNullOrWhiteSpace(exactKingdomId))
                return eligible.FirstOrDefault(x => string.Equals(x.StringId, exactKingdomId, StringComparison.OrdinalIgnoreCase));
            List<Kingdom> ordered = eligible.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            if (variant == "last") return ordered[ordered.Count - 1];
            if (variant == "middle") return ordered[ordered.Count / 2];
            return ordered[0];
        }

        private static JObject KingdomEventTestSnapshot(ReignKingdomEventsCampaignBehavior kingdomEvents, Kingdom kingdom)
        {
            if (kingdom == null) return new JObject();
            List<Town> towns = Town.AllFiefs.Where(x => x?.OwnerClan?.Kingdom == kingdom).OrderBy(x => x.StringId).ToList();
            List<Village> villages = Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                .Select(x => x.Village).Where(x => x != null).OrderBy(x => x.StringId).ToList();
            return new JObject
            {
                ["worldDay"] = CurrentDay(), ["kingdomId"] = kingdom.StringId,
                ["kingdomName"] = kingdom.Name?.ToString() ?? kingdom.StringId,
                ["rulerId"] = kingdom.Leader?.StringId ?? string.Empty,
                ["rulerAlive"] = kingdom.Leader?.IsAlive == true,
                ["rulingClanId"] = kingdom.RulingClan?.StringId ?? string.Empty,
                ["rulingClanFiefIds"] = new JArray(kingdom.RulingClan?.Fiefs
                    .Select(x => x.StringId).OrderBy(x => x) ?? Enumerable.Empty<string>()),
                ["atWarWith"] = new JArray(Kingdom.All.Where(x => x != null && x != kingdom && kingdom.IsAtWarWith(x)).Select(x => x.StringId)),
                ["eligibleWarPartnerIds"] = new JArray(kingdomEvents.EligibleWarPartners(kingdom).Select(x => x.StringId)),
                ["eligiblePeacePartnerIds"] = new JArray(kingdomEvents.EligiblePeacePartners(kingdom).Select(x => x.StringId)),
                ["eligibleHeirIds"] = new JArray(EligibleHeirs(kingdom).Select(x => x.Key.StringId)),
                ["towns"] = new JArray(towns.Select(town => new JObject
                {
                    ["townId"] = town.StringId, ["settlementId"] = town.Settlement?.StringId ?? string.Empty,
                    ["food"] = town.FoodStocks, ["security"] = town.Security, ["loyalty"] = town.Loyalty,
                    ["prosperity"] = town.Prosperity
                })),
                ["villages"] = new JArray(villages.Select(village => new JObject
                {
                    ["villageId"] = village.StringId, ["hearth"] = village.Hearth,
                    ["boundSettlementId"] = village.Bound?.StringId ?? string.Empty
                })),
                ["contributions"] = new JObject
                {
                    ["food"] = new JArray(towns.Select(x => kingdomEvents.GetFoodProductionFactor(x))),
                    ["hearth"] = new JArray(villages.Select(x => kingdomEvents.GetHearthDelta(x))),
                    ["security"] = new JArray(towns.Select(x => kingdomEvents.GetSecurityDelta(x))),
                    ["prosperity"] = new JArray(towns.Select(x => kingdomEvents.GetProsperityDelta(x))),
                    ["construction"] = new JArray(towns.Select(x => kingdomEvents.GetConstructionFactor(x))),
                    ["loyalty"] = new JArray(towns.Select(x => kingdomEvents.GetLoyaltyDelta(x)))
                }
            };
        }

#if !REIGN_EXCLUDE_VERIFICATION
        private static (float Current, float WithoutEvent, float Restored) SampleNativeEconomy(
            ReignKingdomEventsCampaignBehavior owner, ReignKingdomEventRecord record, Func<float> read)
        {
            float current = ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(read);
            float without = owner.ReadEconomyWithTimedEventOmitted(record,
                () => ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(read));
            return (current, without, ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(read));
        }

        private static JObject KingdomEventNativeEconomy(ReignKingdomEventsCampaignBehavior owner,
            ReignKingdomEventTestLedger ledger, ReignKingdomEventRecord record,
            ReignKingdomEventDefinition definition, Kingdom target, bool expectExpired)
        {
            var models = TaleWorlds.CampaignSystem.Campaign.Current?.Models;
            if (models == null || !ReferenceEquals(ReignServices.KingdomEvents, owner)
                || !record.WasForced || !Same(ledger.EventId, record.EventId)
                || !Same(record.ArchetypeId, definition.Id) || !Same(record.KingdomStringId, target.StringId)
                || !Same(ledger.CampaignId, ReignCampaignIdentity.CurrentCampaignId())
                || owner._events.Count(x => x != null && Same(x.EventId, record.EventId)) != 1)
                return new JObject { ["passed"] = false, ["reason"] = "missing_model_or_run_identity" };

            float day = CurrentDay();
            if (record.IsActiveAt(day) && owner._events.Any(x => x != null && !ReferenceEquals(x, record)
                && x.IsActiveAt(day) && Same(x.KingdomStringId, record.KingdomStringId)))
                return new JObject { ["passed"] = false, ["reason"] = "overlapping_timed_event" };

            var towns = Town.AllFiefs.Where(x => x?.OwnerClan?.Kingdom == target)
                .OrderBy(x => x.StringId, StringComparer.Ordinal);
            var villages = Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == target)
                .Select(x => x.Village).Where(x => x != null).OrderBy(x => x.StringId, StringComparer.Ordinal);
            float expected = expectExpired ? 0f : definition.Magnitude;

            switch (definition.Id)
            {
                case "famine": case "bountiful_harvest":
                    return WithRegisteredEconomyModel(models.SettlementFoodModel, typeof(ReignSettlementFoodModel), null,
                        food => NativeEconomyEvidence(owner, record, expected, towns, x => x.StringId,
                            x => food.CalculateTownFoodStocksChange(x, false, false).ResultNumber,
                            nameof(food.CalculateTownFoodStocksChange)));
                case "pestilence": case "population_boom":
                    return WithRegisteredEconomyModel(models.SettlementProsperityModel, typeof(ReignSettlementProsperityModel),
                        "NavalDLCSettlementProsperityModel", hearth => NativeEconomyEvidence(owner, record, expected, villages,
                            x => x.StringId, x => hearth.CalculateHearthChange(x, false).ResultNumber,
                            nameof(hearth.CalculateHearthChange), x => x.Hearth));
                case "crime_wave": case "law_and_order":
                    return WithRegisteredEconomyModel(models.SettlementSecurityModel, typeof(ReignSettlementSecurityModel),
                        "NavalDLCSettlementSecurityModel", security => NativeEconomyEvidence(owner, record, expected, towns,
                            x => x.StringId, x => security.CalculateSecurityChange(x, false).ResultNumber,
                            nameof(security.CalculateSecurityChange)));
                case "trade_collapse": case "trade_boom":
                    return WithRegisteredEconomyModel(models.SettlementProsperityModel, typeof(ReignSettlementProsperityModel),
                        "NavalDLCSettlementProsperityModel", prosperity => NativeEconomyEvidence(owner, record, expected, towns,
                            x => x.StringId, x => prosperity.CalculateProsperityChange(x, false).ResultNumber,
                            nameof(prosperity.CalculateProsperityChange)));
                case "construction_stagnation": case "golden_age":
                    return WithRegisteredEconomyModel(models.BuildingConstructionModel, typeof(ReignBuildingConstructionModel),
                        "NavalDLCBuildingConstructionModel", construction => NativeEconomyEvidence(owner, record, expected, towns,
                            x => x.StringId, x => construction.CalculateDailyConstructionPower(x, false).ResultNumber,
                            nameof(construction.CalculateDailyConstructionPower)));
                case "political_unrest": case "national_unity":
                    return WithRegisteredEconomyModel(models.SettlementLoyaltyModel, typeof(ReignSettlementLoyaltyModel), null,
                        loyalty => NativeEconomyEvidence(owner, record, expected, towns, x => x.StringId,
                            x => loyalty.CalculateLoyaltyChange(x, false).ResultNumber,
                            nameof(loyalty.CalculateLoyaltyChange)));
            }
            return new JObject { ["passed"] = false, ["reason"] = "registered_economy_adapter_unavailable" };
        }

        private static JObject WithRegisteredEconomyModel<TModel>(TModel registered, Type exactReignType,
            string reviewedNavalWrapper, Func<TModel, JObject> sample)
        {
            JObject route = ReignEconomyPatches.ModelRoute(registered, exactReignType, reviewedNavalWrapper);
            if (route.Value<bool>("healthy") != true)
                return new JObject { ["passed"] = false, ["reason"] = "registered_economy_adapter_unavailable",
                    ["modelRoute"] = route };

            // Call through the model the campaign actually registered. The underlying Reign
            // instance establishes route eligibility and must never replace the outer sample target.
            JObject evidence = sample(registered);
            evidence["modelRoute"] = route;
            return evidence;
        }

        private static JObject NativeEconomyEvidence<T>(ReignKingdomEventsCampaignBehavior owner,
            ReignKingdomEventRecord record, float expected, IEnumerable<T> holdings, Func<T, string> id,
            Func<T, float> read, string model, Func<T, float> hearth = null)
        {
            var samples = new JArray();
            int observed = 0;
            bool consistent = true;
            foreach (T holding in holdings)
            {
                string status;
                var sample = new JObject { ["holdingId"] = id(holding) };
                try
                {
                    var (current, without, restored) = SampleNativeEconomy(owner, record, () => read(holding));
                    status = NativeEconomyOutcome(current, without, restored, expected, hearth?.Invoke(holding));
                    if (status != "non_finite")
                    {
                        sample["current"] = current;
                        sample["withoutEvent"] = without;
                        sample["restored"] = restored;
                        sample["difference"] = current - without;
                    }
                }
                catch (Exception ex)
                {
                    status = "measurement_failed";
                    sample["errorType"] = ex.GetType().Name;
                }
                sample["status"] = status;
                samples.Add(sample);
                if (status == "observed") observed++;
                consistent &= status == "observed" || status == "unchanged" || status == "hearth_floor_limited";
            }
            return new JObject
            {
                ["passed"] = samples.Count > 0 && consistent && (expected == 0f || observed > 0),
                ["eventId"] = record.EventId,
                ["model"] = model,
                ["expectedDirection"] = Math.Sign(expected),
                ["observedCount"] = observed,
                ["samples"] = samples
            };
        }

        private static string NativeEconomyOutcome(float current, float without, float restored, float expected,
            float? hearth)
        {
            const float tolerance = 0.0001f;
            float difference = current - without;
            if (float.IsNaN(difference) || float.IsInfinity(difference) || float.IsNaN(restored)
                || float.IsInfinity(restored))
                return "non_finite";
            if (Math.Abs(current - restored) > tolerance)
                return "unstable";
            if (Math.Abs(difference) <= tolerance)
                return "unchanged";
            if (expected != 0f && Math.Sign(difference) == Math.Sign(expected))
                return "observed";
            // Pestilence caps the event's loss at the hearth floor. The uncapped ordinary
            // loss can be greater even though the event still behaves as configured.
            if (expected < 0f && hearth.HasValue)
            {
                float floor = Math.Min(0f, ReignEconomyCampaignBehavior.MinimumVillageHearth - hearth.Value);
                if (without < floor && Math.Abs(current - floor) <= tolerance)
                    return "hearth_floor_limited";
            }
            return "unexpected_direction";
        }
#endif

        private static bool KingdomEventContributionsMatch(ReignKingdomEventsCampaignBehavior kingdomEvents, ReignKingdomEventDefinition definition, Kingdom kingdom, float expected)
        {
            const float tolerance = 0.0001f;
            List<Town> towns = Town.AllFiefs.Where(x => x?.OwnerClan?.Kingdom == kingdom).ToList();
            List<Village> villages = Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                .Select(x => x.Village).Where(x => x != null).ToList();
            switch (definition.Id)
            {
                case "famine": case "bountiful_harvest":
                    return towns.Count > 0 && towns.All(x => Math.Abs(kingdomEvents.GetFoodProductionFactor(x) - expected) < tolerance);
                case "pestilence": case "population_boom":
                    return villages.Count > 0 && villages.All(x => Math.Abs(kingdomEvents.GetHearthDelta(x) - expected) < tolerance);
                case "crime_wave": case "law_and_order":
                    return towns.Count > 0 && towns.All(x => Math.Abs(kingdomEvents.GetSecurityDelta(x) - expected) < tolerance);
                case "trade_collapse": case "trade_boom":
                    return towns.Count > 0 && towns.All(x => Math.Abs(kingdomEvents.GetProsperityDelta(x) - expected) < tolerance);
                case "construction_stagnation": case "golden_age":
                    return towns.Count > 0 && towns.All(x => Math.Abs(kingdomEvents.GetConstructionFactor(x) - expected) < tolerance);
                case "political_unrest": case "national_unity":
                    return towns.Count > 0 && towns.All(x => Math.Abs(kingdomEvents.GetLoyaltyDelta(x) - expected) < tolerance);
                default: return false;
            }
        }

        private static ReignKingdomEventTestLedger FindKingdomEventLedger(ReignKingdomEventsCampaignBehavior kingdomEvents, string runId)
        {
            return kingdomEvents._testLedgers.FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryRequireKingdomEventTestSave(JObject options,
            out string expectedSaveName, out string activeSaveName, out string error)
        {
            expectedSaveName = (options?.Value<string>("expectedSaveName") ?? string.Empty).Trim();
            activeSaveName = ReignServerClient.ActiveNativeSaveName().Trim();
            error = string.Empty;
            if (expectedSaveName.Length == 0)
            {
                error = "The exact MCP-authorized campaign-test Current save name is required.";
                return false;
            }
            if (activeSaveName.Length == 0)
            {
                error = "Bannerlord did not expose an active loaded save identity.";
                return false;
            }
            if (!string.Equals(expectedSaveName, activeSaveName, StringComparison.OrdinalIgnoreCase))
            {
                error = "The active Bannerlord save does not match the exact MCP-authorized campaign-test Current.";
                return false;
            }
            return true;
        }

        private static JObject FindKingdomEventReloadMarker(ReignKingdomEventTestLedger ledger)
        {
            if (ledger?.ObservationJson == null)
                return null;
            for (int index = ledger.ObservationJson.Count - 1; index >= 0; index--)
            {
                try
                {
                    JObject candidate = JObject.Parse(ledger.ObservationJson[index]);
                    if (Same(candidate.Value<string>("kind"), "reload_prepare"))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        private static string KingdomEventFeatureFingerprint(ReignKingdomEventsCampaignBehavior kingdomEvents, ReignKingdomEventTestLedger ledger,
            ReignKingdomEventRecord record)
        {
            var payload = new JObject
            {
                ["campaignId"] = ledger?.CampaignId ?? string.Empty,
                ["runId"] = ledger?.RunId ?? string.Empty,
                ["archetypeId"] = ledger?.ArchetypeId ?? string.Empty,
                ["targetVariant"] = ledger?.TargetVariant ?? string.Empty,
                ["eventId"] = ledger?.EventId ?? string.Empty,
                ["kingdomId"] = ledger?.KingdomStringId ?? string.Empty,
                ["originalRulerId"] = ledger?.OriginalRulerHeroStringId ?? string.Empty,
                ["previousLastRolledDay"] = ledger?.PreviousLastRolledDay ?? -1,
                ["startedDay"] = ledger?.StartedDay ?? -1f,
                ["beforeSnapshot"] = ledger?.BeforeSnapshotJson ?? "{}",
                ["record"] = record == null ? JValue.CreateNull() : JObject.FromObject(record),
                ["lastRolledDay"] = kingdomEvents._lastRolledDay,
                ["recordCount"] = record == null ? 0 : kingdomEvents._events.Count(x => Same(x.EventId, record.EventId))
            };
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void AddKingdomEventAssertion(JArray assertions, string id, bool passed, string summary, JToken data)
        {
            assertions.Add(new JObject { ["caseId"] = id, ["passed"] = passed, ["summary"] = summary,
                ["data"] = data ?? JValue.CreateNull() });
        }

        private static JObject KingdomEventTestResult(string runId, string phase, JArray assertions)
        {
            int passed = assertions.Count(x => (bool?)x["passed"] == true);
            return new JObject
            {
                ["ok"] = passed == assertions.Count, ["runId"] = runId, ["profile"] = "kingdom_event", ["phase"] = phase,
                ["passedCount"] = passed, ["failedCount"] = assertions.Count - passed,
                ["totalCount"] = assertions.Count, ["assertions"] = assertions
            };
        }

        private static JObject KingdomEventTestFailure(string runId, string phase, string error)
        {
            return new JObject { ["ok"] = false, ["runId"] = runId, ["profile"] = "kingdom_event",
                ["phase"] = phase, ["error"] = error ?? "Kingdom-event test phase failed." };
        }
    }
}
