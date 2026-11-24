param([Parameter(Mandatory)][string]$Workspace)
$ErrorActionPreference = 'Stop'
$integration = Join-Path $Workspace 'ReignBeta/src/Modules/Platform/Integration'
$contracts = Join-Path $Workspace 'ReignServer/shared/Reign.Core.Contracts/Platform'
$probe = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;

namespace ReignBeta.Platform.Settings
{
    // Each fixture runs outside the game, with a task-owned endpoint setting.
    internal sealed class ReignBetaSettings
    {
        internal static ReignBetaSettings Instance { get; } = new ReignBetaSettings();
        internal string LocalServerUrl { get; set; }
    }
}

namespace ReignBeta.Platform.Integration
{
    internal static class ReignLog { internal static void Info(string message) { } }

    internal static class ReignMainThread
    {
        internal sealed class GameScope { internal bool Closed { get; set; } }
    }

    internal static class ReignRequestOwnership
    {
        internal static Task EnsureAsync(Func<bool> owner, string stage, ReignMainThread.GameScope scope = null)
        {
            if (scope?.Closed == true || (owner != null && !owner())) throw new OperationCanceledException(stage);
            return Task.CompletedTask;
        }
    }
}

namespace Reign.Core.Contracts.Platform
{
    // Only the common version number is needed by the real handshake handler.
    internal static class ReignInstallation { public const int SupportedProtocol = 1; }
}

public static class ReignClientTransportProbe
{
    private const string Default = "http://127.0.0.1:8089";
    private const string Alias = "http://localhost:8089";
    private const string OtherBackend = "http://127.0.0.1:8099";

    public static async Task Run()
    {
        await Check("streamed mutation loses response after application and ignores a cached peer", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => throw new TimeoutException("lost reply after receipt")))
            using (var client = Client(wire))
            {
                var error = await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(
                    () => Post(client, "/dialogue/respond?private=sentinel"));
                Require(error.Message.Contains("unknown_outcome") && !error.Message.Contains("sentinel"), "unhelpful or overbroad diagnostic");
                Require(error.InnerException is TimeoutException, "lost original failure");
                wire.Expect("POST " + Alias + "/dialogue/respond");
            }
        });

        await Check("direct Save Sync mutation loses response body", async () =>
        {
            Configure(Alias, Default);
            using (var wire = new FakeWire(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new LostBody() }))
            using (var client = Client(wire))
            {
                await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(() =>
                    ReignHttpTransport.PostDirectJsonAsync(client, "/save-sync/load", "{}"));
                wire.Expect("POST " + Alias + "/save-sync/load");
            }
        });

        await Check("mutating GETs, including delivery polls and mixed-mode routes", async () =>
        {
            foreach (string route in new[] { "/actions/next?limit=10", "/diplomacy/events/next?publicHistory=true&%70ublicHistory=false",
                "/political-pressure/notices", "/clan-conflicts/notices", "/negotiations/query?negotiationId=one", "/social/catalog", "/new/unaudited" })
            {
                Configure(Alias, Default);
                using (var wire = new FakeWire(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new LostBody() }))
                using (var client = Client(wire))
                {
                    await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(() => ReignHttpTransport.GetJsonAsync(route, client));
                    wire.Expect("GET " + Alias + route.Split('?')[0]);
                }
            }
        });

        await Check("Spymaster partial effect followed by timeout is submitted once", async () =>
        {
            Configure(Alias, Default);
            int partialEffects = 0;
            using (var wire = new FakeWire(request => { partialEffects++; throw new TaskCanceledException("reply timed out"); }))
            using (var client = Client(wire))
            {
                await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(() => Post(client, "/court/spymaster/social/apply"));
                Require(partialEffects == 1, "partial effect was repeated");
                wire.Expect("POST " + Alias + "/court/spymaster/social/apply");
            }
        });

        await Check("health connection refusal is pre-dispatch and confers no peer authority", async () =>
        {
            Configure(Alias, Default);
            using (var wire = new FakeWire(request => throw new Exception("application should never be submitted")) { RefuseHealth = true })
            using (var client = Client(wire))
            {
                var error = await Throws<HttpRequestException>(() => Post(client, "/actions/report"));
                Require(error.GetType() == typeof(HttpRequestException), "preflight refusal was called an unknown mutation");
                Require(wire.Health.Count == 1 && wire.Health[0] == Alias, "attempted an unverified peer");
                wire.Expect();
            }
        });

        await Check("safe reads keep cached and configured fallback, including POST queries", async () =>
        {
            foreach (string route in new[] { "/api/gameplay-options", "/diplomacy/events/next?publicHistory=true#ignored", "/identity/query", "/court/view" })
            {
                Configure(Alias, OtherBackend);
                using (var wire = new FakeWire(request => request.RequestUri.Port == 8099
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new LostBody() }
                    : Reply(HttpStatusCode.OK, "{\"ok\":true}")))
                using (var client = Client(wire))
                {
                    bool get = !route.StartsWith("/identity/") && !route.StartsWith("/court/");
                    JObject result = get ? await ReignHttpTransport.GetJsonAsync(route, client) :
                        route == "/court/view" ? (await ReignHttpTransport.PostDirectJsonAsync(client, route, "{}")).Json : await Post(client, route);
                    Require((bool)result["ok"], "failed to read from fallback");
                    string path = route.Split('?')[0];
                    string method = get ? "GET " : "POST ";
                    wire.Expect(method + OtherBackend + path, method + Alias + path);
                }
            }
        });

        await Check("success teaching the read cache cannot move a later mutation", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.OK, "{\"ok\":true}")))
            using (var client = Client(wire))
            {
                await ReignHttpTransport.GetJsonAsync("/api/gameplay-options", client);
                ReignBetaSettings.Instance.LocalServerUrl = Default;
                await Post(client, "/actions/report");
                wire.Expect("GET " + OtherBackend + "/api/gameplay-options", "POST " + Default + "/actions/report");
            }
        });

        await Check("gossip readiness and portrait operation status stay with the configured backend", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.OK, "{\"ok\":true}")))
            using (var client = Client(wire))
            {
                await ReignHttpTransport.GetJsonAsync("/gossip/status?deliveryOnly=true", client);
                await Post(client, "/gossip/tick");
                ReignServerEndpoint.ReportSuccess(OtherBackend, 0);
                await Post(client, "/portraits/generate/status");
                wire.Expect("GET " + Alias + "/gossip/status", "POST " + Alias + "/gossip/tick",
                    "POST " + Alias + "/portraits/generate/status");
            }

            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => throw new HttpRequestException("status reply lost")))
            using (var client = Client(wire))
            {
                var error = await Throws<HttpRequestException>(() => Post(client, "/portraits/generate/status"));
                Require(error.GetType() == typeof(HttpRequestException), "read failure reported as an unknown mutation");
                wire.Expect("POST " + Alias + "/portraits/generate/status");
            }
        });

        await Check("a lost portrait generation reply recovers the same operation through status", async () =>
        {
            Configure(Alias, OtherBackend);
            string operationId = new string('a', 32);
            var payload = new JObject { ["generationOperationId"] = operationId };
            int generationRequests = 0;
            using (var wire = new FakeWire(request =>
            {
                var received = JObject.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Require((string)received["generationOperationId"] == operationId, "the operation ID changed");
                if (request.RequestUri.AbsolutePath == "/portraits/generate")
                {
                    generationRequests++;
                    throw new TaskCanceledException("provider reply timed out after server receipt");
                }
                Require(generationRequests == 1, "status ran without exactly one generation request");
                return Reply(HttpStatusCode.OK, "{\"ok\":true,\"generationStatus\":\"completed\"}");
            }))
            using (var client = Client(wire))
            {
                await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(() =>
                    ReignHttpTransport.PostAsync("/portraits/generate", payload, null, null, false, null, client));
                await Throws<HttpRequestException>(() => ReignHttpTransport.PostAsync(
                    "/portraits/generate/status", payload, null, null, false, null, client));
                wire.Expect("POST " + Alias + "/portraits/generate");
                ExpireBackoff();
                JObject status = await ReignHttpTransport.PostAsync("/portraits/generate/status", payload,
                    null, null, false, null, client);
                Require((string)status["generationStatus"] == "completed", "the existing operation was not recovered");
                wire.Expect("POST " + Alias + "/portraits/generate", "POST " + Alias + "/portraits/generate/status");
            }
        });

        await Check("configured fragments cannot turn a read into a delivery request", async () =>
        {
            Configure(Alias + "/actions/next#", Default);
            using (var wire = new FakeWire(request => throw new Exception("invalid URL should never be submitted")))
            using (var client = Client(wire))
            {
                var error = await Throws<InvalidOperationException>(() => ReignHttpTransport.GetJsonAsync("/api/gameplay-options", client));
                Require(error.Message.Contains("query or fragment"), "invalid endpoint failure was unclear");
                wire.Expect();
                Require(wire.Health.Count == 0, "invalid endpoint performed a handshake");
            }
        });

        await Check("Court releases a half-open claim when endpoint validation fails", async () =>
        {
            Configure(Alias + "/actions/next#", Default);
            ReignServerEndpoint.ReportTransportFailure(0);
            ExpireBackoff();
            using (var wire = new FakeWire(request => throw new Exception("invalid URL should never be submitted")))
            using (var client = Client(wire))
            {
                await Throws<InvalidOperationException>(() =>
                    ReignHttpTransport.PostDirectJsonAsync(client, "/court/view", "{}", checkAvailability: true));
                Require(ReignServerEndpoint.TryBeginRequest(out string error, out long claim) && claim != 0,
                    "Court stranded the half-open claim: " + error);
                ReignServerEndpoint.ReleaseRequest(claim);
                wire.Expect();
            }
        });

        await Check("a successful response preserves the application error contract", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.Conflict, "known refusal")))
            using (var client = Client(wire))
            {
                var error = await Throws<InvalidOperationException>(() => Post(client, "/actions/report"));
                Require(error.Message == "HTTP 409: known refusal", "HTTP refusal changed");
                wire.Expect("POST " + Alias + "/actions/report");
            }
        });

        await Check("ambiguous HTTP and invalid JSON replies are observable", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.GatewayTimeout, HttpStatusCode.OK })
            {
                Configure(Alias, Default);
                using (var wire = new FakeWire(request => Reply(status, "invalid json")))
                using (var client = Client(wire))
                {
                    await Throws<ReignHttpTransport.UnknownMutationOutcomeException>(() => Post(client, "/actions/report"));
                    wire.Expect("POST " + Alias + "/actions/report");
                }
            }
        });

        await Check("client acceptance callback failure cannot retry even a safe read", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.OK, "{\"ok\":true}")))
            using (var client = Client(wire))
            {
                await Throws<HttpRequestException>(() => Post(client, "/identity/query",
                    response => throw new HttpRequestException("local acceptance failed")));
                wire.Expect("POST " + OtherBackend + "/identity/query");
            }
        });

        await Check("abandoned save owner sends no request", async () =>
        {
            Configure(Alias, OtherBackend);
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.OK, "{}")))
            using (var client = Client(wire))
            {
                await Throws<OperationCanceledException>(() =>
                    ReignHttpTransport.PostAsync("/actions/report", new JObject(), () => Task.FromResult(false),
                        null, false, null, client));
                wire.Expect();
                Require(wire.Health.Count == 0, "abandoned request performed a handshake");
            }
        });

        await Check("closed game scope sends no portrait request", async () =>
        {
            Configure(Alias, OtherBackend);
            var closedScope = new ReignMainThread.GameScope { Closed = true };
            using (var wire = new FakeWire(request => Reply(HttpStatusCode.OK, "{}")))
            using (var client = Client(wire))
            {
                await Throws<OperationCanceledException>(() =>
                    ReignHttpTransport.PostAsync("/portraits/generate", new JObject(), null,
                        () => true, false, null, client, requestScope: closedScope));
                wire.Expect();
                Require(wire.Health.Count == 0, "closed game scope performed a handshake");
            }
        });
    }

    private static HttpClient Client(FakeWire wire) => new HttpClient(ReignHttpTransport.CreateProtocolPipeline(wire));

    private static Task<JObject> Post(HttpClient client, string route, Func<JObject, Task> accepted = null) =>
        ReignHttpTransport.PostAsync(route, new JObject(), null, null, false, accepted, client);

    private static void Configure(string configured, string cached)
    {
        ReignBetaSettings.Instance.LocalServerUrl = configured;
        ReignServerEndpoint.ReportSuccess(cached, 0);
    }

    private static void ExpireBackoff()
    {
        // Expire the fixture's circuit backoff without wall-clock delay.
        typeof(ReignServerEndpoint).GetField("_unavailableUntilUtc", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, DateTime.MinValue);
    }

    private static async Task<T> Throws<T>(Func<Task> act) where T : Exception
    {
        try { await act(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task Check(string name, Func<Task> check)
    {
        try { await check(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { throw new Exception("Failed " + name, error); }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }

    private static HttpResponseMessage Reply(HttpStatusCode code, string body) =>
        new HttpResponseMessage(code) { Content = new StringContent(body) };

    private sealed class FakeWire : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
        private readonly List<string> _requests = new List<string>();
        internal readonly List<string> Health = new List<string>();
        internal bool RefuseHealth;

        internal FakeWire(Func<HttpRequestMessage, HttpResponseMessage> reply) { _reply = reply; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri uri = request.RequestUri;
            string origin = uri.GetLeftPart(UriPartial.Authority);
            HttpResponseMessage response;
            if (uri.AbsolutePath == "/health")
            {
                Health.Add(origin);
                if (RefuseHealth) throw new HttpRequestException("connection refused", new SocketException((int)SocketError.ConnectionRefused));
                response = Reply(HttpStatusCode.OK, "{\"service\":\"BannerlordReignServer\",\"protocolVersion\":1}");
            }
            else
            {
                Require(request.Headers.Contains("X-Reign-Protocol"), "mutation passed without the handshake header");
                _requests.Add(request.Method + " " + origin + uri.AbsolutePath);
                response = _reply(request);
            }
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        internal void Expect(params string[] expected)
        {
            Require(_requests.SequenceEqual(expected), "HTTP dispatches: " + string.Join(", ", _requests));
        }
    }

    private sealed class LostBody : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
            Task.FromException(new IOException("response stream broke after headers"));
        protected override bool TryComputeLength(out long length) { length = 10; return true; }
    }
}
'@

# Compile the shipped transport, endpoint, JSON writer and handshake together.
# Supply fixture implementations only for game-owned dependencies.
$probeFile = Join-Path ([IO.Path]::GetTempPath()) ('ReignClientTransport-' + [Guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($probeFile, $probe)
    $sources = @($probeFile, (Join-Path $integration 'ReignHttpTransport.cs'),
        (Join-Path $integration 'ReignServerEndpoint.cs'), (Join-Path $integration 'ReignJsonHttpContent.cs'),
        (Join-Path $contracts 'ReignProtocolHandler.cs'))
    $references = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)
    $references += [Newtonsoft.Json.Linq.JObject].Assembly.Location
    Add-Type -Path $sources -ReferencedAssemblies $references -CompilerOptions '/nullable:annotations','/nowarn:CS0618,CS1701,SYSLIB0014'
    $null = [ReignClientTransportProbe]::Run().GetAwaiter().GetResult()
}
finally {
    Remove-Item -LiteralPath $probeFile -ErrorAction SilentlyContinue
}
