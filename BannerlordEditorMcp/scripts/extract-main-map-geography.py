#!/usr/bin/env python3
"""Extract authoritative Main_map cartography inputs from installed game data.

This tool is intentionally read-only with respect to Bannerlord.  It converts
the editor-authored ``flora.bin`` and ``scene.xscene`` plus material-map exports
into deterministic CSV/JSON evidence that the War Council renderer can consume.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
import struct
import xml.etree.ElementTree as ET
from collections import Counter
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from PIL import Image


SCHEMA = "reign-war-council-main-map-geography-v1"
FLORA_MAGIC = b"FLR2"
FLORA_FLOAT_COUNT = 20
FLORA_RECORD_FIXED_BYTES = 4 + 4 + FLORA_FLOAT_COUNT * 4
BRIDGE_SITE_RADIUS = 12.0
SETTLEMENT_TAGS = {"town", "castle", "village", "hideout"}
SETTLEMENT_PREFIXES = ("town_", "castle_", "village_", "hideout_")
MATERIAL_PATTERN = re.compile(r"^layer(?P<index>\d+)-(?P<name>.+)\.png$", re.IGNORECASE)


@dataclass(frozen=True)
class ScenePlacement:
    category: str
    name: str
    prefab: str
    world_x: float
    world_y: float
    world_z: float
    rotation_x: float
    rotation_y: float
    rotation_z: float
    scale_x: float
    scale_y: float
    scale_z: float
    snow: bool
    descendant_bridge_entities: int


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def parse_vector(value: str | None, default: tuple[float, float, float]) -> tuple[float, float, float]:
    if not value:
        return default
    parts = tuple(float(item.strip()) for item in value.split(","))
    if len(parts) != 3:
        raise ValueError(f"Expected three-vector, got {value!r}")
    return parts


def entity_tokens(entity: ET.Element) -> str:
    return f"{entity.get('name', '')} {entity.get('old_prefab_name', '')}".lower()


def descendant_bridge_count(entity: ET.Element) -> int:
    return sum(1 for item in entity.iter("game_entity") if "bridge" in entity_tokens(item))


def is_settlement_entity(entity: ET.Element) -> bool:
    tags = {item.get("name", "").lower() for item in entity.findall("./tags/tag")}
    name = entity.get("name", "").lower()
    return bool(tags & SETTLEMENT_TAGS) or name.startswith(SETTLEMENT_PREFIXES)


def placement_from_entity(entity: ET.Element, category: str, bridge_descendants: int = 0) -> ScenePlacement:
    transform = entity.find("transform")
    if transform is None:
        raise ValueError(f"Cartographic entity {entity.get('name')!r} has no transform")
    position = parse_vector(transform.get("position"), (0.0, 0.0, 0.0))
    rotation = parse_vector(transform.get("rotation_euler"), (0.0, 0.0, 0.0))
    scale = parse_vector(transform.get("scale"), (1.0, 1.0, 1.0))
    name = entity.get("name", "")
    prefab = entity.get("old_prefab_name", "")
    tokens = f"{name} {prefab}".lower()
    return ScenePlacement(
        category=category,
        name=name,
        prefab=prefab,
        world_x=position[0],
        world_y=position[1],
        world_z=position[2],
        rotation_x=rotation[0],
        rotation_y=rotation[1],
        rotation_z=rotation[2],
        scale_x=scale[0],
        scale_y=scale[1],
        scale_z=scale[2],
        snow="_snw" in tokens or "snow" in tokens,
        descendant_bridge_entities=bridge_descendants,
    )


def extract_scene(scene_path: Path) -> tuple[list[ScenePlacement], dict[str, object]]:
    root = ET.parse(scene_path).getroot()
    entities = root.find("entities")
    if entities is None:
        raise ValueError("scene.xscene has no <entities> root")

    placements: list[ScenePlacement] = []
    excluded = Counter()
    raw_matches = Counter()
    for entity in entities.findall("game_entity"):
        settlement = is_settlement_entity(entity)
        direct_tokens = entity_tokens(entity)
        bridge_count = descendant_bridge_count(entity)
        mountain_count = sum(
            1 for item in entity.iter("game_entity") if "map_mountain_rock" in entity_tokens(item)
        )
        raw_matches["bridge_entities"] += bridge_count
        raw_matches["mountain_entities"] += mountain_count

        if settlement:
            excluded["bridge_entities"] += bridge_count
            excluded["mountain_entities"] += mountain_count
            continue

        # A bridge prefab is often expanded into many bridge-named children.
        # The top-level transform is the authoritative strategic placement, so
        # emit exactly one placement for the group and preserve the child count.
        if bridge_count:
            placements.append(placement_from_entity(entity, "bridge", bridge_count))

        # Main_map's strategic mountains are top-level authored placements.
        # Nested mountain rocks belong to settlement/hideout illustrations.
        if "map_mountain_rock" in direct_tokens:
            placements.append(placement_from_entity(entity, "mountain"))

    bridge_sites = cluster_bridge_sites([p for p in placements if p.category == "bridge"])
    flora_rect = root.find("flora_bounding_rect")
    summary: dict[str, object] = {
        "sceneName": root.get("name"),
        "sceneVersion": root.get("version"),
        "sceneRevision": root.get("revision"),
        "floraBounds": {
            "min": flora_rect.get("min") if flora_rect is not None else None,
            "max": flora_rect.get("max") if flora_rect is not None else None,
        },
        "rawDescendantMatches": dict(sorted(raw_matches.items())),
        "excludedSettlementDescendants": dict(sorted(excluded.items())),
        "strategicPlacementCounts": dict(Counter(item.category for item in placements)),
        "bridgeSiteCount": len(bridge_sites),
        "bridgeSites": bridge_sites,
    }
    return placements, summary


def cluster_bridge_sites(bridges: list[ScenePlacement]) -> list[dict[str, object]]:
    clusters: list[list[ScenePlacement]] = []
    for bridge in sorted(bridges, key=lambda item: (item.world_x, item.world_y, item.name)):
        match = None
        for cluster in clusters:
            cx = sum(item.world_x for item in cluster) / len(cluster)
            cy = sum(item.world_y for item in cluster) / len(cluster)
            if math.hypot(bridge.world_x - cx, bridge.world_y - cy) <= BRIDGE_SITE_RADIUS:
                match = cluster
                break
        if match is None:
            clusters.append([bridge])
        else:
            match.append(bridge)

    result: list[dict[str, object]] = []
    for index, cluster in enumerate(clusters):
        xs = sorted(item.world_x for item in cluster)
        ys = sorted(item.world_y for item in cluster)
        mid = len(cluster) // 2
        median_x = xs[mid] if len(cluster) % 2 else (xs[mid - 1] + xs[mid]) / 2.0
        median_y = ys[mid] if len(cluster) % 2 else (ys[mid - 1] + ys[mid]) / 2.0
        result.append(
            {
                "siteId": f"bridge-site-{index + 1}",
                "worldX": round(median_x, 6),
                "worldY": round(median_y, 6),
                "variantCount": len(cluster),
                "variants": [item.name for item in cluster],
            }
        )
    return result


def extract_flora(flora_path: Path, csv_path: Path) -> dict[str, object]:
    counts: Counter[str] = Counter()
    variants: Counter[int] = Counter()
    min_xyz = [math.inf, math.inf, math.inf]
    max_xyz = [-math.inf, -math.inf, -math.inf]

    with flora_path.open("rb") as stream, csv_path.open("w", newline="", encoding="utf-8") as output:
        magic = stream.read(4)
        if magic != FLORA_MAGIC:
            raise ValueError(f"Unexpected flora magic {magic!r}; expected {FLORA_MAGIC!r}")
        payload_bytes, record_count = struct.unpack("<II", stream.read(8))
        if payload_bytes != flora_path.stat().st_size - 8:
            raise ValueError(
                f"flora payload length {payload_bytes} does not match file size {flora_path.stat().st_size}"
            )

        fields = [
            "index", "prefab", "variant", "world_x", "world_y", "world_z",
            "basis_xx", "basis_xy", "basis_xz", "basis_yx", "basis_yy", "basis_yz",
            "basis_zx", "basis_zy", "basis_zz", "factor_0", "factor_1", "factor_2",
            "factor_3", "factor_4",
        ]
        writer = csv.DictWriter(output, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        for index in range(record_count):
            raw_length = stream.read(4)
            if len(raw_length) != 4:
                raise ValueError(f"flora.bin ended before record {index}")
            name_length = struct.unpack("<I", raw_length)[0]
            if name_length > 1024:
                raise ValueError(f"flora record {index} has implausible name length {name_length}")
            prefab = stream.read(name_length).decode("utf-8")
            variant = struct.unpack("<I", stream.read(4))[0]
            values = struct.unpack(f"<{FLORA_FLOAT_COUNT}f", stream.read(FLORA_FLOAT_COUNT * 4))
            # The first twelve floats are a 3x4 basis (the fourth column is 0),
            # followed by world XYZ and five authored factors.
            world_x, world_y, world_z = values[12:15]
            counts[prefab] += 1
            variants[variant] += 1
            for axis, value in enumerate((world_x, world_y, world_z)):
                min_xyz[axis] = min(min_xyz[axis], value)
                max_xyz[axis] = max(max_xyz[axis], value)
            writer.writerow(
                {
                    "index": index,
                    "prefab": prefab,
                    "variant": variant,
                    "world_x": f"{world_x:.6f}",
                    "world_y": f"{world_y:.6f}",
                    "world_z": f"{world_z:.6f}",
                    "basis_xx": f"{values[0]:.9f}",
                    "basis_xy": f"{values[1]:.9f}",
                    "basis_xz": f"{values[2]:.9f}",
                    "basis_yx": f"{values[4]:.9f}",
                    "basis_yy": f"{values[5]:.9f}",
                    "basis_yz": f"{values[6]:.9f}",
                    "basis_zx": f"{values[8]:.9f}",
                    "basis_zy": f"{values[9]:.9f}",
                    "basis_zz": f"{values[10]:.9f}",
                    **{f"factor_{i}": f"{values[15 + i]:.6f}" for i in range(5)},
                }
            )

        trailing = stream.read()
        if trailing:
            raise ValueError(f"flora.bin has {len(trailing)} unexpected trailing bytes")

    return {
        "magic": magic.decode("ascii"),
        "payloadBytes": payload_bytes,
        "recordCount": record_count,
        "prefabCounts": dict(sorted(counts.items())),
        "variantCounts": {str(key): value for key, value in sorted(variants.items())},
        "worldBounds": {"min": min_xyz, "max": max_xyz},
        "csv": str(csv_path),
        "csvSha256": sha256(csv_path),
    }


def extract_material_layers(directory: Path) -> list[dict[str, object]]:
    layers: list[dict[str, object]] = []
    for path in sorted(directory.glob("layer*.png")):
        match = MATERIAL_PATTERN.match(path.name)
        if not match:
            continue
        with Image.open(path) as image:
            layers.append(
                {
                    "index": int(match.group("index")),
                    "name": match.group("name"),
                    "path": str(path.resolve()),
                    "width": image.width,
                    "height": image.height,
                    "mode": image.mode,
                    "extrema": image.getextrema(),
                    "bytes": path.stat().st_size,
                    "sha256": sha256(path),
                }
            )
    if len(layers) != 7:
        raise ValueError(f"Expected exactly seven semantic material layers, found {len(layers)}")
    if any((item["width"], item["height"]) != (4097, 4097) for item in layers):
        raise ValueError("Every material layer must be a 4097x4097 full-map editor export")
    return layers


def write_scene_csv(path: Path, placements: Iterable[ScenePlacement]) -> None:
    rows = [asdict(item) for item in placements]
    if not rows:
        raise ValueError("No strategic scene placements were extracted")
    with path.open("w", newline="", encoding="utf-8") as output:
        writer = csv.DictWriter(output, fieldnames=list(rows[0]), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scene", type=Path, required=True)
    parser.add_argument("--flora", type=Path, required=True)
    parser.add_argument("--material-layer-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    for path in (args.scene, args.flora, args.material_layer_dir):
        if not path.exists():
            raise FileNotFoundError(path)
    args.output.mkdir(parents=True, exist_ok=True)

    flora_csv = args.output / "flora-instances.csv"
    scene_csv = args.output / "scene-cartography.csv"
    manifest_path = args.output / "main-map-geography-manifest.json"

    flora = extract_flora(args.flora, flora_csv)
    placements, scene = extract_scene(args.scene)
    write_scene_csv(scene_csv, placements)
    materials = extract_material_layers(args.material_layer_dir)

    manifest = {
        "schema": SCHEMA,
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "authority": "installed Bannerlord NavalDLC/Main_map editor data",
        "sources": {
            "scene": {"path": str(args.scene.resolve()), "bytes": args.scene.stat().st_size, "sha256": sha256(args.scene)},
            "flora": {"path": str(args.flora.resolve()), "bytes": args.flora.stat().st_size, "sha256": sha256(args.flora)},
        },
        "flora": flora,
        "scene": {**scene, "csv": str(scene_csv), "csvSha256": sha256(scene_csv)},
        "materialLayers": materials,
        "invariants": {
            "floraRecordCount": flora["recordCount"] == 69_500,
            "strategicMountainCount": scene["strategicPlacementCounts"].get("mountain") == 39,
            "strategicBridgePlacementCount": scene["strategicPlacementCounts"].get("bridge") == 39,
            "bridgeSiteCount": scene["bridgeSiteCount"] == 33,
            "materialLayerCount": len(materials) == 7,
        },
    }
    if not all(manifest["invariants"].values()):
        raise ValueError(f"Main_map geography invariant failed: {manifest['invariants']}")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"ok": True, "manifest": str(manifest_path), "invariants": manifest["invariants"]}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
