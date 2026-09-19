using System.Text.Json;

namespace Reign.Mcp.Server;

// Only these source directories may be projected from the sibling public repo.
// A projection is a junction, not a second copy or a second editing location.
public sealed class ReignSourceLayout
{
    public static readonly string[] ServerDirectories = ["ReignServer"];
    public const string ClientOrigin = "https://github.com/Dwemer-Dynamics/Reign.git";
    public const string ServerOrigin = "https://github.com/Dwemer-Dynamics/ReignServer.git";
    public string WorkspaceRoot { get; }
    public string? ServerRoot { get; }
    public bool IsPaired => ServerRoot != null;

    public ReignSourceLayout(string workspaceRoot)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string manifest = Path.Combine(WorkspaceRoot, "reign.repositories.json");
        if (!File.Exists(manifest)) return;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = json.RootElement;
        if (root.GetProperty("schema").GetString() != "reign-paired-repositories-v1")
            throw new InvalidDataException("Unsupported Reign repository layout.");
        string? mode = root.GetProperty("mode").GetString();
        if (mode == "migration") return;
        if (mode != "paired") throw new InvalidDataException("Unsupported Reign repository mode.");
        if (new DirectoryInfo(WorkspaceRoot).Name != "Reign")
            throw new InvalidDataException("The paired integration workspace must be the Reign checkout.");
        var client = root.GetProperty("client");
        var server = root.GetProperty("server");
        if (client.GetProperty("name").GetString() != "Reign"
            || client.GetProperty("origin").GetString() != ClientOrigin
            || server.GetProperty("name").GetString() != "ReignServer"
            || server.GetProperty("origin").GetString() != ServerOrigin
            || server.GetProperty("relativePath").GetString() != "../ReignServer")
            throw new InvalidDataException("The paired workspace must use the authorized Reign and ReignServer destinations.");
        var declared = root.GetProperty("serverSourceDirectories").EnumerateArray().Select(v => v.GetString()).ToArray();
        if (!declared.Order().SequenceEqual(ServerDirectories.Order()))
            throw new InvalidDataException("The paired workspace has an unknown or missing source projection.");
        ServerRoot = Path.GetFullPath(Path.Combine(WorkspaceRoot, "..", "ReignServer"));
        if ((!Directory.Exists(Path.Combine(ServerRoot, ".git")) && !File.Exists(Path.Combine(ServerRoot, ".git")))
            || (new DirectoryInfo(ServerRoot).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The sibling ReignServer Git checkout is missing or redirected.");
    }

    public void ValidateProjections()
    {
        if (!IsPaired) return;
        foreach (string name in ServerDirectories)
        {
            var directory = new DirectoryInfo(Path.Combine(WorkspaceRoot, name));
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) == 0 || !CanTraverse(directory))
                throw new InvalidDataException("Missing or incorrect ReignServer source junction: " + name + ". Run Connect-Repositories.ps1.");
        }
    }

    public bool CanTraverse(DirectoryInfo directory)
    {
        string workspaceRelative = Path.GetRelativePath(WorkspaceRoot, directory.FullName).Replace('\\', '/');
        if (workspaceRelative is "ReignServer/data" or "ReignServer/runtime"
            || workspaceRelative.StartsWith("ReignServer/data/", StringComparison.OrdinalIgnoreCase)
            || workspaceRelative.StartsWith("ReignServer/runtime/", StringComparison.OrdinalIgnoreCase)) return false;
        if ((directory.Attributes & FileAttributes.ReparsePoint) == 0) return true;
        if (!IsPaired) return false;
        string relative = Path.GetRelativePath(WorkspaceRoot, directory.FullName);
        if (!ServerDirectories.Contains(relative, StringComparer.Ordinal)) return false;
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);
        return target != null && Path.GetFullPath(target.FullName).Equals(
            ServerRoot!, StringComparison.OrdinalIgnoreCase);
    }

    public void ValidateAccessPath(string path)
    {
        path = Path.GetFullPath(path);
        ReignMcpOptions.EnsureWithin(WorkspaceRoot, path, nameof(path));
        ValidateProjections();
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Source file links are outside the readable boundary.");
        var directory = new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
        while (!directory.FullName.Equals(WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (!directory.Exists || !CanTraverse(directory))
                throw new InvalidDataException("Source path crosses an unregistered or redirected directory.");
            directory = directory.Parent ?? throw new InvalidDataException("Source path has no workspace ancestor.");
        }
    }
}
