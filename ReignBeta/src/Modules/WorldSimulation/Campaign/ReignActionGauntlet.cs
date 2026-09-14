using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using Reign.Core.Contracts.Platform;
using ReignBeta.Settings;
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

namespace ReignBeta.Campaign
{
    public static class ReignActionGauntlet
    {
        private const string Mode = "action_gauntlet";
        private const float AllSuiteDelayDays = 0.0417f;
        private const int LiveDialogueBatchSize = 20;
        private const int OvernightGoalBatchPassThreshold = 18;
        private const int OvernightGoalConsecutiveBatches = 2;
        private const float OvernightBatchDelayDays = 0f;
        private const float OvernightCommandPollSeconds = 5f;
        private const string OvernightCommandFileName = "live-dialogue-overnight-command.json";
        private const string OvernightStatusFileName = "live-dialogue-overnight-status.json";
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
        private static bool _overnightLiveDialogueActive;
        private static bool _overnightLiveDialoguePaused;
        private static bool _overnightBatchInFlight;
        private static string _overnightRunId = string.Empty;
        private static string _lastOvernightCommandId = string.Empty;
        private static int _overnightBatchIndex;
        private static int _overnightMaxBatches = 100;
        private static int _overnightTotalPassed;
        private static int _overnightTotalSkipped;
        private static int _overnightTotalFailed;
        private static int _overnightTotalCases;
        private static int _overnightConsecutiveGoalBatches;
        private static int _overnightBestBatchPassed;
        private static float _overnightNextDay;
        private static float _overnightCommandPollAccumulator;
        private static string _lastOvernightSummary = "No overnight live dialogue gauntlet has run yet.";
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

        private static async Task<LiveDialogueBatchSummary> RunLiveDialogueBetaSuiteAsync(bool complexMcmPass = false)
        {
            _liveDialogueActive = true;
            string correlationId = NewCorrelationId("live-dialogue");
            Stopwatch timer = Stopwatch.StartNew();
            List<LiveDialogueResult> results = new List<LiveDialogueResult>();
            bool previousComplianceMode = ReignBetaSettings.Instance?.DialogueComplianceTestModeEnabled ?? false;
            LiveDialogueBatchSummary batchSummary = new LiveDialogueBatchSummary
            {
                RunId = correlationId,
                Status = "failed",
                Message = "Live dialogue beta suite did not complete."
            };

            try
            {
                GauntletContext context = BuildContext(correlationId);
                List<string> notes = new List<string>();
                EnsureLiveDialogueArena(context, notes);
                if (ReignBetaSettings.Instance != null)
                {
                    ReignBetaSettings.Instance.DialogueComplianceTestModeEnabled = true;
                }

                List<LiveDialogueCase> cases = complexMcmPass
                    ? BuildComplexLiveDialogueCases(context)
                    : BuildLiveDialogueCases(context);
                if (complexMcmPass && cases.Count != LiveDialogueBatchSize)
                {
                    _lastLiveDialogueSummary = "Complex MCM live pass requires exactly " + LiveDialogueBatchSize + " valid cases, but only " + cases.Count + " could be built from the loaded campaign.";
                    Show(_lastLiveDialogueSummary);
                    batchSummary.Message = _lastLiveDialogueSummary;
                    return batchSummary;
                }

                if (cases.Count == 0)
                {
                    _lastLiveDialogueSummary = "Live dialogue beta suite found no valid cases. Prepare the arena in an active campaign first.";
                    Show(_lastLiveDialogueSummary);
                    batchSummary.Message = _lastLiveDialogueSummary;
                    return batchSummary;
                }

                JObject startData = ContextJson(context);
                startData["notes"] = new JArray(notes);
                startData["caseCount"] = cases.Count;
                startData["batchSize"] = LiveDialogueBatchSize;
                startData["profile"] = complexMcmPass ? "complex_mcm_20" : "mixed_live_20";
                startData["cases"] = new JArray(cases.Select(x => x.ToJson()));
                WriteGauntletLog(correlationId, "live_dialogue.start", "started", "Live dialogue beta suite started.", startData);
                _ = ReignServerClient.IngestAuditAsync(correlationId, Mode, "gauntlet.live_dialogue.start", "started", "Live dialogue beta suite started.", startData);
                Show("Live dialogue beta suite started: " + cases.Count + (complexMcmPass ? " complex" : " natural") + " LLM turns. Keep the server open.");
                await WaitForLiveDialogueClientWarmupAsync().ConfigureAwait(false);

                foreach (LiveDialogueCase testCase in cases)
                {
                    if (!_liveDialogueActive)
                    {
                        break;
                    }

                    LiveDialogueResult result = await RunLiveDialogueCaseAsync(correlationId, testCase).ConfigureAwait(false);
                    results.Add(result);
                    WriteGauntletLog(correlationId, "live_dialogue." + testCase.Id, result.Status, result.Message, result.ToJson());
                    _ = ReignServerClient.IngestAuditAsync(correlationId, Mode, "gauntlet.live_dialogue." + testCase.Id, result.Status, result.Message, result.ToJson(), durationMs: result.DurationMs);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Live dialogue beta suite", ex);
                results.Add(new LiveDialogueResult
                {
                    Id = "suite_exception",
                    Speaker = string.Empty,
                    PlayerText = string.Empty,
                    ExpectedType = ReignWorldActionType.Unknown,
                    Status = "failed",
                    Message = ex.Message
                });
            }
            finally
            {
                if (ReignBetaSettings.Instance != null)
                {
                    ReignBetaSettings.Instance.DialogueComplianceTestModeEnabled = previousComplianceMode;
                }

                timer.Stop();
                int passed = results.Count(x => x.Status == "passed");
                int skipped = results.Count(x => x.Status == "skipped");
                int failed = results.Count(x => x.Status == "failed");
                _lastLiveDialogueSummary = "Live dialogue beta suite: passed=" + passed + ", skipped=" + skipped + ", failed=" + failed + ", total=" + results.Count + ".";

                JObject summary = new JObject
                {
                    ["runId"] = correlationId,
                    ["durationMs"] = timer.ElapsedMilliseconds,
                    ["passed"] = passed,
                    ["skipped"] = skipped,
                    ["failed"] = failed,
                    ["total"] = results.Count,
                    ["results"] = new JArray(results.Select(x => x.ToJson()))
                };

                WriteGauntletLog(correlationId, "live_dialogue.summary", failed == 0 ? "completed" : "failed", _lastLiveDialogueSummary, summary);
                _ = ReignServerClient.IngestAuditAsync(correlationId, Mode, "gauntlet.live_dialogue.summary", failed == 0 ? "completed" : "failed", _lastLiveDialogueSummary, summary, durationMs: timer.ElapsedMilliseconds);
                _liveDialogueActive = false;
                Show(_lastLiveDialogueSummary);
                batchSummary = new LiveDialogueBatchSummary
                {
                    RunId = correlationId,
                    Status = failed == 0 ? "completed" : "failed",
                    Message = _lastLiveDialogueSummary,
                    Passed = passed,
                    Skipped = skipped,
                    Failed = failed,
                    Total = results.Count,
                    DurationMs = timer.ElapsedMilliseconds,
                    Results = new JArray(results.Select(x => x.ToJson()))
                };
                _lastLiveDialogueRunId = correlationId;
                _lastLiveDialoguePassed = passed;
                _lastLiveDialogueSkipped = skipped;
                _lastLiveDialogueFailed = failed;
                _lastLiveDialogueTotal = results.Count;
                _lastLiveDialogueResults = batchSummary.Results;
            }

            return batchSummary;
        }

        private static async Task RunOvernightLiveDialogueBatchAsync()
        {
            _overnightBatchInFlight = true;
            int batchNumber = _overnightBatchIndex + 1;
            WriteOvernightStatus("running_batch", "Running live dialogue batch " + batchNumber + " of " + _overnightMaxBatches + ".");

            try
            {
                LiveDialogueBatchSummary summary = await RunLiveDialogueBetaSuiteAsync().ConfigureAwait(false);
                _overnightBatchIndex++;
                _overnightTotalPassed += summary.Passed;
                _overnightTotalSkipped += summary.Skipped;
                _overnightTotalFailed += summary.Failed;
                _overnightTotalCases += summary.Total;
                bool goalBatch = summary.Passed >= OvernightGoalBatchPassThreshold && summary.Total >= LiveDialogueBatchSize;
                bool repairNeeded = LiveDialogueNeedsRepair(summary);
                _overnightBestBatchPassed = Math.Max(_overnightBestBatchPassed, summary.Passed);
                _overnightConsecutiveGoalBatches = goalBatch ? _overnightConsecutiveGoalBatches + 1 : 0;

                JObject data = new JObject
                {
                    ["overnightRunId"] = _overnightRunId,
                    ["batch"] = batchNumber,
                    ["maxBatches"] = _overnightMaxBatches,
                    ["summary"] = summary.ToJson(),
                    ["totals"] = OvernightTotalsJson(),
                    ["goalBatch"] = goalBatch,
                    ["repairNeeded"] = repairNeeded,
                    ["goalPassThreshold"] = OvernightGoalBatchPassThreshold,
                    ["goalConsecutiveTarget"] = OvernightGoalConsecutiveBatches,
                    ["consecutiveGoalBatches"] = _overnightConsecutiveGoalBatches
                };
                string batchStatus = repairNeeded ? "failed" : goalBatch ? "goal_batch" : "completed_below_goal";
                WriteGauntletLog(_overnightRunId, "live_dialogue.overnight.batch", batchStatus, summary.Message, data);

                if (repairNeeded)
                {
                    _overnightLiveDialoguePaused = true;
                    _lastOvernightSummary = "Overnight live dialogue paused for repair after batch " + batchNumber + ": " + summary.Failed + " failed, " + summary.Passed + " passed.";
                    WriteOvernightStatus("paused_for_repair", _lastOvernightSummary, summary);
                    Show(_lastOvernightSummary);
                    return;
                }

                if (_overnightConsecutiveGoalBatches >= OvernightGoalConsecutiveBatches)
                {
                    CompleteOvernightLiveDialogueGauntlet("Reached target: " + OvernightGoalConsecutiveBatches + " consecutive 20-case batches with at least " + OvernightGoalBatchPassThreshold + " successes.");
                    return;
                }

                if (_overnightBatchIndex >= _overnightMaxBatches)
                {
                    CompleteOvernightLiveDialogueGauntlet("Reached requested safety batch count before target.");
                    return;
                }

                _overnightNextDay = CurrentDay() + OvernightBatchDelayDays;
                _lastOvernightSummary = "Overnight live dialogue batch " + batchNumber + " scored " + summary.Passed + "/" + summary.Total + "; consecutive target batches=" + _overnightConsecutiveGoalBatches + "/" + OvernightGoalConsecutiveBatches + ". Next batch starts immediately.";
                WriteOvernightStatus("waiting_next_batch", _lastOvernightSummary, summary);
                Show(_lastOvernightSummary);
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Overnight live dialogue batch", ex);
                _overnightLiveDialoguePaused = true;
                _lastOvernightSummary = "Overnight live dialogue paused after exception: " + ex.Message;
                WriteOvernightStatus("paused_for_repair", _lastOvernightSummary);
                Show(_lastOvernightSummary);
            }
            finally
            {
                _overnightBatchInFlight = false;
            }
        }

        private static void StartOvernightLiveDialogueGauntlet(int maxBatches, string source)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null)
            {
                settings.McmTestModeEnabled = true;
                settings.DialogueComplianceTestModeEnabled = true;
            }

            _overnightRunId = NewCorrelationId("overnight-live-dialogue");
            _overnightBatchIndex = 0;
            _overnightMaxBatches = Math.Max(1, maxBatches);
            _overnightTotalPassed = 0;
            _overnightTotalSkipped = 0;
            _overnightTotalFailed = 0;
            _overnightTotalCases = 0;
            _overnightConsecutiveGoalBatches = 0;
            _overnightBestBatchPassed = 0;
            _overnightNextDay = CurrentDay();
            _overnightLiveDialogueActive = true;
            _overnightLiveDialoguePaused = false;
            _overnightBatchInFlight = false;
            _lastOvernightSummary = "Overnight live dialogue gauntlet started from " + (source ?? "unknown") + ". Max batches=" + _overnightMaxBatches + ".";

            WriteOvernightStatus(HasCampaign() ? "running" : "waiting_for_campaign", _lastOvernightSummary);
            WriteGauntletLog(_overnightRunId, "live_dialogue.overnight.start", "started", _lastOvernightSummary, new JObject
            {
                ["runId"] = _overnightRunId,
                ["source"] = source ?? string.Empty,
                ["maxBatches"] = _overnightMaxBatches,
                ["batchSize"] = LiveDialogueBatchSize
            });
            Show(_lastOvernightSummary);
        }

        private static void StopOvernightLiveDialogueGauntlet(string reason)
        {
            _overnightLiveDialogueActive = false;
            _overnightLiveDialoguePaused = false;
            _overnightBatchInFlight = false;
            _liveDialogueActive = false;
            _lastOvernightSummary = "Overnight live dialogue gauntlet stopped. " + (reason ?? string.Empty);
            WriteOvernightStatus("stopped", _lastOvernightSummary);
            WriteGauntletLog(string.IsNullOrWhiteSpace(_overnightRunId) ? NewCorrelationId("overnight-stop") : _overnightRunId, "live_dialogue.overnight.stop", "stopped", _lastOvernightSummary, OvernightTotalsJson());
            Show(_lastOvernightSummary);
        }

        private static void ResumeOvernightLiveDialogueGauntlet(string source)
        {
            if (string.IsNullOrWhiteSpace(_overnightRunId))
            {
                TryRestoreOvernightStateFromStatus();
                if (string.IsNullOrWhiteSpace(_overnightRunId))
                {
                    StartOvernightLiveDialogueGauntlet(_overnightMaxBatches, source ?? "resume");
                    return;
                }
            }

            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null)
            {
                settings.McmTestModeEnabled = true;
                settings.DialogueComplianceTestModeEnabled = true;
            }

            _overnightLiveDialogueActive = true;
            _overnightLiveDialoguePaused = false;
            _overnightBatchInFlight = false;
            _overnightNextDay = CurrentDay();
            _lastOvernightSummary = "Overnight live dialogue gauntlet resumed from " + (source ?? "command") + " at batch " + (_overnightBatchIndex + 1) + ".";
            WriteOvernightStatus("running", _lastOvernightSummary);
            WriteGauntletLog(_overnightRunId, "live_dialogue.overnight.resume", "started", _lastOvernightSummary, OvernightTotalsJson());
            Show(_lastOvernightSummary);
        }

        private static void TryRestoreOvernightStateFromStatus()
        {
            try
            {
                string path = OvernightStatusPath();
                if (!File.Exists(path))
                {
                    return;
                }

                JObject status = JObject.Parse(File.ReadAllText(path));
                string runId = status.Value<string>("overnightRunId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return;
                }

                _overnightRunId = runId;
                _overnightBatchIndex = status.Value<int?>("batchIndex") ?? 0;
                _overnightMaxBatches = Math.Max(1, status.Value<int?>("maxBatches") ?? _overnightMaxBatches);
                JObject totals = status["totals"] as JObject ?? new JObject();
                _overnightTotalPassed = totals.Value<int?>("passed") ?? 0;
                _overnightTotalSkipped = totals.Value<int?>("skipped") ?? 0;
                _overnightTotalFailed = totals.Value<int?>("failed") ?? 0;
                _overnightTotalCases = totals.Value<int?>("total") ?? 0;
                _overnightConsecutiveGoalBatches = totals.Value<int?>("consecutiveGoalBatches") ?? 0;
                _overnightBestBatchPassed = totals.Value<int?>("bestBatchPassed") ?? 0;
                _lastOvernightSummary = "Restored overnight run " + _overnightRunId + " from status file.";
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Could not restore overnight state: " + ex.Message);
            }
        }

        private static void CompleteOvernightLiveDialogueGauntlet(string reason)
        {
            _overnightLiveDialogueActive = false;
            _overnightLiveDialoguePaused = false;
            _overnightBatchInFlight = false;
            _lastOvernightSummary = "Overnight live dialogue complete: batches=" + _overnightBatchIndex + ", passed=" + _overnightTotalPassed + ", skipped=" + _overnightTotalSkipped + ", failed=" + _overnightTotalFailed + ", total=" + _overnightTotalCases + ". " + (reason ?? string.Empty);
            WriteOvernightStatus("complete", _lastOvernightSummary);
            WriteGauntletLog(_overnightRunId, "live_dialogue.overnight.complete", _overnightTotalFailed == 0 ? "completed" : "failed", _lastOvernightSummary, OvernightTotalsJson());
            Show(_lastOvernightSummary);
        }

        private static void PollOvernightCommandFile()
        {
            try
            {
                string path = OvernightCommandPath();
                if (!File.Exists(path))
                {
                    return;
                }

                JObject command = JObject.Parse(File.ReadAllText(path));
                string commandId = command.Value<string>("commandId") ?? command.Value<string>("id") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(commandId) && string.Equals(commandId, _lastOvernightCommandId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _lastOvernightCommandId = string.IsNullOrWhiteSpace(commandId) ? Guid.NewGuid().ToString("N") : commandId;
                string name = (command.Value<string>("command") ?? string.Empty).Trim().ToLowerInvariant();
                int maxBatches = command.Value<int?>("maxBatches") ?? _overnightMaxBatches;
                switch (name)
                {
                    case "start":
                        StartOvernightLiveDialogueGauntlet(maxBatches, "command_file");
                        break;
                    case "resume":
                        ResumeOvernightLiveDialogueGauntlet("command_file");
                        break;
                    case "stop":
                        StopOvernightLiveDialogueGauntlet("Command file requested stop.");
                        break;
                    case "status":
                        WriteOvernightStatus(_overnightLiveDialoguePaused ? "paused_for_repair" : (_overnightLiveDialogueActive ? "running" : "idle"), _lastOvernightSummary);
                        break;
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Overnight command poll failed: " + ex.Message);
                WriteOvernightStatus("command_error", "Could not read overnight command: " + ex.Message);
            }
        }

        private static void PollVerificationGameCommand()
        {
            try
            {
                string path = VerificationGameCommandPath();
                if (!File.Exists(path)) return;
                JObject command = JObject.Parse(File.ReadAllText(path));
                string commandId = command.Value<string>("commandId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(commandId) || string.Equals(commandId, _verificationCommandId, StringComparison.OrdinalIgnoreCase)) return;
                _verificationCommandId = commandId;
                string name = (command.Value<string>("command") ?? "run").Trim().ToLowerInvariant();
                if (name == "cancel")
                {
                    _queuedAllActive = false;
                    _queuedAllSuites = null;
                    _verificationGameRunActive = false;
                    WriteVerificationGameStatus("cancelled", "Native game verification was cancelled.", 0, 0, 0, 0);
                    return;
                }
                if (!HasCampaign())
                {
                    WriteVerificationGameStatus("waiting_for_campaign", "Load the disposable Bannerlord verification save.", 0, 0, 0, 0);
                    _verificationCommandId = string.Empty;
                    return;
                }

                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null) settings.McmTestModeEnabled = true;
                _verificationGameRunActive = true;
                WriteVerificationGameStatus("preparing", "Preparing the disposable native action arena.", 0, 0, 0, 0);
                PrepareArena();
                RunAllSuites();
                WriteVerificationGameStatus("running", "Native action gauntlets are running one suite per in-game hour.", 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                _verificationGameRunActive = false;
                ReignLog.Warn("Verification game command failed: " + ex.Message);
                WriteVerificationGameStatus("failed", "Game verification command failed: " + ex.Message, 0, 0, 1, 1);
            }
        }

        private static void WriteVerificationGameStatus(string state, string message, int passed, int skipped, int failed, int total)
        {
            try
            {
                string path = VerificationGameStatusPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                JObject status = new JObject
                {
                    ["commandId"] = _verificationCommandId ?? string.Empty,
                    ["state"] = state ?? string.Empty,
                    ["message"] = message ?? string.Empty,
                    ["passed"] = passed,
                    ["skipped"] = skipped,
                    ["failed"] = failed,
                    ["total"] = total,
                    ["campaignLoaded"] = HasCampaign(),
                    ["updatedUtc"] = DateTime.UtcNow.ToString("o"),
                    ["gauntletRunId"] = _queuedAllRunId ?? string.Empty
                };
                File.WriteAllText(path, status.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Could not write verification game status: " + ex.Message);
            }
        }

        private static async Task<LiveDialogueResult> RunLiveDialogueCaseAsync(string correlationId, LiveDialogueCase testCase)
        {
            Stopwatch timer = Stopwatch.StartNew();
            LiveDialogueResult result = new LiveDialogueResult
            {
                Id = testCase.Id,
                Speaker = testCase.Speaker?.StringId ?? string.Empty,
                SpeakerName = testCase.Speaker?.Name?.ToString() ?? string.Empty,
                PlayerText = testCase.PlayerText,
                ExpectedType = testCase.ExpectedType,
                Status = "failed"
            };

            try
            {
                if (testCase.Speaker == null)
                {
                    result.Status = "skipped";
                    result.Message = "No speaker was available.";
                    return result;
                }

                ReignIndividualChatScreenVM vm = await OpenLiveDialogueChatAsync(testCase.Speaker).ConfigureAwait(false);
                if (vm == null)
                {
                    result.Message = "Individual chat screen did not open.";
                    return result;
                }

                await WaitForChatReadyAsync(vm).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    PrepareLiveDialogueCaseWorldState(testCase);
                    vm.AddAutomationSystemLine("Live beta test: " + testCase.Label);
                }).ConfigureAwait(false);
                HashSet<string> beforeActionIds = await ReignMainThread.InvokeAsync(() => new HashSet<string>((ReignAICampaignBehavior.Instance?.Actions ?? new List<ReignWorldActionRecord>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ActionId))
                    .Select(x => x.ActionId))).ConfigureAwait(false);
                ReignDialogueReply reply = await vm.SendAutomationLineAsync(testCase.PlayerText).ConfigureAwait(false);
                result.Ok = reply.Ok;
                result.Reply = reply.Text ?? string.Empty;
                result.Error = reply.Error ?? string.Empty;
                result.ActionShadowPreview = reply.ActionShadowPreview ?? string.Empty;
                result.TimingSummary = reply.TimingSummary ?? string.Empty;
                result.QueuedCount = reply.QueuedActions?.Count ?? 0;
                result.QueuedTypes = new JArray((reply.QueuedActions ?? new List<ReignWorldActionRecord>()).Select(x => x.Type.ToString()));
                result.ActionIds = new JArray((reply.QueuedActions ?? new List<ReignWorldActionRecord>()).Select(x => x.ActionId ?? string.Empty));

                List<string> queuedIds = (reply.QueuedActions ?? new List<ReignWorldActionRecord>())
                    .Select(x => x.ActionId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                List<ReignWorldActionRecord> ledgerActions = await ReignMainThread.InvokeAsync(() => (ReignAICampaignBehavior.Instance?.Actions ?? new List<ReignWorldActionRecord>())
                    .Where(x => x != null && (queuedIds.Contains(x.ActionId) || !beforeActionIds.Contains(x.ActionId)))
                    .ToList()).ConfigureAwait(false);
                result.LedgerStatuses = new JArray(ledgerActions.Select(x => x.Type + ":" + x.Status + (string.IsNullOrWhiteSpace(x.FailureReason) ? "" : ":" + x.FailureReason)));
                result.ExecutionState = DescribeLedgerState(testCase.ExpectedType, ledgerActions);

                bool expectedQueued = (reply.QueuedActions ?? new List<ReignWorldActionRecord>()).Any(x => x != null && x.Type == testCase.ExpectedType)
                    || ledgerActions.Any(x => x != null && x.Type == testCase.ExpectedType);
                bool failedAction = ledgerActions.Any(x => x != null && x.Type == testCase.ExpectedType && x.Status == ReignWorldActionStatus.Failed);
                ReignWorldActionRecord expectedAction = ledgerActions.LastOrDefault(x => x != null && x.Type == testCase.ExpectedType);
                ReignActionResult executionResult = expectedAction == null
                    ? null
                    : await ReignMainThread.InvokeAsync(() => ReignAICampaignBehavior.Instance?.GetLastActionResult(expectedAction.ActionId)).ConfigureAwait(false);
                result.VerifiedReceipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(executionResult);
                result.ExecutionEffects = executionResult?.Effects == null
                    ? new JArray()
                    : new JArray(executionResult.Effects.Select(JObject.FromObject));
                bool verificationFailed = false;
                string verificationFailure = string.Empty;
                if (expectedAction != null && ReignActionReceiptFormatter.RequiresVerifiedReceipt(expectedAction.Type))
                {
                    verificationFailed = !ReignActionReceiptFormatter.ValidatePromisedEffects(expectedAction, executionResult, out verificationFailure);
                }

                if (!verificationFailed && !ValidateCaseExpectedEffects(testCase, executionResult, out verificationFailure))
                {
                    verificationFailed = true;
                }

                if (!reply.Ok)
                {
                    result.FailureClass = "llm_or_server_failed";
                    result.Message = "LLM dialogue call failed: " + FriendlyResultText(reply.Error);
                }
                else if (!expectedQueued)
                {
                    result.FailureClass = result.QueuedCount <= 0 ? "no_action_detected" : "wrong_action_detected";
                    result.Message = "Expected " + testCase.ExpectedType + " but queued " + string.Join(", ", (reply.QueuedActions ?? new List<ReignWorldActionRecord>()).Select(x => x.Type.ToString()).DefaultIfEmpty("none")) + ".";
                }
                else if (failedAction)
                {
                    result.FailureClass = "action_failed_after_queue";
                    result.Message = "Expected action queued but failed: " + string.Join("; ", ledgerActions.Where(x => x.Type == testCase.ExpectedType).Select(x => x.FailureReason).Where(x => !string.IsNullOrWhiteSpace(x)).DefaultIfEmpty("no failure reason"));
                }
                else if (verificationFailed)
                {
                    result.FailureClass = "execution_verification_failed";
                    result.Message = "Expected action was detected but its promised game-state changes were not verified: " + verificationFailure;
                }
                else
                {
                    result.Status = "passed";
                    result.FailureClass = string.Empty;
                    result.Message = "Expected action " + testCase.ExpectedType + " reached " + (string.IsNullOrWhiteSpace(result.ExecutionState) ? "queued" : result.ExecutionState) + " through normal dialogue."
                        + (string.IsNullOrWhiteSpace(result.VerifiedReceipt) ? string.Empty : " Verified: " + result.VerifiedReceipt + ".");
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                ReignLog.Exception("Live dialogue beta case " + testCase.Id, ex);
            }
            finally
            {
                timer.Stop();
                result.DurationMs = timer.ElapsedMilliseconds;
            }

            return result;
        }

        private static async Task<ReignIndividualChatScreenVM> OpenLiveDialogueChatAsync(Hero speaker)
        {
            await ReignMainThread.InvokeAsync(() =>
            {
                if (ReignIndividualChatScreenManager.IsOpen)
                {
                    ReignIndividualChatScreenManager.Close();
                }
            }).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                ReignIndividualChatScreenVM vm = await ReignMainThread.InvokeAsync(() =>
                {
                    ReignIndividualChatScreenManager.OpenForHero(speaker);
                    return ReignIndividualChatScreenManager.ActiveViewModel;
                }).ConfigureAwait(false);

                if (vm != null)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    return vm;
                }

                await Task.Delay(350).ConfigureAwait(false);
            }

            return null;
        }

        private static void PrepareLiveDialogueCaseWorldState(LiveDialogueCase testCase)
        {
            if (testCase == null)
            {
                return;
            }

            EnsureActorResources(testCase.Speaker, null);
            EnsurePlayerResources(null);
            EnsureCaseTradeResources(testCase);

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Kingdom speakerKingdom = testCase.Speaker?.Clan?.Kingdom;
            if (testCase.ExpectedType == ReignWorldActionType.DiplomacyMakePeace
                || testCase.ExpectedType == ReignWorldActionType.DiplomacyDemandReparationsPeace
                || testCase.ExpectedType == ReignWorldActionType.DiplomacyDemandSettlementPeace
                || testCase.ExpectedType == ReignWorldActionType.DiplomacyDemandSurrenderPeace)
            {
                EnsureWar(playerKingdom, speakerKingdom, null);
                return;
            }

            if (testCase.ExpectedType == ReignWorldActionType.DiplomacySignTradeAgreement
                || testCase.ExpectedType == ReignWorldActionType.DiplomacySignNonAggressionPact
                || testCase.ExpectedType == ReignWorldActionType.DiplomacySignAlliance
                || testCase.ExpectedType == ReignWorldActionType.DiplomacySignDefensivePact
                || testCase.ExpectedType == ReignWorldActionType.DiplomacyPackage)
            {
                EnsurePeace(playerKingdom, speakerKingdom, null);
            }
        }

        private static void EnsureCaseTradeResources(LiveDialogueCase testCase)
        {
            if (testCase?.ExpectedItems == null || testCase.ExpectedItems.Count == 0)
            {
                return;
            }

            Hero sourceHero = testCase.PlayerGivesAssets ? Hero.MainHero : testCase.Speaker;
            MobileParty sourceParty = sourceHero == Hero.MainHero ? MobileParty.MainParty : sourceHero?.PartyBelongedTo;
            if (sourceParty == null)
            {
                return;
            }

            Hero goldPayer = testCase.PlayerGivesAssets ? testCase.Speaker : Hero.MainHero;
            if (testCase.ExpectedGoldAmount > 0)
            {
                EnsureHeroGold(goldPayer, testCase.ExpectedGoldAmount + 10000);
            }

            foreach (LiveDialogueItemTerm term in testCase.ExpectedItems)
            {
                if (term == null)
                {
                    continue;
                }

                ItemObject item = ReignObjectResolver.FindItem(term.ItemId);
                if (item == null || term.Amount <= 0)
                {
                    continue;
                }

                int existing = sourceParty.ItemRoster.GetItemNumber(item);
                if (existing < term.Amount)
                {
                    sourceParty.ItemRoster.AddToCounts(item, term.Amount - existing);
                }
            }
        }

        private static bool ValidateCaseExpectedEffects(LiveDialogueCase testCase, ReignActionResult executionResult, out string reason)
        {
            reason = string.Empty;
            if (testCase?.ExpectedItems == null || testCase.ExpectedItems.Count == 0)
            {
                return true;
            }

            if (executionResult?.Effects == null)
            {
                reason = "The case has expected item transfers but no execution receipt was recorded.";
                return false;
            }

            Hero itemFrom = testCase.PlayerGivesAssets ? Hero.MainHero : testCase.Speaker;
            Hero itemTo = testCase.PlayerGivesAssets ? testCase.Speaker : Hero.MainHero;
            if (itemFrom == null || itemTo == null)
            {
                reason = "The case could not resolve its expected item-transfer parties.";
                return false;
            }

            foreach (LiveDialogueItemTerm term in testCase.ExpectedItems)
            {
                bool transferred = executionResult.Effects.Any(effect => IsExpectedItemTransfer(effect, term, itemFrom, itemTo));
                if (!transferred)
                {
                    reason = "Expected " + term.Amount + " " + term.Name + " from " + itemFrom.Name + " to " + itemTo.Name + " was not present in the verified receipt.";
                    return false;
                }
            }

            if (testCase.ExpectedGoldAmount > 0)
            {
                Hero goldFrom = testCase.PlayerGivesAssets ? testCase.Speaker : Hero.MainHero;
                Hero goldTo = testCase.PlayerGivesAssets ? Hero.MainHero : testCase.Speaker;
                bool goldTransferred = executionResult.Effects.Any(effect => IsExpectedGoldTransfer(effect, testCase.ExpectedGoldAmount, goldFrom, goldTo));
                if (!goldTransferred)
                {
                    reason = "Expected " + testCase.ExpectedGoldAmount + " denars from " + goldFrom.Name + " to " + goldTo.Name + " was not present in the verified receipt.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsExpectedItemTransfer(Dictionary<string, string> effect, LiveDialogueItemTerm term, Hero from, Hero to)
        {
            if (effect == null || term == null || from == null || to == null)
            {
                return false;
            }

            if (!effect.TryGetValue("effectType", out string effectType)
                || (!string.Equals(effectType, "item_transfer", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(effectType, "equipped_item_transfer", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return effect.TryGetValue("id", out string itemId)
                && string.Equals(itemId, term.ItemId, StringComparison.OrdinalIgnoreCase)
                && effect.TryGetValue("detail", out string detail)
                && DetailHasValue(detail, "from", from.StringId)
                && DetailHasValue(detail, "to", to.StringId)
                && DetailHasValue(detail, "amount", term.Amount.ToString());
        }

        private static bool IsExpectedGoldTransfer(Dictionary<string, string> effect, int amount, Hero from, Hero to)
        {
            if (effect == null || from == null || to == null
                || !effect.TryGetValue("effectType", out string effectType)
                || !string.Equals(effectType, "gold_transfer", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return effect.TryGetValue("id", out string recipientId)
                && string.Equals(recipientId, to.StringId, StringComparison.OrdinalIgnoreCase)
                && effect.TryGetValue("detail", out string detail)
                && DetailHasValue(detail, "from", from.StringId)
                && DetailHasValue(detail, "amount", amount.ToString());
        }

        private static bool DetailHasValue(string detail, string key, string expected)
        {
            return (detail ?? string.Empty).Split(';')
                .Any(part => string.Equals(part?.Trim(), (key ?? string.Empty) + "=" + (expected ?? string.Empty), StringComparison.OrdinalIgnoreCase));
        }

        private static async Task WaitForChatReadyAsync(ReignIndividualChatScreenVM vm)
        {
            for (int i = 0; i < 120 && vm != null; i++)
            {
                bool isBusy = await ReignMainThread.InvokeAsync(() => vm.IsBusy).ConfigureAwait(false);
                if (!isBusy)
                {
                    return;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private static async Task WaitForLiveDialogueClientWarmupAsync()
        {
            for (int i = 0; i < 20; i++)
            {
                bool campaignReady = TaleWorlds.CampaignSystem.Campaign.Current != null
                    && Hero.MainHero != null
                    && Clan.PlayerClan != null
                    && MobileParty.MainParty != null;
                if (campaignReady && i >= 12)
                {
                    return;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private static void EnsureLiveDialogueArena(GauntletContext context, List<string> notes)
        {
            EnsurePlayerResources(notes);
            EnsurePlayerKingdomState(notes);
            foreach (Hero hero in new[] { context.ActorHero, context.TargetHero, context.TargetKingdom?.Leader }.Where(x => x != null).Distinct())
            {
                EnsureActorResources(hero, notes);
                if (hero.PartyBelongedTo != null && context.ArenaSettlement != null)
                {
                    TeleportParty(hero.PartyBelongedTo, context.ArenaSettlement);
                    notes?.Add(hero.Name + " staged near " + context.ArenaSettlement.Name);
                }
            }

            if (context.PrisonerHero != null && !context.PrisonerHero.IsPrisoner)
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, context.PrisonerHero);
                notes?.Add(context.PrisonerHero.Name + " taken as live dialogue prisoner");
            }

            if (context.HostileVillage != null)
            {
                EnsureWar(context.ActorHero?.PartyBelongedTo?.MapFaction, context.HostileVillage.MapFaction, notes);
            }

            if (context.HostileFortification != null)
            {
                EnsureWar(context.ActorHero?.PartyBelongedTo?.MapFaction, context.HostileFortification.MapFaction, notes);
            }
        }

        private static List<LiveDialogueCase> BuildLiveDialogueCases(GauntletContext context)
        {
            List<LiveDialogueCase> cases = new List<LiveDialogueCase>();
            Hero actor = context.ActorHero ?? context.TargetHero ?? context.TargetKingdom?.Leader;
            Hero second = context.TargetHero ?? actor;
            Hero ruler = context.TargetKingdom?.Leader ?? actor;
            Settlement travel = context.ArenaSettlement ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown);
            Settlement hostileVillage = context.HostileVillage;
            Settlement hostileFort = context.HostileFortification;
            Settlement tradeSettlement = FindClanOwnedFortification(ruler?.Clan);
            Settlement surrenderSettlement = FindOwnedFortification(ruler?.Clan?.Kingdom, tradeSettlement);

            AddLiveCase(cases, "gold_gift_plain", "Gold gift plain", actor, "Give me one thousand denars as a goodwill gift.", ReignWorldActionType.RegularGiveGoldToPlayer);
            AddLiveCase(cases, "gold_gift_casual", "Gold gift casual phrasing", actor, "Hand me 250 denars from your purse so I can test the ledgers.", ReignWorldActionType.RegularGiveGoldToPlayer);
            if (second != null && second != actor)
            {
                AddLiveCase(cases, "transfer_gold_to_named_hero", "Gold transfer to named hero", actor, "Give " + second.Name + " 300 denars from your purse.", ReignWorldActionType.RegularTransferGold);
            }

            AddLiveCase(cases, "grain_from_npc", "NPC grain transfer", actor, "Give me 10 grain from your party supplies.", ReignWorldActionType.RegularTransferItem);
            AddLiveCase(cases, "grain_to_npc", "Player grain transfer", actor, "Take 100 grain from my party supplies.", ReignWorldActionType.RegularTransferItem);
            AddLiveCase(cases, "armor_transfer", "Armor transfer", actor, "Give me the armor you are wearing.", ReignWorldActionType.RegularTransferItem);
            AddLiveCase(cases, "trade_buy_armor", "Trade package armor purchase", actor, "I will pay you 5000 denars for your best armor.", ReignWorldActionType.RegularTradePackage);
            AddLiveCase(cases, "complex_trade_sell_grain_horses", "Complex trade: player grain and horses for gold", actor, "I will sell you 50 grain and 4 horses for 1800 denars.", ReignWorldActionType.RegularTradePackage, true);
            AddLiveCase(cases, "complex_trade_buy_grain_horses", "Complex trade: NPC grain and horses for gold", actor, "Sell me 40 grain and 3 horses for 2200 denars.", ReignWorldActionType.RegularTradePackage, true);
            if (context.PrisonerHero != null)
            {
                AddLiveCase(cases, "trade_prisoner_ransom", "Trade package prisoner ransom", actor, "I will pay you 20000 denars for the prisoner " + context.PrisonerHero.Name + ".", ReignWorldActionType.RegularTradePackage);
                AddLiveCase(cases, "complex_prisoner_gold_grain_package", "Complex diplomatic package: prisoner for gold and grain", ruler, "Accept this diplomatic package: I will pay you 12000 denars and send 50 grain for the release of prisoner " + context.PrisonerHero.Name + " and a public non-aggression promise.", ReignWorldActionType.DiplomacyPackage, true);
            }
            else
            {
                AddLiveCase(cases, "trade_buy_grain", "Trade package grain purchase", actor, "I will pay you 300 denars for 50 grain.", ReignWorldActionType.RegularTradePackage);
            }

            AddLiveCase(cases, "trade_agreement", "Trade agreement", ruler, "Let us sign a trade agreement between our kingdoms.", ReignWorldActionType.DiplomacySignTradeAgreement);
            AddLiveCase(cases, "complex_marriage_dowry_horses", "Complex diplomatic package: marriage dowry with horses", ruler, "Accept a compound marriage alliance: one of your family members marries into my clan, and I will pay you 12000 denars plus 6 horses as the dowry.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_alliance_gold_grain_horses", "Complex diplomatic package: alliance for resources", ruler, "Accept this alliance package: I give your kingdom 15000 denars, 80 grain, and 5 horses as the price for a public alliance.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "marriage_alliance_plain", "Marriage alliance plain", ruler, "Make a marriage alliance between your clan and mine.", ReignWorldActionType.PoliticsMarriageAlliance, true);
            AddLiveCase(cases, "non_aggression", "Non-aggression pact", ruler, "Agree to a non-aggression pact with my kingdom.", ReignWorldActionType.DiplomacySignNonAggressionPact);
            AddLiveCase(cases, "alliance", "Alliance", ruler, "Accept an alliance between our kingdoms.", ReignWorldActionType.DiplomacySignAlliance);
            AddLiveCase(cases, "make_peace", "Make peace", ruler, "End the war with my kingdom and make peace now.", ReignWorldActionType.DiplomacyMakePeace);
            AddLiveCase(cases, "reparations", "Reparations", ruler, "Pay me 5000 denars in reparations and make peace.", ReignWorldActionType.DiplomacyDemandReparationsPeace);
            if (tradeSettlement != null)
            {
                AddLiveCase(cases, "trade_buy_settlement", "Trade package settlement purchase", ruler, "I will pay you 100 denars for " + tradeSettlement.Name + ".", ReignWorldActionType.RegularTradePackage);
            }

            if (surrenderSettlement != null)
            {
                AddLiveCase(cases, "settlement_surrender", "Settlement surrender", ruler, "Give me " + surrenderSettlement.Name + " in exchange for peace.", ReignWorldActionType.DiplomacyDemandSettlementPeace);
            }

            if (travel != null)
            {
                AddLiveCase(cases, "go_to_settlement", "Go to settlement", actor, "Ride to " + travel.Name + " now.", ReignWorldActionType.RegularGoToSettlement);
            }

            if (hostileFort != null)
            {
                AddLiveCase(cases, "besiege_settlement", "Besiege settlement", actor, "Besiege " + hostileFort.Name + " and take the walls.", ReignWorldActionType.RegularBesiegeSettlement);
            }

            AddLiveCase(cases, "follow_player", "Follow player", actor, "Follow my party on the campaign map.", ReignWorldActionType.RegularFollowOnMap);
            if (travel != null)
            {
                AddLiveCase(cases, "patrol_settlement", "Patrol settlement", actor, "Patrol around " + travel.Name + " until I call for you.", ReignWorldActionType.RegularPatrolAroundSettlement);
                AddLiveCase(cases, "wait_near_settlement", "Wait near settlement", actor, "Wait near " + travel.Name + " and do not wander off.", ReignWorldActionType.RegularWaitNearSettlement);
            }
            AddLiveCase(cases, "trade_sell_grain", "Trade package player grain sale", actor, "I will sell you 50 grain for 300 denars.", ReignWorldActionType.RegularTradePackage);

            List<LiveDialogueCase> validCases = cases.Where(x => x.Speaker != null).ToList();
            List<LiveDialogueCase> priorityCases = validCases
                .Where(x => x.Priority)
                .OrderBy(_ => Guid.NewGuid())
                .Take(LiveDialogueBatchSize)
                .ToList();
            List<LiveDialogueCase> fillerCases = validCases
                .Where(x => !x.Priority)
                .OrderBy(_ => Guid.NewGuid())
                .Take(Math.Max(0, LiveDialogueBatchSize - priorityCases.Count))
                .ToList();

            return priorityCases
                .Concat(fillerCases)
                .OrderBy(_ => Guid.NewGuid())
                .ToList();
        }

        private static List<LiveDialogueCase> BuildComplexLiveDialogueCases(GauntletContext context)
        {
            List<LiveDialogueCase> cases = new List<LiveDialogueCase>();
            Hero actor = context.ActorHero ?? context.TargetHero ?? context.TargetKingdom?.Leader;
            Hero ruler = context.TargetKingdom?.Leader ?? actor;

            List<List<LiveDialogueItemTerm>> tradeBundles = BuildBroadTradeGoodBundles();
            for (int i = 0; i < tradeBundles.Count; i++)
            {
                List<LiveDialogueItemTerm> bundle = tradeBundles[i];
                string bundleText = DescribeLiveDialogueItems(bundle);
                int price = EstimateLiveDialogueBundlePrice(bundle, 1200 + i * 250);
                AddLiveCase(cases, "complex_mcm_sell_goods_" + (i + 1), "Player sells broad trade-goods bundle " + (i + 1), actor,
                    "I will sell you " + bundleText + " from my trade goods for " + price + " denars.", ReignWorldActionType.RegularTradePackage, true, bundle, true, price);
                AddLiveCase(cases, "complex_mcm_buy_goods_" + (i + 1), "Player buys broad trade-goods bundle " + (i + 1), actor,
                    "Sell me " + bundleText + " from your party trade goods for " + price + " denars.", ReignWorldActionType.RegularTradePackage, true, bundle, false, price);
            }

            List<List<LiveDialogueItemTerm>> equipmentBundles = BuildEquipmentBundles();
            for (int i = 0; i < equipmentBundles.Count; i++)
            {
                List<LiveDialogueItemTerm> bundle = equipmentBundles[i];
                string bundleText = DescribeLiveDialogueItems(bundle);
                int price = EstimateLiveDialogueBundlePrice(bundle, 7000 + i * 2000);
                bool playerGivesAssets = i % 2 == 0;
                string direction = playerGivesAssets
                    ? "I will sell you my " + bundleText + " equipment bundle for " + price + " denars."
                    : "Sell me your " + bundleText + " equipment bundle for " + price + " denars.";
                AddLiveCase(cases, "complex_mcm_equipment_" + (i + 1), (playerGivesAssets ? "Player sells" : "Player buys") + " equipment bundle " + (i + 1), actor,
                    direction, ReignWorldActionType.RegularTradePackage, true, bundle, playerGivesAssets, price);
            }

            Hero namedMarriageHero = FindEligibleMarriageHero(ruler?.Clan, Clan.PlayerClan);
            string marriagePrompt = namedMarriageHero == null
                ? "Accept a compound marriage alliance: one eligible member of your family marries an eligible member of mine, and I will pay you 12000 denars plus 6 horses as the dowry."
                : "Accept a compound marriage alliance where " + namedMarriageHero.Name + " specifically marries an eligible member of my clan, and I will pay you 12000 denars plus 6 horses as the dowry.";
            AddLiveCase(cases, "complex_mcm_marriage_dowry", "Named real marriage with gold and horse dowry", ruler, marriagePrompt, ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_mcm_alliance_player_assets", "Alliance bought with player goods", ruler, "Accept this alliance package: I give your kingdom 15000 denars, 20 tools, 15 hardwood, and 4 horses as the price for a public alliance.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_mcm_alliance_npc_assets", "Alliance bought with NPC goods", ruler, "Accept an alliance package where you give my kingdom 9000 denars, 10 linen, 8 velvet, and 3 horses as the price for our public alliance.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_mcm_nonaggression_player_assets", "Non-aggression bought with player resources", ruler, "Accept this diplomatic package: I give you 7000 denars, 12 charcoal, 10 hides, and 2 horses in exchange for a public non-aggression pact.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_mcm_nonaggression_npc_assets", "Non-aggression bought with NPC resources", ruler, "Accept a diplomatic package where you give me 6000 denars, 15 pottery, 12 fur, and 2 horses in exchange for our non-aggression pact.", ReignWorldActionType.DiplomacyPackage, true);
            AddLiveCase(cases, "complex_mcm_trade_treaty_assets", "Trade treaty with varied goods", ruler, "Accept this diplomatic package: I give you 5000 denars, 15 meat, 12 felt, and 2 horses, and our kingdoms sign a public trade agreement.", ReignWorldActionType.DiplomacyPackage, true);

            if (context.PrisonerHero != null)
            {
                AddLiveCase(cases, "complex_mcm_prisoner_package", "Prisoner release with gold and trade goods", ruler, "Accept this diplomatic package: I pay you 12000 denars and send 25 planks plus 10 tools for the release of prisoner " + context.PrisonerHero.Name + " and a public non-aggression promise.", ReignWorldActionType.DiplomacyPackage, true);
            }
            else
            {
                AddLiveCase(cases, "complex_mcm_fallback_package", "Fallback alliance resource package", ruler, "Accept this diplomatic package: I give you 8000 denars, 20 iron ore, 12 charcoal, and 3 horses as the price for a public alliance.", ReignWorldActionType.DiplomacyPackage, true);
            }
            return cases.Take(LiveDialogueBatchSize).ToList();
        }

        private static List<List<LiveDialogueItemTerm>> BuildBroadTradeGoodBundles()
        {
            List<ItemObject> items = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                .Where(x => x != null && x.IsTradeGood && !string.Equals(x.StringId, "trash", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Name?.ToString() ?? x.StringId)
                .ToList();
            ItemObject horse = FindTradeHorseItem();
            if (horse != null && !items.Any(x => string.Equals(x.StringId, horse.StringId, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(horse);
            }

            if (items.Count == 0 && DefaultItems.Grain != null)
            {
                items.Add(DefaultItems.Grain);
            }

            int bundleCount = 4;
            int bundleSize = Math.Max(1, (int)Math.Ceiling(items.Count / (double)bundleCount));
            List<List<LiveDialogueItemTerm>> bundles = items
                .Select((item, index) => new { item, index })
                .GroupBy(x => x.index / bundleSize)
                .Select(group => group.Select(x => CreateLiveDialogueItemTerm(x.item)).ToList())
                .ToList();
            while (bundles.Count < bundleCount && bundles.Count > 0)
            {
                bundles.Add(bundles[0].Select(x => x.Copy()).ToList());
            }

            return bundles.Take(bundleCount).ToList();
        }

        private static List<List<LiveDialogueItemTerm>> BuildEquipmentBundles()
        {
            string[] categories = { "BodyArmor", "HeadArmor", "OneHandedWeapon", "Shield", "Bow" };
            List<LiveDialogueItemTerm> equipment = new List<LiveDialogueItemTerm>();
            foreach (string category in categories)
            {
                ItemObject item = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                    .Where(x => x != null && string.Equals(x.Type.ToString(), category, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Value)
                    .FirstOrDefault();
                if (item != null)
                {
                    equipment.Add(CreateLiveDialogueItemTerm(item, 1));
                }
            }

            if (equipment.Count == 0)
            {
                return new List<List<LiveDialogueItemTerm>>
                {
                    new List<LiveDialogueItemTerm> { CreateLiveDialogueItemTerm(FindTradeHorseItem(), 1) },
                    new List<LiveDialogueItemTerm> { CreateLiveDialogueItemTerm(FindTradeHorseItem(), 1) },
                    new List<LiveDialogueItemTerm> { CreateLiveDialogueItemTerm(FindTradeHorseItem(), 1) },
                    new List<LiveDialogueItemTerm> { CreateLiveDialogueItemTerm(FindTradeHorseItem(), 1) },
                    new List<LiveDialogueItemTerm> { CreateLiveDialogueItemTerm(FindTradeHorseItem(), 1) }
                };
            }

            List<List<LiveDialogueItemTerm>> bundles = new List<List<LiveDialogueItemTerm>>
            {
                equipment.Take(2).Select(x => x.Copy()).ToList(),
                equipment.Take(2).Select(x => x.Copy()).ToList(),
                equipment.Skip(2).Take(2).Select(x => x.Copy()).ToList(),
                equipment.Skip(2).Take(2).Select(x => x.Copy()).ToList(),
                equipment.Skip(4).Take(1).Select(x => x.Copy()).ToList()
            };
            return bundles.Select(x => x.Count > 0 ? x : equipment.Take(1).Select(y => y.Copy()).ToList()).ToList();
        }

        private static LiveDialogueItemTerm CreateLiveDialogueItemTerm(ItemObject item, int? amountOverride = null)
        {
            if (item == null)
            {
                return new LiveDialogueItemTerm();
            }

            int amount = amountOverride ?? (item.IsMountable ? 2 : item.Value >= 500 ? 2 : 10);
            return new LiveDialogueItemTerm
            {
                ItemId = item.StringId ?? string.Empty,
                Name = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                Amount = Math.Max(1, amount)
            };
        }

        private static string DescribeLiveDialogueItems(IEnumerable<LiveDialogueItemTerm> items)
        {
            return string.Join(", ", (items ?? Enumerable.Empty<LiveDialogueItemTerm>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name) && x.Amount > 0)
                .Select(x => x.Amount + " " + x.Name));
        }

        private static int EstimateLiveDialogueBundlePrice(IEnumerable<LiveDialogueItemTerm> terms, int floor)
        {
            int value = (terms ?? Enumerable.Empty<LiveDialogueItemTerm>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ItemId))
                .Sum(x => Math.Max(1, ReignObjectResolver.FindItem(x.ItemId)?.Value ?? 1) * Math.Max(1, x.Amount));
            return Math.Max(floor, value + Math.Max(500, value / 2));
        }

        private static Hero FindEligibleMarriageHero(Clan npcClan, Clan playerClan)
        {
            if (npcClan == null || playerClan == null || global::TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel == null)
            {
                return null;
            }

            return npcClan.Heroes
                .Where(x => x != null && x.IsAlive)
                .FirstOrDefault(npc => playerClan.Heroes.Any(player => player != null
                    && player.IsAlive
                    && global::TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(player, npc)));
        }

        private static bool LiveDialogueNeedsRepair(LiveDialogueBatchSummary summary)
        {
            if (summary == null || summary.Total <= 0)
            {
                return true;
            }

            int infrastructureFailures = 0;
            foreach (JObject result in (summary.Results ?? new JArray()).OfType<JObject>())
            {
                string id = result.Value<string>("id") ?? string.Empty;
                string failureClass = result.Value<string>("failureClass") ?? string.Empty;
                if (string.Equals(id, "suite_exception", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(failureClass, "llm_or_server_failed", StringComparison.OrdinalIgnoreCase))
                {
                    infrastructureFailures++;
                }
            }

            return summary.Passed == 0 && infrastructureFailures >= Math.Max(3, summary.Total / 2);
        }

        private static string DescribeLedgerState(ReignWorldActionType expectedType, List<ReignWorldActionRecord> ledgerActions)
        {
            if (ledgerActions == null || ledgerActions.Count == 0)
            {
                return "not_imported";
            }

            ReignWorldActionRecord expected = ledgerActions.LastOrDefault(x => x != null && x.Type == expectedType);
            if (expected == null)
            {
                return "wrong_action_imported";
            }

            string state = expected.Status.ToString();
            if (!string.IsNullOrWhiteSpace(expected.FailureReason))
            {
                state += ": " + FriendlyResultText(expected.FailureReason);
            }

            return state;
        }

        private static void AddLiveCase(List<LiveDialogueCase> cases, string id, string label, Hero speaker, string playerText, ReignWorldActionType expectedType, bool priority = false, IEnumerable<LiveDialogueItemTerm> expectedItems = null, bool playerGivesAssets = false, int expectedGoldAmount = 0)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(playerText))
            {
                return;
            }

            cases.Add(new LiveDialogueCase
            {
                Id = id,
                Label = label,
                Speaker = speaker,
                PlayerText = RandomLiveDialoguePrompt(id, playerText),
                ExpectedType = expectedType,
                Priority = priority,
                ExpectedItems = (expectedItems ?? Enumerable.Empty<LiveDialogueItemTerm>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.ItemId) && x.Amount > 0).Select(x => x.Copy()).ToList(),
                PlayerGivesAssets = playerGivesAssets,
                ExpectedGoldAmount = expectedGoldAmount
            });
        }

        private static string RandomLiveDialoguePrompt(string id, string fallback)
        {
            string[] options;
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "gold_gift_plain":
                    options = new[] { fallback, "Give me 1000 denars as a goodwill payment.", "Hand over one thousand denars to me as a gift." };
                    break;
                case "gold_gift_casual":
                    options = new[] { fallback, "Pass me 250 denars from your purse so the ledgers can record it.", "Send me 250 denars right now as a small purse transfer." };
                    break;
                case "grain_from_npc":
                    options = new[] { fallback, "Give me 10 grain from your supplies.", "Transfer ten grain from your party stores to me." };
                    break;
                case "grain_to_npc":
                    options = new[] { fallback, "Take 100 grain from my party inventory.", "Accept one hundred grain out of my supplies." };
                    break;
                case "armor_transfer":
                    options = new[] { fallback, "Hand me the armor you have equipped.", "Transfer the armor you are wearing to me." };
                    break;
                case "trade_buy_armor":
                    options = new[] { fallback, "I will buy your best armor for 5000 denars.", "Sell me your finest armor in exchange for five thousand denars." };
                    break;
                case "complex_trade_sell_grain_horses":
                    options = new[] { fallback, "I will sell you 50 grain and 4 horses for 1800 denars.", "Buy 50 grain and 4 horses from me for 1800 denars." };
                    break;
                case "complex_trade_buy_grain_horses":
                    options = new[] { fallback, "Sell me 40 grain and 3 horses for 2200 denars.", "I will pay you 2200 denars for 40 grain and 3 horses." };
                    break;
                case "complex_prisoner_gold_grain_package":
                    options = new[] { fallback, "Accept a package where I pay 12000 denars and 50 grain for the prisoner's release plus a non-aggression promise.", "I will give you 12000 denars and 50 grain in exchange for releasing the prisoner and promising non-aggression." };
                    break;
                case "complex_marriage_dowry_horses":
                    options = new[] { fallback, "Accept a marriage alliance: your family member marries into my clan, and I pay 12000 denars plus 6 horses as dowry.", "Seal a marriage alliance with my clan in exchange for 12000 denars and 6 horses as dowry." };
                    break;
                case "complex_alliance_gold_grain_horses":
                    options = new[] { fallback, "Accept an alliance package where I give you 15000 denars, 80 grain, and 5 horses.", "Make a public alliance in exchange for 15000 denars, 80 grain, and 5 horses from my stores." };
                    break;
                case "marriage_alliance_plain":
                    options = new[] { fallback, "Seal a marriage alliance between your clan and mine.", "Join our clans through a formal marriage alliance." };
                    break;
                case "trade_ship_offer":
                    options = new[] { fallback, "Sell me all of your ships for 100 denars.", "I will buy every ship you own for one hundred denars." };
                    break;
                case "trade_agreement":
                    options = new[] { fallback, "Sign a trade agreement with my kingdom.", "Let our kingdoms formalize open trade." };
                    break;
                case "non_aggression":
                    options = new[] { fallback, "Sign a non-aggression pact with my kingdom.", "Promise my kingdom a formal non-aggression pact." };
                    break;
                case "alliance":
                    options = new[] { fallback, "Sign an alliance between your realm and mine.", "Make a formal alliance with my kingdom." };
                    break;
                case "make_peace":
                    options = new[] { fallback, "Make peace with my kingdom immediately.", "End our war now and sign peace." };
                    break;
                case "reparations":
                    options = new[] { fallback, "Pay me 5000 denars as reparations and make peace.", "Settle this war by paying me five thousand denars in reparations." };
                    break;
                case "follow_player":
                    options = new[] { fallback, "Follow my party across the campaign map.", "Keep your party following mine on the map." };
                    break;
                case "trade_sell_grain":
                    options = new[] { fallback, "Buy 50 grain from me for 300 denars.", "I will sell you fifty grain for three hundred denars." };
                    break;
                case "full_surrender":
                    options = new[] { fallback, "Accept full capitulation and transfer every town and castle your kingdom owns to my kingdom.", "Surrender completely and hand over all towns and castles your kingdom controls." };
                    break;
                default:
                    return fallback ?? string.Empty;
            }

            return options.OrderBy(_ => Guid.NewGuid()).FirstOrDefault() ?? fallback ?? string.Empty;
        }

        private static Settlement FindOwnedFortification(Kingdom kingdom, Settlement exclude = null)
        {
            if (kingdom == null)
            {
                return null;
            }

            return Settlement.All.FirstOrDefault(x => x != null && x != exclude && x.IsFortification && x.MapFaction == kingdom && !x.IsUnderSiege);
        }

        private static Settlement FindClanOwnedFortification(Clan clan,
            Settlement exclude = null)
        {
            if (clan == null)
            {
                return null;
            }

            return Settlement.All.FirstOrDefault(x => x != null && x != exclude
                && x.IsFortification && x.OwnerClan == clan && !x.IsUnderSiege);
        }

        private static string FriendlyResultText(string text)
        {
            text = (text ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(text) ? "no error text" : text.Length > 240 ? text.Substring(0, 240) + "..." : text;
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
            Run(results, context, "Diplomacy", "DiplomaticPackage", DiplomacyPackage, DiplomaticPackageEffect);
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

                ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
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

        private static ActionSpec FollowOnMap(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null)
            {
                return Skip("Need two active non-player lord parties.");
            }

            return Spec(Action(ReignWorldActionType.RegularFollowOnMap, c, "FollowOnMap", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["targetPartyId"] = c.TargetHero.PartyBelongedTo.StringId
            }));
        }

        private static ActionSpec FollowInScene(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need an actor hero.") : Spec(Action(ReignWorldActionType.RegularFollowInScene, c, "FollowInScene", c.ActorHero));
        }

        private static ActionSpec StopFollowing(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need an actor party.") : Spec(Action(ReignWorldActionType.RegularStopFollowing, c, "StopFollowing", c.ActorHero));
        }

        private static ActionSpec GoToSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularGoToSettlement, c, "GoToSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec PatrolAroundSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularPatrolAroundSettlement, c, "PatrolAroundSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec WaitNearSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularWaitNearSettlement, c, "WaitNearSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec RaidVillage(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileVillage == null)
            {
                return Skip("Need actor party and hostile village.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileVillage.MapFaction, null);
            return Spec(Action(ReignWorldActionType.RegularRaidVillage, c, "RaidVillage", c.ActorHero, null, c.HostileVillage));
        }

        private static ActionSpec BesiegeSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(Action(ReignWorldActionType.RegularBesiegeSettlement, c, "BesiegeSettlement", c.ActorHero, null, c.HostileFortification));
        }

        private static ActionSpec CreateParty(GauntletContext c)
        {
            Hero hero = c.PartylessHero ?? c.ActorHero;
            return hero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularCreateParty, c, "CreateParty", hero, null, c.ArenaSettlement));
        }

        private static ActionSpec ShowTheWay(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularShowTheWay, c, "ShowTheWay", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec AttackParty(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null)
            {
                return Skip("Need two active non-player lord parties.");
            }

            return Spec(Action(ReignWorldActionType.RegularAttackParty, c, "AttackParty", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["targetPartyId"] = c.TargetHero.PartyBelongedTo.StringId
            }));
        }

        private static ActionSpec AttackPlayerParty(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need actor party.") : Spec(Action(ReignWorldActionType.RegularAttackPlayerParty, c, "AttackPlayerParty", c.ActorHero));
        }

        private static ActionSpec SurrenderToPlayer(GauntletContext c)
        {
            Hero surrenderHero = c.SurrenderHero;
            MobileParty surrenderParty = surrenderHero?.PartyBelongedTo;
            if (surrenderParty == null || MobileParty.MainParty == null)
            {
                return Skip("Need a distinct hostile lord party for a disposable player encounter.");
            }

            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.Finish();
            }

            EnsureWar(MobileParty.MainParty.MapFaction, surrenderParty.MapFaction, null);
            StartBattleAction.ApplyStartBattle(MobileParty.MainParty, surrenderParty);
            if (surrenderParty.MapEvent != null && PartyBase.MainParty.MapEvent == surrenderParty.MapEvent)
            {
                PlayerEncounter.Start();
                PlayerEncounter.Init();
            }

            if (PlayerEncounter.Current == null || surrenderParty.MapEvent == null || PartyBase.MainParty.MapEvent != surrenderParty.MapEvent)
            {
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.Finish();
                }
                return Skip("Could not establish the native player encounter required for surrender.");
            }

            return Spec(
                Action(ReignWorldActionType.RegularSurrenderToPlayer, c, "SurrenderToPlayer", surrenderHero),
                () =>
                {
                    if (PlayerEncounter.Current != null)
                    {
                        PlayerEncounter.Finish();
                    }
                });
        }

        private static ActionSpec LeavePlayerAlone(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need actor party.") : Spec(Action(ReignWorldActionType.RegularLeavePlayerAlone, c, "LeavePlayerAlone", c.ActorHero));
        }

        private static ActionSpec KillCharacter(GauntletContext c)
        {
            return c.KillTarget == null || c.ActorHero == null ? Skip("Need a non-critical kill target and actor hero.") : Spec(Action(ReignWorldActionType.RegularKillCharacter, c, "KillCharacter", c.ActorHero, c.KillTarget));
        }

        private static ActionSpec DuelPlayer(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularDuelPlayer, c, "DuelPlayer", c.ActorHero));
        }

        private static ActionSpec GiveGoldToPlayer(GauntletContext c)
        {
            if (c.ActorHero == null)
            {
                return Skip("Need actor hero.");
            }

            EnsureHeroGold(c.ActorHero, 5000);
            return Spec(Action(ReignWorldActionType.RegularGiveGoldToPlayer, c, "GiveGoldToPlayer", c.ActorHero, Hero.MainHero, null, new JObject { ["gold"] = 250 }));
        }

        private static ActionSpec TransferGold(GauntletContext c)
        {
            if (c.ActorHero == null || c.TargetHero == null)
            {
                return Skip("Need actor and target heroes.");
            }

            EnsureHeroGold(c.ActorHero, 5000);
            return Spec(Action(ReignWorldActionType.RegularTransferGold, c, "TransferGold", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["gold"] = 300,
                ["fromHeroStringId"] = c.ActorHero.StringId,
                ["toHeroStringId"] = c.TargetHero.StringId
            }));
        }

        private static ActionSpec TransferItem(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null || DefaultItems.Grain == null)
            {
                return Skip("Need two active parties and grain.");
            }

            c.ActorHero.PartyBelongedTo.ItemRoster.AddToCounts(DefaultItems.Grain, 15);
            return Spec(Action(ReignWorldActionType.RegularTransferItem, c, "TransferItem", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["itemId"] = DefaultItems.Grain.StringId,
                ["amount"] = 5,
                ["fromHeroStringId"] = c.ActorHero.StringId,
                ["toHeroStringId"] = c.TargetHero.StringId
            }));
        }

        private static ActionSpec TransferWorkshop(GauntletContext c)
        {
            if (c.Workshop == null)
            {
                return Skip("Need a workshop with an owner.");
            }

            return Spec(Action(ReignWorldActionType.RegularTransferWorkshop, c, "TransferWorkshop", c.ActorHero, Hero.MainHero, null, new JObject
            {
                ["workshopId"] = ReignObjectResolver.WorkshopId(c.Workshop),
                ["toHeroStringId"] = Hero.MainHero.StringId
            }));
        }

        private static ActionSpec TransferPrisoner(GauntletContext c)
        {
            if (c.PrisonerHero == null)
            {
                return Skip("Need a prisoner hero.");
            }

            if (!c.PrisonerHero.IsPrisoner)
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, c.PrisonerHero);
            }

            return Spec(Action(ReignWorldActionType.RegularTransferPrisoner, c, "TransferPrisoner", c.ActorHero, c.PrisonerHero, null, new JObject
            {
                ["prisonerHeroStringId"] = c.PrisonerHero.StringId,
                ["toHeroStringId"] = Hero.MainHero.StringId
            }));
        }

        private static ActionSpec HirePlayerAsMercenary(GauntletContext c)
        {
            EnsurePlayerClanIndependentForMembershipTest();
            return c.TargetKingdom == null ? Skip("Need target kingdom.") : Spec(Action(ReignWorldActionType.RegularHirePlayerAsMercenary, c, "HirePlayerAsMercenary", null, null, null, new JObject { ["awardMultiplier"] = 35 }, null, null, c.TargetKingdom));
        }

        private static ActionSpec DismissPlayerMercenary(GauntletContext c)
        {
            return Clan.PlayerClan == null || !Clan.PlayerClan.IsUnderMercenaryService ? Skip("Player clan is not in mercenary service.") : Spec(Action(ReignWorldActionType.RegularDismissPlayerMercenary, c, "DismissPlayerMercenary"));
        }

        private static ActionSpec OfferPlayerVassalage(GauntletContext c)
        {
            EnsurePlayerClanIndependentForMembershipTest();
            return c.TargetKingdom == null ? Skip("Need target kingdom.") : Spec(Action(ReignWorldActionType.RegularOfferPlayerVassalage, c, "OfferPlayerVassalage", null, null, null, null, null, null, c.TargetKingdom));
        }

        private static ActionSpec DismissPlayerVassal(GauntletContext c)
        {
            return Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.IsUnderMercenaryService ? Skip("Player clan is not a normal vassal.") : Spec(Action(ReignWorldActionType.RegularDismissPlayerVassal, c, "DismissPlayerVassal"));
        }

        private static ActionSpec JoinClan(GauntletContext c)
        {
            Hero hero = c.ClanTransferHero;
            if (hero == null || c.TargetClan == null)
            {
                return Skip("Need a living non-leader clan member and a different target clan.");
            }

            c.ClanTransferOriginalClan = hero.Clan;
            return Spec(Action(ReignWorldActionType.RegularJoinClan, c, "JoinClan", hero, null, null, null, null, c.TargetClan));
        }

        private static ActionSpec LeaveClan(GauntletContext c)
        {
            Hero hero = c.ClanTransferHero;
            if (hero?.Clan == null)
            {
                return Skip("Need the joined non-leader hero to be in a clan.");
            }

            Clan originalClan = c.ClanTransferOriginalClan;
            return Spec(
                Action(ReignWorldActionType.RegularLeaveClan, c, "LeaveClan", hero),
                () =>
                {
                    if (hero != null && hero.CompanionOf == null && hero.Clan != originalClan)
                    {
                        hero.Clan = originalClan;
                    }
                });
        }

        private static ActionSpec JoinKingdom(GauntletContext c)
        {
            if (c.JoinClan == null || c.TargetKingdom == null)
            {
                return Skip("Need a non-player clan and target kingdom.");
            }

            if (c.JoinClan.Kingdom == c.TargetKingdom)
            {
                return Skip("Selected clan already belongs to target kingdom.");
            }

            return Spec(Action(ReignWorldActionType.RegularJoinKingdom, c, "JoinKingdom", null, null, null, null, c.JoinClan, null, c.TargetKingdom));
        }

        private static ActionSpec LeaveKingdom(GauntletContext c)
        {
            Clan clan = c.LastJoinedClan ?? c.JoinClan;
            return clan == null || clan.Kingdom == null ? Skip("Need a clan currently in a kingdom.") : Spec(Action(ReignWorldActionType.RegularLeaveKingdom, c, "LeaveKingdom", null, null, null, null, clan));
        }

        private static ActionSpec HireMercenaryClan(GauntletContext c)
        {
            Clan clan = c.MercenaryClan ?? c.JoinClan;
            return clan == null || c.TargetKingdom == null ? Skip("Need mercenary/target clan and target kingdom.") : Spec(Action(ReignWorldActionType.RegularHireMercenaryClan, c, "HireMercenaryClan", null, null, null, new JObject { ["awardMultiplier"] = 45 }, null, clan, c.TargetKingdom));
        }

        private static ActionSpec DiplomacyDeclareWar(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDeclareWar, c, "DeclareWar", actor, target);
        }

        private static ActionSpec DiplomacyMakePeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyMakePeace, c, "MakePeace", actor, target);
        }

        private static ActionSpec DiplomacyOfferTributePeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyOfferTributePeace, c, "OfferTributePeace", actor, target, null, new JObject { ["dailyTribute"] = 25, ["durationDays"] = 10 });
        }

        private static ActionSpec DiplomacyRecordPromise(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return DiplomacySpec(ReignWorldActionType.DiplomacyRecordPromise, c, "RecordPromise", actor, target, null, new JObject { ["durationDays"] = 15, ["isPublic"] = false, ["promiseText"] = "gauntlet promise" });
        }

        private static ActionSpec DiplomacyDemandReparationsPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            EnsureLeaderGold(target, 1000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandReparationsPeace, c, "DemandReparationsPeace", actor, target, null, new JObject { ["reparationsGold"] = 100, ["dailyTribute"] = 10, ["durationDays"] = 10 });
        }

        private static ActionSpec DiplomacyDemandSettlementPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            Settlement settlement = FindFortificationOwnedBy(target);
            if (settlement == null)
            {
                return Skip("Need a target-owned town or castle.");
            }

            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandSettlementPeace, c, "DemandSettlementPeace", actor, target, settlement, new JObject
            {
                ["settlementIds"] = new JArray(settlement.StringId),
                ["reparationsGold"] = 50
            });
        }

        private static ActionSpec DiplomacyDemandSurrenderPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            if (target == null || !Settlement.All.Any(x => x != null && x.IsFortification && x.MapFaction == target))
            {
                return Skip("Need a target kingdom with towns or castles.");
            }

            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandSurrenderPeace, c, "DemandSurrenderPeace", actor, target, null, new JObject
            {
                ["allTargetFortifications"] = true,
                ["reparationsGold"] = 50
            });
        }

        private static ActionSpec DiplomacySignTradeAgreement(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignTradeAgreement, c, "SignTradeAgreement", "trade_agreement", 120);
        }

        private static ActionSpec DiplomacySignNonAggressionPact(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignNonAggressionPact, c, "SignNonAggressionPact", "non_aggression_pact", 90);
        }

        private static ActionSpec DiplomacySignAlliance(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignAlliance, c, "SignAlliance", "alliance", 180);
        }

        private static ActionSpec DiplomacySignDefensivePact(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignDefensivePact, c, "SignDefensivePact", "defensive_pact", 120);
        }

        private static ActionSpec DiplomacyBreakTreaty(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            if (actor == null || target == null)
            {
                return Skip("Need two live kingdoms.");
            }

            EnsurePeace(actor, target, null);
            ReignWorldActionRecord seed = WorldAction(ReignWorldActionType.DiplomacyRecordPromise, c, "BreakTreatySeed", actor, target, null, null, null, null, null, null, new JObject { ["kind"] = "trade_agreement" });
            ReignAICampaignBehavior.Instance?.RecordAgreementFromAction(seed, "trade_agreement", 30f, true);
            return DiplomacySpec(ReignWorldActionType.DiplomacyBreakTreaty, c, "BreakTreaty", actor, target, null, new JObject { ["agreementKind"] = "trade_agreement" });
        }

        private static ActionSpec DiplomacyExchangePrisoners(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Hero prisoner = EnsurePrisonerFrom(target, actor, c.ActorHero, c.TargetHero);
            if (prisoner == null)
            {
                return Skip("Need a target-kingdom prisoner held by the actor kingdom.");
            }

            return DiplomacySpec(ReignWorldActionType.DiplomacyExchangePrisoners, c, "ExchangePrisoners", actor, target, null, new JObject { ["maxEachSide"] = 2 }, prisoner);
        }

        private static ActionSpec DiplomacyRansomPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Hero prisoner = EnsurePrisonerFrom(target, actor, c.ActorHero, c.TargetHero);
            if (prisoner == null)
            {
                return Skip("Need a target-kingdom prisoner held by the actor kingdom.");
            }

            EnsureLeaderGold(target, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyRansomPackage, c, "RansomPackage", actor, target, null, new JObject { ["maxPrisoners"] = 1, ["ransomGold"] = 500 }, prisoner);
        }

        private static ActionSpec DiplomacyHostageGuarantee(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyHostageGuarantee, c, "HostageGuarantee", new JObject { ["durationDays"] = 60, ["isPublic"] = false });
        }

        private static ActionSpec DiplomacyWarIndemnity(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureLeaderGold(target, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyWarIndemnity, c, "WarIndemnity", actor, target, null, new JObject { ["indemnityGold"] = 400 });
        }

        private static ActionSpec DiplomacyRecognizeConquest(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyRecognizeConquest, c, "RecognizeConquest", new JObject { ["durationDays"] = 0 });
        }

        private static ActionSpec DiplomacyReturnOccupiedSettlement(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Settlement settlement = FindFortificationOwnedBy(actor);
            if (settlement == null)
            {
                return Skip("Need an actor-owned town or castle to return.");
            }

            EnsurePeace(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyReturnOccupiedSettlement, c, "ReturnOccupiedSettlement", actor, target, settlement);
        }

        private static ActionSpec DiplomacyDemilitarizedBorder(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyDemilitarizedBorder, c, "DemilitarizedBorder", new JObject { ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacySupplyAgreement(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacySupplyAgreement, c, "SupplyAgreement", actor, target, null, new JObject { ["gold"] = 200, ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacyLoanOrSubsidy(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyLoanOrSubsidy, c, "LoanOrSubsidy", actor, target, null, new JObject { ["loanGold"] = 300, ["durationDays"] = 120 });
        }

        private static ActionSpec DiplomacyPayToStayNeutral(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyPayToStayNeutral, c, "PayToStayNeutral", actor, target, null, new JObject { ["gold"] = 250, ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacyPayToJoinWar(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Kingdom enemy = FindOtherLiveKingdom(actor, target);
            if (enemy == null)
            {
                return Skip("Need a third live kingdom.");
            }

            EnsureLeaderGold(actor, 10000);
            EnsurePeace(target, enemy, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyPayToJoinWar, c, "PayToJoinWar", actor, target, null, new JObject
            {
                ["gold"] = 250,
                ["enemyKingdomId"] = enemy.StringId,
                ["durationDays"] = 30
            });
        }

        private static ActionSpec DiplomacyGuaranteeIndependence(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacyGuaranteeIndependence, c, "GuaranteeIndependence", "guarantee_independence", 180);
        }

        private static ActionSpec DiplomacyProtectorateOrVassalage(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacyProtectorateOrVassalage, c, "ProtectorateOrVassalage", "protectorate_or_vassalage", 180);
        }

        private static ActionSpec DiplomacyPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            Settlement settlement = FindFortificationOwnedBy(target);
            if (actor == null || target == null || settlement == null)
            {
                return Skip("Need actor/target kingdoms and a target-owned fortification.");
            }

            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyPackage, c, "DiplomaticPackage", actor, target, settlement, new JObject
            {
                ["gold"] = 100,
                ["fromHeroStringId"] = actor.Leader?.StringId ?? string.Empty,
                ["toHeroStringId"] = target.Leader?.StringId ?? string.Empty,
                ["settlementIds"] = new JArray(settlement.StringId),
                ["packageText"] = "gauntlet compound package"
            });
        }

        private static ActionSpec PeaceAgreementSpec(ReignWorldActionType type, GauntletContext c, string label, string kind, int days)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            return DiplomacySpec(type, c, label, actor, target, null, new JObject { ["durationDays"] = days, ["kind"] = kind });
        }

        private static ActionSpec AgreementSpec(ReignWorldActionType type, GauntletContext c, string label, JObject terms)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return DiplomacySpec(type, c, label, actor, target, null, terms);
        }

        private static ActionSpec StrategyRecruitAndRecover(GauntletContext c)
        {
            MobileParty party = c.ActorHero?.PartyBelongedTo;
            if (party == null || c.ArenaSettlement == null)
            {
                return Skip("Need actor party and recovery settlement.");
            }

            // Prepare a real recovery need without destructively removing troops from the
            // disposable save. The action's requested threshold is one troop above the
            // current roster, so the native executor must issue a recovery movement order.
            int requiredTroops = Math.Min(10000, Math.Max(1, party.MemberRoster.TotalManCount + 1));
            return Spec(WorldAction(ReignWorldActionType.StrategyRecruitAndRecover, c, "RecruitAndRecover", null, null, c.ActorHero, null, c.ArenaSettlement, null, null, null, new JObject(), minimumTroops: requiredTroops));
        }

        private static ActionSpec StrategyFormArmy(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyFormArmy, c, "FormArmy", null, null, c.ActorHero, null, c.HostileFortification, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec StrategyAttackSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileVillage == null)
            {
                return Skip("Need actor party and hostile village.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileVillage.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyAttackSettlement, c, "AttackSettlement", null, null, c.ActorHero, null, c.HostileVillage, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec StrategyCaptureSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyCaptureSettlement, c, "CaptureSettlement", null, null, c.ActorHero, null, c.HostileFortification, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec PoliticsMarriageAlliance(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            Clan target = FindSecondClanInKingdom(kingdom, claimant);
            return PoliticsSpec(ReignWorldActionType.PoliticsMarriageAlliance, c, "MarriageAlliance", kingdom, claimant, target, new JObject { ["durationDays"] = 365 });
        }

        private static ActionSpec PoliticsSupportClaimant(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            return PoliticsSpec(ReignWorldActionType.PoliticsSupportClaimant, c, "SupportClaimant", kingdom, claimant, null, new JObject { ["durationDays"] = 120, ["isPublic"] = false });
        }

        private static ActionSpec PoliticsMediateClanDispute(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            Clan target = FindSecondClanInKingdom(kingdom, claimant);
            return PoliticsSpec(ReignWorldActionType.PoliticsMediateClanDispute, c, "MediateClanDispute", kingdom, claimant, target, new JObject { ["durationDays"] = 30 });
        }

        private static ActionSpec PoliticsEncourageClanDefection(GauntletContext c)
        {
            Kingdom destination = FindActorKingdom();
            Clan clan = Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && !x.IsClanTypeMercenary && x.Kingdom != null && x.Kingdom != destination && x.Leader != null);
            return PoliticsSpec(ReignWorldActionType.PoliticsEncourageClanDefection, c, "EncourageClanDefection", destination, clan, null, new JObject { ["durationDays"] = 120, ["minimumStayDays"] = 10 });
        }

        private static ActionSpec PoliticsExileClan(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan clan = FindClaimantClan(kingdom);
            c.LastExiledClan = clan;
            return PoliticsSpec(ReignWorldActionType.PoliticsExileClan, c, "ExileClan", kingdom, clan, null, new JObject { ["durationDays"] = 180 });
        }

        private static ActionSpec PoliticsRestoreExiledClan(GauntletContext c)
        {
            Kingdom kingdom = FindActorKingdom() ?? FindPoliticsKingdom();
            Clan clan = c.LastExiledClan ?? Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Kingdom == null && x.Leader != null);
            return PoliticsSpec(ReignWorldActionType.PoliticsRestoreExiledClan, c, "RestoreExiledClan", kingdom, clan, null, new JObject { ["durationDays"] = 120, ["minimumStayDays"] = 10 });
        }

        private static ActionSpec PoliticsInstallRulingClan(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            return PoliticsSpec(ReignWorldActionType.PoliticsInstallRulingClan, c, "InstallRulingClan", kingdom, claimant, null, new JObject());
        }

        private static ActionSpec PoliticsStartRulingClanRebellion(GauntletContext c)
        {
            return Skip("NPC rebellion starts are now tested through the saved weekly native-relationship roll; the explicit action is reserved for an in-person or mailed player declaration to their current ruler.");
        }

        private static ActionSpec FailureRetiredTemporaryTruce(GauntletContext c)
        {
            return ExpectedFailure(DiplomacySpec(ReignWorldActionType.DiplomacySignTemporaryTruce, c, "RetiredTemporaryTruce", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()))?.Action);
        }

        private static ActionSpec FailureRetiredTradeEmbargo(GauntletContext c)
        {
            return ExpectedFailure(DiplomacySpec(ReignWorldActionType.DiplomacyTradeEmbargo, c, "RetiredTradeEmbargo", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()))?.Action);
        }

        private static ActionSpec DiplomacyCaravanProtection(GauntletContext c)
        {
            return DiplomacySpec(ReignWorldActionType.DiplomacyCaravanProtectionAgreement, c, "CaravanProtection", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()), null, new JObject { ["durationDays"] = 60 });
        }

        private static ActionSpec FailureIllegalVillageSurrender(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Settlement village = Settlement.All.FirstOrDefault(x => x != null && x.IsVillage && x.MapFaction == target);
            if (village == null)
            {
                return Skip("Need target-owned village.");
            }

            EnsureWar(actor, target, null);
            return ExpectedFailure(WorldAction(ReignWorldActionType.DiplomacyDemandSettlementPeace, c, "IllegalVillageSurrender", actor, target, null, null, village, null, null, null, new JObject { ["settlementIds"] = new JArray(village.StringId) }));
        }

        private static ActionSpec FailureMissingPrisonerPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return ExpectedFailure(WorldAction(ReignWorldActionType.DiplomacyPackage, c, "MissingPrisonerPackage", actor, target, null, null, null, null, null, "missing_prisoner_hero", new JObject { ["prisonerHeroStringId"] = "missing_prisoner_hero" }));
        }

        private static ActionSpec FailureUnaffordableGoldTransfer(GauntletContext c)
        {
            if (c.ActorHero == null)
            {
                return Skip("Need actor hero.");
            }

            return ExpectedFailure(Action(ReignWorldActionType.RegularGiveGoldToPlayer, c, "UnaffordableGoldTransfer", c.ActorHero, Hero.MainHero, null, new JObject { ["gold"] = 2000000000 }));
        }

        private static ActionSpec FailureUnauthorizedSettlementTrade(GauntletContext c)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification && x.OwnerClan?.Leader != null
                && x.OwnerClan != Clan.PlayerClan);
            Hero unrelated = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero && x != settlement?.OwnerClan?.Leader
                && x.Clan != null);
            if (settlement == null || unrelated == null || Hero.MainHero == null)
            {
                return Skip("Need a non-player fief owner and an unrelated living NPC.");
            }

            JObject terms = SettlementTradeTerms(settlement, unrelated);
            ReignWorldActionRecord action = Action(ReignWorldActionType.RegularTradePackage,
                c, "UnauthorizedSettlementTrade", unrelated, Hero.MainHero, settlement,
                terms);
            BindDialogueSettlementTestAuthorization(action, unrelated, terms);
            return ExpectedFailure(action);
        }

        private static ActionSpec FailureTamperedSettlementConsent(GauntletContext c)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification && x.OwnerClan?.Leader != null
                && x.OwnerClan != Clan.PlayerClan);
            Hero owner = settlement?.OwnerClan?.Leader;
            if (settlement == null || owner == null || Hero.MainHero == null)
            {
                return Skip("Need a transferable non-player fief with a living clan leader.");
            }

            JObject terms = SettlementTradeTerms(settlement, owner);
            ReignWorldActionRecord action = Action(ReignWorldActionType.RegularTradePackage,
                c, "TamperedSettlementConsent", owner, Hero.MainHero, settlement, terms);
            BindDialogueSettlementTestAuthorization(action, owner, terms);
            terms["gold"] = 2;
            action.TermsJson = terms.ToString(Formatting.None);
            return ExpectedFailure(action);
        }

        private static JObject SettlementTradeTerms(Settlement settlement, Hero source)
        {
            return new JObject
            {
                ["targetSettlementStringId"] = settlement.StringId,
                ["settlementToHeroStringId"] = Hero.MainHero.StringId,
                ["settlementFromHeroStringId"] = source.StringId,
                ["settlementFromClanStringId"] = settlement.OwnerClan.StringId,
                ["settlementAuthorizingHeroStringId"] = source.StringId,
                ["settlementAuthorityKind"] = "owner_clan_leader",
                ["gold"] = 1,
                ["goldFromHeroStringId"] = Hero.MainHero.StringId,
                ["goldToHeroStringId"] = source.StringId,
                ["bypassFairness"] = true
            };
        }

        private static void BindDialogueSettlementTestAuthorization(
            ReignWorldActionRecord action, Hero acceptedBy, JObject terms)
        {
            action.Source = "test_lab_action_gate";
            action.AuthorizationMode = "dialogue_acceptance";
            action.AcceptedByHeroStringId = acceptedBy.StringId;
            action.NegotiationId = "gauntlet-settlement-authority-" + Guid.NewGuid().ToString("N");
            action.NegotiatedCommand = "trade_package";
            action.RequiresAcceptance = false;
            action.TermsJson = terms.ToString(Formatting.None);
            action.TermsHash = ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand,
                action.TermsJson);
        }

        private static ActionSpec FailureUnknownAction(GauntletContext c)
        {
            return ExpectedFailure(WorldAction(ReignWorldActionType.Unknown, c, "UnknownAction", null, null, c.ActorHero, null, null, null, null, null, new JObject()));
        }

        private static ActionSpec DiplomacySpec(ReignWorldActionType type, GauntletContext c, string label, Kingdom actor, Kingdom target, Settlement settlement = null, JObject terms = null, Hero targetHero = null)
        {
            if (actor == null || target == null)
            {
                return Skip("Need two live kingdoms.");
            }

            return Spec(WorldAction(type, c, label, actor, target, null, targetHero, settlement, null, null, null, terms ?? new JObject()));
        }

        private static ActionSpec PoliticsSpec(ReignWorldActionType type, GauntletContext c, string label, Kingdom kingdom, Clan actorClan, Clan targetClan, JObject terms)
        {
            if (kingdom == null || actorClan == null)
            {
                return Skip("Need kingdom and actor clan.");
            }

            if ((type == ReignWorldActionType.PoliticsMarriageAlliance || type == ReignWorldActionType.PoliticsMediateClanDispute) && targetClan == null)
            {
                return Skip("Need target clan.");
            }

            return Spec(WorldAction(type, c, label, kingdom, null, null, null, null, actorClan, targetClan, null, terms ?? new JObject()));
        }

        private static ActionSpec ExpectedFailure(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return Skip("No invalid action could be constructed.");
            }

            return new ActionSpec { Action = action, ExpectValidationFailure = true };
        }

        private static ReignWorldActionRecord WorldAction(
            ReignWorldActionType type,
            GauntletContext context,
            string label,
            Kingdom actorKingdom,
            Kingdom targetKingdom,
            Hero actor,
            Hero target,
            Settlement settlement,
            Clan actorClan,
            Clan targetClan,
            string targetHeroId,
            JObject terms,
            int minimumTroops = 0)
        {
            return new ReignWorldActionRecord
            {
                Type = type,
                Source = "gauntlet:" + context.CorrelationId,
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                TargetHeroStringId = target?.StringId ?? targetHeroId ?? string.Empty,
                ActorClanStringId = actorClan?.StringId ?? actor?.Clan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? target?.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = actorKingdom?.StringId ?? actor?.Clan?.Kingdom?.StringId ?? actorClan?.Kingdom?.StringId ?? string.Empty,
                TargetKingdomStringId = targetKingdom?.StringId ?? target?.Clan?.Kingdom?.StringId ?? targetClan?.Kingdom?.StringId ?? string.Empty,
                TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = "ReignBeta Action Gauntlet: " + label,
                RequiresAcceptance = false,
                MaxAttempts = 1,
                MinimumTroops = minimumTroops
            };
        }

        private static Kingdom FindActorKingdom()
        {
            return Clan.PlayerClan?.Kingdom
                ?? Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.Leader != null);
        }

        private static Kingdom FindOtherLiveKingdom(Kingdom actor, Kingdom exclude = null)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.Leader != null && x != actor && x != exclude);
        }

        private static Kingdom FindKingdomWithFortificationNot(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null
                && !x.IsEliminated
                && x.Leader != null
                && x != actor
                && Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x));
        }

        private static Settlement FindFortificationOwnedBy(Kingdom kingdom)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsFortification && x.MapFaction == kingdom && !x.IsUnderSiege);
        }

        private static void EnsurePeace(Kingdom left, Kingdom right, List<string> notes)
        {
            if (left == null || right == null || left == right || !left.IsAtWarWith(right))
            {
                return;
            }

            MakePeaceAction.ApplyByKingdomDecision(left, right, 0, 0);
            notes?.Add("peace made between " + left.InformalName + " and " + right.InformalName);
        }

        private static void EnsureLeaderGold(Kingdom kingdom, int minimumGold)
        {
            if (kingdom?.Leader != null)
            {
                EnsureHeroGold(kingdom.Leader, minimumGold);
            }
        }

        private static Hero EnsurePrisonerFrom(Kingdom prisonerKingdom, Kingdom holderKingdom, params Hero[] exclude)
        {
            if (prisonerKingdom == null || holderKingdom == null)
            {
                return null;
            }

            Hero prisoner = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !exclude.Contains(x)
                && !x.IsDead
                && x.Clan?.Kingdom == prisonerKingdom
                && x != prisonerKingdom.Leader)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && !exclude.Contains(x)
                    && !x.IsDead
                    && x.IsLord
                    && x != prisonerKingdom.Leader);

            if (prisoner == null)
            {
                return null;
            }

            if (!prisoner.IsPrisoner)
            {
                PartyBase holder = holderKingdom == Clan.PlayerClan?.Kingdom && PartyBase.MainParty != null
                    ? PartyBase.MainParty
                    : holderKingdom.Leader?.PartyBelongedTo?.Party;
                if (holder == null)
                {
                    return null;
                }

                TakePrisonerAction.Apply(holder, prisoner);
            }

            return prisoner;
        }

        private static Kingdom FindPoliticsKingdom()
        {
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.RulingClan != null && FindClaimantClan(x) != null)
                ?? FindActorKingdom();
        }

        private static Clan FindClaimantClan(Kingdom kingdom)
        {
            return Clan.All.FirstOrDefault(x => x != null
                && x.Kingdom == kingdom
                && x != kingdom?.RulingClan
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Leader != null);
        }

        private static Clan FindSecondClanInKingdom(Kingdom kingdom, Clan except)
        {
            return Clan.All.FirstOrDefault(x => x != null
                && x.Kingdom == kingdom
                && x != except
                && x != kingdom?.RulingClan
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Leader != null);
        }

        private static void EnsurePlayerKingdomState(List<string> notes)
        {
            Hero player = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            if (player == null || playerClan == null || MobileParty.MainParty == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                return;
            }

            EnsureHeroGold(player, 1000000);
            notes?.Add("player kingdom gold ensured");

            CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
            if (cataphract != null && MobileParty.MainParty.MemberRoster.TotalManCount < 150)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 150);
                notes?.Add("player troops ensured");
            }

            if (DefaultItems.Grain != null && MobileParty.MainParty.ItemRoster.GetItemNumber(DefaultItems.Grain) < 300)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                notes?.Add("player grain ensured");
            }

            int requiredRenown = TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(4);
            if (playerClan.Renown < requiredRenown)
            {
                GainRenownAction.Apply(player, requiredRenown - playerClan.Renown + 5f, true);
                notes?.Add("player clan tier ensured");
            }

            Settlement zeonica = FindArenaSettlement();
            if (zeonica != null && zeonica.IsTown && zeonica.OwnerClan != playerClan)
            {
                ChangeOwnerOfSettlementAction.ApplyByGift(zeonica, player);
                notes?.Add("arena town granted to player");
            }

            if (playerClan.Leader != player)
            {
                playerClan.SetLeader(player);
                notes?.Add("player set as clan leader");
            }

            if (playerClan.Kingdom == null)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.KingdomManager.CreateKingdom(playerClan.Name, playerClan.InformalName, playerClan.Culture, playerClan);
                notes?.Add("player kingdom created");
            }

            if (playerClan.Kingdom != null && playerClan.Kingdom.RulingClan != playerClan)
            {
                ChangeRulingClanAction.Apply(playerClan.Kingdom, playerClan);
                notes?.Add("player clan made ruling clan");
            }
        }

        private static void EnsurePlayerClanIndependentForMembershipTest()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || playerClan.Kingdom == null)
            {
                return;
            }

            if (playerClan.IsUnderMercenaryService)
            {
                ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(playerClan, true);
                return;
            }

            ChangeKingdomAction.ApplyByLeaveKingdom(playerClan, true);
        }

        private static string ExpectedValidationFailure(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return result != null && result.ResultCode == "validation_failed" ? string.Empty : "Expected terminal validation failure.";
        }

        private static string WarStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "War action failed.";
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return actor != null && target != null && actor.IsAtWarWith(target) ? string.Empty : "Expected kingdoms to be at war.";
        }

        private static string PeaceMade(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Peace action failed.";
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return actor != null && target != null && !actor.IsAtWarWith(target) ? string.Empty : "Expected kingdoms to be at peace.";
        }

        private static string TreatyRecorded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Treaty action failed.";
            }

            return HasEffect(result, "treaty_recorded") || after.Value<int>("agreementCount") > before.Value<int>("agreementCount")
                ? string.Empty
                : "Expected a treaty/agreement record.";
        }

        private static string TreatyBroken(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Break treaty action failed.";
            }

            return HasEffect(result, "treaty_broken") ? string.Empty : "Expected treaty_broken effect.";
        }

        private static string PrisonerOrTreatyEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Prisoner diplomacy action failed.";
            }

            return HasEffect(result, "prisoners_released") || HasEffect(result, "treaty_recorded") ? string.Empty : "Expected prisoner release or treaty effect.";
        }

        private static string PrisonerOrGoldEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Ransom action failed.";
            }

            return HasEffect(result, "prisoners_released") || HasEffect(result, "gold_transfer") ? string.Empty : "Expected prisoner or gold effect.";
        }

        private static string GoldOrTreatyEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Gold/treaty action failed.";
            }

            return HasEffect(result, "gold_transfer") || HasEffect(result, "treaty_recorded") ? string.Empty : "Expected gold transfer or treaty effect.";
        }

        private static string SettlementTransferredToActor(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Settlement transfer action failed.";
            }

            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            return settlement != null && actor != null && settlement.MapFaction == actor ? string.Empty : "Expected target settlement to transfer to actor kingdom.";
        }

        private static string SettlementTransferredToTarget(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Return settlement action failed.";
            }

            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return settlement != null && target != null && settlement.MapFaction == target ? string.Empty : "Expected target settlement to transfer to target kingdom.";
        }

        private static string FullSurrenderTransferredLand(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Full surrender failed.";
            }

            return HasEffect(result, "settlement_transferred") || after.Value<int>("targetKingdomFortifications") < before.Value<int>("targetKingdomFortifications")
                ? string.Empty
                : "Expected at least one surrendered settlement.";
        }

        private static string WarStartedAgainstThird(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Pay-to-join-war action failed.";
            }

            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Kingdom enemy = ReignObjectResolver.FindKingdom(ReadTerm(action.TermsJson, "enemyKingdomId"));
            return target != null && enemy != null && target.IsAtWarWith(enemy) ? string.Empty : "Expected target kingdom to join war against third kingdom.";
        }

        private static string DiplomaticPackageEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Diplomatic package failed.";
            }

            if (!HasEffect(result, "diplomatic_package"))
            {
                return "Expected diplomatic_package effect.";
            }

            return ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string reason)
                ? string.Empty
                : reason;
        }

        private static string VerifiedTransactionCompleted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string reason)
                ? string.Empty
                : reason;
        }

        private static string CapturePlanProgressed(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Capture plan failed.";
            }

            return action.PlanStage != ReignStrategicPlanStage.None && action.PlanStage != ReignStrategicPlanStage.Failed
                ? string.Empty
                : "Expected capture plan stage to advance.";
        }

        private static string ClanKingdomChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Clan kingdom action failed.";
            }

            return before.Value<string>("actionActorClanKingdom") != after.Value<string>("actionActorClanKingdom") || HasEffect(result, "clan_defected")
                ? string.Empty
                : "Expected clan kingdom to change.";
        }

        private static string ClanExiled(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Exile clan action failed.";
            }

            return string.IsNullOrWhiteSpace(after.Value<string>("actionActorClanKingdom")) ? string.Empty : "Expected clan to have no kingdom after exile.";
        }

        private static string ClanRestored(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Restore clan action failed.";
            }

            return after.Value<string>("actionActorClanKingdom") == action.ActorKingdomStringId ? string.Empty : "Expected clan to be restored to actor kingdom.";
        }

        private static string RulingClanChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Ruling clan action failed.";
            }

            return after.Value<string>("actorKingdomRulingClan") == action.ActorClanStringId || HasEffect(result, "ruling_clan_changed")
                ? string.Empty
                : "Expected actor clan to become ruling clan.";
        }

        private static string RebellionStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Rebellion action failed.";
            }

            return HasEffect(result, "rebellion_started") || before.Value<string>("actionActorClanKingdom") != after.Value<string>("actionActorClanKingdom")
                ? string.Empty
                : "Expected rebellion to start or claimant clan kingdom to change.";
        }

        private static bool HasEffect(ReignActionResult result, string effectType)
        {
            return result?.Effects != null && result.Effects.Any(x => x != null
                && x.TryGetValue("effectType", out string value)
                && string.Equals(value, effectType, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadTerm(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                return obj[key]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SuccessAny(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return result != null && result.Success ? string.Empty : result?.Message ?? "No execution result.";
        }

        private static string SuccessOrProgress(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "No execution result.";
            }

            return result.Outcome == "progress" || result.Completed ? string.Empty : "Expected progress or completion, got " + result.Outcome + ".";
        }

        private static Assertion MovementOrder(string expectedDetail)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Movement action produced no execution result.";
                }

                bool exactEffect = result.Effects != null && result.Effects.Any(effect => effect != null
                    && effect.TryGetValue("effectType", out string effectType)
                    && string.Equals(effectType, "movement_order", StringComparison.OrdinalIgnoreCase)
                    && effect.TryGetValue("detail", out string detail)
                    && string.Equals(detail, expectedDetail, StringComparison.OrdinalIgnoreCase));
                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, "movement_order_changed", StringComparison.OrdinalIgnoreCase));
                return exactEffect && changed ? string.Empty : "Expected native movement effect " + expectedDetail + " and movement_order_changed.";
            };
        }

        private static Assertion NativeChange(string expectedChangeType)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Native action produced no execution result.";
                }

                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, expectedChangeType, StringComparison.OrdinalIgnoreCase));
                return changed ? string.Empty : "Expected verified native change " + expectedChangeType + ", but only an intent/pending result was produced.";
            };
        }

        private static Assertion NativeChangeOrEffect(string expectedType)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Native action produced no execution result.";
                }

                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, expectedType, StringComparison.OrdinalIgnoreCase));
                bool effect = result.Effects != null && result.Effects.Any(row => row != null
                    && row.TryGetValue("effectType", out string effectType)
                    && string.Equals(effectType, expectedType, StringComparison.OrdinalIgnoreCase));
                return changed || effect ? string.Empty : "Expected verified native change/effect " + expectedType + ".";
            };
        }

        private static string PendingHook(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "No execution result.";
            }

            return result.ResultCode != null && result.ResultCode.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0
                ? string.Empty
                : "Expected a pending hook diagnostic result.";
        }

        private static string GoldChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Gold action failed.";
            }

            return (int)before["playerGold"] != (int)after["playerGold"] || (int)before["actorGold"] != (int)after["actorGold"]
                ? string.Empty
                : "Expected hero gold to change.";
        }

        private static string ItemChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Item action failed.";
            }

            return (int)after["targetGrain"] > (int)before["targetGrain"] ? string.Empty : "Expected target party grain to increase.";
        }

        private static string WorkshopChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Workshop action failed.";
            }

            return string.Equals((string)after["workshopOwner"], Hero.MainHero.StringId, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "Expected workshop owner to become player.";
        }

        private static string PrisonerReleased(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Prisoner action failed.";
            }

            return after.Value<bool>("prisonerIsPrisoner") ? "Expected prisoner to be released." : string.Empty;
        }

        private static string HeroKilled(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Kill action failed.";
            }

            return after.Value<bool>("targetDead") ? string.Empty : "Expected target hero to be dead.";
        }

        private static string CreatePartyAssert(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Create party failed.";
            }

            return after.Value<bool>("actorHasParty") ? string.Empty : "Expected actor to have a party or an explicit no-op result.";
        }

        private static string PlayerMercenaryStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Mercenary start failed.";
            }

            return Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to enter mercenary service.";
        }

        private static string PlayerMercenaryEnded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Mercenary dismissal failed.";
            }

            return Clan.PlayerClan != null && !Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to leave mercenary service.";
        }

        private static string PlayerVassalStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Vassalage start failed.";
            }

            return Clan.PlayerClan?.Kingdom != null && !Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to become a vassal.";
        }

        private static string PlayerVassalEnded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Vassalage dismissal failed.";
            }

            return Clan.PlayerClan?.Kingdom == null ? string.Empty : "Expected player clan to leave kingdom.";
        }

        private static string ClanJoinedKingdom(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Join kingdom failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            if (clan != null && kingdom != null && clan.Kingdom == kingdom)
            {
                c.LastJoinedClan = clan;
                return string.Empty;
            }

            return "Expected clan to join target kingdom.";
        }

        private static string ClanLeftKingdom(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Leave kingdom failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            return clan != null && clan.Kingdom == null ? string.Empty : "Expected clan to leave kingdom.";
        }

        private static string ClanMercenaryStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Hire mercenary clan failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.TargetClanStringId) ?? ReignObjectResolver.FindClan(action.ActorClanStringId);
            return clan != null && clan.Kingdom != null ? string.Empty : "Expected clan to enter kingdom service.";
        }

        private static GauntletContext BuildContext(string correlationId)
        {
            Settlement arena = FindArenaSettlement();
            Hero actor = FindActiveLord(null, null);
            Hero target = FindActiveLord(actor, null);
            Hero partyless = FindPartylessLord();
            Hero clanTransfer = FindClanTransferHero(actor, target, partyless);
            Settlement hostileFort = FindHostileFortification(actor) ?? FindAnyFortificationNotOwnedBy(actor?.PartyBelongedTo?.MapFaction);
            Settlement hostileVillage = FindHostileVillage(actor) ?? FindAnyVillageNotOwnedBy(actor?.PartyBelongedTo?.MapFaction);
            return new GauntletContext
            {
                CorrelationId = correlationId,
                ArenaSettlement = arena,
                ActorHero = actor,
                TargetHero = target,
                PartylessHero = partyless,
                SurrenderHero = FindSurrenderHero(actor, target),
                ClanTransferHero = clanTransfer,
                PrisonerHero = FindPrisonerCandidate(actor, target),
                KillTarget = FindKillTarget(actor, target),
                HostileFortification = hostileFort,
                HostileVillage = hostileVillage,
                Workshop = FindWorkshop(),
                TargetKingdom = FindTargetKingdom(),
                TargetClan = FindTargetClan(clanTransfer?.Clan ?? actor?.Clan),
                JoinClan = FindJoinClan(),
                MercenaryClan = FindMercenaryClan()
            };
        }

        private static ReignWorldActionRecord Action(
            ReignWorldActionType type,
            GauntletContext context,
            string label,
            Hero actor = null,
            Hero target = null,
            Settlement settlement = null,
            JObject terms = null,
            Clan actorClan = null,
            Clan targetClan = null,
            Kingdom targetKingdom = null)
        {
            return new ReignWorldActionRecord
            {
                Type = type,
                Source = "gauntlet:" + context.CorrelationId,
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                TargetHeroStringId = target?.StringId ?? string.Empty,
                ActorClanStringId = actorClan?.StringId ?? actor?.Clan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? target?.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = actor?.Clan?.Kingdom?.StringId ?? actorClan?.Kingdom?.StringId ?? string.Empty,
                TargetKingdomStringId = targetKingdom?.StringId ?? target?.Clan?.Kingdom?.StringId ?? string.Empty,
                TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = "ReignBeta Action Gauntlet: " + label,
                RequiresAcceptance = false,
                MaxAttempts = 1,
                MinimumTroops = 0
            };
        }

        private static Hero ResolveSnapshotHero(string heroId, params Hero[] candidates)
        {
            if (!string.IsNullOrWhiteSpace(heroId))
            {
                Hero found = ReignObjectResolver.FindHero(heroId);
                if (found != null)
                {
                    return found;
                }

                foreach (Hero candidate in candidates)
                {
                    if (candidate != null && string.Equals(candidate.StringId, heroId, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            foreach (Hero candidate in candidates)
            {
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static JObject Snapshot(GauntletContext c, ReignWorldActionRecord action)
        {
            Hero actor = ResolveSnapshotHero(action.ActorHeroStringId, c.ActorHero, Hero.MainHero, c.TargetHero, c.KillTarget, c.PrisonerHero);
            Hero target = ResolveSnapshotHero(action.TargetHeroStringId, c.TargetHero, c.KillTarget, c.PrisonerHero, Hero.MainHero);
            MobileParty actorParty = actor?.PartyBelongedTo;
            MobileParty targetParty = target?.PartyBelongedTo;
            Workshop workshop = c.Workshop;
            Hero prisoner = c.PrisonerHero;
            Clan actorActionClan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            Clan targetActionClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
            Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Settlement targetSettlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            return new JObject
            {
                ["actorHeroId"] = actor?.StringId ?? string.Empty,
                ["targetHeroId"] = target?.StringId ?? string.Empty,
                ["targetHeroName"] = target?.Name?.ToString() ?? string.Empty,
                ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                ["actorGold"] = actor?.Gold ?? 0,
                ["targetGold"] = target?.Gold ?? 0,
                ["actorPartyId"] = actorParty?.StringId ?? string.Empty,
                ["actorHasParty"] = actorParty != null,
                ["actorPosition"] = actorParty == null ? string.Empty : actorParty.GetPosition2D.ToString(),
                ["targetPartyId"] = targetParty?.StringId ?? string.Empty,
                ["actorGrain"] = actorParty == null || DefaultItems.Grain == null ? 0 : actorParty.ItemRoster.GetItemNumber(DefaultItems.Grain),
                ["targetGrain"] = targetParty == null || DefaultItems.Grain == null ? 0 : targetParty.ItemRoster.GetItemNumber(DefaultItems.Grain),
                ["workshopOwner"] = workshop?.Owner?.StringId ?? string.Empty,
                ["prisonerHero"] = prisoner?.StringId ?? string.Empty,
                ["prisonerIsPrisoner"] = prisoner != null && prisoner.IsPrisoner,
                ["targetDead"] = target != null && target.IsDead,
                ["playerClanKingdom"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerClanMercenary"] = Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService,
                ["actorClanKingdom"] = actor?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["actionActorClanId"] = actorActionClan?.StringId ?? string.Empty,
                ["actionActorClanKingdom"] = actorActionClan?.Kingdom?.StringId ?? string.Empty,
                ["actionActorClanMercenary"] = actorActionClan != null && actorActionClan.IsUnderMercenaryService,
                ["actionTargetClanId"] = targetActionClan?.StringId ?? string.Empty,
                ["actionTargetClanKingdom"] = targetActionClan?.Kingdom?.StringId ?? string.Empty,
                ["actionTargetClanMercenary"] = targetActionClan != null && targetActionClan.IsUnderMercenaryService,
                ["actorKingdomId"] = actorKingdom?.StringId ?? string.Empty,
                ["targetKingdomId"] = targetKingdom?.StringId ?? string.Empty,
                ["actorKingdomAtWarWithTarget"] = actorKingdom != null && targetKingdom != null && actorKingdom.IsAtWarWith(targetKingdom),
                ["actorKingdomRulingClan"] = actorKingdom?.RulingClan?.StringId ?? string.Empty,
                ["targetKingdomRulingClan"] = targetKingdom?.RulingClan?.StringId ?? string.Empty,
                ["actorKingdomEliminated"] = actorKingdom != null && actorKingdom.IsEliminated,
                ["targetKingdomEliminated"] = targetKingdom != null && targetKingdom.IsEliminated,
                ["actorLeaderGold"] = actorKingdom?.Leader?.Gold ?? 0,
                ["targetLeaderGold"] = targetKingdom?.Leader?.Gold ?? 0,
                ["targetSettlementId"] = targetSettlement?.StringId ?? string.Empty,
                ["targetSettlementOwnerKingdom"] = targetSettlement?.MapFaction?.StringId ?? string.Empty,
                ["targetKingdomFortifications"] = targetKingdom == null ? 0 : Settlement.All.Count(x => x != null && x.IsFortification && x.MapFaction == targetKingdom),
                ["agreementCount"] = CountActiveAgreements(action)
            };
        }

        private static int CountActiveAgreements(ReignWorldActionRecord action)
        {
            IReadOnlyList<ReignDiplomaticAgreementRecord> agreements = ReignAICampaignBehavior.Instance?.Agreements;
            if (agreements == null || action == null)
            {
                return 0;
            }

            return agreements.Count(x => x != null
                && x.IsActive
                && SamePair(x.ActorKingdomStringId, x.TargetKingdomStringId, action.ActorKingdomStringId, action.TargetKingdomStringId)
                && SameOptional(x.ActorClanStringId, action.ActorClanStringId)
                && SameOptional(x.TargetClanStringId, action.TargetClanStringId)
                && SameOptional(x.TargetHeroStringId, action.TargetHeroStringId)
                && SameOptional(x.TargetSettlementStringId, action.TargetSettlementStringId));
        }

        private static bool SamePair(string a1, string a2, string b1, string b2)
        {
            return (string.Equals(a1 ?? string.Empty, b1 ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b2 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(a1 ?? string.Empty, b2 ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b1 ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameOptional(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePlayerResources(List<string> notes)
        {
            Hero player = Hero.MainHero;
            if (player == null || MobileParty.MainParty == null)
            {
                return;
            }

            EnsureHeroGold(player, 250000);
            notes?.Add("player gold ensured");

            CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
            if (cataphract != null && MobileParty.MainParty.MemberRoster.TotalManCount < 150)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 150);
                notes?.Add("player troops ensured");
            }

            if (DefaultItems.Grain != null && MobileParty.MainParty.ItemRoster.GetItemNumber(DefaultItems.Grain) < 300)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                notes?.Add("player grain ensured");
            }

            ItemObject horse = FindTradeHorseItem();
            if (horse != null && MobileParty.MainParty.ItemRoster.GetItemNumber(horse) < 20)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(horse, 20);
                notes?.Add("player horses ensured");
            }
        }

        private static void EnsureActorResources(Hero hero, List<string> notes)
        {
            if (hero == null)
            {
                return;
            }

            EnsureHeroGold(hero, 50000);
            MobileParty party = hero.PartyBelongedTo;
            if (party != null)
            {
                CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
                if (cataphract != null && party.MemberRoster.TotalManCount < 40)
                {
                    party.MemberRoster.AddToCounts(cataphract, 40);
                }

                if (DefaultItems.Grain != null && party.ItemRoster.GetItemNumber(DefaultItems.Grain) < 50)
                {
                    party.ItemRoster.AddToCounts(DefaultItems.Grain, 50);
                }

                ItemObject horse = FindTradeHorseItem();
                if (horse != null && party.ItemRoster.GetItemNumber(horse) < 10)
                {
                    party.ItemRoster.AddToCounts(horse, 10);
                }
            }

            notes?.Add(hero.Name + " resources ensured");
        }

        private static ItemObject FindTradeHorseItem()
        {
            return MBObjectManager.Instance.GetObject<ItemObject>("imperial_charger")
                ?? MBObjectManager.Instance.GetObject<ItemObject>("sumpter_horse")
                ?? MBObjectManager.Instance.GetObjectTypeList<ItemObject>().FirstOrDefault(x => x != null && x.HorseComponent != null);
        }

        private static void EnsureHeroGold(Hero hero, int minimumGold)
        {
            if (hero != null && hero.Gold < minimumGold)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, hero, minimumGold - hero.Gold, false);
            }
        }

        private static void EnsureWar(IFaction left, IFaction right, List<string> notes)
        {
            if (left == null || right == null || left == right || left.IsAtWarWith(right))
            {
                return;
            }

            DeclareWarAction.ApplyByDefault(left, right);
            notes?.Add("war declared between " + left.Name + " and " + right.Name);
        }

        private static void TeleportParty(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null)
            {
                return;
            }

            party.SetPositionAfterMapChange(settlement.GatePosition);
            party.SetMoveModeHold();
        }

        private static Settlement FindArenaSettlement()
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsTown && string.Equals(x.StringId, "town_EW2", StringComparison.OrdinalIgnoreCase))
                ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown && x.Name != null && x.Name.ToString().IndexOf("Zeonica", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown);
        }

        private static Hero FindActiveLord(Hero except, IFaction faction)
        {
            IEnumerable<Hero> heroes = Hero.AllAliveHeroes.Where(x => x != null
                && x != Hero.MainHero
                && x != except
                && x.IsLord
                && !x.IsPrisoner
                && x.PartyBelongedTo != null
                && !x.PartyBelongedTo.IsMainParty
                && x.PartyBelongedTo.IsActive
                && x.PartyBelongedTo.MapEvent == null
                && x.PartyBelongedTo.BesiegedSettlement == null);

            if (faction != null)
            {
                heroes = heroes.Where(x => x.PartyBelongedTo.MapFaction == faction);
            }

            return heroes.OrderByDescending(SafeActiveLordScore).FirstOrDefault();
        }

        private static float SafeActiveLordScore(Hero hero)
        {
            try
            {
                MobileParty party = hero?.PartyBelongedTo;
                if (party == null)
                {
                    return 0f;
                }

                return party.MemberRoster?.TotalManCount ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static Hero FindPartylessLord()
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x.IsLord
                && !x.IsPrisoner
                && x.PartyBelongedTo == null
                && x.Clan != null
                && !x.Clan.IsEliminated);
        }

        private static Hero FindSurrenderHero(params Hero[] excluded)
        {
            IFaction playerFaction = MobileParty.MainParty?.MapFaction;
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !excluded.Contains(x)
                && !x.IsPrisoner
                && x.PartyBelongedTo != null
                && x.PartyBelongedTo.IsActive
                && !x.PartyBelongedTo.IsMainParty
                && x.PartyBelongedTo.LeaderHero == x
                && x.PartyBelongedTo.MapEvent == null
                && x.PartyBelongedTo.BesiegedSettlement == null
                && playerFaction != null
                && x.PartyBelongedTo.MapFaction != null
                && x.PartyBelongedTo.MapFaction != playerFaction
                && playerFaction.IsAtWarWith(x.PartyBelongedTo.MapFaction));
        }

        private static Hero FindClanTransferHero(params Hero[] excluded)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !excluded.Contains(x)
                && !x.IsPrisoner
                && x.CompanionOf == null
                && x.Clan != null
                && !x.Clan.IsEliminated
                && x.Clan.Leader != x
                && x.PartyBelongedTo == null
                && x.GovernorOf == null);
        }

        private static Hero FindPrisonerCandidate(Hero actor, Hero target)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x != actor
                && x != target
                && !x.IsDead
                && !x.IsPrisoner
                && x.IsLord
                && x.Clan?.Leader != x)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && x != actor
                    && x != target
                    && !x.IsDead
                    && !x.IsPrisoner
                    && x.IsWanderer
                    && !x.IsPlayerCompanion);
        }

        private static Hero FindKillTarget(Hero actor, Hero target)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x != actor
                && x != target
                && !x.IsDead
                && !x.IsPrisoner
                && x.IsWanderer
                && !x.IsPlayerCompanion)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && x != actor
                    && x != target
                    && !x.IsDead
                    && !x.IsPrisoner
                    && x.IsLord
                    && x.Clan?.Leader != x
                    && x.PartyBelongedTo == null);
        }

        private static Settlement FindHostileFortification(Hero actor)
        {
            IFaction faction = actor?.PartyBelongedTo?.MapFaction;
            return Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification
                && x.MapFaction != null
                && faction != null
                && faction.IsAtWarWith(x.MapFaction)
                && !x.IsUnderSiege);
        }

        private static Settlement FindHostileVillage(Hero actor)
        {
            IFaction faction = actor?.PartyBelongedTo?.MapFaction;
            return Settlement.All.FirstOrDefault(x => x != null
                && x.IsVillage
                && x.MapFaction != null
                && faction != null
                && faction.IsAtWarWith(x.MapFaction));
        }

        private static Settlement FindAnyFortificationNotOwnedBy(IFaction faction)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsFortification && !x.IsUnderSiege && x.MapFaction != null && x.MapFaction != faction);
        }

        private static Settlement FindAnyVillageNotOwnedBy(IFaction faction)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsVillage && x.MapFaction != null && x.MapFaction != faction);
        }

        private static Workshop FindWorkshop()
        {
            foreach (Town town in Town.AllTowns.Where(x => x != null))
            {
                Workshop workshop = town.Workshops?.FirstOrDefault(x => x != null && x.Owner != null && x.WorkshopType != null);
                if (workshop != null)
                {
                    return workshop;
                }
            }

            return null;
        }

        private static Kingdom FindTargetKingdom()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x != playerKingdom)
                ?? Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated);
        }

        private static Clan FindTargetClan(Clan except)
        {
            return Clan.All.FirstOrDefault(x => x != null && x != except && !x.IsEliminated && !x.IsClanTypeMercenary && x.Leader != null);
        }

        private static Clan FindJoinClan()
        {
            Kingdom target = FindTargetKingdom();
            return Clan.All.FirstOrDefault(x => x != null
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Kingdom != target
                && x.Leader != null)
                ?? Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Kingdom != null && x.Leader != null);
        }

        private static Clan FindMercenaryClan()
        {
            return Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.IsClanTypeMercenary && x.Leader != null);
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

        private static void WriteOvernightStatus(string state, string message, LiveDialogueBatchSummary batch = null)
        {
            try
            {
                string directory = LogDirectory();
                Directory.CreateDirectory(directory);
                JObject status = new JObject
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["state"] = state ?? string.Empty,
                    ["message"] = message ?? string.Empty,
                    ["overnightRunId"] = _overnightRunId ?? string.Empty,
                    ["batchIndex"] = _overnightBatchIndex,
                    ["maxBatches"] = _overnightMaxBatches,
                    ["batchSize"] = LiveDialogueBatchSize,
                    ["active"] = _overnightLiveDialogueActive,
                    ["paused"] = _overnightLiveDialoguePaused,
                    ["batchInFlight"] = _overnightBatchInFlight,
                    ["nextDay"] = _overnightNextDay,
                    ["totals"] = OvernightTotalsJson(),
                    ["goal"] = new JObject
                    {
                        ["batchSize"] = LiveDialogueBatchSize,
                        ["passThreshold"] = OvernightGoalBatchPassThreshold,
                        ["consecutiveTarget"] = OvernightGoalConsecutiveBatches,
                        ["consecutiveGoalBatches"] = _overnightConsecutiveGoalBatches,
                        ["bestBatchPassed"] = _overnightBestBatchPassed
                    },
                    ["lastLiveDialogueRunId"] = _lastLiveDialogueRunId ?? string.Empty,
                    ["lastBatch"] = batch == null ? new JObject() : batch.ToJson(),
                    ["commandPath"] = OvernightCommandPath(),
                    ["statusPath"] = OvernightStatusPath(),
                    ["logPath"] = Path.Combine(directory, "action-gauntlet.jsonl")
                };

                File.WriteAllText(OvernightStatusPath(), status.ToString(Formatting.Indented));
            }
            catch
            {
                // The overnight harness should never destabilize the campaign.
            }
        }

        private static JObject OvernightTotalsJson()
        {
            return new JObject
            {
                ["passed"] = _overnightTotalPassed,
                ["skipped"] = _overnightTotalSkipped,
                ["failed"] = _overnightTotalFailed,
                ["total"] = _overnightTotalCases,
                ["consecutiveGoalBatches"] = _overnightConsecutiveGoalBatches,
                ["bestBatchPassed"] = _overnightBestBatchPassed,
                ["goalPassThreshold"] = OvernightGoalBatchPassThreshold,
                ["goalConsecutiveTarget"] = OvernightGoalConsecutiveBatches
            };
        }

        private static string LogDirectory()
        {
            return Path.Combine(BasePath.Name, "Modules", "ReignBeta", "logs");
        }

        private static string OvernightCommandPath()
        {
            return Path.Combine(LogDirectory(), OvernightCommandFileName);
        }

        private static string OvernightStatusPath()
        {
            return Path.Combine(LogDirectory(), OvernightStatusFileName);
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
            return TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null && Clan.PlayerClan != null && MobileParty.MainParty != null && ReignAICampaignBehavior.Instance != null;
        }

        private static bool RequireCampaign()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                Show("Start or load a campaign before running the Action Gauntlet.");
                return false;
            }

            if (ReignAICampaignBehavior.Instance == null)
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
