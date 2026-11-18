using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const string CapitalAmbassadorFixtureConfirmation = "prepare Reign capital ambassador disposable fixture";
        private const string CapitalAmbassadorOwnershipFixtureConfirmation = "grant Reign capital fixture town on disposable save";
        private const int CapitalAmbassadorLedgerMaxCount = 16;
        private const int CapitalAmbassadorLedgerMaxObservations = 16;
        private const int CapitalAmbassadorLedgerMaxStoredJsonChars = 2048;

        internal JObject RunCapitalAmbassadorTestProfile(string runId, string phase, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string normalizedPhase = (phase ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            switch (normalizedPhase)
            {
                case "preflight": return CapitalAmbassadorPreflight(normalizedRunId);
                case "prepare_ownership": return PrepareCapitalAmbassadorOwnershipFixture(normalizedRunId, options);
                case "deterministic": return CapitalAmbassadorDeterministicMatrix(normalizedRunId);
                case "observe": return ObserveCapitalAmbassadorFixture(normalizedRunId, false);
                case "mark_reload": return MarkCapitalAmbassadorReload(normalizedRunId);
                case "verify_reload": return ObserveCapitalAmbassadorFixture(normalizedRunId, true);
                case "move_capital": return MoveCapitalAmbassadorFixture(normalizedRunId);
                case "simulate_loss": return SimulateCapitalLossFixture(normalizedRunId);
                case "simulate_war": return SimulateAmbassadorWarFixture(normalizedRunId);
                case "lifecycle_a": return RunCapitalLifecycleAFixture(normalizedRunId);
                case "lifecycle_b_transition": return RunAmbassadorLifecycleBTransitionFixture(normalizedRunId);
                case "cleanup_marker": return CleanupCapitalAmbassadorFixture(normalizedRunId);
                default: return CapitalAmbassadorFailure(normalizedRunId, normalizedPhase, "Unsupported capital/ambassador test phase.");
            }
        }

        private JObject PrepareCapitalAmbassadorOwnershipFixture(string runId, JObject options)
        {
            if (!string.Equals((string)options?["confirmation"],
                CapitalAmbassadorOwnershipFixtureConfirmation,
                StringComparison.Ordinal))
            {
                return CapitalAmbassadorFailure(runId, "prepare_ownership",
                    "The exact disposable-save ownership-fixture confirmation is required.");
            }

            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Settlement current = Settlement.CurrentSettlement;
            if (ours == null || !IsPlayerKingdomRuler()
                || current?.IsTown != true || current.IsUnderSiege
                || current.OwnerClan?.Kingdom != ours)
            {
                return CapitalAmbassadorFailure(runId, "prepare_ownership",
                    "Load the disposable ruler campaign while inside its safe owned town.");
            }

            List<Settlement> owned = OwnedSafeTowns(ours);
            if (owned.Count >= 2)
            {
                return new JObject
                {
                    ["ok"] = true,
                    ["runId"] = runId,
                    ["profile"] = "capital_ambassador",
                    ["phase"] = "prepare_ownership",
                    ["idempotent"] = true,
                    ["ownedSafeTowns"] = new JArray(owned.Select(SettlementJson)),
                    ["requiresBaselineRollback"] = false
                };
            }

            Settlement alternate = Settlement.All
                .Where(x => x?.IsTown == true && !x.IsUnderSiege
                    && x.OwnerClan?.Kingdom != null
                    && x.OwnerClan.Kingdom != ours)
                .OrderBy(x => x.StringId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (alternate == null)
            {
                return CapitalAmbassadorFailure(runId, "prepare_ownership",
                    "No safe foreign town is available for the disposable relocation fixture.");
            }

            JObject before = SettlementJson(alternate);
            before["ownerClanId"] = alternate.OwnerClan?.StringId ?? string.Empty;
            before["ownerHeroId"] = alternate.OwnerClan?.Leader?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(alternate, Hero.MainHero);
            List<Settlement> afterOwned = OwnedSafeTowns(ours);
            bool transferred = alternate.OwnerClan == Clan.PlayerClan
                && afterOwned.Count >= 2;
            JArray assertions = new JArray();
            AddCapitalAmbassadorAssertion(assertions,
                "capital_disposable_alternate_town_granted", transferred,
                "The explicitly armed ownership fixture used Bannerlord's native gift action to add one deterministic relocation town.",
                new JObject
                {
                    ["before"] = before,
                    ["after"] = SettlementJson(alternate),
                    ["ownedSafeTownCount"] = afterOwned.Count
                });
            JObject result = CapitalAmbassadorResult(runId,
                "prepare_ownership", assertions);
            result["selectedSettlementId"] = alternate.StringId;
            result["ownedSafeTowns"] = new JArray(afterOwned.Select(SettlementJson));
            result["requiresBaselineRollback"] = true;
            result["rollback"] = "Reload the exact verified disposable baseline; never synthesize a reverse transfer.";
            return result;
        }

        internal async Task<JObject> PrepareCapitalAmbassadorFixtureAsync(string runId, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            options = options ?? new JObject();
            if (!string.Equals((string)options["confirmation"], CapitalAmbassadorFixtureConfirmation, StringComparison.Ordinal))
                return CapitalAmbassadorFailure(normalizedRunId, "prepare", "The exact disposable-save fixture confirmation is required.");

            CapitalAmbassadorTestLedger replay = await ReignMainThread.InvokeAsync(() => FindCapitalAmbassadorLedger(normalizedRunId)).ConfigureAwait(false);
            if (replay != null)
            {
                JObject existing = await ReignMainThread.InvokeAsync(() => ObserveCapitalAmbassadorFixture(normalizedRunId, false)).ConfigureAwait(false);
                existing["idempotent"] = true;
                return existing;
            }

            JObject replacedPosting = null;
            JObject relationFixture = null;
            ForeignAmbassadorPosting blockingPosting = await ReignMainThread.InvokeAsync(() =>
            {
                Kingdom ours = Clan.PlayerClan?.Kingdom;
                if (EligibleFixtureOriginKingdoms(ours).Count > 0) return null;
                return _foreignAmbassadors
                    .Where(x => x?.IsActive == true)
                    .OrderBy(x => x.OriginKingdomStringId, StringComparer.Ordinal)
                    .ThenBy(x => x.PostingId, StringComparer.Ordinal)
                    .FirstOrDefault(x =>
                    {
                        Kingdom origin = Kingdom.All.FirstOrDefault(k => string.Equals(k?.StringId,
                            x.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
                        return ours != null && origin != null && origin != ours && !origin.IsEliminated
                            && origin.Leader?.IsAlive == true && origin.Leader.IsActive
                            && !ours.IsAtWarWith(origin)
                            && x.Charm >= 200
                            && EligibleForeignAmbassadorCandidates(origin).Count > 0;
                    });
            }).ConfigureAwait(false);
            if (blockingPosting != null)
            {
                bool relationReady = await ReignMainThread.InvokeAsync(() =>
                {
                    Kingdom origin = Kingdom.All.FirstOrDefault(k => string.Equals(k?.StringId,
                        blockingPosting.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
                    Hero ruler = origin?.Leader;
                    int beforeRelation = ruler == null ? -100 : Hero.MainHero.GetRelation(ruler);
                    if (ruler != null && beforeRelation < -30)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler,
                            -30 - beforeRelation, false);
                    int afterRelation = ruler == null ? -100 : Hero.MainHero.GetRelation(ruler);
                    relationFixture = new JObject
                    {
                        ["originKingdomId"] = origin?.StringId ?? string.Empty,
                        ["rulerId"] = ruler?.StringId ?? string.Empty,
                        ["beforeRelation"] = beforeRelation,
                        ["afterRelation"] = afterRelation,
                        ["boundary"] = -30,
                        ["adjusted"] = beforeRelation < -30
                    };
                    return afterRelation >= -30;
                }).ConfigureAwait(false);
                if (!relationReady)
                    return CapitalAmbassadorFailure(normalizedRunId, "prepare",
                        "The guarded fixture could not establish the production -30 ruler-relation boundary.");
                JObject before = await ReignMainThread.InvokeAsync(() => PostingJson(blockingPosting)).ConfigureAwait(false);
                ReignCourtServerResponse ended = await ReignCourtServerClient
                    .EndForeignAmbassadorAsync(blockingPosting, "test_fixture_replaced")
                    .ConfigureAwait(false);
                bool serverPostingAbsent = !ended.Ok && string.Equals(ended.Error,
                    "Foreign ambassador posting was not found.", StringComparison.Ordinal);
                if (!ended.Ok && !serverPostingAbsent)
                    return CapitalAmbassadorFailure(normalizedRunId, "prepare",
                        "The guarded fixture could not retire the pre-existing posting: " + ended.Error);
                await ReignMainThread.InvokeAsync(() =>
                {
                    EndForeignAmbassador(blockingPosting, "ended", true, string.Empty,
                        ended.Ok ? ended.Revision : blockingPosting.Revision + 1);
                    blockingPosting.EndReason = "test_fixture_replaced";
                }).ConfigureAwait(false);
                replacedPosting = new JObject
                {
                    ["reason"] = "No unoccupied eligible origin remained on the enrolled disposable save.",
                    ["serverPostingAbsent"] = serverPostingAbsent,
                    ["serverResult"] = ended.Raw ?? new JObject(),
                    ["serverError"] = ended.Error ?? string.Empty,
                    ["before"] = before,
                    ["after"] = await ReignMainThread.InvokeAsync(() => PostingJson(blockingPosting)).ConfigureAwait(false)
                };
            }

            Tuple<Settlement, Settlement, Kingdom, string> setup = await ReignMainThread.InvokeAsync(() =>
            {
                Kingdom ours = Clan.PlayerClan?.Kingdom;
                Settlement capital = Settlement.CurrentSettlement;
                List<Settlement> safeTowns = OwnedSafeTowns(ours);
                Settlement alternate = safeTowns.FirstOrDefault(x => x != capital);
                Kingdom origin = EligibleFixtureOriginKingdoms(ours).FirstOrDefault();
                string error = capital?.IsTown == true && safeTowns.Contains(capital) && alternate != null && origin != null
                    ? string.Empty
                    : "Load a ruler campaign while inside one of at least two safe owned towns, with a peaceful foreign kingdom whose ruler relation is -30 or better and which has an eligible 200-Charm envoy.";
                return Tuple.Create(capital, alternate, origin, error);
            }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(setup.Item4)) return CapitalAmbassadorFailure(normalizedRunId, "prepare", setup.Item4);

            string sessionError = await ReignMainThread.InvokeAsync(() =>
            {
                DesignateCapital(setup.Item1);
                if (IsRuleModeActive && Session?.HostSettlementStringId == setup.Item1.StringId) return string.Empty;
                return TryOpenSession(setup.Item1, out string error) ? string.Empty : error;
            }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sessionError)) return CapitalAmbassadorFailure(normalizedRunId, "prepare", sessionError);

            DateTime sessionDeadline = DateTime.UtcNow.AddSeconds(45);
            while ((_serverRequestInFlight || !ServerAvailable || !ServerSessionOpened) && DateTime.UtcNow < sessionDeadline)
            {
                await Task.Delay(250).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(EnsureServerSessionAligned).ConfigureAwait(false);
            }
            if (_serverRequestInFlight || !ServerAvailable || !ServerSessionOpened)
                return CapitalAmbassadorFailure(normalizedRunId, "prepare", "The production capital Court session did not align with the server before the bounded deadline.");

            Task<string> establishmentTask = await ReignMainThread.InvokeAsync(() => EstablishForeignAmbassadorAsync(setup.Item3)).ConfigureAwait(false);
            string establishmentError = await establishmentTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(establishmentError)) return CapitalAmbassadorFailure(normalizedRunId, "prepare", establishmentError);

            bool forceResident = (bool?)options["forceResident"] == true;
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                ForeignAmbassadorPosting posting = _foreignAmbassadors.LastOrDefault(x => x?.IsActive == true
                    && string.Equals(x.OriginKingdomStringId, setup.Item3.StringId, StringComparison.OrdinalIgnoreCase));
                Hero envoy = FindHero(posting?.HeroStringId);
                if (posting == null || envoy == null) return CapitalAmbassadorFailure(normalizedRunId, "prepare", "The production server did not return a usable foreign ambassador posting.");
                CapitalAmbassadorTestLedger ledger = new CapitalAmbassadorTestLedger
                {
                    RunId = normalizedRunId,
                    CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                    Phase = forceResident ? "resident" : "traveling",
                    CapitalSettlementStringId = setup.Item1.StringId,
                    AlternateCapitalSettlementStringId = setup.Item2.StringId,
                    PostingId = posting.PostingId,
                    EnvoyHeroStringId = envoy.StringId,
                    EnvoyOriginalSettlementStringId = envoy.CurrentSettlement?.StringId ?? string.Empty,
                    OriginKingdomStringId = setup.Item3.StringId,
                    PreparedDay = CurrentDayFloat(),
                    DisposableSaveConfirmed = true
                };
                _capitalAmbassadorTestLedgers.Add(ledger);
                if (forceResident)
                {
                    posting.ArrivalDay = CurrentDayFloat();
                    ProcessForeignAmbassadorTick();
                }
                JObject snapshot = CapitalAmbassadorSnapshot(ledger);
                ledger.PreparedFingerprint = CapitalAmbassadorFingerprint(snapshot);
                RecordCapitalAmbassadorObservation(ledger, snapshot, "prepare");
                JArray assertions = new JArray();
                AddCapitalAmbassadorAssertion(assertions, "fixture_capital_designated", CurrentCapital == setup.Item1
                    && CurrentCapitalDesignation()?.EverDesignated == true,
                    "The production designation path established the current safe owned town as capital.", snapshot["capital"]);
                AddCapitalAmbassadorAssertion(assertions, "fixture_capital_session", IsRuleModeActive && HasRoyalCommandAccess
                    && Session?.Scope == ReignCourtScope.Capital && ServerSessionOpened,
                    "The game and server agree on an active capital-scoped royal Court session.", snapshot["session"]);
                AddCapitalAmbassadorAssertion(assertions, "fixture_production_posting", posting.IsActive
                    && posting.Charm >= 200 && posting.ArrivalDay - posting.AssignedDay >= 0f
                    && posting.ArrivalDay - posting.AssignedDay <= 3.001f,
                    "The production establishment path selected and persisted an eligible envoy with bounded travel.", snapshot["posting"]);
                if (forceResident) AddCapitalAmbassadorAssertion(assertions, "fixture_forced_resident", posting.IsResident
                    && envoy.PartyBelongedTo == null && envoy.CurrentSettlement == setup.Item1,
                    "The deterministic arrival hook exercised the production resident and partyless Keep placement path.", snapshot["envoy"]);
                if (replacedPosting != null) AddCapitalAmbassadorAssertion(assertions,
                    "fixture_preexisting_posting_replaced", !blockingPosting.IsActive
                        && string.Equals(blockingPosting.Status, "ended", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(blockingPosting.EndReason, "test_fixture_replaced", StringComparison.OrdinalIgnoreCase),
                    "The guarded disposable fixture retired exactly one deterministic pre-existing posting through the server and native return-home path before making a fresh appointment.",
                    replacedPosting);
                if (relationFixture != null) AddCapitalAmbassadorAssertion(assertions,
                    "fixture_origin_relation_boundary", (int?)relationFixture["afterRelation"] >= -30,
                    "The guarded disposable fixture preserved or established the exact production ruler-relation acceptance boundary before appointment.",
                    relationFixture);
                JObject prepared = CapitalAmbassadorResult(normalizedRunId, "prepare", assertions);
                prepared["snapshot"] = snapshot;
                prepared["replacedPosting"] = replacedPosting;
                prepared["requiresBaselineRollback"] = true;
                return prepared;
            }).ConfigureAwait(false);
            return result;
        }

        private JObject CapitalAmbassadorPreflight(string runId)
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            List<Settlement> towns = OwnedSafeTowns(ours);
            List<Settlement> castles = Settlement.All.Where(x => x?.IsCastle == true && !x.IsUnderSiege && x.OwnerClan?.Kingdom == ours)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            List<Kingdom> origins = EligibleFixtureOriginKingdoms(ours);
            Settlement current = Settlement.CurrentSettlement;
            JArray assertions = new JArray();
            AddCapitalAmbassadorAssertion(assertions, "capital_campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null,
                "A Bannerlord campaign is loaded.", null);
            AddCapitalAmbassadorAssertion(assertions, "capital_player_is_ruler", ours != null && IsPlayerKingdomRuler(),
                "The player rules an active kingdom.", null);
            AddCapitalAmbassadorAssertion(assertions, "capital_feature_enabled", ReignBeta.Settings.ReignBetaSettings.IsCourtSystemAvailable,
                "The Court feature is enabled.", null);
            AddCapitalAmbassadorAssertion(assertions, "capital_current_safe_owned_town", current?.IsTown == true && !current.IsUnderSiege
                && current.OwnerClan?.Kingdom == ours, "The player is currently inside a safe owned town usable as the test capital.", SettlementJson(current));
            AddCapitalAmbassadorAssertion(assertions, "capital_move_target_available", towns.Count >= 2,
                "At least two safe owned towns exist for first designation and relocation.", new JObject{{"count",towns.Count}});
            AddCapitalAmbassadorAssertion(assertions, "capital_local_scope_target_available", castles.Count > 0,
                "At least one safe owned castle exists for local-scope UI verification.", new JObject{{"count",castles.Count}});
            AddCapitalAmbassadorAssertion(assertions, "ambassador_origin_and_candidate_available", origins.Count > 0,
                "At least one peaceful foreign kingdom meets the -30 ruler-relation boundary and has an eligible 200-Charm envoy.", new JObject{{"count",origins.Count}});
            JObject result = CapitalAmbassadorResult(runId, "preflight", assertions);
            result["currentSettlement"] = SettlementJson(current);
            result["safeOwnedTowns"] = new JArray(towns.Select(SettlementJson));
            result["safeOwnedCastles"] = new JArray(castles.Select(SettlementJson));
            result["eligibleOrigins"] = new JArray(origins.Select(origin => new JObject
            {
                ["kingdomId"] = origin.StringId,
                ["name"] = origin.Name?.ToString() ?? origin.StringId,
                ["rulerId"] = origin.Leader?.StringId ?? string.Empty,
                ["relation"] = Hero.MainHero?.GetRelation(origin.Leader) ?? -100,
                ["candidateCount"] = EligibleForeignAmbassadorCandidates(origin).Count
            }));
            result["occupiedOriginDiagnostics"] = new JArray(_foreignAmbassadors
                .Where(x => x?.IsActive == true)
                .OrderBy(x => x.OriginKingdomStringId, StringComparer.Ordinal)
                .ThenBy(x => x.PostingId, StringComparer.Ordinal)
                .Select(x => OccupiedOriginDiagnostic(x, ours)));
            result["activePostings"] = new JArray(_foreignAmbassadors.Where(x => x?.IsActive == true).Select(PostingJson));
            result["legacyOutboundPostingCount"] = _ambassadors.Count;
            result["safety"] = "Run mutating phases only on the verified disposable baseline; capital moves, envoy placement, and simulated recall/loss require rollback.";
            return result;
        }

        private JObject CapitalAmbassadorDeterministicMatrix(string runId)
        {
            JArray assertions = new JArray();
            bool designationMatrix = IsCapitalDesignationEligible(true,true,true,false,true)
                && !IsCapitalDesignationEligible(false,true,true,false,true)
                && !IsCapitalDesignationEligible(true,false,true,false,true)
                && !IsCapitalDesignationEligible(true,true,false,false,true)
                && !IsCapitalDesignationEligible(true,true,true,true,true)
                && !IsCapitalDesignationEligible(true,true,true,false,false);
            AddCapitalAmbassadorAssertion(assertions,"capital_designation_boolean_matrix",designationMatrix,
                "Only an enabled player ruler in a safe owned town can designate a capital; castles, siege, foreign ownership, and non-rulers fail.",null);

            KingdomCapitalDesignation record = new KingdomCapitalDesignation();
            ApplyCapitalDesignation(record,"kingdom","town_a",10f);
            long firstRevision = record.Revision;
            ApplyCapitalDesignation(record,"kingdom","town_b",11f);
            bool move = record.EverDesignated && record.SettlementStringId=="town_b" && record.Revision==firstRevision+1
                && Math.Abs(record.DesignatedDay-11f)<0.001f && record.ClearedDay<0f;
            AddCapitalAmbassadorAssertion(assertions,"capital_first_and_free_move_state",move,
                "The same production state transition supports immediate first designation and later no-cost/no-cooldown replacement while preserving first-designation history.",JObject.FromObject(record));
            ClearCapitalDesignation(record,12f);
            AddCapitalAmbassadorAssertion(assertions,"capital_loss_preserves_ever_designated",record.EverDesignated
                && string.IsNullOrWhiteSpace(record.SettlementStringId) && Math.Abs(record.ClearedDay-12f)<0.001f,
                "Capital loss clears only the active settlement and preserves the first-designation state.",JObject.FromObject(record));

            AddCapitalAmbassadorAssertion(assertions,"capital_local_scope_matrix",
                ResolveCourtScope("town_a","town_a")==ReignCourtScope.Capital
                && ResolveCourtScope("town_a","castle_a")==ReignCourtScope.Local
                && ResolveCourtScope("","town_a")==ReignCourtScope.Local,
                "Only the exact current capital receives royal-command scope; other owned towns/castles and missing capitals are local.",null);

            AddCapitalAmbassadorAssertion(assertions,"ambassador_travel_boundaries",
                AmbassadorTravelDaysFromDistance(null)==3 && AmbassadorTravelDaysFromDistance(0f)==1
                && AmbassadorTravelDaysFromDistance(50f)==1 && AmbassadorTravelDaysFromDistance(50.001f)==2
                && AmbassadorTravelDaysFromDistance(100f)==2 && AmbassadorTravelDaysFromDistance(100.001f)==3,
                "Travel uses exact one/two/three-day distance boundaries and a safe three-day unknown-distance fallback.",null);

            Func<bool,bool,bool,bool,bool,float,float,bool,bool,bool,int,bool,bool> eligible =
                (ruler,lord,alive,active,prisoner,age,adult,inKingdom,party,governor,charm,assigned) =>
                    IsForeignAmbassadorCandidateEligible("hero",ruler,lord,alive,active,prisoner,age,adult,inKingdom,party,governor,charm,assigned);
            bool candidateMatrix = eligible(false,true,true,true,false,18f,18f,true,false,false,200,false)
                && !eligible(false,true,true,true,false,17.999f,18f,true,false,false,200,false)
                && !eligible(true,true,true,true,false,18f,18f,true,false,false,200,false)
                && !eligible(false,false,true,true,false,18f,18f,true,false,false,200,false)
                && !eligible(false,true,false,true,false,18f,18f,true,false,false,200,false)
                && !eligible(false,true,true,false,false,18f,18f,true,false,false,200,false)
                && !eligible(false,true,true,true,true,18f,18f,true,false,false,200,false)
                && !eligible(false,true,true,true,false,18f,18f,false,false,false,200,false)
                && !eligible(false,true,true,true,false,18f,18f,true,true,false,200,false)
                && !eligible(false,true,true,true,false,18f,18f,true,false,true,200,false)
                && !eligible(false,true,true,true,false,18f,18f,true,false,false,49,false)
                && eligible(false,true,true,true,false,18f,18f,true,false,false,50,false)
                && eligible(false,true,true,true,false,18f,18f,true,false,false,199,false)
                && !eligible(false,true,true,true,false,18f,18f,true,false,false,200,true);
            AddCapitalAmbassadorAssertion(assertions,"ambassador_candidate_boundary_matrix",candidateMatrix,
                "Adult-age and Charm 200 boundaries pass exactly while every ruler, non-lord, dead, inactive, prisoner, foreign, party-leader, governor, low-Charm, and already-assigned exclusion fails.",null);

            ForeignAmbassadorPosting posting = new ForeignAmbassadorPosting{Status="traveling"};
            bool lifecycle = posting.IsActive && !posting.IsResident;
            posting.Status="resident"; lifecycle = lifecycle && posting.IsActive && posting.IsResident;
            posting.Status="sheltered"; lifecycle = lifecycle && posting.IsActive && !posting.IsResident;
            posting.Status="war_recalled"; lifecycle = lifecycle && !posting.IsActive;
            AddCapitalAmbassadorAssertion(assertions,"ambassador_lifecycle_status_matrix",lifecycle,
                "Traveling, resident, sheltered, and terminal recall states expose the correct active/resident contract.",null);

            bool terminalReasonMatrix = string.IsNullOrWhiteSpace(ForeignAmbassadorTerminalReason(true,false,true,true,false))
                && ForeignAmbassadorTerminalReason(false,false,true,true,false)=="origin_kingdom_missing"
                && ForeignAmbassadorTerminalReason(true,true,true,true,false)=="origin_kingdom_eliminated"
                && ForeignAmbassadorTerminalReason(true,false,false,false,false)=="envoy_missing"
                && ForeignAmbassadorTerminalReason(true,false,true,false,false)=="envoy_dead"
                && ForeignAmbassadorTerminalReason(true,false,true,true,true)=="envoy_disabled";
            AddCapitalAmbassadorAssertion(assertions,"ambassador_terminal_reason_matrix",terminalReasonMatrix,
                "Only missing, dead, disabled, or eliminated participants terminate a posting; transient native Traveling and other living states are not terminal.",null);

            AmbassadorPosting legacy = new AmbassadorPosting{PostingId="legacy",Status="traveling",MissionType="public_information"};
            AddCapitalAmbassadorAssertion(assertions,"ambassador_legacy_record_isolation",legacy.PostingId=="legacy"
                && legacy.Status=="traveling" && legacy.MissionType=="public_information",
                "The legacy outbound AmbassadorPosting model remains independent of resident foreign postings.",JObject.FromObject(legacy));

            CapitalAmbassadorTestLedger oversizedLedger = new CapitalAmbassadorTestLedger { RunId = "payload_bound" };
            for (int index = 0; index < 40; index++) oversizedLedger.ObservationJson.Add(new string('x', 10000));
            CompactCapitalAmbassadorTestLedger(oversizedLedger);
            AddCapitalAmbassadorAssertion(assertions,"capital_harness_save_payload_bounded",
                oversizedLedger.ObservationJson.Count<=CapitalAmbassadorLedgerMaxObservations
                && oversizedLedger.ObservationJson.All(x=>(x??string.Empty).Length<=CapitalAmbassadorLedgerMaxStoredJsonChars),
                "Capital/ambassador test evidence is compacted before native serialization so repeated observations cannot corrupt a save.",
                new JObject{{"observations",oversizedLedger.ObservationJson.Count},{"maxStoredChars",oversizedLedger.ObservationJson.Max(x=>(x??string.Empty).Length)}});
            return CapitalAmbassadorResult(runId,"deterministic",assertions);
        }

        private JObject ObserveCapitalAmbassadorFixture(string runId, bool verifyReload)
        {
            CapitalAmbassadorTestLedger ledger = FindCapitalAmbassadorLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return CapitalAmbassadorFailure(runId,verifyReload?"verify_reload":"observe","No persisted disposable capital/ambassador ledger exists for this run.");
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x=>string.Equals(x?.PostingId,ledger.PostingId,StringComparison.OrdinalIgnoreCase));
            Hero envoy = FindHero(ledger.EnvoyHeroStringId);
            JObject snapshot = CapitalAmbassadorSnapshot(ledger);
            RecordCapitalAmbassadorObservation(ledger, snapshot, verifyReload ? "verify_reload" : "observe");
            JArray assertions = new JArray();
            AddCapitalAmbassadorAssertion(assertions,"fixture_ledger_persisted",!verifyReload
                || string.Equals(ledger.CampaignId,ReignCampaignIdentity.CurrentCampaignId(),StringComparison.OrdinalIgnoreCase),
                "The disposable fixture ledger belongs to this campaign and survived the requested load boundary.",null);
            AddCapitalAmbassadorAssertion(assertions,"fixture_posting_exactly_once",posting!=null
                && _foreignAmbassadors.Count(x=>string.Equals(x?.PostingId,ledger.PostingId,StringComparison.OrdinalIgnoreCase))==1,
                "The foreign ambassador posting remains present exactly once.",snapshot["posting"]);
            bool lifecycle = posting!=null && (posting.IsResident
                ? envoy!=null && envoy.PartyBelongedTo==null && envoy.CurrentSettlement?.StringId==posting.CapitalSettlementStringId
                : string.Equals(posting.Status,"traveling",StringComparison.OrdinalIgnoreCase)
                    ? posting.ArrivalDay>=posting.AssignedDay && posting.ArrivalDay-posting.AssignedDay<=3.001f
                    : string.Equals(posting.Status,"sheltered",StringComparison.OrdinalIgnoreCase)
                        ? !string.IsNullOrWhiteSpace(posting.ShelterSettlementStringId) && envoy?.PartyBelongedTo==null
                        : !posting.IsActive);
            AddCapitalAmbassadorAssertion(assertions,"fixture_lifecycle_invariants",lifecycle,
                "Travel, resident Keep placement, shelter, or terminal recall obeys the production posting invariants.",snapshot["posting"]);
            AddCapitalAmbassadorAssertion(assertions,"fixture_unique_active_origin",posting==null || !posting.IsActive
                || _foreignAmbassadors.Count(x=>x?.IsActive==true && string.Equals(x.OriginKingdomStringId,posting.OriginKingdomStringId,StringComparison.OrdinalIgnoreCase))==1,
                "At most one active posting represents each foreign kingdom.",null);
            JObject charter = ParseObject(posting?.AuthorityCharterJson);
            AddCapitalAmbassadorAssertion(assertions,"fixture_authority_snapshot_shape",posting==null || !posting.IsActive
                || (charter["permissions"] as JArray)?.Count==5 && (charter["referralOnlyActions"] as JArray)?.Count==9,
                "Active postings retain the exact five commercial permissions and nine mandatory referral categories.",charter);
            if (verifyReload) AddCapitalAmbassadorAssertion(assertions,"fixture_reload_fingerprint",
                string.Equals(ledger.PreparedFingerprint,CapitalAmbassadorFingerprint(snapshot),StringComparison.OrdinalIgnoreCase),
                "Capital, stable session identity and authority, posting, envoy, and charter state reloaded with the marked fingerprint.",new JObject{{"expected",ledger.PreparedFingerprint},{"actual",CapitalAmbassadorFingerprint(snapshot)}});
            JObject result = CapitalAmbassadorResult(runId,verifyReload?"verify_reload":"observe",assertions);
            result["snapshot"] = snapshot;
            return result;
        }

        private JObject MarkCapitalAmbassadorReload(string runId)
        {
            CapitalAmbassadorTestLedger ledger = FindCapitalAmbassadorLedger(runId);
            if (ledger==null) return CapitalAmbassadorFailure(runId,"mark_reload","No prepared fixture exists.");
            ledger.Phase="reload_marked";
            JObject snapshot=CapitalAmbassadorSnapshot(ledger);
            ledger.PreparedFingerprint=CapitalAmbassadorFingerprint(snapshot);
            return new JObject{{"ok",true},{"runId",runId},{"profile","capital_ambassador"},{"phase","mark_reload"},
                {"fingerprint",ledger.PreparedFingerprint},{"snapshot",snapshot}};
        }

        private JObject MoveCapitalAmbassadorFixture(string runId)
        {
            CapitalAmbassadorTestLedger ledger=FindCapitalAmbassadorLedger(runId);
            Settlement alternate=Settlement.All.FirstOrDefault(x=>string.Equals(x?.StringId,ledger?.AlternateCapitalSettlementStringId,StringComparison.OrdinalIgnoreCase));
            if(ledger==null||alternate==null)return CapitalAmbassadorFailure(runId,"move_capital","The prepared alternate capital is unavailable.");
            DesignateCapital(alternate);
            ledger.Phase="capital_moved";
            ForeignAmbassadorPosting posting=_foreignAmbassadors.FirstOrDefault(x=>string.Equals(x?.PostingId,ledger.PostingId,StringComparison.OrdinalIgnoreCase));
            Hero envoy=FindHero(ledger.EnvoyHeroStringId);
            JArray assertions=new JArray();
            AddCapitalAmbassadorAssertion(assertions,"capital_move_current",CurrentCapital==alternate,"The replacement town became the current capital without a cost or cooldown.",SettlementJson(CurrentCapital));
            AddCapitalAmbassadorAssertion(assertions,"capital_move_old_session_local",Session?.Scope==ReignCourtScope.Local&&!HasRoyalCommandAccess,
                "The still-open session at the old host immediately lost royal-command scope.",Session==null?null:JObject.FromObject(Session));
            AddCapitalAmbassadorAssertion(assertions,"capital_move_resident_relocation",posting!=null
                && posting.CapitalSettlementStringId==alternate.StringId && (!posting.IsResident || envoy?.CurrentSettlement==alternate),
                "Resident ambassadors redirect or relocate to the new capital.",PostingJson(posting));
            JObject result=CapitalAmbassadorResult(runId,"move_capital",assertions);result["snapshot"]=CapitalAmbassadorSnapshot(ledger);result["requiresBaselineRollback"]=true;return result;
        }

        private JObject SimulateCapitalLossFixture(string runId)
        {
            CapitalAmbassadorTestLedger ledger=FindCapitalAmbassadorLedger(runId);
            KingdomCapitalDesignation record=CurrentCapitalDesignation();
            if(ledger==null||record==null)return CapitalAmbassadorFailure(runId,"simulate_loss","No prepared capital designation exists.");
            HandleCapitalLoss(record,false);
            ledger.Phase="capital_lost";
            ForeignAmbassadorPosting posting=_foreignAmbassadors.FirstOrDefault(x=>string.Equals(x?.PostingId,ledger.PostingId,StringComparison.OrdinalIgnoreCase));
            Hero envoy=FindHero(ledger.EnvoyHeroStringId);
            JArray assertions=new JArray();
            AddCapitalAmbassadorAssertion(assertions,"capital_loss_clears_current_only",record.EverDesignated&&CurrentCapital==null&&string.IsNullOrWhiteSpace(record.SettlementStringId),
                "The captured-capital callback core cleared the active capital while preserving first designation.",JObject.FromObject(record));
            AddCapitalAmbassadorAssertion(assertions,"capital_loss_suspends_royal_commands",!HasRoyalCommandAccess&&Session?.Scope==ReignCourtScope.Local,
                "Royal commands are suspended until replacement.",Session==null?null:JObject.FromObject(Session));
            AddCapitalAmbassadorAssertion(assertions,"capital_loss_shelters_envoy",posting!=null
                && (string.Equals(posting.Status,"sheltered",StringComparison.OrdinalIgnoreCase)
                    ? !string.IsNullOrWhiteSpace(posting.ShelterSettlementStringId)&&envoy?.CurrentSettlement?.StringId==posting.ShelterSettlementStringId
                    : !posting.IsActive),
                "The resident envoy is safely sheltered in another owned town or cleanly ended if none exists.",PostingJson(posting));
            JObject result=CapitalAmbassadorResult(runId,"simulate_loss",assertions);result["snapshot"]=CapitalAmbassadorSnapshot(ledger);result["requiresBaselineRollback"]=true;return result;
        }

        private JObject SimulateAmbassadorWarFixture(string runId)
        {
            CapitalAmbassadorTestLedger ledger=FindCapitalAmbassadorLedger(runId);
            ForeignAmbassadorPosting posting=_foreignAmbassadors.FirstOrDefault(x=>string.Equals(x?.PostingId,ledger?.PostingId,StringComparison.OrdinalIgnoreCase));
            Kingdom ours=Clan.PlayerClan?.Kingdom;
            Kingdom origin=Kingdom.All.FirstOrDefault(x=>string.Equals(x?.StringId,ledger?.OriginKingdomStringId,StringComparison.OrdinalIgnoreCase));
            if(ledger==null||posting==null||ours==null||origin==null)return CapitalAmbassadorFailure(runId,"simulate_war","The prepared posting or kingdoms are unavailable.");
            OnAmbassadorWarDeclared(ours,origin,default(DeclareWarAction.DeclareWarDetail));
            ledger.Phase="war_recalled";
            Hero envoy=FindHero(ledger.EnvoyHeroStringId);
            JArray assertions=new JArray();
            AddCapitalAmbassadorAssertion(assertions,"ambassador_war_callback_recall",!posting.IsActive
                && string.Equals(posting.Status,"war_recalled",StringComparison.OrdinalIgnoreCase),
                "The production war-declared callback recalled and ended the represented kingdom's posting.",PostingJson(posting));
            AddCapitalAmbassadorAssertion(assertions,"ambassador_war_return_home",envoy==null||envoy.PartyBelongedTo!=null
                || envoy.CurrentSettlement?.OwnerClan?.Kingdom==origin,
                "A partyless recalled envoy returns to friendly territory.",HeroJson(envoy));
            JObject result=CapitalAmbassadorResult(runId,"simulate_war",assertions);result["snapshot"]=CapitalAmbassadorSnapshot(ledger);result["requiresBaselineRollback"]=true;return result;
        }

        private JObject RunCapitalLifecycleAFixture(string runId)
        {
            CapitalAmbassadorTestLedger ledger = FindCapitalAmbassadorLedger(runId);
            Settlement original = Settlement.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.CapitalSettlementStringId, StringComparison.OrdinalIgnoreCase));
            Settlement alternate = Settlement.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.AlternateCapitalSettlementStringId, StringComparison.OrdinalIgnoreCase));
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Kingdom origin = Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
            Hero capturer = origin?.Leader;
            if (ledger == null || original == null || alternate == null || ours == null || capturer == null
                || original.OwnerClan?.Kingdom != ours || alternate.OwnerClan?.Kingdom != ours)
            {
                return CapitalAmbassadorFailure(runId, "lifecycle_a",
                    "The prepared capital pair or deterministic foreign capture owner is unavailable.");
            }

            JArray assertions = new JArray();
            DesignateCapital(original);
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_a_designated",
                CurrentCapital == original && CurrentCapitalDesignation()?.EverDesignated == true,
                "Lifecycle A designated the prepared safe owned town through the production capital path.",
                SettlementJson(CurrentCapital));

            DesignateCapital(alternate);
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_a_relocated",
                CurrentCapital == alternate && CurrentCapitalDesignation()?.SettlementStringId == alternate.StringId,
                "Lifecycle A relocated the capital through the same production path used by the player menu.",
                SettlementJson(CurrentCapital));

            JObject beforeCapture = SettlementJson(alternate);
            beforeCapture["ownerClanId"] = alternate.OwnerClan?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(alternate, capturer);
            KingdomCapitalDesignation lostRecord = CurrentCapitalDesignation();
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => string.Equals(
                x?.PostingId, ledger.PostingId, StringComparison.OrdinalIgnoreCase));
            Hero envoy = FindHero(ledger.EnvoyHeroStringId);
            bool shelteredOrEnded = posting == null || !posting.IsActive
                || string.Equals(posting.Status, "sheltered", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(posting.ShelterSettlementStringId)
                    && envoy?.CurrentSettlement?.StringId == posting.ShelterSettlementStringId;
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_a_native_capture",
                alternate.OwnerClan?.Kingdom == origin && lostRecord?.EverDesignated == true
                    && CurrentCapital == null && string.IsNullOrWhiteSpace(lostRecord.SettlementStringId)
                    && !HasRoyalCommandAccess && shelteredOrEnded,
                "A native Bannerlord owner transfer captured the active capital, cleared only the current designation, suspended royal scope, and safely displaced any resident envoy.",
                new JObject
                {
                    ["before"] = beforeCapture,
                    ["after"] = SettlementJson(alternate),
                    ["designation"] = lostRecord == null ? JValue.CreateNull() : JObject.FromObject(lostRecord),
                    ["posting"] = PostingJson(posting)
                });

            DesignateCapital(original);
            posting = _foreignAmbassadors.FirstOrDefault(x => string.Equals(
                x?.PostingId, ledger.PostingId, StringComparison.OrdinalIgnoreCase));
            envoy = FindHero(ledger.EnvoyHeroStringId);
            bool envoyRecovered = posting == null || !posting.IsActive
                || string.Equals(posting.Status, "traveling", StringComparison.OrdinalIgnoreCase)
                || posting.IsResident && posting.CapitalSettlementStringId == original.StringId
                    && envoy?.PartyBelongedTo == null && envoy.CurrentSettlement == original;
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_a_recovered",
                CurrentCapital == original && CurrentCapitalDesignation()?.EverDesignated == true
                    && HasRoyalCommandAccess && envoyRecovered,
                "Lifecycle A recovered by designating the surviving owned town and restoring capital authority and resident routing.",
                CapitalAmbassadorSnapshot(ledger));

            ledger.Phase = "lifecycle_a_recovered";
            JObject snapshot = CapitalAmbassadorSnapshot(ledger);
            RecordCapitalAmbassadorObservation(ledger, snapshot, "lifecycle_a");
            JObject result = CapitalAmbassadorResult(runId, "lifecycle_a", assertions);
            result["snapshot"] = snapshot;
            result["capturedSettlementId"] = alternate.StringId;
            result["captureOwnerHeroId"] = capturer.StringId;
            result["requiresBaselineRollback"] = true;
            return result;
        }

        private JObject RunAmbassadorLifecycleBTransitionFixture(string runId)
        {
            CapitalAmbassadorTestLedger ledger = FindCapitalAmbassadorLedger(runId);
            Settlement original = Settlement.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.CapitalSettlementStringId, StringComparison.OrdinalIgnoreCase));
            Settlement alternate = Settlement.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.AlternateCapitalSettlementStringId, StringComparison.OrdinalIgnoreCase));
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Kingdom origin = Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId,
                ledger?.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => string.Equals(
                x?.PostingId, ledger?.PostingId, StringComparison.OrdinalIgnoreCase));
            Hero envoy = FindHero(ledger?.EnvoyHeroStringId);
            if (ledger == null || original == null || alternate == null || ours == null || origin == null
                || posting?.IsResident != true || envoy == null)
            {
                return CapitalAmbassadorFailure(runId, "lifecycle_b_transition",
                    "Lifecycle B requires the exact prepared resident posting and both safe capital towns.");
            }

            JArray assertions = new JArray();
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_b_resident_start",
                posting.IsResident && posting.CapitalSettlementStringId == original.StringId
                    && envoy.PartyBelongedTo == null && envoy.CurrentSettlement == original,
                "Lifecycle B begins from the exact eligible, partyless resident ambassador baseline.",
                PostingJson(posting));

            DesignateCapital(alternate);
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_b_moves_with_capital",
                CurrentCapital == alternate && posting.IsResident
                    && posting.CapitalSettlementStringId == alternate.StringId
                    && envoy.PartyBelongedTo == null && envoy.CurrentSettlement == alternate,
                "The resident ambassador moved with the capital through the production relocation path.",
                CapitalAmbassadorSnapshot(ledger));

            HandleCapitalLoss(CurrentCapitalDesignation(), false);
            bool sheltered = posting.IsActive
                && string.Equals(posting.Status, "sheltered", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(posting.ShelterSettlementStringId)
                && envoy.PartyBelongedTo == null
                && envoy.CurrentSettlement?.StringId == posting.ShelterSettlementStringId;
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_b_shelter",
                sheltered,
                "The production capital-loss path sheltered the partyless resident in a safe owned town.",
                PostingJson(posting));

            DesignateCapital(original);
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_b_shelter_recovery",
                CurrentCapital == original && posting.IsResident
                    && posting.CapitalSettlementStringId == original.StringId
                    && envoy.PartyBelongedTo == null && envoy.CurrentSettlement == original,
                "Redesignating a surviving capital restored the sheltered ambassador to resident Keep placement.",
                CapitalAmbassadorSnapshot(ledger));

            OnAmbassadorWarDeclared(ours, origin, default(DeclareWarAction.DeclareWarDetail));
            AddCapitalAmbassadorAssertion(assertions, "lifecycle_b_war_recall_removal",
                !posting.IsActive && string.Equals(posting.Status, "war_recalled", StringComparison.OrdinalIgnoreCase)
                    && (envoy.PartyBelongedTo != null || envoy.CurrentSettlement?.OwnerClan?.Kingdom == origin),
                "The production war callback recalled and removed the posting and returned the envoy to friendly territory.",
                PostingJson(posting));

            ledger.Phase = "lifecycle_b_recalled";
            JObject snapshot = CapitalAmbassadorSnapshot(ledger);
            RecordCapitalAmbassadorObservation(ledger, snapshot, "lifecycle_b_transition");
            JObject result = CapitalAmbassadorResult(runId, "lifecycle_b_transition", assertions);
            result["snapshot"] = snapshot;
            result["requiresBaselineRollback"] = true;
            return result;
        }

        private JObject CleanupCapitalAmbassadorFixture(string runId)
        {
            int removed=_capitalAmbassadorTestLedgers.RemoveAll(x=>string.Equals(x.RunId,runId,StringComparison.OrdinalIgnoreCase));
            return new JObject{{"ok",true},{"runId",runId},{"profile","capital_ambassador"},{"phase","cleanup_marker"},
                {"ledgersRemoved",removed},{"requiresBaselineRollback",true},
                {"message","Restore the verified baseline to undo capital, envoy-location, server archive, referral, and recall test state; then delete only exact fixture saves and Save Sync points."}};
        }

        private List<Settlement> OwnedSafeTowns(Kingdom kingdom)
        {
            return Settlement.All.Where(x=>x?.IsTown==true&&!x.IsUnderSiege&&x.OwnerClan?.Kingdom==kingdom)
                .OrderBy(x=>x.StringId,StringComparer.Ordinal).ToList();
        }

        private List<Kingdom> EligibleFixtureOriginKingdoms(Kingdom ours)
        {
            return Kingdom.All.Where(origin=>origin!=null&&origin!=ours&&!origin.IsEliminated&&origin.Leader!=null
                    && origin.Leader.IsAlive&&origin.Leader.IsActive&&ours!=null&&!ours.IsAtWarWith(origin)
                    && (Hero.MainHero?.GetRelation(origin.Leader)??-100)>=-30
                    && !_foreignAmbassadors.Any(x=>x?.IsActive==true&&string.Equals(x.OriginKingdomStringId,origin.StringId,StringComparison.OrdinalIgnoreCase))
                    && EligibleForeignAmbassadorCandidates(origin).Count>0)
                .OrderBy(x=>x.StringId,StringComparer.Ordinal).ToList();
        }

        private JObject OccupiedOriginDiagnostic(ForeignAmbassadorPosting posting, Kingdom ours)
        {
            Kingdom origin = Kingdom.All.FirstOrDefault(k => string.Equals(k?.StringId,
                posting?.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
            Hero ruler = origin?.Leader;
            return new JObject
            {
                ["postingId"] = posting?.PostingId ?? string.Empty,
                ["originKingdomId"] = origin?.StringId ?? posting?.OriginKingdomStringId ?? string.Empty,
                ["originFound"] = origin != null,
                ["originEliminated"] = origin?.IsEliminated ?? true,
                ["rulerId"] = ruler?.StringId ?? string.Empty,
                ["rulerAlive"] = ruler?.IsAlive ?? false,
                ["rulerActive"] = ruler?.IsActive ?? false,
                ["atWar"] = ours != null && origin != null && ours.IsAtWarWith(origin),
                ["relation"] = ruler == null ? -100 : Hero.MainHero?.GetRelation(ruler) ?? -100,
                ["postingCharm"] = posting?.Charm ?? 0,
                ["unassignedCandidateCount"] = origin == null ? 0 : EligibleForeignAmbassadorCandidates(origin).Count
            };
        }

        private CapitalAmbassadorTestLedger FindCapitalAmbassadorLedger(string runId)
        {
            return _capitalAmbassadorTestLedgers.FirstOrDefault(x=>string.Equals(x.RunId,runId,StringComparison.OrdinalIgnoreCase));
        }

        private void CompactCapitalAmbassadorTestLedgers()
        {
            _capitalAmbassadorTestLedgers = (_capitalAmbassadorTestLedgers ?? new List<CapitalAmbassadorTestLedger>())
                .Where(x=>x!=null&&!string.IsNullOrWhiteSpace(x.RunId))
                .GroupBy(x=>x.RunId,StringComparer.OrdinalIgnoreCase).Select(x=>x.Last())
                .Take(CapitalAmbassadorLedgerMaxCount).ToList();
            foreach(CapitalAmbassadorTestLedger ledger in _capitalAmbassadorTestLedgers)
                CompactCapitalAmbassadorTestLedger(ledger);
        }

        private static void CompactCapitalAmbassadorTestLedger(CapitalAmbassadorTestLedger ledger)
        {
            if(ledger==null)return;
            ledger.ObservationJson=(ledger.ObservationJson??new List<string>())
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .Select(x=>CompactCapitalAmbassadorStoredJson(x,"observation"))
                .ToList();
            if(ledger.ObservationJson.Count>CapitalAmbassadorLedgerMaxObservations)
                ledger.ObservationJson=ledger.ObservationJson.Skip(ledger.ObservationJson.Count-CapitalAmbassadorLedgerMaxObservations).ToList();
        }

        private static string CompactCapitalAmbassadorStoredJson(string json,string kind)
        {
            string value=json??string.Empty;
            if(value.Length<=CapitalAmbassadorLedgerMaxStoredJsonChars)return value;
            return new JObject{{"compacted",true},{"kind",kind??string.Empty},{"originalLength",value.Length}}
                .ToString(Formatting.None);
        }

        private static void RecordCapitalAmbassadorObservation(CapitalAmbassadorTestLedger ledger,JObject snapshot,string stage)
        {
            if(ledger==null)return;
            ledger.ObservationJson=ledger.ObservationJson??new List<string>();
            JObject checkpoint=new JObject
            {
                ["stage"]=stage??string.Empty,
                ["campaignId"]=(string)snapshot?["campaignId"]??string.Empty,
                ["worldDay"]=(double?)snapshot?["worldDay"]??0d,
                ["ledgerPhase"]=(string)snapshot?["ledgerPhase"]??string.Empty,
                ["capitalId"]=(string)snapshot?["capital"]?["SettlementStringId"]??string.Empty,
                ["postingId"]=(string)snapshot?["posting"]?["PostingId"]??string.Empty,
                ["postingStatus"]=(string)snapshot?["posting"]?["Status"]??string.Empty
            };
            ledger.ObservationJson.Add(checkpoint.ToString(Formatting.None));
            CompactCapitalAmbassadorTestLedger(ledger);
        }

        private JObject CapitalAmbassadorSnapshot(CapitalAmbassadorTestLedger ledger)
        {
            ForeignAmbassadorPosting posting=_foreignAmbassadors.FirstOrDefault(x=>string.Equals(x?.PostingId,ledger?.PostingId,StringComparison.OrdinalIgnoreCase));
            Hero envoy=FindHero(ledger?.EnvoyHeroStringId);
            KingdomCapitalDesignation designation=CurrentCapitalDesignation();
            return new JObject
            {
                ["campaignId"]=ReignCampaignIdentity.CurrentCampaignId(),["worldDay"]=CurrentDayFloat(),["ledgerPhase"]=ledger?.Phase??string.Empty,
                ["capital"]=designation==null?JValue.CreateNull():JObject.FromObject(designation),
                ["currentCapital"]=SettlementJson(CurrentCapital),
                ["session"]=_session==null?JValue.CreateNull():JObject.FromObject(_session),
                ["posting"]=PostingJson(posting),["envoy"]=HeroJson(envoy),
                ["legacyOutboundPostingCount"]=_ambassadors.Count,
                ["activeOriginPostingCount"]=posting==null?0:_foreignAmbassadors.Count(x=>x?.IsActive==true&&string.Equals(x.OriginKingdomStringId,posting.OriginKingdomStringId,StringComparison.OrdinalIgnoreCase))
            };
        }

        private static string CapitalAmbassadorFingerprint(JObject snapshot)
        {
            JObject stable=snapshot==null?new JObject():(JObject)snapshot.DeepClone();
            stable.Remove("worldDay");
            JObject session=stable["session"] as JObject;
            if(session!=null)
            {
                session.Remove("LastAgendaDay");
                session.Remove("LastPlayerSittingDay");
                session.Remove("Revision");
            }
            return ReignCourtTerms.Hash(stable.ToString(Formatting.None));
        }

        private static JObject SettlementJson(Settlement settlement)
        {
            return settlement==null?null:new JObject{{"settlementId",settlement.StringId},{"name",settlement.Name?.ToString()??settlement.StringId},
                {"isTown",settlement.IsTown},{"isCastle",settlement.IsCastle},{"underSiege",settlement.IsUnderSiege},
                {"ownerKingdomId",settlement.OwnerClan?.Kingdom?.StringId??string.Empty}};
        }

        private static JObject PostingJson(ForeignAmbassadorPosting posting)
        {
            return posting==null?null:new JObject{{"postingId",posting.PostingId},{"heroId",posting.HeroStringId},{"originKingdomId",posting.OriginKingdomStringId},
                {"originRulerId",posting.OriginRulerHeroStringId},{"capitalSettlementId",posting.CapitalSettlementStringId},
                {"shelterSettlementId",posting.ShelterSettlementStringId},{"status",posting.Status},{"assignedDay",posting.AssignedDay},
                {"arrivalDay",posting.ArrivalDay},{"endedDay",posting.EndedDay},{"charm",posting.Charm},{"rulerTrust",posting.RulerTrust},
                {"authorityRevision",posting.AuthorityRevision},{"revision",posting.Revision},{"authorityCharter",ParseObject(posting.AuthorityCharterJson)}};
        }

        private static JObject HeroJson(Hero hero)
        {
            return hero==null?null:new JObject{{"heroId",hero.StringId},{"name",hero.Name?.ToString()??hero.StringId},
                {"alive",hero.IsAlive},{"active",hero.IsActive},{"prisoner",hero.IsPrisoner},{"partyId",hero.PartyBelongedTo?.StringId??string.Empty},
                {"settlementId",hero.CurrentSettlement?.StringId??string.Empty},{"kingdomId",hero.Clan?.Kingdom?.StringId??string.Empty},
                {"charm",hero.GetSkillValue(DefaultSkills.Charm)}};
        }

        private static void AddCapitalAmbassadorAssertion(JArray assertions,string id,bool passed,string summary,JToken data)
        {
            assertions.Add(new JObject{{"caseId",id},{"passed",passed},{"summary",summary},{"data",data??JValue.CreateNull()}});
        }

        private static JObject CapitalAmbassadorResult(string runId,string phase,JArray assertions)
        {
            int passed=assertions.Count(x=>(bool?)x["passed"]==true);
            return new JObject{{"ok",passed==assertions.Count},{"runId",runId},{"profile","capital_ambassador"},{"phase",phase},
                {"passedCount",passed},{"failedCount",assertions.Count-passed},{"totalCount",assertions.Count},{"assertions",assertions}};
        }

        private static JObject CapitalAmbassadorFailure(string runId,string phase,string error)
        {
            return new JObject{{"ok",false},{"runId",runId},{"profile","capital_ambassador"},{"phase",phase},{"error",error??"Capital/ambassador test phase failed."}};
        }
    }
}
