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
                HashSet<string> beforeActionIds = await ReignMainThread.InvokeAsync(() => new HashSet<string>((ReignServices.AI?.Actions ?? new List<ReignWorldActionRecord>())
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
                List<ReignWorldActionRecord> ledgerActions = await ReignMainThread.InvokeAsync(() => (ReignServices.AI?.Actions ?? new List<ReignWorldActionRecord>())
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
                    : await ReignMainThread.InvokeAsync(() => ReignServices.AI?.GetLastActionResult(expectedAction.ActionId)).ConfigureAwait(false);
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
    }
}
