using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class RuntimeTools
{
    [McpServerTool(Name = "reign_get_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the Reign MCP server's enforced boundaries, configured workspace/server targets, and whether gated build or verification controls are enabled.")]
    public static CapabilityManifest GetCapabilities(ReignMcpOptions options)
    {
        return new CapabilityManifest
        {
            ServerName = "reign",
            Version = "0.2.0",
            WorkspaceRoot = options.WorkspaceRoot,
            ReignServer = options.ServerBaseUri.ToString().TrimEnd('/'),
            Transport = "stdio",
            BuildEnabled = options.AllowBuild,
            RestoreEnabled = options.AllowRestore,
            VerificationControlEnabled = options.AllowVerificationControl,
            OfflineVerificationEnabled = options.AllowOfflineVerification,
            EnforcedProhibitions =
            [
                "No Reign or Bannerlord deployment",
                "No arbitrary server/game start, restart, or activation; Reign server shutdown and exact disposable-save Bannerlord startup are limited to confirmation-gated lifecycle tools",
                "No arbitrary HTTP proxy",
                "No arbitrary shell command",
                "No campaign deletion, import, rollback, Save Sync mutation, or native action execution",
                "No direct access to API keys or runtime data files"
            ],
            DataBoundaries =
            [
                "Reign API origin must be numeric loopback HTTP",
                "Source reads are workspace-confined and extension allowlisted",
                "Runtime data, staging, build output, logs, portraits, bin, and obj are excluded from source reads",
                "API responses are size-bounded and secret-redacted",
                "Build outputs are redirected to the configured workspace build root; standard MSBuild obj intermediates remain inside their source projects"
            ]
        };
    }

    [McpServerTool(Name = "reign_get_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns a bounded composite status for the Reign server, provider middleware, background queues, memory worker, telemetry, and prompt cache. If Reign is offline, returns only the failed health probe.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetStatus(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        var health = await api.GetAsync("/health", cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, ApiEnvelope>(StringComparer.OrdinalIgnoreCase)
        {
            ["health"] = health
        };
        if (!health.Ok)
        {
            return result;
        }

        var probes = new Dictionary<string, Task<ApiEnvelope>>(StringComparer.OrdinalIgnoreCase)
        {
            ["diagnostics"] = api.GetAsync("/api/diagnostics", cancellationToken: cancellationToken),
            ["provider"] = api.GetAsync("/api/provider/status", cancellationToken: cancellationToken),
            ["background"] = api.GetAsync("/api/background/status", cancellationToken: cancellationToken),
            ["memoryWorker"] = api.GetAsync("/memory/background/status", cancellationToken: cancellationToken),
            ["telemetry"] = api.GetAsync("/api/telemetry/status", cancellationToken: cancellationToken),
            ["promptCache"] = api.GetAsync("/llm/prompt-cache/status", cancellationToken: cancellationToken)
        };
        await Task.WhenAll(probes.Values).ConfigureAwait(false);
        foreach (var pair in probes)
        {
            result[pair.Key] = await pair.Value.ConfigureAwait(false);
        }
        return result;
    }

    [McpServerTool(Name = "reign_shutdown_server", ReadOnly = false, Destructive = true,
        Idempotent = true, UseStructuredContent = true)]
    [Description("Gracefully shuts down the supported visible unified Reign server lifetime group without using the Control Center confirmation dialog. Refuses non-unified server instances, requires verification control plus exact confirmation, and verifies that the health endpoint goes offline.")]
    public static async Task<IReadOnlyDictionary<string, object>> ShutdownServer(
        ReignApiClient api,
        ReignMcpOptions options,
        [Description("Exact text required: shut down Reign server lifetime group")]
        string confirmation = "",
        [Description("Seconds to wait for the coordinated lifetime group to stop, from 5 through 60.")]
        int waitSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException(
                "Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "shut down Reign server lifetime group");
        waitSeconds = InputGuard.Range(waitSeconds, nameof(waitSeconds), 5, 60);

        ApiEnvelope before = await api.GetAsync("/health", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!before.Ok)
        {
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["status"] = "already_stopped",
                ["healthOfflineVerified"] = true,
                ["before"] = before
            };
        }
        if (!HealthFlag(before, "unifiedControlCenter")
            || !HealthFlag(before, "allProcessesCloseTogether"))
            throw new InvalidOperationException(
                "Reign is not running as the supported visible unified lifetime group; shutdown was refused.");

        ApiEnvelope accepted = await api.PostAsync("/api/shutdown", new { }, cancellationToken)
            .ConfigureAwait(false);
        if (!accepted.Ok)
        {
            return new Dictionary<string, object>
            {
                ["ok"] = false,
                ["status"] = "shutdown_rejected",
                ["healthOfflineVerified"] = false,
                ["before"] = before,
                ["accepted"] = accepted
            };
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        ApiEnvelope after = before;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            after = await api.GetAsync("/health", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!after.Ok)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["status"] = "stopped",
                    ["healthOfflineVerified"] = true,
                    ["unifiedLifetimeGroupOwned"] = true,
                    ["before"] = before,
                    ["accepted"] = accepted,
                    ["after"] = after
                };
            }
        }

        return new Dictionary<string, object>
        {
            ["ok"] = false,
            ["status"] = "stop_timeout",
            ["healthOfflineVerified"] = false,
            ["unifiedLifetimeGroupOwned"] = true,
            ["before"] = before,
            ["accepted"] = accepted,
            ["after"] = after,
            ["error"] = "The Reign server accepted shutdown but remained healthy after " + waitSeconds + " seconds."
        };
    }

    private static bool HealthFlag(ApiEnvelope envelope, string propertyName)
    {
        return envelope.Data is JsonElement data
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.True;
    }

    [McpServerTool(Name = "reign_get_prompt_inventory", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns Reign's editable prompt inventory and current prompt-cache status. Values are passed through Reign's UI-safe API and redacted again by this MCP server.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetPromptInventory(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        var prompts = api.GetAsync("/api/prompts", cancellationToken: cancellationToken);
        var cache = api.GetAsync("/llm/prompt-cache/status", cancellationToken: cancellationToken);
        await Task.WhenAll(prompts, cache).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        {
            ["prompts"] = await prompts.ConfigureAwait(false),
            ["cache"] = await cache.ConfigureAwait(false)
        };
    }

    [McpServerTool(Name = "reign_get_prompt_evidence", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads a redacted page of a complete prompt captured before a provider attempt or during production Test Lab assembly. captureStage distinguishes them. Pin evidenceId when paging; missing or expired evidence is explicitly unavailable. Never invokes a provider or changes the campaign.")]
    public static Task<ApiEnvelope> GetPromptEvidence(
        ReignApiClient api,
        [Description("Exact conversation correlation identifier.")] string correlationId,
        [Description("Optional campaign identifier; defaults to the current log campaign.")] string campaignId = "",
        [Description("Exact evidence identifier returned by the first page; empty selects the latest captured attempt.")] string evidenceId = "",
        [Description("Zero-based message index, from 0 through 99.")] int messageIndex = 0,
        [Description("Zero-based character offset in the redacted message.")] int offset = 0,
        [Description("Page length from 1 through 12000 characters.")] int length = 4000,
        CancellationToken cancellationToken = default)
    {
        correlationId = InputGuard.OptionalIdentifier(correlationId, nameof(correlationId));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("An exact correlationId is required.", nameof(correlationId));
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        evidenceId = InputGuard.OptionalIdentifier(evidenceId, nameof(evidenceId));
        InputGuard.Range(messageIndex, nameof(messageIndex), 0, 99);
        InputGuard.Range(offset, nameof(offset), 0, 10000000);
        InputGuard.Range(length, nameof(length), 1, 12000);
        return api.GetAsync("/audit/prompt", new Dictionary<string, string?> {
            ["campaignId"] = campaignId, ["correlationId"] = correlationId, ["evidenceId"] = evidenceId,
            ["messageIndex"] = messageIndex.ToString(), ["offset"] = offset.ToString(), ["length"] = length.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_query_logs", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Queries bounded Reign operational logs through the existing API. Secrets are redacted. This tool cannot clear or export logs.")]
    public static Task<ApiEnvelope> QueryLogs(
        ReignApiClient api,
        [Description("Optional case-insensitive text filter, at most 200 characters.")] string search = "",
        [Description("Maximum rows from 1 through 500.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        search = InputGuard.BoundedText(search, nameof(search), 200);
        limit = InputGuard.Range(limit, nameof(limit), 1, 500);
        return api.GetAsync("/api/logs", new Dictionary<string, string?>
        {
            ["search"] = search,
            ["limit"] = limit.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_query_audit", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Queries Reign's bounded audit ledger by campaign, correlation, category, or text. This tool never ingests or alters audit records.")]
    public static Task<ApiEnvelope> QueryAudit(
        ReignApiClient api,
        [Description("Optional campaign identifier.")] string campaignId = "",
        [Description("Optional correlation identifier.")] string correlationId = "",
        [Description("Optional audit category/type.")] string type = "",
        [Description("Optional bounded text search.")] string search = "",
        [Description("Maximum rows from 1 through 500.")] int limit = 100,
        [Description("Return oldest matching records first.")] bool oldestFirst = false,
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        correlationId = InputGuard.OptionalIdentifier(correlationId, nameof(correlationId));
        type = InputGuard.OptionalIdentifier(type, nameof(type));
        search = InputGuard.BoundedText(search, nameof(search), 200);
        limit = InputGuard.Range(limit, nameof(limit), 1, 500);
        return api.GetAsync("/audit/query", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["correlationId"] = correlationId,
            ["type"] = type,
            ["search"] = search,
            ["limit"] = limit.ToString(),
            ["oldestFirst"] = oldestFirst.ToString().ToLowerInvariant()
        }, cancellationToken);
    }
}
