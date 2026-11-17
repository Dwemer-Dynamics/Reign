using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using ReignBeta.Settings;

namespace ReignBeta.Integration
{
    internal static class ReignServerEndpoint
    {
        internal const string DefaultBaseUrl = "http://127.0.0.1:8089";
        private static readonly object Gate = new object();
        private static string _successfulBaseUrl;
        private static DateTime _unavailableUntilUtc;
        private static bool _halfOpenProbeRunning;
        // Identifies the one half-open recovery probe. Zero is an anonymous
        // claim taken through the legacy entry point (court and similar callers),
        // which legacy and live reports continue to release exactly as before.
        // Reachability (any HTTP response resets it) is a global signal; claim
        // ownership is released only by the matching identified operation.
        private static long _halfOpenProbeClaim;
        private static long _nextProbeClaim;
        private static int _consecutiveFailures;

        internal static string BuildUrl(string route)
        {
            return CurrentBaseUrl().TrimEnd('/') + NormalizeRoute(route);
        }

        internal static IReadOnlyList<string> CandidateBaseUrls()
        {
            string configured = ConfiguredBaseUrl();
            lock (Gate)
            {
                return new[] { _successfulBaseUrl, configured, ShouldTryDefaultFallback(configured) ? DefaultBaseUrl : null }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.TrimEnd('/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal static bool TryBeginRequest(out string error)
        {
            return TryBeginRequest(out error, out _, false);
        }

        // An identified caller receives a nonzero claim only when it became the
        // half-open recovery probe. It reports through the claim overloads below
        // and calls ReleaseRequest(claim) unconditionally in finally.
        internal static bool TryBeginRequest(out string error, out long probeClaim)
        {
            return TryBeginRequest(out error, out probeClaim, true);
        }

        private static bool TryBeginRequest(out string error, out long probeClaim, bool identified)
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
                        _halfOpenProbeClaim = identified ? ++_nextProbeClaim : 0;
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

        // Legacy and explicit live reachability success: resets the breaker and
        // releases an anonymous legacy claim, never an identified probe that
        // another operation still owns.
        internal static void ReportSuccess(string baseUrl)
        {
            ReportSuccess(baseUrl, 0);
        }

        // Any response resets global reachability. The claim is released only
        // when it is this operation's own current claim (zero: anonymous only),
        // so a superseded or older completion never mutates a newer claim.
        internal static void ReportSuccess(string baseUrl, long probeClaim)
        {
            lock (Gate)
            {
                _successfulBaseUrl = NormalizeBaseUrl(baseUrl);
                _unavailableUntilUtc = DateTime.MinValue;
                _consecutiveFailures = 0;
                if (probeClaim == _halfOpenProbeClaim) ClearProbeClaim();
            }
        }

        // Legacy global report (court, Save Sync). It still applies the usual
        // backoff, but it can release only an anonymous legacy claim, never an
        // identified probe that another operation still owns.
        internal static void ReportTransportFailure()
        {
            lock (Gate)
            {
                ApplyTransportFailurePenalty();
                if (_halfOpenProbeClaim == 0) _halfOpenProbeRunning = false;
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

        private static string ConfiguredBaseUrl()
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
