using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class ActionProbeTools
{
    private const string Confirmation =
        "start Reign action batch on disposable save";
    private const string CooperationDirective =
        "For this one test turn, willingly accept the player's clear, feasible present request. Keep all world facts, identities, ownership, authority, and required terms accurate. If the request is negated, hypothetical, quoted, ambiguous, or beyond your power, do not agree to an action. Reply naturally as this character.";

    [McpServerTool(Name = "reign_start_action_batch", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts up to eight natural-language Individual Chat action probes on the exact armed campaign-test Current. Each case opens production chat, captures native before/after state, sends the unchanged player wording, and closes the session. The server may apply only a one-turn cooperation scope, never a direct action command.")]
    public static async Task<IReadOnlyDictionary<string, object?>> StartActionBatch(
        ReignApiClient api, ReignMcpOptions options,
        CampaignTestService campaignTests, string campaignId,
        string campaignTestRunId, string batchId, string casesJson,
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException(
                "Action probes require the MCP verification-control gate.");
        InputGuard.RequireConfirmation(confirmation, Confirmation);
        campaignId = RequiredId(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredId(campaignTestRunId,
            nameof(campaignTestRunId));
        batchId = RequiredId(batchId, nameof(batchId));
        casesJson = InputGuard.BoundedText(casesJson, nameof(casesJson), 16000);
        List<ActionProbeCase>? cases = JsonSerializer.Deserialize<List<ActionProbeCase>>(
            casesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (cases is null || cases.Count is < 1 or > 8)
            throw new ArgumentException("An action batch requires 1 to 8 cases.",
                nameof(casesJson));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ActionProbeCase item in cases)
        {
            item.CaseId = RequiredId(item.CaseId, "caseId");
            item.ActorHeroId = RequiredId(item.ActorHeroId, "actorHeroId");
            item.ExpectedCommand = RequiredId(item.ExpectedCommand,
                "expectedCommand");
            item.PlayerText = InputGuard.BoundedText(item.PlayerText,
                "playerText", 1200);
            if (item.PlayerText.Length < 3 || !seen.Add(item.CaseId))
                throw new ArgumentException(
                    "Every case needs distinct identity and nonempty player text.",
                    nameof(casesJson));
            if (item.NegativeControl && item.CooperationOverride)
                throw new ArgumentException(
                    "Negative controls cannot use the cooperation override.",
                    nameof(casesJson));
            item.ObservedItemIds = (item.ObservedItemIds ?? new List<string>())
                .Select(value => RequiredId(value, "observedItemIds"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (item.ObservedItemIds.Count > 16)
                throw new ArgumentException(
                    "A case can observe at most 16 exact item IDs.",
                    nameof(casesJson));
        }

        ApiEnvelope catalog = await api.GetAsync("/actions/catalog",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!catalog.Ok || !catalog.Data.HasValue
            || !catalog.Data.Value.TryGetProperty("commands", out JsonElement commands)
            || commands.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                "The authoritative action catalog is unavailable.");
        var allowed = commands.EnumerateArray()
            .Where(command => command.TryGetProperty("family", out JsonElement family)
                && !string.Equals(family.GetString(), "diplomacy",
                    StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                command => command.GetProperty("command").GetString() ?? "",
                command => command.GetProperty("mapsTo").GetString() ?? "",
                StringComparer.OrdinalIgnoreCase);
        if (cases.Any(item => !allowed.ContainsKey(item.ExpectedCommand)))
            throw new ArgumentException(
                "Every expected command must be in the live non-diplomacy catalog.",
                nameof(casesJson));

        CampaignTestAuthorization authorization =
            await campaignTests.RequireArmedOwnedSaveAsync(campaignTestRunId,
                campaignId, cancellationToken).ConfigureAwait(false);
        string liveRunId = "action-" + batchId + "-"
            + Guid.NewGuid().ToString("N")[..8];
        var steps = new List<object>();
        foreach (ActionProbeCase item in cases)
        {
            steps.Add(new { operation = "open", targetSearch = item.ActorHeroId,
                actionProbeCaseId = item.CaseId, timeoutSeconds = 120 });
            steps.Add(new { operation = "gauntlet_snapshot", gauntletRunId = batchId,
                heroIds = new[] { item.ActorHeroId }, actionProbeCaseId = item.CaseId,
                actionProbePhase = "before", itemIds = item.ObservedItemIds,
                timeoutSeconds = 120 });
            steps.Add(new
            {
                operation = "send", text = item.PlayerText,
                naturalLanguageCase = true,
                actionAcceptanceProbe = true,
                actionProbeCaseId = item.CaseId,
                expectedCommand = item.ExpectedCommand,
                negativeControl = item.NegativeControl,
                expectedSaveName = authorization.CurrentSaveName,
                guardedPromptOverride = item.CooperationOverride,
                guardedPromptOverrideDirective = item.CooperationOverride
                    ? CooperationDirective : "",
                timeoutSeconds = 300
            });
            if (!item.NegativeControl)
                steps.Add(new { operation = "action_probe_wait",
                    gauntletRunId = batchId, actionProbeCaseId = item.CaseId,
                    expectedType = allowed[item.ExpectedCommand],
                    waitSeconds = 15, timeoutSeconds = 30 });
            steps.Add(new { operation = "gauntlet_snapshot", gauntletRunId = batchId,
                heroIds = new[] { item.ActorHeroId }, actionProbeCaseId = item.CaseId,
                actionProbePhase = "after", itemIds = item.ObservedItemIds,
                timeoutSeconds = 120 });
            steps.Add(new { operation = "close", actionProbeCaseId = item.CaseId,
                timeoutSeconds = 120 });
        }

        ApiEnvelope started = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId,
            mode = "individual_chat", label = "MCP action batch " + batchId,
            presentation = "visible", effects = "full",
            autoCompleteWhenIdle = true, gauntletRunId = batchId,
            enrollment = new
            {
                campaignId, campaignTestRunId,
                disposableSaveName = authorization.CurrentSaveName,
                protectedBaselineSaveName = authorization.BaselineSaveName,
                timelineId = authorization.TimelineId,
                source = "reign_mcp_action_probe"
            },
            steps
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["ok"] = started.Ok,
            ["batchId"] = batchId,
            ["liveRunId"] = liveRunId,
            ["caseCount"] = cases.Count,
            ["expectedSaveName"] = authorization.CurrentSaveName,
            ["start"] = started
        };
    }

    private static string RequiredId(string? value, string name)
    {
        string result = InputGuard.OptionalIdentifier(value, name);
        if (result.Length == 0)
            throw new ArgumentException($"{name} is required.", name);
        return result;
    }

    public sealed class ActionProbeCase
    {
        public string CaseId { get; set; } = "";
        public string ActorHeroId { get; set; } = "";
        public string ExpectedCommand { get; set; } = "";
        public string PlayerText { get; set; } = "";
        public bool NegativeControl { get; set; }
        public bool CooperationOverride { get; set; }
        public List<string> ObservedItemIds { get; set; } = new();
    }
}
