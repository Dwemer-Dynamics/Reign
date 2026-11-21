using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Platform;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignActionGauntlet
    {
        private const string Mode = "action_gauntlet";
        private const float AllSuiteDelayDays = 0.0417f;
        private const int LiveDialogueBatchSize = 20;
        private const float OvernightCommandPollSeconds = 5f;
        private const string VerificationGameCommandFileName = "game-command.json";
        private const string VerificationGameStatusFileName = "game-status.json";
        private static string _lastSummary = "No action gauntlet has run yet.";
        private static Queue<QueuedSuite> _queuedAllSuites;
        private static List<SuiteSummary> _queuedAllSummaries = new List<SuiteSummary>();
        private static string _queuedAllRunId = string.Empty;
        private static float _queuedAllNextDay;
        private static bool _queuedAllActive;
        private static bool _liveDialogueActive;
        private static string _lastLiveDialogueSummary = "No live dialogue beta suite has run yet.";
        private static string _lastLiveDialogueRunId = string.Empty;
        private static int _lastLiveDialoguePassed;
        private static int _lastLiveDialogueSkipped;
        private static int _lastLiveDialogueFailed;
        private static int _lastLiveDialogueTotal;
        private static JArray _lastLiveDialogueResults = new JArray();
        private static float _overnightCommandPollAccumulator;
        private static float _verificationCommandPollAccumulator;
        private static string _verificationCommandId = string.Empty;
        private static bool _verificationGameRunActive;

        private delegate string Assertion(GauntletContext context, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after);

        public static void PrepareArena()
        {
            if (!RequireTestMode() || !RequireCampaign())
            {
                return;
            }

            string correlationId = NewCorrelationId("prepare");
            Stopwatch timer = Stopwatch.StartNew();
            GauntletContext context = BuildContext(correlationId);
            List<string> notes = new List<string>();

            EnsurePlayerResources(notes);
            EnsurePlayerKingdomState(notes);
            if (context.ArenaSettlement != null)
            {
                TeleportParty(MobileParty.MainParty, context.ArenaSettlement);
                notes.Add("player party moved to " + context.ArenaSettlement.Name);
            }

            foreach (Hero hero in new[] { context.ActorHero, context.TargetHero }.Where(x => x?.PartyBelongedTo != null))
            {
                EnsureActorResources(hero, notes);
                if (context.ArenaSettlement != null)
                {
                    TeleportParty(hero.PartyBelongedTo, context.ArenaSettlement);
                    notes.Add(hero.Name + " moved to arena");
                }
            }

            if (context.PrisonerHero != null && !context.PrisonerHero.IsPrisoner)
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, context.PrisonerHero);
                notes.Add(context.PrisonerHero.Name + " taken as gauntlet prisoner");
            }

            if (context.HostileVillage != null)
            {
                EnsureWar(context.ActorHero?.PartyBelongedTo?.MapFaction, context.HostileVillage.MapFaction, notes);
            }

            if (context.HostileFortification != null)
            {
                EnsureWar(context.ActorHero?.PartyBelongedTo?.MapFaction, context.HostileFortification.MapFaction, notes);
            }

            timer.Stop();
            JObject data = ContextJson(context);
            data["notes"] = new JArray(notes);
            data["durationMs"] = timer.ElapsedMilliseconds;
            WriteGauntletLog(correlationId, "prepare", "completed", "Prepared action gauntlet arena.", data);
            _ = ReignServerClient.IngestAuditAsync(correlationId, Mode, "gauntlet.prepare", "completed", "Prepared action gauntlet arena.", data, durationMs: timer.ElapsedMilliseconds);
            Show("Action gauntlet arena prepared. " + string.Join("; ", notes.Take(8)) + (notes.Count > 8 ? "; ..." : "."));
        }

        public static void RunRegularSuite()
        {
            RunSuiteCore("Regular", "regular", RunRegularTests, true);
        }

        public static void RunDiplomacySuite()
        {
            RunSuiteCore("Diplomacy", "diplomacy", RunDiplomacyTests, true);
        }

        public static void RunStrategySuite()
        {
            RunSuiteCore("Strategy", "strategy", RunStrategyTests, true);
        }

        public static void RunPoliticsSuite()
        {
            RunSuiteCore("Politics", "politics", RunPoliticsTests, true);
        }

        public static void RunFailureSuite()
        {
            RunSuiteCore("Failure/Validation", "failure", RunFailureTests, true);
        }

        public static void RunAllSuites()
        {
            if (!RequireTestMode() || !RequireCampaign())
            {
                return;
            }

            _queuedAllRunId = NewCorrelationId("all");
            _queuedAllSummaries = new List<SuiteSummary>();
            _queuedAllSuites = new Queue<QueuedSuite>(new[]
            {
                new QueuedSuite("Regular", "regular", RunRegularTests),
                new QueuedSuite("Diplomacy", "diplomacy", RunDiplomacyTests),
                new QueuedSuite("Strategy", "strategy", RunStrategyTests),
                new QueuedSuite("Politics", "politics", RunPoliticsTests),
                new QueuedSuite("Failure/Validation", "failure", RunFailureTests)
            });
            _queuedAllNextDay = CurrentDay() + 0.01f;
            _queuedAllActive = true;
            _lastSummary = "All action gauntlets queued safely. They will run one suite per in-game hour, starting when campaign time advances.";

            JObject data = new JObject
            {
                ["runId"] = _queuedAllRunId,
                ["suite"] = "All",
                ["queuedSuites"] = new JArray(_queuedAllSuites.Select(x => x.Name)),
                ["nextDay"] = _queuedAllNextDay,
                ["delayDays"] = AllSuiteDelayDays,
                ["reason"] = "Native Bannerlord scene notifications can crash if many kingdom/clan-changing tests run in the same UI tick."
            };

            WriteGauntletLog(_queuedAllRunId, "suite.All.queue", "queued", _lastSummary, data);
            _ = ReignServerClient.IngestAuditAsync(_queuedAllRunId, Mode, "gauntlet.suite.All.queue", "queued", _lastSummary, data);
            Show(_lastSummary);
        }

        public static void TickAllSuites()
        {
            if (!_queuedAllActive || _queuedAllSuites == null || _queuedAllSuites.Count == 0)
            {
                return;
            }

            if (!RequireCampaign())
            {
                return;
            }

            float now = CurrentDay();
            if (now < _queuedAllNextDay)
            {
                return;
            }

            QueuedSuite suite = _queuedAllSuites.Dequeue();
            SuiteSummary summary = RunSuiteCore(suite.Name, suite.Id, suite.Body, false);
            _queuedAllSummaries.Add(summary);

            JObject data = new JObject
            {
                ["runId"] = _queuedAllRunId,
                ["completedSuite"] = summary.ToJson(),
                ["remainingSuites"] = _queuedAllSuites.Count,
                ["currentDay"] = now
            };

            WriteGauntletLog(_queuedAllRunId, "suite.All.step", summary.Failed == 0 ? "completed" : "failed", summary.Message, data);
            _ = ReignServerClient.IngestAuditAsync(_queuedAllRunId, Mode, "gauntlet.suite.All.step", summary.Failed == 0 ? "completed" : "failed", summary.Message, data, durationMs: summary.DurationMs);

            if (_queuedAllSuites.Count == 0)
            {
                FinishQueuedAllSuites();
                return;
            }

            _queuedAllNextDay = now + AllSuiteDelayDays;
            Show("Run All progress: " + suite.Name + " finished. Next suite starts after about one in-game hour.");
        }

        private static void FinishQueuedAllSuites()
        {
            List<SuiteSummary> summaries = _queuedAllSummaries ?? new List<SuiteSummary>();
            int passed = summaries.Sum(x => x.Passed);
            int skipped = summaries.Sum(x => x.Skipped);
            int failed = summaries.Sum(x => x.Failed);
            int total = summaries.Sum(x => x.Total);
            _lastSummary = "All action gauntlets: passed=" + passed + ", skipped=" + skipped + ", failed=" + failed + ", total=" + total + ", run=" + _queuedAllRunId + ".";

            JObject data = new JObject
            {
                ["runId"] = _queuedAllRunId,
                ["suite"] = "All",
                ["passed"] = passed,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["total"] = total,
                ["suites"] = new JArray(summaries.Select(x => x.ToJson()))
            };

            WriteGauntletLog(_queuedAllRunId, "suite.All", failed == 0 ? "completed" : "failed", _lastSummary, data);
            _ = ReignServerClient.IngestAuditAsync(_queuedAllRunId, Mode, "gauntlet.suite.All", failed == 0 ? "completed" : "failed", _lastSummary, data);
            _queuedAllActive = false;
            _queuedAllSuites = null;
            if (_verificationGameRunActive)
            {
                WriteVerificationGameStatus(failed == 0 ? "completed" : "failed", _lastSummary, passed, skipped, failed, total);
                _verificationGameRunActive = false;
            }
            Show(_lastSummary);
        }

        public static void ShowLastSummary()
        {
            Show(_lastSummary);
        }

        public static void PrepareLiveDialogueBetaArena()
        {
            if (!RequireTestMode() || !RequireCampaign())
            {
                return;
            }

            PrepareArena();
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null)
            {
                settings.DialogueComplianceTestModeEnabled = true;
            }

            Show("Live dialogue beta arena prepared. Dialogue Compliance Test Mode is enabled; start the server, then run the live suite.");
        }

        public static void RunLiveDialogueBetaSuite()
        {
            if (!RequireTestMode() || !RequireCampaign())
            {
                return;
            }

            if (_liveDialogueActive)
            {
                Show("Live dialogue beta suite is already running.");
                return;
            }

            _ = RunLiveDialogueBetaSuiteAsync(true);
        }

        public static void ShowLiveDialogueBetaSummary()
        {
            Show(_lastLiveDialogueSummary);
        }

        public static void StartOvernightLiveDialogueGauntlet()
        {
            // Retained as an inert compatibility surface so older saves keep the
            // same generated client type inventory. The overnight harness is retired.
        }

        public static void StopOvernightLiveDialogueGauntlet()
        {
            // Retained as an inert compatibility surface; see Start above.
        }

        public static void ShowOvernightLiveDialogueSummary()
        {
            // Retained as an inert compatibility surface; see Start above.
        }

        public static bool IsLiveDialogueGauntletActive
        {
            get { return _liveDialogueActive; }
        }

        public static void ApplicationTick(float dt)
        {
            // Keep the retired field in the compiled layout without ever
            // accumulating time or polling the obsolete command file.
            if (_overnightCommandPollAccumulator != 0f)
            {
                _overnightCommandPollAccumulator = 0f;
            }
            _verificationCommandPollAccumulator += dt;
            if (_verificationCommandPollAccumulator >= OvernightCommandPollSeconds)
            {
                _verificationCommandPollAccumulator = 0f;
                PollVerificationGameCommand();
            }
        }

        private static float CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
        }

        private static SuiteSummary RunSuiteCore(string suiteName, string suiteId, Action<List<GauntletResult>, GauntletContext> body, bool showSummary)
        {
            if (!RequireTestMode() || !RequireCampaign())
            {
                return new SuiteSummary { Suite = suiteName ?? string.Empty };
            }

            string correlationId = NewCorrelationId(suiteId);
            Stopwatch timer = Stopwatch.StartNew();
            GauntletContext context = BuildContext(correlationId);
            List<GauntletResult> results = new List<GauntletResult>();

            try
            {
                body?.Invoke(results, context);
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Action gauntlet suite " + suiteName, ex);
                results.Add(new GauntletResult
                {
                    Suite = suiteName ?? string.Empty,
                    Name = "Suite",
                    Status = "failed",
                    Message = ex.Message,
                    ResultCode = "suite_exception",
                    Outcome = "failed",
                    DurationMs = timer.ElapsedMilliseconds,
                    Before = new JObject(),
                    After = new JObject()
                });
            }

            timer.Stop();
            SuiteSummary summary = BuildSuiteSummary(suiteName, correlationId, timer.ElapsedMilliseconds, context, results);
            _lastSummary = summary.Message;

            JObject summaryData = summary.ToJson();
            summaryData["context"] = ContextJson(context);
            summaryData["results"] = new JArray(results.Select(x => x.ToJson()));

            WriteGauntletLog(correlationId, "suite." + suiteName, summary.Failed == 0 ? "completed" : "failed", summary.Message, summaryData);
            _ = ReignServerClient.IngestAuditAsync(correlationId, Mode, "gauntlet.suite." + suiteName, summary.Failed == 0 ? "completed" : "failed", summary.Message, summaryData, durationMs: timer.ElapsedMilliseconds);
            if (showSummary)
            {
                Show(summary.Message);
            }

            return summary;
        }

        private static SuiteSummary BuildSuiteSummary(string suiteName, string correlationId, long durationMs, GauntletContext context, List<GauntletResult> results)
        {
            int passed = results.Count(x => x.Status == "passed");
            int skipped = results.Count(x => x.Status == "skipped");
            int failed = results.Count(x => x.Status == "failed");
            return new SuiteSummary
            {
                Suite = suiteName ?? string.Empty,
                RunId = correlationId ?? string.Empty,
                DurationMs = durationMs,
                Passed = passed,
                Skipped = skipped,
                Failed = failed,
                Total = results.Count,
                Message = (suiteName ?? "Action") + " action gauntlet: passed=" + passed + ", skipped=" + skipped + ", failed=" + failed + ", total=" + results.Count + ", run=" + correlationId + "."
            };
        }

        private static void RunRegularTests(List<GauntletResult> results, GauntletContext context)
        {
            Run(results, context, "Regular", "FollowOnMap", FollowOnMap, MovementOrder("order=follow_on_map"));
            Run(results, context, "Regular", "FollowInScene", FollowInScene, NativeChange("mission_follow_started"));
            Run(results, context, "Regular", "StopFollowing", StopFollowing, MovementOrder("order=hold"));
            Run(results, context, "Regular", "GoToSettlement", GoToSettlement, MovementOrder("order=go_to_settlement"));
            Run(results, context, "Regular", "PatrolAroundSettlement", PatrolAroundSettlement, MovementOrder("order=patrol"));
            Run(results, context, "Regular", "WaitNearSettlement", WaitNearSettlement, MovementOrder("order=wait_near"));
            Run(results, context, "Regular", "RaidVillage", RaidVillage, MovementOrder("order=raid_village"));
            Run(results, context, "Regular", "BesiegeSettlement", BesiegeSettlement, MovementOrder("order=besiege"));
            Run(results, context, "Regular", "CreateParty", CreateParty, CreatePartyAssert);
            Run(results, context, "Regular", "ShowTheWay", ShowTheWay, NativeChange("mission_guide_started"));
            Run(results, context, "Regular", "AttackParty", AttackParty, MovementOrder("order=attack_party"));
            Run(results, context, "Regular", "AttackPlayerParty", AttackPlayerParty, MovementOrder("order=attack_player_party"));
            Run(results, context, "Regular", "SurrenderToPlayer", SurrenderToPlayer, NativeChange("party_surrendered"));
            Run(results, context, "Regular", "LeavePlayerAlone", LeavePlayerAlone, MovementOrder("order=leave_player_alone"));
            Run(results, context, "Regular", "GiveGoldToPlayer", GiveGoldToPlayer, GoldChanged);
            Run(results, context, "Regular", "TransferGold", TransferGold, GoldChanged);
            Run(results, context, "Regular", "TransferItem", TransferItem, ItemChanged);
            Run(results, context, "Regular", "TransferWorkshop", TransferWorkshop, WorkshopChanged);
            Run(results, context, "Regular", "TransferPrisoner", TransferPrisoner, PrisonerReleased);
            Run(results, context, "Regular", "HirePlayerAsMercenary", HirePlayerAsMercenary, PlayerMercenaryStarted);
            Run(results, context, "Regular", "DismissPlayerMercenary", DismissPlayerMercenary, PlayerMercenaryEnded);
            Run(results, context, "Regular", "OfferPlayerVassalage", OfferPlayerVassalage, PlayerVassalStarted);
            Run(results, context, "Regular", "DismissPlayerVassal", DismissPlayerVassal, PlayerVassalEnded);
            Run(results, context, "Regular", "JoinClan", JoinClan, NativeChange("hero_clan_changed"));
            Run(results, context, "Regular", "LeaveClan", LeaveClan, NativeChange("hero_clan_changed"));
            Run(results, context, "Regular", "JoinKingdom", JoinKingdom, ClanJoinedKingdom);
            Run(results, context, "Regular", "LeaveKingdom", LeaveKingdom, ClanLeftKingdom);
            Run(results, context, "Regular", "HireMercenaryClan", HireMercenaryClan, ClanMercenaryStarted);
            Run(results, context, "Regular", "DuelPlayer", DuelPlayer, NativeChange("duel_mission_started"));
            Run(results, context, "Regular", "KillCharacter", KillCharacter, HeroKilled);
        }

        private static void RunDiplomacyTests(List<GauntletResult> results, GauntletContext context)
        {
            EnsurePlayerKingdomState(null);
            Run(results, context, "Diplomacy", "DeclareWar", DiplomacyDeclareWar, WarStarted);
            Run(results, context, "Diplomacy", "MakePeace", DiplomacyMakePeace, PeaceMade);
            Run(results, context, "Diplomacy", "OfferTributePeace", DiplomacyOfferTributePeace, PeaceMade);
            Run(results, context, "Diplomacy", "RecordPromise", DiplomacyRecordPromise, TreatyRecorded);
            Run(results, context, "Diplomacy", "DemandReparationsPeace", DiplomacyDemandReparationsPeace, PeaceMade);
            Run(results, context, "Diplomacy", "DemandSettlementPeace", DiplomacyDemandSettlementPeace, SettlementTransferredToActor);
            Run(results, context, "Diplomacy", "SignTradeAgreement", DiplomacySignTradeAgreement, TreatyRecorded);
            Run(results, context, "Diplomacy", "SignNonAggressionPact", DiplomacySignNonAggressionPact, TreatyRecorded);
            Run(results, context, "Diplomacy", "SignAlliance", DiplomacySignAlliance, TreatyRecorded);
            Run(results, context, "Diplomacy", "SignDefensivePact", DiplomacySignDefensivePact, TreatyRecorded);
            Run(results, context, "Diplomacy", "BreakTreaty", DiplomacyBreakTreaty, TreatyBroken);
            Run(results, context, "Diplomacy", "ExchangePrisoners", DiplomacyExchangePrisoners, PrisonerOrTreatyEffect);
            Run(results, context, "Diplomacy", "RansomPackage", DiplomacyRansomPackage, PrisonerOrGoldEffect);
            Run(results, context, "Diplomacy", "HostageGuarantee", DiplomacyHostageGuarantee, TreatyRecorded);
            Run(results, context, "Diplomacy", "WarIndemnity", DiplomacyWarIndemnity, GoldOrTreatyEffect);
            Run(results, context, "Diplomacy", "RecognizeConquest", DiplomacyRecognizeConquest, TreatyRecorded);
            Run(results, context, "Diplomacy", "ReturnOccupiedSettlement", DiplomacyReturnOccupiedSettlement, SettlementTransferredToTarget);
            Run(results, context, "Diplomacy", "DemilitarizedBorder", DiplomacyDemilitarizedBorder, TreatyRecorded);
            Run(results, context, "Diplomacy", "CaravanProtection", DiplomacyCaravanProtection, TreatyRecorded);
            Run(results, context, "Diplomacy", "SupplyAgreement", DiplomacySupplyAgreement, GoldOrTreatyEffect);
            Run(results, context, "Diplomacy", "LoanOrSubsidy", DiplomacyLoanOrSubsidy, GoldOrTreatyEffect);
            Run(results, context, "Diplomacy", "PayToStayNeutral", DiplomacyPayToStayNeutral, GoldOrTreatyEffect);
            Run(results, context, "Diplomacy", "PayToJoinWar", DiplomacyPayToJoinWar, WarStartedAgainstThird);
            Run(results, context, "Diplomacy", "GuaranteeIndependence", DiplomacyGuaranteeIndependence, TreatyRecorded);
            Run(results, context, "Diplomacy", "ProtectorateOrVassalage", DiplomacyProtectorateOrVassalage, TreatyRecorded);
            Run(results, context, "Failure/Validation", "DiplomaticPackage", DiplomacyPackage, ExpectedValidationFailure);
            Run(results, context, "Diplomacy", "DemandSurrenderPeace", DiplomacyDemandSurrenderPeace, FullSurrenderTransferredLand);
        }

        private static void RunStrategyTests(List<GauntletResult> results, GauntletContext context)
        {
            Run(results, context, "Strategy", "RecruitAndRecover", StrategyRecruitAndRecover, MovementOrder("order=recruit_and_recover"));
            Run(results, context, "Strategy", "FormArmy", StrategyFormArmy, NativeChangeOrEffect("army_created"));
            Run(results, context, "Strategy", "AttackSettlement", StrategyAttackSettlement, MovementOrder("order=raid"));
            Run(results, context, "Strategy", "CaptureSettlement", StrategyCaptureSettlement, CapturePlanProgressed);
        }

        private static void RunPoliticsTests(List<GauntletResult> results, GauntletContext context)
        {
            Run(results, context, "Politics", "MarriageAlliance", PoliticsMarriageAlliance, VerifiedTransactionCompleted);
            Run(results, context, "Politics", "SupportClaimant", PoliticsSupportClaimant, TreatyRecorded);
            Run(results, context, "Politics", "MediateClanDispute", PoliticsMediateClanDispute, TreatyRecorded);
            Run(results, context, "Politics", "EncourageClanDefection", PoliticsEncourageClanDefection, ClanKingdomChanged);
            Run(results, context, "Politics", "ExileClan", PoliticsExileClan, ClanExiled);
            Run(results, context, "Politics", "RestoreExiledClan", PoliticsRestoreExiledClan, ClanRestored);
            Run(results, context, "Politics", "InstallRulingClan", PoliticsInstallRulingClan, RulingClanChanged);
            Run(results, context, "Politics", "StartRulingClanRebellion", PoliticsStartRulingClanRebellion, RebellionStarted);
        }

        private static void RunFailureTests(List<GauntletResult> results, GauntletContext context)
        {
            Run(results, context, "Failure/Validation", "RetiredTemporaryTruce", FailureRetiredTemporaryTruce, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "RetiredTradeEmbargo", FailureRetiredTradeEmbargo, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "IllegalVillageSurrender", FailureIllegalVillageSurrender, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "MissingPrisonerPackage", FailureMissingPrisonerPackage, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "UnaffordableGoldTransfer", FailureUnaffordableGoldTransfer, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "UnauthorizedSettlementTrade", FailureUnauthorizedSettlementTrade, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "TamperedSettlementConsent", FailureTamperedSettlementConsent, ExpectedValidationFailure);
            Run(results, context, "Failure/Validation", "UnknownAction", FailureUnknownAction, ExpectedValidationFailure);
        }

        private static void Run(List<GauntletResult> results, GauntletContext context, string name, Func<GauntletContext, ActionSpec> build, Assertion assertion)
        {
            Run(results, context, "Regular", name, build, assertion);
        }

        private static void Run(List<GauntletResult> results, GauntletContext context, string suite, string name, Func<GauntletContext, ActionSpec> build, Assertion assertion)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string status = "failed";
            string message = string.Empty;
            ReignWorldActionRecord action = null;
            ReignActionResult result = null;
            JObject before = new JObject();
            JObject after = new JObject();
            Action cleanup = null;

            try
            {
                ActionSpec spec = build(context);
                cleanup = spec?.Cleanup;
                if (spec == null || spec.Action == null)
                {
                    status = "skipped";
                    message = spec?.SkipReason ?? "No valid gauntlet target was available.";
                    return;
                }

                action = spec.Action;
                before = Snapshot(context, action);
                if (!ReignActionValidator.Validate(action, out string validationFailure))
                {
                    if (spec.ExpectValidationFailure)
                    {
                        result = ReignActionResult.ValidationFailed(validationFailure);
                        status = "passed";
                        message = "Expected validation failure: " + validationFailure;
                    }
                    else
                    {
                        status = "failed";
                        message = "Validation failed before execution: " + validationFailure;
                    }

                    return;
                }

                if (spec.ExpectValidationFailure)
                {
                    status = "failed";
                    message = "Expected validation failure, but validation passed.";
                    return;
                }

                ReignAICampaignBehavior behavior = ReignServices.AI;
                if (behavior == null)
                {
                    status = "failed";
                    message = "Bannerlord Reign campaign behavior is not active.";
                    return;
                }

                result = behavior.ExecuteActionForTest(action);
                after = Snapshot(context, action);
                string assertionFailure = assertion == null ? SuccessAny(context, action, result, before, after) : assertion(context, action, result, before, after);
                status = string.IsNullOrWhiteSpace(assertionFailure) ? "passed" : "failed";
                message = status == "passed" ? result.Message : assertionFailure;

                if (action.Status == ReignWorldActionStatus.Executing && action.Source != null && action.Source.StartsWith("gauntlet", StringComparison.OrdinalIgnoreCase))
                {
                    action.Status = ReignWorldActionStatus.Completed;
                    action.FailureReason = string.Empty;
                }
            }
            catch (Exception ex)
            {
                status = "failed";
                message = ex.Message;
                ReignLog.Exception("Action gauntlet test " + name, ex);
            }
            finally
            {
                timer.Stop();
                GauntletResult row = new GauntletResult
                {
                    Suite = suite ?? string.Empty,
                    Name = name,
                    Status = status,
                    Message = message,
                    ActionId = action?.ActionId ?? string.Empty,
                    Type = action?.Type.ToString() ?? string.Empty,
                    ResultCode = result?.ResultCode ?? string.Empty,
                    Outcome = result?.Outcome ?? string.Empty,
                    DurationMs = timer.ElapsedMilliseconds,
                    Before = before,
                    After = after
                };
                results.Add(row);

                JObject data = row.ToJson();
                data["resultSuccess"] = result != null && result.Success;
                data["resultCompleted"] = result != null && result.Completed;
                data["resultMessage"] = result?.Message ?? string.Empty;
                data["effects"] = ToJson(result?.Effects);
                data["changedEntities"] = ToJson(result?.ChangedEntities);
                WriteGauntletLog(context.CorrelationId, "action." + suite + "." + name, status, message, data);
                _ = ReignServerClient.IngestAuditAsync(context.CorrelationId, Mode, "gauntlet.action." + suite + "." + name, status, message, data, actionId: action?.ActionId ?? string.Empty, durationMs: timer.ElapsedMilliseconds);
                if (cleanup != null)
                {
                    try
                    {
                        cleanup();
                    }
                    catch (Exception cleanupError)
                    {
                        ReignLog.Exception("Action gauntlet cleanup " + name, cleanupError);
                    }
                }
            }
        }

        private static ActionSpec Spec(ReignWorldActionRecord action, Action cleanup = null)
        {
            return new ActionSpec { Action = action, Cleanup = cleanup };
        }

        private static ActionSpec Skip(string reason)
        {
            return new ActionSpec { SkipReason = reason ?? "Skipped." };
        }

        private static JObject ContextJson(GauntletContext c)
        {
            return new JObject
            {
                ["runId"] = c.CorrelationId ?? string.Empty,
                ["arenaSettlement"] = c.ArenaSettlement?.StringId ?? string.Empty,
                ["actorHero"] = c.ActorHero?.StringId ?? string.Empty,
                ["targetHero"] = c.TargetHero?.StringId ?? string.Empty,
                ["partylessHero"] = c.PartylessHero?.StringId ?? string.Empty,
                ["prisonerHero"] = c.PrisonerHero?.StringId ?? string.Empty,
                ["killTarget"] = c.KillTarget?.StringId ?? string.Empty,
                ["hostileFortification"] = c.HostileFortification?.StringId ?? string.Empty,
                ["hostileVillage"] = c.HostileVillage?.StringId ?? string.Empty,
                ["workshop"] = ReignObjectResolver.WorkshopId(c.Workshop),
                ["targetKingdom"] = c.TargetKingdom?.StringId ?? string.Empty,
                ["targetClan"] = c.TargetClan?.StringId ?? string.Empty,
                ["joinClan"] = c.JoinClan?.StringId ?? string.Empty,
                ["mercenaryClan"] = c.MercenaryClan?.StringId ?? string.Empty
            };
        }

        private static JArray ToJson(List<Dictionary<string, string>> rows)
        {
            JArray array = new JArray();
            if (rows == null)
            {
                return array;
            }

            foreach (Dictionary<string, string> row in rows)
            {
                JObject obj = new JObject();
                if (row != null)
                {
                    foreach (KeyValuePair<string, string> pair in row)
                    {
                        obj[pair.Key] = pair.Value;
                    }
                }

                array.Add(obj);
            }

            return array;
        }

        private static void WriteGauntletLog(string correlationId, string phase, string status, string summary, JObject data)
        {
            try
            {
                string directory = LogDirectory();
                Directory.CreateDirectory(directory);
                JObject row = new JObject
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["correlationId"] = correlationId ?? string.Empty,
                    ["phase"] = phase ?? string.Empty,
                    ["status"] = status ?? string.Empty,
                    ["summary"] = summary ?? string.Empty,
                    ["data"] = data ?? new JObject()
                };

                string path = Path.Combine(directory, "action-gauntlet.jsonl");
                TrimGauntletLog(path);
                File.AppendAllText(path, row.ToString(Formatting.None) + Environment.NewLine);
            }
            catch
            {
                // Test logging should never destabilize the campaign.
            }
        }

        private static void TrimGauntletLog(string path)
        {
            const long maximumBytes = 8L * 1024L * 1024L;
            const long retainedBytes = 4L * 1024L * 1024L;
            if (!File.Exists(path)) return;
            FileInfo info = new FileInfo(path);
            if (info.Length <= maximumBytes) return;

            int length = (int)Math.Min(info.Length, retainedBytes + 4096L);
            byte[] buffer = new byte[length];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Seek(-length, SeekOrigin.End);
                int read = 0;
                while (read < buffer.Length)
                {
                    int amount = stream.Read(buffer, read, buffer.Length - read);
                    if (amount <= 0) break;
                    read += amount;
                }
                int start = 0;
                if (info.Length > length)
                {
                    while (start < read && buffer[start] != (byte)'\n') start++;
                    if (start < read) start++;
                }
                stream.SetLength(0);
                stream.Position = 0;
                stream.Write(buffer, start, Math.Max(0, read - start));
            }
        }

        private static string LogDirectory()
        {
            return Path.Combine(BasePath.Name, "Modules", "ReignBeta", "logs");
        }

        private static string VerificationGameDirectory()
        {
            string dataRoot = ReignInstallation.TryLoadCurrent()?.DataRoot
                ?? Path.Combine(BasePath.Name, "Modules", "ReignBeta", "server", "app", "data");
            return Path.Combine(dataRoot, "tests", "verification");
        }

        private static string VerificationGameCommandPath()
        {
            return Path.Combine(VerificationGameDirectory(), VerificationGameCommandFileName);
        }

        private static string VerificationGameStatusPath()
        {
            return Path.Combine(VerificationGameDirectory(), VerificationGameStatusFileName);
        }

        private static string NewCorrelationId(string suite)
        {
            return "gauntlet-" + suite + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static bool RequireTestMode()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || settings.McmTestModeEnabled)
            {
                return true;
            }

            Show("Enable Bannerlord Reign > MCM Test Mode > Enable MCM Test Mode before running the Action Gauntlet.");
            return false;
        }

        private static bool HasCampaign()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null && Clan.PlayerClan != null && MobileParty.MainParty != null && ReignServices.AI != null;
        }

        private static bool RequireCampaign()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                Show("Start or load a campaign before running the Action Gauntlet.");
                return false;
            }

            if (ReignServices.AI == null)
            {
                Show("Bannerlord Reign behavior is not active.");
                return false;
            }

            return true;
        }

        private static void Show(string message)
        {
            if (ReignMainThread.IsMainThread)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFF66CCFF)));
                return;
            }

            _ = ReignMainThread.InvokeAsync(() =>
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFF66CCFF))));
        }

        private sealed class ActionSpec
        {
            public ReignWorldActionRecord Action;
            public string SkipReason;
            public bool ExpectValidationFailure;
            public Action Cleanup;
        }

        private sealed class GauntletContext
        {
            public string CorrelationId;
            public Settlement ArenaSettlement;
            public Hero ActorHero;
            public Hero TargetHero;
            public Hero PartylessHero;
            public Hero SurrenderHero;
            public Hero ClanTransferHero;
            public Clan ClanTransferOriginalClan;
            public Hero PrisonerHero;
            public Hero KillTarget;
            public Settlement HostileFortification;
            public Settlement HostileVillage;
            public Workshop Workshop;
            public Kingdom TargetKingdom;
            public Clan TargetClan;
            public Clan JoinClan;
            public Clan MercenaryClan;
            public Clan LastJoinedClan;
            public Clan LastExiledClan;
        }

        private sealed class GauntletResult
        {
            public string Suite;
            public string Name;
            public string Status;
            public string Message;
            public string ActionId;
            public string Type;
            public string ResultCode;
            public string Outcome;
            public long DurationMs;
            public JObject Before;
            public JObject After;

            public JObject ToJson()
            {
                return new JObject
                {
                    ["suite"] = Suite ?? string.Empty,
                    ["name"] = Name ?? string.Empty,
                    ["status"] = Status ?? string.Empty,
                    ["message"] = Message ?? string.Empty,
                    ["actionId"] = ActionId ?? string.Empty,
                    ["type"] = Type ?? string.Empty,
                    ["resultCode"] = ResultCode ?? string.Empty,
                    ["outcome"] = Outcome ?? string.Empty,
                    ["durationMs"] = DurationMs,
                    ["before"] = Before ?? new JObject(),
                    ["after"] = After ?? new JObject()
                };
            }
        }

        private sealed class LiveDialogueCase
        {
            public string Id;
            public string Label;
            public Hero Speaker;
            public string PlayerText;
            public ReignWorldActionType ExpectedType;
            public bool Priority;
            public List<LiveDialogueItemTerm> ExpectedItems = new List<LiveDialogueItemTerm>();
            public bool PlayerGivesAssets;
            public int ExpectedGoldAmount;

            public JObject ToJson()
            {
                return new JObject
                {
                    ["id"] = Id ?? string.Empty,
                    ["label"] = Label ?? string.Empty,
                    ["speaker"] = Speaker?.StringId ?? string.Empty,
                    ["speakerName"] = Speaker?.Name?.ToString() ?? string.Empty,
                    ["playerText"] = PlayerText ?? string.Empty,
                    ["expectedType"] = ExpectedType.ToString(),
                    ["priority"] = Priority,
                    ["expectedItems"] = new JArray((ExpectedItems ?? new List<LiveDialogueItemTerm>()).Select(x => x.ToJson())),
                    ["playerGivesAssets"] = PlayerGivesAssets,
                    ["expectedGoldAmount"] = ExpectedGoldAmount
                };
            }
        }

        private sealed class LiveDialogueItemTerm
        {
            public string ItemId;
            public string Name;
            public int Amount;

            public LiveDialogueItemTerm Copy()
            {
                return new LiveDialogueItemTerm { ItemId = ItemId, Name = Name, Amount = Amount };
            }

            public JObject ToJson()
            {
                return new JObject
                {
                    ["itemId"] = ItemId ?? string.Empty,
                    ["name"] = Name ?? string.Empty,
                    ["amount"] = Amount
                };
            }
        }

        private sealed class LiveDialogueResult
        {
            public string Id;
            public string Speaker;
            public string SpeakerName;
            public string PlayerText;
            public ReignWorldActionType ExpectedType;
            public string Status;
            public string Message;
            public bool Ok;
            public string Reply;
            public string Error;
            public string ActionShadowPreview;
            public string TimingSummary;
            public string FailureClass;
            public string ExecutionState;
            public int QueuedCount;
            public JArray QueuedTypes = new JArray();
            public JArray ActionIds = new JArray();
            public JArray LedgerStatuses = new JArray();
            public string VerifiedReceipt;
            public JArray ExecutionEffects = new JArray();
            public long DurationMs;

            public JObject ToJson()
            {
                return new JObject
                {
                    ["id"] = Id ?? string.Empty,
                    ["speaker"] = Speaker ?? string.Empty,
                    ["speakerName"] = SpeakerName ?? string.Empty,
                    ["playerText"] = PlayerText ?? string.Empty,
                    ["expectedType"] = ExpectedType.ToString(),
                    ["status"] = Status ?? string.Empty,
                    ["message"] = Message ?? string.Empty,
                    ["ok"] = Ok,
                    ["reply"] = Reply ?? string.Empty,
                    ["error"] = Error ?? string.Empty,
                    ["actionShadowPreview"] = ActionShadowPreview ?? string.Empty,
                    ["timingSummary"] = TimingSummary ?? string.Empty,
                    ["failureClass"] = FailureClass ?? string.Empty,
                    ["executionState"] = ExecutionState ?? string.Empty,
                    ["queuedCount"] = QueuedCount,
                    ["queuedTypes"] = QueuedTypes ?? new JArray(),
                    ["actionIds"] = ActionIds ?? new JArray(),
                    ["ledgerStatuses"] = LedgerStatuses ?? new JArray(),
                    ["verifiedReceipt"] = VerifiedReceipt ?? string.Empty,
                    ["executionEffects"] = ExecutionEffects ?? new JArray(),
                    ["durationMs"] = DurationMs
                };
            }
        }

        private sealed class LiveDialogueBatchSummary
        {
            public string RunId;
            public string Status;
            public string Message;
            public int Passed;
            public int Skipped;
            public int Failed;
            public int Total;
            public long DurationMs;
            public JArray Results = new JArray();

            public JObject ToJson()
            {
                return new JObject
                {
                    ["runId"] = RunId ?? string.Empty,
                    ["status"] = Status ?? string.Empty,
                    ["message"] = Message ?? string.Empty,
                    ["passed"] = Passed,
                    ["skipped"] = Skipped,
                    ["failed"] = Failed,
                    ["total"] = Total,
                    ["durationMs"] = DurationMs,
                    ["results"] = Results ?? new JArray()
                };
            }
        }

        private sealed class QueuedSuite
        {
            public readonly string Name;
            public readonly string Id;
            public readonly Action<List<GauntletResult>, GauntletContext> Body;

            public QueuedSuite(string name, string id, Action<List<GauntletResult>, GauntletContext> body)
            {
                Name = name ?? string.Empty;
                Id = id ?? string.Empty;
                Body = body;
            }
        }

        private sealed class SuiteSummary
        {
            public string Suite;
            public string RunId;
            public long DurationMs;
            public int Passed;
            public int Skipped;
            public int Failed;
            public int Total;
            public string Message;

            public JObject ToJson()
            {
                return new JObject
                {
                    ["suite"] = Suite ?? string.Empty,
                    ["runId"] = RunId ?? string.Empty,
                    ["durationMs"] = DurationMs,
                    ["passed"] = Passed,
                    ["skipped"] = Skipped,
                    ["failed"] = Failed,
                    ["total"] = Total,
                    ["message"] = Message ?? string.Empty
                };
            }
        }
    }
}
