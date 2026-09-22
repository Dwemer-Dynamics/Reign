using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed class ReignArrestCampaignBehavior : CampaignBehaviorBase
    {
        private List<ReignArrestCase> _cases = new List<ReignArrestCase>();
        private float _lastEvidenceReviewDay = -1000f;

        public static ReignArrestCampaignBehavior Instance { get; private set; }

        public ReignArrestCampaignBehavior()
        {
            Instance = this;
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
            dataStore.SyncData("_reign_arrest_cases_v1", ref _cases);
            dataStore.SyncData("_reign_arrest_evidence_review_day", ref _lastEvidenceReviewDay);
            NormalizeLoadedState();
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
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
            ReignArrestCase item = ResolveActionCase(action, accused)
                ?? _cases.LastOrDefault(candidate => candidate.AccusationActive
                    && string.Equals(candidate.AccusedHeroStringId, accused?.StringId,
                        StringComparison.OrdinalIgnoreCase));
            if (item == null || accused == null)
                return ReignActionResult.ValidationFailed("No arrest case was found for this character.");

            if (rescind)
            {
                item.AccusationActive = false;
                item.Phase = ReignArrestPhase.Rescinded;
                item.ReleasedDay = CurrentDay();
                item.LastOutcome = "accusation_rescinded";
                AddHistory(item, "rescinded", "The player rescinded the accusation and cleared the accused's name.");
                SynchronizeCaseEffect(item, accused, true);
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
                    : "The player released " + accused.Name + " while leaving the accusation on record.");
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
            if (day - _lastEvidenceReviewDay < 0.95f) return;
            _lastEvidenceReviewDay = day;
            foreach (ReignArrestCase item in _cases.Where(candidate => candidate.AccusationActive).ToList())
            {
                Hero accused = FindHero(item.AccusedHeroStringId);
                if (accused == null) continue;
                if (!item.CauseEstablished && item.IsOpen)
                {
                    EvaluateCause(item, accused);
                    if (item.CauseEstablished)
                    {
                        item.EvidenceStatus = "established_after_arrest";
                        item.RelationshipEffectSynchronized = false;
                        AddHistory(item, "evidence_reclassified", "Later authoritative evidence established cause without revealing it earlier.");
                        SynchronizeCaseEffect(item, accused, false);
                        RecordHistory(item, accused, "arrest_evidence_reclassified", "evidence_established",
                            "Later disclosed evidence established cause in the arrest case against " + accused.Name + ".");
                    }
                }
                if ((!item.RelationshipEffectSynchronized || !item.ReputationSynchronized)
                    && item.Phase == ReignArrestPhase.Captured)
                {
                    SynchronizeCaseEffect(item, accused, false);
                }
            }
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
            RecordHistory(item, accused, "arrest_captured", "captured",
                accused.Name + " was imprisoned on the player's accusation: " + item.RawAccusation + ".");
        }

        private void EvaluateCause(ReignArrestCase item, Hero accused)
        {
            if (HasExposedAgentEvidence(accused, out string evidence))
            {
                item.CauseEstablished = true;
                item.EvidenceStatus = "established";
                item.EvidenceIdsCsv = evidence;
                return;
            }
            try
            {
                JObject response = ReignServerClient.EvaluateArrestEvidenceAsync(new JObject
                {
                    ["accusedHeroId"] = accused?.StringId ?? string.Empty,
                    ["chargeCategory"] = item?.ChargeCategory ?? string.Empty,
                    ["accusation"] = item?.RawAccusation ?? string.Empty
                }).GetAwaiter().GetResult();
                if (response.Value<bool?>("ok") == true
                    && response.Value<bool?>("causeEstablished") == true)
                {
                    item.CauseEstablished = true;
                    item.EvidenceStatus = response.Value<string>("evidenceStatus") ?? "established";
                    item.EvidenceIdsCsv = string.Join(",", (response["evidenceIds"] as JArray
                        ?? new JArray()).Values<string>());
                    return;
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Arrest evidence review deferred for "
                    + (accused?.StringId ?? string.Empty) + ": " + ex.Message);
            }
            item.CauseEstablished = false;
            item.EvidenceStatus = "unsupported";
            item.EvidenceIdsCsv = string.Empty;
        }

        private bool HasExposedAgentEvidence(Hero accused, out string evidence)
        {
            evidence = string.Empty;
#if !REIGN_EXCLUDE_COURT
            ReignSpymasterState state = ReignCourtCampaignBehavior.Instance?.SpymasterState;
            ReignForeignAgentRecord agent = state?.ForeignAgents?.FirstOrDefault(candidate => candidate.Exposed
                && string.Equals(candidate.AgentHeroStringId, accused?.StringId,
                    StringComparison.OrdinalIgnoreCase));
            if (agent == null) return false;
            List<string> ids = new List<string> { "foreign_agent:" + agent.AgentHeroStringId };
            ids.AddRange((state.ForeignAgentActions ?? new List<ReignForeignAgentActionRecord>())
                .Where(action => action.Detected && string.Equals(action.AgentHeroStringId,
                    agent.AgentHeroStringId, StringComparison.OrdinalIgnoreCase))
                .Select(action => "foreign_agent_action:" + action.ActionId));
            evidence = string.Join(",", ids.Distinct(StringComparer.OrdinalIgnoreCase));
            return true;
#else
            return false;
#endif
        }

        private void SynchronizeCaseEffect(ReignArrestCase item, Hero accused, bool rescind)
        {
            if (item == null || accused == null) return;
            int desiredDelta = RelationshipDelta(item.Severity, item.CauseEstablished);
            JObject payload = new JObject
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
                ["worldDay"] = CurrentDay()
            };
            try
            {
                JObject response = ReignServerClient.ApplyArrestCaseEffectAsync(payload)
                    .GetAwaiter().GetResult();
                if (response.Value<bool?>("ok") == true)
                {
                    item.RelationshipDeltaApplied = response.Value<int?>("appliedRelationshipDelta")
                        ?? desiredDelta;
                    item.RelationshipEffectSynchronized = true;
                    item.ReputationSynchronized = true;
                }
            }
            catch (Exception ex)
            {
                item.RelationshipEffectSynchronized = false;
                item.ReputationSynchronized = false;
                ReignLog.Warn("Arrest case social synchronization deferred case=" + item.CaseId
                    + ": " + ex.Message);
            }
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

        private ReignArrestCase ResolveActionCase(ReignWorldActionRecord action, Hero accused)
        {
            string id = ReadString(ParseTerms(action?.TermsJson), "caseId", string.Empty);
            return !string.IsNullOrWhiteSpace(id) ? FindCase(id) : ActiveCaseFor(accused);
        }

        private static void AddHistory(ReignArrestCase item, string phase, string summary)
        {
            if (item == null) return;
            item.History = item.History ?? new List<string>();
            string entry = CurrentDay().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "|" + (phase ?? string.Empty) + "|" + (summary ?? string.Empty);
            if (!item.History.Contains(entry)) item.History.Add(entry);
            if (item.History.Count > 120) item.History.RemoveRange(0, item.History.Count - 120);
        }

        private static void RecordHistory(ReignArrestCase item, Hero accused, string eventType,
            string phase, string summary)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(
                eventType, phase, "justice", item?.CaseId ?? string.Empty,
                summary, "ordinary", Hero.MainHero?.StringId ?? string.Empty,
                Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                new JObject
                {
                    ["caseId"] = item?.CaseId ?? string.Empty,
                    ["accusedHeroId"] = accused?.StringId ?? string.Empty,
                    ["accusation"] = item?.RawAccusation ?? string.Empty,
                    ["chargeCategory"] = item?.ChargeCategory ?? string.Empty,
                    ["severity"] = item == null ? string.Empty : SeverityName(item.Severity),
                    ["causeEstablished"] = item?.CauseEstablished ?? false,
                    ["relationshipDelta"] = item?.RelationshipDeltaApplied ?? 0,
                    ["reputationTagId"] = item?.ReputationTagId ?? string.Empty
                },
                new JArray(accused?.StringId ?? string.Empty));
        }

        private static JObject ParseTerms(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch { return new JObject(); }
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
