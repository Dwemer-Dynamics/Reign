using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ArrestDailyReviewOwnershipTests
{
    [WindowsFact]
    public async Task Daily_arrest_review_retries_the_same_day_after_delayed_reply_loses_ownership()
    {
        string workspace = TestOptions.FindWorkspace();
        string arrests = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/WorldSimulation/Arrests/ReignArrestCampaignBehavior.cs"));
        string evidenceParser = Path.Combine(workspace,
            "ReignBeta/src/Modules/WorldSimulation/Arrests/ReignArrestEvidenceResponse.cs");
        string client = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Platform/Integration/ReignServerClient.cs"));
        string daily = Slice(arrests, "private void OnDailyTick()", "private void ContinueDailyArrestMaintenance(");
        string response = Slice(arrests, "private static JObject BuildEvidencePayload(",
            "private static void LogEvidenceReviewFailure(");
        string fence = Slice(client, "internal sealed class RequestFence", "internal static ReignWorldActionRecord ParseActionRecord(");
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "arrest-daily-review-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "probe.cs"),
            Harness.Replace("/* PRODUCTION_DAILY */", daily).Replace("/* PRODUCTION_FENCE */", fence)
                .Replace("/* PRODUCTION_RESPONSE */", response));
        File.Copy(evidenceParser, Path.Combine(evidence, "ReignArrestEvidenceResponse.cs"));
        string script = Path.Combine(evidence, "probe.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            $sources = @((Join-Path $PSScriptRoot 'probe.cs'), (Join-Path $PSScriptRoot 'ReignArrestEvidenceResponse.cs'))
            $references = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' -File).FullName)
            $references += (Join-Path $PSHOME 'Newtonsoft.Json.dll')
            Add-Type -Path $sources -ReferencedAssemblies $references -CompilerOptions '/nowarn:CS1701' -WarningAction SilentlyContinue
            $results = [ArrestReviewProbe]::RunAsync().GetAwaiter().GetResult()
            [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'report.json'), (ConvertTo-Json -InputObject @($results)))
            """);
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated arrest probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Arrest daily review probe timed out: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.Equal(new[] { "alignment-start", "alignment-reply", "load-reply", "generation-reply",
            "point-reply", "save-sync-reply", "legacy-reply" },
            report.RootElement.EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    private static string Slice(string source, string opening, string next)
    {
        int start = source.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing production opening: " + opening);
        int end = source.IndexOf(next, start, StringComparison.Ordinal);
        Assert.True(end > start, "Missing production end: " + next);
        return source[start..end];
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Isolated PowerShell source execution requires Windows.";
        }
    }

    // The actual daily loop and fence run with controlled native state and an HTTP reply gate.
    // The transport deliberately returns an old response so the callback guard is tested independently.
    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Newtonsoft.Json.Linq;
        using ReignBeta.WorldSimulation.Arrests;

        namespace TaleWorlds.CampaignSystem { public static class Campaign { public static object Current = new object(); } }
        public sealed class Hero { public static Hero MainHero = new Hero("player"); public readonly string StringId; public Hero(string id) { StringId = id; } }
        public enum ReignArrestPhase { Captured, Proposed }
        public sealed class ReignArrestCase {
            public string CaseId = "case", AccusedHeroStringId = "accused", PlayerHeroStringId = "player", ChargeCategory = "espionage", RawAccusation = "probe";
            public string EvidenceStatus = "unsupported", EvidenceIdsCsv = string.Empty;
            public string DailyLateEvidenceIntentJson = string.Empty;
            public ReignArrestPhase Phase = ReignArrestPhase.Captured;
            public bool AccusationActive = true, IsOpen = true, CauseEstablished;
        }
        public static class ReignArrestCustodyPatches { public static void ClearExpiredSovereigntyReleases() { } }
        public static class ReignCampaignInitializationGate { public static bool IsPending => false; public static JObject Snapshot() => new JObject { ["generationId"] = "initialized" }; }
        public static class ReignSaveSyncCoordinator { public static bool IsAlignmentPending; public static int CampaignGeneration; }
        public sealed class HistoryState { public string TimelineId = "main", SharedActivityGeneration = "original"; }
        public sealed class SaveSyncState { public string CurrentSavePointId = "original"; }
        public static class ReignServices {
            public static readonly HistoryState WorldHistory = new HistoryState();
            public static SaveSyncState SaveSync = new SaveSyncState();
        }
        public static class ReignLog { public static void Warn(string message) { throw new InvalidOperationException(message); } }
        public sealed class ReignRequestOwnerAbandonedException : Exception { }

        public static class ReignMainThread {
            public static Action Dispatched;
            public static Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
            public static Task InvokeAsync(Action action) { action(); Dispatched?.Invoke(); return Task.CompletedTask; }
        }
        public static class ReignServerClient {
            public static TaskCompletionSource<JObject> Sent;
            public static TaskCompletionSource<JObject> Reply;
            public static string GetCampaignId() => "isolated";
            public static string ResponseOrigin(JObject response) => "http://127.0.0.1:8089";
            public static void Prepare() {
                Sent = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                Reply = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            public static Task<JObject> PostOwnedJsonAsync(string route, JObject payload, Func<bool> owner) {
                if (!owner()) throw new ReignRequestOwnerAbandonedException();
                if (route != "/arrests/evidence/evaluate") throw new InvalidOperationException("Unexpected evidence route.");
                Sent.SetResult((JObject)payload.DeepClone());
                return Reply.Task;
            }
            /* PRODUCTION_FENCE */
        }
        public sealed class ArrestReviewProbe {
            private sealed class CapturedRepairWork { }
            public static ArrestReviewProbe Instance;
            private readonly List<ReignArrestCase> _cases = new List<ReignArrestCase> { new ReignArrestCase() };
            private readonly Hero accused = new Hero("accused");
            private const float Day = 12f;
            private float _lastEvidenceReviewDay = -1000f;
            private ReignServerClient.RequestFence _dailyEvidenceReview;
            private int maintenance;
            private int ownedReviews;

            private static float CurrentDay() => Day;
            private Hero FindHero(string id) => id == accused.StringId ? accused : null;
            private static bool MatchesEvidenceSubject(ReignArrestCase item, Hero hero) => item.AccusedHeroStringId == hero?.StringId;
            private static bool NeedsCapturedRepair(ReignArrestCase item) => false;
            private static CapturedRepairWork CaptureDailyCapturedRepair(ReignServerClient.RequestFence fence, Func<bool> ownsPoint, ReignArrestCase item, Hero hero) => throw new InvalidOperationException();
            private static Task<bool> ReviewCapturedRepairAsync(CapturedRepairWork repair) => throw new InvalidOperationException();
            private static bool TryGetLocalEvidence(ReignArrestCase item, Hero hero, out string evidence) { evidence = string.Empty; return false; }
            private static void LogEvidenceReviewFailure(string id, Exception error) => throw new InvalidOperationException(id, error);
            private void ContinueDailyArrestMaintenance(ReignArrestCase item, Hero hero, bool reclassify) { maintenance++; }
            // The source probe exercises production dispatch. Native journal and PostgreSQL receipt tests own delivery.
            private static void RecordOwnedLateEvidence(ReignArrestCase item, Hero hero, string evidence, string origin = null)
            {
                Check(origin == "http://127.0.0.1:8089", "the captured remote result retains its answering origin");
                item.DailyLateEvidenceIntentJson = evidence;
            }
            private Task<bool> ReviewOwnedLateEvidenceAsync(ReignArrestCase item, Func<bool> ownsPoint)
            {
                Check(ownsPoint(), "pending late evidence resumes only under the current review owner");
                ownedReviews++;
                return Task.FromResult(false);
            }
            /* PRODUCTION_DAILY */
            /* PRODUCTION_RESPONSE */

            private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
            private static JObject EspionageReply(JObject request)
            {
                Check(request.Value<string>("schema") == "reign-arrest-evidence-request-v2"
                    && request.Value<string>("campaignId") == "isolated" && request.Value<string>("timelineId") == "main"
                    && request.Value<string>("chargeCategory") == "espionage" && request.Value<string>("accusedHeroId") == "accused",
                    "the daily request must carry exact v2 scope");
                const string evidenceId = "foreign_agent:accused:sponsor";
                return new JObject
                {
                    ["schema"] = "reign-arrest-evidence-response-v2", ["campaignId"] = request["campaignId"],
                    ["timelineId"] = request["timelineId"], ["chargeCategory"] = request["chargeCategory"],
                    ["accusedHeroId"] = request["accusedHeroId"], ["ok"] = true, ["causeEstablished"] = true,
                    ["evidenceStatus"] = "established", ["hiddenAgentInformationRevealed"] = false,
                    ["evidenceIds"] = new JArray(evidenceId),
                    ["evidence"] = new JArray(new JObject
                    {
                        ["kind"] = "foreign_agent_exposure", ["subjectRole"] = "agent",
                        ["subjectHeroId"] = request["accusedHeroId"], ["evidenceId"] = evidenceId
                    })
                };
            }

            private Task<bool> StartDailyReview()
            {
                ReignServerClient.Prepare();
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ReignMainThread.Dispatched = () => { if (_dailyEvidenceReview == null) completed.TrySetResult(true); };
                OnDailyTick();
                return completed.Task;
            }

            public static async Task<string[]> RunAsync()
            {
                var passed = new List<string>();
                ReignSaveSyncCoordinator.IsAlignmentPending = true;
                ReignServerClient.Prepare();
                var blocked = new ArrestReviewProbe();
                Instance = blocked;
                blocked.OnDailyTick();
                Check(blocked._dailyEvidenceReview == null && !ReignServerClient.Sent.Task.IsCompleted, "alignment cannot admit an open-case review");
                blocked._cases.Clear();
                blocked.OnDailyTick();
                Check(blocked._lastEvidenceReviewDay < Day, "alignment cannot consume the empty-case review day");
                passed.Add("alignment-start");

                foreach (string change in new[] { "alignment", "load", "generation", "point", "save-sync" })
                {
                    ReignSaveSyncCoordinator.IsAlignmentPending = false;
                    var owner = new ArrestReviewProbe();
                    Instance = owner;
                    Task oldReview = owner.StartDailyReview();
                    JObject oldRequest = await ReignServerClient.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!oldReview.IsCompleted && owner.maintenance == 0, "the remote reply must still be pending");
                    if (change == "alignment") ReignSaveSyncCoordinator.IsAlignmentPending = true;
                    else if (change == "load") ReignSaveSyncCoordinator.CampaignGeneration++;
                    else if (change == "generation") ReignServices.WorldHistory.SharedActivityGeneration = "replacement";
                    else if (change == "point")
                    {
                        var active = owner._dailyEvidenceReview;
                        ReignServices.SaveSync.CurrentSavePointId = "replacement";
                        owner.OnDailyTick();
                        Check(ReferenceEquals(active, owner._dailyEvidenceReview), "point rollover cannot start a second daily review");
                    }
                    else ReignServices.SaveSync = new SaveSyncState { CurrentSavePointId = ReignServices.SaveSync.CurrentSavePointId };
                    ReignServerClient.Reply.SetResult(EspionageReply(oldRequest));
                    await oldReview.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(owner._dailyEvidenceReview == null && owner._lastEvidenceReviewDay < Day
                        && owner.maintenance == 0 && !owner._cases[0].CauseEstablished
                        && owner._cases[0].EvidenceStatus == "unsupported" && owner._cases[0].EvidenceIdsCsv == string.Empty
                        && owner._cases[0].DailyLateEvidenceIntentJson == string.Empty && owner.ownedReviews == 0,
                        "a stale reply cannot mutate the case, run maintenance or consume the open day");

                    ReignSaveSyncCoordinator.IsAlignmentPending = false;
                    Task retry = owner.StartDailyReview();
                    JObject retryRequest = await ReignServerClient.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    ReignServerClient.Reply.SetResult(EspionageReply(retryRequest));
                    await retry.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(owner._dailyEvidenceReview == null && owner._lastEvidenceReviewDay < Day
                        && owner.maintenance == 0 && !owner._cases[0].CauseEstablished
                        && owner._cases[0].DailyLateEvidenceIntentJson == "foreign_agent:accused:sponsor" && owner.ownedReviews == 1,
                        "the same day starts one owned transition and remains unfinished while delivery is pending");
                    await owner.StartDailyReview().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(owner.ownedReviews == 2 && !ReignServerClient.Sent.Task.IsCompleted,
                        "the pending intent resumes without requesting evidence twice");
                    passed.Add(change + "-reply");
                }
                var legacy = new ArrestReviewProbe();
                legacy._cases[0].Phase = ReignArrestPhase.Proposed;
                Instance = legacy;
                Task legacyReview = legacy.StartDailyReview();
                JObject legacyRequest = await ReignServerClient.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ReignServerClient.Reply.SetResult(EspionageReply(legacyRequest));
                await legacyReview.WaitAsync(TimeSpan.FromSeconds(5));
                Check(legacy._lastEvidenceReviewDay == Day && legacy.maintenance == 1 && legacy.ownedReviews == 0
                    && legacy._cases[0].CauseEstablished && legacy._cases[0].EvidenceIdsCsv == "foreign_agent:accused:sponsor",
                    "a noncaptured case keeps its direct evidence path");
                passed.Add("legacy-reply");
                return passed.ToArray();
            }
        }
        """;
}
