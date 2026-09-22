# Manual editor test plan

Prove writes only in a disposable scene in a development module. Official scenes may be inspected read-only after the disposable-scene proof; never save a temporary controller into an official scene.

## Modding Kit startup recovery

The installed Modding Kit can present a sequence of non-destructive `RGL WARNING` call-stack dialogs during startup. Dismiss the warning, decline diagnostic upload if offered, and use the editor's persistent ignore option for the known assertion. Continue dismissing repeated warning dialogs until the editor main menu appears. Do not treat this recoverable assertion sequence as a crash, and do not bypass security or destructive confirmations.

## Preconditions

1. Close the Bannerlord Modding Kit before deploying or replacing the bridge DLL.
2. Build and test the solution with `scripts/build.ps1`.
3. Deploy only the development bridge module with `scripts/deploy-bridge.ps1`.
4. Create or choose a disposable scene owned by a development module.
5. Enable `BannerlordEditorBridge` and the development module in the Modding Kit.

## Attach the controller

1. Open the disposable scene.
2. Add one empty entity named `mcp_editor_bridge_controller`.
3. Attach the `EditorBridgeController` script component.
4. Set `TargetModuleId` to the disposable scene's module ID.
5. Set `DisposableSceneName` to the exact active scene name.
6. Confirm `BridgeEnabled` is on automatically, `ReadOnly` is on, and `SafeWritesArmed` is off.

## Read-only test

1. Start Codex with the example project MCP configuration.
2. Confirm `ConnectionStatus` becomes `Connected` without arming writes.
3. Call `editor_get_status`, `editor_get_capabilities`, `scene_inspect`, and `scene_inspect_terrain`.
4. Confirm terrain dimensions, bounds, and height ranges are returned and the scene remains unchanged.
5. Confirm terrain/navmesh/save write capabilities remain false.

## Official scene read-only inspection

1. Complete the disposable-scene read-only proof first.
2. Open the official scene and add one temporary empty `mcp_editor_bridge_controller` entity without saving.
3. Attach `EditorBridgeController`; it connects read-only automatically.
4. Confirm status reports the expected official scene and `ReadOnly = true`, `SafeWritesArmed = false`.
5. Use only read tools, including `scene_inspect_terrain` for exact dimensions and bounded height samples. For a seamless 1025 by 1025 authoring grid, request sixteen overlapping 257 by 257 tiles with column/row offsets `0`, `256`, `512`, and `768`, and set both complete logical-grid dimensions to `1025`.
6. Save each MCP `resultJson` payload as `tile_r{row}_c{column}.json`, then run `scripts/export-terrain-capture.ps1 -TileDirectory <tiles> -OutputDirectory <export>`. Require `ok = true`, complete logical-grid coverage, zero overlap mismatches, and hashes for both exported height products.
7. Close or switch scenes and choose **No** when asked to save. Reopen the scene and confirm the temporary controller is absent.

## Safe write test

1. Save a clean disposable scene checkpoint manually.
2. Set `ReadOnly` off, then set `SafeWritesArmed` on.
3. Inspect status and copy the exact `sceneRevision`.
4. Call `scene_create_test_entity` with `dryRun = true` and an `mcp_test_...` name.
5. Confirm the proposed change and unchanged scene.
6. Repeat with `dryRun = false` and the same inspected revision but a fresh idempotency key.
7. Confirm one tagged empty entity appears.
8. Retry the identical request and confirm no duplicate appears.
9. Reinspect, move the test entity using the new revision, and confirm the transform.
10. Reinspect, remove the test entity, and confirm ordinary entities are untouched.

## Failure tests

- Attempt a write while disarmed: expect `writes_disarmed`.
- Use a stale revision: expect `stale_scene_revision`.
- Use a different module or scene: expect a target rejection.
- Reuse an idempotency key with different arguments: expect `idempotency_conflict`.
- Try to move or remove an ordinary entity: expect `test_entity_not_found`.

Do not save changes from the first write test unless the scene contains only disposable test content.
