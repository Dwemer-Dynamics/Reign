# Security model

## Trust boundaries

- MCP arguments are untrusted and validated before being sent to the editor.
- Named-pipe JSON is untrusted and validated again inside Bannerlord.
- The installed game and official modules are read-only.
- Only the configured development module and project roots may become writable in future file tools.
- No tool accepts an arbitrary filesystem path.

## Editor states

| State | Allowed behavior |
| --- | --- |
| Bridge disabled | No pipe connection or commands |
| Read-only (startup default) | Status, capabilities, entity inspection/search, and bounded terrain inspection |
| Safe writes armed | Prototype test-entity create/move/remove in one disposable scene |

Every launch and editor initialization enables the current-user-only pipe in read-only mode and resets writes to disarmed. Saving a scene also disarms writes. Attaching the controller to an official scene is permitted only as an unsaved, temporary read-only inspection aid.

## Prohibited capabilities

The prototype provides no arbitrary C# execution, console-command execution, private-method invocation, Harmony patching, native injection, memory editing, UI-coordinate automation, arbitrary file writes, scene saving, terrain writes, or navmesh writes.

## Write requirements

Every write requires:

- a valid allowlisted target module ID;
- the configured disposable scene name, matching the current scene;
- the current scene revision;
- a non-empty idempotency key;
- `ReadOnly = false` and `SafeWritesArmed = true` in the editor;
- an entity name and ownership tag reserved for this prototype.

The MCP host's approval annotations are supplemental. Enforcement remains inside the bridge.
