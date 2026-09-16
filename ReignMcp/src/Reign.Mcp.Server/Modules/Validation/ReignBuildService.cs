using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

public sealed class ReignBuildService(
    ReignMcpOptions options,
    ReignProcessRunner runner,
    ReignApiClient apiClient,
    ReignProjectCatalog catalog)
{
    private static readonly Regex ValidationRunIdPattern = new(
        "^[0-9]{8}-[0-9]{6}-[0-9a-fA-F]{8}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<BuildBatchResult> BuildAsync(
        string component,
        string configuration,
        bool restore,
        CancellationToken cancellationToken)
    {
        if (!options.AllowBuild)
        {
            throw new InvalidOperationException(
                "Build tools are disabled. Set REIGN_MCP_ALLOW_BUILD=true in the trusted MCP configuration.");
        }
        if (restore && !options.AllowRestore)
        {
            throw new InvalidOperationException(
                "Package restore is disabled. Set REIGN_MCP_ALLOW_RESTORE=true only when network/package-cache writes are intended.");
        }

        component = component.Trim().ToLowerInvariant();
        configuration = configuration.Trim();
        if (configuration is not ("Debug" or "Release"))
        {
            throw new ArgumentException("configuration must be Debug or Release.", nameof(configuration));
        }

        var catalogProfile = component is "core" or "product" or "tooling" or "all"
            ? component
            : "all";
        var plan = catalog.CreatePlan(catalogProfile, configuration, restore);
        if (!plan.CoverageComplete)
        {
            throw new InvalidOperationException(
                "The Reign project catalog has unclassified projects. Use reign_get_validation_plan to resolve coverage before building.");
        }
        var selected = component is "core" or "product" or "tooling" or "all"
            ? plan.Projects
            : plan.Projects
                .Where(project => project.Id.Equals(component, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (selected.Count == 0)
        {
            throw new ArgumentException(
                "component must be a discovered project ID, core, product, tooling, or all.",
                nameof(component));
        }

        var runRoot = Path.Combine(
            options.BuildRoot,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(runRoot);
        var results = new List<ProcessResult>();

        foreach (var selectedProject in selected)
        {
            var project = Path.Combine(
                options.WorkspaceRoot,
                selectedProject.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
            var output = Path.Combine(runRoot, selectedProject.Id, "out");
            Directory.CreateDirectory(output);
            var arguments = new List<string>
            {
                "build",
                project,
                "-c",
                configuration,
                restore ? "--restore" : "--no-restore",
                $"-p:OutputPath={Path.TrimEndingDirectorySeparator(output)}{Path.DirectorySeparatorChar}",
                "-p:AppendTargetFrameworkToOutputPath=false",
                "-p:AppendRuntimeIdentifierToOutputPath=false"
            };
            var result = await runner.RunAsync(
                "dotnet",
                arguments,
                options.WorkspaceRoot,
                artifactDirectory: output,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (!result.Ok)
            {
                break;
            }
        }

        return new BuildBatchResult
        {
            Ok = results.Count == selected.Count && results.All(result => result.Ok),
            Configuration = configuration,
            ArtifactRoot = runRoot,
            Results = results
        };
    }

    public async Task<OfflineVerificationResult> RunOfflineVerificationAsync(
        string tier,
        string suite,
        int seed,
        int repeat,
        bool failFast,
        string validationRunId,
        string confirmation,
        CancellationToken cancellationToken)
    {
        if (!options.AllowOfflineVerification)
        {
            throw new InvalidOperationException(
                "Offline verification execution is disabled. Set REIGN_MCP_ALLOW_OFFLINE_VERIFICATION=true in the trusted MCP configuration.");
        }
        InputGuard.RequireConfirmation(confirmation, "run isolated offline verification");
        tier = tier.Trim().ToLowerInvariant();
        if (tier is not ("quick" or "offline"))
        {
            throw new ArgumentException(
                "The MCP CLI runner permits only quick or offline tiers. Live-LLM and game tiers require explicit Reign UI/test-control workflows.",
                nameof(tier));
        }
        suite = InputGuard.OptionalIdentifier(suite, nameof(suite));
        seed = InputGuard.Range(seed, nameof(seed), 0, int.MaxValue);
        repeat = InputGuard.Range(repeat, nameof(repeat), 1, 100);

        var active = await apiClient.GetAsync("/verification/status", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (active.Ok
            && active.Data is { } status
            && status.TryGetProperty("running", out var running)
            && running.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            throw new InvalidOperationException(
                "The live Reign server already has an active verification run. Wait for it to finish before starting an isolated CLI run.");
        }

        var artifact = ResolveValidatedServerArtifact(options, validationRunId);
        var executable = artifact.ExecutablePath;

        var arguments = new List<string>
        {
            "--run-verification", "--tier", tier, "--seed", seed.ToString(),
            "--repeat", repeat.ToString(), "--json"
        };
        if (suite.Length > 0)
        {
            arguments.Add("--suite");
            arguments.Add(suite);
        }
        if (failFast)
        {
            arguments.Add("--fail-fast");
        }
        var process = await runner.RunAsync(
            executable,
            arguments,
            Path.GetDirectoryName(executable)!,
            artifactDirectory: Path.Combine(Path.GetDirectoryName(executable)!, "data", "tests"),
            environment: new Dictionary<string, string>
            {
                ["REIGN_VALIDATION_MODE"] = "1",
                ["REIGN_DB_NAME"] = "ReignValidation"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new OfflineVerificationResult
        {
            Schema = "reign-offline-verification-result-v1",
            ValidationRunId = artifact.ValidationRunId,
            ValidationReportPath = artifact.ValidationReportPath,
            SourceFingerprintSha256 = artifact.SourceFingerprintSha256,
            ExecutablePath = artifact.ExecutablePath,
            ExecutableSha256 = artifact.ExecutableSha256,
            Process = process
        };
    }

    internal static ValidatedServerArtifact ResolveValidatedServerArtifact(
        ReignMcpOptions options,
        string validationRunId)
    {
        validationRunId = validationRunId?.Trim() ?? string.Empty;
        if (!ValidationRunIdPattern.IsMatch(validationRunId))
        {
            throw new ArgumentException(
                "validationRunId must be an exact canonical validation run ID such as 20260905-043544-696f877f.",
                nameof(validationRunId));
        }

        var validationRoot = Path.GetFullPath(Path.Combine(options.BuildRoot, "validation"));
        var runRoot = Path.GetFullPath(Path.Combine(validationRoot, validationRunId));
        ReignMcpOptions.EnsureWithin(validationRoot, runRoot, nameof(validationRunId));
        var reportPath = Path.Combine(runRoot, "validation-report.json");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException(
                "The selected canonical validation report was not found. Run reign_validate first and pass its exact runId.",
                reportPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
        var report = document.RootElement;
        RequireString(report, "Schema", "reign-validation-report-v4", reportPath);
        RequireString(report, "RunId", validationRunId, reportPath);
        RequireTrue(report, "Ok", reportPath);

        var recordedArtifactRoot = Path.GetFullPath(
            RequireString(report, "ArtifactRoot", reportPath));
        if (!string.Equals(recordedArtifactRoot, runRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The validation report artifact root does not match its canonical validation run directory.");
        }
        var recordedReportPath = Path.GetFullPath(
            RequireString(report, "ReportPath", reportPath));
        if (!string.Equals(recordedReportPath, reportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The validation report path does not match its canonical validation run directory.");
        }

        var plan = report.GetProperty("Plan");
        RequireString(plan, "Configuration", "Release", reportPath);
        RequireTrue(plan, "CoverageComplete", reportPath);
        if (!plan.GetProperty("Projects").EnumerateArray().Any(project =>
                string.Equals(RequireString(project, "Id", reportPath), "server",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The selected validation report did not build the Reign server project.");
        }
        if (report.GetProperty("Results").EnumerateArray().Any(result =>
                !result.GetProperty("Process").GetProperty("Ok").GetBoolean()))
        {
            throw new InvalidDataException(
                "The selected validation report contains a failed process result.");
        }

        var executable = Path.GetFullPath(Path.Combine(runRoot, "server", "out", "ReignBetaServer.dll"));
        ReignMcpOptions.EnsureWithin(runRoot, executable, "validated Reign server executable");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "The selected successful validation run no longer contains its Reign server executable.",
                executable);
        }

        using var stream = File.OpenRead(executable);
        using var sha256 = SHA256.Create();
        var executableHash = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        return new ValidatedServerArtifact(
            validationRunId,
            reportPath,
            RequireString(report, "SourceFingerprintSha256", reportPath),
            executable,
            executableHash);
    }

    private static string RequireString(
        JsonElement element,
        string propertyName,
        string reportPath)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"The validation report is missing required string {propertyName}: {reportPath}");
        }
        return property.GetString()!;
    }

    private static void RequireString(
        JsonElement element,
        string propertyName,
        string expected,
        string reportPath)
    {
        var actual = RequireString(element, propertyName, reportPath);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The validation report has unsupported {propertyName} '{actual}'; expected '{expected}': {reportPath}");
        }
    }

    private static void RequireTrue(
        JsonElement element,
        string propertyName,
        string reportPath)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not JsonValueKind.True)
        {
            throw new InvalidDataException(
                $"The validation report must record {propertyName}=true: {reportPath}");
        }
    }
}

public sealed record OfflineVerificationResult
{
    public required string Schema { get; init; }
    public required string ValidationRunId { get; init; }
    public required string ValidationReportPath { get; init; }
    public required string SourceFingerprintSha256 { get; init; }
    public required string ExecutablePath { get; init; }
    public required string ExecutableSha256 { get; init; }
    public required ProcessResult Process { get; init; }
}

internal sealed record ValidatedServerArtifact(
    string ValidationRunId,
    string ValidationReportPath,
    string SourceFingerprintSha256,
    string ExecutablePath,
    string ExecutableSha256);
