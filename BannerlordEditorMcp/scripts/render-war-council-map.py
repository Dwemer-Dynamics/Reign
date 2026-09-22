#!/usr/bin/env python3
"""Render the War Council's baked, editor-accurate medieval map assets.

The renderer deliberately separates authority from appearance:

* geography comes only from losslessly audited Bannerlord editor height,
  material-layer, flora, and scene-placement data;
* variable-width river water comes from the packed official NavalDLC river mask;
* settlement coordinates and names come only from the audited settlement CSV;
* ImageGen contributes only appearance artwork: parchment, terrain symbols,
  map-detail strokes, and bridge engravings;
* all map ink is generated in one global coordinate system before runtime tiling.

That arrangement prevents AI-generated geography, settlements in water, thick lines
caused by bitmap upscaling, and visible seams between runtime texture tiles.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import random
import re
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageStat

# This renderer creates and then reopens its own audited 16K master.  Disable
# Pillow's generic web-upload bomb limit only for this trusted local pipeline.
Image.MAX_IMAGE_PIXELS = None


MASTER_SIZE = 16_384
TILE_COUNT = 4
TILE_SIZE = MASTER_SIZE // TILE_COUNT
RUNTIME_OVERVIEW_SIZE = 2_048
FIXED_STRATEGIC_SCALE = 44
WORLD_SIZE = 1040.0
# Exact playable border entities read from NavalDLC/Main_map in the official
# editor.  The terrain extends far beyond this rectangle as outer mesh; it is
# intentionally excluded from the council map.
PLAYABLE_MIN_X = 13.353
PLAYABLE_MIN_Y = 6.733
PLAYABLE_MAX_X = 992.535
PLAYABLE_MAX_Y = 937.948
SEA_LEVEL = -0.20
INK = (55, 37, 22, 232)
SOFT_INK = (69, 49, 30, 138)
FAINT_INK = (78, 57, 35, 74)
LAND_WASH = (174, 132, 76, 20)
SEA_WASH = (143, 118, 77, 18)
BEACH_WASH = (226, 196, 139, 84)
BEACH_INK = (67, 46, 27, 184)
EXPECTED_FLORA_INSTANCES = 69_500
EXPECTED_MOUNTAIN_PLACEMENTS = 39
EXPECTED_BRIDGE_PLACEMENTS = 39
EXPECTED_BRIDGE_SITES = 33
SETTLEMENT_SCALES = {"town": 200, "castle": 150, "village": 100}


@dataclass(frozen=True)
class Settlement:
    settlement_id: str
    name: str
    kind: str
    world_x: float
    world_y: float


@dataclass(frozen=True)
class FloraInstance:
    prefab: str
    world_x: float
    world_y: float
    world_z: float
    basis_xx: float
    basis_xy: float
    basis_xz: float


@dataclass(frozen=True)
class CartographicPlacement:
    category: str
    name: str
    prefab: str
    world_x: float
    world_y: float
    world_z: float
    rotation_z: float
    scale_x: float
    scale_y: float
    scale_z: float
    snow: bool


class MotifAtlas:
    """Ink-only motifs recovered from an ImageGen cartographer reference sheet.

    Geography never comes from this atlas.  The renderer places these motifs
    only where the official editor height grid proves suitable land.
    """

    def __init__(self, path: Path) -> None:
        source = Image.open(path).convert("RGB")
        self.source_sha256 = sha256(path)
        self.source_path = path
        cell_width = source.width // 4
        cell_height = source.height // 4
        self.cells: list[Image.Image] = []
        for row in range(4):
            for column in range(4):
                cell = source.crop(
                    (
                        column * cell_width,
                        row * cell_height,
                        (column + 1) * cell_width,
                        (row + 1) * cell_height,
                    )
                )
                self.cells.append(self._recover_ink(cell))

        self.mountains = self.cells[0:8]
        self.forests = self.cells[8:12]
        self.town = self.cells[12]
        self.castle = self.cells[13]
        self.village = self.cells[14]
        self.waves = self.cells[15]

    @staticmethod
    def _recover_ink(cell: Image.Image) -> Image.Image:
        rgb = np.asarray(cell, dtype=np.float32)
        gray = rgb[:, :, 0] * 0.299 + rgb[:, :, 1] * 0.587 + rgb[:, :, 2] * 0.114
        local_paper = np.asarray(
            Image.fromarray(np.clip(gray, 0, 255).astype(np.uint8), mode="L").filter(
                ImageFilter.GaussianBlur(radius=13.0)
            ),
            dtype=np.float32,
        )
        # Local contrast removes the generated parchment while preserving even
        # the finest engraved strokes.  A hard low alpha cutoff prevents the
        # atlas texture from becoming a visible rectangular stamp.
        alpha = np.clip((local_paper - gray - 8.0) * 7.0, 0.0, 255.0)
        alpha[alpha < 28.0] = 0.0
        ys, xs = np.nonzero(alpha > 0.0)
        if len(xs) == 0:
            return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
        padding = 5
        left = max(0, int(xs.min()) - padding)
        top = max(0, int(ys.min()) - padding)
        right = min(cell.width, int(xs.max()) + padding + 1)
        bottom = min(cell.height, int(ys.max()) + padding + 1)
        recovered = Image.new("RGBA", cell.size, (46, 31, 20, 0))
        recovered.putalpha(Image.fromarray(alpha.astype(np.uint8), mode="L"))
        return recovered.crop((left, top, right, bottom))

    @staticmethod
    def scaled(motif: Image.Image, target_width: int) -> Image.Image:
        target_width = max(2, target_width)
        target_height = max(2, round(motif.height * target_width / max(1, motif.width)))
        return motif.resize((target_width, target_height), Image.Resampling.LANCZOS)

    @staticmethod
    def scaled_height(motif: Image.Image, target_height: int) -> Image.Image:
        target_height = max(2, target_height)
        target_width = max(2, round(motif.width * target_height / max(1, motif.height)))
        return motif.resize((target_width, target_height), Image.Resampling.LANCZOS)


def recover_imagegen_sepia_ink(
    cell: Image.Image,
    minimum_component_pixels: int = 0,
) -> Image.Image:
    """Recover warm ink from ImageGen's baked neutral checkerboard preview.

    Built-in ImageGen currently serializes the transparency preview as RGB.  Its
    checkerboard is neutral gray while the requested iron-gall strokes are warm
    sepia, so red/blue chroma is a deterministic alpha key.  Runtime pixels are
    recolored to one ordinary-RGB ink tone; generated background and pale fills
    can never enter the authored master.
    """

    rgb = np.asarray(cell.convert("RGB"), dtype=np.float32)
    red, green, blue = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    warm_chroma = np.maximum(red - blue, (green - blue) * 1.35)
    alpha = np.clip((warm_chroma - 1.5) * 12.0, 0.0, 255.0)
    alpha[alpha < 22.0] = 0.0
    if minimum_component_pixels > 1:
        active = alpha > 0.0
        visited = np.zeros(active.shape, dtype=bool)
        kept = np.zeros(active.shape, dtype=bool)
        rows, columns = active.shape
        for seed_y, seed_x in zip(*np.nonzero(active & ~visited)):
            if visited[seed_y, seed_x]:
                continue
            stack = [(int(seed_y), int(seed_x))]
            visited[seed_y, seed_x] = True
            component: list[tuple[int, int]] = []
            while stack:
                y, x = stack.pop()
                component.append((y, x))
                for adjacent_y in range(max(0, y - 1), min(rows, y + 2)):
                    for adjacent_x in range(max(0, x - 1), min(columns, x + 2)):
                        if active[adjacent_y, adjacent_x] and not visited[adjacent_y, adjacent_x]:
                            visited[adjacent_y, adjacent_x] = True
                            stack.append((adjacent_y, adjacent_x))
            if len(component) >= minimum_component_pixels:
                for y, x in component:
                    kept[y, x] = True
        alpha[~kept] = 0.0
    ys, xs = np.nonzero(alpha > 0.0)
    if len(xs) == 0:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
    padding = 5
    left = max(0, int(xs.min()) - padding)
    top = max(0, int(ys.min()) - padding)
    right = min(cell.width, int(xs.max()) + padding + 1)
    bottom = min(cell.height, int(ys.max()) + padding + 1)
    recovered = Image.new("RGBA", cell.size, (49, 33, 19, 0))
    recovered.putalpha(Image.fromarray(alpha.astype(np.uint8), mode="L"))
    return recovered.crop((left, top, right, bottom))


def extract_equal_row(
    source: Image.Image,
    count: int,
    top: int,
    bottom: int,
    minimum_component_pixels: int = 0,
) -> list[Image.Image]:
    motifs: list[Image.Image] = []
    for index in range(count):
        left = round(index * source.width / count)
        right = round((index + 1) * source.width / count)
        motif = recover_imagegen_sepia_ink(
            source.crop((left, top, right, bottom)),
            minimum_component_pixels,
        )
        if motif.size == (1, 1):
            raise ValueError(f"ImageGen atlas row {top}:{bottom} cell {index} contained no recoverable ink")
        motifs.append(motif)
    return motifs


def extract_spaced_row(
    source: Image.Image,
    count: int,
    top: int,
    bottom: int,
    minimum_gap: int = 20,
    padding: int = 6,
) -> list[Image.Image]:
    """Extract unevenly spaced motifs without cutting one drawing into two cells."""

    row = source.crop((0, top, source.width, bottom))
    rgb = np.asarray(row.convert("RGB"), dtype=np.float32)
    red, green, blue = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    warm_chroma = np.maximum(red - blue, (green - blue) * 1.35)
    active_columns = np.nonzero((warm_chroma >= 3.333).any(axis=0))[0]
    if len(active_columns) == 0:
        raise ValueError(f"ImageGen atlas row {top}:{bottom} contained no recoverable ink")
    runs: list[tuple[int, int]] = []
    start = previous = int(active_columns[0])
    for column in active_columns[1:]:
        column = int(column)
        if column - previous - 1 >= minimum_gap:
            runs.append((start, previous))
            start = column
        previous = column
    runs.append((start, previous))
    if len(runs) != count:
        raise ValueError(
            f"ImageGen atlas row {top}:{bottom} contained {len(runs)} spaced motifs; expected {count}"
        )
    motifs: list[Image.Image] = []
    for left, right in runs:
        cell = source.crop((max(0, left - padding), top, min(source.width, right + padding + 1), bottom))
        motif = recover_imagegen_sepia_ink(cell, 12)
        if motif.size == (1, 1):
            raise ValueError(f"ImageGen atlas row {top}:{bottom} motif {len(motifs)} contained no ink")
        motifs.append(motif)
    return motifs


class TerrainMotifAtlas:
    """Image-generated trees and mountains; editor evidence owns placement."""

    def __init__(self, path: Path) -> None:
        source = Image.open(path).convert("RGB")
        if source.size != (1536, 1024):
            raise ValueError(f"Terrain atlas is {source.size}; expected 1536x1024")
        self.source_path = path
        self.source_sha256 = sha256(path)
        first_tree_row = extract_equal_row(source, 12, 0, 385, 20)
        second_tree_row = extract_equal_row(source, 13, 365, 680, 20)
        all_conifers = first_tree_row + second_tree_row[:9]
        # A few generated cells contain detached ink from a neighboring cell.
        # Use only visually audited, single-tree motifs; geography still comes
        # exclusively from exact flora.bin roots.
        self.conifers = [all_conifers[index] for index in (0, 2, 3, 4, 5, 7, 9, 10, 11, 12, 14, 15, 17, 19)]
        self.deciduous: list[Image.Image] = []
        self.trees = self.conifers
        all_mountains = (
            extract_equal_row(source, 5, 665, 865, 20)
            + extract_equal_row(source, 4, 830, 1024, 20)
        )
        self.mountains = [all_mountains[index] for index in (0, 3, 4, 6)]


class DetailMotifAtlas:
    """Image-generated ground marks and water hatching; never geography."""

    def __init__(self, path: Path) -> None:
        source = Image.open(path).convert("RGB")
        if source.size != (1690, 931):
            raise ValueError(f"Detail atlas is {source.size}; expected 1690x931")
        self.source_path = path
        self.source_sha256 = sha256(path)
        self.ground = extract_equal_row(source, 13, 315, 555)
        # The generated detail sheet deliberately separates small pebble/dot,
        # grass, and rock-hatch vocabularies.  Editor material weights decide
        # which family appears; these subsets affect appearance only.
        self.ground_dirt = [self.ground[index] for index in (2, 3, 4, 9)]
        self.ground_grass = [self.ground[index] for index in (0, 1, 5, 6, 8, 10, 12)]
        self.ground_rock = [self.ground[index] for index in (2, 3, 7, 9, 11)]
        self.water = (
            extract_equal_row(source, 6, 545, 735)
            + extract_equal_row(source, 5, 710, 931)
        )


class BridgeMotifAtlas:
    """Strict overhead ImageGen bridge engravings for measured crossings."""

    def __init__(self, path: Path) -> None:
        source = Image.open(path).convert("RGB")
        if source.size != (1690, 931):
            raise ValueError(f"Bridge atlas is {source.size}; expected 1690x931")
        self.source_path = path
        self.source_sha256 = sha256(path)
        all_bridges = extract_spaced_row(source, 8, 385, 535)
        # Every crossing intentionally uses this one continuous overhead timber
        # deck.  The atlas drawings are not laid out on an equal-width grid;
        # equal-cell extraction cut adjacent drawings together and visibly made
        # a detached abutment plus a different deck.  Whitespace-bounded
        # extraction above preserves the complete second bridge as one type.
        self.bridges = [all_bridges[1]]
        alpha = np.asarray(self.bridges[0].getchannel("A"), dtype=np.uint8)
        visible_columns = np.nonzero((alpha > 0).any(axis=0))[0]
        self.continuous_longitudinal_ink = bool(
            len(visible_columns) > 0
            and np.all((alpha[:, visible_columns[0]:visible_columns[-1] + 1] > 0).any(axis=0))
        )
        if not self.continuous_longitudinal_ink:
            raise ValueError("Selected bridge motif has a longitudinal gap and is not one continuous body")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def clean_localized_name(value: str) -> str:
    return re.sub(r"^\{=[^}]+\}", "", value).strip()


def parse_settlements(path: Path) -> list[Settlement]:
    settlements: list[Settlement] = []
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            kind = row["Type"].strip().lower()
            if kind not in {"town", "castle", "village"}:
                raise ValueError(f"Unsupported settlement type {kind!r} for {row['Id']}")
            settlements.append(
                Settlement(
                    settlement_id=row["Id"].strip(),
                    name=clean_localized_name(row["Name"]),
                    kind=kind,
                    world_x=float(row["XmlX"]),
                    world_y=float(row["XmlY"]),
                )
            )
    counts = {kind: sum(item.kind == kind for item in settlements) for kind in ("town", "castle", "village")}
    expected = {"town": 57, "castle": 76, "village": 307}
    if counts != expected:
        raise ValueError(f"Settlement counts are {counts}; expected {expected}")
    return settlements


def load_geography_manifest(path: Path) -> dict[str, object]:
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if manifest.get("schema") != "reign-war-council-main-map-geography-v1":
        raise ValueError(f"Unsupported geography manifest schema in {path}")
    invariants = manifest.get("invariants", {})
    if not invariants or not all(invariants.values()):
        raise ValueError(f"Geography manifest invariants are not all satisfied: {invariants}")
    return manifest


def parse_flora_instances(path: Path) -> list[FloraInstance]:
    instances: list[FloraInstance] = []
    with path.open("r", encoding="utf-8", newline="") as stream:
        for row in csv.DictReader(stream):
            instances.append(
                FloraInstance(
                    prefab=row["prefab"],
                    world_x=float(row["world_x"]),
                    world_y=float(row["world_y"]),
                    world_z=float(row["world_z"]),
                    basis_xx=float(row["basis_xx"]),
                    basis_xy=float(row["basis_xy"]),
                    basis_xz=float(row["basis_xz"]),
                )
            )
    if len(instances) != EXPECTED_FLORA_INSTANCES:
        raise ValueError(f"Flora CSV contains {len(instances)} records; expected {EXPECTED_FLORA_INSTANCES}")
    return instances


def parse_cartographic_placements(path: Path) -> list[CartographicPlacement]:
    placements: list[CartographicPlacement] = []
    with path.open("r", encoding="utf-8", newline="") as stream:
        for row in csv.DictReader(stream):
            placements.append(
                CartographicPlacement(
                    category=row["category"],
                    name=row["name"],
                    prefab=row["prefab"],
                    world_x=float(row["world_x"]),
                    world_y=float(row["world_y"]),
                    world_z=float(row["world_z"]),
                    rotation_z=float(row["rotation_z"]),
                    scale_x=float(row["scale_x"]),
                    scale_y=float(row["scale_y"]),
                    scale_z=float(row["scale_z"]),
                    snow=row["snow"].strip().lower() == "true",
                )
            )
    counts = Counter(item.category for item in placements)
    expected = {"bridge": EXPECTED_BRIDGE_PLACEMENTS, "mountain": EXPECTED_MOUNTAIN_PLACEMENTS}
    if dict(counts) != expected:
        raise ValueError(f"Scene cartography counts are {dict(counts)}; expected {expected}")
    return placements


def load_material_layers(manifest: dict[str, object]) -> dict[str, np.ndarray]:
    layers: dict[str, np.ndarray] = {}
    records = manifest.get("materialLayers", [])
    for record in records:
        path = Path(record["path"])
        if sha256(path) != record["sha256"]:
            raise ValueError(f"Material-layer hash changed after extraction: {path}")
        data = np.asarray(Image.open(path), dtype=np.uint16)
        if data.shape != (4097, 4097):
            raise ValueError(f"Material layer {path} is {data.shape}; expected 4097x4097")
        layers[record["name"]] = data
    expected_names = {"default", "default_1", "rock", "grass", "dirt", "dirt2", "forest"}
    if set(layers) != expected_names:
        raise ValueError(f"Material layers are {sorted(layers)}; expected {sorted(expected_names)}")
    return layers


def load_height_grid(path: Path) -> np.ndarray:
    values = np.fromfile(path, dtype="<f4")
    expected = 1025 * 1025
    if values.size != expected:
        raise ValueError(f"Height grid contains {values.size} floats; expected {expected}")
    grid_y_up = values.reshape((1025, 1025))
    # The rendered map is north-up: image row zero corresponds to maximum world Y.
    return np.flipud(grid_y_up).copy()


def load_official_height_texture(path: Path) -> np.ndarray:
    height = np.asarray(Image.open(path), dtype=np.uint16)
    if height.shape != (4096, 4096):
        raise ValueError(f"Official height texture is {height.shape}; expected 4096x4096")
    return height


def load_official_river_mask(path: Path) -> np.ndarray:
    rgba = np.asarray(Image.open(path).convert("RGBA"), dtype=np.uint8)
    if rgba.shape != (1024, 1024, 4):
        raise ValueError(f"Official river mask is {rgba.shape}; expected 1024x1024 RGBA")
    # The packed worldmap_river_mask stores the authored river raster in blue,
    # with texture row zero at minimum world Y.  The council master is north-up
    # (row zero is maximum world Y), so this authority texture requires exactly
    # one vertical flip.  Do not mirror X: editor bridge alignment rejects it.
    return np.flipud(rgba[:, :, 2] >= 128).copy()


def map_bounds(size: int) -> tuple[int, int, int, int]:
    # Keep map ink clear of the baked rollers and physical edge clutter. These
    # margins appear only when the player pans to the corresponding map extreme.
    return (
        round(size * 0.078),
        round(size * 0.092),
        round(size * 0.922),
        round(size * 0.908),
    )


def world_to_pixel(x: float, y: float, bounds: Sequence[int]) -> tuple[float, float]:
    left, top, right, bottom = bounds
    nx = (x - PLAYABLE_MIN_X) / (PLAYABLE_MAX_X - PLAYABLE_MIN_X)
    ny = (y - PLAYABLE_MIN_Y) / (PLAYABLE_MAX_Y - PLAYABLE_MIN_Y)
    px = left + np.clip(nx, 0.0, 1.0) * (right - left)
    py = top + (1.0 - np.clip(ny, 0.0, 1.0)) * (bottom - top)
    return px, py


def height_at_world(height: np.ndarray, x: float, y: float) -> float:
    column = int(round(np.clip(x / WORLD_SIZE * 1024.0, 0.0, 1024.0)))
    # height is already north-up.
    row = int(round(np.clip((1.0 - y / WORLD_SIZE) * 1024.0, 0.0, 1024.0)))
    return float(height[row, column])


def find_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    candidates = [
        Path(r"C:\Windows\Fonts\georgiab.ttf" if bold else r"C:\Windows\Fonts\georgia.ttf"),
        Path(r"C:\Windows\Fonts\timesbd.ttf" if bold else r"C:\Windows\Fonts\times.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default(size=size)


def add_native_parchment_detail(base: Image.Image, seed: int = 7355608) -> Image.Image:
    """Add high-frequency fibers after upscaling the ImageGen material base."""
    rng = random.Random(seed)
    result = base.convert("RGB")
    draw = ImageDraw.Draw(result, "RGBA")
    width, height = result.size
    # Fine irregular fibers remain sharp at the fixed strategic close scale.
    for _ in range(58_000):
        x = rng.randrange(width)
        y = rng.randrange(height)
        length = rng.randint(5, 42)
        alpha = rng.randint(4, 13)
        color = (62, 40, 22, alpha) if rng.random() < 0.58 else (255, 240, 192, alpha)
        draw.line((x, y, min(width - 1, x + length), y + rng.choice((-1, 0, 0, 0, 1))), fill=color, width=1)
    for _ in range(3_200):
        x = rng.randrange(width)
        y = rng.randrange(height)
        radius = rng.choice((1, 1, 1, 2))
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(45, 29, 17, rng.randint(6, 20)))
    return result


def make_land_mask(height_texture: np.ndarray, bounds: Sequence[int], size: int) -> tuple[Image.Image, int]:
    left, top, right, bottom = bounds
    # The dominant R16 value is the official flat ocean plane.  Preserve the
    # texture's 4K coastline resolution and feather only the sub-pixel boundary.
    ocean_baseline = int(np.bincount(height_texture.ravel(), minlength=65_536).argmax())
    shoreline_band = np.clip(
        (height_texture.astype(np.float32) - float(ocean_baseline - 64)) / 1024.0,
        0.0,
        1.0,
    )
    source_limit = height_texture.shape[0] - 1
    column0 = max(0, int(math.floor(PLAYABLE_MIN_X / WORLD_SIZE * source_limit)))
    column1 = min(source_limit, int(math.ceil(PLAYABLE_MAX_X / WORLD_SIZE * source_limit)))
    row0 = max(0, int(math.floor((1.0 - PLAYABLE_MAX_Y / WORLD_SIZE) * source_limit)))
    row1 = min(source_limit, int(math.ceil((1.0 - PLAYABLE_MIN_Y / WORLD_SIZE) * source_limit)))
    shoreline_band = shoreline_band[row0 : row1 + 1, column0 : column1 + 1]
    source = Image.fromarray(np.round(shoreline_band * 255.0).astype(np.uint8), mode="L")
    source = source.filter(ImageFilter.GaussianBlur(radius=0.72))
    inner = source.resize((right - left, bottom - top), Image.Resampling.BICUBIC)
    inner = inner.point(lambda value: 255 if value >= 128 else 0)
    mask = Image.new("L", (size, size), 0)
    mask.paste(inner, (left, top))
    return mask, ocean_baseline


def draw_washes(base: Image.Image, land_mask: Image.Image, bounds: Sequence[int]) -> None:
    left, top, right, bottom = bounds
    inner_mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(inner_mask).rectangle((left, top, right - 1, bottom - 1), fill=255)
    sea_mask = Image.new("L", base.size, 0)
    sea_mask.paste(Image.eval(land_mask, lambda v: 255 - v), mask=inner_mask)
    land_layer = Image.new("RGBA", base.size, LAND_WASH)
    sea_layer = Image.new("RGBA", base.size, SEA_WASH)
    base.paste(land_layer, (0, 0), ImageChops_multiply_alpha(land_mask, LAND_WASH[3]))
    base.paste(sea_layer, (0, 0), ImageChops_multiply_alpha(sea_mask, SEA_WASH[3]))


def ImageChops_multiply_alpha(mask: Image.Image, alpha: int) -> Image.Image:
    return mask.point(lambda value: (value * alpha) // 255)


def draw_beach_band(
    canvas: Image.Image,
    land_mask: Image.Image,
    bounds: Sequence[int],
    width_pixels: int,
) -> tuple[dict[str, object], Image.Image | None]:
    """Lay a narrow parchment-sand band inside every authoritative shore.

    The band is derived only by inward morphology of the final unified land
    mask, so it follows exact sea, lake, and variable-width river banks without
    changing the water geometry or inventing a second coastline.
    """

    if width_pixels <= 0:
        return {
            "beachWidthPixels": 0,
            "beachBandPixels": 0,
            "method": "disabled",
        }, None
    left, top, right, bottom = bounds
    interior = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(interior).rectangle((left + 10, top + 10, right - 11, bottom - 11), fill=255)
    # The source coast is itself a 4K official texture and the authored rivers
    # are 1K.  Perform only this cosmetic inward-distance operation at quarter
    # master resolution, then return to 16K.  The full-resolution outer boundary
    # remains untouched and authoritative while avoiding an impractical 33x33
    # rank filter over 268 million pixels.
    morphology_scale = 4
    small_size = (
        math.ceil(land_mask.width / morphology_scale),
        math.ceil(land_mask.height / morphology_scale),
    )
    small_land = land_mask.resize(small_size, Image.Resampling.NEAREST)
    small_radius = max(1, round(width_pixels / morphology_scale))
    contracted_small = small_land.filter(ImageFilter.MinFilter(small_radius * 2 + 1))
    contracted = contracted_small.resize(land_mask.size, Image.Resampling.BICUBIC).point(
        lambda value: 255 if value >= 128 else 0
    )
    beach_band = ImageChops.multiply(ImageChops.subtract(land_mask, contracted), interior)
    canvas.paste(
        Image.new("RGBA", canvas.size, BEACH_WASH),
        (0, 0),
        ImageChops_multiply_alpha(beach_band, BEACH_WASH[3]),
    )
    return {
        "beachWidthPixels": width_pixels,
        "beachBandPixels": sum(beach_band.histogram()[1:]),
        "morphologyResolution": {"width": small_size[0], "height": small_size[1]},
        "method": "light parchment-sand wash clipped to a cosmetic inward band of the unified authoritative sea/lake/river shoreline; the outer geographic boundary remains full-resolution and unchanged",
    }, contracted


def draw_coastline(
    canvas: Image.Image,
    land_mask: Image.Image,
    bounds: Sequence[int],
    beach_inner_land: Image.Image | None,
) -> dict[str, object]:
    left, top, right, bottom = bounds
    # The playable crop is not a shoreline. Suppress morphology exactly at its
    # four cut edges so land continuing into excluded outer mesh never becomes
    # an artificial straight coast on the parchment.
    interior = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(interior).rectangle((left + 10, top + 10, right - 11, bottom - 11), fill=255)
    # This is the single geographic contour for oceans, lakes, and both banks
    # of every authored river.  Its master-resolution stroke must survive the
    # native texture sampler at the fixed strategic view; a 1-2 pixel line at
    # 16K disappeared in game even though the river water itself was exact.
    # Keep the shared sea/lake/river contour at the original engraved-line
    # weight.  The War Council displays the authored master at 48x strategic
    # scale, so a nine-pixel master stroke becomes an oversized dark band in
    # native play and makes rivers look painted on top of the cartography.
    # The channel width still comes entirely from the authoritative water
    # mask; only the paired-bank ink is narrowed here.
    expanded = land_mask.filter(ImageFilter.MaxFilter(3))
    contracted = land_mask.filter(ImageFilter.MinFilter(3))
    # Keep morphology in Pillow's 8-bit image engine.  Converting two 16K masks
    # to int16 NumPy arrays requires more than a gigabyte of transient memory.
    edge_mask = ImageChops.multiply(
        ImageChops.subtract(expanded, contracted).point(lambda value: 238 if value > 0 else 0),
        interior,
    )
    coast = Image.new("RGBA", canvas.size, INK)
    canvas.paste(coast, (0, 0), edge_mask)

    inner_beach_edge_pixels = 0
    if beach_inner_land is not None:
        beach_inner_expanded = beach_inner_land.filter(ImageFilter.MaxFilter(3))
        beach_inner_contracted = beach_inner_land.filter(ImageFilter.MinFilter(3))
        inner_edge_mask = ImageChops.multiply(
            ImageChops.subtract(beach_inner_expanded, beach_inner_contracted).point(
                lambda value: 176 if value > 0 else 0
            ),
            interior,
        )
        inner_beach_edge_pixels = sum(inner_edge_mask.histogram()[1:])
        canvas.paste(Image.new("RGBA", canvas.size, BEACH_INK), (0, 0), inner_edge_mask)

    # A restrained outer echo is generated from that same contour.  It is not
    # a river centerline or river-only overlay.
    outer = land_mask.filter(ImageFilter.MaxFilter(7))
    echo_mask = ImageChops.multiply(
        ImageChops.subtract(outer, expanded).point(lambda value: 92 if value > 0 else 0),
        interior,
    )
    canvas.paste(Image.new("RGBA", canvas.size, SOFT_INK), (0, 0), echo_mask)
    return {
        "sharedBoundaryStrokePixels": sum(edge_mask.histogram()[1:]),
        "innerBeachEdgePixels": inner_beach_edge_pixels,
        "outerEchoPixels": sum(echo_mask.histogram()[1:]),
        "method": "one shared authoritative water boundary plus a restrained inner beach edge and outer echo; no independent or redrawn coastline",
    }


def draw_sea_hatching(canvas: Image.Image, land_mask: Image.Image, bounds: Sequence[int]) -> None:
    left, top, right, bottom = bounds
    waves = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(waves, "RGBA")
    rng = random.Random(88191)
    for y in range(top + 24, bottom, 54):
        phase = rng.randint(-25, 25)
        for x in range(left + phase, right, 130):
            length = rng.randint(52, 104)
            lift = rng.choice((-2, -1, 0, 1, 2))
            draw.arc((x, y - 7 + lift, x + length, y + 8 + lift), 200, 340, fill=FAINT_INK, width=1)
    sea_mask = Image.eval(land_mask, lambda value: 255 - value)
    canvas.alpha_composite(Image.composite(waves, Image.new("RGBA", canvas.size), sea_mask))


def carve_official_river_water(
    official_mask: np.ndarray,
    land_mask: Image.Image,
    bounds: Sequence[int],
) -> tuple[Image.Image, Image.Image, dict[str, object]]:
    """Make the authored river area part of the base land/water geography.

    The packed NavalDLC mask already contains variable-width navigable channels.
    It must never be thinned to a centerline or redrawn at a fixed width.  This
    projects the exact playable world crop onto the parchment, gates it by the
    official height-derived land, and subtracts it before any wash, material ink,
    or coastline is produced.  The ordinary coastline pass therefore inks both
    river banks as original geographic edges, continuous with lakes and seas.
    """
    left, top, right, bottom = bounds
    rows, columns = official_mask.shape
    source = Image.fromarray(official_mask.astype(np.uint8) * 255, mode="L")
    source_extent = (
        PLAYABLE_MIN_X / WORLD_SIZE * (columns - 1),
        (1.0 - PLAYABLE_MAX_Y / WORLD_SIZE) * (rows - 1),
        PLAYABLE_MAX_X / WORLD_SIZE * (columns - 1),
        (1.0 - PLAYABLE_MIN_Y / WORLD_SIZE) * (rows - 1),
    )
    playable = source.transform(
        (right - left, bottom - top),
        Image.Transform.EXTENT,
        source_extent,
        Image.Resampling.BICUBIC,
    )
    projected = Image.new("L", land_mask.size, 0)
    projected.paste(playable, (left, top))
    land_bound_water = ImageChops.multiply(projected, land_mask)
    carved_land = ImageChops.subtract(land_mask, land_bound_water)
    return carved_land, land_bound_water, {
        "sourcePixels": int(np.count_nonzero(official_mask)),
        "projectedPlayablePixels": int(np.count_nonzero(np.asarray(playable) >= 128)),
        "landBoundWaterPixels": int(np.count_nonzero(np.asarray(land_bound_water) >= 128)),
        "sourceResolution": {"width": columns, "height": rows},
        "sourcePlayableExtent": {
            "left": round(source_extent[0], 6),
            "top": round(source_extent[1], 6),
            "right": round(source_extent[2], 6),
            "bottom": round(source_extent[3], 6),
        },
        "masterProjection": {
            "left": left,
            "top": top,
            "right": right,
            "bottom": bottom,
        },
        "sourceTextureTransform": "vertical flip from texture row-zero=min-world-Y to north-up row-zero=max-world-Y; no horizontal mirror",
        "widthPolicy": "preserve the authored 50-percent mask contour with bicubic edge coverage; no skeleton, centerline, geometry smoothing, or fixed-width redraw",
        "composition": "river area subtracted from official land before washes, terrain ink, and shared coastline bank generation",
    }


def audit_river_bridge_alignment(
    official_mask: np.ndarray,
    bridge_sites: Sequence[dict[str, object]],
) -> dict[str, object]:
    """Prove the river texture orientation against independent editor geometry."""
    river_pixels = np.argwhere(official_mask)
    if river_pixels.size == 0:
        raise ValueError("Official river mask contains no authored river pixels")
    rows, columns = official_mask.shape
    distances: list[float] = []
    for site in bridge_sites:
        row = (1.0 - float(site["worldY"]) / WORLD_SIZE) * (rows - 1)
        column = float(site["worldX"]) / WORLD_SIZE * (columns - 1)
        squared = np.square(river_pixels - np.array((row, column))).sum(axis=1)
        distances.append(float(np.sqrt(squared.min()) * WORLD_SIZE / (columns - 1)))
    within_ten = sum(distance <= 10.0 for distance in distances)
    # One of the 33 clustered bridge sites is a coastal approach rather than a
    # river crossing. Every other site must independently confirm orientation.
    if within_ten < len(bridge_sites) - 1:
        raise ValueError(
            f"River-mask orientation failed editor bridge alignment: {within_ten}/{len(bridge_sites)} within 10 world units"
        )
    return {
        "bridgeSiteCount": len(bridge_sites),
        "withinFiveWorldUnits": sum(distance <= 5.0 for distance in distances),
        "withinTenWorldUnits": within_ten,
        "medianDistanceWorldUnits": round(float(np.median(distances)), 3),
        "maximumAlignedDistanceWorldUnits": round(max(distance for distance in distances if distance <= 10.0), 3),
        "outlierDistancesWorldUnits": [round(distance, 3) for distance in distances if distance > 10.0],
        "authority": "33 physical bridge sites clustered from installed NavalDLC/Main_map top-level scene transforms",
    }


def paste_centered(canvas: Image.Image, motif: Image.Image, x: float, y: float, target_width: int) -> tuple[int, int, int, int]:
    stamp = MotifAtlas.scaled(motif, target_width)
    left = round(x - stamp.width / 2.0)
    top = round(y - stamp.height / 2.0)
    canvas.alpha_composite(stamp, (left, top))
    return left, top, left + stamp.width, top + stamp.height


def with_scaled_alpha(motif: Image.Image, opacity: float) -> Image.Image:
    opacity = float(np.clip(opacity, 0.0, 1.0))
    result = motif.copy()
    result.putalpha(result.getchannel("A").point(lambda value: round(value * opacity)))
    return result


def crisp_scaled_ink(motif: Image.Image, target_width: int, opacity: float) -> Image.Image:
    """Downscale ImageGen line art into sharp native-master engraving pixels."""

    stamp = MotifAtlas.scaled(motif, target_width)
    alpha = stamp.getchannel("A").filter(
        ImageFilter.UnsharpMask(radius=0.65, percent=260, threshold=2)
    )
    alpha = alpha.point(
        lambda value: 0
        if value < 34
        else round(min(255, 44 + (value - 34) * 1.28))
    )
    stamp.putalpha(alpha.point(lambda value: round(value * float(np.clip(opacity, 0.0, 1.0)))))
    return stamp


def paste_baseline(
    canvas: Image.Image,
    motif: Image.Image,
    x: float,
    baseline_y: float,
    target_height: int,
) -> tuple[int, int, int, int]:
    stamp = MotifAtlas.scaled_height(motif, target_height)
    left = round(x - stamp.width / 2.0)
    top = round(baseline_y - stamp.height)
    canvas.alpha_composite(stamp, (left, top))
    return left, top, left + stamp.width, top + stamp.height


def paste_masked(
    canvas: Image.Image,
    stamp: Image.Image,
    left: int,
    top: int,
    allowed_mask: Image.Image,
) -> bool:
    right = left + stamp.width
    bottom = top + stamp.height
    clip_left = max(0, left)
    clip_top = max(0, top)
    clip_right = min(canvas.width, right)
    clip_bottom = min(canvas.height, bottom)
    if clip_left >= clip_right or clip_top >= clip_bottom:
        return False
    source = stamp.crop((clip_left - left, clip_top - top, clip_right - left, clip_bottom - top))
    gate = allowed_mask.crop((clip_left, clip_top, clip_right, clip_bottom))
    source.putalpha(ImageChops.multiply(source.getchannel("A"), gate))
    if source.getchannel("A").getbbox() is None:
        return False
    canvas.alpha_composite(source, (clip_left, clip_top))
    return True


def rectangles_intersect(
    first: tuple[int, int, int, int],
    second: tuple[int, int, int, int],
    padding: int = 0,
) -> bool:
    return not (
        first[2] + padding <= second[0]
        or second[2] + padding <= first[0]
        or first[3] + padding <= second[1]
        or second[3] + padding <= first[1]
    )


def playable_material_crop(layer: np.ndarray) -> np.ndarray:
    source_limit = layer.shape[0] - 1
    column0 = max(0, int(math.floor(PLAYABLE_MIN_X / WORLD_SIZE * source_limit)))
    column1 = min(source_limit, int(math.ceil(PLAYABLE_MAX_X / WORLD_SIZE * source_limit)))
    row0 = max(0, int(math.floor((1.0 - PLAYABLE_MAX_Y / WORLD_SIZE) * source_limit)))
    row1 = min(source_limit, int(math.ceil((1.0 - PLAYABLE_MIN_Y / WORLD_SIZE) * source_limit)))
    return layer[row0 : row1 + 1, column0 : column1 + 1]


def material_to_l(layer: np.ndarray) -> Image.Image:
    return Image.fromarray(np.round(layer.astype(np.float32) / 257.0).astype(np.uint8), mode="L")


def draw_authored_ground_and_water(
    canvas: Image.Image,
    material_layers: dict[str, np.ndarray],
    land_mask: Image.Image,
    bounds: Sequence[int],
    atlas: DetailMotifAtlas,
    ground_spacing: int,
    ground_min_width: int,
    ground_max_width: int,
    plain_dot_bias: float,
    water_spacing_y: int,
    water_spacing_x: int,
    water_min_width: int,
    water_max_width: int,
    water_opacity: float,
    crisp_detail_ink: bool,
) -> dict[str, object]:
    """Add quiet map artwork without exposing Bannerlord's topology render.

    The editor material maps influence only the density and motif choice of
    tiny appearance strokes.  Their raw weights, relief, contours, drainage,
    and topology are never composited.  Water uses the same parchment base as
    land and receives one consistent family of horizontal ink hatches across
    seas, lakes, and authored rivers.
    """

    left, top, right, bottom = bounds
    material_crops = {
        name: playable_material_crop(material_layers[name])
        for name in ("rock", "grass", "dirt", "dirt2", "forest")
    }
    source_height, source_width = material_crops["rock"].shape
    ground_count = 0
    for cell_y, y0 in enumerate(range(top + 28, bottom - 28, ground_spacing)):
        for cell_x, x0 in enumerate(range(left + 28, right - 28, ground_spacing)):
            token = hashlib.sha256(f"ground|{cell_x}|{cell_y}".encode("utf-8")).digest()
            jitter_x = int.from_bytes(token[0:2], "little") % 65 - 32
            jitter_y = int.from_bytes(token[2:4], "little") % 65 - 32
            x = int(np.clip(x0 + jitter_x, left + 6, right - 7))
            y = int(np.clip(y0 + jitter_y, top + 6, bottom - 7))
            if land_mask.getpixel((x, y)) < 128:
                continue
            source_x = int(np.clip(round((x - left) / max(1, right - left - 1) * (source_width - 1)), 0, source_width - 1))
            source_y = int(np.clip(round((y - top) / max(1, bottom - top - 1) * (source_height - 1)), 0, source_height - 1))
            weights = [material_crops[name][source_y, source_x] for name in ("rock", "grass", "dirt", "dirt2", "forest")]
            dominant_index = int(np.argmax(weights))
            dominant_weight = float(weights[dominant_index]) / 65535.0
            probability = 0.34 + dominant_weight * 0.46
            if token[4] / 255.0 > probability:
                continue
            dominant_name = ("rock", "grass", "dirt", "dirt2", "forest")[dominant_index]
            if plain_dot_bias > 0.0:
                if dominant_name in {"dirt", "dirt2"}:
                    motif_pool = atlas.ground_dirt
                elif dominant_name == "grass":
                    motif_pool = atlas.ground_dirt if token[5] / 255.0 < plain_dot_bias else atlas.ground_grass
                elif dominant_name == "rock":
                    motif_pool = atlas.ground_rock
                else:
                    motif_pool = atlas.ground_grass
                motif = motif_pool[token[8] % len(motif_pool)]
            else:
                motif_index = (dominant_index * 3 + token[5]) % len(atlas.ground)
                motif = atlas.ground[motif_index]
            target_width = ground_min_width + token[6] % (ground_max_width - ground_min_width + 1)
            opacity = (0.48 if crisp_detail_ink else 0.36) + token[7] / 255.0 * (0.16 if crisp_detail_ink else 0.18)
            stamp = (
                crisp_scaled_ink(motif, target_width, opacity)
                if crisp_detail_ink
                else with_scaled_alpha(MotifAtlas.scaled(motif, target_width), opacity)
            )
            if paste_masked(canvas, stamp, x - stamp.width // 2, y - stamp.height // 2, land_mask):
                ground_count += 1

    interior = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(interior).rectangle((left, top, right - 1, bottom - 1), fill=255)
    water_mask = ImageChops.multiply(Image.eval(land_mask, lambda value: 255 - value), interior)
    water_count = 0
    for row, y in enumerate(range(top + 18, bottom - 18, water_spacing_y)):
        phase_token = hashlib.sha256(f"water-row|{row}".encode("utf-8")).digest()
        phase = int.from_bytes(phase_token[:2], "little") % water_spacing_x
        for column, x in enumerate(range(left - water_spacing_x + phase, right, water_spacing_x)):
            token = hashlib.sha256(f"water|{row}|{column}".encode("utf-8")).digest()
            motif = atlas.water[token[0] % len(atlas.water)]
            target_width = water_min_width + token[1] % (water_max_width - water_min_width + 1)
            stamp = (
                crisp_scaled_ink(motif, target_width, water_opacity)
                if crisp_detail_ink
                else with_scaled_alpha(MotifAtlas.scaled(motif, target_width), water_opacity)
            )
            if paste_masked(canvas, stamp, x, y - stamp.height // 2, water_mask):
                water_count += 1

    return {
        "sourceResolution": {"width": source_width, "height": source_height},
        "materialLayersUsedForAppearanceOnly": ["rock", "grass", "dirt", "dirt2", "forest"],
        "rawTopologyPixelsComposited": 0,
        "groundMotifs": ground_count,
        "groundSpacingPixels": ground_spacing,
        "groundMotifWidthPixels": {"minimum": ground_min_width, "maximum": ground_max_width},
        "plainDotBias": plain_dot_bias,
        "waterHatchMotifs": water_count,
        "waterHatchSpacing": {"horizontal": water_spacing_x, "vertical": water_spacing_y},
        "waterHatchWidthPixels": {"minimum": water_min_width, "maximum": water_max_width},
        "waterHatchOpacity": water_opacity,
        "crispNativeDetailInk": crisp_detail_ink,
        "imageGenDetailAtlas": str(atlas.source_path),
        "imageGenDetailAtlasSha256": atlas.source_sha256,
        "method": "editor material weights select sparse ImageGen ground marks without rendering game topology; one consistent ImageGen hatch family is clipped to the unified sea/lake/river water mask",
    }


def draw_exact_flora(
    canvas: Image.Image,
    flora: Sequence[FloraInstance],
    bounds: Sequence[int],
    atlas: TerrainMotifAtlas,
    land_mask: Image.Image,
    blocked_bboxes: Sequence[tuple[int, int, int, int]],
    minimum_ink_separation_pixels: int,
) -> dict[str, object]:
    counts: Counter[str] = Counter()
    candidates: list[tuple[float, bytes, FloraInstance, Image.Image, int, tuple[int, int, int, int]]] = []
    skipped_water = 0
    for tree in flora:
        if not (PLAYABLE_MIN_X <= tree.world_x <= PLAYABLE_MAX_X and PLAYABLE_MIN_Y <= tree.world_y <= PLAYABLE_MAX_Y):
            continue
        x, y = world_to_pixel(tree.world_x, tree.world_y, bounds)
        scale = math.sqrt(tree.basis_xx * tree.basis_xx + tree.basis_xy * tree.basis_xy + tree.basis_xz * tree.basis_xz)
        name = tree.prefab.lower()
        token = hashlib.sha256(
            f"{tree.prefab}|{tree.world_x:.6f}|{tree.world_y:.6f}|{tree.world_z:.6f}".encode("utf-8")
        ).digest()
        # The War Council uses one coherent conifer engraving language, as in
        # the accepted original artwork.  Exact flora transforms still own all
        # forest geography; Bannerlord prefab families only influence scale.
        motif_pool = atlas.conifers
        if "beech" in name or "acacia" in name:
            target_height = int(np.clip(round(62.0 + (scale - 0.20) * 122.0), 54, 92))
        else:
            target_height = int(np.clip(round(68.0 + (scale - 0.22) * 144.0), 58, 104))
        motif = motif_pool[token[0] % len(motif_pool)]
        stamp = MotifAtlas.scaled_height(motif, target_height)
        bbox = (
            round(x - stamp.width / 2.0),
            round(y - stamp.height),
            round(x - stamp.width / 2.0) + stamp.width,
            round(y - stamp.height) + stamp.height,
        )
        root_x, root_y = int(round(x)), int(round(y))
        if not (0 <= root_x < land_mask.width and 0 <= root_y < land_mask.height) or land_mask.getpixel((root_x, root_y)) < 128:
            skipped_water += 1
            continue
        counts[tree.prefab] += 1
        candidates.append((scale, token, tree, motif, target_height, bbox))

    # Prefer mature exact transforms, with a stable digest tie-break.  Rectangle
    # occupancy removes only visual overlaps; accepted roots are never moved,
    # synthesized, or snapped away from the official flora.bin coordinates.
    candidates.sort(key=lambda item: (-item[0], item[1]))
    cell_size = 96
    occupied: dict[tuple[int, int], list[tuple[int, int, int, int]]] = {}

    def cells_for(rect: tuple[int, int, int, int]) -> Iterable[tuple[int, int]]:
        return (
            (column, row)
            for row in range(math.floor(rect[1] / cell_size), math.floor((rect[3] - 1) / cell_size) + 1)
            for column in range(math.floor(rect[0] / cell_size), math.floor((rect[2] - 1) / cell_size) + 1)
        )

    for blocked in blocked_bboxes:
        for cell in cells_for(blocked):
            occupied.setdefault(cell, []).append(blocked)

    representatives: list[dict[str, object]] = []
    collision_culled = 0
    for scale, token, tree, motif, target_height, bbox in candidates:
        nearby: list[tuple[int, int, int, int]] = []
        for cell in cells_for(bbox):
            nearby.extend(occupied.get(cell, ()))
        if any(
            rectangles_intersect(
                bbox,
                other,
                padding=minimum_ink_separation_pixels,
            )
            for other in nearby
        ):
            collision_culled += 1
            continue
        x, y = world_to_pixel(tree.world_x, tree.world_y, bounds)
        drawn_bbox = paste_baseline(canvas, motif, x, y, target_height)
        for cell in cells_for(drawn_bbox):
            occupied.setdefault(cell, []).append(drawn_bbox)
        representatives.append({
            "worldX": tree.world_x,
            "worldY": tree.world_y,
            "prefab": tree.prefab,
            "heightPixels": target_height,
        })

    return {
        "sourceInstances": len(flora),
        "exactLandCandidates": len(candidates),
        "renderedDeclutteredRepresentatives": len(representatives),
        "collisionCulled": collision_culled,
        "skippedWater": skipped_water,
        "prefabCounts": dict(sorted(counts.items())),
        "imageGenTreeMotifs": len(atlas.trees),
        "imageGenTerrainAtlas": str(atlas.source_path),
        "imageGenTerrainAtlasSha256": atlas.source_sha256,
        "minimumInkSeparationPixels": minimum_ink_separation_pixels,
        "method": "natural-size ImageGen tree engravings selected only from exact flora.bin transforms; stable mature-first rectangle occupancy culls overlaps without moving or synthesizing a tree root",
        "_representatives": representatives,
    }


def draw_exact_mountains(
    canvas: Image.Image,
    placements: Sequence[CartographicPlacement],
    bounds: Sequence[int],
    atlas: TerrainMotifAtlas,
    height_grid: np.ndarray,
    material_layers: dict[str, np.ndarray],
    land_mask: Image.Image,
    blocked_bboxes: Sequence[tuple[int, int, int, int]],
    minimum_grid_distance: float,
    placement_limit: int,
    mountain_scale: float,
) -> tuple[dict[str, object], list[tuple[int, int, int, int]]]:
    mountains = [item for item in placements if item.category == "mountain"]
    rows, columns = height_grid.shape
    if (rows, columns) != (1025, 1025):
        raise ValueError(f"Mountain placement height grid is {height_grid.shape}; expected 1025x1025")
    y_world = np.linspace(WORLD_SIZE, 0.0, rows)
    x_world = np.linspace(0.0, WORLD_SIZE, columns)
    playable = (
        (x_world[None, :] >= PLAYABLE_MIN_X)
        & (x_world[None, :] <= PLAYABLE_MAX_X)
        & (y_world[:, None] >= PLAYABLE_MIN_Y)
        & (y_world[:, None] <= PLAYABLE_MAX_Y)
        & (height_grid > SEA_LEVEL)
    )
    land_heights = height_grid[playable]
    gradient_y, gradient_x = np.gradient(height_grid)
    slope = np.hypot(gradient_x, gradient_y)
    rock_grid = material_layers["rock"][::4, ::4].astype(np.float32) / 65535.0
    if rock_grid.shape != height_grid.shape:
        raise ValueError(f"Mountain rock grid is {rock_grid.shape}; expected {height_grid.shape}")

    # The previous 75th-elevation/70th-slope gate found only isolated local
    # maxima.  On Main_map, the exposed-rock material is the authoritative
    # footprint of broad mountain massifs such as the Zeonica-Amitatys range,
    # including their lower shoulders.  Height and slope supplement that exact
    # material footprint where snow or other terrain paint covers a ridge.
    elevation_threshold = float(np.percentile(land_heights, 65.0))
    high_elevation_threshold = float(np.percentile(land_heights, 88.0))
    slope_threshold = float(np.percentile(slope[playable], 52.0))
    summit_slope_floor = float(np.percentile(slope[playable], 40.0))
    rock_threshold = 0.12
    rock_zone = playable & (rock_grid >= rock_threshold)
    height_ridge_zone = playable & (height_grid >= elevation_threshold) & (slope >= slope_threshold)
    summit_zone = playable & (height_grid >= high_elevation_threshold) & (slope >= summit_slope_floor)
    mountain_zone = rock_zone | height_ridge_zone | summit_zone

    candidate_records: list[dict[str, object]] = []
    # Inspect every authoritative editor grid cell.  A four-cell stride made
    # dense ranges fall into visible horizontal bands at the 44x runtime view.
    # Spatial allocation and minimum distance still declutter the result, but
    # candidate roots now follow the irregular rock/elevation evidence itself.
    sample_step = 1
    for row in range(0, rows, sample_step):
        world_y = float(y_world[row])
        if not (PLAYABLE_MIN_Y <= world_y <= PLAYABLE_MAX_Y):
            continue
        for column in range(0, columns, sample_step):
            world_x = float(x_world[column])
            if not (PLAYABLE_MIN_X <= world_x <= PLAYABLE_MAX_X) or not playable[row, column]:
                continue
            elevation = float(height_grid[row, column])
            local_slope = float(slope[row, column])
            rock_weight = float(rock_grid[row, column])
            if not mountain_zone[row, column]:
                continue
            score = (
                rock_weight * 4.0
                + max(0.0, (elevation - elevation_threshold) / 5.0)
                + max(0.0, (local_slope - summit_slope_floor) / 0.48)
            )
            candidate_records.append({
                "row": row,
                "column": column,
                "worldX": world_x,
                "worldY": world_y,
                "elevation": elevation,
                "slope": local_slope,
                "rockWeight": rock_weight,
                "score": score,
                "sceneAnchor": False,
                "name": f"height-{row}-{column}",
            })

    accepted: list[dict[str, object]] = []
    grid_cell_size = 9
    occupied: dict[tuple[int, int], list[dict[str, object]]] = {}
    def register(record: dict[str, object], force: bool = False) -> bool:
        row = int(record["row"])
        column = int(record["column"])
        cell = (column // grid_cell_size, row // grid_cell_size)
        nearby: list[dict[str, object]] = []
        for cy in range(cell[1] - 1, cell[1] + 2):
            for cx in range(cell[0] - 1, cell[0] + 2):
                nearby.extend(occupied.get((cx, cy), ()))
        if not force and any(
            (column - int(other["column"])) ** 2 + (row - int(other["row"])) ** 2
            < minimum_grid_distance ** 2
            for other in nearby
        ):
            return False
        accepted.append(record)
        occupied.setdefault(cell, []).append(record)
        return True

    # Preserve every official top-level mountain transform as source evidence,
    # but cluster adjacent props into one physical engraved range.  The native
    # scene commonly uses several overlapping props to build one mountain.
    for mountain in mountains:
        row = int(round(np.clip((1.0 - mountain.world_y / WORLD_SIZE) * 1024.0, 0.0, 1024.0)))
        column = int(round(np.clip(mountain.world_x / WORLD_SIZE * 1024.0, 0.0, 1024.0)))
        register({
            "row": row,
            "column": column,
            "worldX": mountain.world_x,
            "worldY": mountain.world_y,
            "elevation": float(height_grid[row, column]),
            "slope": float(slope[row, column]),
            "rockWeight": float(rock_grid[row, column]),
            "score": 1_000_000.0,
            "sceneAnchor": True,
            "name": mountain.name,
            "snow": mountain.snow,
        })

    # Allocate placements across every occupied massif cell before adding a
    # second or third peak in any one cell.  A global score sort starved lower
    # but geographically extensive ranges even when thousands of valid editor
    # samples existed there.
    coverage_cell_size = 12
    spatial_candidates: dict[tuple[int, int], list[dict[str, object]]] = {}
    for record in candidate_records:
        key = (
            int(record["column"]) // coverage_cell_size,
            int(record["row"]) // coverage_cell_size,
        )
        spatial_candidates.setdefault(key, []).append(record)
    for records in spatial_candidates.values():
        records.sort(key=lambda item: (-float(item["score"]), int(item["row"]), int(item["column"])))
    spatial_keys = sorted(spatial_candidates, key=lambda item: (item[1], item[0]))
    candidate_rank = 0
    while len(accepted) < placement_limit:
        registered_this_round = 0
        candidates_this_round = 0
        for key in spatial_keys:
            records = spatial_candidates[key]
            if candidate_rank >= len(records):
                continue
            candidates_this_round += 1
            if register(records[candidate_rank]):
                registered_this_round += 1
            if len(accepted) >= placement_limit:
                break
        if candidates_this_round == 0:
            break
        candidate_rank += 1

    placement_records: list[dict[str, object]] = []
    bboxes: list[tuple[int, int, int, int]] = []
    rendered_grid_records: list[dict[str, object]] = []
    rendered_footprint = np.zeros_like(mountain_zone, dtype=bool)
    skipped_settlement_overlap = 0
    skipped_water_overlap = 0
    for record in sorted(accepted, key=lambda item: float(item["worldY"]), reverse=True):
        token = hashlib.sha256(
            f"{record['name']}|{float(record['worldX']):.4f}|{float(record['worldY']):.4f}".encode("utf-8")
        ).digest()
        motif = atlas.mountains[int.from_bytes(token[:2], "little") % len(atlas.mountains)]
        elevation = float(record["elevation"])
        local_slope = float(record["slope"])
        rock_weight = float(record["rockWeight"])
        if bool(record["sceneAnchor"]):
            target_width = int(np.clip(round(92.0 + elevation * 1.8), 90, 154))
        else:
            target_width = int(np.clip(round(
                64.0
                + rock_weight * 30.0
                + max(0.0, elevation - elevation_threshold) * 3.4
                + local_slope * 11.0
            ), 62, 132))
        target_width = max(2, round(target_width * mountain_scale))
        stamp = MotifAtlas.scaled(motif, target_width)
        x, y = world_to_pixel(float(record["worldX"]), float(record["worldY"]), bounds)
        left = round(x - stamp.width / 2.0)
        top = round(y - stamp.height)
        bbox = (left, top, left + stamp.width, top + stamp.height)
        if any(rectangles_intersect(bbox, blocked, padding=12) for blocked in blocked_bboxes):
            skipped_settlement_overlap += 1
            continue
        clip_left = max(0, bbox[0])
        clip_top = max(0, bbox[1])
        clip_right = min(land_mask.width, bbox[2])
        clip_bottom = min(land_mask.height, bbox[3])
        if clip_left >= clip_right or clip_top >= clip_bottom:
            skipped_water_overlap += 1
            continue
        land_fraction = float(np.mean(
            np.asarray(land_mask.crop((clip_left, clip_top, clip_right, clip_bottom)), dtype=np.uint8) >= 128
        ))
        if land_fraction < 0.78:
            skipped_water_overlap += 1
            continue
        if not paste_masked(canvas, stamp, left, top, land_mask):
            skipped_water_overlap += 1
            continue
        bboxes.append(bbox)
        rendered_grid_records.append(record)
        row = int(record["row"])
        column = int(record["column"])
        pixels_per_world_x = (bounds[2] - bounds[0]) / (PLAYABLE_MAX_X - PLAYABLE_MIN_X)
        pixels_per_world_y = (bounds[3] - bounds[1]) / (PLAYABLE_MAX_Y - PLAYABLE_MIN_Y)
        grid_samples_per_world = 1024.0 / WORLD_SIZE
        radius_column = max(1.0, stamp.width * 0.5 / pixels_per_world_x * grid_samples_per_world)
        radius_row = max(1.0, stamp.height * 0.5 / pixels_per_world_y * grid_samples_per_world)
        row0, row1 = max(0, math.floor(row - radius_row)), min(rows, math.ceil(row + radius_row) + 1)
        column0, column1 = max(0, math.floor(column - radius_column)), min(columns, math.ceil(column + radius_column) + 1)
        yy, xx = np.ogrid[row0:row1, column0:column1]
        rendered_footprint[row0:row1, column0:column1] |= (
            ((yy - row) / radius_row) ** 2 + ((xx - column) / radius_column) ** 2 <= 1.0
        )
        placement_records.append({
            "worldX": float(record["worldX"]),
            "worldY": float(record["worldY"]),
            "gridRow": int(record["row"]),
            "gridColumn": int(record["column"]),
            "elevation": elevation,
            "slope": local_slope,
            "rockWeight": round(rock_weight, 4),
            "sceneAnchor": bool(record["sceneAnchor"]),
            "widthPixels": target_width,
            "landFraction": round(land_fraction, 4),
        })

    elevated_zone = playable & (height_grid >= elevation_threshold)
    zeonica_massif_zone = mountain_zone & (
        (x_world[None, :] >= 470.0)
        & (x_world[None, :] <= 610.0)
        & (y_world[:, None] >= 275.0)
        & (y_world[:, None] <= 420.0)
    )

    def footprint_coverage(zone: np.ndarray) -> float:
        return 100.0 * float(np.count_nonzero(rendered_footprint & zone)) / max(1, int(np.count_nonzero(zone)))

    zeonica_placements = sum(
        470.0 <= float(record["worldX"]) <= 610.0
        and 275.0 <= float(record["worldY"]) <= 420.0
        for record in rendered_grid_records
    )
    height_derived = sum(not bool(record["sceneAnchor"]) for record in rendered_grid_records)
    result = {
        "sceneAnchorSourcePlacements": len(mountains),
        "sceneAnchorClusteredCandidates": sum(bool(record["sceneAnchor"]) for record in accepted),
        "sceneAnchorRenderedPlacements": sum(bool(record["sceneAnchor"]) for record in rendered_grid_records),
        "snowSceneAnchors": sum(item.snow for item in mountains),
        "heightDerivedCandidateSamples": len(candidate_records),
        "heightDerivedRenderedPlacements": height_derived,
        "acceptedCandidatePlacements": len(accepted),
        "renderedPlacements": len(rendered_grid_records),
        "skippedSettlementOverlap": skipped_settlement_overlap,
        "skippedWaterOverlap": skipped_water_overlap,
        "elevationThreshold": round(elevation_threshold, 6),
        "highElevationThreshold": round(high_elevation_threshold, 6),
        "slopeThreshold": round(slope_threshold, 6),
        "summitSlopeFloor": round(summit_slope_floor, 6),
        "rockMaterialThreshold": rock_threshold,
        "minimumGridDistance": minimum_grid_distance,
        "editorCandidateSampleStep": sample_step,
        "placementLimit": placement_limit,
        "spatialCoverageCellSize": coverage_cell_size,
        "spatialAllocationRounds": candidate_rank,
        "mountainScale": mountain_scale,
        "mountainZoneGridSamples": int(np.count_nonzero(mountain_zone)),
        "rockZoneGridSamples": int(np.count_nonzero(rock_zone)),
        "actualIllustrationFootprintCoveragePercent": round(footprint_coverage(mountain_zone), 3),
        "rockZoneIllustrationFootprintCoveragePercent": round(footprint_coverage(rock_zone), 3),
        "elevatedZoneCoveragePercent": round(footprint_coverage(elevated_zone), 3),
        "zeonicaAmitatysMassif": {
            "worldBounds": {"minX": 470.0, "maxX": 610.0, "minY": 275.0, "maxY": 420.0},
            "eligibleGridSamples": int(np.count_nonzero(zeonica_massif_zone)),
            "renderedPlacements": zeonica_placements,
            "actualIllustrationFootprintCoveragePercent": round(footprint_coverage(zeonica_massif_zone), 3),
        },
        "heightGridResolution": {"width": columns, "height": rows},
        "imageGenMountainMotifs": len(atlas.mountains),
        "imageGenTerrainAtlas": str(atlas.source_path),
        "imageGenTerrainAtlasSha256": atlas.source_sha256,
        "method": "39 official scene.xscene anchors plus spatially balanced exact-cell editor rock-material, height, and slope samples fill irregular whole mountain massifs instead of isolated global maxima or a visible sampling grid; measured motif footprints, bridge and settlement clearance, and official land clipping are audited; ImageGen supplies only upright engraved appearance, never mountain geography",
        "_placements": placement_records,
    }
    return result, bboxes


def draw_exact_bridges(
    canvas: Image.Image | None,
    placements: Sequence[CartographicPlacement],
    bridge_sites: Sequence[dict[str, object]],
    bounds: Sequence[int],
    water_mask: Image.Image,
    cartographic_land_mask: Image.Image,
    atlas: BridgeMotifAtlas,
) -> dict[str, object]:
    # The deck must remain visibly seated on both banks after the 16K master is
    # enlarged to the 44x Gauntlet map extent.  Eighteen master pixels passed a
    # point-sample test but could still read as a short/floating bridge.
    # The clean single-body motif retains a few transparent crop pixels at its
    # ends.  Give each bridge a visibly generous authored landing so a complete
    # timber deck—not just its image rectangle—sits well onto both banks.
    landing_depth = 64
    minimum_visible_landing_depth = 56
    bridges = [item for item in placements if item.category == "bridge"]
    bridge_thickness = 48
    water = water_mask.load()
    land = cartographic_land_mask.load()
    width, height = water_mask.size
    rendered: list[dict[str, object]] = []
    exclusion_bboxes: list[tuple[int, int, int, int]] = []

    def sample_mask(mask: object, x: float, y: float, ux: float, uy: float, step: float) -> bool:
        px = int(round(x + ux * step))
        py = int(round(y + uy * step))
        return 0 <= px < width and 0 <= py < height and mask[px, py] >= 128

    def crossing_at(x: float, y: float, angle: float) -> tuple[int, int, int, int] | None:
        ux, uy = math.cos(angle), -math.sin(angle)
        samples: list[bool] = []
        for step in range(-240, 241):
            samples.append(sample_mask(water, x, y, ux, uy, step))
        runs: list[tuple[int, int]] = []
        start: int | None = None
        for index, active in enumerate(samples):
            step = index - 240
            if active and start is None:
                start = step
            elif not active and start is not None:
                runs.append((start, step - 1))
                start = None
        if start is not None:
            runs.append((start, 240))
        if not runs:
            return None
        containing = [run for run in runs if run[0] <= 0 <= run[1]]
        if containing:
            water_start, water_end = containing[0]
        else:
            water_start, water_end = min(runs, key=lambda run: min(abs(run[0]), abs(run[1])))
            if min(abs(water_start), abs(water_end)) > 24:
                return None

        # The editor scene point may sit on an approach rather than at the
        # channel midpoint. Find the first proven land pixel beyond each side
        # of the exact water run so the illustrated deck can be recentered on
        # the water and land on opposite banks instead of floating beside it.
        bank_start = next(
            (
                step
                for step in range(water_start - 1, water_start - 145, -1)
                if sample_mask(land, x, y, ux, uy, step)
            ),
            None,
        )
        bank_end = next(
            (
                step
                for step in range(water_end + 1, water_end + 145)
                if sample_mask(land, x, y, ux, uy, step)
            ),
            None,
        )
        if bank_start is None or bank_end is None:
            return None
        if not all(
            sample_mask(land, x, y, ux, uy, bank_start - depth)
            for depth in range(landing_depth + 1)
        ):
            return None
        if not all(
            sample_mask(land, x, y, ux, uy, bank_end + depth)
            for depth in range(landing_depth + 1)
        ):
            return None
        return water_start, water_end, bank_start, bank_end

    for site in bridge_sites:
        world_x = float(site["worldX"])
        world_y = float(site["worldY"])
        nearest = min(bridges, key=lambda item: (item.world_x - world_x) ** 2 + (item.world_y - world_y) ** 2)
        preferred_angle = nearest.rotation_z
        x, y = world_to_pixel(world_x, world_y, bounds)
        candidates: list[tuple[int, float, float, int, int, int, int]] = []
        for degrees in range(0, 180, 3):
            candidate_angle = math.radians(degrees)
            crossing = crossing_at(x, y, candidate_angle)
            if crossing is None:
                continue
            water_start, water_end, bank_start, bank_end = crossing
            span = water_end - water_start + 1
            center_step = (bank_start + bank_end) * 0.5
            candidates.append(
                (span, abs(center_step), candidate_angle, water_start, water_end, bank_start, bank_end)
            )
        if candidates:
            span, _, angle, water_start, water_end, bank_start, bank_end = min(
                candidates,
                key=lambda item: (item[0], item[1]),
            )
            ux, uy = math.cos(angle), -math.sin(angle)
            deck_start = bank_start - landing_depth
            deck_end = bank_end + landing_depth
            center_step = (deck_start + deck_end) * 0.5
            center_x = x + ux * center_step
            center_y = y + uy * center_step
            target_length = int(round(float(np.clip(deck_end - deck_start + 1, 96, 520))))
            river_aligned = True
        else:
            span, angle, water_start, water_end = 0, preferred_angle, 0, 0
            bank_start = bank_end = 0
            center_x, center_y = x, y
            center_step = 0.0
            target_length = 72
            river_aligned = False
        # One authored bridge type is used globally; mixing adjacent atlas cells
        # previously created the appearance of two incompatible pieces.
        motif_index = 0
        # River width changes bridge length, never its cartographic road width.
        # Proportional scaling made the widest crossings look like enormous
        # game platforms instead of a consistent top-down bridge symbol.
        stamp = with_scaled_alpha(
            atlas.bridges[motif_index].resize(
                (target_length, bridge_thickness),
                Image.Resampling.LANCZOS,
            ),
            0.72,
        )
        alpha_bbox = stamp.getchannel("A").getbbox()
        if alpha_bbox is None:
            raise ValueError(f"Bridge motif {motif_index} has no visible ink")
        visible_start_inset = alpha_bbox[0]
        visible_end_inset = stamp.width - alpha_bbox[2]
        visible_deck_start = deck_start + visible_start_inset if river_aligned else 0
        visible_deck_end = deck_end - visible_end_inset if river_aligned else 0
        visible_start_landing = bank_start - visible_deck_start if river_aligned else 0
        visible_end_landing = visible_deck_end - bank_end if river_aligned else 0
        endpoint_start_land = (
            sample_mask(land, x, y, math.cos(angle), -math.sin(angle), visible_deck_start)
            if river_aligned else False
        )
        endpoint_end_land = (
            sample_mask(land, x, y, math.cos(angle), -math.sin(angle), visible_deck_end)
            if river_aligned else False
        )
        opposite_banks_proven = (
            river_aligned
            and endpoint_start_land
            and endpoint_end_land
            and visible_start_landing >= minimum_visible_landing_depth
            and visible_end_landing >= minimum_visible_landing_depth
            and visible_deck_start < bank_start < water_start <= water_end < bank_end < visible_deck_end
        )
        # The atlas contains only orthographic deck surfaces and tiny overhead
        # abutments.  Rotation aligns that flat footprint to the shortest exact
        # river-water crossing; it never introduces a side elevation or arch.
        rotated = stamp.rotate(math.degrees(angle), resample=Image.Resampling.BICUBIC, expand=True)
        left = round(center_x - rotated.width / 2.0)
        top = round(center_y - rotated.height / 2.0)
        if canvas is not None:
            canvas.alpha_composite(rotated, (left, top))
        exclusion_bboxes.append((left, top, left + rotated.width, top + rotated.height))
        rendered.append({
            "siteId": site["siteId"],
            "world": {"x": world_x, "y": world_y},
            "riverAligned": river_aligned,
            "measuredWaterSpanPixels": span,
            "illustratedLengthPixels": target_length,
            "illustratedThicknessPixels": bridge_thickness,
            "motifIndex": motif_index,
            "strictTopDown": True,
            "crossingAngleRadians": round(angle, 6),
            "sceneAnchorPixel": {"x": round(x, 3), "y": round(y, 3)},
            "deckCenterPixel": {"x": round(center_x, 3), "y": round(center_y, 3)},
            "sceneAnchorToDeckCenterPixels": round(abs(center_step), 3),
            "waterRunSteps": {"start": water_start, "end": water_end},
            "bankLandingSteps": {"start": bank_start, "end": bank_end},
            "minimumContinuousLandingDepthPixels": landing_depth,
            "minimumVisibleLandingDepthPixels": minimum_visible_landing_depth,
            "visibleLandingDepthPixels": {
                "start": visible_start_landing,
                "end": visible_end_landing,
            },
            "visibleDeckRunSteps": {"start": visible_deck_start, "end": visible_deck_end},
            "endpointStartOnLand": endpoint_start_land,
            "endpointEndOnLand": endpoint_end_land,
            "oppositeBankLandingProven": opposite_banks_proven,
        })
    failed_bank_landings = [
        item["siteId"]
        for item in rendered
        if item["riverAligned"] and not item["oppositeBankLandingProven"]
    ]
    if failed_bank_landings:
        raise ValueError(
            "Bridge footprint did not land on opposite banks: " + ", ".join(failed_bank_landings)
        )
    return {
        "rawScenePlacements": len(bridges),
        "physicalSites": len(bridge_sites),
        "riverAlignedSites": sum(item["riverAligned"] for item in rendered),
        "oppositeBankLandingSites": sum(item["oppositeBankLandingProven"] for item in rendered),
        "failedBankLandings": failed_bank_landings,
        "imageGenBridgeMotifs": len(atlas.bridges),
        "imageGenBridgeAtlas": str(atlas.source_path),
        "imageGenBridgeAtlasSha256": atlas.source_sha256,
        "strictTopDown": True,
        "uniformBridgeType": True,
        "continuousLongitudinalInk": atlas.continuous_longitudinal_ink,
        "placements": rendered,
        "_exclusionBboxes": exclusion_bboxes,
        "minimumContinuousLandingDepthPixels": landing_depth,
        "minimumVisibleLandingDepthPixels": minimum_visible_landing_depth,
        "fixedIllustratedThicknessPixels": bridge_thickness,
        "method": "one uniform fixed-thickness strict-orthographic overhead timber deck, extracted as one whitespace-bounded drawing rather than an equal-width mixed atlas cell, is recentered from each clustered Main_map scene anchor onto the shortest nearby unified-water run; the continuous visible footprint must cross the complete measured water run and extend at least 56 master pixels onto continuous opposite land banks",
    }


def draw_town(draw: ImageDraw.ImageDraw, x: float, y: float, scale: float) -> None:
    pen = max(2, round(scale * 0.055))
    color = INK
    radius = scale * 0.46
    draw.ellipse((x - radius, y - radius * 0.55, x + radius, y + radius * 0.55), outline=color, width=pen)
    draw.rectangle((x - scale * 0.18, y - scale * 0.30, x + scale * 0.18, y + scale * 0.22), outline=color, width=pen)
    draw.polygon(((x - scale * 0.23, y - scale * 0.30), (x, y - scale * 0.55), (x + scale * 0.23, y - scale * 0.30)), outline=color)
    for dx in (-0.34, 0.34):
        draw.rectangle((x + dx * scale - scale * 0.09, y - scale * 0.18, x + dx * scale + scale * 0.09, y + scale * 0.20), outline=color, width=pen)


def draw_castle(draw: ImageDraw.ImageDraw, x: float, y: float, scale: float) -> None:
    pen = max(2, round(scale * 0.06))
    color = INK
    draw.rectangle((x - scale * 0.36, y - scale * 0.25, x + scale * 0.36, y + scale * 0.30), outline=color, width=pen)
    for dx in (-0.29, 0.0, 0.29):
        draw.rectangle((x + dx * scale - scale * 0.10, y - scale * 0.43, x + dx * scale + scale * 0.10, y + scale * 0.06), outline=color, width=pen)
    draw.arc((x - scale * 0.11, y + scale * 0.07, x + scale * 0.11, y + scale * 0.43), 180, 360, fill=color, width=pen)


def draw_village(draw: ImageDraw.ImageDraw, x: float, y: float, scale: float) -> None:
    pen = max(1, round(scale * 0.065))
    color = INK
    for dx, dy, factor in ((-0.22, 0.03, 0.62), (0.19, 0.13, 0.50), (0.02, -0.19, 0.48)):
        width = scale * factor
        left = x + dx * scale - width * 0.5
        top = y + dy * scale - width * 0.32
        draw.rectangle((left, top, left + width, top + width * 0.58), outline=color, width=pen)
        draw.line((left - width * 0.08, top, left + width * 0.5, top - width * 0.40, left + width * 1.08, top), fill=color, width=pen)


def plan_settlement_visuals(
    settlements: Iterable[Settlement],
    bounds: Sequence[int],
    atlas: MotifAtlas,
    cartographic_land_mask: Image.Image,
    blocked_bboxes: Sequence[tuple[int, int, int, int]] = (),
) -> list[dict[str, object]]:
    """Keep large settlement drawings on land without changing their anchors.

    Bannerlord's settlement coordinates are authoritative point anchors, not
    illustration centers. A 200-pixel city centered blindly on a riverbank can
    therefore cover navigable water even though its source point is valid land.
    For each settlement, search the nearest land-safe visual center at the
    requested scale; only genuinely constrained islands may use a smaller
    member of the same town/castle/village family. The immutable world anchor,
    visual offset, footprint coverage, and chosen scale are all audited.
    """

    motifs = {"town": atlas.town, "castle": atlas.castle, "village": atlas.village}
    order = sorted(
        settlements,
        key=lambda item: ({"town": 0, "castle": 1, "village": 2}[item.kind], item.name),
    )
    offset_cache: dict[int, list[tuple[int, int, int]]] = {}
    stamp_cache: dict[tuple[str, int], dict[str, object]] = {}
    plans: list[dict[str, object]] = []
    land_width, land_height = cartographic_land_mask.size
    shoreline_clearance = 6

    def offsets(maximum: int) -> list[tuple[int, int, int]]:
        if maximum not in offset_cache:
            step = 4
            values = [
                (dx * dx + dy * dy, dx, dy)
                for dy in range(-maximum, maximum + 1, step)
                for dx in range(-maximum, maximum + 1, step)
                if dx * dx + dy * dy <= maximum * maximum
            ]
            values.append((0, 0, 0))
            offset_cache[maximum] = sorted(set(values), key=lambda item: (item[0], item[2], item[1]))
        return offset_cache[maximum]

    def bit_rows(mask: np.ndarray) -> list[tuple[int, int, int]]:
        rows: list[tuple[int, int, int]] = []
        for row_index, row in enumerate(mask):
            count = int(np.count_nonzero(row))
            if count == 0:
                continue
            packed = np.packbits(row, bitorder="little").tobytes()
            rows.append((row_index, int.from_bytes(packed, "little"), count))
        return rows

    def stamp_data(kind: str, target_scale: int) -> dict[str, object]:
        key = (kind, target_scale)
        if key in stamp_cache:
            return stamp_cache[key]
        stamp = MotifAtlas.scaled(motifs[kind], target_scale)
        visible = np.asarray(stamp.getchannel("A"), dtype=np.uint8) >= 16
        padded = np.pad(visible, shoreline_clearance, mode="constant", constant_values=False)
        clearance = np.asarray(
            Image.fromarray((padded * 255).astype(np.uint8), mode="L").filter(
                ImageFilter.MaxFilter(shoreline_clearance * 2 + 1)
            ),
            dtype=np.uint8,
        ) >= 128
        data: dict[str, object] = {
            "stamp": stamp,
            "visibleRows": bit_rows(visible),
            "visibleWidth": visible.shape[1],
            "visibleHeight": visible.shape[0],
            "visiblePixels": int(np.count_nonzero(visible)),
            "clearanceRows": bit_rows(clearance),
            "clearanceWidth": clearance.shape[1],
            "clearanceHeight": clearance.shape[0],
            "clearancePixels": int(np.count_nonzero(clearance)),
        }
        stamp_cache[key] = data
        return data

    for settlement in order:
        anchor_x, anchor_y = world_to_pixel(settlement.world_x, settlement.world_y, bounds)
        base_scale = SETTLEMENT_SCALES[settlement.kind]
        maximum_offset = round(base_scale * 1.15)
        base_stamp = MotifAtlas.scaled(motifs[settlement.kind], base_scale)
        patch_padding = maximum_offset + max(base_stamp.width, base_stamp.height) + shoreline_clearance + 4
        patch_left = max(0, int(math.floor(anchor_x - patch_padding)))
        patch_top = max(0, int(math.floor(anchor_y - patch_padding)))
        patch_right = min(land_width, int(math.ceil(anchor_x + patch_padding + 1)))
        patch_bottom = min(land_height, int(math.ceil(anchor_y + patch_padding + 1)))
        patch = np.asarray(
            cartographic_land_mask.crop((patch_left, patch_top, patch_right, patch_bottom)),
            dtype=np.uint8,
        ) >= 128
        land_row_bits = [
            int.from_bytes(np.packbits(row, bitorder="little").tobytes(), "little")
            for row in patch
        ]

        def rows_fit(
            rows: Sequence[tuple[int, int, int]],
            mask_width: int,
            mask_height: int,
            center_x: float,
            center_y: float,
            stamp: Image.Image,
            padding: int,
        ) -> bool:
            left = round(center_x - stamp.width / 2.0) - padding - patch_left
            top = round(center_y - stamp.height / 2.0) - padding - patch_top
            if left < 0 or top < 0 or left + mask_width > patch.shape[1] or top + mask_height > patch.shape[0]:
                return False
            for row_index, mask_bits, _ in rows:
                shifted_mask = mask_bits << left
                if shifted_mask & ~land_row_bits[top + row_index]:
                    return False
            return True

        safe_candidates: list[tuple[float, int, float, int, int, dict[str, object]]] = []
        for scale_factor in (1.0, 0.90, 0.80, 0.70, 0.60):
            target_scale = max(2, round(base_scale * scale_factor))
            data = stamp_data(settlement.kind, target_scale)
            stamp = data["stamp"]
            for squared_distance, dx, dy in offsets(maximum_offset):
                center_x = anchor_x + dx
                center_y = anchor_y + dy
                if not rows_fit(
                    data["clearanceRows"],
                    int(data["clearanceWidth"]),
                    int(data["clearanceHeight"]),
                    center_x,
                    center_y,
                    stamp,
                    shoreline_clearance,
                ):
                    continue
                displacement = math.sqrt(squared_distance)
                icon_left = round(center_x - stamp.width / 2.0)
                icon_top = round(center_y - stamp.height / 2.0)
                icon_rect = (icon_left, icon_top, icon_left + stamp.width, icon_top + stamp.height)
                if any(rectangle_intersects(icon_rect, blocked, padding=10) for blocked in blocked_bboxes):
                    continue
                anchor_inside = (
                    icon_left <= anchor_x < icon_left + stamp.width
                    and icon_top <= anchor_y < icon_top + stamp.height
                )
                anchor_tolerance = max(12, round(target_scale * 0.25))
                anchor_near = (
                    icon_left - anchor_tolerance <= anchor_x < icon_left + stamp.width + anchor_tolerance
                    and icon_top - anchor_tolerance <= anchor_y < icon_top + stamp.height + anchor_tolerance
                )
                # A smaller nearby engraving is more geographically honest than
                # a full-size town detached from its source point.  Size still
                # carries a real cost so unconstrained inland art stays large.
                score = (
                    displacement
                    + (1.0 - scale_factor) * base_scale * 1.20
                    + (0.0 if anchor_near else base_scale * 4.0)
                )
                safe_candidates.append((score, squared_distance, scale_factor, dx, dy, data))
                break

        if not safe_candidates:
            raise ValueError(f"No land-safe settlement illustration footprint found for {settlement.name}")
        _, squared_distance, scale_factor, dx, dy, data = min(
            safe_candidates,
            key=lambda item: (item[0], -item[2], item[1]),
        )
        candidate_options: list[dict[str, object]] = []
        for score, candidate_distance, candidate_factor, candidate_dx, candidate_dy, candidate_data in safe_candidates:
            candidate_stamp = candidate_data["stamp"]
            candidate_left = round(anchor_x + candidate_dx - candidate_stamp.width / 2.0)
            candidate_top = round(anchor_y + candidate_dy - candidate_stamp.height / 2.0)
            candidate_options.append(
                {
                    "scaleFactor": float(candidate_factor),
                    "renderScale": int(candidate_stamp.width),
                    "offsetPixels": round(math.sqrt(candidate_distance), 3),
                    "offsetX": candidate_dx,
                    "offsetY": candidate_dy,
                    "anchorInsideIllustrationBounds": bool(
                        candidate_left <= anchor_x < candidate_left + candidate_stamp.width
                        and candidate_top <= anchor_y < candidate_top + candidate_stamp.height
                    ),
                    "anchorWithinQuarterScale": bool(
                        candidate_left - max(12, round(candidate_stamp.width * 0.25))
                        <= anchor_x
                        < candidate_left + candidate_stamp.width + max(12, round(candidate_stamp.width * 0.25))
                        and candidate_top - max(12, round(candidate_stamp.width * 0.25))
                        <= anchor_y
                        < candidate_top + candidate_stamp.height + max(12, round(candidate_stamp.width * 0.25))
                    ),
                    "selectionScore": round(score, 3),
                }
            )
        stamp = data["stamp"]
        visual_x = anchor_x + dx
        visual_y = anchor_y + dy
        icon_left = round(visual_x - stamp.width / 2.0)
        icon_top = round(visual_y - stamp.height / 2.0)
        icon_rect = (icon_left, icon_top, icon_left + stamp.width, icon_top + stamp.height)
        anchor_inside = (
            icon_rect[0] <= anchor_x < icon_rect[2]
            and icon_rect[1] <= anchor_y < icon_rect[3]
        )
        anchor_tolerance = max(12, round(stamp.width * 0.25))
        anchor_near = (
            icon_rect[0] - anchor_tolerance <= anchor_x < icon_rect[2] + anchor_tolerance
            and icon_rect[1] - anchor_tolerance <= anchor_y < icon_rect[3] + anchor_tolerance
        )
        visible_pixels = int(data["visiblePixels"])
        plans.append(
            {
                "settlement": settlement,
                "stamp": stamp,
                "anchorX": anchor_x,
                "anchorY": anchor_y,
                "visualX": visual_x,
                "visualY": visual_y,
                "iconRect": icon_rect,
                "baseScale": base_scale,
                "renderScale": stamp.width,
                "scaleFactor": scale_factor,
                "offsetPixels": math.sqrt(squared_distance),
                "footprintLandPixels": visible_pixels,
                "footprintPixels": visible_pixels,
                "footprintWaterPixels": 0,
                "clearanceLandFraction": 1.0,
                "anchorInsideIllustrationBounds": anchor_inside,
                "anchorWithinQuarterScale": anchor_near,
                "candidateOptions": candidate_options,
            }
        )
    return plans


def settlement_exclusion_bboxes(
    settlement_plans: Iterable[dict[str, object]],
) -> list[tuple[int, int, int, int]]:
    return [tuple(plan["iconRect"]) for plan in settlement_plans]


def rectangle_intersects(a: tuple[int, int, int, int], b: tuple[int, int, int, int], padding: int = 5) -> bool:
    return not (a[2] + padding < b[0] or b[2] + padding < a[0] or a[3] + padding < b[1] or b[3] + padding < a[1])


def draw_settlements(
    canvas: Image.Image,
    settlement_plans: Iterable[dict[str, object]],
) -> dict[str, object]:
    draw = ImageDraw.Draw(canvas, "RGBA")
    fonts = {
        "town": find_font(28, bold=True),
        "castle": find_font(23, bold=False),
        "village": find_font(18, bold=False),
    }
    occupied: list[tuple[int, int, int, int]] = []
    placements: list[dict[str, object]] = []
    water_failures: list[str] = []
    footprint_water_failures: list[str] = []

    for plan in settlement_plans:
        settlement = plan["settlement"]
        stamp = plan["stamp"]
        anchor_x = float(plan["anchorX"])
        anchor_y = float(plan["anchorY"])
        x = float(plan["visualX"])
        y = float(plan["visualY"])
        icon_rect = tuple(plan["iconRect"])
        canvas.alpha_composite(stamp, (icon_rect[0], icon_rect[1]))

        font = fonts[settlement.kind]
        text_box = draw.textbbox((0, 0), settlement.name, font=font, stroke_width=1)
        text_width = text_box[2] - text_box[0]
        text_height = text_box[3] - text_box[1]
        # Names remain under their settlement drawing.  Dense clusters may be
        # staggered by one or two text rows, but never displaced to a side where
        # the association becomes ambiguous.
        label_top = icon_rect[3] + 3
        candidates = tuple(
            (x - text_width / 2 + horizontal, label_top + vertical)
            for vertical, horizontal in (
                (0, 0),
                (text_height + 2, 0),
                (0, -text_width * 0.18),
                (0, text_width * 0.18),
                (2 * (text_height + 2), 0),
            )
        )
        chosen = candidates[0]
        collided = True
        for candidate in candidates:
            rect = (round(candidate[0]), round(candidate[1]), round(candidate[0] + text_width), round(candidate[1] + text_height))
            if not any(rectangle_intersects(rect, other) for other in occupied):
                chosen = candidate
                collided = False
                break
        rect = (round(chosen[0]), round(chosen[1]), round(chosen[0] + text_width), round(chosen[1] + text_height))
        occupied.append(rect)
        draw.text(
            chosen,
            settlement.name,
            font=font,
            fill=(46, 30, 18, 242),
            stroke_width=1,
            stroke_fill=(219, 190, 132, 142),
        )

        terrain_height = height_at_world(draw_settlements.height_grid, settlement.world_x, settlement.world_y)
        if terrain_height <= SEA_LEVEL:
            water_failures.append(settlement.settlement_id)
        if int(plan["footprintWaterPixels"]) != 0:
            footprint_water_failures.append(settlement.settlement_id)
        placements.append(
            {
                "id": settlement.settlement_id,
                "name": settlement.name,
                "type": settlement.kind,
                "world": {"x": settlement.world_x, "y": settlement.world_y},
                "pixel": {"x": round(anchor_x, 3), "y": round(anchor_y, 3)},
                "visualCenterPixel": {"x": round(x, 3), "y": round(y, 3)},
                "visualOffsetPixels": round(float(plan["offsetPixels"]), 3),
                "baseScalePixels": int(plan["baseScale"]),
                "renderScalePixels": int(plan["renderScale"]),
                "scaleFactor": round(float(plan["scaleFactor"]), 3),
                "footprintLandFraction": round(
                    int(plan["footprintLandPixels"]) / max(1, int(plan["footprintPixels"])),
                    6,
                ),
                "footprintWaterPixels": int(plan["footprintWaterPixels"]),
                "shorelineClearanceLandFraction": round(float(plan["clearanceLandFraction"]), 6),
                "anchorInsideIllustrationBounds": bool(plan["anchorInsideIllustrationBounds"]),
                "anchorWithinQuarterScale": bool(plan["anchorWithinQuarterScale"]),
                "candidateOptions": plan["candidateOptions"],
                "terrainHeight": round(terrain_height, 6),
                "labelCollisionFallback": collided,
            }
        )

    if water_failures:
        raise ValueError(f"Editor settlement coordinates sampled as water: {', '.join(water_failures)}")
    if footprint_water_failures:
        raise ValueError(
            "Settlement artwork footprint overlaps water: " + ", ".join(footprint_water_failures)
        )
    return {
        "placements": placements,
        "waterFailures": water_failures,
        "footprintWaterFailures": footprint_water_failures,
        "landSafeFootprintCount": sum(item["footprintWaterPixels"] == 0 for item in placements),
        "inlandAdjustedCount": sum(item["visualOffsetPixels"] > 0.01 for item in placements),
        "adaptiveScaleCount": sum(item["scaleFactor"] < 0.999 for item in placements),
        "anchorInsideIllustrationCount": sum(item["anchorInsideIllustrationBounds"] for item in placements),
        "anchorWithinQuarterScaleCount": sum(item["anchorWithinQuarterScale"] for item in placements),
        "maximumVisualOffsetPixels": round(max((item["visualOffsetPixels"] for item in placements), default=0), 3),
        "method": "authoritative world coordinates remain immutable anchors; actual visible engraving ink plus a six-pixel shoreline buffer must remain on land and clear every planned bridge corridor; illustrations preserve full scale when the source anchor stays within one-quarter icon width and adapt scale before becoming geographically detached",
    }


def save_master_and_tiles(canvas: Image.Image, output_directory: Path) -> tuple[Path, Path, list[Path]]:
    output_directory.mkdir(parents=True, exist_ok=True)
    master = output_directory / "reign_war_council_calradia_master.png"
    # The 16K master is build evidence, not a runtime asset.  Lossless level-1
    # compression avoids spending many minutes searching a marginally smaller
    # deflate stream; the reconstruction audit, not PNG optimization, proves it.
    canvas.convert("RGB").save(master, optimize=False, compress_level=1)
    # The Gauntlet runtime draws the high-definition tile set. Keep the legacy
    # map key as a compact availability/fallback overview so the tracked source
    # asset remains below the repository's ordinary file-size boundary.
    overview = output_directory / "reign_war_council_calradia.png"
    authored_overview = canvas.convert("RGB").resize(
        (RUNTIME_OVERVIEW_SIZE, RUNTIME_OVERVIEW_SIZE), Image.Resampling.LANCZOS
    )
    # Bannerlord's direct Texture.CreateFromMemory path consumes PNG color
    # bytes as BGR.  Keep the authored master in ordinary RGB, but encode the
    # runtime overview and tiles with red/blue exchanged so the native view is
    # the same sepia artwork without a costly managed 16-tile decode/re-encode.
    red, green, blue = authored_overview.split()
    Image.merge("RGB", (blue, green, red)).save(overview, optimize=False, compress_level=4)
    tiles: list[Path] = []
    for row in range(TILE_COUNT):
        for column in range(TILE_COUNT):
            authored_tile = canvas.crop((column * TILE_SIZE, row * TILE_SIZE, (column + 1) * TILE_SIZE, (row + 1) * TILE_SIZE)).convert("RGB")
            red, green, blue = authored_tile.split()
            tile = Image.merge("RGB", (blue, green, red))
            path = output_directory / f"reign_war_council_tile_{row}_{column}.png"
            # The client passes these audited opaque PNG bytes directly to the
            # engine. Avoiding the generic decode/re-encode normalization keeps
            # the lossless atlas while removing the original first-open stall.
            tile.save(path, optimize=False, compress_level=1)
            tiles.append(path)
    return master, overview, tiles


def verify_runtime_tile_quality(master_path: Path, tiles: Sequence[Path]) -> dict[str, object]:
    """Prove engine-ready PNGs reconstruct the authored RGB master exactly."""
    mismatches: list[str] = []
    quality: list[dict[str, object]] = []
    with Image.open(master_path) as master_source:
        master = master_source.convert("RGB")
        for index, path in enumerate(tiles):
            row, column = divmod(index, TILE_COUNT)
            expected = master.crop(
                (
                    column * TILE_SIZE,
                    row * TILE_SIZE,
                    (column + 1) * TILE_SIZE,
                    (row + 1) * TILE_SIZE,
                )
            )
            with Image.open(path) as tile_source:
                tile = tile_source.convert("RGB")
                if tile.size != (TILE_SIZE, TILE_SIZE) or tile_source.format != "PNG":
                    mismatches.append(path.name)
                    continue
                encoded_blue, encoded_green, encoded_red = tile.split()
                normalized = Image.merge("RGB", (encoded_red, encoded_green, encoded_blue))
                difference = ImageChops.difference(expected, normalized)
                exact = difference.getbbox() is None
                quality.append({"tile": path.name, "pixelExact": exact, "bytes": path.stat().st_size})
                if not exact:
                    mismatches.append(path.name)
    if mismatches:
        raise ValueError("Runtime PNG tiles fail layout or lossless reconstruction: " + ", ".join(mismatches))
    return {
        "checkedTiles": len(tiles),
        "mismatchCount": 0,
        "mismatches": mismatches,
        "format": "PNG RGB lossless; engine-ready BGR channel encoding",
        "runtimeEncoding": "red/blue exchanged for Bannerlord direct Texture.CreateFromMemory",
        "authoredColorSpace": "ordinary RGB sepia",
        "pixelExactTileCount": sum(1 for item in quality if item["pixelExact"]),
        "encodedBytes": sum(path.stat().st_size for path in tiles),
        "uncompressedRgbBytes": TILE_COUNT * TILE_COUNT * TILE_SIZE * TILE_SIZE * 3,
        "quality": quality,
        "method": "PNG header/dimension check, inverse runtime red/blue exchange, and pixel-exact comparison against each corresponding authored-master crop",
    }


def verify_runtime_overview_quality(master_path: Path, overview_path: Path) -> dict[str, object]:
    """Prove the engine-ready fallback overview preserves the authored colors."""
    with Image.open(master_path) as master_source:
        expected = master_source.convert("RGB").resize(
            (RUNTIME_OVERVIEW_SIZE, RUNTIME_OVERVIEW_SIZE), Image.Resampling.LANCZOS
        )
    with Image.open(overview_path) as overview_source:
        encoded = overview_source.convert("RGB")
        if encoded.size != (RUNTIME_OVERVIEW_SIZE, RUNTIME_OVERVIEW_SIZE) or overview_source.format != "PNG":
            raise ValueError("Runtime overview fails PNG layout validation")
    encoded_blue, encoded_green, encoded_red = encoded.split()
    normalized = Image.merge("RGB", (encoded_red, encoded_green, encoded_blue))
    exact = ImageChops.difference(expected, normalized).getbbox() is None
    if not exact:
        raise ValueError("Runtime overview fails inverse-channel reconstruction")
    return {
        "pixelExact": True,
        "runtimeEncoding": "red/blue exchanged for Bannerlord direct Texture.CreateFromMemory",
        "method": "inverse runtime red/blue exchange and pixel-exact comparison against the authored-master LANCZOS overview",
    }


def save_authored_visual_evidence(
    canvas: Image.Image,
    evidence_directory: Path,
    bounds: Sequence[int],
) -> dict[str, object]:
    """Save ordinary-RGB review images before runtime channel encoding."""

    authored = canvas.convert("RGB")
    overview_path = evidence_directory / "authored-map-overview.png"
    authored.resize((2048, 2048), Image.Resampling.LANCZOS).save(overview_path, compress_level=4)
    left, top, right, bottom = bounds
    map_width = right - left
    map_height = bottom - top
    crops: list[dict[str, object]] = []
    regions = (
        ("north", left, top, right, top + map_height * 0.43),
        ("middle", left, top + map_height * 0.285, right, top + map_height * 0.715),
        ("south", left, top + map_height * 0.57, right, bottom),
    )
    for name, region_left, region_top, region_right, region_bottom in regions:
        crop = authored.crop((round(region_left), round(region_top), round(region_right), round(region_bottom)))
        target_height = round(2048 * crop.height / max(1, crop.width))
        crop = crop.resize((2048, target_height), Image.Resampling.LANCZOS)
        path = evidence_directory / f"authored-map-{name}-detail.png"
        crop.save(path, compress_level=4)
        crops.append({"name": name, "path": str(path), "sha256": sha256(path)})
    return {
        "overview": {"path": str(overview_path), "sha256": sha256(overview_path)},
        "details": crops,
        "colorSpace": "ordinary RGB sepia before Bannerlord runtime channel encoding",
    }


def render(args: argparse.Namespace) -> dict[str, object]:
    if not 0 <= args.forest_ink_separation <= 12:
        raise ValueError("Forest ink separation must be from 0 through 12 pixels")
    if not 3.0 <= args.mountain_grid_distance <= 24.0:
        raise ValueError("Mountain grid distance must be from 3 through 24 samples")
    if not 1 <= args.mountain_placement_limit <= 15000:
        raise ValueError("Mountain placement limit must be from 1 through 15000")
    if not 0.5 <= args.mountain_scale <= 2.0:
        raise ValueError("Mountain scale must be from 0.5 through 2.0")
    if not 0 <= args.shore_beach_width <= 40:
        raise ValueError("Shore beach width must be from 0 through 40 pixels")
    if not 24 <= args.ground_motif_spacing <= 160:
        raise ValueError("Ground motif spacing must be from 24 through 160 pixels")
    if not 2 <= args.ground_motif_min_width <= args.ground_motif_max_width <= 48:
        raise ValueError("Ground motif widths must be ordered from 2 through 48 pixels")
    if not 0.0 <= args.plain_dot_bias <= 1.0:
        raise ValueError("Plain dot bias must be from 0 through 1")
    if not 10 <= args.water_hatch_spacing_y <= 96:
        raise ValueError("Water hatch vertical spacing must be from 10 through 96 pixels")
    if not 40 <= args.water_hatch_spacing_x <= 320:
        raise ValueError("Water hatch horizontal spacing must be from 40 through 320 pixels")
    if not 8 <= args.water_hatch_min_width <= args.water_hatch_max_width <= 192:
        raise ValueError("Water hatch widths must be ordered from 8 through 192 pixels")
    if not 0.1 <= args.water_hatch_opacity <= 0.8:
        raise ValueError("Water hatch opacity must be from 0.1 through 0.8")

    height_path = Path(args.height_grid).resolve()
    height_texture_path = Path(args.height_texture).resolve()
    river_mask_path = Path(args.river_mask).resolve()
    settlement_path = Path(args.settlements).resolve()
    geography_manifest_path = Path(args.geography_manifest).resolve()
    parchment_path = Path(args.parchment).resolve()
    atlas_path = Path(args.motif_atlas).resolve()
    terrain_atlas_path = Path(args.terrain_atlas).resolve()
    detail_atlas_path = Path(args.detail_atlas).resolve()
    bridge_atlas_path = Path(args.bridge_atlas).resolve()
    output_directory = Path(args.output_directory).resolve()
    evidence_directory = Path(args.evidence_directory).resolve()
    evidence_directory.mkdir(parents=True, exist_ok=True)

    height = load_height_grid(height_path)
    official_height = load_official_height_texture(height_texture_path)
    official_river_mask = load_official_river_mask(river_mask_path)
    settlements = parse_settlements(settlement_path)
    geography_manifest = load_geography_manifest(geography_manifest_path)
    flora_path = Path(geography_manifest["flora"]["csv"]).resolve()
    scene_cartography_path = Path(geography_manifest["scene"]["csv"]).resolve()
    if sha256(flora_path) != geography_manifest["flora"]["csvSha256"]:
        raise ValueError("Flora CSV hash does not match the geography manifest")
    if sha256(scene_cartography_path) != geography_manifest["scene"]["csvSha256"]:
        raise ValueError("Scene-cartography CSV hash does not match the geography manifest")
    flora = parse_flora_instances(flora_path)
    cartographic_placements = parse_cartographic_placements(scene_cartography_path)
    material_layers = load_material_layers(geography_manifest)
    river_bridge_alignment = audit_river_bridge_alignment(
        official_river_mask,
        geography_manifest["scene"]["bridgeSites"],
    )
    atlas = MotifAtlas(atlas_path)
    terrain_atlas = TerrainMotifAtlas(terrain_atlas_path)
    detail_atlas = DetailMotifAtlas(detail_atlas_path)
    bridge_atlas = BridgeMotifAtlas(bridge_atlas_path)
    bounds = map_bounds(MASTER_SIZE)

    parchment = Image.open(parchment_path).convert("RGB")
    parchment = parchment.resize((MASTER_SIZE, MASTER_SIZE), Image.Resampling.LANCZOS)
    parchment = ImageEnhance.Color(parchment).enhance(0.72)
    parchment = ImageEnhance.Contrast(parchment).enhance(0.96)
    canvas = add_native_parchment_detail(parchment).convert("RGBA")

    land_mask, ocean_baseline = make_land_mask(official_height, bounds, MASTER_SIZE)
    cartographic_land_mask, river_water_mask, river_result = carve_official_river_water(
        official_river_mask,
        land_mask,
        bounds,
    )
    unified_water_mask = cartographic_land_mask.point(
        lambda value: 255 if value < 128 else 0
    )
    river_result["bridgeAlignment"] = river_bridge_alignment
    draw_washes(canvas, cartographic_land_mask, bounds)
    beach_result, beach_inner_land = draw_beach_band(
        canvas,
        cartographic_land_mask,
        bounds,
        args.shore_beach_width,
    )
    material_result = draw_authored_ground_and_water(
        canvas,
        material_layers,
        cartographic_land_mask,
        bounds,
        detail_atlas,
        args.ground_motif_spacing,
        args.ground_motif_min_width,
        args.ground_motif_max_width,
        args.plain_dot_bias,
        args.water_hatch_spacing_y,
        args.water_hatch_spacing_x,
        args.water_hatch_min_width,
        args.water_hatch_max_width,
        args.water_hatch_opacity,
        args.crisp_detail_ink,
    )
    # Plan bridge footprints before static illustrations so no city, mountain,
    # or tree can be baked beneath a deck.  Drawing remains after coastline ink.
    bridge_plan = draw_exact_bridges(
        None,
        cartographic_placements,
        geography_manifest["scene"]["bridgeSites"],
        bounds,
        unified_water_mask,
        cartographic_land_mask,
        bridge_atlas,
    )
    bridge_exclusions = bridge_plan.pop("_exclusionBboxes")
    settlement_plans = plan_settlement_visuals(
        settlements,
        bounds,
        atlas,
        cartographic_land_mask,
        bridge_exclusions,
    )
    settlement_exclusions = settlement_exclusion_bboxes(settlement_plans)
    mountain_result, mountain_bboxes = draw_exact_mountains(
        canvas,
        cartographic_placements,
        bounds,
        terrain_atlas,
        height,
        material_layers,
        cartographic_land_mask,
        bridge_exclusions + settlement_exclusions,
        args.mountain_grid_distance,
        args.mountain_placement_limit,
        args.mountain_scale,
    )
    flora_result = draw_exact_flora(
        canvas,
        flora,
        bounds,
        terrain_atlas,
        cartographic_land_mask,
        bridge_exclusions + mountain_bboxes + settlement_exclusions,
        args.forest_ink_separation,
    )
    # Ink coasts, lakes, and both exact variable-width river banks in one shared
    # pass after terrain detail. Rivers are geography here, not a runtime-style
    # line overlay, and remain legible beneath the bridge symbols that follow.
    coastline_land_mask = cartographic_land_mask.point(
        lambda value: 255 if value >= 128 else 0
    )
    coastline_result = draw_coastline(
        canvas,
        coastline_land_mask,
        bounds,
        beach_inner_land,
    )
    bridge_result = draw_exact_bridges(
        canvas,
        cartographic_placements,
        geography_manifest["scene"]["bridgeSites"],
        bounds,
        unified_water_mask,
        cartographic_land_mask,
        bridge_atlas,
    )
    bridge_result.pop("_exclusionBboxes")
    if bridge_result["placements"] != bridge_plan["placements"]:
        raise ValueError("Bridge planning and final drawing produced different geometry")
    draw_settlements.height_grid = height
    settlement_result = draw_settlements(canvas, settlement_plans)

    visual_evidence = save_authored_visual_evidence(canvas, evidence_directory, bounds)
    mountain_placements = mountain_result.pop("_placements")
    flora_representatives = flora_result.pop("_representatives")
    placement_evidence_path = evidence_directory / "terrain-placement-evidence.json"
    placement_evidence_path.write_text(json.dumps({
        "schema": "reign-war-council-terrain-placement-evidence/v1",
        "mountains": mountain_placements,
        "floraRepresentatives": flora_representatives,
    }, indent=2) + "\n", encoding="utf-8")
    master, overview, tiles = save_master_and_tiles(canvas, output_directory)
    tile_reconstruction = verify_runtime_tile_quality(master, tiles)
    overview_reconstruction = verify_runtime_overview_quality(master, overview)
    audit = {
        "schema": "reign-war-council-map-render/v1",
        "ok": True,
        "authority": {
            "heightGrid": str(height_path),
            "heightGridSha256": sha256(height_path),
            "officialHeightTexture": str(height_texture_path),
            "officialHeightTextureSha256": sha256(height_texture_path),
            "officialRiverMask": str(river_mask_path),
            "officialRiverMaskSha256": sha256(river_mask_path),
            "settlements": str(settlement_path),
            "settlementsSha256": sha256(settlement_path),
            "mainMapGeographyManifest": str(geography_manifest_path),
            "mainMapGeographyManifestSha256": sha256(geography_manifest_path),
            "floraCsv": str(flora_path),
            "floraCsvSha256": sha256(flora_path),
            "sceneCartographyCsv": str(scene_cartography_path),
            "sceneCartographyCsvSha256": sha256(scene_cartography_path),
            "parchmentMaterial": str(parchment_path),
            "parchmentMaterialSha256": sha256(parchment_path),
            "motifAtlas": str(atlas_path),
            "motifAtlasSha256": atlas.source_sha256,
            "terrainMotifAtlas": str(terrain_atlas_path),
            "terrainMotifAtlasSha256": terrain_atlas.source_sha256,
            "detailMotifAtlas": str(detail_atlas_path),
            "detailMotifAtlasSha256": detail_atlas.source_sha256,
            "bridgeMotifAtlas": str(bridge_atlas_path),
            "bridgeMotifAtlasSha256": bridge_atlas.source_sha256,
            "geographySource": "installed NavalDLC/Main_map 1025 height grid, seven 4097 material layers, 69,500 flora transforms, scene bridge/mountain transforms, packed river mask, and audited settlements",
            "appearanceSource": "ImageGen physical parchment and engraved tree, mountain, detail, settlement, and strict top-down bridge motifs only; no generated geography or placement",
        },
        "render": {
            "masterSize": {"width": MASTER_SIZE, "height": MASTER_SIZE},
            "tileGrid": {"columns": TILE_COUNT, "rows": TILE_COUNT, "tileSize": TILE_SIZE},
            "worldSize": {"width": WORLD_SIZE, "height": WORLD_SIZE},
            "playableBorder": {
                "minimumX": PLAYABLE_MIN_X,
                "minimumY": PLAYABLE_MIN_Y,
                "maximumX": PLAYABLE_MAX_X,
                "maximumY": PLAYABLE_MAX_Y,
                "authority": "official Main_map border_min/border_max editor entities",
                "outerMeshExcluded": True,
            },
            "mapBounds": {"left": bounds[0], "top": bounds[1], "right": bounds[2], "bottom": bounds[3]},
            "seaLevel": SEA_LEVEL,
            "officialHeightTextureOceanBaseline": ocean_baseline,
            "fixedStrategicScale": f"{FIXED_STRATEGIC_SCALE}x close strategic view; native 16384px raster",
            "officialRivers": river_result,
            "riverWaterMaskPixels": int(np.count_nonzero(np.asarray(river_water_mask) >= 128)),
            "staticCartography": {
                "authoredGroundAndWater": material_result,
                "shoreline": {
                    "beachBand": beach_result,
                    "boundaryInk": coastline_result,
                },
                "flora": flora_result,
                "mountains": mountain_result,
                "bridges": bridge_result,
                "proceduralGeographyPlacements": 0,
                "geographyInvariantPassed": True,
            },
            "tileReconstruction": tile_reconstruction,
            "overviewReconstruction": overview_reconstruction,
        },
        "settlements": {
            "count": len(settlement_result["placements"]),
            "towns": sum(item.kind == "town" for item in settlements),
            "castles": sum(item.kind == "castle" for item in settlements),
            "villages": sum(item.kind == "village" for item in settlements),
            "waterFailures": settlement_result["waterFailures"],
            "footprintWaterFailures": settlement_result["footprintWaterFailures"],
            "landSafeFootprintCount": settlement_result["landSafeFootprintCount"],
            "inlandAdjustedCount": settlement_result["inlandAdjustedCount"],
            "adaptiveScaleCount": settlement_result["adaptiveScaleCount"],
            "anchorInsideIllustrationCount": settlement_result["anchorInsideIllustrationCount"],
            "anchorWithinQuarterScaleCount": settlement_result["anchorWithinQuarterScaleCount"],
            "maximumVisualOffsetPixels": settlement_result["maximumVisualOffsetPixels"],
            "placementMethod": settlement_result["method"],
            "placements": settlement_result["placements"],
        },
        "outputs": {
            "master": {"path": str(master), "sha256": sha256(master)},
            "runtimeOverview": {
                "path": str(overview),
                "sha256": sha256(overview),
                "width": RUNTIME_OVERVIEW_SIZE,
                "height": RUNTIME_OVERVIEW_SIZE,
            },
            "tiles": [{"path": str(path), "sha256": sha256(path)} for path in tiles],
            "visualEvidence": visual_evidence,
            "terrainPlacementEvidence": {
                "path": str(placement_evidence_path),
                "sha256": sha256(placement_evidence_path),
                "mountainPlacements": len(mountain_placements),
                "floraRepresentatives": len(flora_representatives),
            },
        },
    }
    audit_path = evidence_directory / "war-council-map-render-audit.json"
    audit_path.write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    return audit


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--height-grid", required=True)
    parser.add_argument("--height-texture", required=True)
    parser.add_argument("--river-mask", required=True)
    parser.add_argument("--settlements", required=True)
    parser.add_argument("--geography-manifest", required=True)
    parser.add_argument("--parchment", required=True)
    parser.add_argument("--motif-atlas", required=True)
    parser.add_argument("--terrain-atlas", required=True)
    parser.add_argument("--detail-atlas", required=True)
    parser.add_argument("--bridge-atlas", required=True)
    parser.add_argument("--forest-ink-separation", type=int, default=3)
    parser.add_argument("--mountain-grid-distance", type=float, default=3.0)
    parser.add_argument("--mountain-placement-limit", type=int, default=15000)
    parser.add_argument("--mountain-scale", type=float, default=0.98)
    parser.add_argument("--shore-beach-width", type=int, default=0)
    parser.add_argument("--ground-motif-spacing", type=int, default=92)
    parser.add_argument("--ground-motif-min-width", type=int, default=12)
    parser.add_argument("--ground-motif-max-width", type=int, default=27)
    parser.add_argument("--plain-dot-bias", type=float, default=0.0)
    parser.add_argument("--water-hatch-spacing-y", type=int, default=42)
    parser.add_argument("--water-hatch-spacing-x", type=int, default=172)
    parser.add_argument("--water-hatch-min-width", type=int, default=88)
    parser.add_argument("--water-hatch-max-width", type=int, default=159)
    parser.add_argument("--water-hatch-opacity", type=float, default=0.30)
    parser.add_argument("--crisp-detail-ink", action="store_true")
    parser.add_argument("--output-directory", required=True)
    parser.add_argument("--evidence-directory", required=True)
    args = parser.parse_args()
    audit = render(args)
    print(json.dumps({"ok": audit["ok"], "render": audit["render"], "settlements": {k: v for k, v in audit["settlements"].items() if k != "placements"}, "audit": str(Path(args.evidence_directory).resolve() / "war-council-map-render-audit.json")}, indent=2))


if __name__ == "__main__":
    main()
