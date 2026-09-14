using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Shared;
using ReignBeta.Events;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignKingdomEventsCampaignBehavior
    {
        private const string KingdomEventTestConfirmation = "force Reign kingdom event on disposable save";

        internal JObject RunKingdomEventTestProfile(string runId, string phase, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string normalizedPhase = (phase ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            switch (normalizedPhase)
            {
                case "preflight": return KingdomEventTestPreflight(normalizedRunId, options);
                case "force": return ForceKingdomEventTest(normalizedRunId, options);
                case "observe": return ObserveKingdomEventTest(normalizedRunId, options, false);
                case "verify_reload": return ObserveKingdomEventTest(normalizedRunId, options, true);
                case "mark_reload": return MarkKingdomEventReloadTest(normalizedRunId, options);
                case "no_target": return NoTargetKingdomEventTest(normalizedRunId, options);
                case "organic_prepare": return PrepareOrganicKingdomEventTest(normalizedRunId, options);
                case "organic_verify": return VerifyOrganicKingdomEventTest(normalizedRunId, options);
                case "cleanup_marker": return CleanupKingdomEventTest(normalizedRunId, options);
                default: return KingdomEventTestFailure(normalizedRunId, normalizedPhase, "Unsupported kingdom-event test phase.");
            }
        }

        private JObject KingdomEventTestPreflight(string runId, JObject options)
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
                ["eligibleTargets"] = new JArray(EligibleKingdoms(definition).Select(kingdom => new JObject
                {
                    ["kingdomId"] = kingdom.StringId,
                    ["name"] = kingdom.Name?.ToString() ?? kingdom.StringId,
                    ["isPlayerKingdom"] = kingdom.RulingClan == Clan.PlayerClan,
                    ["fiefCount"] = kingdom.Fiefs.Count
                }))
            }));
            JObject result = KingdomEventTestResult(runId, "preflight", assertions);
            result["catalog"] = catalog;
            result["lastRolledDay"] = _lastRolledDay;
            result["activeTimedEvents"] = new JArray(_events.Where(x => x?.IsActiveAt(CurrentDay()) == true).Select(x => JObject.FromObject(x)));
            result["safety"] = "Force only after loading a verified disposable baseline; immediate events change diplomacy or succession.";
            return result;
        }

        private JObject ForceKingdomEventTest(string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "force", saveError);
            if (!string.Equals((string)options["confirmation"], KingdomEventTestConfirmation, StringComparison.Ordinal))
                return KingdomEventTestFailure(runId, "force", "The exact disposable-save force confirmation is required.");
            ReignKingdomEventTestLedger replay = FindKingdomEventLedger(runId);
            if (replay != null)
            {
                JObject existing = ObserveKingdomEventTest(runId, options, false);
                existing["idempotent"] = true;
                return existing;
            }

            string archetypeId = ((string)options["archetypeId"] ?? string.Empty).Trim().ToLowerInvariant();
            string targetVariant = ((string)options["targetVariant"] ?? "first").Trim().ToLowerInvariant();
            ReignKingdomEventDefinition definition = FindDefinition(archetypeId);
            if (definition == null) return KingdomEventTestFailure(runId, "force", "Unknown kingdom event archetype.");
            List<Kingdom> eligible = EligibleKingdoms(definition);
            List<Kingdom> orderedEligible = eligible.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            Kingdom target = SelectKingdomEventTestTarget(eligible, targetVariant, (string)options["kingdomId"]);
            if (target == null) return KingdomEventTestFailure(runId, "force",
                "No eligible " + targetVariant + " target exists for " + definition.Title + ".");

            HashSet<string> beforeIds = new HashSet<string>(_events.Select(x => x.EventId), StringComparer.OrdinalIgnoreCase);
            int previousLastRolledDay = _lastRolledDay;
            JObject before = KingdomEventTestSnapshot(target);
            before["eligibleTargetOrder"] = new JArray(orderedEligible.Select(x => x.StringId));
            int expectedTargetIndex = targetVariant == "last" ? orderedEligible.Count - 1
                : targetVariant == "middle" ? orderedEligible.Count / 2 : 0;
            before["selectedTargetIndex"] = expectedTargetIndex;
            string originalRuler = target.Leader?.StringId ?? string.Empty;
            bool forced = ForceEvent(definition.Id, target.StringId, out string forceResult);
            ReignKingdomEventRecord record = _events.LastOrDefault(x => !beforeIds.Contains(x.EventId));
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
            _testLedgers.Add(ledger);
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
            AddKingdomEventAssertion(assertions, "kingdom_event_daily_roll_untouched", _lastRolledDay == previousLastRolledDay
                && record.TriggerRollBasisPoints == -1,
                "Forcing the test event did not consume, advance, or rewrite the production daily roll.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_record_exactly_once", _events.Count(x => x.EventId == record.EventId) == 1,
                "The event ID occurs exactly once.", null);
            JObject result = KingdomEventTestResult(runId, "force", assertions);
            result["record"] = JObject.FromObject(record);
            result["before"] = before;
            result["after"] = KingdomEventTestSnapshot(target);
            result["forceResult"] = forceResult;
            result["requiresAnnouncementScreenshot"] = true;
            result["worldHistoryCorrelationId"] = record.EventId;
            return result;
        }

        private JObject ObserveKingdomEventTest(string runId, JObject options, bool verifyReload)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out string activeSaveName, out string saveError))
                return KingdomEventTestFailure(runId, verifyReload ? "verify_reload" : "observe", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return KingdomEventTestFailure(runId, verifyReload ? "verify_reload" : "observe", "No persisted forced-event ledger exists for this run.");
            ReignKingdomEventRecord record = _events.FirstOrDefault(x => string.Equals(x.EventId, ledger.EventId, StringComparison.OrdinalIgnoreCase));
            Kingdom target = FindKingdom(ledger.KingdomStringId);
            ReignKingdomEventDefinition definition = FindDefinition(ledger.ArchetypeId);
            JObject snapshot = KingdomEventTestSnapshot(target);
            snapshot["capturedUtc"] = DateTime.UtcNow.ToString("o");
            ledger.ObservationJson.Add(snapshot.ToString(Formatting.None));
            if (ledger.ObservationJson.Count > 64) ledger.ObservationJson.RemoveRange(0, ledger.ObservationJson.Count - 64);

            bool expectExpired = (bool?)options["expectExpired"] == true;
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_ledger_persisted", !verifyReload
                || string.Equals(ledger.CampaignId, ReignCampaignIdentity.CurrentCampaignId(), StringComparison.OrdinalIgnoreCase),
                "The forced-event ledger belongs to this campaign and survived the requested save/load boundary.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_record_persisted", record != null
                && _events.Count(x => x.EventId == ledger.EventId) == 1,
                "The production event record remains present exactly once.", record == null ? null : JObject.FromObject(record));
            AddKingdomEventAssertion(assertions, "kingdom_event_daily_roll_marker_monotonic",
                _lastRolledDay >= ledger.PreviousLastRolledDay && _lastRolledDay <= CurrentDayIndex(),
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
                string actualFingerprint = KingdomEventFeatureFingerprint(ledger, record);
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
                    bool contribution = KingdomEventContributionsMatch(definition, target, expectExpired ? 0f : definition.Magnitude);
                    AddKingdomEventAssertion(assertions, "kingdom_event_timed_duration_state", expectExpired
                        ? !active && CurrentDay() + 0.001f >= record.EndDay
                        : active && Math.Abs((record.EndDay - record.StartDay) - TimedEventDurationDays) < 0.001f,
                        "The timed event has the expected 30-day active or expired state.", null);
                    AddKingdomEventAssertion(assertions, "kingdom_event_production_modifier", contribution,
                        "Every current applicable holding in the target kingdom receives the exact production model contribution, including ownership-following lookup.", snapshot["contributions"]);
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
                        bool origin = ReignAICampaignBehavior.Instance?.WarOrigins.Any(x => x != null
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
            JObject result = KingdomEventTestResult(runId, verifyReload ? "verify_reload" : "observe", assertions);
            result["record"] = record == null ? JValue.CreateNull() : JObject.FromObject(record);
            result["before"] = JObject.Parse(string.IsNullOrWhiteSpace(ledger.BeforeSnapshotJson) ? "{}" : ledger.BeforeSnapshotJson);
            result["after"] = snapshot;
            result["worldHistoryCorrelationId"] = ledger.EventId;
            return result;
        }

        private JObject NoTargetKingdomEventTest(string runId, JObject options)
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
            int beforeCount = _events.Count;
            int beforeMarker = _lastRolledDay;
            HashSet<string> beforeIds = new HashSet<string>(_events.Select(x => x.EventId), StringComparer.OrdinalIgnoreCase);
            bool forced = ForceEvent(definition.Id, missingKingdomId, out string forceResult);
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_refused", !forced,
                "The production force path refused an exact kingdom that was absent from the eligible set.",
                new JObject { ["archetypeId"] = definition.Id, ["kingdomId"] = missingKingdomId,
                    ["forceResult"] = forceResult });
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_no_record",
                _events.Count == beforeCount && _events.All(x => beforeIds.Contains(x.EventId)),
                "A no-target refusal created no production event record.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_no_target_marker_unchanged",
                _lastRolledDay == beforeMarker,
                "A no-target refusal did not consume or rewrite the production daily-roll marker.", null);
            return KingdomEventTestResult(runId, "no_target", assertions);
        }

        private JObject PrepareOrganicKingdomEventTest(string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out _, out string saveError))
                return KingdomEventTestFailure(runId, "organic_prepare", saveError);
            ReignKingdomEventTestLedger existing = FindKingdomEventLedger(runId);
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
            int startDay = Math.Max(CurrentDayIndex() + 1, _lastRolledDay + 1);
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            int targetDay = -1;
            bool beneficial = false;
            bool accelerated = options.Value<bool?>("accelerateOrganicTrigger") == true;
            if (accelerated)
            {
                targetDay = startDay;
                beneficial = ReignKingdomEventRollerCore.IsBeneficial(campaignId, targetDay);
                if (!Definitions.Where(x => x.Beneficial == beneficial).Any(x => EligibleKingdoms(x).Count > 0))
                    beneficial = !beneficial;
                if (!Definitions.Where(x => x.Beneficial == beneficial).Any(x => EligibleKingdoms(x).Count > 0))
                    return KingdomEventTestFailure(runId, "organic_prepare",
                        "No eligible polarity exists for the accelerated next-day organic observation.");
            }
            for (int day = startDay; !accelerated && day <= startDay + 400; day++)
            {
                if (!ReignKingdomEventRollerCore.IsDailyTrigger(campaignId, day))
                    continue;
                bool candidateBeneficial = ReignKingdomEventRollerCore.IsBeneficial(campaignId, day);
                bool hasEligibleAdapter = Definitions.Where(x => x.Beneficial == candidateBeneficial)
                    .Any(x => EligibleKingdoms(x).Count > 0);
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
                ["eventIdsBefore"] = new JArray(_events.Select(x => x.EventId))
            };
            var ledger = new ReignKingdomEventTestLedger
            {
                RunId = runId,
                CampaignId = campaignId,
                ArchetypeId = "organic",
                TargetVariant = "natural",
                EventId = string.Empty,
                PreviousLastRolledDay = _lastRolledDay,
                StartedDay = CurrentDay(),
                BeforeSnapshotJson = prepare.ToString(Formatting.None),
                DisposableSaveConfirmed = true
            };
            _testLedgers.Add(ledger);
            if (accelerated)
            {
                _testOrganicTriggerDay = targetDay;
                _testOrganicTriggerRunId = runId;
            }
            JArray assertions = new JArray();
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_unchanged_one_percent",
                basisPoints >= 0 && basisPoints < ReignKingdomEventRollerCore.BasisPointScale
                && ReignKingdomEventRollerCore.DailyTriggerBasisPoints == 100
                && ReignKingdomEventRollerCore.BasisPointScale == 10000,
                "The production model remains exactly 1%; any requested acceleration is isolated to one disposable-save daily tick.", prepare);
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_acceleration_guard",
                !accelerated || (_testOrganicTriggerDay == targetDay
                    && Same(_testOrganicTriggerRunId, runId) && targetDay == startDay),
                "The optional probability override is bound to this run and the next day only.", null);
            JObject result = KingdomEventTestResult(runId, "organic_prepare", assertions);
            result["prepare"] = prepare;
            return result;
        }

        private JObject VerifyOrganicKingdomEventTest(string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "organic_verify", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(runId);
            if (ledger == null || !Same(ledger.ArchetypeId, "organic"))
                return KingdomEventTestFailure(runId, "organic_verify", "No organic production observation was prepared.");
            JObject prepare = JObject.Parse(ledger.BeforeSnapshotJson ?? "{}");
            int targetDay = prepare.Value<int?>("targetDay") ?? -1;
            bool accelerated = prepare.Value<bool?>("accelerated") == true;
            HashSet<string> beforeIds = new HashSet<string>(
                (prepare["eventIdsBefore"] as JArray ?? new JArray()).Select(x => (string)x),
                StringComparer.OrdinalIgnoreCase);
            List<ReignKingdomEventRecord> natural = _events.Where(x => x != null && !x.WasForced
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
                _lastRolledDay >= targetDay && _lastRolledDay <= CurrentDayIndex(),
                "The ordinary production marker advanced monotonically through the natural trigger day.", null);
            AddKingdomEventAssertion(assertions, "kingdom_event_organic_acceleration_consumed",
                !accelerated || (_testOrganicTriggerDay == -1 && string.IsNullOrEmpty(_testOrganicTriggerRunId)),
                "The next-day probability override was consumed before production selection and is no longer armed.", null);
            JObject result = KingdomEventTestResult(runId, "organic_verify", assertions);
            result["prepare"] = prepare;
            result["record"] = record == null ? JValue.CreateNull() : JObject.FromObject(record);
            result["worldHistoryCorrelationId"] = record?.EventId ?? string.Empty;
            return result;
        }

        private JObject MarkKingdomEventReloadTest(string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out string expectedSaveName,
                out string activeSaveName, out string saveError))
                return KingdomEventTestFailure(runId, "mark_reload", saveError);
            ReignKingdomEventTestLedger ledger = FindKingdomEventLedger(runId);
            ReignKingdomEventRecord record = ledger == null ? null
                : _events.FirstOrDefault(x => Same(x.EventId, ledger.EventId));
            if (ledger == null || record == null)
                return KingdomEventTestFailure(runId, "mark_reload", "No forced event ledger and production record exist for reload preparation.");
            JObject marker = new JObject
            {
                ["kind"] = "reload_prepare",
                ["expectedSaveName"] = expectedSaveName,
                ["activeSaveName"] = activeSaveName,
                ["gameInstanceId"] = options.Value<string>("gameInstanceId") ?? string.Empty,
                ["featureFingerprint"] = KingdomEventFeatureFingerprint(ledger, record),
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

        private JObject CleanupKingdomEventTest(string runId, JObject options)
        {
            if (!TryRequireKingdomEventTestSave(options, out _, out _, out string saveError))
                return KingdomEventTestFailure(runId, "cleanup_marker", saveError);
            bool accelerationCleared = Same(_testOrganicTriggerRunId, runId);
            if (accelerationCleared)
            {
                _testOrganicTriggerDay = -1;
                _testOrganicTriggerRunId = string.Empty;
            }
            int removed = _testLedgers.RemoveAll(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
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

        private JObject KingdomEventTestSnapshot(Kingdom kingdom)
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
                ["eligibleWarPartnerIds"] = new JArray(EligibleWarPartners(kingdom).Select(x => x.StringId)),
                ["eligiblePeacePartnerIds"] = new JArray(EligiblePeacePartners(kingdom).Select(x => x.StringId)),
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
                    ["food"] = new JArray(towns.Select(x => GetFoodProductionFactor(x))),
                    ["hearth"] = new JArray(villages.Select(x => GetHearthDelta(x))),
                    ["security"] = new JArray(towns.Select(x => GetSecurityDelta(x))),
                    ["prosperity"] = new JArray(towns.Select(x => GetProsperityDelta(x))),
                    ["construction"] = new JArray(towns.Select(x => GetConstructionFactor(x))),
                    ["loyalty"] = new JArray(towns.Select(x => GetLoyaltyDelta(x)))
                }
            };
        }

        private bool KingdomEventContributionsMatch(ReignKingdomEventDefinition definition, Kingdom kingdom, float expected)
        {
            const float tolerance = 0.0001f;
            List<Town> towns = Town.AllFiefs.Where(x => x?.OwnerClan?.Kingdom == kingdom).ToList();
            List<Village> villages = Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                .Select(x => x.Village).Where(x => x != null).ToList();
            switch (definition.Id)
            {
                case "famine": case "bountiful_harvest":
                    return towns.Count > 0 && towns.All(x => Math.Abs(GetFoodProductionFactor(x) - expected) < tolerance);
                case "pestilence": case "population_boom":
                    return villages.Count > 0 && villages.All(x => Math.Abs(GetHearthDelta(x) - expected) < tolerance);
                case "crime_wave": case "law_and_order":
                    return towns.Count > 0 && towns.All(x => Math.Abs(GetSecurityDelta(x) - expected) < tolerance);
                case "trade_collapse": case "trade_boom":
                    return towns.Count > 0 && towns.All(x => Math.Abs(GetProsperityDelta(x) - expected) < tolerance);
                case "construction_stagnation": case "golden_age":
                    return towns.Count > 0 && towns.All(x => Math.Abs(GetConstructionFactor(x) - expected) < tolerance);
                case "political_unrest": case "national_unity":
                    return towns.Count > 0 && towns.All(x => Math.Abs(GetLoyaltyDelta(x) - expected) < tolerance);
                default: return false;
            }
        }

        private ReignKingdomEventTestLedger FindKingdomEventLedger(string runId)
        {
            return _testLedgers.FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
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

        private string KingdomEventFeatureFingerprint(ReignKingdomEventTestLedger ledger,
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
                ["lastRolledDay"] = _lastRolledDay,
                ["recordCount"] = record == null ? 0 : _events.Count(x => Same(x.EventId, record.EventId))
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
