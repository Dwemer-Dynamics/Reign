namespace Bannerlord.EditorMcp.Server;

public sealed class PathAccessPolicy
{
    private readonly string _gameRoot;
    private readonly string _projectRoot;
    private readonly string _modRoot;

    public PathAccessPolicy(BridgeServerOptions options)
    {
        _gameRoot = NormalizeOptional(options.GameDirectory);
        _projectRoot = NormalizeOptional(options.ProjectDirectory);
        _modRoot = NormalizeOptional(options.ModDirectory);
    }

    public bool CanRead(string path)
    {
        var candidate = NormalizeRequired(path);
        return IsWithin(candidate, _gameRoot) ||
               IsWithin(candidate, _projectRoot) ||
               IsWithin(candidate, _modRoot);
    }

    public bool CanWrite(string path)
    {
        var candidate = NormalizeRequired(path);
        return IsWithin(candidate, _projectRoot) || IsWithin(candidate, _modRoot);
    }

    public string ResolveProjectLogicalPath(string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            throw new InvalidOperationException("BANNERLORD_PROJECT_DIR is not configured.");
        }

        if (string.IsNullOrWhiteSpace(logicalPath) || Path.IsPathRooted(logicalPath))
        {
            throw new ArgumentException("A non-rooted logical path is required.", nameof(logicalPath));
        }

        var resolved = Path.GetFullPath(Path.Combine(_projectRoot, logicalPath));
        if (!IsWithin(resolved, _projectRoot))
        {
            throw new UnauthorizedAccessException("The logical path escapes the project directory.");
        }

        return resolved;
    }

    private static string NormalizeOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeRequired(value);
    }

    private static string NormalizeRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A filesystem path is required.", nameof(value));
        }

        return Path.GetFullPath(value)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithin(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
