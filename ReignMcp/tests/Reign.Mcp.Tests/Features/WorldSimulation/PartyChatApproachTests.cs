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
        Assert.Equal(35, report.RootElement.GetArrayLength());
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
        namespace ReignBeta.Campaign { public sealed class ReignWorldHistoryCampaignBehavior { public static ReignWorldHistoryCampaignBehavior Instance; public string TimelineId; } }
        public sealed class Options { public bool Enabled = true; public bool Allows(string key) => Enabled; }
        public static class ReignServerClient { public static Options NpcLivesOptions = new Options(); public static string GetCampaignId() => "isolated"; }
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
            public string SentText, VisibleText, Error;
            public Draft SentDraft;
            public bool SuppressAssessment;
            public List<Hero> Heroes = new List<Hero> { new Hero { StringId = "npc_a" } };
            public List<Hero> GetSelectedHeroes() => Heroes;
            public string LifeAudienceKey(List<Hero> heroes) => string.Join("|", heroes.Select(h => h.StringId));
            public Task<ReignPartyChatBatchResult> RequestLifeOpening(List<Hero> heroes) { Openings++; return Task.FromResult(new ReignPartyChatBatchResult()); }
            public void AddSystemLine(string text, bool persist) { Error = text; }
            public void AddChatLine(string name, string text, string role) { VisibleText = text; }
            public void RememberConversationHeroes(List<Hero> heroes) { }
            public Task<ReignPartyChatBatchResult> RequestLiveNpcTurns(List<Hero> heroes, string text, bool suppressRelationshipAssessment = false, Draft actionDraft = null) { Sends++; SentText = text; SentDraft = actionDraft; SuppressAssessment = suppressRelationshipAssessment; return Task.FromResult(new ReignPartyChatBatchResult()); }
            public void RefreshLabels() { }
            public void PersistCastleTranscript() { }
            /* PRODUCTION_SEND */
            private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
            private static void Reset() { ReignServerClient.NpcLivesOptions.Enabled = true; ReignConversationInputJournal.Fail = false; ReignConversationInputJournal.Text = null; }
            public static string[] Run() {
                var passed = new List<string>();
                foreach (string previous in new[] { "", "npc_a", "npc_other" })
                foreach (string text in new[] { "What happened today?", "  My own opening.  ", "*waves*", "\u2003Hello\u2003" }) {
                    Reset(); var vm = new PartySendProbe { InputText = text, _lifeApproachedKey = previous };
                    vm.ExecuteSend().GetAwaiter().GetResult();
                    Check(vm.Openings == 0 && vm.Sends == 1, "typed input must bypass Approach for audience " + previous);
                    Check(vm.SentText == text && vm.VisibleText == text && vm._session.Transcript == text && ReignConversationInputJournal.Text == text, "preserve exact typed text");
                    Check(vm._session.PlayerInputCount == 1 && vm.InputText == "" && vm._activeBatchTask == null, "normal send bookkeeping");
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
                    Reset(); ReignServerClient.NpcLivesOptions.Enabled = false; var vm = new PartySendProbe { InputText = text };
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
                return passed.ToArray();
            }
        }
        """;
}
