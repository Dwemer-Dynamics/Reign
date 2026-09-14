using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

public interface IReignRepositoryHygieneAudit
{
    Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken);
}

internal interface IReignGitClient
{
    Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken);
}

internal sealed record GitCommandResult(
    bool Ok,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated);

public sealed class ReignRepositoryHygieneAudit : IReignRepositoryHygieneAudit
{
    private const int MaxIssues = 500;
    private readonly ReignMcpOptions options;
    private readonly IReignGitClient git;

    public ReignRepositoryHygieneAudit(ReignMcpOptions options)
        : this(options, new ReignGitClient(options.WorkspaceRoot))
    {
    }

    internal ReignRepositoryHygieneAudit(ReignMcpOptions options, IReignGitClient git)
    {
        this.options = options;
        this.git = git;
    }

    public async Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken)
    {
        string policyPath = Path.Combine(options.WorkspaceRoot, "reign.repository.json");
        RepositoryPolicy policy = LoadPolicy(policyPath);
        var issues = new List<RepositoryHygieneIssue>();
        var prohibitedTrackedPaths = new RepositoryGlobSet(policy.ProhibitedTrackedPatterns);
        var secretNames = new RepositoryGlobSet(policy.SecretNamePatterns);
        var largeFileAllowlist = new RepositoryGlobSet(policy.LargeFileAllowlist);
        var layout = new ReignSourceLayout(options.WorkspaceRoot);
        layout.ValidateProjections();

        GitCommandResult rootResult = await git.RunAsync(
            ["rev-parse", "--show-toplevel"], null, cancellationToken).ConfigureAwait(false);
        RequireGitSuccess(rootResult, "locate the repository root");
        string gitRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        if (!PathsEqual(gitRoot, options.WorkspaceRoot))
        {
            AddIssue(issues, "wrong-git-root", ".", "repository-root",
                "The Git root does not match the configured Reign workspace.");
        }

        if (policy.CanonicalRemote is ReignSourceLayout.ClientOrigin or ReignSourceLayout.ServerOrigin)
        {
            GitCommandResult remote = await git.RunAsync(["remote", "get-url", "origin"], null, cancellationToken).ConfigureAwait(false);
            if (!remote.Ok || remote.StandardOutput.Trim() != policy.CanonicalRemote)
                AddIssue(issues, "wrong-publishing-remote", ".git/config", "canonical-remote",
                    "origin must match this repository's authorized Dwemer-Dynamics destination.");
            GitCommandResult pushRemote = await git.RunAsync(["remote", "get-url", "--push", "origin"], null, cancellationToken).ConfigureAwait(false);
            if (!pushRemote.Ok || pushRemote.StandardOutput.Trim() != policy.CanonicalRemote)
                AddIssue(issues, "wrong-publishing-remote", ".git/config", "canonical-push-remote",
                    "The origin push URL must match the authorized destination.");
        }

        GitCommandResult trackedResult = await git.RunAsync(
            ["ls-files", "-z"], null, cancellationToken).ConfigureAwait(false);
        RequireGitSuccess(trackedResult, "list tracked files");
        var tracked = SplitNull(trackedResult.StandardOutput)
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        GitCommandResult statusResult = await git.RunAsync(
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            null, cancellationToken).ConfigureAwait(false);
        RequireGitSuccess(statusResult, "inspect repository status");
        var untracked = ParseUntracked(statusResult.StandardOutput);

        foreach (string required in policy.RequiredTrackedPaths.Select(NormalizePath))
        {
            if (!tracked.Contains(required))
            {
                AddIssue(issues, "required-path-not-tracked", required, "required-path",
                    "A required recovery path is not tracked by Git.");
            }
        }

        foreach (string path in tracked.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (prohibitedTrackedPaths.Matches(path))
            {
                AddIssue(issues, "prohibited-tracked-path", path, "prohibited-path",
                    "A generated, runtime, backup, or reference path is tracked.");
            }
            if (secretNames.Matches(path))
            {
                AddIssue(issues, "secret-named-file", path, "secret-name",
                    "A file with a secret-bearing name is tracked.");
            }

            string absolute = ResolveWorkspacePath(path);
            if (!File.Exists(absolute)) continue;
            long length = new FileInfo(absolute).Length;
            if (length > policy.MaxOrdinaryFileBytes
                && !largeFileAllowlist.Matches(path))
            {
                AddIssue(issues, "oversized-tracked-file", path, "file-size",
                    "A tracked file exceeds the ordinary repository size limit.");
            }

            if (!policy.HumanAuthoredExtensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                || length > policy.MaxOrdinaryFileBytes)
            {
                continue;
            }
            string content;
            try
            {
                content = await File.ReadAllTextAsync(absolute, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                AddIssue(issues, "tracked-file-unreadable", path, "content-scan",
                    "A tracked authored file could not be read for hygiene inspection.");
                continue;
            }
            foreach (SecretContentClassifier classifier in policy.SecretContentClassifiers)
            {
                string allowlistKey = classifier.Id + ":" + path;
                if (policy.SecretContentAllowlist.Contains(
                        allowlistKey, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (Regex.IsMatch(content, classifier.Pattern,
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    AddIssue(issues, "secret-content", path, classifier.Id,
                        "A tracked file matched a secret-content classifier.");
                }
            }
        }

        foreach (string path in untracked.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (IsUnderManagedRoot(path, policy.ManagedSourceRoots)
                && policy.HumanAuthoredExtensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                AddIssue(issues, "untracked-human-authored", path, "authored-extension",
                    "Human-authored source under a managed root is not tracked.");
            }
        }

        RepositoryHygieneReport? serverAudit = null;
        if (layout.IsPaired)
        {
            var serverOptions = options with { WorkspaceRoot = layout.ServerRoot! };
            serverAudit = await new ReignRepositoryHygieneAudit(serverOptions).AuditAsync(cancellationToken).ConfigureAwait(false);
            foreach (var issue in serverAudit.Issues)
                AddIssue(issues, issue.Category, "ReignServer/" + issue.Path, issue.Classifier, issue.Message);
            if (!serverAudit.Enforcement.Equals("enforce", StringComparison.OrdinalIgnoreCase))
                AddIssue(issues, "unenforced-source-repository", "ReignServer/reign.repository.json", "enforcement",
                    "The paired server repository must enforce hygiene.");
        }
        bool ok = issues.Count == 0;
        bool enforce = policy.Enforcement.Equals("enforce", StringComparison.OrdinalIgnoreCase);
        return new RepositoryHygieneReport
        {
            Schema = "reign-repository-hygiene-report-v1",
            Enforcement = policy.Enforcement,
            Ok = ok,
            BlocksValidation = enforce && !ok,
            PolicyPath = policyPath,
            EvidencePath = null,
            GitRoot = gitRoot,
            RepositoryRoots = serverAudit == null ? [gitRoot] : [gitRoot, serverAudit.GitRoot],
            TrackedFileCount = tracked.Count + (serverAudit?.TrackedFileCount ?? 0),
            UntrackedFileCount = untracked.Count + (serverAudit?.UntrackedFileCount ?? 0),
            Issues = issues
        };
    }

    private static RepositoryPolicy LoadPolicy(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("Repository policy is missing: " + path);
        RepositoryPolicy? policy;
        try
        {
            policy = JsonSerializer.Deserialize<RepositoryPolicy>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Repository policy is invalid.", error);
        }
        if (policy is null
            || policy.Schema != "reign-repository-policy-v1"
            || (policy.Enforcement != "audit" && policy.Enforcement != "enforce")
            || policy.MaxOrdinaryFileBytes <= 0)
        {
            throw new InvalidOperationException("Repository policy is incomplete or unsupported.");
        }
        foreach (SecretContentClassifier classifier in policy.SecretContentClassifiers)
        {
            try { _ = new Regex(classifier.Pattern, RegexOptions.CultureInvariant); }
            catch (ArgumentException error)
            {
                throw new InvalidOperationException(
                    $"Repository secret classifier '{classifier.Id}' is invalid.", error);
            }
        }
        return policy;
    }

    private string ResolveWorkspacePath(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(options.WorkspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(options.WorkspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Repository path escaped the workspace root.");
        return path;
    }

    private static void RequireGitSuccess(GitCommandResult result, string operation)
    {
        if (!result.Ok || result.OutputTruncated)
            throw new InvalidOperationException($"Git could not {operation} safely (exit {result.ExitCode}).");
    }

    private static IReadOnlyList<string> ParseUntracked(string status) =>
        SplitNull(status)
            .Where(item => item.StartsWith("?? ", StringComparison.Ordinal))
            .Select(item => NormalizePath(item[3..]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> SplitNull(string value) =>
        value.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').Trim();

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderManagedRoot(string path, IReadOnlyList<string> roots) =>
        roots.Any(root =>
        {
            string normalized = NormalizePath(root).TrimEnd('/');
            return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
        });

    private static void AddIssue(List<RepositoryHygieneIssue> issues, string category,
        string path, string classifier, string message)
    {
        if (issues.Count >= MaxIssues) return;
        issues.Add(new RepositoryHygieneIssue
        {
            Category = category,
            Path = path,
            Classifier = classifier,
            Message = message
        });
    }

    private sealed record RepositoryPolicy
    {
        public string Schema { get; init; } = string.Empty;
        public string Enforcement { get; init; } = string.Empty;
        public string CanonicalRemote { get; init; } = string.Empty;
        public IReadOnlyList<string> RequiredTrackedPaths { get; init; } = [];
        public IReadOnlyList<string> ManagedSourceRoots { get; init; } = [];
        public IReadOnlyList<string> ProhibitedTrackedPatterns { get; init; } = [];
        public IReadOnlyList<string> HumanAuthoredExtensions { get; init; } = [];
        public IReadOnlyList<string> SecretNamePatterns { get; init; } = [];
        public IReadOnlyList<SecretContentClassifier> SecretContentClassifiers { get; init; } = [];
        public IReadOnlyList<string> SecretContentAllowlist { get; init; } = [];
        public long MaxOrdinaryFileBytes { get; init; }
        public IReadOnlyList<string> LargeFileAllowlist { get; init; } = [];
    }

    private sealed record SecretContentClassifier
    {
        public string Id { get; init; } = string.Empty;
        public string Pattern { get; init; } = string.Empty;
    }
}

internal sealed class RepositoryGlobSet
{
    private readonly Regex[] patterns;

    public RepositoryGlobSet(IEnumerable<string> patterns)
    {
        this.patterns = patterns.Select(Compile).ToArray();
    }

    public bool Matches(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/').Trim();
        return patterns.Any(pattern => pattern.IsMatch(normalized));
    }

    private static Regex Compile(string pattern)
    {
        string normalized = pattern.Replace('\\', '/').TrimStart('/').Trim();
        var expression = new StringBuilder("^");
        for (int index = 0; index < normalized.Length; index++)
        {
            char current = normalized[index];
            if (current == '*' && index + 1 < normalized.Length && normalized[index + 1] == '*')
            {
                bool followedBySlash = index + 2 < normalized.Length && normalized[index + 2] == '/';
                expression.Append(followedBySlash ? "(?:.*/)?" : ".*");
                index += followedBySlash ? 2 : 1;
            }
            else if (current == '*') expression.Append("[^/]*");
            else if (current == '?') expression.Append("[^/]");
            else expression.Append(Regex.Escape(current.ToString()));
        }
        expression.Append('$');
        return new Regex(expression.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }
}

internal sealed class ReignGitClient(string workspaceRoot) : IReignGitClient
{
    private const int MaxOutputChars = 32 * 1024 * 1024;

    public async Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments,
        string? standardInput, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Git failed to start.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        bool truncated = stdout.Length > MaxOutputChars || stderr.Length > MaxOutputChars;
        if (stdout.Length > MaxOutputChars) stdout = stdout[..MaxOutputChars];
        if (stderr.Length > MaxOutputChars) stderr = stderr[..MaxOutputChars];
        return new GitCommandResult(process.ExitCode == 0, process.ExitCode,
            stdout, stderr, truncated);
    }
}
