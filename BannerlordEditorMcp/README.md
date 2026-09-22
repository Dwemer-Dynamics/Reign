# Bannerlord Editor MCP

Development-only integration between Codex and the Mount & Blade II: Bannerlord Scene Editor.

The system has three deliberately separated parts:

1. `Bannerlord.EditorMcp.Server` is a modern .NET STDIO MCP server launched by Codex.
2. `Bannerlord.EditorBridge` is a small Bannerlord module loaded only by the Modding Kit.
3. `Bannerlord.EditorMcp.Protocol` contains dependency-free DTOs shared over a current-user-only Windows named pipe.

The installed game and all official modules are treated as read-only. The bridge starts read-only and disarmed after every launch. Terrain writes, navmesh writes, scene saving, arbitrary code execution, and arbitrary filesystem access are not exposed.

## Current capability boundary

Public metadata in the installed `PC@v1.3.4` editor assemblies confirms managed APIs for:

- editor lifecycle callbacks;
- scene and entity inspection;
- creating empty entities and instantiating prefabs;
- names, tags, and transforms;
- named paths and path points;
- terrain dimensions, bounds, height ranges, node metadata, and bounded height-grid reads;
- navigation inspection and path queries.

No public managed heightmap-import or terrain-brush write method has been confirmed. Those capabilities remain disabled.

See [docs/capability-results.md](docs/capability-results.md) and [docs/manual-test-plan.md](docs/manual-test-plan.md).

## Build and test

From PowerShell:

```powershell
& '.\scripts\build.ps1'
```

The script restores packages, builds the external MCP host and editor module against the installed D: Modding Kit, and runs the unit and STDIO integration tests. It does not launch Bannerlord.

## Deploy the editor bridge

Close Bannerlord and the Modding Kit, then run:

```powershell
& '.\scripts\deploy-bridge.ps1'
```

Deployment creates only `Modules\BannerlordEditorBridge` and refuses to run while a Bannerlord process is active.

## Configure Codex

Copy `.codex/config.toml.example` to `.codex/config.toml` in the trusted project from which you want to use the bridge, then restart that Codex surface. Keep the example scoped to this project; do not make the bridge a required global MCP server.

The editor-side procedure is documented in [docs/manual-test-plan.md](docs/manual-test-plan.md). Prove writes only in a disposable development scene. Official scenes may be inspected read-only by attaching a temporary unsaved controller entity and then closing the scene without saving.

## Read-only packed texture extraction

`scripts/extract-tpac-texture.py` provides the supported fallback when an
official packed texture must be inspected but opening it in the Resource Browser
is unsafe or loses package provenance. It parses TPAC metadata read-only,
decompresses the exact texture pixel segment, and writes both a PNG and a JSON
audit containing package, asset, segment, decoded-pixel, and output hashes.

Example:

```powershell
python .\scripts\extract-tpac-texture.py `
  'D:\...\Modules\NavalDLC\EmAssetPackages\world_map\world_map.tpac' `
  worldmap_river_mask `
  '.\.codex-build\editor-capture\worldmap_river_mask.png'
```

The extractor never writes to the package. The War Council renderer accepts the
audited `world_map_heightmap` and `worldmap_river_mask` products through
`--height-texture` and `--river-mask`; it retains the MCP height grid and audited
settlement CSV as independent coordinate checks before producing its global 16K
master and lossless runtime tiles.
