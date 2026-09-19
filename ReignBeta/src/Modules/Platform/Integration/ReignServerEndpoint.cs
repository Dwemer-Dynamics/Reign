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
            lock (Gate)
            {
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
                    }

                    error = string.Empty;
                    return true;
                }

                int seconds = Math.Max(1, (int)Math.Ceiling((_unavailableUntilUtc - now).TotalSeconds));
                error = "Local Bannerlord Reign server is unavailable; retrying in " + seconds + " seconds.";
                return false;
            }
        }

        internal static void ReportSuccess(string baseUrl)
        {
            lock (Gate)
            {
                _successfulBaseUrl = NormalizeBaseUrl(baseUrl);
                _unavailableUntilUtc = DateTime.MinValue;
                _halfOpenProbeRunning = false;
                _consecutiveFailures = 0;
            }
        }

        internal static void ReportTransportFailure()
        {
            lock (Gate)
            {
                _consecutiveFailures = Math.Min(8, _consecutiveFailures + 1);
                int delaySeconds = Math.Min(60, 5 * (1 << Math.Min(3, _consecutiveFailures - 1)));
                _unavailableUntilUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                _halfOpenProbeRunning = false;
                _successfulBaseUrl = null;
            }
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
