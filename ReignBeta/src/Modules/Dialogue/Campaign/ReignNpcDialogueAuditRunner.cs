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
    public static class ReignNpcDialogueAuditRunner
    {
        private const int SceneCount = 5;
        private const int ExchangesPerScene = 6;
        private static volatile bool _stopRequested;
        private static bool _active;
        private static volatile bool _remotePollInFlight;
        private static DateTime _nextRemotePollUtc = DateTime.MinValue;
        private static TaleWorlds.CampaignSystem.Campaign _observedCampaign;
        private static string _launchRequestId = string.Empty;
        private static Hero _activeHero;
        private static string _runId = string.Empty;
        private static string _lastSummary = "No NPC dialogue audit has run yet.";
        private static string _lastReportPath = string.Empty;

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
                _nextRemotePollUtc = DateTime.UtcNow.AddSeconds(20);
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
                JObject request = await ReignServerClient.PollNpcDialogueAuditRequestAsync().ConfigureAwait(false);
                if (request.Value<bool?>("ok") != true || request.Value<bool?>("found") != true) return;
                if (ReignSaveSyncCoordinator.IsAlignmentPending) return;

                string requestId = request.Value<string>("requestId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requestId)) return;
                if (_active)
                {
                    if (string.Equals(requestId, _launchRequestId, StringComparison.OrdinalIgnoreCase))
                    {
                        await ReignServerClient.AcknowledgeNpcDialogueAuditRequestAsync(
                            requestId, "accepted", _activeHero, "Audit runner is active.").ConfigureAwait(false);
                    }
                    return;
                }

                string heroSearch = request.Value<string>("heroSearch") ?? string.Empty;
                Hero hero = await ReignMainThread.InvokeAsync(() => FindHero(heroSearch)).ConfigureAwait(false);
                if (hero == null)
                {
                    await ReignServerClient.AcknowledgeNpcDialogueAuditRequestAsync(
                        requestId, "rejected", null, "No adult living non-player hero matched '" + heroSearch + "'.").ConfigureAwait(false);
                    ReignLog.Warn("Remote NPC dialogue audit request rejected because no hero matched: " + heroSearch);
                    return;
                }

                int seed = request.Value<int?>("seed") ?? unchecked((int)DateTime.UtcNow.Ticks);
                bool started = await ReignMainThread.InvokeAsync(() =>
                {
                    if (_active || ReignSaveSyncCoordinator.IsAlignmentPending) return false;
                    _launchRequestId = requestId;
                    _activeHero = hero;
                    _ = RunAsync(hero, seed);
                    return true;
                }).ConfigureAwait(false);
                if (!started) return;

                await ReignServerClient.AcknowledgeNpcDialogueAuditRequestAsync(
                    requestId, "accepted", hero, "Audit scheduled through the production dialogue runner.").ConfigureAwait(false);
                ReignLog.Info("Remote NPC dialogue audit request accepted request=" + requestId + " hero=" + hero.StringId);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Remote NPC dialogue audit poll failed: " + ex.Message);
            }
            finally
            {
                _remotePollInFlight = false;
            }
        }

        public static void StartFromMcm()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || !settings.McmTestModeEnabled)
            {
                Show("Enable Debug Controls before starting an NPC dialogue audit.");
                return;
            }
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || CharacterObject.PlayerCharacter == null)
            {
                Show("Load a campaign before starting an NPC dialogue audit.");
                return;
            }
            if (!settings.UseLocalServer)
            {
                Show("The local Reign server must be enabled for this audit.");
                return;
            }
            if (_active)
            {
                Show("An NPC dialogue audit is already running.");
                return;
            }

            TextInquiryData search = new TextInquiryData(
                "NPC Dialogue Audit",
                "Search for an adult living NPC by name or StringId. The audit keeps its conversation and memories in this save.",
                true, true, "Search", "Cancel",
                ShowHeroMatches, null, false,
                value => string.IsNullOrWhiteSpace(value)
                    ? new Tuple<bool, string>(false, "Enter at least part of a name or StringId.")
                    : new Tuple<bool, string>(true, string.Empty),
                string.Empty, GetDefaultSearchText());
            InformationManager.ShowTextInquiry(search, true);
        }

        private static string GetDefaultSearchText()
        {
            Hero preferred = Hero.AllAliveHeroes
                .Where(ReignConversationEligibility.IsAdultLivingNpc)
                .OrderBy(hero => (hero.Name?.ToString() ?? string.Empty).IndexOf("Gavalon", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(hero => hero.Name?.ToString() ?? hero.StringId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return preferred?.Name?.ToString() ?? preferred?.StringId ?? string.Empty;
        }

        private static Hero FindHero(string query)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0) query = GetDefaultSearchText();
            return Hero.AllAliveHeroes
                .Where(ReignConversationEligibility.IsAdultLivingNpc)
                .Select(hero => new { Hero = hero, Score = SearchScore(hero, query) })
                .Where(row => row.Score < 100)
                .OrderBy(row => row.Score)
                .ThenBy(row => row.Hero.Name?.ToString() ?? row.Hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(row => row.Hero)
                .FirstOrDefault();
        }

        public static void StopFromMcm()
        {
            if (!_active)
            {
                Show("No NPC dialogue audit is running.");
                return;
            }
            _stopRequested = true;
            Show("NPC dialogue audit will stop after the current request finishes.");
        }

        public static async void ShowLastSummaryFromMcm()
        {
            try
            {
                JObject status = await ReignServerClient.GetNpcDialogueAuditStatusAsync(_runId).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (status.Value<bool?>("found") == true)
                    {
                        _lastSummary = FormatSummary(status);
                        _lastReportPath = status.Value<string>("reportPath") ?? _lastReportPath;
                    }
                    Show(_lastSummary + (string.IsNullOrWhiteSpace(_lastReportPath) ? string.Empty : " Report: " + _lastReportPath));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => Show(_lastSummary + " Status refresh failed: " + ex.Message)).ConfigureAwait(false);
            }
        }

        private static void ShowHeroMatches(string query)
        {
            query = (query ?? string.Empty).Trim();
            List<Hero> matches = Hero.AllAliveHeroes
                .Where(ReignConversationEligibility.IsAdultLivingNpc)
                .Select(hero => new { Hero = hero, Score = SearchScore(hero, query) })
                .Where(row => row.Score < 100)
                .OrderBy(row => row.Score)
                .ThenBy(row => row.Hero.Name?.ToString() ?? row.Hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .Select(row => row.Hero)
                .ToList();
            if (matches.Count == 0)
            {
                Show("No adult living NPC matched '" + query + "'.");
                return;
            }

            List<InquiryElement> elements = matches.Select(hero => new InquiryElement(
                hero,
                (hero.Name?.ToString() ?? hero.StringId) + "  [" + hero.StringId + "]"
                    + (hero.Clan == null ? string.Empty : " — " + hero.Clan.Name),
                null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Choose NPC For Dialogue Audit",
                "Select one adult living NPC. The test runs 30 production dialogue exchanges in five scenes.",
                elements, true, 1, 1, "Choose", "Cancel",
                selected =>
                {
                    Hero hero = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (hero != null) ConfirmStart(hero);
                },
                null, string.Empty, false), true, false);
        }

        private static int SearchScore(Hero hero, string query)
        {
            string name = hero?.Name?.ToString() ?? string.Empty;
            string id = hero?.StringId ?? string.Empty;
            if (string.Equals(id, query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
            if (id.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            return 100;
        }

        private static void ConfirmStart(Hero hero)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Run NPC Dialogue Audit",
                "This will speak with " + hero.Name + " for 30 exchanges, close and reopen five conversation scenes, and keep the resulting history and memories in this save. Use a disposable test campaign.",
                true, true, "Run Audit", "Cancel",
                () => _ = RunAsync(hero), null), true);
        }

        private static Task RunAsync(Hero hero)
        {
            return RunAsync(hero, null);
        }

        private static async Task RunAsync(Hero hero, int? requestedSeed)
        {
            if (_active || hero == null) return;
            _active = true;
            _activeHero = hero;
            _stopRequested = false;
            int seed = requestedSeed ?? unchecked((int)DateTime.UtcNow.Ticks);
            _runId = "npc-dialogue-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + unchecked((uint)seed).ToString("X8");
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            bool oldDiplomacy = settings?.ExecuteDiplomacyActions ?? false;
            bool oldStrategy = settings?.ExecuteStrategyPlans ?? false;
            bool oldPolitics = settings?.ExecuteInternalPoliticsActions ?? false;
            bool oldConversationActions = settings?.ExecuteConversationActions ?? false;
            bool cancelled = false;

            try
            {
                if (settings != null)
                {
                    settings.ExecuteDiplomacyActions = false;
                    settings.ExecuteStrategyPlans = false;
                    settings.ExecuteInternalPoliticsActions = false;
                    settings.ExecuteConversationActions = false;
                }
                string playerName = Hero.MainHero?.Name?.ToString() ?? "the traveler";
                string playerClanName = Clan.PlayerClan?.Name?.ToString() ?? "an independent house";
                List<AuditLine> plan = BuildPlan(seed, playerName, playerClanName);
                JObject started = await ReignServerClient.StartNpcDialogueAuditAsync(_runId, hero, seed).ConfigureAwait(false);
                if (started.Value<bool?>("ok") != true) throw new InvalidOperationException(started.Value<string>("error") ?? "The server refused to start the audit.");

                for (int sceneIndex = 0; sceneIndex < SceneCount && !_stopRequested; sceneIndex++)
                {
                    if (await IsCancelledOnServerAsync().ConfigureAwait(false)) break;
                    ReignIndividualChatScreenVM vm = await OpenChatAsync(hero).ConfigureAwait(false);
                    if (vm == null) throw new InvalidOperationException("The individual chat overlay did not open.");
                    if (!await WaitForChatReadyAsync(vm).ConfigureAwait(false))
                        throw new InvalidOperationException("The individual chat overlay did not finish loading history.");
                    await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                        "NPC dialogue audit " + _runId + " — scene " + (sceneIndex + 1) + "/" + SceneCount + ".")).ConfigureAwait(false);

                    List<AuditLine> sceneLines = plan.Where(line => line.SceneIndex == sceneIndex).OrderBy(line => line.SceneOrder).ToList();
                    int completedInScene = 0;
                    foreach (AuditLine line in sceneLines)
                    {
                        if (_stopRequested || await IsCancelledOnServerAsync().ConfigureAwait(false)) break;
                        string correlationId = _runId + "-s" + (sceneIndex + 1) + "-t" + (line.TurnIndex + 1);
                        await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                            "Audit turn " + (line.TurnIndex + 1) + "/30 — " + line.Label + ".")).ConfigureAwait(false);
                        ReignDialogueReply reply = await vm.SendAutomationLineAsync(
                            line.Text, correlationId, _runId, sceneIndex, line.TurnIndex).ConfigureAwait(false);
                        if (!reply.Ok)
                        {
                            await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine("Turn failed: " + (reply.Error ?? "unknown error"))).ConfigureAwait(false);
                            continue;
                        }
                        completedInScene++;
                        try
                        {
                            JObject verified = await ReignServerClient.VerifyNpcDialogueAuditTurnAsync(
                                _runId, sceneIndex, line.TurnIndex, line.Text, reply,
                                new JArray(line.ExpectedContextPulls), line.RecallToken).ConfigureAwait(false);
                            await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine(
                                verified.Value<bool?>("passed") == true ? "Turn audit passed." : "Turn audit recorded failures; continuing.")).ConfigureAwait(false);
                        }
                        catch (Exception verifyEx)
                        {
                            ReignLog.Warn("NPC dialogue turn audit verification failed; continuing: " + verifyEx.Message);
                            await ReignMainThread.InvokeAsync(() => vm.AddAutomationSystemLine("Turn completed, but its verifier call failed; continuing.")).ConfigureAwait(false);
                        }
                        await Task.Delay(250).ConfigureAwait(false);
                    }

                    ReignConversationFinishResult finish = await vm.FinishAutomationSceneAsync("npc_dialogue_audit_scene").ConfigureAwait(false);
                    string sessionId = finish.SessionId;
                    await ReignMainThread.InvokeAsync(ReignIndividualChatScreenManager.Close).ConfigureAwait(false);
                    if (finish.Ok && completedInScene == ExchangesPerScene)
                    {
                        await ReignServerClient.VerifyNpcDialogueAuditSceneAsync(_runId, sceneIndex, sessionId).ConfigureAwait(false);
                    }
                    else
                    {
                        ReignLog.Warn("NPC dialogue audit scene finish failed: " + finish.Error);
                    }
                    await Task.Delay(500).ConfigureAwait(false);
                }
                cancelled = _stopRequested;
                JObject completed = await ReignServerClient.FinishNpcDialogueAuditAsync(_runId, cancelled).ConfigureAwait(false);
                _lastSummary = FormatSummary(completed);
                _lastReportPath = completed.Value<string>("reportPath") ?? string.Empty;
            }
            catch (Exception ex)
            {
                cancelled = true;
                _lastSummary = "NPC dialogue audit stopped with an infrastructure error: " + ex.Message;
                ReignLog.Exception("NPC dialogue audit", ex);
                try
                {
                    JObject stopped = await ReignServerClient.FinishNpcDialogueAuditAsync(_runId, true).ConfigureAwait(false);
                    _lastReportPath = stopped.Value<string>("reportPath") ?? string.Empty;
                }
                catch (Exception finishEx)
                {
                    ReignLog.Warn("NPC dialogue audit could not persist cancellation: " + finishEx.Message);
                }
            }
            finally
            {
                if (settings != null)
                {
                    settings.ExecuteDiplomacyActions = oldDiplomacy;
                    settings.ExecuteStrategyPlans = oldStrategy;
                    settings.ExecuteInternalPoliticsActions = oldPolitics;
                    settings.ExecuteConversationActions = oldConversationActions;
                }
                await ReignMainThread.InvokeAsync(() =>
                {
                    ReignIndividualChatScreenManager.Close();
                    Show(_lastSummary + (string.IsNullOrWhiteSpace(_lastReportPath) ? string.Empty : " Report: " + _lastReportPath));
                }).ConfigureAwait(false);
                _active = false;
                _activeHero = null;
                _launchRequestId = string.Empty;
                _stopRequested = false;
            }
        }

        private static async Task<ReignIndividualChatScreenVM> OpenChatAsync(Hero hero)
        {
            await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.OpenForHero(hero)).ConfigureAwait(false);
            for (int i = 0; i < 100; i++)
            {
                ReignIndividualChatScreenVM vm = await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                if (vm != null) return vm;
                await Task.Delay(100).ConfigureAwait(false);
            }
            return null;
        }

        private static async Task<bool> IsCancelledOnServerAsync()
        {
            if (string.IsNullOrWhiteSpace(_launchRequestId) || string.IsNullOrWhiteSpace(_runId)) return _stopRequested;
            try
            {
                JObject status = await ReignServerClient.GetNpcDialogueAuditStatusAsync(_runId).ConfigureAwait(false);
                if (string.Equals(status.Value<string>("status"), "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    _stopRequested = true;
                    ReignLog.Info("Remote NPC dialogue audit cancellation observed run=" + _runId);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Remote NPC dialogue audit cancellation check failed: " + ex.Message);
            }
            return _stopRequested;
        }

        private static async Task<bool> WaitForChatReadyAsync(ReignIndividualChatScreenVM vm)
        {
            for (int i = 0; i < 120 && vm != null; i++)
            {
                bool busy = await ReignMainThread.InvokeAsync(() => vm.IsBusy).ConfigureAwait(false);
                if (!busy) return true;
                await Task.Delay(250).ConfigureAwait(false);
            }
            return false;
        }

        internal static List<AuditLine> BuildPlan(int seed)
        {
            return BuildPlan(seed, "the traveler", "an independent house");
        }

        internal static List<AuditLine> BuildPlan(int seed, string playerName, string playerClanName)
        {
            string token = "Juniper" + Math.Abs(seed % 10000).ToString("0000");
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "the traveler" : playerName.Trim();
            string safePlayerClanName = string.IsNullOrWhiteSpace(playerClanName) ? "an independent house" : playerClanName.Trim();
            List<AuditLine> lines = new List<AuditLine>
            {
                L(0,"greeting","Tell me one small thing that has occupied your thoughts lately."),
                L(0,"appearance","What do my clothes and appearance suggest about my status?", "check_player_appearance_status","check_inventory_appearance"),
                L(0,"inventory","Do my supplies, food, horses, and goods look adequate for travel?", "check_inventory_appearance"),
                L(0,"nearby settlements","Which nearby towns, villages, or castles would be sensible stops on the road?", "nearby_settlements"),
                LP(0,"memory anchor","Good day. I am a traveler having a harmless conversation, not seeking an agreement or giving an order. My trail-name is " + token + "; please remember it for later.", "relevant_memory"),
                L(0,"personal state","What do you fear or hope for when you have a quiet day to yourself?"),

                L(1,"road danger","Are there bandits, looters, or dangerous roads nearby?", "nearby_bandit_parties"),
                L(1,"nearby lords","Which lords, armies, or patrols are nearby right now?", "nearby_lord_parties"),
                L(1,"local facts","How are the market, walls, garrison, security, and prosperity in this settlement?", "current_settlement_facts"),
                L(1,"diplomacy","What is the current war, peace, tribute, and alliance situation between the kingdoms?", "kingdom_diplomacy_status"),
                L(1,"clan standing","How wealthy and influential is your clan, and what standing does your house possess?", "clan_wealth_and_influence"),
                LP(1,"relationship","Good day again. My name is " + safePlayerName + ", and I am of House " + safePlayerClanName + ". " + token + " is only my trail-name. I will respect a refusal. How would you describe the trust and respect between us so far?", "relationship_history","relevant_memory"),

                L(2,"trade appraisal","Without making an agreement, how would you appraise a proposal to buy grain for one hundred denars?", "appraise_trade_offer","check_inventory_appearance"),
                L(2,"world history","I claim that I won a famous battle yesterday. Does reliable world history support that claim?", "verify_world_history"),
                L(2,"commitments","Why do promises, debts, oaths, and favors matter so much between people?", "relevant_memory"),
                L(2,"rumors","How do you decide whether gossip or a rumor deserves belief?"),
                L(2,"resources","What possessions or supplies matter most when preparing for a long journey?", "check_inventory_appearance"),
                LP(2,"variety","Thank you for hearing me after I identified myself. To change subjects, would you rather spend an evening hearing music, watching horses, or discussing old roads?"),

                LRP(3,"exact recall","Good day again. For a simple memory check, what trail-name did I share in our first conversation?", token, "relevant_memory"),
                L(3,"relationship continuity","Has anything in our earlier conversations changed your trust or opinion of me?", "relationship_history","relevant_memory"),
                L(3,"past conversation","What subjects do you remember us discussing last time?", "relevant_memory"),
                L(3,"world affairs","What worries you most about war, peace, armies, and rebellion in the realm?", "kingdom_diplomacy_status"),
                L(3,"local awareness","What should a traveler notice about this place and the roads around it?", "nearby_settlements"),
                L(3,"variety","Tell me whether rain on a campaign is comforting, irritating, or simply ordinary."),

                L(4,"promise memory","What makes a promise from an earlier conversation worth remembering?", "relevant_memory"),
                L(4,"appearance revisit","Has another look at my clothing changed your judgment of my rank or wealth?", "check_player_appearance_status","check_inventory_appearance"),
                L(4,"trade revisit","What makes a trade price fair without committing either of us to a bargain?", "appraise_trade_offer"),
                L(4,"beliefs","When memory and rumor conflict, which should a cautious person trust?", "relevant_memory"),
                LRP(4,"final exact history","Good day once more. Before we finish, please repeat the unusual trail-name from our first scene.", token, "relevant_memory"),
                L(4,"farewell","Choose a completely different subject and leave me with one candid observation about it.")
            };

            Random random = new Random(seed);
            int turnIndex = 0;
            foreach (IGrouping<int, AuditLine> scene in lines.GroupBy(line => line.SceneIndex).OrderBy(group => group.Key))
            {
                AuditLine pinned = scene.FirstOrDefault(line => line.PinnedFirst);
                List<AuditLine> shuffled = scene.Where(line => !line.PinnedFirst).ToList();
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    AuditLine swap = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = swap;
                }
                if (pinned != null) shuffled.Insert(0, pinned);
                for (int i = 0; i < shuffled.Count; i++)
                {
                    shuffled[i].SceneOrder = i;
                    shuffled[i].TurnIndex = turnIndex++;
                }
            }
            return lines;
        }

        private static AuditLine L(int scene, string label, string text, params string[] pulls)
        {
            return new AuditLine { SceneIndex = scene, Label = label, Text = text, ExpectedContextPulls = pulls?.ToList() ?? new List<string>() };
        }

        private static AuditLine LR(int scene, string label, string text, string recallToken, params string[] pulls)
        {
            AuditLine line = L(scene, label, text, pulls);
            line.RecallToken = recallToken;
            return line;
        }

        private static AuditLine LP(int scene, string label, string text, params string[] pulls)
        {
            AuditLine line = L(scene, label, text, pulls);
            line.PinnedFirst = true;
            return line;
        }

        private static AuditLine LRP(int scene, string label, string text, string recallToken, params string[] pulls)
        {
            AuditLine line = LR(scene, label, text, recallToken, pulls);
            line.PinnedFirst = true;
            return line;
        }

        private static string FormatSummary(JObject result)
        {
            return "NPC dialogue audit " + (result.Value<string>("status") ?? "unknown")
                + ": turns=" + (result.Value<int?>("turnCount") ?? 0) + "/30"
                + ", scenes=" + (result.Value<int?>("sceneCount") ?? 0) + "/5"
                + ", assertions passed=" + (result.Value<int?>("passed") ?? 0)
                + ", failed=" + (result.Value<int?>("failed") ?? 0) + ".";
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFFFFCC66)));
        }

        internal sealed class AuditLine
        {
            public int SceneIndex { get; set; }
            public int SceneOrder { get; set; }
            public int TurnIndex { get; set; }
            public string Label { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string RecallToken { get; set; } = string.Empty;
            public bool PinnedFirst { get; set; }
            public List<string> ExpectedContextPulls { get; set; } = new List<string>();
        }
    }
}
