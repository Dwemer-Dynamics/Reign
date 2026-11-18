using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ReignBeta.Platform.Integration
{
    public static class ReignMainThread
    {
        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static int _mainThreadId;
        private static int _pendingCount;
        private static long _lastDrainUtcTicks;
        private static long _lastExecutedUtcTicks;
        private static long _executedCount;
        private static long _slowItemCount;
        private static long _lastSlowItemMicroseconds;
        private static string _lastSlowItemSource = string.Empty;
        private static long _lastSlowLogUtcTicks;

        // Scoped work shares Work's FIFO and drain. A slot is held until its
        // wrapper drains, including canceled tombstones, so the backlog stays
        // bounded across repeated game end/unload.
        private const int ScopedCapacity = 16;
        private static readonly object ScopedGate = new object();
        private static readonly ScopedEntry[] ScopedSlots = new ScopedEntry[ScopedCapacity];
        private static bool _scopedIngressOpen;
        private static GameScope _currentScope;
        private static object _currentScopeGame;

        internal enum ScopedAdmission
        {
            Accepted,
            Busy,
            Closed
        }

        // One campaign game activation, compared by reference. It holds no game
        // object, so workers may retain it.
        internal sealed class GameScope { }

        private sealed class ScopedEntry
        {
            internal int Slot;
            internal Func<bool> Owner;
            internal Action Action;
            internal TaskCompletionSource<bool> Completion; // Null once claimed or tombstoned.

            internal void Run() { RunScoped(this); }
        }

        public static bool IsMainThread
        {
            get { return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        public static int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));
        public static long ExecutedCount => Interlocked.Read(ref _executedCount);
        public static DateTime LastDrainUtc => ReadUtc(ref _lastDrainUtcTicks);
        public static DateTime LastExecutedUtc => ReadUtc(ref _lastExecutedUtcTicks);
        public static long SlowItemCount => Interlocked.Read(ref _slowItemCount);
        public static double LastSlowItemDurationMs => Interlocked.Read(ref _lastSlowItemMicroseconds) / 1000d;
        public static string LastSlowItemSource => Volatile.Read(ref _lastSlowItemSource);

        internal static GameScope CurrentGameScope
        {
            get { lock (ScopedGate) return _currentScope; }
        }

        private static T Execute<T>(Func<T> action)
        {
            long started = Stopwatch.GetTimestamp();
            try { return action(); }
            finally { RecordExecution(started, action); }
        }

        private static void Execute(Action action)
        {
            long started = Stopwatch.GetTimestamp();
            try { action(); }
            finally { RecordExecution(started, action); }
        }

        private static void RecordExecution(long started, Delegate action)
        {
            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            if (milliseconds >= 16d)
            {
                Interlocked.Increment(ref _slowItemCount);
                Interlocked.Exchange(ref _lastSlowItemMicroseconds, (long)(milliseconds * 1000d));
                try
                {
                    string source=(action.Method.DeclaringType?.FullName ?? "")+"."+action.Method.Name;
                    if(source.Length>192)source=source.Substring(0,192);
                    Volatile.Write(ref _lastSlowItemSource, source);
                    long now=DateTime.UtcNow.Ticks;
                    if(now-Interlocked.Read(ref _lastSlowLogUtcTicks)>=TimeSpan.TicksPerSecond*5)
                    {
                        Interlocked.Exchange(ref _lastSlowLogUtcTicks,now);
                        ReignLog.Warn("Main thread callback " + source + " took "
                            + milliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms.");
                    }
                }
                catch { /* Optional diagnostics preserve the callback's outcome. */ }
            }
        }

        public static void DrainOnMainThread()
        {
            using (ReignLog.MeasureWork("main-thread.dispatch", 0f))
            {
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                Interlocked.Exchange(ref _lastDrainUtcTicks, DateTime.UtcNow.Ticks);
                long budgetStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < 200 && Work.TryDequeue(out Action action); i++)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Main thread work item failed: " + ex.Message);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingCount);
                        Interlocked.Increment(ref _executedCount);
                        Interlocked.Exchange(ref _lastExecutedUtcTicks, DateTime.UtcNow.Ticks);
                    }
                    // Always execute the first item, preserve FIFO, then leave the
                    // rest queued once this frame has spent its 4ms dispatch budget.
                    if ((System.Diagnostics.Stopwatch.GetTimestamp() - budgetStarted) * 1000d
                        / System.Diagnostics.Stopwatch.Frequency >= 4d) break;
                }
            }
        }

        public static Task InvokeAsync(Action action)
        {
            if (action == null)
            {
                return Task.CompletedTask;
            }

            if (IsMainThread)
            {
                Execute(action);
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pendingCount);
            Work.Enqueue(() =>
            {
                try
                {
                    Execute(action);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public static Task<T> InvokeAsync<T>(Func<T> action)
        {
            if (action == null)
            {
                return Task.FromResult(default(T));
            }

            if (IsMainThread)
            {
                return Task.FromResult(Execute(action));
            }

            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pendingCount);
            Work.Enqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(Execute(action));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        internal static void OpenScopedIngress()
        {
            lock (ScopedGate) _scopedIngressOpen = true;
        }

        internal static void CloseScopedIngress()
        {
            TaskCompletionSource<bool>[] canceled;
            lock (ScopedGate)
            {
                _scopedIngressOpen = false;
                canceled = EndCurrentScopeLocked();
            }
            CancelOutsideGate(canceled);
        }

        // Game thread. A different Game whose predecessor never reported
        // OnGameEnd replaces and cancels that predecessor's scope.
        internal static void BeginGameScope(object game)
        {
            if (game == null) return;
            TaskCompletionSource<bool>[] canceled;
            lock (ScopedGate)
            {
                if (!_scopedIngressOpen || ReferenceEquals(_currentScopeGame, game)) return;
                canceled = EndCurrentScopeLocked();
                _currentScope = new GameScope();
                _currentScopeGame = game;
            }
            CancelOutsideGate(canceled);
        }

        internal static void EndGameScope(object game)
        {
            if (game == null) return;
            TaskCompletionSource<bool>[] canceled;
            lock (ScopedGate)
            {
                if (!ReferenceEquals(_currentScopeGame, game)) return;
                canceled = EndCurrentScopeLocked();
            }
            CancelOutsideGate(canceled);
        }

        // Always queues, including from the game thread. ownerIsCurrent runs on
        // the game thread immediately before action. A full registry returns
        // Busy without waiting. completion is null unless Accepted.
        internal static ScopedAdmission TryInvokeScoped(GameScope scope, Func<bool> ownerIsCurrent, Action action,
            out Task completion)
        {
            if (ownerIsCurrent == null) throw new ArgumentNullException(nameof(ownerIsCurrent));
            if (action == null) throw new ArgumentNullException(nameof(action));
            completion = null;
            lock (ScopedGate)
            {
                if (scope == null || !ReferenceEquals(scope, _currentScope)) return ScopedAdmission.Closed;
                int slot = Array.IndexOf(ScopedSlots, null);
                if (slot < 0) return ScopedAdmission.Busy;
                ScopedEntry entry = new ScopedEntry
                {
                    Slot = slot,
                    Owner = ownerIsCurrent,
                    Action = action,
                    Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                };
                ScopedSlots[slot] = entry;
                completion = entry.Completion.Task;
                Interlocked.Increment(ref _pendingCount);
                Work.Enqueue(entry.Run);
            }
            return ScopedAdmission.Accepted;
        }

        private static void RunScoped(ScopedEntry entry)
        {
            Func<bool> owner;
            Action action;
            TaskCompletionSource<bool> completion;
            lock (ScopedGate)
            {
                ScopedSlots[entry.Slot] = null;
                owner = entry.Owner;
                action = entry.Action;
                completion = entry.Completion;
                entry.Owner = null;
                entry.Action = null;
                entry.Completion = null;
            }
            if (completion == null) return;

            try
            {
                if (!owner())
                {
                    completion.TrySetCanceled();
                    return;
                }
                Execute(action);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        // Every live entry belongs to the current scope, so ending it
        // tombstones all of them in place; their slots free when drained.
        private static TaskCompletionSource<bool>[] EndCurrentScopeLocked()
        {
            _currentScope = null;
            _currentScopeGame = null;
            TaskCompletionSource<bool>[] canceled = null;
            int count = 0;
            foreach (ScopedEntry entry in ScopedSlots)
            {
                if (entry?.Completion == null) continue;
                if (canceled == null) canceled = new TaskCompletionSource<bool>[ScopedCapacity];
                canceled[count++] = entry.Completion;
                entry.Owner = null;
                entry.Action = null;
                entry.Completion = null;
            }
            return canceled;
        }

        private static void CancelOutsideGate(TaskCompletionSource<bool>[] canceled)
        {
            if (canceled == null) return;
            foreach (TaskCompletionSource<bool> completion in canceled) completion?.TrySetCanceled();
        }

        private static DateTime ReadUtc(ref long ticks)
        {
            long value = Interlocked.Read(ref ticks);
            return value <= 0 ? DateTime.MinValue : new DateTime(value, DateTimeKind.Utc);
        }
    }
}
