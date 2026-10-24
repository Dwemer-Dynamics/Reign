using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_ruler_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the ruler capital, army, capture, judgment, settlement-effect, civil-war, options and persistence acceptance matrix. Uses guarded disposable-campaign fixtures and only the time required by each assertion; does not start Bannerlord, advance time or certify a pass.")]
    public static IReadOnlyDictionary<string, object?> GetRulerTestManifest(ReignMcpOptions options)
    {
        string path = Path.GetFullPath(Path.Combine(options.WorkspaceRoot, "ReignServer/tests/ReignLiveTest/scenarios/capital-ruler-judgment-manifest.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "reign-ruler-certification-manifest-v1") throw new InvalidOperationException("Unsupported ruler manifest.");
        var ids = root.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Ruler certification case identifiers must be distinct.");
        return new Dictionary<string, object?> { ["manifest"] = root.Clone(), ["path"] = path, ["caseCount"] = ids.Length, ["readOnly"] = true };
    }
}
