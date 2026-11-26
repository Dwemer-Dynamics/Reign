using System.Diagnostics;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class IndividualChatApproachTests
{
    [Fact]
    public async Task ActualCommandsLeaveTheFirstMoveToThePlayer()
    {
        if (!OperatingSystem.IsWindows()) return;

        string workspace = TestOptions.FindWorkspace();
        string viewModels = Path.Combine(workspace, "ReignBeta/src/Modules/Dialogue/UI/ViewModels");
        string screen = File.ReadAllText(Path.Combine(viewModels, "ReignIndividualChatScreenVM.cs"));
        string retry = File.ReadAllText(Path.Combine(viewModels, "ReignIndividualChatRetry.cs"));
        const string sendSignature = "public async void ExecuteSend()";
        string methods = Slice(screen, sendSignature, "public Task<ReignDialogueReply> SendAutomationLineAsync(")
                .Replace(sendSignature, "public async Task ExecuteSend()", StringComparison.Ordinal)
            + Slice(screen, "private async Task<bool> InitializePresentationAsync()", "private async Task PrepareLifeOpeningAsync(")
            + Slice(retry, "public string SendButtonText", "private bool TryRecoverSavedInput()");
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "individual-chat-opening-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "probe.cs"), Probe.Replace("/* PRODUCTION */", methods, StringComparison.Ordinal));
        string script = Path.Combine(evidence, "probe.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            Add-Type -Path (Join-Path $PSScriptRoot 'probe.cs') -WarningAction SilentlyContinue
            [IndividualOpeningProbe]::Run()
            """);
        var startInfo = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the Individual Chat opening probe.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Individual Chat opening probe timed out: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        Assert.Contains("Individual Chat opening routes passed.", await output);
    }

    private static string Slice(string source, string first, string next)
    {
        int start = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, first);
        int end = source.IndexOf(next, start, StringComparison.Ordinal);
        Assert.True(end > start, next);
        return source[start..end];
    }

    private const string Probe = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        public sealed class Draft { }
        namespace ReignBeta.Actions { public static class ReignActionService { public static Draft Copy(Draft draft) => new Draft(); } }
        public static class ReignLog { public static void Warn(string text) { } }
        public static class ReignMainThread { public static Task InvokeAsync(Action run) { run(); return Task.CompletedTask; } }
        public static class ReignNpcLivesClient { public static Options NpcLivesOptions = new Options(); }
        public sealed class Options { public bool Enabled = true; public bool Allows(string option) => option == "approach" && Enabled; }
        public sealed class Context : Dictionary<string, object> { public T Value<T>(string key) => TryGetValue(key, out object value) ? (T)value : default(T); }
        public sealed class ReignRecoveredDraftReview {
            public bool Blocked;
            public string SelectedDraft;
            public bool CanSubmit(DateTime now) => !Blocked;
            public static bool HasPendingInSession(List<string> drafts, string session) => session != null && drafts.Contains(session);
        }
        public sealed class FailedTurn {
            public bool HasPending;
            public string Text = "saved failed message", CorrelationId = "original", SceneTurnId = "original-turn";
        }
        public sealed class IndividualOpeningProbe {
            public bool _isFinalized, _calibrationMode, _isOfficialAmbassadorMode, _firstMoveChosen;
            public bool _presentationInitializing = true, _initializePresentation = true;
            public bool IsOutfitVisible, IsOutfitInputVisible, IsBusy, IsPregnancyWarningVisible;
            public bool InputEnabled => !_presentationInitializing && !_isFinalized && !IsBusy && !IsOutfitVisible && !IsPregnancyWarningVisible;
            public string InputText = "", BusyText, SentText, SentId, SentSceneTurnId, SentRecover;
            public Draft ActionDraft, _failedActionDraft, SentAction;
            public string _failedRecoveredInput;
            public ReignRecoveredDraftReview _recoveredDraftReview = new ReignRecoveredDraftReview();
            public Context _conversationContext = new Context();
            public List<string> _recoverableInputs = new List<string>();
            public FailedTurn _failedTurn = new FailedTurn();
            public int History, Openings, Sends, Outfit, Recoveries;
            public void ExecuteSubmitOutfit() { Outfit++; }
            public void RefreshInputAvailability() { }
            public Task LoadHistoryAsync() { History++; return Task.CompletedTask; }
            public Task RequestLifeOpeningAsync() { Openings++; _firstMoveChosen = true; return Task.CompletedTask; }
            public FailedTurn BeginFailedConversationRetry() => _failedTurn;
            public bool TryRecoverSavedInput() {
                if (_failedTurn.HasPending || _recoverableInputs.Count == 0 || !string.IsNullOrWhiteSpace(InputText)
                    || IsBusy || IsPregnancyWarningVisible) return false;
                Recoveries++; InputText = "saved draft"; return true;
            }
            public Task SendTextAsync(string text, bool clear, string correlationId = null, Draft actionDraft = null,
                string retrySceneTurnId = null, bool retrySavedTurn = false, string recoveredInput = null) {
                Sends++; SentText = text; SentId = correlationId; SentSceneTurnId = retrySceneTurnId;
                SentAction = actionDraft; SentRecover = recoveredInput; _firstMoveChosen = true;
                if (clear) InputText = "";
                return Task.CompletedTask;
            }
            /* PRODUCTION */
            private static void Check(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
            private void Send() => ExecuteSend().GetAwaiter().GetResult();
            private static IndividualOpeningProbe Open(string mode = null) {
                var screen = new IndividualOpeningProbe();
                if (mode != null) screen._conversationContext["conversationMode"] = mode;
                Check(screen.InitializePresentationAsync().GetAwaiter().GetResult(), "presentation failed");
                Check(screen.History == 1 && screen.Sends == 0 && screen.Openings == 0, "ordinary open generated dialogue");
                return screen;
            }
            public static string Run() {
                foreach (string mode in new[] { null, "in_person", "npc_story" }) {
                    var screen = Open(mode);
                    Check(screen.SendButtonText == "Approach", "missing initial Approach");
                    screen.InputText = "  I will speak first.  ";
                    screen.ActionDraft = new Draft();
                    Check(screen.SendButtonText == "Send", "typing did not change the label");
                    screen.Send();
                    Check(screen.Sends == 1 && screen.Openings == 0 && screen.SentText == "  I will speak first.  "
                        && screen.SentAction != null && screen.SentAction != screen.ActionDraft, "typed Send changed the turn");
                    screen.Send();
                    Check(screen.SendButtonText == "Send" && screen.Sends == 1 && screen.Openings == 0, "empty follow-up did not stay idle");
                }
                foreach (string blank in new[] { "", "  ", "\u2003" }) {
                    var screen = Open();
                    screen.InputText = blank; screen.Send(); screen.Send();
                    Check(screen.Openings == 1 && screen.Sends == 0 && screen.SendButtonText == "Send", "empty Approach sent text or ran twice");
                }
                var retry = Open();
                retry._failedTurn.HasPending = true; retry._recoverableInputs.Add("old draft"); retry.Send();
                Check(retry.SendButtonText == "Retry" && retry.SentId == "original" && retry.SentSceneTurnId == "original-turn"
                    && retry.SentText == "saved failed message" && retry.Openings == 0 && retry.Recoveries == 0, "Retry lost its priority or identity");
                var recover = Open();
                recover._recoverableInputs.Add("old draft");
                Check(recover.SendButtonText == "Recover", "Recover was hidden");
                recover.Send();
                Check(recover.Recoveries == 1 && recover.Sends == 0 && recover.Openings == 0 && recover.SendButtonText == "Send", "Recover dispatched dialogue");
                var invitation = new IndividualOpeningProbe();
                invitation._conversationContext["approachOpening"] = true;
                Check(invitation.InitializePresentationAsync().GetAwaiter().GetResult() && invitation.History == 1
                    && invitation.Sends == 1 && invitation.SentText == "" && invitation.Openings == 0, "accepted invitation lost its opening");
                var busy = Open();
                busy.IsBusy = true; busy.Send();
                Check(busy.Sends == 0 && busy.Openings == 0 && busy.SendButtonText == "Send", "busy composer dispatched");
                var continued = Open();
                continued._conversationContext["continuationReason"] = "resume";
                continued.Send();
                Check(continued.Sends == 0 && continued.Openings == 0, "a continuation requested a new opening");
                ReignNpcLivesClient.NpcLivesOptions.Enabled = false;
                var disabled = Open(); disabled.Send();
                Check(disabled.Sends == 0 && disabled.Openings == 0 && disabled.SendButtonText == "Send", "disabled Approach dispatched");
                return "Individual Chat opening routes passed.";
            }
        }
        """;
}
