# Architecture

```text
Codex desktop / CLI / IDE
          |
          | MCP over STDIO
          v
Bannerlord.EditorMcp.Server (net10.0)
          |
          | current-user-only Windows named pipe
          | newline-delimited, versioned JSON
          v
Bannerlord.EditorBridge (net472)
          |
          | ConcurrentQueue
          v
EditorBridgeController.OnEditorTick
          |
          v
Public TaleWorlds.Engine APIs
```

The external server owns the named pipe. This keeps pipe access control in the modern .NET process and leaves the Bannerlord process as a simple client. The editor bridge never carries MCP SDK dependencies.

The bridge is scene-scoped: `EditorBridgeController` must be attached to one entity in the active editor scene. It starts connected but strictly read-only, with `BridgeEnabled = true`, `ReadOnly = true`, and `SafeWritesArmed = false` after every editor initialization. A controller added temporarily to an official scene must never be saved. Write testing remains restricted to a disposable development scene.

## Threading

The pipe client runs on a background task. It parses JSON envelopes and enqueues them without touching the engine. `OnEditorTick` dequeues at most eight requests per frame, calls public engine APIs, and signals the waiting pipe task.

## Revisions

The scene revision combines the editor-session GUID and a counter. When the configured disposable scene is armed, status and inspection commands observe the entity hierarchy, identity, names, prefab names, tags, and local/global transforms. A fresh applied request rechecks that state on the editor tick. The counter advances after an observed change or a bridge-applied write.

The observation covers up to 4,096 entities, 64 tags per entity and 512 characters per text field. A missing or incomplete observation blocks a new applied write. Exact replay retrieves the recorded receipt after the existing scope checks. Terrain, navmesh and internal component data require separate evidence.

## Scope

The first write tools create, move, and remove only entities whose names begin with `mcp_test_` and which carry the `bannerlord_mcp_test` tag. The bridge cannot delete or move ordinary entities.
