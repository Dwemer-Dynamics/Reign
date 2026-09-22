#!/usr/bin/env python3
"""Compose image-to-image fills only inside declared dynamic UI regions.

The approved reference remains the byte-for-byte authority everywhere outside
the union of the declared masks. Rectangular text/list regions use a locally
color-matched, inward-feathered fill. Ellipse and polygon regions use the
normalized image-to-image source directly because they represent replaceable
portrait, banner, scene, or map content.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import statistics
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

from PIL import Image, ImageChops, ImageDraw, ImageOps


SCHEMA = "reign-fixed-region-composite-v1"
EVIDENCE_SCHEMA = "reign-fixed-region-composite-evidence-v1"
TOOL_VERSION = "2026.08.28.4"


class CompositeError(ValueError):
    """Raised when a fixed-region composite contract is invalid or unsafe."""


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path) -> Mapping[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def resolve_asset(spec_path: Path, declaration: Mapping[str, Any], label: str) -> Path:
    raw_path = declaration.get("path")
    if not isinstance(raw_path, str) or not raw_path.strip():
        raise CompositeError(f"{label}.path must be a non-empty string")
    path = (spec_path.parent / raw_path).resolve()
    if not path.is_file():
        raise CompositeError(f"{label} is missing: {path}")
    expected = declaration.get("sha256")
    actual = sha256(path)
    if expected and str(expected).lower() != actual:
        raise CompositeError(f"{label} SHA-256 mismatch: expected {expected}, actual {actual}")
    return path


def require_int(value: Any, label: str, minimum: int = 0) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise CompositeError(f"{label} must be an integer >= {minimum}")
    return value


def require_number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise CompositeError(f"{label} must be numeric")
    return float(value)


def normalize_spec(spec_path: Path, raw: Mapping[str, Any]) -> Mapping[str, Any]:
    if raw.get("schema") != SCHEMA:
        raise CompositeError(f"Unsupported schema {raw.get('schema')!r}; expected {SCHEMA}")
    canvas = raw.get("canvas")
    if not isinstance(canvas, Mapping):
        raise CompositeError("canvas must be an object")
    width = require_int(canvas.get("width"), "canvas.width", 1)
    height = require_int(canvas.get("height"), "canvas.height", 1)
    mode = raw.get("rectangularFillMode") or {}
    if not isinstance(mode, Mapping):
        raise CompositeError("rectangularFillMode must be an object")
    border = require_int(mode.get("borderSamplePixels", 4), "rectangularFillMode.borderSamplePixels", 1)
    feather = require_int(mode.get("inwardFeatherPixels", 3), "rectangularFillMode.inwardFeatherPixels", 0)
    regions = raw.get("sceneDependentMasks")
    if not isinstance(regions, list) or not regions:
        raise CompositeError("sceneDependentMasks must be a non-empty array")
    seen: set[str] = set()
    normalized_regions: list[Mapping[str, Any]] = []
    for index, region in enumerate(regions):
        if not isinstance(region, Mapping):
            raise CompositeError(f"sceneDependentMasks[{index}] must be an object")
        identifier = region.get("id")
        if not isinstance(identifier, str) or not identifier.strip() or identifier in seen:
            raise CompositeError(f"sceneDependentMasks[{index}].id must be unique and non-empty")
        seen.add(identifier)
        shape = str(region.get("shape", "")).lower()
        if shape in {"rectangle", "ellipse"}:
            x = require_int(region.get("x"), f"{identifier}.x")
            y = require_int(region.get("y"), f"{identifier}.y")
            region_width = require_int(region.get("width"), f"{identifier}.width", 1)
            region_height = require_int(region.get("height"), f"{identifier}.height", 1)
            if x + region_width > width or y + region_height > height:
                raise CompositeError(f"{identifier} lies outside the {width}x{height} canvas")
            color_match = region.get("colorMatch", True)
            if not isinstance(color_match, bool):
                raise CompositeError(f"{identifier}.colorMatch must be true or false")
            fill_source_x = region.get("fillSourceX", x)
            fill_source_y = region.get("fillSourceY", y)
            fill_source_x = require_int(fill_source_x, f"{identifier}.fillSourceX")
            fill_source_y = require_int(fill_source_y, f"{identifier}.fillSourceY")
            if fill_source_x + region_width > width or fill_source_y + region_height > height:
                raise CompositeError(f"{identifier} fill source lies outside the {width}x{height} canvas")
            normalized_regions.append(
                {
                    "id": identifier,
                    "shape": shape,
                    "x": x,
                    "y": y,
                    "width": region_width,
                    "height": region_height,
                    "colorMatch": color_match,
                    "fillSourceX": fill_source_x,
                    "fillSourceY": fill_source_y,
                }
            )
        elif shape == "polygon":
            raw_points = region.get("points")
            if not isinstance(raw_points, list) or len(raw_points) < 3:
                raise CompositeError(f"{identifier}.points must contain at least three points")
            points: list[tuple[float, float]] = []
            for point_index, point in enumerate(raw_points):
                if not isinstance(point, Sequence) or len(point) != 2:
                    raise CompositeError(f"{identifier}.points[{point_index}] must contain x and y")
                point_x = require_number(point[0], f"{identifier}.points[{point_index}][0]")
                point_y = require_number(point[1], f"{identifier}.points[{point_index}][1]")
                if point_x < 0 or point_y < 0 or point_x > width or point_y > height:
                    raise CompositeError(f"{identifier}.points[{point_index}] lies outside the canvas")
                points.append((point_x, point_y))
            normalized_regions.append({"id": identifier, "shape": shape, "points": points})
        else:
            raise CompositeError(f"{identifier}.shape must be rectangle, ellipse, or polygon")
    return {
        "assetId": str(raw.get("assetId") or spec_path.stem),
        "canvas": {"width": width, "height": height},
        "borderSamplePixels": border,
        "inwardFeatherPixels": feather,
        "regions": normalized_regions,
    }


def normalize_fill(fill: Image.Image, width: int, height: int) -> Image.Image:
    fill = fill.convert("RGBA")
    if fill.size == (width, height):
        return fill
    return fill.resize((width, height), Image.Resampling.LANCZOS)


def region_mask(width: int, height: int, region: Mapping[str, Any]) -> Image.Image:
    mask = Image.new("L", (width, height), 0)
    draw = ImageDraw.Draw(mask)
    shape = region["shape"]
    if shape in {"rectangle", "ellipse"}:
        x = int(region["x"])
        y = int(region["y"])
        right = x + int(region["width"]) - 1
        bottom = y + int(region["height"]) - 1
        if shape == "rectangle":
            draw.rectangle((x, y, right, bottom), fill=255)
        else:
            draw.ellipse((x, y, right, bottom), fill=255)
    else:
        draw.polygon([(round(x), round(y)) for x, y in region["points"]], fill=255)
    return mask


def inward_feather_mask(width: int, height: int, feather: int) -> Image.Image:
    mask = Image.new("L", (width, height), 255)
    if feather <= 0:
        return mask
    pixels = mask.load()
    for y in range(height):
        for x in range(width):
            distance = min(x, y, width - 1 - x, height - 1 - y)
            pixels[x, y] = min(255, round(255 * (distance + 1) / (feather + 1)))
    return mask


def border_deltas(reference: Image.Image, fill: Image.Image, border: int) -> tuple[int, int, int]:
    width, height = reference.size
    border = max(1, min(border, max(1, width // 2), max(1, height // 2)))
    ref_pixels = reference.load()
    fill_pixels = fill.load()
    deltas: list[list[int]] = [[], [], []]
    for y in range(height):
        for x in range(width):
            if x >= border and y >= border and x < width - border and y < height - border:
                continue
            ref_pixel = ref_pixels[x, y]
            fill_pixel = fill_pixels[x, y]
            if ref_pixel[3] == 0 or fill_pixel[3] == 0:
                continue
            for channel in range(3):
                deltas[channel].append(int(ref_pixel[channel]) - int(fill_pixel[channel]))
    return tuple(round(statistics.median(values)) if values else 0 for values in deltas)  # type: ignore[return-value]


def shifted_patch(fill: Image.Image, delta: tuple[int, int, int]) -> Image.Image:
    source = fill.convert("RGBA")
    output = Image.new("RGBA", source.size)
    output.putdata(
        [
            (
                max(0, min(255, pixel[0] + delta[0])),
                max(0, min(255, pixel[1] + delta[1])),
                max(0, min(255, pixel[2] + delta[2])),
                pixel[3],
            )
            for pixel in source.get_flattened_data()
        ]
    )
    return output


def compose(reference: Image.Image, fill: Image.Image, parameters: Mapping[str, Any]) -> tuple[Image.Image, Image.Image, list[Mapping[str, Any]]]:
    width = int(parameters["canvas"]["width"])
    height = int(parameters["canvas"]["height"])
    if reference.size != (width, height):
        raise CompositeError(f"Approved reference is {reference.width}x{reference.height}; expected {width}x{height}")
    normalized_fill = normalize_fill(fill, width, height)
    composite = reference.convert("RGBA").copy()
    union = Image.new("L", (width, height), 0)
    region_evidence: list[Mapping[str, Any]] = []
    for region in parameters["regions"]:
        mask = region_mask(width, height, region)
        union = ImageChops.lighter(union, mask)
        if region["shape"] == "rectangle":
            x = int(region["x"])
            y = int(region["y"])
            region_width = int(region["width"])
            region_height = int(region["height"])
            box = (x, y, x + region_width, y + region_height)
            reference_patch = reference.crop(box).convert("RGBA")
            fill_source_x = int(region.get("fillSourceX", x))
            fill_source_y = int(region.get("fillSourceY", y))
            fill_patch = normalized_fill.crop(
                (
                    fill_source_x,
                    fill_source_y,
                    fill_source_x + region_width,
                    fill_source_y + region_height,
                )
            ).convert("RGBA")
            delta = (
                border_deltas(reference_patch, fill_patch, int(parameters["borderSamplePixels"]))
                if bool(region.get("colorMatch", True))
                else (0, 0, 0)
            )
            patch = shifted_patch(fill_patch, delta)
            feather = inward_feather_mask(region_width, region_height, int(parameters["inwardFeatherPixels"]))
            current = composite.crop(box)
            composite.paste(Image.composite(patch, current, feather), box)
            region_evidence.append(
                {
                    "id": region["id"],
                    "shape": region["shape"],
                    "colorMatch": bool(region.get("colorMatch", True)),
                    "fillSource": {"x": fill_source_x, "y": fill_source_y},
                    "rgbMedianShift": list(delta),
                }
            )
        else:
            composite = Image.composite(normalized_fill, composite, mask)
            region_evidence.append({"id": region["id"], "shape": region["shape"], "rgbMedianShift": [0, 0, 0]})
    return composite, union, region_evidence


def fixed_metrics(reference: Image.Image, composite: Image.Image, dynamic_mask: Image.Image) -> Mapping[str, Any]:
    reference_pixels = reference.convert("RGBA").load()
    composite_pixels = composite.convert("RGBA").load()
    mask_pixels = dynamic_mask.load()
    fixed_count = exact_count = absolute_sum = maximum = 0
    width, height = reference.size
    for y in range(height):
        for x in range(width):
            if mask_pixels[x, y] != 0:
                continue
            fixed_count += 1
            ref_pixel = reference_pixels[x, y]
            out_pixel = composite_pixels[x, y]
            differences = [abs(int(ref_pixel[channel]) - int(out_pixel[channel])) for channel in range(4)]
            if max(differences) == 0:
                exact_count += 1
            absolute_sum += sum(differences)
            maximum = max(maximum, *differences)
    ratio = exact_count / fixed_count if fixed_count else 1.0
    mean = absolute_sum / (fixed_count * 4 * 255) if fixed_count else 0.0
    return {
        "fixedPixelCount": fixed_count,
        "exactFixedPixelCount": exact_count,
        "exactFixedPixelRatio": round(ratio, 12),
        "meanAbsoluteFixedChannelDifference": round(mean, 12),
        "maximumFixedChannelDifference": maximum,
    }


def fixed_difference_artifact(reference: Image.Image, composite: Image.Image, dynamic_mask: Image.Image) -> Image.Image:
    difference = ImageChops.difference(reference.convert("RGBA"), composite.convert("RGBA"))
    outside = ImageOps.invert(dynamic_mask)
    red = Image.new("RGBA", reference.size, (255, 32, 32, 0))
    magnitude = difference.convert("RGB").convert("L")
    alpha = ImageChops.multiply(magnitude.point(lambda value: min(255, value * 4)), outside)
    red.putalpha(alpha)
    return red


def fixed_overlay(reference: Image.Image, dynamic_mask: Image.Image) -> Image.Image:
    """Return approved pixels outside the mask and transparent apertures inside it."""

    output = reference.convert("RGBA").copy()
    fixed_alpha = ImageChops.multiply(output.getchannel("A"), ImageOps.invert(dynamic_mask))
    output.putalpha(fixed_alpha)
    return output


def alpha_metrics(image: Image.Image) -> Mapping[str, Any]:
    histogram = image.convert("RGBA").getchannel("A").histogram()
    total = image.width * image.height
    transparent = histogram[0]
    opaque = histogram[255]
    partial = total - transparent - opaque
    return {
        "pixelCount": total,
        "transparentPixelCount": transparent,
        "partiallyTransparentPixelCount": partial,
        "opaquePixelCount": opaque,
        "transparentPixelRatio": round(transparent / total, 12) if total else 0,
        "partiallyTransparentPixelRatio": round(partial / total, 12) if total else 0,
        "opaquePixelRatio": round(opaque / total, 12) if total else 0,
    }


def write_json(path: Path, value: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def run(spec_path: Path, output_path: Path, evidence_dir: Path, mode: str = "composite") -> Mapping[str, Any]:
    spec_path = spec_path.resolve()
    output_path = output_path.resolve()
    evidence_dir = evidence_dir.resolve()
    raw = read_json(spec_path)
    parameters = normalize_spec(spec_path, raw)
    reference_path = resolve_asset(spec_path, raw.get("approvedReference") or {}, "approvedReference")
    fill_path = resolve_asset(spec_path, raw.get("imageToImageFill") or {}, "imageToImageFill")
    reference = Image.open(reference_path).convert("RGBA")
    fill = Image.open(fill_path).convert("RGBA")
    composite, mask, region_evidence = compose(reference, fill, parameters)
    if mode == "fixed-overlay":
        output = fixed_overlay(reference, mask)
    elif mode == "composite":
        output = composite
    else:
        raise CompositeError(f"Unsupported output mode {mode!r}")
    metrics = fixed_metrics(reference, output, mask)
    if metrics["exactFixedPixelRatio"] != 1 or metrics["meanAbsoluteFixedChannelDifference"] != 0 or metrics["maximumFixedChannelDifference"] != 0:
        raise CompositeError(f"Fixed-region equality failed: {metrics}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    evidence_dir.mkdir(parents=True, exist_ok=True)
    output.save(output_path)
    mask_path = evidence_dir / "dynamic-region-mask.png"
    difference_path = evidence_dir / "fixed-region-difference.png"
    composite_copy_path = evidence_dir / ("exact-fixed-overlay.png" if mode == "fixed-overlay" else "exact-fixed-composite.png")
    mask.save(mask_path)
    fixed_difference_artifact(reference, output, mask).save(difference_path)
    if composite_copy_path != output_path:
        output.save(composite_copy_path)

    evidence = {
        "schema": EVIDENCE_SCHEMA,
        "toolVersion": TOOL_VERSION,
        "mode": mode,
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "assetId": parameters["assetId"],
        "specPath": str(spec_path),
        "specSha256": sha256(spec_path),
        "approvedReference": {"path": str(reference_path), "sha256": sha256(reference_path)},
        "imageToImageFill": {"path": str(fill_path), "sha256": sha256(fill_path), "sourceSize": list(fill.size)},
        "canvas": parameters["canvas"],
        "normalization": "exact canvas or Lanczos stretch to the approved-reference canvas",
        "policy": "Every pixel outside the union of declared dynamic masks is copied byte-for-byte from the approved reference.",
        "rectangularFillMode": {
            "type": "median-border color-matched image-to-image patch",
            "borderSamplePixels": parameters["borderSamplePixels"],
            "inwardFeatherPixels": parameters["inwardFeatherPixels"],
        },
        "regions": region_evidence,
        "metrics": metrics,
        "alpha": alpha_metrics(output),
        "artifacts": {
            "output": {"path": str(output_path), "sha256": sha256(output_path)},
            "dynamicMask": {"path": str(mask_path), "sha256": sha256(mask_path)},
            "fixedDifference": {"path": str(difference_path), "sha256": sha256(difference_path)},
            "evidenceComposite": {"path": str(composite_copy_path), "sha256": sha256(composite_copy_path)},
        },
        "ok": True,
    }
    evidence_path = evidence_dir / "fixed-region-composite-evidence.json"
    write_json(evidence_path, evidence)
    return {"ok": True, "outputPath": str(output_path), "evidencePath": str(evidence_path), "metrics": metrics}


def self_test() -> Mapping[str, Any]:
    with tempfile.TemporaryDirectory(prefix="reign-fixed-composite-") as temporary:
        root = Path(temporary)
        reference = Image.new("RGBA", (80, 60), (18, 18, 17, 255))
        draw = ImageDraw.Draw(reference)
        draw.rectangle((2, 2, 77, 57), outline=(126, 106, 77, 255), width=2)
        draw.text((15, 13), "DYNAMIC", fill=(197, 172, 131, 255))
        fill = Image.new("RGBA", (40, 30), (12, 12, 11, 255))
        fill_draw = ImageDraw.Draw(fill)
        fill_draw.rectangle((1, 1, 38, 28), outline=(53, 44, 32, 255), width=1)
        reference_path = root / "reference.png"
        fill_path = root / "fill.png"
        reference.save(reference_path)
        fill.save(fill_path)
        spec = {
            "schema": SCHEMA,
            "assetId": "self-test",
            "approvedReference": {"path": "reference.png", "sha256": sha256(reference_path)},
            "imageToImageFill": {"path": "fill.png", "sha256": sha256(fill_path)},
            "canvas": {"width": 80, "height": 60},
            "rectangularFillMode": {"borderSamplePixels": 2, "inwardFeatherPixels": 2},
            "sceneDependentMasks": [
                {
                    "id": "text",
                    "shape": "rectangle",
                    "x": 10,
                    "y": 9,
                    "width": 51,
                    "height": 20,
                    "colorMatch": False,
                    "fillSourceX": 20,
                    "fillSourceY": 30,
                },
                {"id": "portrait", "shape": "ellipse", "x": 55, "y": 31, "width": 15, "height": 18},
                {"id": "banner", "shape": "polygon", "points": [[12, 34], [30, 34], [30, 48], [21, 54], [12, 48]]},
            ],
        }
        spec_path = root / "spec.json"
        write_json(spec_path, spec)
        result = run(spec_path, root / "output.png", root / "evidence")
        overlay_result = run(spec_path, root / "overlay.png", root / "overlay-evidence", mode="fixed-overlay")
        evidence = read_json(Path(result["evidencePath"]))
        overlay_evidence = read_json(Path(overlay_result["evidencePath"]))
        disabled_match_region = next(region for region in evidence["regions"] if region["id"] == "text")
        return {
            "schema": "reign-fixed-region-composite-self-test-v1",
            "toolVersion": TOOL_VERSION,
            "ok": bool(
                result["ok"]
                and result["metrics"]["exactFixedPixelRatio"] == 1
                and disabled_match_region["colorMatch"] is False
                and disabled_match_region["rgbMedianShift"] == [0, 0, 0]
                and disabled_match_region["fillSource"] == {"x": 20, "y": 30}
                and overlay_result["metrics"]["exactFixedPixelRatio"] == 1
                and overlay_evidence["mode"] == "fixed-overlay"
                and overlay_evidence["alpha"]["transparentPixelCount"] > 0
                and overlay_evidence["alpha"]["opaquePixelCount"] > 0
                and overlay_evidence["alpha"]["partiallyTransparentPixelCount"] == 0
            ),
            "metrics": result["metrics"],
            "overlayMetrics": overlay_result["metrics"],
            "overlayAlpha": overlay_evidence["alpha"],
        }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spec", type=Path, help="fixed-region composite JSON spec")
    parser.add_argument("--output", type=Path, help="production composite PNG")
    parser.add_argument("--evidence-dir", type=Path, help="directory for mask, diff, composite, and JSON evidence")
    parser.add_argument(
        "--mode",
        choices=("composite", "fixed-overlay"),
        default="composite",
        help="emit an opaque I2I-filled shell or an approved-reference fixed overlay with transparent declared apertures",
    )
    parser.add_argument("--self-test", action="store_true", help="run deterministic isolated coverage")
    args = parser.parse_args()
    if args.self_test:
        result = self_test()
    else:
        if not args.spec or not args.output or not args.evidence_dir:
            parser.error("--spec, --output, and --evidence-dir are required unless --self-test is used")
        result = run(args.spec, args.output, args.evidence_dir, mode=args.mode)
    print(json.dumps(result, indent=2))
    if not result.get("ok"):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
