using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class WorldTools
{
    [McpServerTool(Name = "reign_list_campaigns", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Lists only authoritative real campaigns and their World Test timelines through Reign's existing discovery API.")]
    public static Task<ApiEnvelope> ListCampaigns(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        return api.GetAsync("/world-test/campaigns", cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "reign_get_campaign_storage_audit", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Audits Reign campaign storage against the physical Bannerlord .sav inventory, distinguishing matching saves, stale or overwritten Save Sync registrations, pending final-save retirement, protected imports, orphan roots, and the protected _shared portrait library.")]
    public static Task<ApiEnvelope> GetCampaignStorageAudit(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        return api.GetAsync(
            "/api/campaigns/storage-audit",
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "reign_cleanup_campaigns_without_saves", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Reconciles Save Sync with the physical Bannerlord .sav inventory, permanently deletes audited Reign campaigns with zero matching physical saves, prunes stale registrations from retained campaigns, and removes verified orphan roots while preserving imports awaiting first load and _shared. Requires exact confirmation and Bannerlord to be closed.")]
    public static Task<ApiEnvelope> CleanupCampaignsWithoutSaves(
        ReignApiClient api,
        [Description("Exact text required: delete Reign campaigns with no saves")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        InputGuard.RequireConfirmation(
            confirmation, "delete Reign campaigns with no saves");
        return api.PostAsync("/api/campaigns/cleanup-zero-save", new
        {
            confirmation
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_world_overview", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the World Test overview for one real campaign and timeline, including relationships, diplomacy, rumors, rebellions, actions, checkpoints, health states, and daily changes.")]
    public static Task<ApiEnvelope> GetWorldOverview(
        ReignApiClient api,
        [Description("Real campaign identifier. Empty selects Reign's latest observed campaign.")] string campaignId = "",
        [Description("World-history/Save Sync timeline identifier.")] string timelineId = "main",
        [Description("Optional inclusive first campaign day.")] double? fromDay = null,
        [Description("Optional inclusive last campaign day.")] double? toDay = null,
        [Description("Maximum rollup rows from 10 through 500.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        limit = InputGuard.Range(limit, nameof(limit), 10, 500);
        return api.GetAsync("/world-test/overview", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["timelineId"] = timelineId.Length == 0 ? "main" : timelineId,
            ["fromDay"] = fromDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["toDay"] = toDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_world_details", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns a filtered, paginated World Test drill-down for relationships, Kingdom Leaders, political pressures, rumors, rumor subjects, rebellions, diplomacy, actions, or daily rollups.")]
    public static Task<ApiEnvelope> GetWorldDetails(
        ReignApiClient api,
        [Description("Real campaign identifier. Empty selects Reign's latest observed campaign.")] string campaignId = "",
        [Description("Timeline identifier.")] string timelineId = "main",
        [AllowedValues("relationships", "relationship pair", "kingdom leaders", "political pressures", "rumors", "rumor subjects", "rebellions", "diplomacy", "actions", "daily")]
        [Description("World Test subsystem to inspect.")] string subsystem = "relationships",
        [Description("Optional bounded text filter. For rumor subjects, supply the occurrence ID here.")] string search = "",
        [Description("Optional status filter.")] string status = "",
        [Description("Optional type/tag/source filter.")] string type = "",
        [Description("Required heroA|heroB identifiers for relationship pair diagnostics, including the player. For political pressures, actorKingdomId|targetKingdomId filters activity orientation; incident details contain both sides' pressure changes. Otherwise an optional Kingdom Leaders directional pair filter.")] string pair = "",
        [Description("Optional Kingdom Leaders ruler identifier filter.")] string ruler = "",
        [Description("Optional Kingdom Leaders polarity filter.")] string polarity = "",
        [Description("Optional Kingdom Leaders threshold filter: 30, 50, 70, or 85.")] int? threshold = null,
        [Description("Optional Kingdom Leaders event-type filter.")] string eventType = "",
        [Description("Optional inclusive first campaign day for Kingdom Leaders activity.")] double? fromDay = null,
        [Description("Optional inclusive last campaign day for Kingdom Leaders activity.")] double? toDay = null,
        [Description("One-based page number.")] int page = 1,
        [Description("Rows per page from 10 through 200.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        subsystem = InputGuard.BoundedText(subsystem, nameof(subsystem), 40).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "relationships", "relationship pair", "rumors", "rumor subjects", "rebellions",
            "kingdom leaders", "political pressures", "diplomacy", "actions", "daily"
        };
        if (!allowed.Contains(subsystem))
        {
            throw new ArgumentException("Unsupported World Test subsystem.", nameof(subsystem));
        }
        search = InputGuard.BoundedText(search, nameof(search), 200);
        status = InputGuard.BoundedText(status, nameof(status), 80);
        type = InputGuard.BoundedText(type, nameof(type), 80);
        pair = InputGuard.BoundedText(pair, nameof(pair), 200);
        if (subsystem == "relationship pair")
        {
            var ids = pair.Split('|');
            if (ids.Length != 2 || ids.Any(string.IsNullOrWhiteSpace)
                || ids[0].Equals(ids[1], StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Specify two distinct hero identifiers as heroA|heroB.", nameof(pair));
            foreach (var id in ids) InputGuard.OptionalIdentifier(id, nameof(pair));
        }
        ruler = InputGuard.OptionalIdentifier(ruler, nameof(ruler));
        polarity = InputGuard.BoundedText(polarity, nameof(polarity), 20);
        if (threshold.HasValue && threshold.Value != 30 && threshold.Value != 50
            && threshold.Value != 70 && threshold.Value != 85)
            throw new ArgumentException("Threshold must be 30, 50, 70, or 85.", nameof(threshold));
        eventType = InputGuard.OptionalIdentifier(eventType, nameof(eventType));
        page = InputGuard.Range(page, nameof(page), 1, 100_000);
        pageSize = InputGuard.Range(pageSize, nameof(pageSize), 10, 200);
        return api.GetAsync("/world-test/details", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["timelineId"] = timelineId.Length == 0 ? "main" : timelineId,
            ["subsystem"] = subsystem,
            ["search"] = search,
            ["status"] = status,
            ["type"] = type,
            ["pair"] = pair,
            ["ruler"] = ruler,
            ["polarity"] = polarity,
            ["threshold"] = threshold?.ToString(),
            ["eventType"] = eventType,
            ["fromDay"] = fromDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["toDay"] = toDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_query_world_history", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Queries authoritative Reign world-history events by campaign, timeline, entity, event type, text, and day range.")]
    public static Task<ApiEnvelope> QueryWorldHistory(
        ReignApiClient api,
        string campaignId = "",
        string timelineId = "",
        string entityId = "",
        string eventType = "",
        string text = "",
        double? fromDay = null,
        double? toDay = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        entityId = InputGuard.OptionalIdentifier(entityId, nameof(entityId));
        eventType = InputGuard.OptionalIdentifier(eventType, nameof(eventType));
        text = InputGuard.BoundedText(text, nameof(text), 300);
        limit = InputGuard.Range(limit, nameof(limit), 1, 500);
        return api.GetAsync("/world-history/query", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["timelineId"] = timelineId,
            ["entityId"] = entityId,
            ["eventType"] = eventType,
            ["text"] = text,
            ["fromDay"] = fromDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["toDay"] = toDay?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_world_event", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Loads one authoritative world-history event and its entities and knowledge rules.")]
    public static Task<ApiEnvelope> GetWorldEvent(
        ReignApiClient api,
        string eventId,
        string campaignId = "",
        CancellationToken cancellationToken = default)
    {
        eventId = InputGuard.OptionalIdentifier(eventId, nameof(eventId));
        if (eventId.Length == 0)
        {
            throw new ArgumentException("eventId is required.", nameof(eventId));
        }
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        return api.GetAsync(
            "/world-history/event/" + Uri.EscapeDataString(eventId),
            new Dictionary<string, string?> { ["campaignId"] = campaignId },
            cancellationToken);
    }

    [McpServerTool(Name = "reign_get_world_correlation", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns all world-history events associated with one correlation ID, ordered by authoritative sequence.")]
    public static Task<ApiEnvelope> GetWorldCorrelation(
        ReignApiClient api,
        string correlationId,
        string campaignId = "",
        string timelineId = "",
        CancellationToken cancellationToken = default)
    {
        correlationId = InputGuard.OptionalIdentifier(correlationId, nameof(correlationId));
        if (correlationId.Length == 0)
        {
            throw new ArgumentException("correlationId is required.", nameof(correlationId));
        }
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        return api.GetAsync(
            "/world-history/correlation/" + Uri.EscapeDataString(correlationId),
            new Dictionary<string, string?>
            {
                ["campaignId"] = campaignId,
                ["timelineId"] = timelineId
            },
            cancellationToken);
    }

    [McpServerTool(Name = "reign_get_action_health", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns Reign's authoritative action catalog and bounded recent action failures without proposing, deciding, polling, or executing actions.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetActionHealth(
        ReignApiClient api,
        string campaignId = "",
        int failureLimit = 100,
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        failureLimit = InputGuard.Range(failureLimit, nameof(failureLimit), 1, 500);
        var catalog = api.GetAsync("/actions/catalog", cancellationToken: cancellationToken);
        var failures = api.GetAsync("/actions/failures", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["limit"] = failureLimit.ToString()
        }, cancellationToken);
        await Task.WhenAll(catalog, failures).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        {
            ["catalog"] = await catalog.ConfigureAwait(false),
            ["failures"] = await failures.ConfigureAwait(false)
        };
    }

    [McpServerTool(Name = "reign_list_characters", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Lists Reign character summaries for a campaign. It does not construct, edit, save, roll back, or synchronize any character.")]
    public static Task<ApiEnvelope> ListCharacters(
        ReignApiClient api,
        string campaignId = "",
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        return api.PostAsync("/character-editor/list", new { campaignId }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_relationship_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns compact directional MBTI/native-relationship status for one campaign and timeline without running the relationship director.")]
    public static Task<ApiEnvelope> GetRelationshipStatus(
        ReignApiClient api,
        string campaignId = "",
        string timelineId = "main",
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        return api.PostAsync("/relationships/ambient/status", new
        {
            campaignId,
            timelineId = timelineId.Length == 0 ? "main" : timelineId
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_query_rebellions", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Queries Reign rebellion state through the existing read endpoint. It cannot evaluate, start, join, surrender, judge, or resolve a rebellion.")]
    public static Task<ApiEnvelope> QueryRebellions(
        ReignApiClient api,
        string campaignId = "",
        string timelineId = "main",
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        return api.GetAsync("/rebellions/query", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["timelineId"] = timelineId.Length == 0 ? "main" : timelineId
        }, cancellationToken);
    }
}
