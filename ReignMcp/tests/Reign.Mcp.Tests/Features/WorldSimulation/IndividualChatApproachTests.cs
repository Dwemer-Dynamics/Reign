using System.Diagnostics;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class IndividualChatApproachTests
{
    [Fact]
    public async Task ActualCommandsRespectWhoInitiatedTheScene()
    {
        if (!OperatingSystem.IsWindows()) return;

        string workspace = TestOptions.FindWorkspace();
        string viewModels = Path.Combine(workspace, "ReignBeta/src/Modules/Dialogue/UI/ViewModels");
        string screen = File.ReadAllText(Path.Combine(viewModels, "ReignIndividualChatScreenVM.cs"));
        string retry = File.ReadAllText(Path.Combine(viewModels, "ReignIndividualChatRetry.cs"));
        string socialEvents = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/WorldSimulation/UI/ViewModels/ReignSocialEventScreenVM.cs"));
        const string sendSignature = "public async void ExecuteSend()";
        string methods = Slice(screen, sendSignature, "public Task<ReignDialogueReply> SendAutomationLineAsync(")
                .Replace(sendSignature, "public async Task ExecuteSend()", StringComparison.Ordinal)
            + Slice(screen, "private async Task<bool> InitializePresentationAsync()", "private async Task PrepareLifeOpeningAsync(")
            + Slice(retry, "public string SendButtonText", "private bool TryRecoverSavedInput()");
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "individual-chat-opening-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "probe.cs"), Probe.Replace("/* PRODUCTION */", methods, StringComparison.Ordinal)
            .Replace("/* EVENT INITIALIZATION */", Slice(socialEvents, "private async Task InitializeSocialEventAsync()",
                "public MBBindingList<ReignSocialEventAttendeeVM> Attendees"), StringComparison.Ordinal));
        string script = Path.Combine(evidence, "probe.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            Add-Type -Path (Join-Path $PSScriptRoot 'probe.cs') -WarningAction SilentlyContinue
            [IndividualOpeningProbe]::Run()
            [EventOpeningProbe]::Run()
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
        string receipt = await output;
        Assert.Contains("Individual Chat opening routes passed.", receipt);
        Assert.Contains("Social event opening routes passed.", receipt);
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
        using System.Linq;
        using System.Threading.Tasks;
        public sealed class Draft { }
        namespace ReignBeta.Actions { public static class ReignActionService { public static Draft Copy(Draft draft) => new Draft(); } }
        public static class ReignLog { public static void Warn(string text) { } }
        public static class ReignMainThread {
            public static Task InvokeAsync(Action run) { run(); return Task.CompletedTask; }
            public static Task<T> InvokeAsync<T>(Func<T> run) => Task.FromResult(run());
        }
        public static class ReignNpcLivesClient { public static Options NpcLivesOptions = new Options(); }
        public sealed class Options { public bool Enabled = true; public bool Allows(string option) => option == "approach" && Enabled; }
        public sealed class Context : Dictionary<string, object> {
            public new Context this[string key] {
                get => TryGetValue(key, out object value) ? value as Context : null;
                set => base[key] = value;
            }
            public T Value<T>(string key) => TryGetValue(key, out object value) ? (T)value : default(T);
            public void Set(string key, object value) => base[key] = value;
        }
        public sealed class LoadFence { public bool Current = true; public bool IsCurrent() => Current; }
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
            public bool _presentationInitializing = true, _initializePresentation = true, _conversationFinishRequested;
            public bool OpeningSucceeds = true;
            public LoadFence _loadFence = new LoadFence();
            public bool IsOutfitVisible, IsOutfitInputVisible, IsBusy, IsPregnancyWarningVisible;
            public bool InputEnabled => !_presentationInitializing && !_isFinalized && !IsBusy && !IsOutfitVisible && !IsPregnancyWarningVisible;
            public string InputText = "", BusyText, SentText, SentId, SentSceneTurnId, SentRecover;
            public Draft ActionDraft, _failedActionDraft, SentAction;
            public string _failedRecoveredInput;
            public ReignRecoveredDraftReview _recoveredDraftReview = new ReignRecoveredDraftReview();
            public Context _conversationContext = new Context();
            public List<string> _recoverableInputs = new List<string>();
            public FailedTurn _failedTurn = new FailedTurn();
            public int Sessions, History, Openings, Sends, Outfit, Recoveries;
            public void ExecuteSubmitOutfit() { Outfit++; }
            public void RefreshInputAvailability() { }
            public Task<string> EnsureConversationSessionAsync() { Sessions++; return Task.FromResult("session"); }
            public Task LoadHistoryAsync() { Check(Sessions == 1, "history loaded before session confirmation"); History++; return Task.CompletedTask; }
            public Task RequestLifeOpeningAsync() {
                Openings++;
                if (OpeningSucceeds) _firstMoveChosen = true;
                return Task.CompletedTask;
            }
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
                if (OpeningSucceeds && IsNpcInvitationOpening(text)) {
                    _conversationContext.Set("approachOpening", false);
                    _conversationContext.Set("rescueOpening", false);
                }
                if (clear) InputText = "";
                return Task.CompletedTask;
            }
            /* PRODUCTION */
            private static void Check(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
            private void Send() => ExecuteSend().GetAwaiter().GetResult();
            private static IndividualOpeningProbe Open(string mode = null) {
                var screen = new IndividualOpeningProbe();
                if (mode != null) screen._conversationContext.Set("conversationMode", mode);
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
                var invitation = new IndividualOpeningProbe { OpeningSucceeds = false };
                invitation._conversationContext.Set("approachOpening", true);
                invitation._conversationContext.Set("approachRequestId", "accepted-jealousy-request");
                Check(invitation.InitializePresentationAsync().GetAwaiter().GetResult() && invitation.History == 1
                    && invitation.Sends == 1 && invitation.SentText == "" && invitation.Openings == 0, "accepted jealousy invitation lost its opening");
                invitation.InputText = "a later player message";
                invitation._failedTurn.HasPending = true;
                Check(invitation.SendButtonText == "Retry", "player text preempted the NPC's retry");
                invitation.OpeningSucceeds = true;
                invitation.Send();
                Check(invitation.Sends == 2 && invitation.SentText == "" && invitation.SentId == "original"
                    && invitation.SentSceneTurnId == "original-turn" && invitation.InputText == "a later player message",
                    "jealousy retry changed the original turn or swallowed the draft");
                invitation._failedTurn.HasPending = false;
                invitation.Send();
                Check(invitation.Sends == 3 && invitation.SentText == "a later player message",
                    "accepted opening prevented ordinary follow-up");

                var rescue = new IndividualOpeningProbe();
                var evidence = new Context(); evidence.Set("eventId", "accepted-rescue");
                rescue._conversationContext.Set("rescueOpening", true);
                rescue._conversationContext["rescueContext"] = evidence;
                Check(rescue.InitializePresentationAsync().GetAwaiter().GetResult() && rescue.Sends == 1
                    && rescue.SentText == "" && rescue.Openings == 0, "rescue invitation lost its opening");

                foreach (string request in new[] { "npc-story-invitation", "npc-leave-request" }) {
                    ReignNpcLivesClient.NpcLivesOptions.Enabled = false;
                    var life = new IndividualOpeningProbe { OpeningSucceeds = false };
                    life._conversationContext.Set("npcLifeInvitationEntry", true);
                    life._conversationContext.Set("npcLifeRequestId", request);
                    Check(life.InitializePresentationAsync().GetAwaiter().GetResult() && life.Openings == 1
                        && life.Sends == 0 && life.SendButtonText == "Retry", "accepted personal invitation depends on optional Approach");
                    life.InputText = "my reply after listening";
                    life.OpeningSucceeds = true;
                    life.Send();
                    Check(life.Openings == 2 && life.Sends == 0 && life.InputText == "my reply after listening",
                        "a pending personal invitation consumed the draft");
                    life.Send();
                    Check(life.Sends == 1 && life.Openings == 2 && life.SentText == "my reply after listening",
                        "personal invitation ran again on the first follow-up");
                    ReignNpcLivesClient.NpcLivesOptions.Enabled = true;
                }

                var ordinaryOffer = new IndividualOpeningProbe();
                ordinaryOffer._conversationContext.Set("npcLifeRequestId", "context-only");
                Check(ordinaryOffer.InitializePresentationAsync().GetAwaiter().GetResult() && ordinaryOffer.Openings == 0,
                    "context-only life offer changed an ordinary chat's first move");
                var missingAuthority = new IndividualOpeningProbe();
                missingAuthority._conversationContext.Set("approachOpening", true);
                Check(missingAuthority.InitializePresentationAsync().GetAwaiter().GetResult()
                    && missingAuthority.Sends == 0 && missingAuthority.Openings == 0, "an opening flag alone generated dialogue");
                var stale = new IndividualOpeningProbe();
                stale._conversationContext.Set("npcLifeInvitationEntry", true);
                stale._conversationContext.Set("npcLifeRequestId", "previous-campaign");
                stale._loadFence.Current = false;
                Check(stale.InitializePresentationAsync().GetAwaiter().GetResult() && stale.Openings == 0,
                    "a stale campaign dispatched its opening");
                var busy = Open();
                busy.IsBusy = true; busy.Send();
                Check(busy.Sends == 0 && busy.Openings == 0 && busy.SendButtonText == "Send", "busy composer dispatched");
                var continued = Open();
                continued._conversationContext.Set("continuationReason", "resume");
                continued.Send();
                Check(continued.Sends == 0 && continued.Openings == 0, "a continuation requested a new opening");
                ReignNpcLivesClient.NpcLivesOptions.Enabled = false;
                var disabled = Open(); disabled.Send();
                Check(disabled.Sends == 0 && disabled.Openings == 0 && disabled.SendButtonText == "Send", "disabled Approach dispatched");
                return "Individual Chat opening routes passed.";
            }
        }
        public sealed class Hero {
            public static Hero MainHero = new Hero();
            public string StringId, Name;
        }
        public enum SocialEventStatus { Attended, Resolved }
        public sealed class EventRecord {
            public string GeneratedInitiatorHeroStringId;
            public bool IsGeneratedWildernessEvent;
            public SocialEventStatus Status;
        }
        public sealed class ReignEventReply { public bool Ok; public string Text; }
        public sealed class EventSession {
            public EventRecord Record = new EventRecord();
            public Task<ReignEventReply> InitiatorOpening;
            public List<Hero> Heroes = new List<Hero>();
            public TaskCompletionSource<bool> StartResult = new TaskCompletionSource<bool>();
            public TaskCompletionSource<ReignEventReply> Response = new TaskCompletionSource<ReignEventReply>();
            public int Requests, Transcripts;
            public IEnumerable<Hero> ActiveHeroes() => Heroes;
            public void RecordTranscriptLine(string speaker, string text) { Transcripts++; }
        }
        public static class ReignServerClient {
            public static Task<bool> StartSocialEventAsync(EventSession session) => session.StartResult.Task;
            public static Task<ReignEventReply> RequestSocialEventResponseAsync(EventSession session, Hero speaker,
                string text, bool approach, Func<bool> current) {
                if (!current() || !approach || text != "" || speaker.StringId != session.Record.GeneratedInitiatorHeroStringId)
                    throw new InvalidOperationException("invalid automatic event request");
                session.Requests++;
                return session.Response.Task;
            }
        }
        public sealed class EventOpeningProbe {
            private sealed class SocialTurnOwner {
                internal LoadFence Fence = new LoadFence();
                internal Hero Player = Hero.MainHero;
            }
            private readonly EventSession _session;
            private EventOpeningProbe(EventSession session) { _session = session; }
            private bool IsBusy, Closed;
            private string BusyText, PresentedLine;
            private SocialTurnOwner CaptureSocialOwner() => new SocialTurnOwner();
            private bool IsSocialTurnCurrent(SocialTurnOwner owner) => !Closed;
            private void AddSystemLine(string text) { }
            private async Task RequestRepliesAsync(List<Hero> speakers, string playerText, bool approachOpening, SocialTurnOwner owner,
                Task<ReignEventReply> initialSceneOpening) {
                if (!IsBusy || BusyText.Length == 0 || !approachOpening || playerText != "" || speakers.Count != 1
                    || speakers[0].StringId != _session.Record.GeneratedInitiatorHeroStringId)
                    throw new InvalidOperationException("automatic event opening included player input or used the wrong speaker");
                var reply = await initialSceneOpening;
                if (IsSocialTurnCurrent(owner)) { PresentedLine = reply.Text; IsBusy = false; }
            }
            /* EVENT INITIALIZATION */
            public static string Run() {
                var scene = new EventSession();
                scene.Record.IsGeneratedWildernessEvent = true;
                scene.Record.GeneratedInitiatorHeroStringId = "initiator";
                scene.Heroes.Add(new Hero { StringId = "bystander" });
                scene.Heroes.Add(new Hero { StringId = "initiator", Name = "Initiator" });
                var view = new EventOpeningProbe(scene);
                Task startup = view.InitializeSocialEventAsync();
                if (!view.IsBusy || scene.Requests != 0) throw new InvalidOperationException("event did not wait for its start receipt");
                scene.StartResult.SetResult(true);
                if (scene.Requests != 1) throw new InvalidOperationException("the initiating NPC did not open automatically");

                view.Closed = true;
                var reopened = new EventOpeningProbe(scene);
                Task replacement = reopened.InitializeSocialEventAsync();
                if (scene.Requests != 1 || !reopened.IsBusy)
                    throw new InvalidOperationException("reopening did not join the original opening");
                scene.Response.SetResult(new ReignEventReply { Ok = true, Text = "the opening" });
                Task.WhenAll(startup, replacement).GetAwaiter().GetResult();
                if (scene.Requests != 1 || scene.Transcripts != 1 || view.PresentedLine != null || reopened.PresentedLine != "the opening")
                    throw new InvalidOperationException("reopening regenerated, exposed to the stale view, or dropped the accepted opening");

                foreach (bool generated in new[] { false, true }) {
                    var manual = new EventSession();
                    manual.Record.IsGeneratedWildernessEvent = generated;
                    manual.Record.GeneratedInitiatorHeroStringId = generated ? "absent" : "initiator";
                    manual.Heroes.Add(scene.Heroes[1]);
                    manual.StartResult.SetResult(true);
                    var idle = new EventOpeningProbe(manual);
                    idle.InitializeSocialEventAsync().GetAwaiter().GetResult();
                    if (manual.Requests != 0 || idle.IsBusy) throw new InvalidOperationException("an event without its initiator opened");
                }
                var closedScene = new EventSession();
                closedScene.Record.IsGeneratedWildernessEvent = true;
                closedScene.Record.GeneratedInitiatorHeroStringId = "initiator";
                closedScene.Heroes.Add(scene.Heroes[1]);
                var closed = new EventOpeningProbe(closedScene);
                Task pending = closed.InitializeSocialEventAsync();
                closed.Closed = true;
                closedScene.StartResult.SetResult(true);
                pending.GetAwaiter().GetResult();
                if (closedScene.Requests != 0 || closedScene.InitiatorOpening != null)
                    throw new InvalidOperationException("a closed view dispatched or consumed the opening");
                return "Social event opening routes passed.";
            }
        }
        """;
}
