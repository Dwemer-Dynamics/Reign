using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool(Name = "reign_get_workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the Reign workspace components, target frameworks, roadmap location, live-test scenarios, and verification documentation without reading runtime data.")]
    public static WorkspaceStatus GetWorkspaceStatus(WorkspaceAccess workspace)
    {
        return workspace.GetStatus();
    }

    [McpServerTool(Name = "reign_search_source", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Searches allowlisted Reign source and documentation files within a confined workspace scope. Runtime data, generated outputs, binaries, logs, portraits, and staging are excluded.")]
    public static SourceSearchResult SearchSource(
        WorkspaceAccess workspace,
        [Description("Literal text to find, at most 300 characters.")] string query,
        [Description("Optional workspace-relative directory such as ReignBetaServer or ReignBeta/src.")] string scope = "",
        [Description("Maximum matches from 1 through 200.")] int maxResults = 50,
        bool caseSensitive = false)
    {
        return workspace.Search(query, scope, maxResults, caseSensitive);
    }

    [McpServerTool(Name = "reign_read_source", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads a bounded line window from an allowlisted Reign source or documentation file. It cannot read runtime data, secrets, generated outputs, binaries, logs, portraits, or staging.")]
    public static SourceDocument ReadSource(
        WorkspaceAccess workspace,
        [Description("Workspace-relative source/document path.")] string path,
        [Description("One-based first line.")] int startLine = 1,
        [Description("Maximum lines from 1 through 1000.")] int lineCount = 200)
    {
        return workspace.ReadSource(path, startLine, lineCount);
    }
}

