using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public static class ReignPartyDialogueAuditRunner
    {
        private const int SceneCount = 5;
        private const int ExchangesPerScene = 3;
        private static bool _active;
        private static volatile bool _remotePollInFlight;
        private static volatile bool _stopRequested;
        private static DateTime _nextRemotePollUtc = DateTime.MinValue;
        private static TaleWorlds.CampaignSystem.Campaign _observedCampaign;
        private static string _launchRequestId = string.Empty;
        private static string _runId = string.Empty;

        public static bool IsActive => _active;

        public static void ApplicationTick(float dt)
        {
            if (_remotePollInFlight || DateTime.UtcNow < _nextRemotePollUtc) return;
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (settings == null
                || !settings.McmTestModeEnabled
                || !settings.UseLocalServer
                || campaign == null
                || CharacterObject.PlayerCharacter == null)
                return;
            if (!ReferenceEquals(_observedCampaign, campaign))
            {
                _observedCampaign = campaign;
                _nextRemotePollUtc = DateTime.UtcNow.AddSeconds(10);
                return;
            }
            if (ReignSaveSyncCoordinator.IsAlignmentPending) return;

            // Legacy MCM compatibility requests are infrequent. The unified live
            // bridge owns responsive automation, so avoid churning loopback HTTP
            // and JSON allocations while no legacy audit is waiting.
            _nextRemotePollUtc = DateTime.UtcNow.AddSeconds(30);
            _remotePollInFlight = true;
            _ = PollRemoteRequestAsync();
        }

        private static async Task PollRemoteRequestAsync()
        {
            try
            {
                JObject request = await ReignServerClient.PollPartyDialogueAuditRequestAsync().ConfigureAwait(false);
                if (request.Value<bool?>("ok") != true || request.Value<bool?>("found") != true) return;
                string requestId = request.Value<string>("requestId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requestId)) return;
                if (_active || ReignNpcDialogueAuditRunner.IsActive)
                {
                    await ReignServerClient.AcknowledgePartyDialogueAuditRequestAsync(
                        requestId, "rejected", null, "Another dialogue audit is active.").ConfigureAwait(false);
                    return;
                }

                List<string> searches = (request["heroSearches"] as JArray)?.Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value)).ToList() ?? new List<string>();
                List<Hero> heroes = await ReignMainThread.InvokeAsync(() => ResolveAvailableHeroes(searches)).ConfigureAwait(false);
                if (heroes.Count != searches.Count || heroes.Count < 2)
                {
                    await ReignServerClient.AcknowledgePartyDialogueAuditRequestAsync(
                        requestId, "rejected", heroes,
                        "Every requested hero must be alive and currently available in Party Chat.").ConfigureAwait(false);
                    return;
                }

                int seed = request.Value<int?>("seed") ?? unchecked((int)DateTime.UtcNow.Ticks);
                bool started = await ReignMainThread.InvokeAsync(() =>
                {
                    if (_active || ReignNpcDialogueAuditRunner.IsActive || ReignSaveSyncCoordinator.IsAlignmentPending) return false;
                    _launchRequestId = requestId;
                    _ = RunAsync(heroes, seed);
                    return true;
                }).ConfigureAwait(false);
                if (!started) return;

                await ReignServerClient.AcknowledgePartyDialogueAuditRequestAsync(
                    requestId, "accepted", heroes, "Party audit scheduled through the production Party Chat runner.").ConfigureAwait(false);
                ReignLog.Info("Remote party dialogue audit accepted request=" + requestId
                    + " heroes=" + string.Join(",", heroes.Select(hero => hero.StringId)));
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Remote party dialogue audit poll failed: " + ex.Message);
            }
            finally
            {
                _remotePollInFlight = false;
            }
        }

        private static async Task RunAsync(List<Hero> heroes, int seed)
        {
            if (_active || heroes == null || heroes.Count < 2) return;
            _active = true;
            _stopRequested = false;
            _runId = "party-dialogue-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + unchecked((uint)seed).ToString("X8");
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            bool oldDiplomacy = settings?.ExecuteDiplomacyActions ?? false;
            bool oldStrategy = settings?.ExecuteStrategyPlans ?? false;
            bool oldPolitics = settings?.ExecuteInternalPoliticsActions ?? false;
            bool cancelled = false;
            string reportPath = string.Empty;

            try
            {
                if (settings != null)
                {
                    settings.ExecuteDiplomacyActions = false;
                    settings.ExecuteStrategyPlans = false;
                    settings.ExecuteInternalPoliticsActions = false;
                }
                List<PartyAuditLine> plan = BuildPlan(seed);
                JObject started = await ReignServerClient.StartPartyDialogueAuditAsync(_runId, heroes, seed).ConfigureAwait(false);
                if (started.Value<bool?>("ok") != true)
                    throw new InvalidOperationException(started.Value<string>("error") ?? "The server refused to start the party audit.");
                reportPath = started.Value<string>("reportPath") ?? string.Empty;

                for (int sceneIndex = 0; sceneIndex < SceneCount && !_stopRequested; sceneIndex++)
                {
                    if (await IsCancelledOnServerAsync().ConfigureAwait(false)) break;
                    ReignPartyChatScreenVM vm = await OpenPartyChatAsync(heroes).ConfigureAwait(false);
                    if (vm == null) throw new InvalidOperationException("The Party Chat overlay did not open with the requested heroes.");
                    await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                        "Party dialogue audit " + _runId + " - scene " + (sceneIndex + 1) + "/" + SceneCount + ".")).ConfigureAwait(false);

                    foreach (PartyAuditLine line in plan.Where(row => row.SceneIndex == sceneIndex).OrderBy(row => row.SceneOrder))
                    {
                        if (_stopRequested || await IsCancelledOnServerAsync().ConfigureAwait(false)) break;
                        await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                            "Audit exchange " + (line.TurnIndex + 1) + "/15 - " + line.Label + ".")).ConfigureAwait(false);
                        ReignPartyChatBatchResult batch = await vm.SendAutomationLineAsync(
                            line.Text, _runId, sceneIndex, line.TurnIndex).ConfigureAwait(false);
                        try
                        {
                            JObject verified = await ReignServerClient.VerifyPartyDialogueAuditTurnAsync(
                                _runId,
                                sceneIndex,
                                line.TurnIndex,
                                line.Text,
                                batch,
                                line.RecallTokens,
                                line.RequireEverySpeakerRecall).ConfigureAwait(false);
                            await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                                verified.Value<bool?>("passed") == true
                                    ? "Group exchange audit passed."
                                    : "Group exchange recorded failures; continuing.")).ConfigureAwait(false);
                        }
                        catch (Exception verifyEx)
                        {
                            ReignLog.Warn("Party dialogue turn verification failed; continuing: " + verifyEx.Message);
                        }
                        await Task.Delay(300).ConfigureAwait(false);
                    }

                    ReignConversationFinishResult finish = await vm.FinishAutomationSceneAsync("party_dialogue_audit_scene").ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(ReignPartyChatScreenManager.Close).ConfigureAwait(false);
                    if (finish.Ok)
                    {
                        await ReignServerClient.VerifyPartyDialogueAuditSceneAsync(
                            _runId, sceneIndex, finish.SessionId, finish.SceneSummaryId).ConfigureAwait(false);
                    }
                    else
                    {
                        ReignLog.Warn("Party dialogue audit scene finish failed: " + finish.Error);
                    }
                    await Task.Delay(700).ConfigureAwait(false);
                }

                cancelled = _stopRequested;
                JObject completed = await ReignServerClient.FinishPartyDialogueAuditAsync(_runId, cancelled).ConfigureAwait(false);
                reportPath = completed.Value<string>("reportPath") ?? reportPath;
                int passed = completed.Value<int?>("passed") ?? 0;
                int failed = completed.Value<int?>("failed") ?? 0;
                await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Party dialogue audit " + (cancelled ? "cancelled" : "finished")
                    + ": " + passed + " passed, " + failed + " failed. Report: " + reportPath,
                    failed == 0 ? Color.FromUint(0xFF77DD88) : Color.FromUint(0xFFFFAA55)))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cancelled = true;
                ReignLog.Exception("Party dialogue audit", ex);
                try
                {
                    JObject stopped = await ReignServerClient.FinishPartyDialogueAuditAsync(_runId, true).ConfigureAwait(false);
                    reportPath = stopped.Value<string>("reportPath") ?? reportPath;
                }
                catch (Exception finishEx)
                {
                    ReignLog.Warn("Party dialogue audit could not persist cancellation: " + finishEx.Message);
                }
                await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Party dialogue audit stopped: " + ex.Message,
                    Color.FromUint(0xFFFFAA55)))).ConfigureAwait(false);
            }
            finally
            {
                if (settings != null)
                {
                    settings.ExecuteDiplomacyActions = oldDiplomacy;
                    settings.ExecuteStrategyPlans = oldStrategy;
                    settings.ExecuteInternalPoliticsActions = oldPolitics;
                }
                await ReignMainThread.InvokeAsync(ReignPartyChatScreenManager.Close).ConfigureAwait(false);
                _active = false;
                _stopRequested = false;
                _launchRequestId = string.Empty;
            }
        }

        private static async Task<bool> IsCancelledOnServerAsync()
        {
            if (string.IsNullOrWhiteSpace(_runId)) return false;
            try
            {
                JObject status = await ReignServerClient.GetPartyDialogueAuditStatusAsync(_runId).ConfigureAwait(false);
                if (string.Equals(status.Value<string>("status"), "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    _stopRequested = true;
                }
            }
            catch { }
            return _stopRequested;
        }

        private static async Task<ReignPartyChatScreenVM> OpenPartyChatAsync(List<Hero> heroes)
        {
            await ReignMainThread.InvokeAsync(() =>
            {
                ReignIndividualChatScreenManager.Close();
                ReignPartyChatScreenManager.Close();
                ReignPartyChatScreenManager.Open();
            }).ConfigureAwait(false);
            for (int i = 0; i < 120; i++)
            {
                ReignPartyChatScreenVM vm = await ReignMainThread.InvokeAsync(() => ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                if (vm != null)
                {
                    bool selected = await ReignMainThread.InvokeAsync(() => vm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
                    return selected ? vm : null;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return null;
        }

        private static List<Hero> ResolveAvailableHeroes(IEnumerable<string> searches)
        {
            List<Hero> available = ReignPartyChatSession.GetAvailableConversationHeroes();
            List<Hero> result = new List<Hero>();
            foreach (string search in searches ?? Enumerable.Empty<string>())
            {
                string query = (search ?? string.Empty).Trim();
                Hero match = available
                    .Where(hero => hero != null && !result.Any(existing => existing.StringId == hero.StringId))
                    .Select(hero => new { Hero = hero, Score = SearchScore(hero, query) })
                    .Where(row => row.Score < 100)
                    .OrderBy(row => row.Score)
                    .ThenBy(row => row.Hero.Name?.ToString() ?? row.Hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(row => row.Hero)
                    .FirstOrDefault();
                if (match != null) result.Add(match);
            }
            return result;
        }

        private static int SearchScore(Hero hero, string query)
        {
            string name = hero?.Name?.ToString() ?? string.Empty;
            string id = hero?.StringId ?? string.Empty;
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(id, query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
            if (id.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            return 100;
        }

        internal static List<PartyAuditLine> BuildPlan(int seed)
        {
            string[] casual =
            {
                "What ordinary sound in a busy town do you find comforting, and which one tests your patience?",
                "If the road were quiet tonight, would you rather hear a song, a practical story, or complete silence?",
                "What small meal makes a difficult travel day feel tolerable?"
            };
            Random random = new Random(seed);
            string casualOne = casual[random.Next(casual.Length)];
            string casualTwo = casual.Where(value => value != casualOne).OrderBy(_ => random.Next()).First();
            return new List<PartyAuditLine>
            {
                L(0, "mixed-salience anchors", "Good afternoon, everyone. While we are together in Danustica, remember a few harmless details: I call my old pack mule Pebble, I keep a chipped blue cup wrapped in green cloth, and this morning a spice seller sneezed twice near the east gate. A red kite circled above us while we spoke. Which small detail catches each of you?"),
                L(0, "personal preferences", "Another small fact: I prefer dried apricots to figs, but I dislike honey on either. Tell me one equally ordinary preference of your own."),
                L(0, "casual variety", casualOne),

                LR(1, "first cross-scene recall", "Before we change subjects, each of you tell me what you remember about my mule, the cup and its wrapping, the sneezing seller, the gate, and the bird above us. Do not invent missing details.", true, "Pebble", "blue cup", "green cloth", "twice", "east gate", "red kite"),
                L(1, "second incidental bundle", "Here is another unimportant moment: I promised a potter named Nemos that I would return his borrowed brass needle after the first rain. A loose shutter clicked three times while he spoke, and we shared onion bread. What part seems easiest to forget?"),
                L(1, "relationship perspective", "Without making promises or plans, how would each of you describe the tone between the four of us so far?"),

                L(2, "trade and settlement", "What harmless market custom or travel habit around Danustica would surprise someone who had never visited?"),
                LR(2, "second-bundle recall", "Each of you: who loaned me what, when did I say I would return it, what clicked, how many times, and what food was present?", true, "Nemos", "brass needle", "first rain", "shutter", "three times", "onion bread"),
                L(2, "world affairs", "What distant rumor or political concern have people discussed lately, and how certain are you that it is true?"),

                LR(3, "episode-depth recall", "Reconstruct our earlier conversation as an episode: where were we, who was present, what animal and household object did I mention, what happened near which gate, what flew overhead, and what food preference did I share?", false, "Danustica", "Pebble", "blue cup", "east gate", "red kite", "apricots", "figs", "honey"),
                L(3, "beliefs and uncertainty", "Tell me one belief you hold cautiously and one kind of evidence that would change your mind."),
                L(3, "casual contrast", casualTwo),

                LR(4, "full-history recall", "Final memory check: together, recover as much as you honestly can from both earlier bundles, including names, objects, colors, wrapping, counts, place, weather condition, sounds, food, and the small preference. Say when you are unsure rather than guessing.", false, "Pebble", "blue", "green cloth", "twice", "east gate", "red kite", "Nemos", "brass needle", "first rain", "three times", "onion bread", "apricots", "figs", "honey"),
                L(4, "interpersonal history", "Each of you, name one detail another person in this group focused on earlier, if you truly remember it. Otherwise say that you do not."),
                L(4, "memory prioritization", "To close, each of you choose one fact from this conversation worth carrying forward and one trivial detail you would reasonably let fade. Explain the difference briefly.")
            };
        }

        private static PartyAuditLine L(int scene, string label, string text)
        {
            return new PartyAuditLine(scene, label, text, false, new string[0]);
        }

        private static PartyAuditLine LR(int scene, string label, string text, bool every, params string[] tokens)
        {
            return new PartyAuditLine(scene, label, text, every, tokens);
        }

        internal sealed class PartyAuditLine
        {
            public PartyAuditLine(int sceneIndex, string label, string text, bool every, IEnumerable<string> recallTokens)
            {
                SceneIndex = sceneIndex;
                SceneOrder = BuildPlanCounter.Next(sceneIndex);
                TurnIndex = sceneIndex * ExchangesPerScene + SceneOrder;
                Label = label;
                Text = text;
                RequireEverySpeakerRecall = every;
                RecallTokens = (recallTokens ?? Enumerable.Empty<string>()).ToList();
            }

            public int SceneIndex { get; }
            public int SceneOrder { get; }
            public int TurnIndex { get; }
            public string Label { get; }
            public string Text { get; }
            public bool RequireEverySpeakerRecall { get; }
            public List<string> RecallTokens { get; }
        }

        private static class BuildPlanCounter
        {
            private static readonly Dictionary<int, int> Counts = new Dictionary<int, int>();
            public static int Next(int scene)
            {
                int value = Counts.ContainsKey(scene) ? Counts[scene] : 0;
                Counts[scene] = value + 1;
                if (scene == SceneCount - 1 && Counts[scene] >= ExchangesPerScene) Counts.Clear();
                return value;
            }
        }
    }
}
