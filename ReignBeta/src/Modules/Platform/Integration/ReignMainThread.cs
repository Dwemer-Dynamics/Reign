using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

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

        public static bool IsMainThread
        {
            get { return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        public static int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));
        public static long ExecutedCount => Interlocked.Read(ref _executedCount);
        public static DateTime LastDrainUtc => ReadUtc(ref _lastDrainUtcTicks);
        public static DateTime LastExecutedUtc => ReadUtc(ref _lastExecutedUtcTicks);

        public static void DrainOnMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Interlocked.Exchange(ref _lastDrainUtcTicks, DateTime.UtcNow.Ticks);

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
                action();
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pendingCount);
            Work.Enqueue(() =>
            {
                try
                {
                    action();
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
                return Task.FromResult(action());
            }

            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pendingCount);
            Work.Enqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(action());
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
