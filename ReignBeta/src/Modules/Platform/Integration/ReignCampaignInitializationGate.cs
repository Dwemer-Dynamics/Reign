using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    internal static class ReignCampaignInitializationGate
    {
        private static readonly object Gate = new object();
        private static readonly AsyncLocal<string> InitializationRequestGeneration =
            new AsyncLocal<string>();
        private static TaskCompletionSource<bool> _ready = ReadySource(false);
        private static bool _pending = true;
        private static string _generationId = string.Empty;
        private static ReignInitializationAuthorization _authorization;
        private static string _stage = "campaign_start";
        private static int _completed;
        private static int _total;
        private static DateTime _startedUtc = DateTime.UtcNow;
        private static DateTime? _completedUtc;

        internal static bool IsPending
        {
            get
            {
                lock (Gate) return _pending;
            }
        }

        internal static bool IsInitializationRequestScopeActive
        {
            get
            {
                lock (Gate)
                {
                    return _pending
                        && !string.IsNullOrWhiteSpace(InitializationRequestGeneration.Value)
                        && string.Equals(
                            _generationId,
                            InitializationRequestGeneration.Value,
                            StringComparison.Ordinal);
                }
            }
        }

        internal static bool IsActiveGeneration(string generationId)
        {
            lock (Gate)
            {
                return _pending
                    && string.Equals(_generationId, generationId, StringComparison.Ordinal);
            }
        }

        internal static void BeginGeneration(string generationId)
        {
            if (string.IsNullOrWhiteSpace(generationId))
                throw new ArgumentException("Generation id is required.", nameof(generationId));
            TaskCompletionSource<bool> previous;
            lock (Gate)
            {
                previous = _ready;
                _pending = true;
                _generationId = generationId;
                _authorization = ReignInitializationAuthorization.Begin(generationId);
                _stage = "campaign_start";
                _completed = 0;
                _total = 0;
                _startedUtc = DateTime.UtcNow;
                _completedUtc = null;
                _ready = ReadySource(false);
            }
            previous.TrySetCanceled();
        }

        internal static void ResetForCampaign()
        {
            BeginGeneration(Guid.NewGuid().ToString("N"));
        }

        internal static void BeginProfileGeneration(int total)
        {
            lock (Gate)
            {
                if (!_pending)
                {
                    _pending = true;
                    _ready = ReadySource(false);
                    _startedUtc = DateTime.UtcNow;
                    _completedUtc = null;
                }
                _stage = "assigning_profiles";
                _completed = 0;
                _total = Math.Max(0, total);
            }
        }

        internal static void ReportProgress(string stage, int completed, int total)
        {
            lock (Gate)
            {
                if (!_pending) return;
                _stage = string.IsNullOrWhiteSpace(stage) ? _stage : stage;
                _completed = Math.Max(0, completed);
                _total = Math.Max(_completed, Math.Max(0, total));
            }
        }

        internal static bool TryCreateReleaseToken(
            string generationId,
            ReignCampaignReadinessState state,
            out ReignInitializationReleaseToken token)
        {
            lock (Gate)
            {
                if (!_pending
                    || state == null
                    || !state.CanRelease
                    || !string.Equals(_generationId, generationId, StringComparison.Ordinal)
                    || _authorization == null)
                {
                    token = default(ReignInitializationReleaseToken);
                    return false;
                }
                token = _authorization.IssueReleaseToken(generationId);
                return token.IsIssued;
            }
        }

        internal static bool TrySealAndMarkReady(
            string generationId,
            ReignInitializationReleaseToken token)
        {
            TaskCompletionSource<bool> ready;
            lock (Gate)
            {
                if (!_pending
                    || _authorization == null
                    || !_authorization.CanRelease(generationId, token))
                    return false;
                _pending = false;
                _stage = "ready";
                _completed = _total;
                _completedUtc = DateTime.UtcNow;
                ready = _ready;
            }
            ready.TrySetResult(true);
            return true;
        }

        internal static async Task<T> RunInitializationRequestAsync<T>(
            string generationId,
            Func<Task<T>> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (Gate)
            {
                if (!_pending
                    || !string.Equals(_generationId, generationId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The campaign initialization generation is no longer active.");
            }

            string previous = InitializationRequestGeneration.Value;
            InitializationRequestGeneration.Value = generationId;
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                InitializationRequestGeneration.Value = previous;
            }
        }

        internal static async Task RunInitializationRequestAsync(
            string generationId,
            Func<Task> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            await RunInitializationRequestAsync(
                generationId,
                async () =>
                {
                    await action().ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
        }

        internal static async Task WaitForReadyAsync(string route)
        {
            if (IsInitializationControlRoute(route)) return;
            Task task;
            lock (Gate)
            {
                if (_pending
                    && !string.IsNullOrWhiteSpace(InitializationRequestGeneration.Value)
                    && string.Equals(
                        _generationId,
                        InitializationRequestGeneration.Value,
                        StringComparison.Ordinal))
                    return;
                task = _ready.Task;
            }
            await task.ConfigureAwait(false);
        }

        internal static void RunWhenReadyOnMainThread(Action action)
        {
            if (action == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await WaitForReadyAsync("deferred_campaign_action").ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(action).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Campaign initialization readiness action failed: " + ex.Message);
                }
            });
        }

        internal static JObject Snapshot()
        {
            lock (Gate)
            {
                return new JObject
                {
                    ["pending"] = _pending,
                    ["generationId"] = _generationId,
                    ["stage"] = _stage,
                    ["completed"] = _completed,
                    ["total"] = _total,
                    ["elapsedMs"] = Math.Max(
                        0L,
                        (long)((_completedUtc ?? DateTime.UtcNow) - _startedUtc)
                            .TotalMilliseconds),
                    ["policy"] = "campaign_day_1_before_reign_systems"
                };
            }
        }

        private static bool IsInitializationControlRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return false;
            return route.IndexOf("save-sync", StringComparison.OrdinalIgnoreCase) >= 0
                || route.StartsWith("/tests/live/game/", StringComparison.OrdinalIgnoreCase);
        }

        private static TaskCompletionSource<bool> ReadySource(bool completed)
        {
            TaskCompletionSource<bool> source =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed) source.TrySetResult(true);
            return source;
        }
    }
}
