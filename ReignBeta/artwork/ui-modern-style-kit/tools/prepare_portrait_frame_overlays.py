#!/usr/bin/env python3
"""Build registered portrait aperture plates without touching portrait imagery.

Each live portrait remains an untouched rectangular texture.  This tool turns the
registered decorative frame sprite into an opaque black-marble plate whose only
transparent area is the declared circle, oval, or rectangle aperture.  Existing authored gold
linework is retained above the material plate, so the portrait can be centered and
overscanned beneath it without square leaks, black backing disks, or pale seams.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import tempfile
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, __version__ as PILLOW_VERSION


SPEC_SCHEMA = "reign-ui-portrait-aperture-plate-spec-v2"
EVIDENCE_SCHEMA = "reign-ui-portrait-aperture-plate-evidence-v2"
TOOL_VERSION = "2.3.0"
SUPPORTED_APERTURE_SHAPES = {"circle", "oval", "rectangle"}


class PreparationError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_relative_path(root: Path, value: str, label: str) -> Path:
    candidate = (root / value).resolve()
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise PreparationError(f"{label} escapes the workspace root: {candidate}") from exc
    return candidate


def parse_hex_rgb(value: str) -> tuple[int, int, int]:
    text = str(value or "").strip().lstrip("#")
    if len(text) != 6:
        raise PreparationError(f"Expected a six-digit RGB value, found '{value}'.")
    try:
        return tuple(int(text[index : index + 2], 16) for index in (0, 2, 4))  # type: ignore[return-value]
    except ValueError as exc:
        raise PreparationError(f"Invalid RGB value '{value}'.") from exc


def alpha_statistics(image: Image.Image) -> dict[str, int]:
    histogram = image.getchannel("A").histogram()
    return {
        "transparentPixels": sum(histogram[:17]),
        "partiallyTransparentPixels": sum(histogram[17:240]),
        "opaquePixels": sum(histogram[240:]),
    }


def light_neutral_count(image: Image.Image, minimum_channel: float, chroma_ceiling: float) -> int:
    total = 0
    for red, green, blue, alpha in image.get_flattened_data():
        if alpha <= 16:
            continue
        chroma = max(red, green, blue) - min(red, green, blue)
        if min(red, green, blue) >= minimum_channel and chroma <= chroma_ceiling:
            total += 1
    return total


def normalize_aperture_shape(value: Any, aperture: dict[str, float]) -> str:
    shape = str(value or "").strip().lower()
    if not shape:
        aspect = aperture["width"] / aperture["height"]
        shape = "circle" if abs(aspect - 1.0) <= 0.05 else "oval"
    if shape not in SUPPORTED_APERTURE_SHAPES:
        raise PreparationError(
            f"Unsupported portrait aperture shape '{shape}'. Expected circle, oval, or rectangle."
        )
    return shape


def aperture_distance(
    x: int,
    y: int,
    aperture: dict[str, float],
    shape: str,
) -> float:
    if shape == "rectangle":
        center_x = aperture["x"] + aperture["width"] * 0.5
        center_y = aperture["y"] + aperture["height"] * 0.5
        half_width = aperture["width"] * 0.5
        half_height = aperture["height"] * 0.5
        delta_x = abs((x + 0.5) - center_x) - half_width
        delta_y = abs((y + 0.5) - center_y) - half_height
        outside = math.hypot(max(delta_x, 0.0), max(delta_y, 0.0))
        inside = min(max(delta_x, delta_y), 0.0)
        return outside + inside

    radius_x = aperture["width"] * 0.5
    radius_y = aperture["height"] * 0.5
    center_x = aperture["x"] + radius_x
    center_y = aperture["y"] + radius_y
    normalized_x = ((x + 0.5) - center_x) / max(radius_x, 0.5)
    normalized_y = ((y + 0.5) - center_y) / max(radius_y, 0.5)
    radial = math.sqrt(normalized_x * normalized_x + normalized_y * normalized_y)
    return (radial - 1.0) * min(radius_x, radius_y)


def validate_aperture(value: Any, width: int, height: int) -> dict[str, float]:
    if not isinstance(value, dict):
        raise PreparationError("Each frame asset requires an aperture object.")
    aperture = {key: float(value.get(key, 0)) for key in ("x", "y", "width", "height")}
    if aperture["width"] <= 0 or aperture["height"] <= 0:
        raise PreparationError("Aperture dimensions must be positive.")
    if aperture["x"] < 0 or aperture["y"] < 0:
        raise PreparationError("Aperture origin must be non-negative.")
    if aperture["x"] + aperture["width"] > width or aperture["y"] + aperture["height"] > height:
        raise PreparationError("Aperture lies outside its overlay canvas.")
    return aperture


def build_aperture_alpha(
    size: tuple[int, int],
    aperture: dict[str, float],
    antialias_scale: int,
    shape: str,
) -> Image.Image:
    """Return opaque exterior alpha with one exact antialiased transparent aperture."""

    if antialias_scale < 2 or antialias_scale > 16:
        raise PreparationError("Aperture antialias scale must be between 2 and 16.")
    width, height = size
    scale = antialias_scale
    alpha = Image.new("L", (width * scale, height * scale), 255)
    draw = ImageDraw.Draw(alpha)
    left = round(aperture["x"] * scale)
    top = round(aperture["y"] * scale)
    right = round((aperture["x"] + aperture["width"]) * scale) - 1
    bottom = round((aperture["y"] + aperture["height"]) * scale) - 1
    if shape == "rectangle":
        draw.rectangle((left, top, right, bottom), fill=0)
    else:
        draw.ellipse((left, top, right, bottom), fill=0)
    return alpha.resize(size, Image.Resampling.LANCZOS)


def load_material_plate(
    workspace_root: Path,
    size: tuple[int, int],
    aperture: dict[str, float],
    shape: str,
    entry: dict[str, Any],
    defaults: dict[str, Any],
) -> tuple[Image.Image, dict[str, Any]]:
    configured_color = entry.get("materialColor", defaults.get("materialColor"))
    if configured_color is not None:
        red, green, blue = parse_hex_rgb(str(configured_color))
        antialias_scale = int(entry.get("antialiasScale", defaults.get("antialiasScale", 8)))
        fitted = Image.new("RGBA", size, (red, green, blue, 255))
        fitted.putalpha(build_aperture_alpha(size, aperture, antialias_scale, shape))
        return fitted, {
            "kind": "color",
            "color": "#{:02X}{:02X}{:02X}".format(red, green, blue),
            "antialiasScale": antialias_scale,
        }

    configured = {**(defaults.get("materialSource") or {}), **(entry.get("materialSource") or {})}
    relative_path = str(configured.get("path") or "").strip().replace("\\", "/")
    if not relative_path:
        raise PreparationError("A composed aperture plate requires materialSource.path.")
    source_path = require_relative_path(workspace_root, relative_path, "Aperture material source")
    if not source_path.is_file():
        raise PreparationError(f"Aperture material source is missing: {source_path}")
    source_hash = sha256(source_path)
    expected_hash = str(configured.get("sha256") or "").strip().lower()
    if expected_hash and source_hash != expected_hash:
        raise PreparationError(
            f"Aperture material source hash {source_hash} does not match declared {expected_hash}."
        )

    with Image.open(source_path) as opened:
        opened.load()
        material = opened.convert("RGBA")
    crop = configured.get("crop") or {}
    if crop:
        crop_x = int(crop.get("x") or 0)
        crop_y = int(crop.get("y") or 0)
        crop_width = int(crop.get("width") or 0)
        crop_height = int(crop.get("height") or 0)
        if crop_width <= 0 or crop_height <= 0:
            raise PreparationError("Aperture material crop dimensions must be positive.")
        if crop_x < 0 or crop_y < 0 or crop_x + crop_width > material.width or crop_y + crop_height > material.height:
            raise PreparationError("Aperture material crop lies outside its source image.")
        material = material.crop((crop_x, crop_y, crop_x + crop_width, crop_y + crop_height))

    fitted = ImageOps.fit(material, size, method=Image.Resampling.LANCZOS, centering=(0.5, 0.5))
    antialias_scale = int(entry.get("antialiasScale", defaults.get("antialiasScale", 8)))
    fitted.putalpha(build_aperture_alpha(size, aperture, antialias_scale, shape))
    return fitted, {
        "kind": "image",
        "path": relative_path,
        "sha256": source_hash,
        "crop": crop or None,
        "antialiasScale": antialias_scale,
    }


def retain_components_anchored_to(candidate: Image.Image, anchor: Image.Image) -> tuple[Image.Image, int]:
    """Remove detached warm marble flecks while preserving frame-connected ornament."""

    width, height = candidate.size
    candidate_pixels = candidate.load()
    anchor_pixels = anchor.load()
    retained = Image.new("L", candidate.size, 0)
    retained_pixels = retained.load()
    visited = bytearray(width * height)
    removed = 0
    for seed_y in range(height):
        for seed_x in range(width):
            seed_index = seed_y * width + seed_x
            if visited[seed_index] or candidate_pixels[seed_x, seed_y] == 0:
                continue
            queue = [(seed_x, seed_y)]
            visited[seed_index] = 1
            component: list[tuple[int, int]] = []
            anchored = False
            while queue:
                x, y = queue.pop()
                component.append((x, y))
                anchored = anchored or anchor_pixels[x, y] > 0
                for next_x, next_y in (
                    (x - 1, y - 1), (x, y - 1), (x + 1, y - 1),
                    (x - 1, y), (x + 1, y),
                    (x - 1, y + 1), (x, y + 1), (x + 1, y + 1),
                ):
                    if next_x < 0 or next_y < 0 or next_x >= width or next_y >= height:
                        continue
                    index = next_y * width + next_x
                    if visited[index] or candidate_pixels[next_x, next_y] == 0:
                        continue
                    visited[index] = 1
                    queue.append((next_x, next_y))
            if anchored:
                for x, y in component:
                    retained_pixels[x, y] = 255
            else:
                removed += len(component)
    return retained, removed


def prepare_asset(
    workspace_root: Path,
    entry: dict[str, Any],
    defaults: dict[str, Any],
    force: bool,
) -> dict[str, Any]:
    asset_id = str(entry.get("id") or "").strip()
    if not asset_id:
        raise PreparationError("Every frame asset requires an id.")
    relative_path = str(entry.get("path") or "").strip().replace("\\", "/")
    if not relative_path:
        raise PreparationError(f"Frame asset '{asset_id}' has no path.")
    path = require_relative_path(workspace_root, relative_path, f"Frame asset '{asset_id}'")
    expected_width = int(entry.get("width") or 0)
    expected_height = int(entry.get("height") or 0)
    if expected_width <= 0 or expected_height <= 0:
        raise PreparationError(f"Frame asset '{asset_id}' requires positive width and height.")

    target_existed = path.is_file()
    create_if_missing = entry.get("createIfMissing", False)
    if not isinstance(create_if_missing, bool):
        raise PreparationError(f"Frame asset '{asset_id}' createIfMissing must be a boolean.")
    if not target_existed and not create_if_missing:
        raise PreparationError(f"Frame asset '{asset_id}' is missing: {path}")

    configured_frame_source = entry.get("frameSource")
    frame_source_evidence: dict[str, Any] | None = None
    loaded_frame_source: Image.Image | None = None
    if configured_frame_source is not None:
        if not isinstance(configured_frame_source, dict):
            raise PreparationError(f"Frame asset '{asset_id}' frameSource must be an object.")
        frame_source_relative_path = str(configured_frame_source.get("path") or "").strip().replace("\\", "/")
        if not frame_source_relative_path:
            raise PreparationError(f"Frame asset '{asset_id}' frameSource.path is required.")
        frame_source_path = require_relative_path(workspace_root, frame_source_relative_path, "Frame artwork source")
        if not frame_source_path.is_file():
            raise PreparationError(f"Frame artwork source is missing: {frame_source_path}")
        frame_source_hash = sha256(frame_source_path)
        expected_frame_source_hash = str(configured_frame_source.get("sha256") or "").strip().lower()
        if expected_frame_source_hash and frame_source_hash != expected_frame_source_hash:
            raise PreparationError(
                f"Frame artwork source hash {frame_source_hash} does not match declared "
                f"{expected_frame_source_hash}."
            )
        with Image.open(frame_source_path) as opened_frame_source:
            opened_frame_source.load()
            loaded_frame_source = opened_frame_source.convert("RGBA")
        fit_to_canvas = configured_frame_source.get("fitToCanvas", False)
        if not isinstance(fit_to_canvas, bool):
            raise PreparationError(f"Frame asset '{asset_id}' frameSource.fitToCanvas must be a boolean.")
        if loaded_frame_source.size != (expected_width, expected_height):
            if not fit_to_canvas:
                raise PreparationError(
                    f"Frame artwork source for '{asset_id}' is {loaded_frame_source.width}x"
                    f"{loaded_frame_source.height}; expected {expected_width}x{expected_height}."
                )
            loaded_frame_source = ImageOps.fit(
                loaded_frame_source,
                (expected_width, expected_height),
                method=Image.Resampling.LANCZOS,
                centering=(0.5, 0.5),
            )
        frame_source_evidence = {
            "path": frame_source_relative_path,
            "sha256": frame_source_hash,
            "fitToCanvas": fit_to_canvas,
            "dimensions": {"width": loaded_frame_source.width, "height": loaded_frame_source.height},
        }

    if target_existed:
        source_sha256 = sha256(path)
        with Image.open(path) as opened:
            opened.load()
            source_mode = opened.mode
            source_format = opened.format
            image = opened.convert("RGBA")
    else:
        if loaded_frame_source is None:
            raise PreparationError(
                f"Frame asset '{asset_id}' may be created only when frameSource is declared."
            )
        source_sha256 = str(frame_source_evidence["sha256"])
        source_mode = "RGBA"
        source_format = "PNG"
        image = loaded_frame_source.copy()

    accepted_hashes = {str(value).strip().lower() for value in entry.get("acceptedSourceSha256", [])}
    if target_existed and accepted_hashes and source_sha256 not in accepted_hashes:
        raise PreparationError(
            f"Frame asset '{asset_id}' source hash {source_sha256} is not declared by the spec."
        )
    if image.size != (expected_width, expected_height):
        raise PreparationError(
            f"Frame asset '{asset_id}' is {image.width}x{image.height}; expected "
            f"{expected_width}x{expected_height}."
        )

    frame_source = loaded_frame_source if loaded_frame_source is not None else image

    aperture = validate_aperture(entry.get("aperture"), image.width, image.height)
    shape = normalize_aperture_shape(entry.get("shape"), aperture)
    preparation_mode = str(entry.get("preparationMode") or "compose-opaque-aperture-plate").strip()
    if preparation_mode not in {"compose-opaque-aperture-plate", "verify-existing-aperture-plate"}:
        raise PreparationError(
            f"Frame asset '{asset_id}' has unsupported preparation mode '{preparation_mode}'."
        )
    recompose_existing = entry.get("recomposeExistingAperturePlate", False)
    if not isinstance(recompose_existing, bool):
        raise PreparationError(
            f"Frame asset '{asset_id}' recomposeExistingAperturePlate must be a boolean."
        )
    if recompose_existing and preparation_mode != "compose-opaque-aperture-plate":
        raise PreparationError(
            f"Frame asset '{asset_id}' may recompose an existing plate only in compose mode."
        )
    input_corners = [
        image.getpixel((0, 0)),
        image.getpixel((image.width - 1, 0)),
        image.getpixel((0, image.height - 1)),
        image.getpixel((image.width - 1, image.height - 1)),
    ]
    input_center = (
        int(aperture["x"] + aperture["width"] * 0.5),
        int(aperture["y"] + aperture["height"] * 0.5),
    )
    already_aperture_plate = (
        all(pixel[3] >= 240 for pixel in input_corners)
        and image.getpixel(input_center)[3] <= 16
    )
    effective_preparation_mode = (
        "compose-opaque-aperture-plate"
        if recompose_existing
        else (
            "verify-existing-aperture-plate"
            if preparation_mode == "compose-opaque-aperture-plate" and already_aperture_plate
            else preparation_mode
        )
    )
    material_band = float(entry.get("materialBandPixels", defaults.get("materialBandPixels", 4.0)))
    ornament_band = float(entry.get("ornamentBandPixels", defaults.get("ornamentBandPixels", 32.0)))
    ornament_dilation = int(entry.get("ornamentDilationPixels", defaults.get("ornamentDilationPixels", 1)))
    if material_band <= 0 or ornament_band < material_band:
        raise PreparationError(f"Frame asset '{asset_id}' has invalid material/ornament band widths.")
    if ornament_dilation < 0 or ornament_dilation > 6:
        raise PreparationError(f"Frame asset '{asset_id}' ornament dilation must be between 0 and 6.")

    selector = {**defaults.get("goldSelector", {}), **entry.get("goldSelector", {})}
    minimum_red = int(selector.get("minimumRed", 45))
    minimum_red_minus_blue = int(selector.get("minimumRedMinusBlue", 8))
    minimum_green_minus_blue = int(selector.get("minimumGreenMinusBlue", 3))
    neutral = {**defaults.get("lightNeutral", {}), **entry.get("lightNeutral", {})}
    luminance_floor = float(neutral.get("luminanceFloor", 140.0))
    chroma_ceiling = float(neutral.get("chromaCeiling", 20.0))
    neutral_replacement = parse_hex_rgb(str(neutral.get("replacementRgb", "#0A0A09")))

    source_pixels = frame_source.load()
    gold_mask = Image.new("L", image.size, 0)
    material_mask = Image.new("L", image.size, 0)
    gold_pixels = gold_mask.load()
    material_mask_pixels = material_mask.load()
    distances: list[float] = [0.0] * (image.width * image.height)
    for y in range(image.height):
        for x in range(image.width):
            distance = aperture_distance(x, y, aperture, shape)
            distances[y * image.width + x] = distance
            red, green, blue, alpha = source_pixels[x, y]
            if alpha <= 16:
                continue
            if -1.0 <= distance <= material_band:
                material_mask_pixels[x, y] = 255
            if distance < -1.0 or distance > ornament_band:
                continue
            if (
                red >= minimum_red
                and red - blue >= minimum_red_minus_blue
                and green - blue >= minimum_green_minus_blue
            ):
                gold_pixels[x, y] = 255

    if ornament_dilation:
        filter_size = ornament_dilation * 2 + 1
        gold_mask = gold_mask.filter(ImageFilter.MaxFilter(filter_size))
    candidate_mask = ImageChops.lighter(material_mask, gold_mask)
    retained_mask, removed_detached_pixels = retain_components_anchored_to(candidate_mask, material_mask)
    retained_gold = gold_mask.load()
    retained_pixels = retained_mask.load()
    frame_art = Image.new("RGBA", image.size, (0, 0, 0, 0))
    frame_pixels = frame_art.load()
    material_pixels = 0
    ornament_pixels = 0
    replaced_light_neutral_pixels = 0
    for y in range(image.height):
        for x in range(image.width):
            red, green, blue, alpha = source_pixels[x, y]
            if alpha <= 16:
                continue
            distance = distances[y * image.width + x]
            keep_material = -1.0 <= distance <= material_band
            keep_ornament = distance >= -1.0 and retained_gold[x, y] > 0
            if retained_pixels[x, y] == 0 or (not keep_material and not keep_ornament):
                continue
            chroma = max(red, green, blue) - min(red, green, blue)
            if min(red, green, blue) >= luminance_floor and chroma <= chroma_ceiling:
                if not keep_material:
                    continue
                red, green, blue = neutral_replacement
                replaced_light_neutral_pixels += 1
            frame_pixels[x, y] = (red, green, blue, alpha)
            if keep_material:
                material_pixels += 1
            if keep_ornament:
                ornament_pixels += 1

    material_evidence: dict[str, Any] | None = None
    material_plate: Image.Image | None = None
    if effective_preparation_mode == "compose-opaque-aperture-plate":
        material_plate, material_evidence = load_material_plate(
            workspace_root, image.size, aperture, shape, entry, defaults
        )
        output = Image.alpha_composite(material_plate, frame_art)
    else:
        output = image.copy()

    # Fully transparent RGB is zeroed explicitly for stable atlas filtering.
    output_pixels = output.load()
    for y in range(output.height):
        for x in range(output.width):
            red, green, blue, alpha = output_pixels[x, y]
            if alpha == 0 and (red or green or blue):
                output_pixels[x, y] = (0, 0, 0, 0)

    output_alpha = alpha_statistics(output)
    if output_alpha["opaquePixels"] <= 0:
        raise PreparationError(f"Frame asset '{asset_id}' retained no opaque artwork.")
    corners = [
        output.getpixel((0, 0)),
        output.getpixel((output.width - 1, 0)),
        output.getpixel((0, output.height - 1)),
        output.getpixel((output.width - 1, output.height - 1)),
    ]
    if any(pixel[3] < 240 for pixel in corners):
        raise PreparationError(f"Aperture plate '{asset_id}' has a transparent exterior corner.")
    center = (
        int(aperture["x"] + aperture["width"] * 0.5),
        int(aperture["y"] + aperture["height"] * 0.5),
    )
    if output.getpixel(center)[3] > 16:
        raise PreparationError(f"Aperture plate '{asset_id}' retained an opaque backing disk.")
    remaining_light_neutral = light_neutral_count(output, luminance_floor, chroma_ceiling)
    if remaining_light_neutral:
        raise PreparationError(
            f"Frame asset '{asset_id}' retained {remaining_light_neutral} light-neutral halo pixels."
        )

    aperture_interior_pixels = 0
    aperture_interior_violations = 0
    context_compared_pixels = 0
    context_mismatch_pixels = 0
    gold_topmost_pixels = 0
    gold_topmost_mismatch_pixels = 0
    if material_plate is not None:
        expected_composite = Image.alpha_composite(material_plate, frame_art)
        expected_pixels = expected_composite.load()
        material_pixels_rgba = material_plate.load()
        frame_art_pixels = frame_art.load()
        for y in range(output.height):
            for x in range(output.width):
                distance = distances[y * output.width + x]
                actual = output_pixels[x, y]
                if distance <= -1.5:
                    aperture_interior_pixels += 1
                    if actual[3] > 16:
                        aperture_interior_violations += 1
                if distance >= 0.0 and frame_art_pixels[x, y][3] <= 16:
                    context_compared_pixels += 1
                    if actual != material_pixels_rgba[x, y]:
                        context_mismatch_pixels += 1
                if frame_art_pixels[x, y][3] > 16:
                    gold_topmost_pixels += 1
                    if actual != expected_pixels[x, y]:
                        gold_topmost_mismatch_pixels += 1
        if context_mismatch_pixels:
            raise PreparationError(
                f"Frame asset '{asset_id}' has {context_mismatch_pixels} exterior context mismatches."
            )
        if gold_topmost_mismatch_pixels:
            raise PreparationError(
                f"Frame asset '{asset_id}' has {gold_topmost_mismatch_pixels} topmost frame mismatches."
            )
        if aperture_interior_violations:
            raise PreparationError(
                f"Frame asset '{asset_id}' retained {aperture_interior_violations} opaque aperture pixels."
            )

    temporary_path: Path | None = None
    try:
        file_descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{path.stem}.", suffix=".png", dir=path.parent
        )
        os.close(file_descriptor)
        temporary_path = Path(temporary_name)
        output.save(temporary_path, format="PNG", optimize=True, compress_level=9)
        output_sha256 = sha256(temporary_path)
        expected_output_sha256 = str(entry.get("expectedOutputSha256") or "").strip().lower()
        if expected_output_sha256 and output_sha256 != expected_output_sha256:
            raise PreparationError(
                f"Frame asset '{asset_id}' produced {output_sha256}; expected {expected_output_sha256}."
            )
        if not force and output_sha256 != source_sha256:
            raise PreparationError(
                f"Frame asset '{asset_id}' needs replacement; rerun with --force after reviewing evidence."
            )
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)

    with Image.open(path) as verified:
        verified.load()
        if verified.format != "PNG" or verified.mode != "RGBA" or verified.size != image.size:
            raise PreparationError(f"Frame asset '{asset_id}' failed post-write PNG/RGBA verification.")
    written_sha256 = sha256(path)
    if written_sha256 != output_sha256:
        raise PreparationError(f"Frame asset '{asset_id}' post-write hash changed unexpectedly.")

    return {
        "id": asset_id,
        "path": relative_path,
        "source": {
            "sha256": source_sha256,
            "format": source_format,
            "mode": source_mode,
            "dimensions": {"width": image.width, "height": image.height},
            "targetExistedBeforePreparation": target_existed,
        },
        "aperture": aperture,
        "parameters": {
            "shape": shape,
            "requestedPreparationMode": preparation_mode,
            "effectivePreparationMode": effective_preparation_mode,
            "alreadyAperturePlate": already_aperture_plate,
            "recomposeExistingAperturePlate": recompose_existing,
            "frameSource": frame_source_evidence,
            "materialSource": material_evidence,
            "contextContinuity": {
                "comparisonTolerancePerChannel": 0,
                "comparedExteriorPixels": context_compared_pixels,
                "mismatchPixels": context_mismatch_pixels,
                "goldTopmostPixels": gold_topmost_pixels,
                "goldTopmostMismatchPixels": gold_topmost_mismatch_pixels,
                "apertureInteriorPixels": aperture_interior_pixels,
                "apertureInteriorViolationPixels": aperture_interior_violations,
            },
            "materialBandPixels": material_band,
            "ornamentBandPixels": ornament_band,
            "ornamentDilationPixels": ornament_dilation,
            "goldSelector": {
                "minimumRed": minimum_red,
                "minimumRedMinusBlue": minimum_red_minus_blue,
                "minimumGreenMinusBlue": minimum_green_minus_blue,
            },
            "lightNeutral": {
                "minimumChannel": luminance_floor,
                "chromaCeiling": chroma_ceiling,
                "replacementRgb": "#{:02X}{:02X}{:02X}".format(*neutral_replacement),
            },
        },
        "output": {
            "sha256": output_sha256,
            "expectedSha256": expected_output_sha256,
            "format": "PNG",
            "mode": "RGBA",
            "dimensions": {"width": output.width, "height": output.height},
            "alpha": output_alpha,
            "cornerAlpha": [pixel[3] for pixel in corners],
            "apertureCenterAlpha": output.getpixel(center)[3],
            "materialPixels": material_pixels,
            "ornamentPixels": ornament_pixels,
            "replacedLightNeutralPixels": replaced_light_neutral_pixels,
            "remainingLightNeutralPixels": remaining_light_neutral,
            "removedDetachedPixels": removed_detached_pixels,
            "transparentRgbZeroed": True,
        },
        "portraitSourcePixelsRead": False,
        "portraitSourcePixelsWritten": False,
    }


def write_evidence(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def run(spec_path: Path, evidence_path: Path, force: bool) -> dict[str, Any]:
    spec_path = spec_path.resolve()
    with spec_path.open("r", encoding="utf-8") as stream:
        spec = json.load(stream)
    if spec.get("schema") != SPEC_SCHEMA:
        raise PreparationError(f"Unsupported portrait-frame spec schema '{spec.get('schema')}'.")
    workspace_root = (spec_path.parent / str(spec.get("workspaceRoot") or "../../../..")).resolve()
    entries = spec.get("assets")
    if not isinstance(entries, list) or not entries:
        raise PreparationError("Portrait-frame spec must declare at least one asset.")
    assets = [prepare_asset(workspace_root, entry, spec.get("defaults") or {}, force) for entry in entries]
    evidence = {
        "schema": EVIDENCE_SCHEMA,
        "tool": {"name": Path(__file__).name, "version": TOOL_VERSION, "pillowVersion": PILLOW_VERSION},
        "spec": {"path": str(spec_path), "sha256": sha256(spec_path), "schema": SPEC_SCHEMA},
        "workspaceRoot": str(workspace_root),
        "assetCount": len(assets),
        "assets": assets,
        "invariants": {
            "portraitSourcePixelsRead": False,
            "portraitSourcePixelsWritten": False,
            "opaqueMarbleExteriorRequired": True,
            "transparentApertureRequired": True,
            "opaqueBackingDiskForbidden": True,
            "lightNeutralHaloPixelsAllowed": 0,
            "contextContinuityTolerancePerChannel": 0,
            "goldFrameTopmostRequired": True,
        },
    }
    write_evidence(evidence_path.resolve(), evidence)
    return evidence


def self_test() -> dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="reign-portrait-frame-") as directory:
        root = Path(directory)
        asset = root / "frame.png"
        frame_source = root / "frame-source.png"
        material = root / "material.png"
        material_image = Image.new("RGBA", (160, 160), (10, 10, 9, 255))
        material_pixels = material_image.load()
        for y in range(material_image.height):
            for x in range(material_image.width):
                if (x * 7 + y * 11) % 53 == 0:
                    material_pixels[x, y] = (25, 23, 19, 255)
        material_image.save(material, format="PNG")
        image = Image.new("RGBA", (96, 112), (0, 0, 0, 0))
        pixels = image.load()
        aperture = {"x": 12, "y": 10, "width": 72, "height": 92}
        for y in range(image.height):
            for x in range(image.width):
                distance = aperture_distance(x, y, aperture, "oval")
                if distance < 0:
                    pixels[x, y] = (0, 0, 0, 0)
                elif distance <= 1.2:
                    pixels[x, y] = (238, 238, 238, 255)
                elif distance <= 4.0:
                    pixels[x, y] = (145, 108, 55, 255)
        image.save(asset, format="PNG")
        image.save(frame_source, format="PNG")
        source_hash = sha256(asset)
        entry = {
            "id": "self-test-frame",
            "path": "frame.png",
            "width": 96,
            "height": 112,
            "aperture": aperture,
            "acceptedSourceSha256": [source_hash],
            "materialBandPixels": 5,
            "ornamentBandPixels": 12,
            "materialSource": {
                "path": "material.png",
                "sha256": sha256(material),
                "crop": {"x": 8, "y": 8, "width": 144, "height": 144},
            },
            "recomposeExistingAperturePlate": True,
            "frameSource": {
                "path": "frame-source.png",
                "sha256": sha256(frame_source),
            },
        }
        first = prepare_asset(root, entry, {}, True)
        entry["acceptedSourceSha256"].append(first["output"]["sha256"])
        second = prepare_asset(root, entry, {}, True)
        if first["output"]["sha256"] != second["output"]["sha256"]:
            raise PreparationError("Self-test transformation is not idempotent.")
        if any(alpha < 240 for alpha in first["output"]["cornerAlpha"]):
            raise PreparationError("Self-test left a transparent exterior corner.")
        if first["output"]["apertureCenterAlpha"] > 16:
            raise PreparationError("Self-test retained an opaque backing disk.")
        if first["output"]["remainingLightNeutralPixels"] != 0:
            raise PreparationError("Self-test retained a light-neutral halo.")
        rectangle_alpha = build_aperture_alpha((96, 112), aperture, 8, "rectangle")
        if rectangle_alpha.getpixel((16, 14)) > 16:
            raise PreparationError("Self-test rectangular aperture did not clear its interior corner.")
        if rectangle_alpha.getpixel((5, 5)) < 240:
            raise PreparationError("Self-test rectangular aperture cleared the exterior.")
        return {
            "schema": EVIDENCE_SCHEMA,
            "mode": "self-test",
            "passed": True,
            "idempotent": True,
            "opaqueMarbleCorners": True,
            "transparentAperture": True,
            "rectangleApertureSupported": True,
            "lightNeutralHaloPixels": 0,
            "portraitSourcePixelsRead": False,
            "portraitSourcePixelsWritten": False,
            "outputSha256": second["output"]["sha256"],
        }


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--spec", type=Path)
    result.add_argument("--evidence", type=Path)
    result.add_argument("--force", action="store_true")
    result.add_argument("--self-test", action="store_true")
    return result


def main() -> int:
    args = parser().parse_args()
    try:
        if args.self_test:
            if args.spec or args.evidence or args.force:
                raise PreparationError("--self-test does not accept production arguments.")
            print(json.dumps(self_test(), indent=2, sort_keys=True))
            return 0
        if not args.spec or not args.evidence:
            raise PreparationError("--spec and --evidence are required unless --self-test is used.")
        print(json.dumps(run(args.spec, args.evidence, args.force), indent=2, sort_keys=True))
        return 0
    except (OSError, ValueError, PreparationError) as exc:
        print(f"prepare_portrait_frame_overlays.py: error: {exc}", file=os.sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
