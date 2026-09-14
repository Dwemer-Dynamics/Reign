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

The prototype revision is an editor-session GUID plus a monotonic counter incremented for bridge-applied writes. It prevents requests from an earlier editor session or earlier bridge write from applying.

This prototype does **not** yet detect every manual in-memory editor change. A later capability should derive an on-demand scene fingerprint from relevant entities or an editor-provided dirty/revision signal if one is available.

## Scope

The first write tools create, move, and remove only entities whose names begin with `mcp_test_` and which carry the `bannerlord_mcp_test` tag. The bridge cannot delete or move ordinary entities.
