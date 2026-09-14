#!/usr/bin/env python3
"""Normalize generated PNG artwork into auditable Reign UI overlays.

This utility is deliberately non-creative.  It performs only deterministic
production operations described by a JSON spec: resize/crop/pad a source PNG,
optionally replace or remove generator-baked neutral light mattes, normalize a
bounded neutral-tone range to an exact palette token, suppress high-frequency
marble contrast, and cut one or more antialiased transparent apertures into the
result.

The output is always a true RGBA PNG.  A sidecar JSON record captures the input,
spec, and output hashes; exact effective parameters; dimensions; and alpha
statistics.  Existing output or evidence files are never replaced unless the
caller explicitly supplies ``--force``.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any, Dict, Mapping, Sequence, Tuple

try:
    from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, __version__ as PILLOW_VERSION
except ImportError:  # Keep --help usable when Pillow has not been installed.
    Image = None  # type: ignore[assignment]
    ImageChops = None  # type: ignore[assignment]
    ImageDraw = None  # type: ignore[assignment]
    ImageFilter = None  # type: ignore[assignment]
    ImageOps = None  # type: ignore[assignment]
    PILLOW_VERSION = "unavailable"


TOOL_VERSION = "1.5.0"
SPEC_SCHEMA = "reign-overlay-asset-spec-v1"
EVIDENCE_SCHEMA = "reign-overlay-asset-evidence-v1"
SUPPORTED_FITS = {"exact", "contain", "cover", "stretch"}
SUPPORTED_ANCHORS = {
    "top-left",
    "top",
    "top-right",
    "left",
    "center",
    "right",
    "bottom-left",
    "bottom",
    "bottom-right",
}
SUPPORTED_SHAPES = {"circle", "ellipse", "oval", "rectangle", "rounded_rectangle", "polygon"}
SUPPORTED_APERTURE_EDGE_MODES = {"antialiased", "hard"}


class PreparationError(RuntimeError):
    """Raised when an asset or declarative production parameter is invalid."""


def _require_pillow() -> None:
    if Image is None:
        raise PreparationError(
            "Pillow is required for PNG preparation. Install it with "
            "'python -m pip install Pillow' in the approved workspace environment."
        )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise PreparationError(f"{label} must be a finite number")
    result = float(value)
    if not math.isfinite(result):
        raise PreparationError(f"{label} must be a finite number")
    return result


def _positive_int(value: Any, label: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise PreparationError(f"{label} must be a positive integer")
    return value


def _rgb_hex(value: Any, label: str) -> Tuple[int, int, int]:
    if not isinstance(value, str):
        raise PreparationError(f"{label} must be a #RRGGBB color")
    normalized = value.strip()
    if len(normalized) != 7 or not normalized.startswith("#"):
        raise PreparationError(f"{label} must be a #RRGGBB color")
    try:
        channels = tuple(int(normalized[index : index + 2], 16) for index in (1, 3, 5))
    except ValueError as exc:
        raise PreparationError(f"{label} must be a #RRGGBB color") from exc
    return channels  # type: ignore[return-value]


def _canonicalize_axis_warp(raw: Any) -> Dict[str, list[Dict[str, float]]] | None:
    if raw is None:
        return None
    if not isinstance(raw, dict):
        raise PreparationError("normalization.axisWarp must be a JSON object")
    result: Dict[str, list[Dict[str, float]]] = {}
    for axis in ("x", "y"):
        points = raw.get(axis)
        if not isinstance(points, list) or len(points) < 2:
            raise PreparationError(
                f"normalization.axisWarp.{axis} must contain at least two control points"
            )
        canonical: list[Dict[str, float]] = []
        for index, point in enumerate(points):
            label = f"normalization.axisWarp.{axis}[{index}]"
            if not isinstance(point, dict):
                raise PreparationError(f"{label} must be a JSON object")
            canonical.append(
                {
                    "source": _number(point.get("source"), f"{label}.source"),
                    "destination": _number(
                        point.get("destination"), f"{label}.destination"
                    ),
                }
            )
        for previous, current in zip(canonical, canonical[1:]):
            if current["source"] <= previous["source"]:
                raise PreparationError(
                    f"normalization.axisWarp.{axis} source coordinates must increase"
                )
            if current["destination"] <= previous["destination"]:
                raise PreparationError(
                    f"normalization.axisWarp.{axis} destination coordinates must increase"
                )
        result[axis] = canonical
    return result


def _load_json_object(path: Path) -> Dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8") as stream:
            value = json.load(stream)
    except (OSError, json.JSONDecodeError) as exc:
        raise PreparationError(f"Unable to read JSON spec '{path}': {exc}") from exc
    if not isinstance(value, dict):
        raise PreparationError("The overlay spec root must be a JSON object")
    return value


def _canonicalize_aperture(
    raw: Any,
    index: int,
    canvas_width: int,
    canvas_height: int,
) -> Dict[str, Any]:
    label = f"apertures[{index}]"
    if not isinstance(raw, dict):
        raise PreparationError(f"{label} must be a JSON object")

    identifier = raw.get("id", f"aperture-{index + 1}")
    if not isinstance(identifier, str) or not identifier.strip():
        raise PreparationError(f"{label}.id must be a non-empty string")

    shape = raw.get("shape")
    if not isinstance(shape, str) or shape not in SUPPORTED_SHAPES:
        choices = ", ".join(sorted(SUPPORTED_SHAPES))
        raise PreparationError(f"{label}.shape must be one of: {choices}")

    polygon_points: list[list[float]] | None = None
    if shape == "polygon":
        raw_points = raw.get("points")
        if not isinstance(raw_points, list) or len(raw_points) < 3:
            raise PreparationError(f"{label}.points must contain at least three [x, y] pairs")
        polygon_points = []
        for point_index, point in enumerate(raw_points):
            point_label = f"{label}.points[{point_index}]"
            if not isinstance(point, list) or len(point) != 2:
                raise PreparationError(f"{point_label} must be a two-value [x, y] array")
            point_x = _number(point[0], f"{point_label}[0]")
            point_y = _number(point[1], f"{point_label}[1]")
            if point_x < 0 or point_y < 0 or point_x > canvas_width or point_y > canvas_height:
                raise PreparationError(f"{point_label} must lie inside the canvas")
            polygon_points.append([point_x, point_y])
        doubled_area = abs(
            sum(
                current[0] * following[1] - following[0] * current[1]
                for current, following in zip(
                    polygon_points, polygon_points[1:] + polygon_points[:1]
                )
            )
        )
        if doubled_area <= 1e-6:
            raise PreparationError(f"{label}.points describe a zero-area polygon")
        x = min(point[0] for point in polygon_points)
        y = min(point[1] for point in polygon_points)
        right = max(point[0] for point in polygon_points)
        bottom = max(point[1] for point in polygon_points)
        width = right - x
        height = bottom - y
    else:
        x = _number(raw.get("x"), f"{label}.x")
        y = _number(raw.get("y"), f"{label}.y")
        width = _number(raw.get("width"), f"{label}.width")
        height = _number(raw.get("height"), f"{label}.height")
    if width <= 0 or height <= 0:
        raise PreparationError(f"{label} width and height must both be greater than zero")
    if x < 0 or y < 0 or x + width > canvas_width or y + height > canvas_height:
        raise PreparationError(
            f"{label} must lie fully inside the {canvas_width}x{canvas_height} canvas"
        )

    if shape == "circle" and not math.isclose(width, height, rel_tol=0.0, abs_tol=1e-6):
        raise PreparationError(f"{label} uses shape 'circle' but width and height differ")

    feather = _number(raw.get("feather", 0.0), f"{label}.feather")
    if feather < 0 or feather > 32:
        raise PreparationError(f"{label}.feather must be between 0 and 32 pixels")

    canonical: Dict[str, Any] = {
        "id": identifier.strip(),
        "shape": shape,
        "x": x,
        "y": y,
        "width": width,
        "height": height,
        "feather": feather,
    }
    if polygon_points is not None:
        canonical["points"] = polygon_points

    if shape == "rounded_rectangle":
        radius = _number(raw.get("radius"), f"{label}.radius")
        if radius <= 0 or radius > min(width, height) / 2:
            raise PreparationError(
                f"{label}.radius must be greater than zero and no more than half "
                "the shorter side"
            )
        canonical["radius"] = radius
    elif "radius" in raw:
        raise PreparationError(f"{label}.radius is only valid for rounded_rectangle")

    return canonical


def _effective_parameters(
    raw_spec: Mapping[str, Any],
    marble_override: float | None,
    marble_blur_override: float | None,
    antialias_override: int | None,
) -> Dict[str, Any]:
    schema = raw_spec.get("schema", SPEC_SCHEMA)
    if schema != SPEC_SCHEMA:
        raise PreparationError(f"Unsupported spec schema '{schema}'; expected '{SPEC_SCHEMA}'")

    canvas = raw_spec.get("canvas")
    if not isinstance(canvas, dict):
        raise PreparationError("canvas must be a JSON object with integer width and height")
    canvas_width = _positive_int(canvas.get("width"), "canvas.width")
    canvas_height = _positive_int(canvas.get("height"), "canvas.height")

    normalization = raw_spec.get("normalization", {})
    if not isinstance(normalization, dict):
        raise PreparationError("normalization must be a JSON object")
    fit = normalization.get("fit", "contain")
    anchor = normalization.get("anchor", "center")
    if fit not in SUPPORTED_FITS:
        raise PreparationError(f"normalization.fit must be one of: {', '.join(sorted(SUPPORTED_FITS))}")
    if anchor not in SUPPORTED_ANCHORS:
        raise PreparationError(
            f"normalization.anchor must be one of: {', '.join(sorted(SUPPORTED_ANCHORS))}"
        )
    source_crop = normalization.get("sourceCrop")
    canonical_crop: Dict[str, float] | None = None
    if source_crop is not None:
        if not isinstance(source_crop, dict):
            raise PreparationError("normalization.sourceCrop must be a JSON object")
        canonical_crop = {
            "x": _number(source_crop.get("x"), "normalization.sourceCrop.x"),
            "y": _number(source_crop.get("y"), "normalization.sourceCrop.y"),
            "width": _number(source_crop.get("width"), "normalization.sourceCrop.width"),
            "height": _number(source_crop.get("height"), "normalization.sourceCrop.height"),
        }
        if canonical_crop["x"] < 0 or canonical_crop["y"] < 0:
            raise PreparationError("normalization.sourceCrop x and y may not be negative")
        if canonical_crop["width"] <= 0 or canonical_crop["height"] <= 0:
            raise PreparationError("normalization.sourceCrop width and height must be positive")
    axis_warp = _canonicalize_axis_warp(normalization.get("axisWarp"))
    if axis_warp is not None and fit != "stretch":
        raise PreparationError("normalization.axisWarp requires normalization.fit='stretch'")

    antialias_scale = (
        antialias_override
        if antialias_override is not None
        else raw_spec.get("antialiasScale", 4)
    )
    antialias_scale = _positive_int(antialias_scale, "antialiasScale")
    if antialias_scale < 2 or antialias_scale > 16:
        raise PreparationError("antialiasScale must be between 2 and 16")

    raw_apertures = raw_spec.get("apertures")
    allow_no_apertures = raw_spec.get("allowNoApertures", False)
    if not isinstance(allow_no_apertures, bool):
        raise PreparationError("allowNoApertures must be true or false")
    if not isinstance(raw_apertures, list) or (not raw_apertures and not allow_no_apertures):
        raise PreparationError(
            "apertures must be a non-empty JSON array unless allowNoApertures is true"
        )
    apertures = [
        _canonicalize_aperture(value, index, canvas_width, canvas_height)
        for index, value in enumerate(raw_apertures)
    ]
    identifiers = [item["id"] for item in apertures]
    if len(set(identifiers)) != len(identifiers):
        raise PreparationError("Every aperture id must be unique")

    aperture_edge_mode = raw_spec.get("apertureEdgeMode", "antialiased")
    if aperture_edge_mode not in SUPPORTED_APERTURE_EDGE_MODES:
        raise PreparationError(
            "apertureEdgeMode must be one of: "
            + ", ".join(sorted(SUPPORTED_APERTURE_EDGE_MODES))
        )
    if aperture_edge_mode == "hard" and any(item["feather"] > 0 for item in apertures):
        raise PreparationError("apertureEdgeMode='hard' may not be combined with feathered apertures")

    raw_frame_preservation = raw_spec.get("apertureFramePreservation")
    frame_preservation: Dict[str, Any] | None = None
    if raw_frame_preservation is not None:
        if not isinstance(raw_frame_preservation, dict):
            raise PreparationError("apertureFramePreservation must be a JSON object")
        minimum_red = _number(
            raw_frame_preservation.get("minimumRed", 45),
            "apertureFramePreservation.minimumRed",
        )
        minimum_red_minus_blue = _number(
            raw_frame_preservation.get("minimumRedMinusBlue", 8),
            "apertureFramePreservation.minimumRedMinusBlue",
        )
        minimum_green_minus_blue = _number(
            raw_frame_preservation.get("minimumGreenMinusBlue", 3),
            "apertureFramePreservation.minimumGreenMinusBlue",
        )
        for label, value in (
            ("minimumRed", minimum_red),
            ("minimumRedMinusBlue", minimum_red_minus_blue),
            ("minimumGreenMinusBlue", minimum_green_minus_blue),
        ):
            if not 0 <= value <= 255:
                raise PreparationError(
                    f"apertureFramePreservation.{label} must be between 0 and 255"
                )
        frame_preservation = {
            "minimumRed": float(minimum_red),
            "minimumRedMinusBlue": float(minimum_red_minus_blue),
            "minimumGreenMinusBlue": float(minimum_green_minus_blue),
        }
        dark_pixel_maximum = _number(
            raw_frame_preservation.get("darkPixelMaximum", 0),
            "apertureFramePreservation.darkPixelMaximum",
        )
        if not 0 <= dark_pixel_maximum <= 255:
            raise PreparationError(
                "apertureFramePreservation.darkPixelMaximum must be between 0 and 255"
            )
        frame_preservation["darkPixelMaximum"] = float(dark_pixel_maximum)
        raw_geometric_band = raw_frame_preservation.get("geometricBand")
        if raw_geometric_band is not None:
            if not isinstance(raw_geometric_band, dict):
                raise PreparationError(
                    "apertureFramePreservation.geometricBand must be a JSON object"
                )
            outer = _canonicalize_aperture(
                raw_geometric_band.get("outer"),
                0,
                canvas_width,
                canvas_height,
            )
            inner = _canonicalize_aperture(
                raw_geometric_band.get("inner"),
                1,
                canvas_width,
                canvas_height,
            )
            if outer["shape"] not in {"circle", "ellipse", "oval"}:
                raise PreparationError(
                    "apertureFramePreservation.geometricBand.outer must be an oval"
                )
            if inner["shape"] not in {"circle", "ellipse", "oval"}:
                raise PreparationError(
                    "apertureFramePreservation.geometricBand.inner must be an oval"
                )
            if not (
                inner["x"] >= outer["x"]
                and inner["y"] >= outer["y"]
                and inner["x"] + inner["width"] <= outer["x"] + outer["width"]
                and inner["y"] + inner["height"] <= outer["y"] + outer["height"]
            ):
                raise PreparationError(
                    "apertureFramePreservation.geometricBand.inner must lie inside outer"
                )
            frame_preservation["geometricBand"] = {
                "outer": outer,
                "inner": inner,
                "antialiasScale": antialias_scale,
            }

    raw_drawn_frames = raw_spec.get("drawnFrames", [])
    if not isinstance(raw_drawn_frames, list):
        raise PreparationError("drawnFrames must be a JSON array")
    drawn_frames: list[Dict[str, Any]] = []
    for frame_index, raw_frame in enumerate(raw_drawn_frames):
        if not isinstance(raw_frame, dict):
            raise PreparationError(f"drawnFrames[{frame_index}] must be a JSON object")
        frame = _canonicalize_aperture(
            raw_frame,
            frame_index,
            canvas_width,
            canvas_height,
        )
        if frame["shape"] not in {"circle", "ellipse", "oval"}:
            raise PreparationError(f"drawnFrames[{frame_index}] must be an oval")
        raw_strokes = raw_frame.get("strokes")
        if not isinstance(raw_strokes, list) or not raw_strokes:
            raise PreparationError(
                f"drawnFrames[{frame_index}].strokes must be a non-empty JSON array"
            )
        strokes: list[Dict[str, Any]] = []
        for stroke_index, raw_stroke in enumerate(raw_strokes):
            label = f"drawnFrames[{frame_index}].strokes[{stroke_index}]"
            if not isinstance(raw_stroke, dict):
                raise PreparationError(f"{label} must be a JSON object")
            inset = _number(raw_stroke.get("inset", 0), f"{label}.inset")
            width = _number(raw_stroke.get("width"), f"{label}.width")
            if inset < 0 or width <= 0:
                raise PreparationError(f"{label} inset must be non-negative and width positive")
            if inset * 2 >= min(frame["width"], frame["height"]):
                raise PreparationError(f"{label}.inset collapses the oval")
            color = _rgb_hex(raw_stroke.get("color"), f"{label}.color")
            strokes.append(
                {
                    "inset": float(inset),
                    "width": float(width),
                    "color": "#{:02X}{:02X}{:02X}".format(*color),
                }
            )
        frame["strokes"] = strokes
        drawn_frames.append(frame)

    transparent_rgb = _rgb_hex(
        raw_spec.get("transparentRgb", "#121211"),
        "transparentRgb",
    )

    raw_light_matte = raw_spec.get("lightNeutralMatteReplacement")
    light_matte: Dict[str, Any] | None = None
    if raw_light_matte is not None:
        if not isinstance(raw_light_matte, dict):
            raise PreparationError("lightNeutralMatteReplacement must be a JSON object")
        minimum_channel = _number(
            raw_light_matte.get("minimumChannel", 205),
            "lightNeutralMatteReplacement.minimumChannel",
        )
        maximum_chroma = _number(
            raw_light_matte.get("maximumChroma", 18),
            "lightNeutralMatteReplacement.maximumChroma",
        )
        if not 0 <= minimum_channel <= 255:
            raise PreparationError(
                "lightNeutralMatteReplacement.minimumChannel must be between 0 and 255"
            )
        if not 0 <= maximum_chroma <= 255:
            raise PreparationError(
                "lightNeutralMatteReplacement.maximumChroma must be between 0 and 255"
            )
        replacement = _rgb_hex(
            raw_light_matte.get("color", "#121211"),
            "lightNeutralMatteReplacement.color",
        )
        light_matte = {
            "minimumChannel": float(minimum_channel),
            "maximumChroma": float(maximum_chroma),
            "color": "#{:02X}{:02X}{:02X}".format(*replacement),
        }

    raw_light_matte_alpha = raw_spec.get("lightNeutralMatteAlphaRemoval")
    light_matte_alpha: Dict[str, Any] | None = None
    if raw_light_matte_alpha is not None:
        if light_matte is not None:
            raise PreparationError(
                "lightNeutralMatteReplacement and lightNeutralMatteAlphaRemoval are mutually exclusive"
            )
        if not isinstance(raw_light_matte_alpha, dict):
            raise PreparationError("lightNeutralMatteAlphaRemoval must be a JSON object")
        minimum_channel = _number(
            raw_light_matte_alpha.get("minimumChannel", 205),
            "lightNeutralMatteAlphaRemoval.minimumChannel",
        )
        maximum_chroma = _number(
            raw_light_matte_alpha.get("maximumChroma", 18),
            "lightNeutralMatteAlphaRemoval.maximumChroma",
        )
        if not 0 <= minimum_channel <= 255:
            raise PreparationError(
                "lightNeutralMatteAlphaRemoval.minimumChannel must be between 0 and 255"
            )
        if not 0 <= maximum_chroma <= 255:
            raise PreparationError(
                "lightNeutralMatteAlphaRemoval.maximumChroma must be between 0 and 255"
            )
        light_matte_alpha = {
            "minimumChannel": float(minimum_channel),
            "maximumChroma": float(maximum_chroma),
        }

    raw_neutral_tone = raw_spec.get("neutralToneReplacement")
    neutral_tone: Dict[str, Any] | None = None
    if raw_neutral_tone is not None:
        if not isinstance(raw_neutral_tone, dict):
            raise PreparationError("neutralToneReplacement must be a JSON object")
        minimum_luminance = _number(
            raw_neutral_tone.get("minimumLuminance", 0),
            "neutralToneReplacement.minimumLuminance",
        )
        maximum_luminance = _number(
            raw_neutral_tone.get("maximumLuminance", 255),
            "neutralToneReplacement.maximumLuminance",
        )
        maximum_chroma = _number(
            raw_neutral_tone.get("maximumChroma", 18),
            "neutralToneReplacement.maximumChroma",
        )
        if not 0 <= minimum_luminance <= 255:
            raise PreparationError(
                "neutralToneReplacement.minimumLuminance must be between 0 and 255"
            )
        if not 0 <= maximum_luminance <= 255:
            raise PreparationError(
                "neutralToneReplacement.maximumLuminance must be between 0 and 255"
            )
        if minimum_luminance > maximum_luminance:
            raise PreparationError(
                "neutralToneReplacement.minimumLuminance may not exceed maximumLuminance"
            )
        if not 0 <= maximum_chroma <= 255:
            raise PreparationError(
                "neutralToneReplacement.maximumChroma must be between 0 and 255"
            )
        replacement = _rgb_hex(
            raw_neutral_tone.get("color", "#595956"),
            "neutralToneReplacement.color",
        )
        neutral_tone = {
            "minimumLuminance": float(minimum_luminance),
            "maximumLuminance": float(maximum_luminance),
            "maximumChroma": float(maximum_chroma),
            "color": "#{:02X}{:02X}{:02X}".format(*replacement),
        }

    raw_marble = raw_spec.get("marbleReduction", {})
    if isinstance(raw_marble, (int, float)) and not isinstance(raw_marble, bool):
        raw_marble = {"amount": raw_marble}
    if not isinstance(raw_marble, dict):
        raise PreparationError("marbleReduction must be a number or JSON object")
    amount = _number(
        marble_override if marble_override is not None else raw_marble.get("amount", 0.0),
        "marbleReduction.amount",
    )
    blur_radius = _number(
        marble_blur_override
        if marble_blur_override is not None
        else raw_marble.get("blurRadius", 6.0),
        "marbleReduction.blurRadius",
    )
    luminance_ceiling = _number(
        raw_marble.get("luminanceCeiling", 128.0),
        "marbleReduction.luminanceCeiling",
    )
    chroma_ceiling = _number(
        raw_marble.get("chromaCeiling", 48.0),
        "marbleReduction.chromaCeiling",
    )
    selection_softness = _number(
        raw_marble.get("selectionSoftness", 24.0),
        "marbleReduction.selectionSoftness",
    )
    if amount < 0 or amount > 1:
        raise PreparationError("marbleReduction.amount must be between 0 and 1")
    if blur_radius <= 0 or blur_radius > 128:
        raise PreparationError("marbleReduction.blurRadius must be greater than 0 and at most 128")
    if not 0 <= luminance_ceiling <= 255:
        raise PreparationError("marbleReduction.luminanceCeiling must be between 0 and 255")
    if not 0 <= chroma_ceiling <= 255:
        raise PreparationError("marbleReduction.chromaCeiling must be between 0 and 255")
    if not 0 <= selection_softness <= 255:
        raise PreparationError("marbleReduction.selectionSoftness must be between 0 and 255")

    return {
        "canvas": {"width": canvas_width, "height": canvas_height},
        "normalization": {
            "fit": fit,
            "anchor": anchor,
            "sourceCrop": canonical_crop,
            "axisWarp": axis_warp,
        },
        "antialiasScale": antialias_scale,
        "apertureEdgeMode": aperture_edge_mode,
        "apertureFramePreservation": frame_preservation,
        "drawnFrames": drawn_frames,
        "apertures": apertures,
        "allowNoApertures": allow_no_apertures,
        "transparentRgb": "#{:02X}{:02X}{:02X}".format(*transparent_rgb),
        "lightNeutralMatteReplacement": light_matte,
        "lightNeutralMatteAlphaRemoval": light_matte_alpha,
        "neutralToneReplacement": neutral_tone,
        "marbleReduction": {
            "amount": float(amount),
            "blurRadius": float(blur_radius),
            "luminanceCeiling": float(luminance_ceiling),
            "chromaCeiling": float(chroma_ceiling),
            "selectionSoftness": float(selection_softness),
        },
    }


def _lanczos() -> Any:
    resampling = getattr(Image, "Resampling", Image)
    return resampling.LANCZOS


def _anchor_offset(space_x: int, space_y: int, anchor: str) -> Tuple[int, int]:
    horizontal, vertical = {
        "top-left": (0.0, 0.0),
        "top": (0.5, 0.0),
        "top-right": (1.0, 0.0),
        "left": (0.0, 0.5),
        "center": (0.5, 0.5),
        "right": (1.0, 0.5),
        "bottom-left": (0.0, 1.0),
        "bottom": (0.5, 1.0),
        "bottom-right": (1.0, 1.0),
    }[anchor]
    return int(round(space_x * horizontal)), int(round(space_y * vertical))


def _normalize_image(
    source: Any,
    width: int,
    height: int,
    fit: str,
    anchor: str,
    source_crop: Mapping[str, float] | None,
    axis_warp: Mapping[str, Sequence[Mapping[str, float]]] | None,
) -> Any:
    source = source.convert("RGBA")
    if source_crop is not None:
        left = int(round(source_crop["x"]))
        top = int(round(source_crop["y"]))
        right = int(round(source_crop["x"] + source_crop["width"]))
        bottom = int(round(source_crop["y"] + source_crop["height"]))
        if right <= left or bottom <= top:
            raise PreparationError("normalization.sourceCrop rounds to an empty rectangle")
        if left < 0 or top < 0 or right > source.width or bottom > source.height:
            raise PreparationError(
                "normalization.sourceCrop must lie fully inside the decoded source image"
            )
        source = source.crop((left, top, right, bottom))
    if axis_warp is not None:
        return _piecewise_axis_warp(source, width, height, axis_warp)
    if fit == "exact":
        if source.size != (width, height):
            raise PreparationError(
                f"normalization.fit is 'exact', but source is {source.width}x{source.height} "
                f"and canvas is {width}x{height}"
            )
        return source.copy()
    if fit == "stretch":
        return source.resize((width, height), _lanczos())

    scale = (
        min(width / source.width, height / source.height)
        if fit == "contain"
        else max(width / source.width, height / source.height)
    )
    resized_size = (
        max(1, int(round(source.width * scale))),
        max(1, int(round(source.height * scale))),
    )
    resized = source.resize(resized_size, _lanczos())
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    offset = _anchor_offset(width - resized.width, height - resized.height, anchor)
    canvas.alpha_composite(resized, dest=offset)
    return canvas


def _validate_warp_endpoints(
    points: Sequence[Mapping[str, float]],
    source_extent: int,
    destination_extent: int,
    label: str,
) -> None:
    tolerance = 0.001
    if abs(points[0]["source"]) > tolerance or abs(points[0]["destination"]) > tolerance:
        raise PreparationError(f"{label} must begin at source 0 and destination 0")
    if abs(points[-1]["source"] - source_extent) > tolerance:
        raise PreparationError(f"{label} final source must equal {source_extent}")
    if abs(points[-1]["destination"] - destination_extent) > tolerance:
        raise PreparationError(f"{label} final destination must equal {destination_extent}")


def _piecewise_axis_warp(
    source: Any,
    width: int,
    height: int,
    axis_warp: Mapping[str, Sequence[Mapping[str, float]]],
) -> Any:
    """Apply an auditable separable piecewise-linear registration warp."""

    x_points = axis_warp["x"]
    y_points = axis_warp["y"]
    _validate_warp_endpoints(x_points, source.width, width, "normalization.axisWarp.x")
    _validate_warp_endpoints(y_points, source.height, height, "normalization.axisWarp.y")

    horizontal = Image.new("RGBA", (width, source.height), (0, 0, 0, 0))
    for start, end in zip(x_points, x_points[1:]):
        source_left = int(round(start["source"]))
        source_right = int(round(end["source"]))
        destination_left = int(round(start["destination"]))
        destination_right = int(round(end["destination"]))
        if source_right <= source_left or destination_right <= destination_left:
            raise PreparationError("normalization.axisWarp.x rounds to an empty segment")
        tile = source.crop((source_left, 0, source_right, source.height)).resize(
            (destination_right - destination_left, source.height), _lanczos()
        )
        horizontal.alpha_composite(tile, dest=(destination_left, 0))

    result = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for start, end in zip(y_points, y_points[1:]):
        source_top = int(round(start["source"]))
        source_bottom = int(round(end["source"]))
        destination_top = int(round(start["destination"]))
        destination_bottom = int(round(end["destination"]))
        if source_bottom <= source_top or destination_bottom <= destination_top:
            raise PreparationError("normalization.axisWarp.y rounds to an empty segment")
        tile = horizontal.crop((0, source_top, width, source_bottom)).resize(
            (width, destination_bottom - destination_top), _lanczos()
        )
        result.alpha_composite(tile, dest=(0, destination_top))
    return result


def _soft_below_lut(ceiling: float, softness: float) -> list[int]:
    lower = ceiling - softness
    values: list[int] = []
    for value in range(256):
        if value >= ceiling:
            values.append(0)
        elif softness == 0 or value <= lower:
            values.append(255)
        else:
            values.append(int(round(255 * (ceiling - value) / softness)))
    return values


def _apply_marble_reduction(
    image: Any,
    amount: float,
    blur_radius: float,
    luminance_ceiling: float,
    chroma_ceiling: float,
    selection_softness: float,
) -> Any:
    if amount == 0:
        return image.copy()
    alpha = image.getchannel("A")
    rgb = image.convert("RGB")
    low_frequency = rgb.filter(ImageFilter.GaussianBlur(radius=blur_radius))
    red, green, blue = rgb.split()
    maximum = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    minimum = ImageChops.darker(ImageChops.darker(red, green), blue)
    chroma = ImageChops.subtract(maximum, minimum)
    luminance = ImageOps.grayscale(rgb)
    dark_selection = luminance.point(_soft_below_lut(luminance_ceiling, selection_softness))
    neutral_selection = chroma.point(_soft_below_lut(chroma_ceiling, selection_softness))
    selection = ImageChops.multiply(dark_selection, neutral_selection)
    selection = selection.point([int(round(value * amount)) for value in range(256)])
    # Restrict smoothing to dark, low-chroma material so antique-gold edges,
    # type, and ornamentation remain crisp.
    reduced = Image.composite(low_frequency, rgb, selection)
    reduced.putalpha(alpha)
    return reduced


def _replace_light_neutral_matte(
    image: Any,
    minimum_channel: float,
    maximum_chroma: float,
    color: Tuple[int, int, int],
) -> Any:
    """Replace generator-baked white/checker mattes without touching gold or dark material."""

    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")
    red, green, blue = rgb.split()
    minimum = ImageChops.darker(red, ImageChops.darker(green, blue))
    maximum = ImageChops.lighter(red, ImageChops.lighter(green, blue))
    chroma = ImageChops.subtract(maximum, minimum)
    bright = minimum.point(
        [255 if value >= minimum_channel else 0 for value in range(256)]
    )
    neutral = chroma.point(
        [255 if value <= maximum_chroma else 0 for value in range(256)]
    )
    selection = ImageChops.multiply(bright, neutral)
    replaced_rgb = Image.composite(Image.new("RGB", rgba.size, color), rgb, selection)
    result = replaced_rgb.convert("RGBA")
    result.putalpha(rgba.getchannel("A"))
    return result


def _remove_light_neutral_matte(
    image: Any,
    minimum_channel: float,
    maximum_chroma: float,
) -> Any:
    """Convert generator-baked light neutral checker/white mattes to true alpha."""

    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")
    red, green, blue = rgb.split()
    minimum = ImageChops.darker(red, ImageChops.darker(green, blue))
    maximum = ImageChops.lighter(red, ImageChops.lighter(green, blue))
    chroma = ImageChops.subtract(maximum, minimum)
    bright = minimum.point(
        [255 if value >= minimum_channel else 0 for value in range(256)]
    )
    neutral = chroma.point(
        [255 if value <= maximum_chroma else 0 for value in range(256)]
    )
    removal = ImageChops.multiply(bright, neutral)
    retained_alpha = ImageChops.multiply(
        rgba.getchannel("A"), ImageOps.invert(removal)
    )
    result = rgba.copy()
    result.putalpha(retained_alpha)
    return result


def _replace_neutral_tone_range(
    image: Any,
    minimum_luminance: float,
    maximum_luminance: float,
    maximum_chroma: float,
    color: Tuple[int, int, int],
) -> Any:
    """Map a bounded low-chroma tone range to one exact frozen palette token."""

    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")
    red, green, blue = rgb.split()
    maximum = ImageChops.lighter(red, ImageChops.lighter(green, blue))
    minimum = ImageChops.darker(red, ImageChops.darker(green, blue))
    chroma = ImageChops.subtract(maximum, minimum)
    luminance = ImageOps.grayscale(rgb)
    above_minimum = luminance.point(
        [255 if value >= minimum_luminance else 0 for value in range(256)]
    )
    below_maximum = luminance.point(
        [255 if value <= maximum_luminance else 0 for value in range(256)]
    )
    neutral = chroma.point(
        [255 if value <= maximum_chroma else 0 for value in range(256)]
    )
    selection = ImageChops.multiply(
        ImageChops.multiply(above_minimum, below_maximum), neutral
    )
    replaced_rgb = Image.composite(Image.new("RGB", rgba.size, color), rgb, selection)
    result = replaced_rgb.convert("RGBA")
    result.putalpha(rgba.getchannel("A"))
    return result


def _high_resolution_box(aperture: Mapping[str, Any], scale: int) -> Tuple[int, int, int, int]:
    left = _round_scaled_coordinate(aperture["x"], scale)
    top = _round_scaled_coordinate(aperture["y"], scale)
    right = _round_scaled_coordinate(aperture["x"] + aperture["width"], scale) - 1
    bottom = _round_scaled_coordinate(aperture["y"] + aperture["height"], scale) - 1
    return left, top, max(left, right), max(top, bottom)


def _round_scaled_coordinate(value: float, scale: int) -> int:
    """Round non-negative pixel geometry consistently across translated crops."""

    return int(math.floor(value * scale + 0.5))


def _draw_aperture(mask: Any, aperture: Mapping[str, Any], scale: int) -> None:
    """Rasterize one canonical aperture into ``mask`` at the requested scale."""

    draw = ImageDraw.Draw(mask)
    box = _high_resolution_box(aperture, scale)
    shape = aperture["shape"]
    if shape in {"circle", "ellipse", "oval"}:
        draw.ellipse(box, fill=255)
    elif shape == "rectangle":
        draw.rectangle(box, fill=255)
    elif shape == "rounded_rectangle":
        draw.rounded_rectangle(box, radius=aperture["radius"] * scale, fill=255)
    elif shape == "polygon":
        draw.polygon(
            [
                (
                    _round_scaled_coordinate(point[0], scale),
                    _round_scaled_coordinate(point[1], scale),
                )
                for point in aperture["points"]
            ],
            fill=255,
        )
    else:  # Canonicalization makes this unreachable.
        raise PreparationError(f"Unsupported canonical aperture shape '{shape}'")


def _build_aperture_mask(
    width: int,
    height: int,
    apertures: Sequence[Mapping[str, Any]],
    scale: int,
    edge_mode: str = "antialiased",
) -> Any:
    if edge_mode == "hard":
        hard_result = Image.new("L", (width, height), 0)
        for aperture in apertures:
            _draw_aperture(hard_result, aperture, 1)
        return hard_result

    high_size = (width * scale, height * scale)
    hard_union = Image.new("L", high_size, 0)
    feathered_union = Image.new("L", high_size, 0)
    hard_apertures: list[Mapping[str, Any]] = []
    has_feathered_aperture = False
    for aperture in apertures:
        current = Image.new("L", high_size, 0)
        _draw_aperture(current, aperture, scale)
        feather = aperture["feather"]
        if feather > 0:
            current = current.filter(ImageFilter.GaussianBlur(radius=feather * scale))
            feathered_union = ImageChops.lighter(feathered_union, current)
            has_feathered_aperture = True
        else:
            hard_union = ImageChops.lighter(hard_union, current)
            hard_apertures.append(aperture)

    result = Image.new("L", (width, height), 0)
    if hard_apertures:
        # Lanczos gives the desired smooth inner transition but also produces a
        # faint three-pixel ringing lobe outside a declared zero-feather shape.
        # Clamp that lobe to a full-resolution geometric support mask so a
        # dynamic aperture can never alter approved fixed pixels outside its
        # exact declaration. Feathered apertures intentionally retain their
        # outward falloff and therefore bypass this clamp.
        hard_resized = hard_union.resize((width, height), _lanczos())
        hard_support = Image.new("L", (width, height), 0)
        for aperture in hard_apertures:
            _draw_aperture(hard_support, aperture, 1)
        hard_resized = ImageChops.multiply(hard_resized, hard_support)
        result = ImageChops.lighter(result, hard_resized)
    if has_feathered_aperture:
        result = ImageChops.lighter(
            result,
            feathered_union.resize((width, height), _lanczos()),
        )
    return result


def _cut_apertures(
    image: Any,
    apertures: Sequence[Mapping[str, Any]],
    scale: int,
    edge_mode: str = "antialiased",
) -> Any:
    aperture_mask = _build_aperture_mask(
        image.width,
        image.height,
        apertures,
        scale,
        edge_mode=edge_mode,
    )
    retained_alpha = ImageChops.multiply(image.getchannel("A"), ImageOps.invert(aperture_mask))
    result = image.copy()
    result.putalpha(retained_alpha)
    return result


def _preserve_warm_frame_pixels(
    cut_image: Any,
    frame_source: Any,
    selector: Mapping[str, Any],
) -> Any:
    """Restore authored warm-gold rails intersected by an oversized hard aperture.

    Portrait apertures can be deliberately enlarged beyond an irregular inner rail
    to remove dark backing rings.  Only source pixels matching the declared warm
    frame selector are restored, so portrait imagery reaches the rail without
    restoring the discarded black material band.
    """

    source = frame_source.convert("RGBA")
    geometric_band = selector.get("geometricBand")
    if geometric_band is not None:
        outer_mask = _build_aperture_mask(
            source.width,
            source.height,
            [geometric_band["outer"]],
            geometric_band["antialiasScale"],
        )
        inner_mask = _build_aperture_mask(
            source.width,
            source.height,
            [geometric_band["inner"]],
            geometric_band["antialiasScale"],
        )
        band_mask = ImageChops.multiply(outer_mask, ImageOps.invert(inner_mask))
        frame_pixels = Image.new("L", source.size, 0)
        frame_pixel_values = frame_pixels.load()
        source_pixels = source.load()
        dark_pixel_maximum = selector.get("darkPixelMaximum", 0.0)
        for y in range(source.height):
            for x in range(source.width):
                red, green, blue, alpha = source_pixels[x, y]
                warm_frame = (
                    red >= selector["minimumRed"]
                    and red - blue >= selector["minimumRedMinusBlue"]
                    and green - blue >= selector["minimumGreenMinusBlue"]
                )
                dark_frame = dark_pixel_maximum > 0 and max(red, green, blue) <= dark_pixel_maximum
                if alpha > 0 and (warm_frame or dark_frame):
                    frame_pixel_values[x, y] = alpha
        selected = ImageChops.multiply(band_mask, frame_pixels)
        return Image.composite(source, cut_image, selected)

    selected = Image.new("L", source.size, 0)
    selected_pixels = selected.load()
    source_pixels = source.load()
    for y in range(source.height):
        for x in range(source.width):
            red, green, blue, alpha = source_pixels[x, y]
            if (
                alpha > 0
                and red >= selector["minimumRed"]
                and red - blue >= selector["minimumRedMinusBlue"]
                and green - blue >= selector["minimumGreenMinusBlue"]
            ):
                selected_pixels[x, y] = 255
    return Image.composite(source, cut_image, selected)


def _draw_authored_frames(
    image: Any,
    frames: Sequence[Mapping[str, Any]],
    scale: int,
) -> Any:
    """Bake clean supersampled oval rails into the same RGBA card asset."""

    if not frames:
        return image
    high_size = (image.width * scale, image.height * scale)
    overlay = Image.new("RGBA", high_size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for frame in frames:
        for stroke in frame["strokes"]:
            inset = stroke["inset"]
            left = int(round((frame["x"] + inset) * scale))
            top = int(round((frame["y"] + inset) * scale))
            right = int(round((frame["x"] + frame["width"] - inset) * scale))
            bottom = int(round((frame["y"] + frame["height"] - inset) * scale))
            color = tuple(
                int(stroke["color"][index : index + 2], 16)
                for index in (1, 3, 5)
            ) + (255,)
            draw.ellipse(
                (left, top, right, bottom),
                outline=color,
                width=max(1, int(round(stroke["width"] * scale))),
            )
    overlay = overlay.resize(image.size, _lanczos())
    return Image.alpha_composite(image.convert("RGBA"), overlay)


def _neutralize_transparent_rgb(image: Any, color: Tuple[int, int, int]) -> Any:
    """Remove generator matte RGB from transparent and transition pixels.

    PNG uses straight alpha, so RGB remains present even where alpha is zero.
    Image generators commonly leave white checkerboard RGB behind; resampling it
    produces pale portrait halos.  Replacing RGB wherever alpha is not fully
    opaque preserves the antialiased alpha coverage while making the transition
    composite against the canonical marble-black matte.
    """

    alpha = image.getchannel("A")
    opaque_only = alpha.point([255 if value == 255 else 0 for value in range(256)])
    cleaned_rgb = Image.composite(
        image.convert("RGB"),
        Image.new("RGB", image.size, color),
        opaque_only,
    )
    cleaned = cleaned_rgb.convert("RGBA")
    cleaned.putalpha(alpha)
    return cleaned


def _alpha_statistics(image: Any) -> Dict[str, Any]:
    histogram = image.getchannel("A").histogram()
    total = image.width * image.height
    transparent = histogram[0]
    opaque = histogram[255]
    partial = total - transparent - opaque
    nonzero_bins = [index for index, count in enumerate(histogram) if count]
    weighted = sum(index * count for index, count in enumerate(histogram))
    return {
        "totalPixels": total,
        "transparentPixels": transparent,
        "partiallyTransparentPixels": partial,
        "opaquePixels": opaque,
        "transparentFraction": round(transparent / total, 8),
        "partiallyTransparentFraction": round(partial / total, 8),
        "opaqueFraction": round(opaque / total, 8),
        "minimumAlpha": min(nonzero_bins),
        "maximumAlpha": max(nonzero_bins),
        "meanAlpha": round(weighted / total, 4),
    }


def _write_temp_png(image: Any, target: Path) -> Path:
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=f".{target.name}.", suffix=".tmp", dir=target.parent)
    os.close(descriptor)
    temporary = Path(name)
    try:
        image.save(temporary, format="PNG", optimize=False, compress_level=9)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise
    return temporary


def _write_temp_json(value: Mapping[str, Any], target: Path) -> Path:
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=f".{target.name}.", suffix=".tmp", dir=target.parent)
    os.close(descriptor)
    temporary = Path(name)
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write("\n")
    except Exception:
        temporary.unlink(missing_ok=True)
        raise
    return temporary


def _copy_exclusive(temporary: Path, target: Path) -> None:
    created = False
    try:
        with temporary.open("rb") as source, target.open("xb") as destination:
            created = True
            shutil.copyfileobj(source, destination, length=1024 * 1024)
    except Exception:
        # Only remove a partial target created by this invocation.  If exclusive
        # creation failed because another process won the race, its file is not
        # ours to touch.
        if created:
            target.unlink(missing_ok=True)
        raise
    finally:
        temporary.unlink(missing_ok=True)


def _commit_temporary(temporary: Path, target: Path, force: bool) -> None:
    if force:
        os.replace(temporary, target)
    else:
        _copy_exclusive(temporary, target)


def _validate_paths(source: Path, spec: Path, output: Path, evidence: Path, force: bool) -> None:
    if not source.is_file():
        raise PreparationError(f"Source PNG does not exist: {source}")
    if source.suffix.lower() != ".png":
        raise PreparationError("Source must have a .png extension")
    if not spec.is_file():
        raise PreparationError(f"Overlay spec does not exist: {spec}")
    if output.suffix.lower() != ".png":
        raise PreparationError("Output must have a .png extension")
    if evidence.suffix.lower() != ".json":
        raise PreparationError("Evidence path must have a .json extension")

    resolved = {
        "source": source.resolve(),
        "spec": spec.resolve(),
        "output": output.resolve(),
        "evidence": evidence.resolve(),
    }
    if resolved["output"] in {resolved["source"], resolved["spec"]}:
        raise PreparationError("Output may not replace the source PNG or spec, even with --force")
    if resolved["evidence"] in {resolved["source"], resolved["spec"], resolved["output"]}:
        raise PreparationError("Evidence must use a distinct path from source, spec, and output")
    if not force:
        existing = [str(path) for path in (output, evidence) if path.exists()]
        if existing:
            raise PreparationError(
                "Refusing to overwrite existing production output without --force: "
                + ", ".join(existing)
            )


def prepare_overlay(
    source_path: Path,
    spec_path: Path,
    output_path: Path,
    evidence_path: Path,
    *,
    force: bool = False,
    marble_override: float | None = None,
    marble_blur_override: float | None = None,
    antialias_override: int | None = None,
) -> Dict[str, Any]:
    """Prepare one production overlay and return its evidence record."""

    _require_pillow()
    source_path = Path(source_path)
    spec_path = Path(spec_path)
    output_path = Path(output_path)
    evidence_path = Path(evidence_path)
    _validate_paths(source_path, spec_path, output_path, evidence_path, force)

    raw_spec = _load_json_object(spec_path)
    parameters = _effective_parameters(
        raw_spec,
        marble_override=marble_override,
        marble_blur_override=marble_blur_override,
        antialias_override=antialias_override,
    )

    try:
        with Image.open(source_path) as opened:
            if opened.format != "PNG":
                raise PreparationError("Source bytes are not a PNG image")
            opened.load()
            source_mode = opened.mode
            source_dimensions = {"width": opened.width, "height": opened.height}
            source_rgba = opened.convert("RGBA")
    except PreparationError:
        raise
    except Exception as exc:
        raise PreparationError(f"Unable to decode source PNG '{source_path}': {exc}") from exc

    canvas = parameters["canvas"]
    normalization = parameters["normalization"]
    normalized = _normalize_image(
        source_rgba,
        canvas["width"],
        canvas["height"],
        normalization["fit"],
        normalization["anchor"],
        normalization["sourceCrop"],
        normalization["axisWarp"],
    )
    light_matte = parameters["lightNeutralMatteReplacement"]
    if light_matte is not None:
        replacement_color = tuple(
            int(light_matte["color"][index : index + 2], 16)
            for index in (1, 3, 5)
        )
        normalized = _replace_light_neutral_matte(
            normalized,
            light_matte["minimumChannel"],
            light_matte["maximumChroma"],
            replacement_color,
        )
    light_matte_alpha = parameters["lightNeutralMatteAlphaRemoval"]
    if light_matte_alpha is not None:
        normalized = _remove_light_neutral_matte(
            normalized,
            light_matte_alpha["minimumChannel"],
            light_matte_alpha["maximumChroma"],
        )
    neutral_tone = parameters["neutralToneReplacement"]
    if neutral_tone is not None:
        replacement_color = tuple(
            int(neutral_tone["color"][index : index + 2], 16)
            for index in (1, 3, 5)
        )
        normalized = _replace_neutral_tone_range(
            normalized,
            neutral_tone["minimumLuminance"],
            neutral_tone["maximumLuminance"],
            neutral_tone["maximumChroma"],
            replacement_color,
        )
    marble = parameters["marbleReduction"]
    toned = _apply_marble_reduction(
        normalized,
        marble["amount"],
        marble["blurRadius"],
        marble["luminanceCeiling"],
        marble["chromaCeiling"],
        marble["selectionSoftness"],
    )
    output_image = _cut_apertures(
        toned,
        parameters["apertures"],
        parameters["antialiasScale"],
        edge_mode=parameters["apertureEdgeMode"],
    )
    frame_preservation = parameters["apertureFramePreservation"]
    if frame_preservation is not None:
        output_image = _preserve_warm_frame_pixels(
            output_image,
            toned,
            frame_preservation,
        )
    output_image = _draw_authored_frames(
        output_image,
        parameters["drawnFrames"],
        parameters["antialiasScale"],
    )
    transparent_rgb = tuple(
        int(parameters["transparentRgb"][index : index + 2], 16)
        for index in (1, 3, 5)
    )
    output_image = _neutralize_transparent_rgb(output_image, transparent_rgb)
    if output_image.mode != "RGBA" or output_image.size != (canvas["width"], canvas["height"]):
        raise PreparationError("Internal invariant failed: output is not normalized RGBA")

    temporary_output: Path | None = None
    temporary_evidence: Path | None = None
    output_committed = False
    try:
        temporary_output = _write_temp_png(output_image, output_path)
        output_sha256 = _sha256(temporary_output)
        evidence: Dict[str, Any] = {
            "schema": EVIDENCE_SCHEMA,
            "tool": {
                "name": "prepare_overlay_assets.py",
                "version": TOOL_VERSION,
                "pillowVersion": PILLOW_VERSION,
            },
            "source": {
                "path": str(source_path.resolve()),
                "sha256": _sha256(source_path),
                "format": "PNG",
                "mode": source_mode,
                "dimensions": source_dimensions,
                "alpha": _alpha_statistics(source_rgba),
            },
            "spec": {
                "path": str(spec_path.resolve()),
                "sha256": _sha256(spec_path),
                "schema": SPEC_SCHEMA,
            },
            "parameters": parameters,
            "normalized": {
                "mode": "RGBA",
                "dimensions": {"width": normalized.width, "height": normalized.height},
                "alpha": _alpha_statistics(normalized),
            },
            "output": {
                "path": str(output_path.resolve()),
                "sha256": output_sha256,
                "format": "PNG",
                "mode": "RGBA",
                "dimensions": {"width": output_image.width, "height": output_image.height},
                "alpha": _alpha_statistics(output_image),
            },
        }
        temporary_evidence = _write_temp_json(evidence, evidence_path)

        # Recheck immediately before committing to close the ordinary race window.
        if not force and (output_path.exists() or evidence_path.exists()):
            raise PreparationError("A target appeared during preparation; refusing to overwrite without --force")
        _commit_temporary(temporary_output, output_path, force)
        temporary_output = None
        output_committed = True
        try:
            _commit_temporary(temporary_evidence, evidence_path, force)
            temporary_evidence = None
        except Exception:
            if not force and output_committed:
                output_path.unlink(missing_ok=True)
            raise
    finally:
        if temporary_output is not None:
            temporary_output.unlink(missing_ok=True)
        if temporary_evidence is not None:
            temporary_evidence.unlink(missing_ok=True)

    try:
        with Image.open(output_path) as verified:
            verified.load()
            if verified.format != "PNG" or verified.mode != "RGBA" or verified.size != output_image.size:
                raise PreparationError("Written output failed PNG/RGBA/dimension verification")
    except PreparationError:
        raise
    except Exception as exc:
        raise PreparationError(f"Unable to verify written output '{output_path}': {exc}") from exc
    if _sha256(output_path) != evidence["output"]["sha256"]:
        raise PreparationError("Written output hash does not match the evidence record")
    return evidence


def _self_test_check(condition: bool, message: str) -> None:
    if not condition:
        raise PreparationError(f"Self-test failed: {message}")


def _run_self_test() -> Dict[str, Any]:
    """Exercise all production invariants entirely inside the system temp area."""

    _require_pillow()
    result: Dict[str, Any]
    with tempfile.TemporaryDirectory(prefix="reign-overlay-selftest-") as directory:
        root = Path(directory)
        source = root / "synthetic-source.png"
        spec = root / "overlay-spec.json"
        output = root / "production-overlay.png"
        evidence_path = root / "production-overlay.png.evidence.json"

        synthetic = Image.new("RGBA", (96, 72), (26, 25, 23, 255))
        draw = ImageDraw.Draw(synthetic)
        for offset in range(-72, 120, 9):
            draw.line((offset, 0, offset + 72, 72), fill=(91, 82, 65, 255), width=2)
        draw.rectangle((2, 2, 93, 69), outline=(169, 132, 70, 255), width=2)
        synthetic.save(source, format="PNG", compress_level=9)

        spec_value = {
            "schema": SPEC_SCHEMA,
            "canvas": {"width": 160, "height": 100},
            "normalization": {"fit": "cover", "anchor": "center"},
            "antialiasScale": 4,
            "marbleReduction": {"amount": 0.5, "blurRadius": 2.0},
            "apertures": [
                {"id": "portrait-oval", "shape": "oval", "x": 55, "y": 10, "width": 50, "height": 80},
                {"id": "portrait-circle", "shape": "circle", "x": 8, "y": 20, "width": 30, "height": 30},
                {"id": "scene-rect", "shape": "rectangle", "x": 118, "y": 12, "width": 34, "height": 26},
                {
                    "id": "scene-rounded-rect",
                    "shape": "rounded_rectangle",
                    "x": 118,
                    "y": 55,
                    "width": 34,
                    "height": 34,
                    "radius": 5,
                    "feather": 0.25,
                },
                {
                    "id": "banner-shield",
                    "shape": "polygon",
                    "points": [[42, 18], [112, 18], [112, 42], [77, 53], [42, 42]],
                },
            ],
        }
        with spec.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(spec_value, stream, indent=2, sort_keys=True)
            stream.write("\n")

        evidence = prepare_overlay(source, spec, output, evidence_path)
        reduced_probe = _apply_marble_reduction(synthetic, 0.5, 2.0, 128.0, 48.0, 24.0)
        _self_test_check(
            reduced_probe.getchannel("A").tobytes() == synthetic.getchannel("A").tobytes(),
            "marble reduction changed alpha",
        )
        _self_test_check(
            reduced_probe.convert("RGB").tobytes() != synthetic.convert("RGB").tobytes(),
            "marble reduction did not alter dark, low-chroma texture",
        )
        _self_test_check(
            reduced_probe.getpixel((2, 2)) == synthetic.getpixel((2, 2)),
            "marble reduction altered the antique-gold test edge",
        )
        matte_probe = Image.new("RGBA", (2, 1), (0, 0, 0, 255))
        matte_probe.putpixel((0, 0), (242, 240, 241, 173))
        matte_probe.putpixel((1, 0), (169, 132, 70, 211))
        matte_replaced = _replace_light_neutral_matte(
            matte_probe, 205.0, 18.0, (18, 18, 17)
        )
        _self_test_check(
            matte_replaced.getpixel((0, 0)) == (18, 18, 17, 173),
            "neutral light matte was not replaced with the canonical material color",
        )
        _self_test_check(
            matte_replaced.getpixel((1, 0)) == (169, 132, 70, 211),
            "neutral light matte replacement altered antique gold",
        )
        matte_removed = _remove_light_neutral_matte(
            matte_probe, 205.0, 18.0
        )
        _self_test_check(
            matte_removed.getpixel((0, 0))[3] == 0,
            "neutral light matte alpha removal did not clear generator matte",
        )
        _self_test_check(
            matte_removed.getpixel((1, 0)) == (169, 132, 70, 211),
            "neutral light matte alpha removal altered antique gold",
        )
        tone_probe = Image.new("RGBA", (3, 1), (0, 0, 0, 255))
        tone_probe.putpixel((0, 0), (58, 59, 57, 173))
        tone_probe.putpixel((1, 0), (18, 18, 17, 211))
        tone_probe.putpixel((2, 0), (91, 65, 30, 199))
        tone_replaced = _replace_neutral_tone_range(
            tone_probe, 42.0, 90.0, 8.0, (89, 89, 86)
        )
        _self_test_check(
            tone_replaced.getpixel((0, 0)) == (89, 89, 86, 173),
            "bounded neutral tone was not replaced with the frozen palette token",
        )
        _self_test_check(
            tone_replaced.getpixel((1, 0)) == (18, 18, 17, 211),
            "neutral tone replacement altered material below its luminance range",
        )
        _self_test_check(
            tone_replaced.getpixel((2, 0)) == (91, 65, 30, 199),
            "neutral tone replacement altered high-chroma antique gold",
        )
        warp_probe = Image.new("RGBA", (6, 4), (20, 30, 40, 255))
        ImageDraw.Draw(warp_probe).rectangle((0, 0, 1, 3), fill=(180, 40, 30, 255))
        warped = _piecewise_axis_warp(
            warp_probe,
            8,
            6,
            {
                "x": [
                    {"source": 0.0, "destination": 0.0},
                    {"source": 2.0, "destination": 4.0},
                    {"source": 6.0, "destination": 8.0},
                ],
                "y": [
                    {"source": 0.0, "destination": 0.0},
                    {"source": 1.0, "destination": 2.0},
                    {"source": 4.0, "destination": 6.0},
                ],
            },
        )
        _self_test_check(warped.size == (8, 6), "axis warp produced incorrect dimensions")
        _self_test_check(
            warped.getpixel((1, 3))[0] > warped.getpixel((6, 3))[0],
            "axis warp did not preserve registered segment ordering",
        )
        inward_probe_apertures = [
            {
                "id": "inward-aa-polygon",
                "shape": "polygon",
                "points": [[12.0, 8.0], [49.0, 8.0], [49.0, 35.0], [30.5, 53.0], [12.0, 35.0]],
                "x": 12.0,
                "y": 8.0,
                "width": 37.0,
                "height": 45.0,
                "feather": 0.0,
            }
        ]
        inward_probe = _build_aperture_mask(64, 64, inward_probe_apertures, 8)
        inward_support = Image.new("L", (64, 64), 0)
        _draw_aperture(inward_support, inward_probe_apertures[0], 1)
        outward_spill = ImageChops.multiply(inward_probe, ImageOps.invert(inward_support))
        _self_test_check(
            outward_spill.getbbox() is None,
            "zero-feather aperture antialiasing escaped its declared geometric support",
        )
        inward_histogram = inward_probe.histogram()
        _self_test_check(
            sum(inward_histogram[1:255]) > 0,
            "inward-clamped aperture lost its antialiased transition pixels",
        )
        hard_probe = _build_aperture_mask(64, 64, inward_probe_apertures, 8, edge_mode="hard")
        _self_test_check(
            sum(hard_probe.histogram()[1:255]) == 0,
            "hard aperture edge retained partial-alpha transition pixels",
        )
        frame_probe = Image.new("RGBA", (64, 64), (7, 8, 8, 255))
        frame_probe.putpixel((30, 8), (169, 132, 70, 255))
        cut_frame_probe = _cut_apertures(
            frame_probe,
            inward_probe_apertures,
            8,
            edge_mode="hard",
        )
        preserved_frame_probe = _preserve_warm_frame_pixels(
            cut_frame_probe,
            frame_probe,
            {
                "minimumRed": 45.0,
                "minimumRedMinusBlue": 8.0,
                "minimumGreenMinusBlue": 3.0,
                "darkPixelMaximum": 32.0,
            },
        )
        _self_test_check(
            preserved_frame_probe.getpixel((30, 8))[3] == 255,
            "hard aperture did not restore a selected warm frame pixel",
        )
        _self_test_check(
            preserved_frame_probe.getpixel((30, 9))[3] == 0,
            "hard aperture restored discarded dark material beside the frame",
        )
        band_probe_source = Image.new("RGBA", (64, 64), (25, 24, 22, 255))
        band_probe_cut = Image.new("RGBA", (64, 64), (7, 8, 8, 0))
        band_probe = _preserve_warm_frame_pixels(
            band_probe_cut,
            band_probe_source,
            {
                "minimumRed": 45.0,
                "minimumRedMinusBlue": 8.0,
                "minimumGreenMinusBlue": 3.0,
                "darkPixelMaximum": 32.0,
                "geometricBand": {
                    "outer": {
                        "id": "outer",
                        "shape": "oval",
                        "x": 8.0,
                        "y": 4.0,
                        "width": 48.0,
                        "height": 56.0,
                        "feather": 0.0,
                    },
                    "inner": {
                        "id": "inner",
                        "shape": "oval",
                        "x": 14.0,
                        "y": 10.0,
                        "width": 36.0,
                        "height": 44.0,
                        "feather": 0.0,
                    },
                    "antialiasScale": 8,
                },
            },
        )
        _self_test_check(
            band_probe.getpixel((12, 32))[3] > 0,
            "geometric frame band did not restore its outer rail",
        )
        _self_test_check(
            band_probe.getpixel((32, 32))[3] == 0,
            "geometric frame band restored the empty portrait center",
        )
        drawn_frame_probe = _draw_authored_frames(
            Image.new("RGBA", (64, 64), (0, 0, 0, 0)),
            [
                {
                    "id": "drawn",
                    "shape": "oval",
                    "x": 8.0,
                    "y": 4.0,
                    "width": 48.0,
                    "height": 56.0,
                    "feather": 0.0,
                    "strokes": [
                        {"inset": 0.0, "width": 2.0, "color": "#A98446"},
                    ],
                }
            ],
            8,
        )
        _self_test_check(
            drawn_frame_probe.getpixel((9, 32))[3] > 0,
            "drawn oval frame did not produce a visible rail",
        )
        _self_test_check(
            drawn_frame_probe.getpixel((32, 32))[3] == 0,
            "drawn oval frame filled its transparent center",
        )
        global_polygon = dict(inward_probe_apertures[0])
        global_polygon["points"] = [
            [point[0] + 13.0, point[1] + 7.0]
            for point in inward_probe_apertures[0]["points"]
        ]
        global_polygon["x"] += 13.0
        global_polygon["y"] += 7.0
        global_mask = _build_aperture_mask(90, 80, [global_polygon], 8)
        translated_crop = global_mask.crop((13, 7, 77, 71))
        _self_test_check(
            translated_crop.tobytes() == inward_probe.tobytes(),
            "half-pixel aperture rasterization changed after integer crop translation",
        )
        with Image.open(output) as prepared:
            prepared.load()
            _self_test_check(prepared.mode == "RGBA", "output mode is not RGBA")
            _self_test_check(prepared.size == (160, 100), "output dimensions are not normalized")
            _self_test_check(prepared.getpixel((80, 50))[3] == 0, "oval center is not transparent")
            _self_test_check(prepared.getpixel((23, 35))[3] == 0, "circle center is not transparent")
            _self_test_check(prepared.getpixel((135, 25))[3] == 0, "rectangle center is not transparent")
            _self_test_check(
                prepared.getpixel((135, 72))[3] == 0,
                "rounded rectangle center is not transparent",
            )
            _self_test_check(prepared.getpixel((77, 34))[3] == 0, "polygon center is not transparent")
            _self_test_check(prepared.getpixel((159, 99))[3] == 255, "frame exterior is not opaque")
        alpha = evidence["output"]["alpha"]
        _self_test_check(alpha["transparentPixels"] > 0, "output has no transparent aperture pixels")
        _self_test_check(
            alpha["partiallyTransparentPixels"] > 0,
            "output has no antialiased partial-alpha edge pixels",
        )
        _self_test_check(alpha["opaquePixels"] > 0, "output has no opaque frame pixels")
        _self_test_check(
            _sha256(output) == evidence["output"]["sha256"],
            "output SHA-256 does not match evidence",
        )

        refused_overwrite = False
        try:
            prepare_overlay(source, spec, output, evidence_path)
        except PreparationError as exc:
            refused_overwrite = "Refusing to overwrite" in str(exc)
        _self_test_check(refused_overwrite, "existing targets were not refused without --force")

        first_hash = evidence["output"]["sha256"]
        forced = prepare_overlay(source, spec, output, evidence_path, force=True)
        _self_test_check(
            forced["output"]["sha256"] == first_hash,
            "forced deterministic replacement produced different output bytes",
        )
        with evidence_path.open("r", encoding="utf-8") as stream:
            persisted = json.load(stream)
        _self_test_check(persisted == forced, "persisted evidence differs from returned evidence")

        result = {
            "status": "PASS",
            "temporaryIsolation": True,
            "outputMode": forced["output"]["mode"],
            "outputDimensions": forced["output"]["dimensions"],
            "outputSha256": forced["output"]["sha256"],
            "alpha": forced["output"]["alpha"],
            "apertureShapesTested": ["oval", "circle", "rectangle", "rounded_rectangle", "polygon"],
            "lightNeutralMatteReplacementTested": True,
            "lightNeutralMatteAlphaRemovalTested": True,
            "neutralToneReplacementTested": True,
            "piecewiseAxisWarpTested": True,
            "inwardZeroFeatherAntialiasClampTested": True,
            "hardApertureEdgeTested": True,
            "warmFramePreservationTested": True,
            "translatedCropApertureParityTested": True,
            "noClobberTested": True,
            "forceReplacementTested": True,
            "deterministicReplacementTested": True,
        }
    result["temporaryOutputRemoved"] = True
    return result


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Deterministically normalize a generated PNG and cut declarative, "
            "antialiased transparent apertures for a Reign production overlay."
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""Spec example:
  {
    "schema": "reign-overlay-asset-spec-v1",
    "canvas": {"width": 512, "height": 512},
    "normalization": {"fit": "cover", "anchor": "center"},
    "antialiasScale": 4,
    "transparentRgb": "#121211",
    "marbleReduction": {"amount": 0.25, "blurRadius": 6.0,
                         "luminanceCeiling": 128, "chromaCeiling": 48,
                         "selectionSoftness": 24},
    "apertures": [
      {"id": "portrait", "shape": "oval", "x": 96, "y": 48,
       "width": 320, "height": 400, "feather": 0.0}
    ]
  }

Shapes: circle, ellipse/oval, rectangle, rounded_rectangle.
Fits: exact, contain, cover, stretch. Existing targets require --force.
""",
    )
    parser.add_argument("source", nargs="?", type=Path, help="generated source PNG")
    parser.add_argument("output", nargs="?", type=Path, help="production RGBA overlay PNG")
    parser.add_argument("--spec", type=Path, help="JSON canvas/aperture production spec")
    parser.add_argument(
        "--evidence",
        type=Path,
        help="JSON evidence path (default: OUTPUT.png.evidence.json)",
    )
    parser.add_argument(
        "--marble-reduction",
        type=float,
        help="override marbleReduction.amount (0..1)",
    )
    parser.add_argument(
        "--marble-blur-radius",
        type=float,
        help="override marbleReduction.blurRadius in pixels",
    )
    parser.add_argument(
        "--antialias-scale",
        type=int,
        help="override supersampling scale (2..16)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="explicitly allow atomic replacement of existing output/evidence files",
    )
    parser.add_argument(
        "--self-test",
        action="store_true",
        help="run isolated synthetic RGBA/aperture/no-clobber tests in the system temp directory",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)
    try:
        if args.self_test:
            if args.source is not None or args.output is not None or args.spec is not None:
                parser.error("--self-test does not accept source, output, or --spec")
            print(json.dumps(_run_self_test(), indent=2, sort_keys=True))
            return 0

        if args.source is None or args.output is None or args.spec is None:
            parser.error("source, output, and --spec are required unless --self-test is used")
        evidence_path = args.evidence or args.output.with_suffix(args.output.suffix + ".evidence.json")
        evidence = prepare_overlay(
            args.source,
            args.spec,
            args.output,
            evidence_path,
            force=args.force,
            marble_override=args.marble_reduction,
            marble_blur_override=args.marble_blur_radius,
            antialias_override=args.antialias_scale,
        )
        print(json.dumps(evidence, indent=2, sort_keys=True))
        return 0
    except PreparationError as exc:
        print(f"prepare_overlay_assets.py: error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
