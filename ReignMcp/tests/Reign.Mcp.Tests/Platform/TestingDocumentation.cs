using System.Text.Json;

namespace Reign.Mcp.Tests;

/// <summary>Catalog assertions follow the indexed runbook, while the MCP guide resource stays concise.</summary>
internal static class TestingDocumentation
{
    public static string Read(string path)
    {
        string text = File.ReadAllText(path);
        string workspace = TestOptions.FindWorkspace();
        string guide = Path.Combine(workspace, "docs", "agent", "TESTING_TOOL_GUIDE.md");
        if (!Path.GetFullPath(path).Equals(guide, StringComparison.OrdinalIgnoreCase))
            return text;
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        foreach (var item in catalog.RootElement.GetProperty("guideDocuments").EnumerateArray())
        {
            string relative = item.GetString() ?? throw new InvalidDataException("Empty guide document.");
            string full = Path.GetFullPath(Path.Combine(workspace, relative));
            string allowed = Path.Combine(workspace, "docs", "agent", "testing") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
                || !full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Guide document leaves the testing runbook.");
            text += Environment.NewLine + File.ReadAllText(full);
        }
        return text;
    }
}
