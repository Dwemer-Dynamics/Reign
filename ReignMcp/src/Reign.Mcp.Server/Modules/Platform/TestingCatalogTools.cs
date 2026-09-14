using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class TestingCatalogTools
{
    [McpServerTool(Name = "reign_get_testing_catalog", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the versioned Reign testing catalog, current MCP tool inventory, safety metadata, guide path, and catalog fingerprint. Optional text filters keep the result bounded.")]
    public static IReadOnlyDictionary<string, object?> GetTestingCatalog(
        TestingCatalogService catalog,
        [Description("Optional case-insensitive tool name, capability id, or description filter.")]
        string filter = "")
    {
        return catalog.GetCatalog(filter);
    }
}

public sealed class TestingCatalogService
{
    private readonly ReignMcpOptions _options;

    public TestingCatalogService(ReignMcpOptions options)
    {
        _options = options;
    }

    public IReadOnlyDictionary<string, object?> GetCatalog(string filter = "")
    {
        filter = InputGuard.BoundedText(filter, nameof(filter), 120);
        string path = Path.Combine(_options.WorkspaceRoot, "reign.testing.json");
        string json = File.ReadAllText(path, Encoding.UTF8);
        using JsonDocument document = JsonDocument.Parse(json);
        object? staticCatalog = JsonSerializer.Deserialize<object>(json);
        var tools = typeof(TestingCatalogTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => new
            {
                Method = method,
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty
            })
            .Where(item => item.Tool is not null)
            .Select(item => new Dictionary<string, object?>
            {
                ["name"] = item.Tool!.Name,
                ["description"] = item.Description,
                ["readOnly"] = item.Tool.ReadOnly,
                ["destructive"] = item.Tool.Destructive,
                ["idempotent"] = item.Tool.Idempotent,
                ["inputs"] = item.Method.GetParameters()
                    .Where(parameter => !IsInjected(parameter.ParameterType))
                    .Select(parameter => new Dictionary<string, object?>
                    {
                        ["name"] = parameter.Name,
                        ["type"] = FriendlyType(parameter.ParameterType),
                        ["optional"] = parameter.HasDefaultValue,
                        ["default"] = parameter.HasDefaultValue ? parameter.DefaultValue : null,
                        ["description"] = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty
                    }).ToArray()
            })
            .Where(tool => filter.Length == 0
                || Contains(tool["name"], filter)
                || Contains(tool["description"], filter))
            .OrderBy(tool => Convert.ToString(tool["name"]), StringComparer.Ordinal)
            .ToArray();
        string scenarioRoot = Path.Combine(_options.WorkspaceRoot, "ReignBetaServer", "ReignLiveTest", "scenarios");
        string[] scenarios = Directory.Exists(scenarioRoot)
            ? Directory.EnumerateFiles(scenarioRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Select(file => Path.GetRelativePath(_options.WorkspaceRoot, file).Replace('\\', '/'))
                .Where(file => filter.Length == 0 || file.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray()
            : [];

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = document.RootElement.GetProperty("schema").GetString(),
            ["version"] = document.RootElement.GetProperty("version").GetString(),
            ["catalogFingerprintSha256"] = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
            ["catalogPath"] = path,
            ["guidePath"] = Path.Combine(_options.WorkspaceRoot, "docs", "agent", "TESTING_TOOL_GUIDE.md"),
            ["filter"] = filter,
            ["toolCount"] = tools.Length,
            ["tools"] = tools,
            ["scenarioCount"] = scenarios.Length,
            ["scenarioFiles"] = scenarios,
            ["catalog"] = staticCatalog
        };
    }

    private static bool IsInjected(Type type)
    {
        return type == typeof(CancellationToken)
            || type == typeof(ReignApiClient)
            || type == typeof(ReignMcpOptions)
            || type == typeof(ReignProcessRunner)
            || type == typeof(WorkspaceAccess)
            || type == typeof(TestingCatalogService)
            || type.Name.EndsWith("Service", StringComparison.Ordinal);
    }

    private static bool Contains(object? value, string filter)
    {
        return Convert.ToString(value)?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string FriendlyType(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual.IsEnum) return string.Join("|", Enum.GetNames(actual));
        if (actual.IsArray) return FriendlyType(actual.GetElementType()!) + "[]";
        return actual.Name;
    }
}
