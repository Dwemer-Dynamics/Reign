# Installed editor capability results

Inspection date: 2026-07-21  
Installed root: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord`  
Editor binaries: `bin\Win64_Shipping_wEditor`

`package_info.txt` reports `Environment: PC@v1.3.4`. The installed official `Native/SubModule.xml` independently reports `v1.4.7`. The bridge therefore compiles against the installed assemblies and does not infer API compatibility from either label alone.

The following results come from public .NET assembly metadata. No private invocation or decompilation was used.

| Capability | Result | Public surface observed |
| --- | --- | --- |
| Editor initialization/tick | Confirmed | `ScriptComponentBehavior.OnEditorInit`, `OnEditorTick` |
| Editor variable/save callbacks | Confirmed | `OnEditorVariableChanged`, `OnSceneSave` |
| Application tick | Confirmed | `MBSubModuleBase.OnApplicationTick` |
| Scene inspection | Confirmed | `Scene.GetEntities`, `FindEntityWithName`, `FindEntitiesWithTag` |
| Empty entity creation | Confirmed | `GameEntity.CreateEmpty` |
| Prefab instantiation | Confirmed, not exposed yet | `GameEntity.Instantiate` |
| Transform/name/tag writes | Confirmed | `SetLocalPosition`, `SetFrame`, `Name`, `AddTag`, `RemoveTag` |
| Named paths | Confirmed, not exposed yet | `Scene.AddPath`, `AddPathPoint`, `GetPathWithName` |
| Terrain reads | Confirmed and exposed | `scene_inspect_terrain` uses `GetTerrainData`, `GetTerrainHeight`, `GetTerrainNodeData`, `GetTerrainMinMaxHeight`, and `GetBoundingBox` |
| Heightmap import | Not found in public managed surface | Capability disabled |
| Terrain brush writes | Not found in public managed surface | Capability disabled |
| Navmesh inspection/path queries | Confirmed | face counts/records and path-query methods |
| Navmesh generation/editing | Not proven safe | Capability disabled |
| Scene saving | Not exposed by prototype | Capability disabled |

`scene_inspect_terrain` returns the exact terrain node dimensions and size, scene bounds, global and per-node height ranges, and an optional bounded sample tile (up to 257 by 257). Tile offsets and complete logical-grid dimensions (up to 4097 by 4097) preserve one exact shared sampling lattice across overlapping reads. This supports seamless high-resolution map extraction and external asset generation without modifying or saving the scene.

`scripts/export-terrain-capture.ps1` consumes the durable tile JSON files, rejects missing samples or inconsistent overlaps, and exports a row-major little-endian float grid plus a north-up 16-bit PGM heightmask. Its JSON audit records every tile hash, exact logical coverage, overlap count, orientation, and output hash so downstream art generation can prove it used one coherent editor capture.
