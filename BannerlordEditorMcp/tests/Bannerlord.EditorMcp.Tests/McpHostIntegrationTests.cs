using Bannerlord.EditorMcp.Server;
using ModelContextProtocol.Client;

namespace Bannerlord.EditorMcp.Tests;

public sealed class McpHostIntegrationTests
{
    [Fact]
    public async Task StdioHostAdvertisesExpectedTools()
    {
        var serverAssembly = typeof(EditorTools).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Bannerlord Editor MCP integration test",
            Command = "dotnet",
            Arguments = new[] { serverAssembly }
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("editor_get_status", names);
        Assert.Contains("editor_get_capabilities", names);
        Assert.Contains("scene_inspect", names);
        Assert.Contains("scene_inspect_terrain", names);
        Assert.Contains("scene_find_entities", names);
        Assert.Contains("scene_create_test_entity", names);
        Assert.Contains("scene_set_test_entity_transform", names);
        Assert.Contains("scene_remove_test_entity", names);
    }
}
