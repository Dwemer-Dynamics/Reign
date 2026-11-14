using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;
using Reign.Core.Contracts.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ReignBeta.Integration
{
    public static class ReignLog
    {
        private const long MaximumLogBytes = 8L * 1024L * 1024L;
        private const long RetainedLogBytes = 4L * 1024L * 1024L;
        private static readonly object FileLock = new object();
        private static readonly object TimingLock = new object();
        private static readonly Dictionary<string, WorkSample> WorkSamples = new Dictionary<string, WorkSample>(StringComparer.Ordinal);
        private static readonly NativePerformanceWindows Performance = new NativePerformanceWindows(new[] {
            "population.tavern", "population.wanderer", "government.political.select",
            "government.political.capture", "government.political.apply", "diagnostics.heartbeat", "main-thread.dispatch", "relationships.capture" });
        private static readonly JsonSerializerSettings PerformanceJson = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver() };
        private static long performanceGeneration;
        private static object performanceCampaign;
        private static readonly int GameProcessId = CurrentProcessId();
        private static readonly string ClientModuleMvid = typeof(ReignLog).Assembly.ManifestModule.ModuleVersionId.ToString("D");
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        private static int CurrentProcessId()
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) return process.Id;
        }

        private static bool IsGameForeground()
        {
            try { GetWindowThreadProcessId(GetForegroundWindow(), out uint processId); return processId == GameProcessId; }
            catch { return false; }
        }

        internal static long CapturePerformanceGeneration(object campaign)
        {
            lock (TimingLock) return ReferenceEquals(campaign, performanceCampaign) ? performanceGeneration : -1;
        }

        private static JToken PerformanceUnavailable() => new JRaw(
            "{\"schema\":\"reign-native-performance-v1\",\"samplingEnabled\":false,\"pending\":true}");

        internal static Task<JToken> PerformanceSnapshotAsync(long expectedGeneration)
        {
            // The heartbeat awaits this worker after capturing its native fields. It sends
            // this fresh publication, rather than one from the previous idle heartbeat.
            return Task.Run<JToken>(() =>
            {
                try
                {
                NativePerformanceSnapshot snapshot;
                DateTime captured;
                lock (TimingLock)
                {
                    if (expectedGeneration < 0 || expectedGeneration != performanceGeneration) return PerformanceUnavailable();
                    captured = DateTime.UtcNow;
                    snapshot = Performance.Snapshot(System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency);
                }
                snapshot.SnapshotCapturedUtc = captured.ToString("o");
                snapshot.ClientModuleMvid = ClientModuleMvid;
                snapshot.Measurement = "application-tick-intervals; not GPU/present timing; phase totals may overlap; GC counts include observer allocations";
                // Serialize the frozen DTO directly. An intermediate DOM for 120
                // windows repeatedly allocated every phase/property a second time.
                string json = JsonConvert.SerializeObject(snapshot, Formatting.None, PerformanceJson);
                lock (TimingLock)
                    return expectedGeneration == performanceGeneration ? new JRaw(json) : PerformanceUnavailable();
                }
                catch { return PerformanceUnavailable(); } // Optional diagnostics cannot break the recovery heartbeat.
            });
        }

        private sealed class WorkSample
        {
            public long Passes, LastLogTicks, WindowStartedTicks;
            public double MaximumMs, TotalMs;
        }

        // Main-thread-only rolling observations; no heap walk or OS profiler.
        private static readonly double[] FrameIntervals = new double[1024];
        private static long previousFrameTicks, lastFrameLogTicks;
        private static int frameIndex, frameCount;
        private static bool frameHadSlowInterval;

        internal static void ObserveFrameInterval(bool enabled)
        {
            long observed = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (TimingLock)
            {
                object campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (!ReferenceEquals(campaign, performanceCampaign))
                {
                    Performance.Observe(false, observed * 1000d / System.Diagnostics.Stopwatch.Frequency, 0, 0, 0, false);
                    performanceCampaign = campaign;
                    performanceGeneration++;
                }
                Performance.Observe(enabled, observed * 1000d / System.Diagnostics.Stopwatch.Frequency,
                    GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), enabled && IsGameForeground());
            }
            if (!enabled)
            {
                previousFrameTicks = lastFrameLogTicks = 0;
                frameIndex = frameCount = 0;
                frameHadSlowInterval = false;
                return;
            }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (previousFrameTicks == 0) { previousFrameTicks = now; return; }
            double latestMs = Math.Max(0, now - previousFrameTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            previousFrameTicks = now;
            FrameIntervals[frameIndex] = latestMs;
            frameIndex = (frameIndex + 1) % FrameIntervals.Length;
            frameCount = Math.Min(frameCount + 1, FrameIntervals.Length);
            frameHadSlowInterval |= latestMs >= 100d;
            if (!frameHadSlowInterval || (lastFrameLogTicks != 0
                && now - lastFrameLogTicks < 15L * System.Diagnostics.Stopwatch.Frequency)) return;
            var sorted = new double[frameCount];
            Array.Copy(FrameIntervals, sorted, frameCount);
            Array.Sort(sorted);
            lastFrameLogTicks = now;
            frameHadSlowInterval = false;
            Info("Native frame intervals samples=" + frameCount + " rollingCapacity=" + FrameIntervals.Length
                + " p95Ms=" + sorted[Math.Max(0, (int)Math.Ceiling(frameCount * .95d) - 1)].ToString("0.00", CultureInfo.InvariantCulture)
                + " maxMs=" + sorted[frameCount - 1].ToString("0.00", CultureInfo.InvariantCulture)
                + " latestMs=" + latestMs.ToString("0.00", CultureInfo.InvariantCulture));
        }

        public readonly struct WorkTiming : IDisposable
        {
            private readonly string operation;
            private readonly long started;
            private readonly float nativeDelta;
            internal WorkTiming(string operation, float nativeDelta)
            {
                this.operation = operation;
                this.nativeDelta = nativeDelta;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (string.IsNullOrEmpty(operation)) return;
                RecordWork(operation, nativeDelta, started);
            }
        }

        public static WorkTiming MeasureWork(string operation, float nativeDelta) => new WorkTiming(operation, nativeDelta);

        private static void RecordWork(string operation, float nativeDelta, long started)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double milliseconds = Math.Max(0, now - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            string message = null;
            lock (TimingLock)
            {
                Performance.RecordPhase(operation, milliseconds);
                if (!WorkSamples.TryGetValue(operation, out WorkSample sample))
                {
                    if (WorkSamples.Count >= 64) return;
                    WorkSamples[operation] = sample = new WorkSample();
                }
                if (sample.WindowStartedTicks == 0) sample.WindowStartedTicks = started;
                sample.Passes++;
                sample.TotalMs += milliseconds;
                sample.MaximumMs = Math.Max(sample.MaximumMs, milliseconds);
                if (sample.MaximumMs >= 16d && (sample.LastLogTicks == 0
                    || now - sample.LastLogTicks >= 15L * System.Diagnostics.Stopwatch.Frequency))
                {
                    message = "Native maintenance timing operation=" + operation
                        + " passesSinceLastLog=" + sample.Passes
                        + " elapsedWindowMs=" + ((now - sample.WindowStartedTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency).ToString("0.00", CultureInfo.InvariantCulture)
                        + " totalMs=" + sample.TotalMs.ToString("0.00", CultureInfo.InvariantCulture)
                        + " maxMs=" + sample.MaximumMs.ToString("0.00", CultureInfo.InvariantCulture)
                        + " latestMs=" + milliseconds.ToString("0.00", CultureInfo.InvariantCulture)
                        + " nativeDt=" + nativeDelta.ToString("0.0000", CultureInfo.InvariantCulture);
                    sample.LastLogTicks = now;
                    sample.Passes = 0;
                    sample.MaximumMs = sample.TotalMs = 0;
                    sample.WindowStartedTicks = now;
                }
            }
            if (message != null) Info(message);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Exception(string context, Exception ex)
        {
            Write("ERROR", context + ": " + ex);
        }

        private static void Write(string level, string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + message;
            Debug.Print("[Bannerlord Reign] " + line);

            try
            {
                string directory = Path.Combine(BasePath.Name, "Modules", "ReignBeta", "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "reignbeta.log");
                lock (FileLock)
                {
                    TrimTail(path);
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging should never threaten campaign stability.
            }
        }

        private static void TrimTail(string path)
        {
            if (!File.Exists(path)) return;
            FileInfo info = new FileInfo(path);
            if (info.Length <= MaximumLogBytes) return;

            int length = (int)Math.Min(info.Length, RetainedLogBytes + 4096L);
            byte[] buffer = new byte[length];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Seek(-length, SeekOrigin.End);
                int read = 0;
                while (read < buffer.Length)
                {
                    int amount = stream.Read(buffer, read, buffer.Length - read);
                    if (amount <= 0) break;
                    read += amount;
                }
                int start = 0;
                if (info.Length > length)
                {
                    while (start < read && buffer[start] != (byte)'\n') start++;
                    if (start < read) start++;
                }
                stream.SetLength(0);
                stream.Position = 0;
                stream.Write(buffer, start, Math.Max(0, read - start));
            }
        }
    }
}
