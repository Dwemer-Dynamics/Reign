using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class NarrativeTools
{
    [McpServerTool(Name = "reign_inspect_character_narrative", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads one character's complete base narrative, effective interests, defining selection, development history and catalog diversity. Never materializes or constructs a character and never invokes a provider.")]
    public static Task<ApiEnvelope> Inspect(ReignApiClient api, string heroId, string campaignId = "", CancellationToken cancellationToken = default)
    {
        heroId = InputGuard.OptionalIdentifier(heroId, nameof(heroId));
        if (heroId.Length == 0) throw new ArgumentException("heroId is required.");
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        return api.GetAsync("/characters/narrative", new Dictionary<string, string?> { ["heroId"] = heroId, ["campaignId"] = campaignId }, cancellationToken);
    }

    [McpServerTool(Name = "reign_author_character_narratives", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Authors a resumable batch of 1-25 shipped narratives (up to three profiles concurrently) using installed Character Construction settings, or evaluates staged narratives with operation=evaluate using Dialogue and Character Construction judge settings. Runs an isolated, non-listening successful Release artifact. Both operations require explicit provider-cost confirmation. Writes only under the workspace build root; never activates a catalog, deploys, changes a campaign or starts a listener.")]
    public static async Task<object> Author(ReignMcpOptions options, ReignProcessRunner runner,
        string validationRunId, string authoringRunId, int offset = 0, int count = 25,
        string confirmation = "", string operation = "author", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl || !options.AllowOfflineVerification)
            throw new InvalidOperationException("Narrative authoring requires trusted verification control and isolated verification permission.");
        InputGuard.RequireConfirmation(confirmation, "author character narratives using configured provider");
        if (operation is not ("author" or "evaluate")) throw new ArgumentException("operation must be author or evaluate.");
        authoringRunId ??= string.Empty;
        if (!System.Text.RegularExpressions.Regex.IsMatch(authoringRunId, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$"))
            throw new ArgumentException("authoringRunId must be a simple task name of 1-80 letters, numbers, underscores or hyphens.");
        offset = InputGuard.Range(offset, nameof(offset), 0, 100000);
        count = InputGuard.Range(count, nameof(count), 1, 25);
        var artifact = ReignBuildService.ResolveValidatedServerArtifact(options, validationRunId);
        var root = Path.GetFullPath(Path.Combine(options.BuildRoot, "narrative", authoringRunId));
        ReignMcpOptions.EnsureWithin(Path.Combine(options.BuildRoot, "narrative"), root, nameof(authoringRunId));
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bannerlord Reign", "settings.json");
        if (!File.Exists(settings)) throw new FileNotFoundException("The installed Control Center provider settings are unavailable.");
        Directory.CreateDirectory(root);
        // Serialize writers for one authoring run; separate batches can safely resume
        // after process cancellation without ever touching an active catalog.
        await using var lease = new FileStream(Path.Combine(root, "authoring.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var process = await runner.RunAsync(artifact.ExecutablePath,
            ["--author-character-narratives", "--operation", operation, "--offset", offset.ToString(), "--count", count.ToString(), "--output-root", root, "--json"],
            Path.GetDirectoryName(artifact.ExecutablePath)!, timeout: TimeSpan.FromHours(2), artifactDirectory: root,
            environment: new Dictionary<string, string> {
                ["REIGN_VALIDATION_MODE"] = "1", ["REIGN_DB_NAME"] = "ReignValidation",
                ["REIGN_DATA_ROOT"] = Path.Combine(root, "runtime"), ["REIGN_NARRATIVE_AUTHORING_SETTINGS"] = settings,
                ["REIGN_NARRATIVE_AUTHORING_ROOT"] = root,
                ["REIGN_NARRATIVE_PROVIDER_AUTHORIZED"] = "1" }, cancellationToken: cancellationToken);
        return new { schema = "reign-narrative-authoring-result-v1", validationRunId, authoringRunId,
            artifact.SourceFingerprintSha256, artifact.ExecutableSha256, outputRoot = root, process };
    }
}
