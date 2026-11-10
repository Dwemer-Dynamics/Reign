using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ReignBeta.Integration
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

        private static DateTime ReadUtc(ref long ticks)
        {
            long value = Interlocked.Read(ref ticks);
            return value <= 0 ? DateTime.MinValue : new DateTime(value, DateTimeKind.Utc);
        }
    }
}
