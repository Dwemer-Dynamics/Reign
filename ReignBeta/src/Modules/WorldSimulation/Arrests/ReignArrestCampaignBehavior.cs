using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static ReignBeta.WorldSimulation.World.ReignWorldParsing;
using ReignBeta.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Domain;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Integration;

namespace ReignBeta.WorldSimulation.Arrests
{
    public sealed partial class ReignArrestCampaignBehavior : CampaignBehaviorBase
    {
        private List<ReignArrestCase> _cases = new List<ReignArrestCase>();
        private float _lastEvidenceReviewDay = -1000f;
        private ReignServerClient.RequestFence _dailyEvidenceReview;
        private readonly HashSet<string> _capturedRepairIntentsOpenedHere = new HashSet<string>(StringComparer.Ordinal);
        private const string CapturedRepairRoute = "/arrests/case/apply";

        public static ReignArrestCampaignBehavior Instance => ReignServices.Arrest;

        public ReignArrestCampaignBehavior()
        {
            ReignServices.Arrest = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsLoading)
            {
                _dailyEvidenceReview = null;
                _capturedRepairIntentsOpenedHere.Clear();
            }
            dataStore.SyncData("_reign_arrest_cases_v1", ref _cases);
            dataStore.SyncData("_reign_arrest_evidence_review_day", ref _lastEvidenceReviewDay);
            NormalizeLoadedState();
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            _dailyEvidenceReview = null;
            _capturedRepairIntentsOpenedHere.Clear();
            ReignArrestCustodyPatches.ResetSovereigntyReleases();
            NormalizeLoadedState();
        }

        private void NormalizeLoadedState()
        {
            _cases = (_cases ?? new List<ReignArrestCase>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.CaseId))
                .GroupBy(item => item.CaseId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            foreach (ReignArrestCase item in _cases)
            {
                item.Version = Math.Max(1, item.Version);
                item.History = item.History ?? new List<string>();
                item.ReputationTagId = string.IsNullOrWhiteSpace(item.ReputationTagId)
                    ? "arrest_accusation_" + CompactId(item.CaseId)
                    : item.ReputationTagId;
            }
        }

        public IReadOnlyList<ReignArrestCase> Cases => _cases;

        public ReignArrestCase FindCase(string caseId)
        {
            return _cases.FirstOrDefault(item => string.Equals(item.CaseId,
                caseId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        public ReignArrestCase ActiveCaseFor(Hero accused)
        {
            return accused == null ? null : _cases.LastOrDefault(item => item.IsOpen
                && string.Equals(item.AccusedHeroStringId, accused.StringId,
                    StringComparison.OrdinalIgnoreCase));
        }

        public JObject BuildConversationContext(Hero accused)
        {
            ReignArrestCase active = ActiveCaseFor(accused)
                ?? _cases.LastOrDefault(item => item.AccusationActive
                    && string.Equals(item.AccusedHeroStringId, accused?.StringId,
                        StringComparison.OrdinalIgnoreCase));
            ReignArrestContextKind available = ResolveContext(accused, out Settlement settlement,
                out MobileParty targetParty);
            bool heldByPlayer = accused?.IsPrisoner == true && IsPlayerCustody(accused.PartyBelongedToAsPrisoner);
            return new JObject
            {
                ["available"] = available != ReignArrestContextKind.Unknown,
                ["contextKind"] = ContextName(available),
                ["settlementId"] = settlement?.StringId ?? string.Empty,
                ["targetPartyId"] = targetParty?.StringId ?? string.Empty,
                ["heldByPlayer"] = heldByPlayer,
                ["activeCaseId"] = active?.CaseId ?? string.Empty,
                ["phase"] = active == null ? string.Empty : PhaseName(active.Phase),
                ["rawAccusation"] = active?.RawAccusation ?? string.Empty,
                ["chargeCategory"] = active?.ChargeCategory ?? string.Empty,
                ["severity"] = active == null ? string.Empty : SeverityName(active.Severity),
                ["causeEstablished"] = active?.CauseEstablished ?? false,
                ["surrenderDisposition"] = active?.SurrenderDisposition ?? string.Empty,
                ["accusationActive"] = active?.AccusationActive ?? false
            };
        }

        public ReignActionResult PrepareArrest(ReignWorldActionRecord action, Hero accused)
        {
            if (action == null || accused == null)
                return ReignActionResult.ValidationFailed("PrepareArrest requires the current NPC.");
            JObject terms = ParseTerms(action.TermsJson);
            string accusation = ReadString(terms, "accusation", action.Reason).Trim();
            if (accusation.Length < 3)
                return ReignActionResult.ValidationFailed("The player must state why the character is being arrested.")
                    .WithResultCode("arrest_reason_required");

            ReignArrestCase existing = ActiveCaseFor(accused);
            if (existing != null)
            {
                return ReignActionResult.NoOp("An arrest case for " + accused.Name + " is already open.")
                    .WithResultCode("arrest_case_already_open")
                    .WithDiagnostic("arrestCaseId", existing.CaseId);
            }

            ReignArrestContextKind context = ResolveContext(accused, out Settlement settlement,
                out MobileParty targetParty);
            if (context == ReignArrestContextKind.Unknown)
                return ReignActionResult.ValidationFailed("The player can arrest the current NPC only in a player-owned town or castle, from the player party, or during an encounter with the NPC's party.")
                    .WithResultCode("arrest_outside_jurisdiction");

            ReignArrestChargeSeverity severity = ParseSeverity(ReadString(terms,
                "severity", "serious"));
            ReignArrestCase created = new ReignArrestCase
            {
                CaseId = ReadString(terms, "caseId", "arrest_" + Guid.NewGuid().ToString("N")),
                PlayerHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                AccusedHeroStringId = accused.StringId,
                Phase = ReignArrestPhase.AwaitingConfirmation,
                ContextKind = context,
                SettlementStringId = settlement?.StringId ?? string.Empty,
                CustodyPartyStringId = context == ReignArrestContextKind.PlayerSettlement
                    ? PartyStringId(settlement?.Party)
                    : PartyStringId(PartyBase.MainParty),
                BattlePartyStringId = targetParty?.StringId ?? string.Empty,
                RawAccusation = accusation,
                ChargeCategory = ReadString(terms, "chargeCategory", "other"),
                Severity = severity,
                ReputationValue = ReputationValue(severity),
                ProposedDay = CurrentDay(),
                SurrenderAccepted = ReadBool(terms, "surrenderAccepted", context != ReignArrestContextKind.PartyEncounter),
                SurrenderDisposition = context == ReignArrestContextKind.PartyEncounter
                    ? ReadBool(terms, "surrenderAccepted", false) ? "accepted" : "refused"
                    : "not_required",
                ReactionSnapshot = ReadString(terms, "reactionSnapshot", string.Empty),
                ReactionDay = CurrentDay()
            };
            created.ReputationTagId = "arrest_accusation_" + CompactId(created.CaseId);
            EvaluateCause(created, accused);
            AddHistory(created, "proposed", "The player accused " + accused.Name + " of " + accusation + ".");
            if (!string.IsNullOrWhiteSpace(created.ReactionSnapshot))
                AddHistory(created, "reaction", accused.Name + " reacted: "
                    + created.ReactionSnapshot);
            _cases.Add(created);
            RecordHistory(created, accused, "arrest_proposed", "proposed",
                "The player ordered guards to prepare to arrest " + accused.Name + ".");
            return ReignActionResult.Done("The guards moved into place around " + accused.Name + ".")
                .WithResultCode("arrest_awaiting_confirmation")
                .WithEffect("arrest_prepared", "hero", accused.StringId,
                    accused.Name.ToString(), "caseId=" + created.CaseId)
                .WithChangedEntity("arrest_case", created.CaseId, accused.Name.ToString(),
                    "awaiting_confirmation")
                .WithDiagnostic("arrestCaseId", created.CaseId)
                .WithDiagnostic("causeEstablished", created.CauseEstablished.ToString());
        }

        public ReignActionResult ConfirmArrest(ReignWorldActionRecord action, Hero accused)
        {
            ReignArrestCase item = ResolveActionCase(action, accused);
            if (item == null)
                return ReignActionResult.ValidationFailed("There is no prepared arrest for the current NPC.")
                    .WithResultCode("arrest_case_missing");
            if (item.Phase != ReignArrestPhase.AwaitingConfirmation
                && item.Phase != ReignArrestPhase.Refused)
                return ReignActionResult.NoOp("The arrest case is already " + PhaseName(item.Phase) + ".")
                    .WithResultCode("arrest_case_not_confirmable");

            item.ConfirmedDay = CurrentDay();
            AddHistory(item, "confirmed", "The player explicitly confirmed that the prepared arrest should proceed.");
            if (item.ContextKind == ReignArrestContextKind.PartyEncounter && !item.SurrenderAccepted)
            {
                item.Phase = ReignArrestPhase.Refused;
                item.LastOutcome = "surrender_refused";
                AddHistory(item, "refused", accused.Name + " refused to surrender.");
                RecordHistory(item, accused, "arrest_refused", "refused",
                    accused.Name + " refused the player's demand to surrender for arrest.");
                return ReignActionResult.NoOp(accused.Name + " refused to surrender. Capture requires a nonlethal duel or a party battle.")
                    .WithResultCode("arrest_surrender_refused")
                    .WithDiagnostic("arrestCaseId", item.CaseId);
            }

            PartyBase custody = ResolveCustodyParty(item);
            if (custody == null)
                return ReignActionResult.ValidationFailed("The arrest's custody destination is no longer valid.")
                    .WithResultCode("arrest_custody_invalid");
            if (!accused.IsPrisoner)
                TakePrisonerAction.Apply(custody, accused);
            FinalizeCapture(item, accused, custody, "confirmed_arrest");
            return ReignActionResult.Done(accused.Name + " was taken into custody.")
                .WithResultCode("arrest_captured")
                .WithEffect("hero_arrested", "hero", accused.StringId,
                    accused.Name.ToString(), "caseId=" + item.CaseId + ";custody=" + PartyStringId(custody))
                .WithChangedEntity("arrest_case", item.CaseId, accused.Name.ToString(), "captured")
                .WithDiagnostic("arrestCaseId", item.CaseId);
        }

        public ReignActionResult ReleaseArrested(ReignWorldActionRecord action, Hero accused, bool rescind)
        {
            ReignArrestCase item = ResolveActionCase(action, accused, includeClosedAccusation: true);
            if (item == null || accused == null)
                return ReignActionResult.ValidationFailed("No arrest case was found for this character.");

            (ArrestEffectSyncStatus Status, int? Remainder)? effect = null;
            if (rescind)
            {
                item.AccusationActive = false;
                item.Phase = ReignArrestPhase.Rescinded;
                item.ReleasedDay = CurrentDay();
                item.LastOutcome = "accusation_rescinded";
                AddHistory(item, "rescinded", "The player rescinded the accusation and cleared the accused's name.");
                effect = SynchronizeCaseEffect(item, accused, true);
            }
            if (accused.IsPrisoner && IsPlayerCustody(accused.PartyBelongedToAsPrisoner))
                EndCaptivityAction.ApplyByReleasedByChoice(accused, Hero.MainHero);
            if (!rescind)
            {
                item.Phase = ReignArrestPhase.Released;
                item.ReleasedDay = CurrentDay();
                item.LastOutcome = "released_accusation_retained";
                AddHistory(item, "released", "The player released the accused without rescinding the accusation.");
            }
            RecordHistory(item, accused, rescind ? "arrest_accusation_rescinded" : "arrest_released",
                rescind ? "rescinded" : "released",
                rescind ? "The player cleared " + accused.Name + " of the arrest accusation."
                    : "The player released " + accused.Name + " while leaving the accusation on record.", effect);
            return ReignActionResult.Done(rescind
                    ? accused.Name + " was cleared and released."
                    : accused.Name + " was released; the accusation remains on record.")
                .WithResultCode(rescind ? "arrest_rescinded_and_released" : "arrest_released")
                .WithChangedEntity("arrest_case", item.CaseId, accused.Name.ToString(),
                    rescind ? "rescinded" : "released");
        }

        public ReignActionResult AttackTargetParty(ReignWorldActionRecord action, Hero accused)
        {
            ReignArrestCase item = ResolveActionCase(action, accused);
            MobileParty targetParty = accused?.PartyBelongedTo;
            if (item == null || targetParty == null || targetParty.Party == PartyBase.MainParty)
                return ReignActionResult.ValidationFailed("A refused arrest and the accused's active party are required.")
                    .WithResultCode("arrest_battle_target_missing");
            if (PlayerEncounter.EncounteredParty != targetParty.Party)
                return ReignActionResult.ValidationFailed("The accused's party is not the active player encounter.")
                    .WithResultCode("arrest_battle_encounter_missing");
            item.Phase = ReignArrestPhase.BattlePending;
            item.BattlePartyStringId = targetParty.StringId;
            AddHistory(item, "battle_ordered", "The player ordered an attack after the accused refused arrest.");
            BeHostileAction.ApplyEncounterHostileAction(PartyBase.MainParty, targetParty.Party);
            RecordHistory(item, accused, "arrest_party_attack_ordered", "battle_pending",
                "The player ordered an attack on " + accused.Name + "'s party after surrender was refused.");
            return ReignActionResult.Progress("The player's party attacked " + targetParty.Name + ".")
                .WithResultCode("arrest_party_battle_started")
                .WithEffect("player_party_attack", "party", targetParty.StringId,
                    targetParty.Name?.ToString() ?? targetParty.StringId, "caseId=" + item.CaseId)
                .WithDiagnostic("arrestCaseId", item.CaseId);
        }

        public void MarkDuelPending(string caseId, string actionId)
        {
            ReignArrestCase item = FindCase(caseId);
            if (item == null) return;
            item.Phase = ReignArrestPhase.DuelPending;
            item.DuelActionId = actionId ?? string.Empty;
            AddHistory(item, "duel_pending", "A nonlethal duel was accepted to decide the arrest.");
        }

        public void CompleteArrestDuel(string caseId, Hero accused, bool playerWon)
        {
            ReignArrestCase item = FindCase(caseId);
            if (item == null || accused == null) return;
            if (playerWon)
            {
                if (!accused.IsPrisoner) TakePrisonerAction.Apply(PartyBase.MainParty, accused);
                FinalizeCapture(item, accused, PartyBase.MainParty, "arrest_duel_won");
            }
            else
            {
                item.Phase = ReignArrestPhase.Escaped;
                item.LastOutcome = "escaped_after_duel";
                AddHistory(item, "duel_lost", "The accused defeated the player and escaped arrest.");
                MobileParty party = accused.PartyBelongedTo;
                if (party != null && party.IsActive)
                {
                    // The victor is allowed to resume normal AI movement while the
                    // player's party remains stopped after the encounter, providing
                    // a short practical head start without installing persistent AI.
                    MobileParty.MainParty?.SetMoveModeHold();
                }
                RecordHistory(item, accused, "arrest_duel_lost", "escaped",
                    accused.Name + " defeated the player in a nonlethal arrest duel and escaped.");
            }
        }

        public bool IsProtectedCustody(Hero prisoner)
        {
            if (prisoner == null || !prisoner.IsPrisoner
                || !IsPlayerCustody(prisoner.PartyBelongedToAsPrisoner)) return false;
            ReignArrestCase item = _cases.LastOrDefault(candidate => candidate.Phase == ReignArrestPhase.Captured
                && string.Equals(candidate.AccusedHeroStringId, prisoner.StringId,
                    StringComparison.OrdinalIgnoreCase));
            if (item == null) return false;
            if (prisoner.Clan == Clan.PlayerClan) return true;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return playerKingdom != null && playerKingdom.Leader == Hero.MainHero
                && prisoner.Clan?.Kingdom == playerKingdom;
        }

        public static int RelationshipDelta(ReignArrestChargeSeverity severity, bool justified)
        {
            int index = Math.Max(0, Math.Min(3, (int)severity));
            int[] values = justified ? new[] { -6, -3, 0, 8 } : new[] { -12, -20, -30, -45 };
            return values[index];
        }

        public static int ReputationValue(ReignArrestChargeSeverity severity)
        {
            int index = Math.Max(0, Math.Min(3, (int)severity));
            return new[] { -8, -15, -25, -40 }[index];
        }

        internal List<ReignArrestCase> PrepareSerializationFixtures(
            string fixtureRunId, Hero accused)
        {
            string safeRun = CompactId(fixtureRunId);
            if (safeRun.Length == 0 || accused == null) return new List<ReignArrestCase>();
            string prefix = "arrest_test_" + safeRun + "_";
            _cases.RemoveAll(item => item?.CaseId?.StartsWith(prefix,
                StringComparison.OrdinalIgnoreCase) == true);
            List<ReignArrestCase> created = new List<ReignArrestCase>();
            foreach (ReignArrestPhase phase in Enum.GetValues(typeof(ReignArrestPhase)))
            {
                ReignArrestCase item = new ReignArrestCase
                {
                    CaseId = prefix + PhaseName(phase),
                    PlayerHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                    AccusedHeroStringId = accused.StringId,
                    Phase = phase,
                    ContextKind = ReignArrestContextKind.PlayerParty,
                    RawAccusation = "deterministic serialization fixture for " + PhaseName(phase),
                    ChargeCategory = phase == ReignArrestPhase.Captured ? "espionage" : "other",
                    Severity = (ReignArrestChargeSeverity)((int)phase % 4),
                    CauseEstablished = phase == ReignArrestPhase.Captured,
                    EvidenceStatus = phase == ReignArrestPhase.Captured ? "fixture_established" : "fixture_unsupported",
                    EvidenceIdsCsv = phase == ReignArrestPhase.Captured ? "fixture:evidence" : string.Empty,
                    ReputationValue = ReputationValue((ReignArrestChargeSeverity)((int)phase % 4)),
                    ReputationTagId = "arrest_accusation_" + safeRun + "_" + PhaseName(phase),
                    AccusationActive = phase != ReignArrestPhase.Rescinded,
                    SurrenderAccepted = true,
                    SurrenderDisposition = "fixture",
                    ProposedDay = CurrentDay(),
                    LastOutcome = "serialization_fixture_" + PhaseName(phase)
                };
                AddHistory(item, "fixture_created", "Prepared deterministic save/load fixture for " + PhaseName(phase) + ".");
                _cases.Add(item);
                created.Add(item);
            }
            return created;
        }

        internal int CleanupSerializationFixtures(string fixtureRunId)
        {
            string safeRun = CompactId(fixtureRunId);
            if (safeRun.Length == 0) return 0;
            string prefix = "arrest_test_" + safeRun + "_";
            return _cases.RemoveAll(item => item?.CaseId?.StartsWith(prefix,
                StringComparison.OrdinalIgnoreCase) == true);
        }

        internal ReignArrestCase PrepareNativeAcceptanceFixture(string fixtureRunId,
            string caseSuffix, Hero accused, ReignArrestPhase phase,
            ReignArrestContextKind context, Settlement settlement,
            bool surrenderAccepted, bool causeEstablished)
        {
            string safeRun = CompactId(fixtureRunId);
            string safeSuffix = CompactId(caseSuffix);
            if (safeRun.Length == 0 || safeSuffix.Length == 0 || accused == null) return null;
            string caseId = "arrest_test_" + safeRun + "_native_" + safeSuffix;
            _cases.RemoveAll(item => string.Equals(item?.CaseId, caseId,
                StringComparison.OrdinalIgnoreCase));
            ReignArrestCase created = new ReignArrestCase
            {
                CaseId = caseId,
                PlayerHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                AccusedHeroStringId = accused.StringId,
                Phase = phase,
                ContextKind = context,
                SettlementStringId = settlement?.StringId ?? string.Empty,
                CustodyPartyStringId = context == ReignArrestContextKind.PlayerSettlement
                    ? PartyStringId(settlement?.Party) : PartyStringId(PartyBase.MainParty),
                RawAccusation = "guarded native arrest acceptance fixture",
                ChargeCategory = "espionage",
                Severity = ReignArrestChargeSeverity.Grave,
                CauseEstablished = causeEstablished,
                EvidenceStatus = causeEstablished ? "fixture_established" : "fixture_unsupported",
                EvidenceIdsCsv = causeEstablished ? "fixture:native_acceptance" : string.Empty,
                ReputationValue = ReputationValue(ReignArrestChargeSeverity.Grave),
                ReputationTagId = "arrest_accusation_" + CompactId(caseId),
                AccusationActive = true,
                SurrenderAccepted = surrenderAccepted,
                SurrenderDisposition = surrenderAccepted ? "accepted" : "refused",
                ProposedDay = CurrentDay(),
                LastOutcome = "native_acceptance_fixture"
            };
            AddHistory(created, "fixture_created",
                "Prepared guarded native acceptance fixture " + safeSuffix + ".");
            _cases.Add(created);
            return created;
        }

        internal bool RemoveNativeAcceptanceFixture(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId)
                || !caseId.StartsWith("arrest_test_", StringComparison.OrdinalIgnoreCase))
                return false;
            return _cases.RemoveAll(item => string.Equals(item?.CaseId, caseId,
                StringComparison.OrdinalIgnoreCase)) > 0;
        }

        private void OnDailyTick()
        {
            ReignArrestCustodyPatches.ClearExpiredSovereigntyReleases();
            float day = CurrentDay();
            if (ReignCampaignInitializationGate.IsPending || day - _lastEvidenceReviewDay < 0.95f
                || _dailyEvidenceReview?.IsCurrent() == true)
                return;
            ReignServerClient.RequestFence fence = ReignServerClient.RequestFence.Capture();
            if (!fence.IsCurrentActionPoll()) return;

            var cases = _cases.Where(item => item.AccusationActive && item.IsOpen
                || !string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson)).ToList();
            if (cases.Count == 0)
            {
                _lastEvidenceReviewDay = day;
                return;
            }

            _dailyEvidenceReview = fence;
            var saveSync = ReignServices.SaveSync;
            string savePoint = saveSync?.CurrentSavePointId;
            // Saving can advance the point without changing the loaded campaign generation.
            // The owner predicate is invoked only on the game thread.
            bool OwnsPoint() => OwnsDailyEvidenceReview(fence) && ReferenceEquals(saveSync, ReignServices.SaveSync)
                && string.Equals(savePoint, saveSync?.CurrentSavePointId, StringComparison.Ordinal);
            _ = Task.Run(() => ReviewDailyEvidenceAsync(fence, OwnsPoint, day, cases));
        }

        private bool OwnsDailyEvidenceReview(ReignServerClient.RequestFence fence) =>
            ReferenceEquals(Instance, this) && ReferenceEquals(_dailyEvidenceReview, fence)
            && !ReignCampaignInitializationGate.IsPending && fence.IsCurrentActionPoll();

        private async Task ReviewDailyEvidenceAsync(ReignServerClient.RequestFence fence, Func<bool> ownsPoint, float day,
            List<ReignArrestCase> cases)
        {
            bool finished = false;
            bool capturedRepairsSettled = true;
            try
            {
                foreach (ReignArrestCase item in cases)
                {
                    bool lateEvidencePending = await ReignMainThread.InvokeAsync(() =>
                        ownsPoint() && !string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson)).ConfigureAwait(false);
                    if (lateEvidencePending)
                    {
                        if (!await ReviewOwnedLateEvidenceAsync(item, ownsPoint).ConfigureAwait(false))
                            capturedRepairsSettled = false;
                        continue;
                    }
                    var request = await ReignMainThread.InvokeAsync(() =>
                        CaptureDailyEvidenceRequest(fence, ownsPoint, item)).ConfigureAwait(false);
                    if (request.RequiresCapturedRepair)
                    {
                        if (!await ReviewCapturedRepairAsync(request.CapturedRepair).ConfigureAwait(false))
                            capturedRepairsSettled = false;
                        continue;
                    }
                    if (request.Payload == null)
                    {
                        if (await ReignMainThread.InvokeAsync(() => !string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson)).ConfigureAwait(false))
                        {
                            if (!await ReviewOwnedLateEvidenceAsync(item, ownsPoint).ConfigureAwait(false))
                                capturedRepairsSettled = false;
                            continue;
                        }
                        if (!await ReignMainThread.InvokeAsync(ownsPoint).ConfigureAwait(false))
                            return;
                        continue;
                    }

                    JObject response = null;
                    try
                    {
                        response = await ReignServerClient.PostOwnedJsonAsync("/arrests/evidence/evaluate",
                            request.Payload, request.IsCurrent).ConfigureAwait(false);
                    }
                    catch (ReignRequestOwnerAbandonedException)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        LogEvidenceReviewFailure(request.Payload.Value<string>("accusedHeroId"), ex);
                    }

                    await ReignMainThread.InvokeAsync(() => request.Complete(response))
                        .ConfigureAwait(false);
                    if (await ReignMainThread.InvokeAsync(() => !string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson)).ConfigureAwait(false)
                        && !await ReviewOwnedLateEvidenceAsync(item, ownsPoint).ConfigureAwait(false))
                        capturedRepairsSettled = false;
                }
                finished = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Daily arrest evidence review deferred: " + ex.Message);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_dailyEvidenceReview, fence)) return;
                    // A save taken while a response is outstanding can review the unfinished day again.
                    if (finished && capturedRepairsSettled && ownsPoint()) _lastEvidenceReviewDay = day;
                    _dailyEvidenceReview = null;
                }).ConfigureAwait(false);
            }
        }

        private (JObject Payload, Func<bool> IsCurrent, Action<JObject> Complete,
            bool RequiresCapturedRepair, CapturedRepairWork CapturedRepair) CaptureDailyEvidenceRequest(
                ReignServerClient.RequestFence fence, Func<bool> ownsPoint, ReignArrestCase item)
        {
            if (!ownsPoint() || !_cases.Contains(item) || !item.AccusationActive || !item.IsOpen)
                return default;

            Hero accused = FindHero(item.AccusedHeroStringId);
            if (!MatchesEvidenceSubject(item, accused)) return default;
            if (item.CauseEstablished)
                return NeedsCapturedRepair(item)
                    ? (null, null, null, true, CaptureDailyCapturedRepair(fence, ownsPoint, item, accused))
                    : default;
            if (TryGetLocalEvidence(item, accused, out string evidence))
            {
                if (item.Phase == ReignArrestPhase.Captured)
                {
                    RecordOwnedLateEvidence(item, accused, evidence);
                    return default;
                }
                MarkEstablishedEvidence(item, "established", evidence);
                ContinueDailyArrestMaintenance(item, accused, true);
                return default;
            }

            var identity = DailyEvidenceIdentity(item);
            Hero player = Hero.MainHero;
            JObject payload = BuildEvidencePayload(item, accused);

            bool Current() => ownsPoint() && _cases.Contains(item)
                && item.AccusationActive && item.IsOpen && !item.CauseEstablished
                && identity.Equals(DailyEvidenceIdentity(item))
                && ReferenceEquals(Hero.MainHero, player)
                && ReferenceEquals(FindHero(identity.Accused), accused);

            void Complete(JObject response)
            {
                if (!Current()) return;
                try
                {
                    if (item.Phase == ReignArrestPhase.Captured
                        && ReignArrestEvidenceResponse.TryRead(payload, response, out string confirmedEvidence))
                    {
                        string responseOrigin = ReignServerClient.ResponseOrigin(response);
                        if (!string.IsNullOrEmpty(responseOrigin))
                            RecordOwnedLateEvidence(item, accused, confirmedEvidence, responseOrigin);
                        return;
                    }
                    ApplyEvidenceResponse(item, payload, response);
                }
                catch (Exception ex)
                {
                    LogEvidenceReviewFailure(identity.Accused, ex);
                    MarkUnsupportedEvidence(item);
                }
                ContinueDailyArrestMaintenance(item, accused, item.CauseEstablished);
            }

            return (payload, Current, Complete, false, null);
        }

        private static (string Case, string Accused, string Player, string Category, string Accusation, ReignArrestPhase Phase)
            DailyEvidenceIdentity(ReignArrestCase item) => (item.CaseId, item.AccusedHeroStringId,
                item.PlayerHeroStringId, item.ChargeCategory, item.RawAccusation, item.Phase);

        private void ContinueDailyArrestMaintenance(ReignArrestCase item, Hero accused, bool reclassify)
        {
            if (reclassify)
            {
                item.EvidenceStatus = "established_after_arrest";
                item.RelationshipEffectSynchronized = false;
                AddHistory(item, "evidence_reclassified", "Later authoritative evidence established cause without revealing it earlier.");
                SynchronizeCaseEffect(item, accused, false);
                RecordHistory(item, accused, "arrest_evidence_reclassified", "evidence_established",
                    "Later disclosed evidence established cause in the arrest case against " + accused.Name + ".");
            }
            if ((!item.RelationshipEffectSynchronized || !item.ReputationSynchronized)
                && item.Phase == ReignArrestPhase.Captured)
                SynchronizeCaseEffect(item, accused, false);
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (prisoner == null || !IsPlayerCustody(captor)) return;
            ReignArrestCase item = _cases.LastOrDefault(candidate =>
                (candidate.Phase == ReignArrestPhase.BattlePending
                 || candidate.Phase == ReignArrestPhase.DuelPending
                 || candidate.Phase == ReignArrestPhase.AwaitingConfirmation)
                && string.Equals(candidate.AccusedHeroStringId, prisoner.StringId,
                    StringComparison.OrdinalIgnoreCase));
            if (item != null) FinalizeCapture(item, prisoner, captor, "native_capture_event");
        }

        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase captor, IFaction faction,
            EndCaptivityDetail detail, bool showNotification)
        {
            ReignArrestCase item = _cases.LastOrDefault(candidate => candidate.Phase == ReignArrestPhase.Captured
                && string.Equals(candidate.AccusedHeroStringId, prisoner?.StringId,
                    StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            Settlement settlement = captor?.IsSettlement == true ? captor.Settlement : null;
            bool sovereigntyRelease = settlement?.IsFortification == true
                && settlement.OwnerClan != null
                && settlement.OwnerClan != Clan.PlayerClan
                && string.Equals(item.SettlementStringId, settlement.StringId,
                    StringComparison.OrdinalIgnoreCase)
                && ReignArrestCustodyPatches.ConsumeSovereigntyRelease(
                    prisoner, settlement, settlement.OwnerClan.Leader);
            if (sovereigntyRelease)
            {
                TakePrisonerAction.Apply(settlement.Party, prisoner);
                if (prisoner?.IsPrisoner == true
                    && prisoner.PartyBelongedToAsPrisoner == settlement.Party)
                {
                    item.Phase = ReignArrestPhase.Captured;
                    item.ReleasedDay = -1f;
                    item.CustodyPartyStringId = PartyStringId(settlement.Party);
                    item.LastOutcome = "sovereignty_custody_migrated";
                    AddHistory(item, "custody_migrated",
                        "Custody remained with " + settlement.Name
                        + " after its sovereignty changed.");
                    RecordHistory(item, prisoner, "arrest_custody_migrated", "captured",
                        prisoner.Name + " remained in " + settlement.Name
                        + " custody after sovereignty changed.");
                    return;
                }
            }
            item.Phase = detail == EndCaptivityDetail.ReleasedAfterEscape
                ? ReignArrestPhase.Escaped : ReignArrestPhase.Released;
            item.ReleasedDay = CurrentDay();
            item.LastOutcome = "native_release_" + detail;
            AddHistory(item, item.Phase == ReignArrestPhase.Escaped ? "escaped" : "released",
                "Custody ended through " + detail + ".");
            RecordHistory(item, prisoner, item.Phase == ReignArrestPhase.Escaped
                    ? "arrest_prisoner_escaped" : "arrest_prisoner_released",
                PhaseName(item.Phase), prisoner.Name + " left custody through " + detail + ".");
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement?.IsFortification != true || settlement.Party == null) return;
            foreach (ReignArrestCase item in _cases.Where(candidate => candidate != null
                && candidate.AccusationActive
                && candidate.CapturedDay >= 0f
                && string.Equals(candidate.SettlementStringId, settlement.StringId,
                    StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Hero accused = FindHero(item.AccusedHeroStringId);
                bool capturedAtOwnershipChange = item.Phase == ReignArrestPhase.Captured;
                if (!capturedAtOwnershipChange) continue;
                ReignArrestCustodyPatches.RecordSovereigntyTransition(
                    accused, settlement, newOwner);
                if (accused?.IsPrisoner == true
                    && accused.PartyBelongedToAsPrisoner != settlement.Party)
                    EndCaptivityAction.ApplyByReleasedByChoice(accused, Hero.MainHero);
                if (accused?.IsPrisoner != true)
                    TakePrisonerAction.Apply(settlement.Party, accused);
                if (accused?.IsPrisoner == true
                    && accused.PartyBelongedToAsPrisoner == settlement.Party)
                {
                    item.Phase = ReignArrestPhase.Captured;
                    item.ReleasedDay = -1f;
                    item.CustodyPartyStringId = PartyStringId(settlement.Party);
                    item.LastOutcome = "sovereignty_custody_migrated";
                    AddHistory(item, "custody_migrated", "Custody migrated with " + settlement.Name
                        + " after sovereignty changed from " + (oldOwner?.Name?.ToString() ?? "the former ruler")
                        + " to " + (newOwner?.Name?.ToString() ?? "the new ruler") + ".");
                    RecordHistory(item, accused, "arrest_custody_migrated", "captured",
                        accused.Name + " remained in " + settlement.Name
                        + " custody after sovereignty changed.");
                }
            }
        }

        internal void ReconcileSovereigntyCustodyAfterOwnerChange(Settlement settlement,
            Hero newOwner)
        {
            if (settlement?.IsFortification != true || settlement.Party == null) return;
            foreach (ReignArrestCase item in _cases.Where(candidate => candidate != null
                && candidate.AccusationActive
                && candidate.CapturedDay >= 0f
                && candidate.Phase == ReignArrestPhase.Captured
                && string.Equals(candidate.SettlementStringId, settlement.StringId,
                    StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Hero accused = FindHero(item.AccusedHeroStringId);
                if (accused == null) continue;
                if (accused.IsPrisoner
                    && accused.PartyBelongedToAsPrisoner != settlement.Party)
                    EndCaptivityAction.ApplyByReleasedByChoice(accused, Hero.MainHero);
                if (!accused.IsPrisoner)
                    TakePrisonerAction.Apply(settlement.Party, accused);
                if (!accused.IsPrisoner
                    || accused.PartyBelongedToAsPrisoner != settlement.Party) continue;
                item.ReleasedDay = -1f;
                item.CustodyPartyStringId = PartyStringId(settlement.Party);
                item.LastOutcome = "sovereignty_custody_migrated";
                AddHistory(item, "custody_migrated_post_dispatch",
                    "Custody reconciliation completed after sovereignty changed to "
                    + (newOwner?.Name?.ToString() ?? "the new ruler") + ".");
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.IsFieldBattle) return;
            HashSet<string> participantPartyIds = new HashSet<string>(
                mapEvent.AttackerSide.Parties.Concat(mapEvent.DefenderSide.Parties)
                    .Select(entry => PartyStringId(entry.Party))
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            if (!participantPartyIds.Contains(PartyStringId(PartyBase.MainParty))) return;
            foreach (ReignArrestCase item in _cases.Where(candidate =>
                candidate.Phase == ReignArrestPhase.BattlePending
                && participantPartyIds.Contains(candidate.BattlePartyStringId)).ToList())
            {
                Hero accused = FindHero(item.AccusedHeroStringId);
                if (accused?.IsPrisoner == true && IsPlayerCustody(accused.PartyBelongedToAsPrisoner))
                {
                    FinalizeCapture(item, accused, accused.PartyBelongedToAsPrisoner,
                        "native_battle_capture");
                    continue;
                }
                item.Phase = ReignArrestPhase.Escaped;
                item.LastOutcome = "native_battle_capture_failed";
                AddHistory(item, "battle_capture_failed",
                    "The battle ended without the accused entering player custody.");
                RecordHistory(item, accused, "arrest_battle_capture_failed", "escaped",
                    (accused?.Name?.ToString() ?? item.AccusedHeroStringId)
                    + " was not captured after the player ordered a party attack.");
            }
        }

        private void FinalizeCapture(ReignArrestCase item, Hero accused, PartyBase custody, string source)
        {
            if (item == null || accused == null) return;
            bool firstCapture = item.Phase != ReignArrestPhase.Captured;
            item.Phase = ReignArrestPhase.Captured;
            item.CapturedDay = item.CapturedDay < 0f ? CurrentDay() : item.CapturedDay;
            item.CustodyPartyStringId = PartyStringId(custody);
            item.LastOutcome = source;
            if (firstCapture) AddHistory(item, "captured", accused.Name + " was placed in player custody.");
            SynchronizeCaseEffect(item, accused, false);
            // The native custody event can finalize this capture before its caller returns.
            if (firstCapture)
                RecordHistory(item, accused, "arrest_captured", "captured",
                    accused.Name + " was imprisoned on the player's accusation: " + item.RawAccusation + ".");
        }

        private void EvaluateCause(ReignArrestCase item, Hero accused)
        {
            if (!MatchesEvidenceSubject(item, accused))
            {
                if (item != null) MarkUnsupportedEvidence(item);
                return;
            }
            if (TryGetLocalEvidence(item, accused, out string evidence))
            {
                MarkEstablishedEvidence(item, "established", evidence);
                return;
            }
            try
            {
                JObject payload = BuildEvidencePayload(item, accused);
                JObject response = ReignServerClient.EvaluateArrestEvidenceAsync(payload).GetAwaiter().GetResult();
                ApplyEvidenceResponse(item, payload, response);
            }
            catch (Exception ex)
            {
                LogEvidenceReviewFailure(accused?.StringId, ex);
                MarkUnsupportedEvidence(item);
            }
        }

        private static JObject BuildEvidencePayload(ReignArrestCase item, Hero accused) => new JObject
        {
            ["schema"] = ReignArrestEvidenceResponse.RequestSchema,
            ["campaignId"] = ReignServerClient.GetCampaignId(),
            ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
            ["accusedHeroId"] = accused?.StringId ?? string.Empty,
            ["chargeCategory"] = item?.ChargeCategory ?? string.Empty,
            ["accusation"] = item?.RawAccusation ?? string.Empty
        };

        private static void ApplyEvidenceResponse(ReignArrestCase item, JObject request, JObject response)
        {
            if (request.Value<string>("accusedHeroId") == item.AccusedHeroStringId
                && request.Value<string>("chargeCategory") == item.ChargeCategory
                && ReignArrestEvidenceResponse.TryRead(request, response, out string evidence))
                MarkEstablishedEvidence(item, "established", evidence);
            else
                MarkUnsupportedEvidence(item);
        }

        private static void MarkEstablishedEvidence(ReignArrestCase item, string status, string evidence)
        {
            item.CauseEstablished = true;
            item.EvidenceStatus = status;
            item.EvidenceIdsCsv = evidence;
        }

        private static void MarkUnsupportedEvidence(ReignArrestCase item)
        {
            item.CauseEstablished = false;
            item.EvidenceStatus = "unsupported";
            item.EvidenceIdsCsv = string.Empty;
        }

        private static void LogEvidenceReviewFailure(string accusedId, Exception ex) =>
            ReignLog.Warn("Arrest evidence review deferred for " + (accusedId ?? string.Empty) + ": " + ex.Message);

        private static bool MatchesEvidenceSubject(ReignArrestCase item, Hero accused) =>
            !string.IsNullOrWhiteSpace(item?.AccusedHeroStringId)
            && string.Equals(item.AccusedHeroStringId, accused?.StringId, StringComparison.Ordinal);

        internal static bool TryGetLocalEvidence(ReignArrestCase item, Hero accused, out string evidence)
        {
            evidence = string.Empty;
#if !REIGN_EXCLUDE_COURT
            if (!MatchesEvidenceSubject(item, accused))
                return false;
            ICourtWorldBridge court = ReignServices.CourtWorld;
            if (court == null)
                return false;
            if (item.ChargeCategory == "espionage")
                return court.HasExposedAgentEvidence(accused, out evidence);
            return item.ChargeCategory == "murder" && court.HasPublicMurderArrestEvidence(accused, out evidence);
#else
            return false;
#endif
        }

        private sealed class CapturedRepairWork
        {
            internal CapturedRepairWork(ReignArrestCase item, JObject material, string existingIntent,
                string origin, ReignSaveSyncCoordinator.VerifiedLoad load, Func<string, bool> owns)
            {
                Item = item;
                Material = material;
                ExistingIntent = existingIntent;
                Origin = origin;
                Load = load;
                Owns = owns;
            }

            internal ReignArrestCase Item { get; }
            internal JObject Material { get; }
            internal string ExistingIntent { get; }
            internal string Origin { get; }
            internal ReignSaveSyncCoordinator.VerifiedLoad Load { get; }
            internal Func<string, bool> Owns { get; }
        }

        private static bool NeedsCapturedRepair(ReignArrestCase item) => item.AccusationActive
            && item.CauseEstablished && item.Phase == ReignArrestPhase.Captured
            && (!item.RelationshipEffectSynchronized || !item.ReputationSynchronized);

        // The immutable request is rebuilt only to prove that the native case still describes
        // these exact terms. Keep its original observed day across transport retries and saves.
        private static JObject BuildCapturedRepairMaterial(ReignArrestCase item, Hero accused,
            double day, string campaign, string timeline, string generation)
        {
            var material = BuildArrestEffectRequest(item, accused, false);
            material["worldDay"] = day;
            string description = material.Value<string>("description").Trim();
            material["description"] = description.Length > 1000 ? description.Substring(0, 1000).TrimEnd() : description;
            material["chargeCategory"] = item.ChargeCategory ?? string.Empty;
            material["evidenceStatus"] = item.EvidenceStatus ?? string.Empty;
            material["evidenceIds"] = item.EvidenceIdsCsv ?? string.Empty;
            material["campaignId"] = campaign;
            material["timelineId"] = timeline;
            material["sharedActivityGeneration"] = generation;
            material["arrestEffectVersion"] = 2;
            return material;
        }

        private CapturedRepairWork CaptureDailyCapturedRepair(ReignServerClient.RequestFence fence,
            Func<bool> ownsPoint, ReignArrestCase item, Hero accused)
        {
            Hero player = Hero.MainHero;
            if (player == null || item.PlayerHeroStringId != player.StringId || !fence.IsCurrentActionPoll()) return null;
            string campaign = ReignServerClient.GetCampaignId(), timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
            string generation = fence.SharedActivityGeneration;
            if (string.IsNullOrWhiteSpace(campaign) || string.IsNullOrWhiteSpace(timeline)
                || string.IsNullOrWhiteSpace(item.ReputationTagId)) return null;

            var load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            if (load != null && (load.CampaignId != campaign || load.RestoreGeneration != generation)) return null;
            string origin = load?.Origin;
            string existingIntent = item.DailyCapturedRepairIntentJson ?? string.Empty;
            double day = CurrentDay();
            if (existingIntent.Length != 0)
            {
                JObject persisted = ReadCapturedRepairIntent(existingIntent);
                if (!(persisted?["request"] is JObject saved) || !(saved["sharedActivityGeneration"] is JValue oldGeneration)
                    || oldGeneration.Type != JTokenType.String || !TryReadCapturedIntentIdentity(saved, out _, out _)) return null;
                string savedOrigin = CapturedString(persisted, "origin");
                if (!ReignHttpTransport.IsAllowedPinnedOrigin(savedOrigin)) return null;
                if (!_capturedRepairIntentsOpenedHere.Contains(existingIntent) || (string)oldGeneration != generation)
                {
                    // On a native reload a serialized intent has no send authority by itself.
                    // Its exact checkpoint must have been verified by this same server origin.
                    if (load == null || load.Origin != savedOrigin || string.IsNullOrEmpty(load.SavePointId)
                        || string.IsNullOrEmpty(load.LoadSessionId)) return null;
                }
                if (origin != null && origin != savedOrigin) return null;
                origin = savedOrigin;
                if (!TryReadCapturedDay(saved, out day)) return null;
                var original = (JObject)saved.DeepClone();
                original.Remove("expectedMaterialRevision");
                original.Remove("effectIntentId");
                original["worldDay"] = day;
                if (!JToken.DeepEquals(original, BuildCapturedRepairMaterial(item, accused, day,
                        campaign, timeline, (string)oldGeneration))) return null;
            }

            var material = BuildCapturedRepairMaterial(item, accused, day, campaign, timeline, generation);
            bool Current(string expectedIntent) => ownsPoint()
                && ReferenceEquals(load, ReignSaveSyncCoordinator.CurrentVerifiedLoad)
                && _cases.Contains(item) && NeedsCapturedRepair(item)
                && ReferenceEquals(player, Hero.MainHero) && ReferenceEquals(accused, FindHero(item.AccusedHeroStringId))
                && item.PlayerHeroStringId == player.StringId && item.AccusedHeroStringId == accused.StringId
                && (item.DailyCapturedRepairIntentJson ?? string.Empty) == expectedIntent
                && JToken.DeepEquals(material, BuildCapturedRepairMaterial(item, accused, day, campaign, timeline, generation));
            return new CapturedRepairWork(item, material, existingIntent, origin, load, Current);
        }

        private async Task<bool> ReviewCapturedRepairAsync(CapturedRepairWork work)
        {
            if (work == null) return false;
            try
            {
                if (work.ExistingIntent.Length != 0)
                    return await SendCapturedRepairIntentAsync(work, work.ExistingIntent).ConfigureAwait(false);

                var inspection = (JObject)work.Material.DeepClone();
                inspection["operation"] = "inspect";
                bool OwnsInspection() => work.Owns(string.Empty);
                JObject result = work.Origin == null
                    ? await ReignServerClient.PostOwnedJsonAsync(CapturedRepairRoute, inspection, OwnsInspection).ConfigureAwait(false)
                    : await ReignServerClient.SendPinnedJsonAsync(work.Origin, CapturedRepairRoute, inspection, OwnsInspection).ConfigureAwait(false);
                string origin = work.Origin ?? ReignHttpTransport.ResponseOrigin(result);
                if (!ReignHttpTransport.IsAllowedPinnedOrigin(origin) || !CapturedInspectionMatches(inspection, result)
                    || !TryReadCapturedRevision(result, "materialRevision", out long revision)) return false;

                var preparation = await ReignMainThread.InvokeAsync(() =>
                {
                    if (!OwnsInspection()) return (Settled: false, Intent: (string)null);
                    string status = CapturedString(result, "status");
                    if (status == "terminal_rescinded" && IsCapturedBoolean(result, "ok", false))
                        return (Settled: true, Intent: (string)null);
                    if (status == "settled")
                        return (Settled: ApplyMeasuredCapturedRepair(work.Item, result), Intent: (string)null);
                    if (status != "ready" || !IsCapturedBoolean(result, "ok", true) || revision != 0)
                        return (Settled: false, Intent: (string)null);

                    var payload = (JObject)work.Material.DeepClone();
                    payload["expectedMaterialRevision"] = revision;
                    payload["effectIntentId"] = revision.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
                    string pending = new JObject { ["schema"] = 1, ["phase"] = "Captured", ["origin"] = origin,
                        ["request"] = payload }.ToString(Newtonsoft.Json.Formatting.None);
                    work.Item.DailyCapturedRepairIntentJson = pending;
                    _capturedRepairIntentsOpenedHere.Add(pending);
                    return (Settled: false, Intent: pending);
                }).ConfigureAwait(false);
                return preparation.Intent == null ? preparation.Settled
                    : await SendCapturedRepairIntentAsync(work, preparation.Intent).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Captured arrest effect retry deferred: " + ex.GetType().Name);
                return false;
            }
        }

        private async Task<bool> SendCapturedRepairIntentAsync(CapturedRepairWork work, string pending)
        {
            var envelope = ReadCapturedRepairIntent(pending);
            if (!(envelope?["request"] is JObject saved)
                || !TryReadCapturedIntentIdentity(saved, out long previousRevision, out string intent)) return false;
            string origin = CapturedString(envelope, "origin");
            if (!ReignHttpTransport.IsAllowedPinnedOrigin(origin) || work.Origin != null && work.Origin != origin) return false;
            var request = (JObject)saved.DeepClone();
            // The original intent bytes stay saved. A positively verified reload can transport
            // identical material with the current restore owner, leaving the nonce unchanged.
            request["sharedActivityGeneration"] = work.Material["sharedActivityGeneration"].DeepClone();
            bool Current() => work.Owns(pending) && (work.Load == null || work.Load.Origin == origin);
            JObject result = await ReignServerClient.SendPinnedJsonAsync(origin, CapturedRepairRoute, request, Current).ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                if (!Current() || !CapturedReplyMatches(request, result)
                    || CapturedString(result, "effectIntentId") != intent
                    || !TryReadCapturedRevision(result, "materialRevision", out long revision)) return false;
                string status = CapturedString(result, "status");
                bool committed = status == "committed" && revision == previousRevision + 1
                    && ApplyMeasuredCapturedRepair(work.Item, result);
                bool superseded = IsCapturedBoolean(result, "ok", false) && (status == "terminal_rescinded"
                    || status == "stale_material" && revision > previousRevision);
                if (!committed && !superseded) return false;
                work.Item.DailyCapturedRepairIntentJson = string.Empty;
                _capturedRepairIntentsOpenedHere.Remove(pending);
                return true;
            }).ConfigureAwait(false);
        }

        private static JObject ReadCapturedRepairIntent(string json)
        {
            try
            {
                var envelope = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                return TryReadCapturedRevision(envelope, "schema", out long schema) && schema == 1
                    && CapturedString(envelope, "phase") == "Captured" ? envelope : null;
            }
            catch (Newtonsoft.Json.JsonException) { return null; }
            catch (OverflowException) { return null; }
        }

        private static bool TryReadCapturedDay(JObject payload, out double day)
        {
            day = 0;
            var token = payload?["worldDay"];
            return (token?.Type == JTokenType.Integer || token?.Type == JTokenType.Float)
                && double.TryParse(token.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out day) && !double.IsInfinity(day) && !double.IsNaN(day) && day >= 0;
        }

        private static string CapturedString(JObject payload, string field) =>
            payload?[field]?.Type == JTokenType.String ? payload.Value<string>(field) : null;

        private static bool IsCapturedBoolean(JObject payload, string field, bool expected) =>
            payload?[field]?.Type == JTokenType.Boolean && payload.Value<bool>(field) == expected;

        private static bool TryReadCapturedRevision(JObject payload, string field, out long revision)
        {
            revision = -1;
            return payload?[field]?.Type == JTokenType.Integer && long.TryParse(
                payload[field].ToString(Newtonsoft.Json.Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out revision);
        }

        private static bool TryReadCapturedIntentIdentity(JObject payload, out long revision, out string intent)
        {
            intent = CapturedString(payload, "effectIntentId");
            if (!TryReadCapturedRevision(payload, "expectedMaterialRevision", out revision)
                || revision == long.MaxValue || string.IsNullOrEmpty(intent)) return false;
            string prefix = revision.ToString(CultureInfo.InvariantCulture) + "_";
            return intent.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(intent.Substring(prefix.Length), "N", out Guid id)
                && id != Guid.Empty && intent == prefix + id.ToString("N");
        }

        private static bool CapturedReplyMatches(JObject request, JObject response) =>
            TryReadCapturedRevision(response, "arrestEffectVersion", out long version) && version == 2
            && MatchesCaseEffectIdentity(request, response, "operation") && MatchesCaseEffectIdentity(request, response, "caseId")
            && MatchesCaseEffectIdentity(request, response, "reputationTagId");

        private static bool CapturedInspectionMatches(JObject request, JObject response) =>
            CapturedReplyMatches(request, response) && MatchesCaseEffectIdentity(request, response, "campaignId")
            && MatchesCaseEffectIdentity(request, response, "timelineId") && MatchesCaseEffectIdentity(request, response, "accusedHeroId")
            && MatchesCaseEffectIdentity(request, response, "playerHeroId")
            && CapturedString(response, "sharedActivityGeneration") == CapturedString(request, "sharedActivityGeneration");

        private static bool ApplyMeasuredCapturedRepair(ReignArrestCase item, JObject response)
        {
            var delta = response["appliedRelationshipDelta"];
            if (!IsCapturedBoolean(response, "ok", true) || !IsCapturedBoolean(response, "relationshipEffectResolved", true)
                || delta?.Type != JTokenType.Integer || !int.TryParse(delta.ToString(Newtonsoft.Json.Formatting.None),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int measured) || measured < -100 || measured > 100)
                return false;
            item.RelationshipDeltaApplied = measured;
            item.RelationshipEffectSynchronized = true;
            item.ReputationSynchronized = true;
            return true;
        }

        private enum ArrestEffectSyncStatus
        {
            Unverified,
            Confirmed,
            TerminalPartial,
            TerminalUnmeasured
        }

        private static JObject BuildArrestEffectRequest(ReignArrestCase item, Hero accused, bool rescind)
        {
            int desiredDelta = RelationshipDelta(item.Severity, item.CauseEstablished);
            return new JObject
            {
                ["operation"] = rescind ? "rescind" : "apply",
                ["caseId"] = item.CaseId,
                ["accusedHeroId"] = accused.StringId,
                ["playerHeroId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["relationshipDelta"] = desiredDelta,
                ["reputationTagId"] = item.ReputationTagId,
                ["reputationValue"] = item.ReputationValue,
                ["description"] = "Accused by " + (Hero.MainHero?.Name?.ToString() ?? "the ruler")
                    + " of " + item.RawAccusation + ".",
                ["chargeCategory"] = item.ChargeCategory,
                ["severity"] = SeverityName(item.Severity),
                ["causeEstablished"] = item.CauseEstablished,
                ["evidenceStatus"] = item.EvidenceStatus,
                ["evidenceIds"] = item.EvidenceIdsCsv,
            };
        }

        private (ArrestEffectSyncStatus Status, int? Remainder) SynchronizeCaseEffect(
            ReignArrestCase item, Hero accused, bool rescind)
        {
            if (item == null || accused == null) return (ArrestEffectSyncStatus.Unverified, null);
            JObject payload = BuildArrestEffectRequest(item, accused, rescind);
            payload["worldDay"] = CurrentDay();
            try
            {
                JObject response = ReignServerClient.ApplyArrestCaseEffectAsync(payload)
                    .GetAwaiter().GetResult();
                return ApplyCaseEffectResponse(item, payload, response, rescind);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Arrest case social synchronization deferred case=" + item.CaseId
                    + ": " + ex.Message);
                return MarkCaseEffectUnverified(item, rescind);
            }
        }

        private static (ArrestEffectSyncStatus Status, int? Remainder) ApplyCaseEffectResponse(
            ReignArrestCase item, JObject request, JObject response, bool rescind)
        {
            if (response?["ok"]?.Type != JTokenType.Boolean
                || !MatchesCaseEffectIdentity(request, response, "operation")
                || !MatchesCaseEffectIdentity(request, response, "caseId")
                || !MatchesCaseEffectIdentity(request, response, "reputationTagId"))
                return MarkCaseEffectUnverified(item, rescind);

            JToken resolved = response["relationshipEffectResolved"];
            if (resolved != null && resolved.Type != JTokenType.Boolean)
                return MarkCaseEffectUnverified(item, rescind);

            JToken amount = response["appliedRelationshipDelta"];
            int applied = 0;
            bool hasAmount = amount?.Type == JTokenType.Integer
                && int.TryParse(amount.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out applied)
                && applied >= -100 && applied <= 100;
            bool ok = (bool)response["ok"];
            bool? effectResolved = (bool?)resolved;
            if (ok && hasAmount && (rescind ? applied == 0 && effectResolved == true : effectResolved != false))
            {
                item.RelationshipDeltaApplied = applied;
                item.RelationshipEffectSynchronized = true;
                item.ReputationSynchronized = true;
                return (ArrestEffectSyncStatus.Confirmed, null);
            }

            // The legacy server confirmed the reputation update, but its nominal zero did not measure the reversal.
            bool legacyRescission = ok && effectResolved == null && hasAmount && applied == 0;
            bool terminalUnmeasured = !ok && effectResolved == false && amount?.Type == JTokenType.Null;
            bool terminalPartial = !ok && effectResolved == false && hasAmount && applied != 0;
            if (!rescind || (!legacyRescission && !terminalUnmeasured && !terminalPartial))
                return MarkCaseEffectUnverified(item, rescind);

            item.RelationshipDeltaApplied = 0;
            item.RelationshipEffectSynchronized = false;
            item.ReputationSynchronized = true;
            return terminalPartial ? (ArrestEffectSyncStatus.TerminalPartial, applied)
                : (ArrestEffectSyncStatus.TerminalUnmeasured, (int?)null);
        }

        private static bool MatchesCaseEffectIdentity(JObject request, JObject response, string key)
        {
            string expected = (string)request[key];
            JToken actual = response[key];
            return !string.IsNullOrEmpty(expected) && actual?.Type == JTokenType.String
                && string.Equals(expected, (string)actual, StringComparison.Ordinal);
        }

        private static (ArrestEffectSyncStatus Status, int? Remainder) MarkCaseEffectUnverified(
            ReignArrestCase item, bool rescind)
        {
            if (rescind) item.RelationshipDeltaApplied = 0;
            item.RelationshipEffectSynchronized = false;
            item.ReputationSynchronized = false;
            return (ArrestEffectSyncStatus.Unverified, null);
        }

        private static ReignArrestContextKind ResolveContext(Hero accused,
            out Settlement settlement, out MobileParty targetParty)
        {
            settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement
                ?? accused?.CurrentSettlement;
            targetParty = accused?.PartyBelongedTo;
            if (settlement != null && settlement.IsFortification
                && settlement.OwnerClan == Clan.PlayerClan)
                return ReignArrestContextKind.PlayerSettlement;
            if (targetParty == MobileParty.MainParty)
                return ReignArrestContextKind.PlayerParty;
            if (targetParty != null && targetParty.IsActive
                && targetParty.LeaderHero == accused
                && PlayerEncounter.EncounteredParty == targetParty.Party)
                return ReignArrestContextKind.PartyEncounter;
            return ReignArrestContextKind.Unknown;
        }

        private PartyBase ResolveCustodyParty(ReignArrestCase item)
        {
            if (item.ContextKind == ReignArrestContextKind.PlayerSettlement)
            {
                Settlement settlement = FindSettlement(item.SettlementStringId);
                return settlement != null && settlement.IsFortification
                    && settlement.OwnerClan == Clan.PlayerClan ? settlement.Party : null;
            }
            return PartyBase.MainParty;
        }

        private static bool IsPlayerCustody(PartyBase party)
        {
            if (party == null) return false;
            if (party == PartyBase.MainParty) return true;
            if (party.IsSettlement)
                return party.Settlement != null && party.Settlement.IsFortification
                    && party.Settlement.OwnerClan == Clan.PlayerClan;
            return party.IsMobile && party.MobileParty == MobileParty.MainParty;
        }

        private ReignArrestCase ResolveActionCase(ReignWorldActionRecord action, Hero accused,
            bool includeClosedAccusation = false)
        {
            if (accused == null) return null;

            string id = ReadString(ParseTerms(action?.TermsJson), "caseId", string.Empty);
            if (!string.IsNullOrWhiteSpace(id))
            {
                ReignArrestCase item = FindCase(id);
                return item != null && string.Equals(item.AccusedHeroStringId, accused.StringId,
                    StringComparison.OrdinalIgnoreCase) ? item : null;
            }

            return ActiveCaseFor(accused) ?? (includeClosedAccusation
                ? _cases.LastOrDefault(item => item.AccusationActive
                    && string.Equals(item.AccusedHeroStringId, accused.StringId, StringComparison.OrdinalIgnoreCase))
                : null);
        }

        private static void AddHistory(ReignArrestCase item, string phase, string summary, double? observedDay = null)
        {
            if (item == null) return;
            item.History = item.History ?? new List<string>();
            string entry = (observedDay ?? CurrentDay()).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "|" + (phase ?? string.Empty) + "|" + (summary ?? string.Empty);
            if (!item.History.Contains(entry)) item.History.Add(entry);
            if (item.History.Count > 120) item.History.RemoveRange(0, item.History.Count - 120);
        }

        private static void RecordHistory(ReignArrestCase item, Hero accused, string eventType,
            string phase, string summary, (ArrestEffectSyncStatus Status, int? Remainder)? effect = null)
        {
            var worldHistory = ReignServices.WorldHistory;
            if (worldHistory == null) return;

            var history = new JObject
            {
                ["caseId"] = item?.CaseId ?? string.Empty,
                ["accusedHeroId"] = accused?.StringId ?? string.Empty,
                ["accusation"] = item?.RawAccusation ?? string.Empty,
                ["chargeCategory"] = item?.ChargeCategory ?? string.Empty,
                ["severity"] = item == null ? string.Empty : SeverityName(item.Severity),
                ["causeEstablished"] = item?.CauseEstablished ?? false,
                ["reputationTagId"] = item?.ReputationTagId ?? string.Empty
            };
            WriteCaseEffectHistory(history, item, effect);
            worldHistory.RecordReignSystemEvent(
                eventType, phase, "justice", item?.CaseId ?? string.Empty,
                summary, "ordinary", Hero.MainHero?.StringId ?? string.Empty,
                Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                history, new JArray(accused?.StringId ?? string.Empty));
        }

        private static void WriteCaseEffectHistory(JObject history, ReignArrestCase item,
            (ArrestEffectSyncStatus Status, int? Remainder)? effect)
        {
            if (effect == null)
            {
                history["relationshipDelta"] = item?.RelationshipDeltaApplied ?? 0;
                return;
            }

            var (status, remainder) = effect.Value;
            history["relationshipEffectResolved"] = status == ArrestEffectSyncStatus.Confirmed;
            history["reputationRescissionConfirmed"] = status != ArrestEffectSyncStatus.Unverified;
            switch (status)
            {
                case ArrestEffectSyncStatus.Confirmed:
                    history["relationshipDelta"] = 0;
                    break;
                case ArrestEffectSyncStatus.TerminalPartial:
                    history["relationshipEffectStatus"] = "terminal_partial";
                    history["remainingRelationshipDelta"] = remainder.Value;
                    break;
                case ArrestEffectSyncStatus.TerminalUnmeasured:
                    history["relationshipEffectStatus"] = "terminal_unmeasured";
                    break;
                default:
                    history["relationshipEffectStatus"] = "unverified";
                    break;
            }
        }

        private static string ReadString(JObject obj, string key, string fallback)
        {
            return obj?[key]?.ToString() ?? fallback ?? string.Empty;
        }

        private static bool ReadBool(JObject obj, string key, bool fallback)
        {
            return obj?.Value<bool?>(key) ?? fallback;
        }

        private static ReignArrestChargeSeverity ParseSeverity(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "minor": return ReignArrestChargeSeverity.Minor;
                case "grave": return ReignArrestChargeSeverity.Grave;
                case "capital":
                case "treason": return ReignArrestChargeSeverity.Capital;
                default: return ReignArrestChargeSeverity.Serious;
            }
        }

        internal static string SeverityName(ReignArrestChargeSeverity value)
        {
            return value.ToString().ToLowerInvariant();
        }

        internal static string PhaseName(ReignArrestPhase value)
        {
            return value.ToString().ToLowerInvariant();
        }

        private static string ContextName(ReignArrestContextKind value)
        {
            switch (value)
            {
                case ReignArrestContextKind.PlayerSettlement: return "player_settlement";
                case ReignArrestContextKind.PlayerParty: return "player_party";
                case ReignArrestContextKind.PartyEncounter: return "party_encounter";
                default: return "unknown";
            }
        }

        private static float CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null
                ? 0f : (float)CampaignTime.Now.ToDays;
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.FindFirst(hero =>
                string.Equals(hero.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static Settlement FindSettlement(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Settlement.All.FirstOrDefault(item =>
                string.Equals(item.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string PartyStringId(PartyBase party)
        {
            if (party == null) return string.Empty;
            if (party.IsMobile) return party.MobileParty?.StringId ?? string.Empty;
            if (party.IsSettlement) return party.Settlement?.StringId ?? string.Empty;
            return string.Empty;
        }

        private static string CompactId(string id)
        {
            string value = new string((id ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            return value.Length <= 24 ? value : value.Substring(value.Length - 24);
        }
    }
}
