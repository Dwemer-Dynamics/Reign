using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PartyChatApproachTests
{
    [WindowsFact]
    public async Task ActualSendMethodRoutesPlayerTextWithoutAnApproach()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignPartyChatScreenVM.cs"));
        const string signature = "public async void ExecuteSend()";
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        int end = source.IndexOf("public async Task<ReignPartyChatBatchResult> SendAutomationLineAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate the production Send method.");
        // Execute the entire production method with isolated I/O/native dependencies.
        // Only the return type changes so the test can await this async-void UI command.
        string method = source[start..end].Replace(signature, "public async Task ExecuteSend()");
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "party-chat-send-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "probe.cs"), Harness.Replace("/* PRODUCTION_SEND */", method));
        File.WriteAllText(Path.Combine(evidence, "probe.ps1"), """
            $ErrorActionPreference = 'Stop'
            Add-Type -Path (Join-Path $PSScriptRoot 'probe.cs') -WarningAction SilentlyContinue
            $json = ConvertTo-Json -InputObject @([PartySendProbe]::Run()) -Depth 6
            [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'report.json'), $json)
            """ );
        var startInfo = new ProcessStartInfo("pwsh") {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(evidence, "probe.ps1") })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the isolated party Send probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Party Send probe timed out: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.Equal(37, report.RootElement.GetArrayLength());
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Isolated PowerShell source execution requires Windows."; }
    }

    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        public sealed class Hero { public static Hero MainHero = new Hero { StringId = "player", Name = "Player" }; public string StringId; public string Name; }
        public sealed class Draft { public string NpcId; }
        namespace ReignBeta.Actions { public static class ReignActionService { public static Draft Copy(Draft draft) => new Draft { NpcId = draft.NpcId }; } }
        namespace ReignBeta.WorldSimulation.Campaign { public sealed class ReignWorldHistoryCampaignBehavior { public static ReignWorldHistoryCampaignBehavior Instance; public string TimelineId; } }
        public static class ReignServices { public static ReignBeta.WorldSimulation.Campaign.ReignWorldHistoryCampaignBehavior WorldHistory; }
        public sealed class Options { public bool Enabled = true; public bool Allows(string key) => Enabled; }
        public static class ReignNpcLivesClient { public static Options NpcLivesOptions = new Options(); }
        public static class ReignServerClient {
            public static string GetCampaignId() => "isolated";
            // Modeled load identity: a campaign object and a save generation, both replaced by a load change.
            public static object Campaign = new object(); public static int SaveGeneration;
            public static void ChangeLoad() { Campaign = new object(); SaveGeneration++; }
            public sealed class RequestFence {
                private readonly object _campaign; private readonly int _generation;
                private RequestFence(object campaign, int generation) { _campaign = campaign; _generation = generation; }
                public static RequestFence Capture() => new RequestFence(Campaign, SaveGeneration);
                public bool IsCurrent() { ReignMainThread.FenceChecks++; return _campaign != null && ReferenceEquals(_campaign, Campaign) && _generation == SaveGeneration; }
            }
        }
        // Dispatches each callback as the game thread and records it; exceptions fault the task as in production.
        public static class ReignMainThread {
            public static bool IsMainThread; public static int Dispatched, FenceChecks;
            public static Task InvokeAsync(Action action) { return InvokeAsync<bool>(() => { action(); return true; }); }
            public static Task<T> InvokeAsync<T>(Func<T> action) {
                bool previous = IsMainThread; IsMainThread = true; Dispatched++;
                try { return Task.FromResult(action()); } catch (Exception ex) { return Task.FromException<T>(ex); } finally { IsMainThread = previous; }
            }
        }
        public static class ReignConversationInputJournal { public static bool Fail; public static string Text; public static void Save(string campaign, string timeline, string player, string id, string text, string session) { if (Fail) throw new Exception("journal failed"); Text = text; } }
        public sealed class Session { public string ApproachRequestId = "", SessionId = "session"; public int PlayerInputCount; public string Transcript; public void RegisterPlayerInput() { PlayerInputCount++; } public void RecordTranscriptLine(string name, string text, string id, string role) { Transcript = text; } }
        public sealed class ReignPartyChatBatchResult { public bool Ok = true; }
        public sealed class PartySendProbe {
            public bool _targetedReviewMode, _banterReadOnly, _isFinalized, IsBusy;
            public bool InputEnabled = true, ShowLifeControls = true;
            public bool ContinuationPresent = true;
            public bool CheckContinuedPartyPresence() => ContinuationPresent;
            public string InputText, _lifeApproachedKey = "";
            public Session _session = new Session();
            public Draft ActionDraft;
            public Task _activeBatchTask;
            public int Openings, Sends;
            public bool SentOnMainThread;
            public TaskCompletionSource<ReignPartyChatBatchResult> PendingOpening;
            public string SentText, VisibleText, Error;
            public Draft SentDraft;
            public bool SuppressAssessment;
            public List<Hero> Heroes = new List<Hero> { new Hero { StringId = "npc_a" } };
            public List<Hero> GetSelectedHeroes() => Heroes;
            public string LifeAudienceKey(List<Hero> heroes) => string.Join("|", heroes.Select(h => h.StringId));
            public Task<ReignPartyChatBatchResult> RequestLifeOpening(List<Hero> heroes) { Openings++; return PendingOpening != null ? PendingOpening.Task : Task.FromResult(new ReignPartyChatBatchResult()); }
            public void AddSystemLine(string text, bool persist) { Error = text; }
            public void AddChatLine(string name, string text, string role) { VisibleText = text; }
            public void RememberConversationHeroes(List<Hero> heroes) { }
            public Task<ReignPartyChatBatchResult> RequestLiveNpcTurns(List<Hero> heroes, string text, bool suppressRelationshipAssessment = false, Draft actionDraft = null) { Sends++; SentOnMainThread = ReignMainThread.IsMainThread; SentText = text; SentDraft = actionDraft; SuppressAssessment = suppressRelationshipAssessment; return Task.FromResult(new ReignPartyChatBatchResult()); }
            public void RefreshLabels() { }
            public void PersistCastleTranscript() { }
            /* PRODUCTION_SEND */
            private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
            private static void Reset() { ReignNpcLivesClient.NpcLivesOptions.Enabled = true; ReignConversationInputJournal.Fail = false; ReignConversationInputJournal.Text = null;
                ReignServerClient.Campaign = new object(); ReignServerClient.SaveGeneration = 0; ReignMainThread.IsMainThread = false; ReignMainThread.Dispatched = 0; ReignMainThread.FenceChecks = 0; }
            public static string[] Run() {
                // Continuations resume inline on this probe thread, never posted to a host context.
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                var passed = new List<string>();
                foreach (string previous in new[] { "", "npc_a", "npc_other" })
                foreach (string text in new[] { "What happened today?", "  My own opening.  ", "*waves*", "\u2003Hello\u2003" }) {
                    Reset(); var vm = new PartySendProbe { InputText = text, _lifeApproachedKey = previous };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 0 && vm.Sends == 1, "typed input must bypass Approach for audience " + previous);
                    Check(vm.SentText == text && vm.VisibleText == text && vm._session.Transcript == text && ReignConversationInputJournal.Text == text, "preserve exact typed text");
                    Check(vm._session.PlayerInputCount == 1 && vm.InputText == "" && vm._activeBatchTask == null, "normal send bookkeeping");
                    Check(vm.SentOnMainThread && ReignMainThread.Dispatched == 2 && ReignMainThread.FenceChecks == 2, "send admitted and completed through fenced game-thread dispatch");
                    passed.Add("typed:" + previous + ":" + text);
                }
                foreach (string text in new string[] { null, "", " \t\r\n", "\u2003" }) {
                    Reset(); var vm = new PartySendProbe { InputText = text };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 1 && vm.Sends == 0 && vm._session.PlayerInputCount == 0 && ReignConversationInputJournal.Text == null, "explicit empty Approach only");
                    passed.Add("approach:" + text);
                }
                foreach (string text in new[] { "Hello", "" })
                foreach (string gate in new[] { "busy", "finalized", "input-disabled" }) {
                    Reset(); var vm = new PartySendProbe { InputText = text, IsBusy = gate == "busy", _isFinalized = gate == "finalized", InputEnabled = gate != "input-disabled" };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 0 && vm.Sends == 0 && vm.InputText == text && vm._session.PlayerInputCount == 0, "gate " + gate);
                    passed.Add("guard:" + gate + ":" + text);
                }
                foreach (string text in new[] { "Hello", "" }) {
                    Reset(); ReignNpcLivesClient.NpcLivesOptions.Enabled = false; var vm = new PartySendProbe { InputText = text };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 0 && vm.Sends == (text.Length > 0 ? 1 : 0), "disabled approach preference");
                    passed.Add("disabled:" + text);
                }
                foreach (string mode in new[] { "home", "castle", "invitation", "review", "banter" }) {
                    Reset(); var vm = new PartySendProbe { InputText = "Hello", ShowLifeControls = false, _targetedReviewMode = mode == "review", _banterReadOnly = mode == "banter" };
                    if (mode == "invitation") vm._session.ApproachRequestId = "existing";
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == (mode == "home" || mode == "castle" ? 1 : 0) && vm.Sends == 1, "preserve existing special-mode routing " + mode);
                    passed.Add("special:" + mode);
                }
                foreach (string target in new[] { "npc_a", "npc_other" }) {
                    Reset(); var vm = new PartySendProbe { InputText = "Please join me.", ActionDraft = new Draft { NpcId = target } };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 0 && vm.Sends == (target == "npc_a" ? 1 : 0), "validate draft target before dispatch");
                    if (vm.Sends == 1) Check(vm.SentDraft.NpcId == target && !ReferenceEquals(vm.SentDraft, vm.ActionDraft), "retain copied action attachment");
                    else Check(vm.InputText == "Please join me." && vm._session.PlayerInputCount == 0, "keep invalid-target draft");
                    passed.Add("action:" + target);
                }
                Reset(); var empty = new PartySendProbe { InputText = "Hello", Heroes = new List<Hero>() };
                empty.ExecuteSend().GetAwaiter().GetResult();
                Check(empty.Openings == 0 && empty.Sends == 0 && empty.InputText == "Hello" && empty._session.PlayerInputCount == 0, "empty audience retains text"); passed.Add("empty-audience");
                Reset(); ReignConversationInputJournal.Fail = true; var failed = new PartySendProbe { InputText = "Keep my text" };
                failed.ExecuteSend().GetAwaiter().GetResult();
                Check(failed.Openings == 0 && failed.Sends == 0 && failed.InputText == "Keep my text" && failed._session.PlayerInputCount == 0, "journal failure retains unsent text"); passed.Add("journal-failure");
                foreach (string text in new[] { "Keep my unsent continuation", "" }) {
                    Reset(); var departed = new PartySendProbe { InputText = text, ContinuationPresent = false };
                    departed.ExecuteSend().GetAwaiter().GetResult();
                    Check(departed.Openings == 0 && departed.Sends == 0 && departed.InputText == text
                        && departed._session.PlayerInputCount == 0 && ReignConversationInputJournal.Text == null,
                        "a departed original participant blocks both Send and Approach without losing the draft");
                    passed.Add("departed-participant:" + text);
                }
                foreach (bool loadChanged in new[] { true, false }) {
                    // Home-mode typed send: an actual in-flight opening precedes the send continuation.
                    Reset(); var pending = new PartySendProbe { InputText = "Continue after opening", ShowLifeControls = false,
                        PendingOpening = new TaskCompletionSource<ReignPartyChatBatchResult>() };
                    Task running = pending.ExecuteSend();
                    Check(!running.IsCompleted && pending.Openings == 1 && pending.Sends == 0 && ReferenceEquals(pending._activeBatchTask, pending.PendingOpening.Task)
                        && ReignMainThread.Dispatched == 0 && ReignConversationInputJournal.Text == null && pending._session.PlayerInputCount == 0, "opening is genuinely in flight before continuation");
                    if (loadChanged) ReignServerClient.ChangeLoad();
                    pending.PendingOpening.SetResult(new ReignPartyChatBatchResult());
                    running.GetAwaiter().GetResult();
                    if (loadChanged) {
                        Check(pending.Sends == 0 && pending.InputText == "Continue after opening" && pending._session.PlayerInputCount == 0
                            && pending._session.Transcript == null && pending.VisibleText == null && pending.Error == null && ReignConversationInputJournal.Text == null,
                            "old-fence continuation performs no send and no local mutation");
                        Check(ReignMainThread.Dispatched == 1 && ReignMainThread.FenceChecks == 1, "old fence is rejected inside the dispatched admission callback");
                        passed.Add("inflight-load-change");
                    } else {
                        Check(pending.Sends == 1 && pending.SentOnMainThread && pending.SentText == "Continue after opening" && pending.InputText == ""
                            && pending._session.PlayerInputCount == 1 && ReignConversationInputJournal.Text == "Continue after opening" && pending._activeBatchTask == null,
                            "valid-owner continuation sends once on the dispatched game thread");
                        Check(ReignMainThread.Dispatched == 2 && ReignMainThread.FenceChecks == 2, "valid owner passes both fenced dispatches");
                        passed.Add("inflight-valid-owner");
                    }
                }
                return passed.ToArray();
            }
        }
        """;
}
