using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using ReignBeta.Platform.Settings;

namespace ReignBeta.Platform.Integration
{
    internal static class ReignServerEndpoint
    {
        internal const string DefaultBaseUrl = "http://127.0.0.1:8089";
        private static readonly object Gate = new object();
        private static string _successfulBaseUrl;
        private static DateTime _unavailableUntilUtc;
        private static bool _halfOpenProbeRunning;
        // Identifies the one half-open recovery probe. Zero means no claim.
        // Reachability is a global signal. Only the owning operation can
        // release its recovery claim.
        private static long _halfOpenProbeClaim;
        private static long _nextProbeClaim;
        private static int _consecutiveFailures;

        internal static string BuildUrl(string route)
        {
            return CurrentBaseUrl().TrimEnd('/') + NormalizeRoute(route);
        }

        internal static IReadOnlyList<string> CandidateBaseUrls(string configured = null)
        {
            configured = configured ?? ConfiguredBaseUrl();
            lock (Gate)
            {
                return new[] { _successfulBaseUrl, configured, ShouldTryDefaultFallback(configured) ? DefaultBaseUrl : null }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.TrimEnd('/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        // A caller receives a nonzero claim only when it became the half-open
        // recovery probe. The transport releases it unconditionally in finally.
        internal static bool TryBeginRequest(out string error, out long probeClaim)
        {
            lock (Gate)
            {
                probeClaim = 0;
                DateTime now = DateTime.UtcNow;
                if (_unavailableUntilUtc <= now)
                {
                    if (_consecutiveFailures > 0)
                    {
                        if (_halfOpenProbeRunning)
                        {
                            error = "Local Bannerlord Reign server recovery probe is already running.";
                            return false;
                        }
                        _halfOpenProbeRunning = true;
                        _halfOpenProbeClaim = ++_nextProbeClaim;
                        probeClaim = _halfOpenProbeClaim;
                    }

                    error = string.Empty;
                    return true;
                }

                int seconds = Math.Max(1, (int)Math.Ceiling((_unavailableUntilUtc - now).TotalSeconds));
                error = "Local Bannerlord Reign server is unavailable; retrying in " + seconds + " seconds.";
                return false;
            }
        }

        // Any response resets global reachability. A matching nonzero claim
        // also releases this operation's recovery probe.
        internal static void ReportSuccess(string baseUrl, long probeClaim)
        {
            lock (Gate)
            {
                _successfulBaseUrl = NormalizeBaseUrl(baseUrl);
                _unavailableUntilUtc = DateTime.MinValue;
                _consecutiveFailures = 0;
                if (probeClaim != 0 && probeClaim == _halfOpenProbeClaim) ClearProbeClaim();
            }
        }

        // Identified transport failure (timeouts included): global backoff, and
        // release of this operation's own claim only.
        internal static void ReportTransportFailure(long probeClaim)
        {
            lock (Gate)
            {
                ApplyTransportFailurePenalty();
                if (probeClaim != 0 && probeClaim == _halfOpenProbeClaim) ClearProbeClaim();
            }
        }

        // Unconditional finally release for identified operations: abandonment,
        // cancellation, a non-transport exception or an already-settled claim.
        // No penalty; idempotent; an older completion cannot clear a newer probe.
        internal static void ReleaseRequest(long probeClaim)
        {
            if (probeClaim == 0) return;
            lock (Gate)
            {
                if (probeClaim == _halfOpenProbeClaim) ClearProbeClaim();
            }
        }

        private static void ApplyTransportFailurePenalty()
        {
            _consecutiveFailures = Math.Min(8, _consecutiveFailures + 1);
            int delaySeconds = Math.Min(60, 5 * (1 << Math.Min(3, _consecutiveFailures - 1)));
            _unavailableUntilUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            _successfulBaseUrl = null;
        }

        private static void ClearProbeClaim()
        {
            _halfOpenProbeRunning = false;
            _halfOpenProbeClaim = 0;
        }

        internal static bool IsTransportFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is HttpRequestException || current is TimeoutException || current is System.Threading.Tasks.TaskCanceledException)
                {
                    return true;
                }
            }
            return false;
        }

        private static string CurrentBaseUrl()
        {
            lock (Gate)
            {
                return string.IsNullOrWhiteSpace(_successfulBaseUrl) ? ConfiguredBaseUrl() : _successfulBaseUrl;
            }
        }

        internal static string ConfiguredBaseUrl()
        {
            return NormalizeBaseUrl(ReignBetaSettings.Instance?.LocalServerUrl);
        }

        private static string NormalizeBaseUrl(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim().TrimEnd('/');
        }

        private static string NormalizeRoute(string route)
        {
            return string.IsNullOrWhiteSpace(route) ? string.Empty : route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route;
        }

        private static bool ShouldTryDefaultFallback(string configured)
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri uri)) return false;
            bool loopback = uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
            return loopback && !string.Equals(configured.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
