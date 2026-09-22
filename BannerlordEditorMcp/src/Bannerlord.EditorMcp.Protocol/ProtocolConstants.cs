namespace Bannerlord.EditorMcp.Protocol;

public static class ProtocolConstants
{
    public const string Version = "bannerlord-editor-bridge/1";
    public const string DefaultPipeName = "BannerlordEditorMcp.v1";
    public const string TestEntityPrefix = "mcp_test_";
    public const string TestEntityTag = "bannerlord_mcp_test";
}

public static class BridgeCommands
{
    public const string EditorGetStatus = "editor.get_status";
    public const string EditorGetCapabilities = "editor.get_capabilities";
    public const string SceneInspect = "scene.inspect";
    public const string SceneInspectTerrain = "scene.inspect_terrain";
    public const string SceneFindEntities = "scene.find_entities";
    public const string SceneCreateTestEntity = "scene.create_test_entity";
    public const string SceneSetTestEntityTransform = "scene.set_test_entity_transform";
    public const string SceneRemoveTestEntity = "scene.remove_test_entity";
}
