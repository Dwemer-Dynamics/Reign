using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Platform.Integration
{
    // The one HTTP transport to the local Reign server: pooled clients per route class,
    // read-only candidate fallback with the shared circuit breaker, transport diagnostics,
    // response provenance and the pinned terminal-report channel. Readiness gates,
    // conversation preparation and module routes stay with the callers (ReignServerClient).
    internal static class ReignHttpTransport
    {
        internal const int InteractiveProviderTimeoutSeconds = 900;
        internal static readonly HttpClient Client = CreateLocalServerHttpClient(8);
        internal static readonly HttpClient PriorityClient = CreateLocalServerHttpClient(4);
        internal static readonly HttpClient InteractiveClient =
            CreateLocalServerHttpClient(4, InteractiveProviderTimeoutSeconds);
        internal static readonly HttpClient LiveTestClient = CreateLocalServerHttpClient(4, 5);
        // Terminal action report v1 only: one proven origin, never redirected.
        internal static readonly HttpClient PinnedClient = CreateLocalServerHttpClient(2, 30, false);
        // Local response provenance for the two protocol import routes. A value is the
        // proven origin; an empty value marks a qualifying response whose origin is unproven.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject, string> ResponseOrigins =
            new System.Runtime.CompilerServices.ConditionalWeakTable<JObject, string>();
        private static long _streamedPostRequests;
        private static long _streamedPostBytes;
        private static long _largeStreamedPostRequests;
        private static long _peakStreamedPostBytes;
        private static long _totalPostSerializationMs;
        private static long _peakPostSerializationMs;

        internal static HttpClient CreateLocalServerHttpClient(int maximumConnections, int timeoutSeconds = 360, bool allowAutoRedirect = false)
        {
            // Bannerlord runs this assembly on .NET Framework, whose default HTTP
            // connection limit is only two. At accelerated campaign speed that
            // allowed redundant hourly work to occupy both connections for minutes,
            // starving daily relationship and World Test telemetry before it ever
            // reached the local server.
            ServicePointManager.DefaultConnectionLimit = Math.Max(
                ServicePointManager.DefaultConnectionLimit,
                16);
            HttpClientHandler handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = Math.Max(2, maximumConnections),
                UseProxy = false,
                AllowAutoRedirect = allowAutoRedirect
            };
            return new HttpClient(CreateProtocolPipeline(handler)) { Timeout = TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)) };
        }

        internal static HttpMessageHandler CreateProtocolPipeline(HttpMessageHandler handler)
        {
            // Track the application request on either side of the protocol health
            // check. The check's own GET has no marker and cannot submit the mutation.
            var dispatch = new DispatchTrackingHandler(handler, DispatchStage.Application);
            var protocol = new Reign.Core.Contracts.Platform.ReignProtocolHandler(dispatch);
            return new DispatchTrackingHandler(protocol, DispatchStage.ProtocolCheck);
        }

        internal static bool IsPriorityServerRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.StartsWith("/relationships/ambient/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/world-test/heartbeat", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/political-pressure/", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/save-sync/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/portraits/generate/status", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/initialization/readiness/", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/tests/live/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/world-history/timeline/open", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/tests/npc-dialogue/request", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsProviderBackedInteractiveRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.Equals("/dialogue/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/party-chat/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/scene", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/family-chambers/scene/generate", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/portraits/generate", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsHighFrequencyControlRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.Equals("/tests/live/game/heartbeat", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/live/game/poll", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/relationships/ambient/native_sync/pull", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/npc-dialogue/request/poll", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/party-dialogue/request/poll", StringComparison.OrdinalIgnoreCase);
        }

        internal static JObject HttpTransportDiagnostics()
        {
            return new JObject
            {
                ["mode"] = "pooled_segmented_utf8_json",
                ["interactiveProviderTimeoutSeconds"] = InteractiveProviderTimeoutSeconds,
                ["interactiveProviderRoutes"] = new JArray(
                    "/dialogue/respond",
                    "/party-chat/respond",
                    "/family-chambers/scene/generate",
                    "/portraits/generate"),
                ["pooledRequests"] = Interlocked.Read(ref _streamedPostRequests),
                ["serializedBytes"] = Interlocked.Read(ref _streamedPostBytes),
                ["largeRequests"] = Interlocked.Read(ref _largeStreamedPostRequests),
                ["peakRequestBytes"] = Interlocked.Read(ref _peakStreamedPostBytes),
                ["totalSerializationMs"] = Interlocked.Read(ref _totalPostSerializationMs),
                ["peakSerializationMs"] = Interlocked.Read(ref _peakPostSerializationMs)
            };
        }

        internal static void RecordStreamedPost(ReignJsonHttpContent content)
        {
            if (content == null) return;
            long bytes = Math.Max(0L, content.SerializedBytes);
            long durationMs = Math.Max(0L, content.SerializationDurationMs);
            Interlocked.Increment(ref _streamedPostRequests);
            Interlocked.Add(ref _streamedPostBytes, bytes);
            Interlocked.Add(ref _totalPostSerializationMs, durationMs);
            if (bytes >= 85L * 1024L)
                Interlocked.Increment(ref _largeStreamedPostRequests);
            UpdateMaximum(ref _peakStreamedPostBytes, bytes);
            UpdateMaximum(ref _peakPostSerializationMs, durationMs);
        }

        internal static void UpdateMaximum(ref long target, long value)
        {
            long observed = Interlocked.Read(ref target);
            while (value > observed)
            {
                long prior = Interlocked.CompareExchange(
                    ref target,
                    value,
                    observed);
                if (prior == observed) return;
                observed = prior;
            }
        }

        private enum RoutePolicy { Mutation, ReadWithFallback, ReadFromConfiguredEndpoint }

        // Several GET routes write delivery state. Some reads also carry proof
        // or status for an operation owned by one installation.
        private static RoutePolicy ClassifyRoute(HttpMethod method, string route)
        {
            string path = RoutePath(route);
            if (method == HttpMethod.Get)
            {
                switch (path)
                {
                    case "/api/gameplay-options":
                    case "/court/patronage/public-history":
                    case "/tests/live/run/status":
                    case "/tests/npc-dialogue/status":
                    case "/tests/party-dialogue/status":
                    case "/verification/status":
                    case "/verification/results":
                        return RoutePolicy.ReadWithFallback;
                    case "/gossip/status":
                        // Readiness returns the clock epoch and nonces for /gossip/tick.
                        return RoutePolicy.ReadFromConfiguredEndpoint;
                    case "/diplomacy/events/next":
                        return QueryValue(route, "publicHistory") == "true"
                            ? RoutePolicy.ReadWithFallback : RoutePolicy.Mutation;
                    case "/negotiations/query":
                        return string.IsNullOrWhiteSpace(QueryValue(route, "negotiationId"))
                            ? RoutePolicy.ReadWithFallback : RoutePolicy.Mutation;
                }
            }

            if (method == HttpMethod.Post)
            {
                switch (path)
                {
                    case "/identity/query":
                    case "/court/view":
                    case "/correspondence/threads":
                    case "/dialogue/history":
                    case "/party-chat/history":
                        return RoutePolicy.ReadWithFallback;
                    case "/portraits/generate/status":
                        // The generation operation ID is local to its originating backend.
                        return RoutePolicy.ReadFromConfiguredEndpoint;
                }
            }

            return RoutePolicy.Mutation;
        }

        private static string RoutePath(string route)
        {
            string path = route.Split(new[] { '?', '#' }, 2)[0];
            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private static string QueryValue(string route, string key)
        {
            // Match ingress: URL-decoded names, ordinal-ignore-case lookup and
            // the last occurrence wins. Fragments are never sent to the server.
            int query = route.IndexOf('?');
            int fragment = route.IndexOf('#');
            if (query < 0 || (fragment >= 0 && fragment < query)) return string.Empty;
            string value = string.Empty;
            string encoded = fragment < 0 ? route.Substring(query + 1) : route.Substring(query + 1, fragment - query - 1);
            foreach (string part in encoded.Split('&'))
            {
                string[] field = part.Split(new[] { '=' }, 2);
                if (string.Equals(WebUtility.UrlDecode(field[0]), key, StringComparison.OrdinalIgnoreCase))
                    value = field.Length == 2 ? WebUtility.UrlDecode(field[1]) : string.Empty;
            }
            return value;
        }

        private enum DispatchStage { Preparing, Untracked, ProtocolCheck, Application }

        private sealed class RequestAttempt
        {
            internal RequestAttempt(RoutePolicy policy) { MayMutate = policy == RoutePolicy.Mutation; }

            internal bool MayMutate { get; }
            internal DispatchStage Stage { get; set; }
            internal bool MayHaveDispatched => Stage == DispatchStage.Application || Stage == DispatchStage.Untracked;

            internal Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request,
                HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
            {
                request.Properties.Add(DispatchMarker, this);
                Stage = DispatchStage.Untracked;
                return client.SendAsync(request, completion);
            }
        }

        private const string DispatchMarker = "ReignHttpTransport.RequestAttempt";

        private sealed class DispatchTrackingHandler : DelegatingHandler
        {
            private readonly DispatchStage _stage;

            internal DispatchTrackingHandler(HttpMessageHandler inner, DispatchStage stage) : base(inner)
            {
                _stage = stage;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Properties.TryGetValue(DispatchMarker, out object entry) && entry is RequestAttempt attempt)
                    attempt.Stage = _stage;
                return base.SendAsync(request, cancellationToken);
            }
        }

        internal sealed class UnknownMutationOutcomeException : HttpRequestException
        {
            internal UnknownMutationOutcomeException(HttpMethod method, string routePath, Exception cause)
                : base("unknown_outcome: Reign could not confirm the outcome of " + method + " " + routePath
                    + ". " + cause.Message, cause) { }
        }

        private static async Task<T> WithCandidatesAsync<T>(HttpMethod method, string route,
            Func<string, string, RequestAttempt, Task<T>> send, Action reportFailure)
        {
            string configured = ReignServerEndpoint.ConfiguredBaseUrl();
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri configuredUri)
                || !IsHttpEndpoint(configuredUri))
                throw new InvalidOperationException("The Reign server URL must be an HTTP endpoint without a query or fragment.");

            // A route mounted below a configured path prefix has no reviewed
            // fallback contract. Evaluate read replay on root endpoints only.
            RoutePolicy policy = configuredUri.AbsolutePath == "/" ? ClassifyRoute(method, route) : RoutePolicy.Mutation;
            // A version handshake or a cached read proves reachability, not common
            // backend/campaign identity. Even localhost and 127.0.0.1 can route
            // differently. Only the configured destination is authoritative for a
            // mutation. Exact-operation replay belongs to origin-pinned protocols.
            var candidates = policy == RoutePolicy.ReadWithFallback
                ? ReignServerEndpoint.CandidateBaseUrls(configured)
                    .Where(value => Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && IsHttpEndpoint(uri) && uri.AbsolutePath == "/")
                    .GroupBy(value => new Uri(value).GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                : new[] { configured };
            Exception lastFailure = null;
            foreach (string baseUrl in candidates)
            {
                var attempt = new RequestAttempt(policy);
                string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                try
                {
                    return await send(baseUrl, url, attempt).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is UnknownMutationOutcomeException) && attempt.Stage != DispatchStage.Preparing &&
                    (ReignServerEndpoint.IsTransportFailure(ex) || ex is IOException || ex is OperationCanceledException))
                {
                    lastFailure = ex;
                    if (attempt.MayMutate && attempt.MayHaveDispatched)
                    {
                        reportFailure();
                        throw new UnknownMutationOutcomeException(method, RoutePath(route), ex);
                    }
                }
            }

            reportFailure();
            throw lastFailure ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
        }

        private static bool IsHttpEndpoint(Uri uri) =>
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Query.Length == 0 && uri.Fragment.Length == 0;

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
        {
            if (response.Content == null) return string.Empty;
            try { return await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch (InvalidOperationException ex) when (ex.InnerException is ArgumentException)
            {
                throw new IOException("Reign could not decode the server reply.", ex);
            }
        }

        private static JObject ParseResponse(HttpMethod method, string route, string body, RequestAttempt attempt)
        {
            try { return JObject.Parse(body); }
            catch (JsonException ex) when (attempt.MayMutate)
            {
                throw new UnknownMutationOutcomeException(method, RoutePath(route), ex);
            }
        }

        private static Exception HttpRejection(HttpMethod method, string route,
            HttpResponseMessage response, string body, RequestAttempt attempt)
        {
            int status = (int)response.StatusCode;
            var error = new InvalidOperationException("HTTP " + status + ": " + body);
            // Failure after dispatch can be generated after a write, including by
            // a gateway. Preserve definitive client refusals and their error text.
            if (attempt.MayMutate && (status < 400 || status == 408 || status >= 500))
                return new UnknownMutationOutcomeException(method, RoutePath(route), error);
            return error;
        }

        internal static async Task<JObject> GetJsonAsync(string route, HttpClient requestClient)
        {
            if (!ReignServerEndpoint.TryBeginRequest(out string unavailable, out long probeClaim))
                throw new HttpRequestException(unavailable);

            try
            {
                return await WithCandidatesAsync(HttpMethod.Get, route, async (baseUrl, url, attempt) =>
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (HttpResponseMessage response = await attempt.SendAsync(requestClient ?? Client, request).ConfigureAwait(false))
                    {
                        string body = await ReadBodyAsync(response).ConfigureAwait(false);
                        ReignServerEndpoint.ReportSuccess(baseUrl, probeClaim);
                        if (!response.IsSuccessStatusCode)
                            throw HttpRejection(HttpMethod.Get, route, response, body, attempt);

                        return RecordResponseOrigin(route, baseUrl, url, response, ParseResponse(HttpMethod.Get, route, body, attempt));
                    }
                }, () => ReignServerEndpoint.ReportTransportFailure(probeClaim)).ConfigureAwait(false);
            }
            finally
            {
                ReignServerEndpoint.ReleaseRequest(probeClaim);
            }
        }

        // Record local provenance for responses followed by a pinned acknowledgment.
        // Redirected or unobservable final targets and path-prefixed bases prove no origin.
        internal static JObject RecordResponseOrigin(string route, string baseUrl, string requestUrl, HttpResponseMessage response, JObject parsed)
        {
            if (parsed == null || !(route == "/dialogue/respond"
                || (route == "/arrests/case/apply" && parsed["operation"]?.Type == JTokenType.String
                    && parsed.Value<string>("operation") == "inspect")
                || route.StartsWith("/actions/next?", StringComparison.Ordinal)
                || route.StartsWith("/political-pressure/notices?", StringComparison.Ordinal)
                || route.StartsWith("/clan-conflicts/notices?", StringComparison.Ordinal)
                || route == "/relationships/director/poll_actions")) return parsed;
            string origin = PinnableOrigin(baseUrl);
            Uri final = response?.RequestMessage?.RequestUri;
            if (final == null || !Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri requested)
                || !string.Equals(final.AbsoluteUri, requested.AbsoluteUri, StringComparison.Ordinal)) origin = string.Empty;
            ResponseOrigins.Remove(parsed);
            ResponseOrigins.Add(parsed, origin);
            return parsed;
        }

        // Null when the token's response did not come through a qualifying import route.
        internal static string ResponseOrigin(JToken token)
        {
            JObject root = token?.Root as JObject;
            return root != null && ResponseOrigins.TryGetValue(root, out string origin) ? origin : null;
        }

        internal static string PinnableOrigin(string baseUrl)
        {
            string trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) || uri.UserInfo.Length > 0
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return string.Empty;
            string origin = uri.GetLeftPart(UriPartial.Authority);
            // Uri canonicalizes an explicit default port out of the origin.
            string explicitDefaultPort = origin + ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(origin, trimmed, StringComparison.OrdinalIgnoreCase)
                || uri.IsDefaultPort && string.Equals(explicitDefaultPort, trimmed, StringComparison.OrdinalIgnoreCase)
                ? origin : string.Empty;
        }

        // A pinned origin stays usable only while it is one of the currently allowed endpoints.
        internal static bool IsAllowedPinnedOrigin(string origin) => !string.IsNullOrEmpty(origin)
            && ReignServerEndpoint.CandidateBaseUrls().Any(baseUrl => string.Equals(PinnableOrigin(baseUrl), origin, StringComparison.Ordinal));

        // A definite HTTP answer from the exact pinned origin. It stays an
        // InvalidOperationException, with the same message, for existing callers.
        internal sealed class PinnedHttpRefusalException : InvalidOperationException
        {
            internal PinnedHttpRefusalException(int statusCode)
                : base("HTTP " + statusCode + " from the pinned terminal report endpoint.") { StatusCode = statusCode; }
            internal int StatusCode { get; }
            // 408 Request Timeout and 429 Too Many Requests are transient answers that keep
            // the caller's bounded retry.
            internal bool IsDefiniteClientRefusal => StatusCode >= 400 && StatusCode < 500
                && StatusCode != 408 && StatusCode != 429;
        }

        // Terminal action report v1 transport: the exact pinned origin only, no redirects,
        // no candidate failover and no shared circuit-breaker state. The protocol handler
        // still performs its handshake and adds its header. Failures throw without the
        // response body; the caller keeps its exact report pending. requestOwner is a
        // required game-thread predicate, rechecked here (after the caller's readiness gates)
        // and again immediately before dispatch (an abandoned owner sends nothing).
        internal static async Task<JObject> SendPinnedJsonAsync(string origin, string pathAndQuery, JObject payload, Func<bool> requestOwner)
        {
            if (requestOwner == null) throw new ArgumentNullException(nameof(requestOwner));
            await EnsureRequestOwnerAsync(requestOwner, "pinned request preparation").ConfigureAwait(false);
            if (!IsAllowedPinnedOrigin(origin))
                throw new InvalidOperationException("The pinned terminal report origin is no longer an allowed local endpoint.");
            string url = origin + pathAndQuery;
            using (var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Post, url))
            {
                if (payload != null) request.Content = new ReignJsonHttpContent(payload);
                await EnsureRequestOwnerAsync(requestOwner, "pinned transport dispatch").ConfigureAwait(false);
                using (HttpResponseMessage response = await PinnedClient.SendAsync(request).ConfigureAwait(false))
                {
                    Uri final = response.RequestMessage?.RequestUri;
                    if (final == null || !string.Equals(final.AbsoluteUri, new Uri(url).AbsoluteUri, StringComparison.Ordinal))
                        throw new InvalidOperationException("The pinned terminal report response came from a different endpoint.");
                    if (!response.IsSuccessStatusCode)
                        throw new PinnedHttpRefusalException((int)response.StatusCode);
                    return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        // One POST to the local server: pooled client per route class, candidate endpoint
        // policy and the shared circuit breaker. isCurrentRequest and requestOwner are
        // rechecked before encoding and again before dispatch. encodeOffThread moves large
        // payload serialization off the game thread. onAccepted runs after a successful
        // response is parsed, before it is returned.
        internal static async Task<JObject> PostAsync(string route, JObject payload,
            Func<Task<bool>> isCurrentRequest, Func<bool> requestOwner, bool encodeOffThread,
            Func<JObject, Task> onAccepted, HttpClient requestClient = null)
        {
            bool liveTestRoute = route.StartsWith(
                "/tests/live/", StringComparison.OrdinalIgnoreCase);
            // The production bridge is itself the recovery/control plane. An
            // unrelated passive-system timeout must not place heartbeat, polling,
            // acknowledgement, or lifecycle traffic behind the shared endpoint
            // circuit breaker. Live routes use their own short-timeout client and
            // always probe loopback directly; a successful probe still clears the
            // shared breaker because it proves the server is reachable.
            long probeClaim = 0;
            if (!liveTestRoute
                && !ReignServerEndpoint.TryBeginRequest(out string unavailable, out probeClaim))
            {
                throw new HttpRequestException(unavailable);
            }

            try
            {
                HttpClient routeClient = requestClient ?? (liveTestRoute
                    ? LiveTestClient
                    : IsProviderBackedInteractiveRoute(route) ? InteractiveClient
                    : IsPriorityServerRoute(route) ? PriorityClient : Client);
                JObject result = await WithCandidatesAsync(HttpMethod.Post, route, async (baseUrl, url, attempt) =>
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                        throw new OperationCanceledException("Request belongs to a previous loaded save.");
                    await EnsureRequestOwnerAsync(requestOwner, "request encoding").ConfigureAwait(false);
                    using (ReignJsonHttpContent content = encodeOffThread
                        ? await Task.Run(() => new ReignJsonHttpContent(payload)).ConfigureAwait(false)
                        : new ReignJsonHttpContent(payload ?? new JObject()))
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                            throw new OperationCanceledException("Request belongs to a previous loaded save after encoding.");
                        await EnsureRequestOwnerAsync(requestOwner, "transport dispatch").ConfigureAwait(false);
                        request.Content = content;
                        try
                        {
                            using (HttpResponseMessage response = await attempt.SendAsync(routeClient, request,
                                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                            {
                                string body = await ReadBodyAsync(response).ConfigureAwait(false);
                                timer.Stop();
                                // Successful empty control-plane polls are intentionally quiet.
                                if (!IsHighFrequencyControlRoute(route)
                                    || timer.ElapsedMilliseconds >= 250
                                    || !response.IsSuccessStatusCode)
                                {
                                    ReignLog.Info("HTTP POST " + route
                                        + " status=" + (int)response.StatusCode
                                        + " clientMs=" + timer.ElapsedMilliseconds
                                        + " requestBytes=" + content.SerializedBytes
                                        + " serializationMs=" + content.SerializationDurationMs
                                        + " responseChars=" + body.Length);
                                }

                                ReignServerEndpoint.ReportSuccess(baseUrl, probeClaim);
                                if (!response.IsSuccessStatusCode)
                                    throw HttpRejection(HttpMethod.Post, route, response, body, attempt);

                                return RecordResponseOrigin(route, baseUrl, url, response, ParseResponse(HttpMethod.Post, route, body, attempt));
                            }
                        }
                        finally
                        {
                            RecordStreamedPost(content);
                        }
                    }
                }, () =>
                {
                    if (!liveTestRoute) ReignServerEndpoint.ReportTransportFailure(probeClaim);
                }).ConfigureAwait(false);

                // A local callback failure cannot authorize a second HTTP request.
                if (onAccepted != null) await onAccepted(result).ConfigureAwait(false);
                return result;
            }
            finally
            {
                // Owner abandonment, cancellation and non-transport exceptions
                // release only this operation's own claim, without penalty; after
                // a report this matching release is a no-op.
                ReignServerEndpoint.ReleaseRequest(probeClaim);
            }
        }

        // One answered direct POST: the HTTP status and body of the first candidate endpoint
        // that answered, its base URL and request URL, and the observed final request URI.
        internal sealed class DirectJsonResponse
        {
            internal int StatusCode;
            internal bool IsSuccessStatusCode;
            internal string Body;
            internal JObject Json;
            internal string BaseUrl;
            internal string RequestUrl;
            internal Uri FinalRequestUri;
            internal long ElapsedMilliseconds;
        }

        // Court observes the shared breaker. Save Sync owns its readiness gates
        // and continues to report reachability without taking a recovery claim.
        internal static async Task<DirectJsonResponse> PostDirectJsonAsync(HttpClient client, string route,
            string json, bool checkAvailability = false)
        {
            long probeClaim = 0;
            if (checkAvailability && !ReignServerEndpoint.TryBeginRequest(out string unavailable, out probeClaim))
                throw new HttpRequestException(unavailable);

            try
            {
                return await WithCandidatesAsync(HttpMethod.Post, route, async (baseUrl, url, attempt) =>
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                    })
                    using (HttpResponseMessage response = await attempt.SendAsync(client, request).ConfigureAwait(false))
                    {
                        string body = await ReadBodyAsync(response).ConfigureAwait(false);
                        timer.Stop();
                        ReignServerEndpoint.ReportSuccess(baseUrl, probeClaim);
                        if (!response.IsSuccessStatusCode && HttpRejection(HttpMethod.Post, route, response, body, attempt)
                            is UnknownMutationOutcomeException unknown)
                            throw unknown;
                        return new DirectJsonResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            IsSuccessStatusCode = response.IsSuccessStatusCode,
                            Body = body,
                            Json = response.IsSuccessStatusCode ? ParseResponse(HttpMethod.Post, route, body, attempt) : null,
                            BaseUrl = baseUrl,
                            RequestUrl = url,
                            FinalRequestUri = response.RequestMessage?.RequestUri,
                            ElapsedMilliseconds = timer.ElapsedMilliseconds
                        };
                    }
                }, () => ReignServerEndpoint.ReportTransportFailure(probeClaim)).ConfigureAwait(false);
            }
            finally
            {
                ReignServerEndpoint.ReleaseRequest(probeClaim);
            }
        }

        private static Task EnsureRequestOwnerAsync(Func<bool> requestOwner, string stage) =>
            ReignRequestOwnership.EnsureAsync(requestOwner, stage);
    }
}
