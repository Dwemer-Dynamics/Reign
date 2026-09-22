using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerResourceType]
public static class ReignResources
{
    [McpServerResource(
        UriTemplate = "reign://testing/guide",
        Name = "Reign testing tool guide",
        MimeType = "text/markdown")]
    [Description("The canonical operational guide for Reign testing, campaign automation, evidence, recovery, and Codex restart continuity.")]
    public static string TestingGuide(WorkspaceAccess workspace)
    {
        return workspace.ReadDocumentation(
            Path.Combine("docs", "agent", "TESTING_TOOL_GUIDE.md"));
    }

    [McpServerResource(
        UriTemplate = "reign://testing/catalog",
        Name = "Reign testing capability catalog",
        MimeType = "application/json")]
    [Description("The versioned Reign testing catalog merged with the current callable MCP tool inventory.")]
    public static TextResourceContents TestingCatalog(TestingCatalogService catalog)
    {
        return new TextResourceContents
        {
            Uri = "reign://testing/catalog",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(catalog.GetCatalog(), ReignJson.Indented)
        };
    }

    [McpServerResource(
        UriTemplate = "reign://workspace/roadmap",
        Name = "Reign roadmap",
        MimeType = "text/markdown")]
    [Description("The canonical Reign roadmap. Checkbox state remains authoritative.")]
    public static string Roadmap(WorkspaceAccess workspace)
    {
        return workspace.ReadRoadmap();
    }

    [McpServerResource(
        UriTemplate = "reign://workspace/architecture",
        Name = "Reign MCP architecture",
        MimeType = "text/markdown")]
    [Description("Architecture and trust boundaries for the Reign MCP façade.")]
    public static string Architecture(WorkspaceAccess workspace)
    {
        return workspace.ReadDocumentation(
            Path.Combine("ReignMcp", "docs", "architecture.md"));
    }

    [McpServerResource(
        UriTemplate = "reign://workspace/tool-catalog",
        Name = "Reign MCP tool catalog",
        MimeType = "text/markdown")]
    [Description("Tool purpose, side effects, gates, and excluded capabilities.")]
    public static string ToolCatalog(WorkspaceAccess workspace)
    {
        return workspace.ReadDocumentation(
            Path.Combine("ReignMcp", "docs", "tool-catalog.md"));
    }

    [McpServerResource(
        UriTemplate = "reign://workspace/runtime-extraction-plan",
        Name = "Reign runtime test-tool extraction plan",
        MimeType = "text/markdown")]
    [Description("The staged plan for moving test presentation and orchestration out of the release game while preserving minimal authoritative telemetry.")]
    public static string RuntimeExtractionPlan(WorkspaceAccess workspace)
    {
        return workspace.ReadDocumentation(
            Path.Combine("ReignMcp", "docs", "runtime-extraction-plan.md"));
    }

    [McpServerResource(
        UriTemplate = "reign://runtime/status",
        Name = "Reign runtime status",
        MimeType = "application/json")]
    [Description("A current composite status snapshot from the local Reign server.")]
    public static async Task<TextResourceContents> RuntimeStatus(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        var status = await RuntimeTools.GetStatus(api, cancellationToken).ConfigureAwait(false);
        return new TextResourceContents
        {
            Uri = "reign://runtime/status",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(status, ReignJson.Indented)
        };
    }

    [McpServerResource(
        UriTemplate = "reign://campaign/{campaignId}/world/{timelineId}",
        Name = "Reign campaign World Test overview",
        MimeType = "application/json")]
    [Description("A current World Test overview for one campaign and timeline.")]
    public static async Task<TextResourceContents> CampaignWorld(
        ReignApiClient api,
        string campaignId,
        string timelineId,
        CancellationToken cancellationToken)
    {
        var result = await WorldTools.GetWorldOverview(
            api, campaignId, timelineId, limit: 100, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new TextResourceContents
        {
            Uri = $"reign://campaign/{Uri.EscapeDataString(campaignId)}/world/{Uri.EscapeDataString(timelineId)}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(result, ReignJson.Indented)
        };
    }
}

internal static class ReignJson
{
    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
