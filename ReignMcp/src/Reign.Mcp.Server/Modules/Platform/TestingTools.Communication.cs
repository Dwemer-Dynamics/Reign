using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class CommunicationTools
{
    [McpServerTool(Name = "reign_get_communication_status", ReadOnly = true, Destructive = false,
        Idempotent = true, UseStructuredContent = true)]
    [Description("Reads bounded source-linked message, reply, delivery and summons ledger evidence on one exact campaign/timeline. Never dispatches, calls a provider, advances time or controls the game. Private intended content may appear in diagnostics; native and semantic proof remain separate.")]
    public static Task<ApiEnvelope> GetStatus(ReignApiClient api, string campaignId, string timelineId,
        string heroId = "", int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("An exact campaign and timeline are required.", name)
            : InputGuard.OptionalIdentifier(value, name);
        return api.GetAsync("/communications/status", new Dictionary<string, string?> {
            ["campaignId"] = Required(campaignId, nameof(campaignId)), ["timelineId"] = Required(timelineId, nameof(timelineId)),
            ["heroId"] = InputGuard.OptionalIdentifier(heroId, nameof(heroId)),
            ["offset"] = InputGuard.Range(offset, nameof(offset), 0, 1000000).ToString(),
            ["limit"] = InputGuard.Range(limit, nameof(limit), 1, 100).ToString()
        }, cancellationToken);
    }
}
