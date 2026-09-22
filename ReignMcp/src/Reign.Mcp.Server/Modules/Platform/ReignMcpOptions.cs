using System.Net;

namespace Reign.Mcp.Server;

public sealed record ReignMcpOptions
{
    public required string WorkspaceRoot { get; init; }
    public required Uri ServerBaseUri { get; init; }
    public required string BuildRoot { get; init; }
    public bool AllowBuild { get; init; }
    public bool AllowRestore { get; init; }
    public bool AllowVerificationControl { get; init; }
    public bool AllowOfflineVerification { get; init; }
    public int MaxApiResponseBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxToolTextBytes { get; init; } = 512 * 1024;
    public TimeSpan ApiTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public static ReignMcpOptions FromEnvironment()
    {
        var workspace = ResolveWorkspaceRoot(
            Environment.GetEnvironmentVariable("REIGN_WORKSPACE_ROOT"));
        var baseUri = ParseLoopbackUri(
            Environment.GetEnvironmentVariable("REIGN_SERVER_URL")
            ?? "http://127.0.0.1:5101");
        var buildRoot = Path.GetFullPath(Path.Combine(
            workspace,
            Environment.GetEnvironmentVariable("REIGN_MCP_BUILD_ROOT")
                ?? Path.Combine(".codex-build", "reign-mcp")));

        EnsureWithin(workspace, buildRoot, "REIGN_MCP_BUILD_ROOT");
        return new ReignMcpOptions
        {
            WorkspaceRoot = workspace,
            ServerBaseUri = baseUri,
            BuildRoot = buildRoot,
            AllowBuild = ReadBoolean("REIGN_MCP_ALLOW_BUILD", false),
            AllowRestore = ReadBoolean("REIGN_MCP_ALLOW_RESTORE", false),
            AllowVerificationControl = ReadBoolean("REIGN_MCP_ALLOW_VERIFICATION_CONTROL", false),
            AllowOfflineVerification = ReadBoolean("REIGN_MCP_ALLOW_OFFLINE_VERIFICATION", false),
            MaxApiResponseBytes = ReadBoundedInt("REIGN_MCP_MAX_API_BYTES", 4 * 1024 * 1024, 64 * 1024, 16 * 1024 * 1024),
            MaxToolTextBytes = ReadBoundedInt("REIGN_MCP_MAX_TEXT_BYTES", 512 * 1024, 16 * 1024, 2 * 1024 * 1024),
            ApiTimeout = TimeSpan.FromSeconds(ReadBoundedInt("REIGN_MCP_API_TIMEOUT_SECONDS", 30, 2, 300)),
            ProcessTimeout = TimeSpan.FromSeconds(ReadBoundedInt("REIGN_MCP_PROCESS_TIMEOUT_SECONDS", 1800, 30, 7200))
        };
    }

    internal static Uri ParseLoopbackUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "REIGN_SERVER_URL must be a plain HTTP loopback origin such as http://127.0.0.1:5101.");
        }

        if (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                "REIGN_SERVER_URL must use a numeric loopback address. Remote Reign servers are intentionally unsupported.");
        }

        return uri;
    }

    internal static void EnsureWithin(string root, string candidate, string label)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label} must stay within the Reign workspace.");
        }
    }

    private static string ResolveWorkspaceRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var result = Path.GetFullPath(configured);
            ValidateWorkspace(result);
            return result;
        }

        foreach (var origin in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var cursor = new DirectoryInfo(origin);
            for (var depth = 0; cursor is not null && depth < 12; depth++, cursor = cursor.Parent)
            {
                if (File.Exists(Path.Combine(cursor.FullName, "REIGN_ROADMAP.md"))
                    && Directory.Exists(Path.Combine(cursor.FullName, "ReignBeta"))
                    && Directory.Exists(Path.Combine(cursor.FullName, "ReignServer")))
                {
                    return cursor.FullName;
                }
            }
        }

        throw new InvalidOperationException(
            "The Reign workspace was not found. Set REIGN_WORKSPACE_ROOT to the Bannerlord Events workspace.");
    }

    private static void ValidateWorkspace(string path)
    {
        if (!File.Exists(Path.Combine(path, "REIGN_ROADMAP.md"))
            || !Directory.Exists(Path.Combine(path, "ReignBeta"))
            || !Directory.Exists(Path.Combine(path, "ReignServer")))
        {
            throw new InvalidOperationException(
                "REIGN_WORKSPACE_ROOT does not identify a Reign workspace.");
        }
    }

    private static bool ReadBoolean(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Equals("1", StringComparison.OrdinalIgnoreCase)
              || value.Equals("true", StringComparison.OrdinalIgnoreCase)
              || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadBoundedInt(string name, int fallback, int minimum, int maximum)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }
}
