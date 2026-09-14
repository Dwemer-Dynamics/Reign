using System.ComponentModel;
using System.Text.Json;
using Bannerlord.EditorMcp.Protocol;
using ModelContextProtocol.Server;

namespace Bannerlord.EditorMcp.Server;

[McpServerToolType]
public static class EditorTools
{
    [McpServerTool(Name = "editor_get_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns whether the development-only Bannerlord Scene Editor bridge is connected and reports the active editor session. Does not modify the game or scene.")]
    public static async Task<EditorStatus> GetStatus(IBridgeClient bridge, CancellationToken cancellationToken)
    {
        if (!bridge.IsConnected)
        {
            return new EditorStatus { Connected = false };
        }

        var response = await bridge.SendAsync(NewReadRequest(BridgeCommands.EditorGetStatus), cancellationToken).ConfigureAwait(false);
        return DeserializeResult<EditorStatus>(response);
    }

    [McpServerTool(Name = "editor_get_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns only editor operations proven against the installed Bannerlord assemblies. A false capability must not be invoked through another tool.")]
    public static async Task<EditorCapabilities> GetCapabilities(IBridgeClient bridge, CancellationToken cancellationToken)
    {
        var response = await bridge.SendAsync(NewReadRequest(BridgeCommands.EditorGetCapabilities), cancellationToken).ConfigureAwait(false);
        return DeserializeResult<EditorCapabilities>(response);
    }

    [McpServerTool(Name = "scene_inspect", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Inspects the active editor scene and returns a bounded entity summary. Does not modify the scene.")]
    public static Task<BridgeResponse> InspectScene(
        IBridgeClient bridge,
        [Description("Maximum number of entities to return, from 1 through 500.")] int maxEntities = 100,
        CancellationToken cancellationToken = default)
    {
        if (maxEntities is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntities), "maxEntities must be between 1 and 500.");
        }

        return bridge.SendAsync(NewReadRequest(BridgeCommands.SceneInspect, new { maxEntities }), cancellationToken);
    }

    [McpServerTool(Name = "scene_inspect_terrain", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Inspects terrain dimensions, bounds, height range, node metadata, and an optional bounded height sample grid in the active editor scene. Does not modify the scene.")]
    public static Task<BridgeResponse> InspectTerrain(
        IBridgeClient bridge,
        [Description("Terrain-height columns returned by this call. Use zero for no samples; otherwise 1 through 257.")] int sampleColumns = 0,
        [Description("Terrain-height rows returned by this call. Use zero for no samples; otherwise 1 through 257.")] int sampleRows = 0,
        [Description("Zero-based first column in the complete logical sample grid. Use zero for a non-tiled read.")] int sampleColumnOffset = 0,
        [Description("Zero-based first row in the complete logical sample grid. Use zero for a non-tiled read.")] int sampleRowOffset = 0,
        [Description("Complete logical sample-grid columns, from 1 through 4097. Use zero to match sampleColumns.")] int totalSampleColumns = 0,
        [Description("Complete logical sample-grid rows, from 1 through 4097. Use zero to match sampleRows.")] int totalSampleRows = 0,
        CancellationToken cancellationToken = default)
    {
        if (sampleColumns is < 0 or > 257)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleColumns), "sampleColumns must be between 0 and 257.");
        }

        if (sampleRows is < 0 or > 257)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRows), "sampleRows must be between 0 and 257.");
        }

        if ((sampleColumns == 0) != (sampleRows == 0))
        {
            throw new ArgumentException("sampleColumns and sampleRows must both be zero or both be positive.");
        }

        if (sampleColumns == 0)
        {
            if (sampleColumnOffset != 0 || sampleRowOffset != 0 || totalSampleColumns != 0 || totalSampleRows != 0)
            {
                throw new ArgumentException("Sample-grid offsets and totals require positive sampleColumns and sampleRows.");
            }
        }
        else
        {
            totalSampleColumns = totalSampleColumns == 0 ? sampleColumns : totalSampleColumns;
            totalSampleRows = totalSampleRows == 0 ? sampleRows : totalSampleRows;

            if (totalSampleColumns is < 1 or > 4097 || totalSampleRows is < 1 or > 4097)
            {
                throw new ArgumentOutOfRangeException(nameof(totalSampleColumns), "Logical sample-grid dimensions must be between 1 and 4097.");
            }

            if (sampleColumnOffset < 0 || sampleRowOffset < 0 ||
                sampleColumnOffset + sampleColumns > totalSampleColumns ||
                sampleRowOffset + sampleRows > totalSampleRows)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleColumnOffset), "The requested sample tile must fit inside the complete logical sample grid.");
            }
        }

        return bridge.SendAsync(
            NewReadRequest(BridgeCommands.SceneInspectTerrain, new
            {
                sampleColumns,
                sampleRows,
                sampleColumnOffset,
                sampleRowOffset,
                totalSampleColumns,
                totalSampleRows
            }),
            cancellationToken);
    }

    [McpServerTool(Name = "scene_find_entities", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Finds entities in the active editor scene by a name fragment or exact tag. Does not modify the scene.")]
    public static Task<BridgeResponse> FindEntities(
        IBridgeClient bridge,
        [Description("Case-insensitive entity-name fragment. Empty means no name filter.")] string nameContains = "",
        [Description("Exact entity tag. Empty means no tag filter.")] string tag = "",
        [Description("Maximum number of matches, from 1 through 500.")] int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults must be between 1 and 500.");
        }

        return bridge.SendAsync(NewReadRequest(BridgeCommands.SceneFindEntities, new { nameContains, tag, maxResults }), cancellationToken);
    }

    [McpServerTool(Name = "scene_create_test_entity", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Plans or creates one specially tagged empty test entity. The editor must be armed, the target must be the configured development module and disposable scene, and a current scene revision is required.")]
    public static Task<BridgeResponse> CreateTestEntity(
        IBridgeClient bridge,
        string targetModule,
        string scene,
        string expectedSceneRevision,
        string idempotencyKey,
        string entityName,
        float x = 0,
        float y = 0,
        float z = 0,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.SendAsync(NewWriteRequest(
            BridgeCommands.SceneCreateTestEntity,
            targetModule,
            scene,
            expectedSceneRevision,
            idempotencyKey,
            dryRun,
            new { entityName, x, y, z }), cancellationToken);
    }

    [McpServerTool(Name = "scene_set_test_entity_transform", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Plans or moves an entity created by this prototype. It refuses ordinary scene entities and applies only to the configured disposable scene.")]
    public static Task<BridgeResponse> SetTestEntityTransform(
        IBridgeClient bridge,
        string targetModule,
        string scene,
        string expectedSceneRevision,
        string idempotencyKey,
        string entityName,
        float x,
        float y,
        float z,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.SendAsync(NewWriteRequest(
            BridgeCommands.SceneSetTestEntityTransform,
            targetModule,
            scene,
            expectedSceneRevision,
            idempotencyKey,
            dryRun,
            new { entityName, x, y, z }), cancellationToken);
    }

    [McpServerTool(Name = "scene_remove_test_entity", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Plans or removes only a specially tagged prototype test entity from the configured disposable scene. It cannot remove normal entities.")]
    public static Task<BridgeResponse> RemoveTestEntity(
        IBridgeClient bridge,
        string targetModule,
        string scene,
        string expectedSceneRevision,
        string idempotencyKey,
        string entityName,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.SendAsync(NewWriteRequest(
            BridgeCommands.SceneRemoveTestEntity,
            targetModule,
            scene,
            expectedSceneRevision,
            idempotencyKey,
            dryRun,
            new { entityName }), cancellationToken);
    }

    private static BridgeRequest NewReadRequest(string command, object? arguments = null)
    {
        return new BridgeRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Command = command,
            DryRun = true,
            ArgumentsJson = JsonSerializer.Serialize(arguments ?? new { })
        };
    }

    private static BridgeRequest NewWriteRequest(
        string command,
        string targetModule,
        string scene,
        string expectedSceneRevision,
        string idempotencyKey,
        bool dryRun,
        object arguments)
    {
        return new BridgeRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Command = command,
            TargetModule = targetModule?.Trim() ?? string.Empty,
            Scene = scene?.Trim() ?? string.Empty,
            ExpectedSceneRevision = expectedSceneRevision?.Trim() ?? string.Empty,
            IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty,
            DryRun = dryRun,
            ArgumentsJson = JsonSerializer.Serialize(arguments)
        };
    }

    private static T DeserializeResult<T>(BridgeResponse response)
    {
        if (!response.Ok)
        {
            throw new InvalidOperationException($"{response.ErrorCode}: {response.Error}");
        }

        return JsonSerializer.Deserialize<T>(response.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The editor bridge returned an empty result.");
    }
}
