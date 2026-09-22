#!/usr/bin/env python3
"""Remove the generated checkerboard matte from the modern tavern frame.

The source frame is a generated RGB treatment combined with the legacy frame's
alpha plane.  The legacy plane intentionally preserved old opaque ornaments
inside the modern aperture, where the generated RGB happened to contain a
checkerboard transparency preview.  This tool makes only those neutral,
high-luminance matte pixels transparent and clears hidden RGB to prevent a
light fringe during bilinear sampling.  Dark marble and antique-gold pixels are
left byte-for-byte unchanged.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image


APERTURE_BOUNDS = (90, 90, 1163, 1163)
NEUTRAL_MINIMUM = 110
NEUTRAL_SPREAD_MAXIMUM = 24


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def repair(source: Path, destination: Path, evidence: Path | None) -> None:
    source_bytes = source.read_bytes()
    image = Image.open(source).convert("RGBA")
    if image.size != (1254, 1254):
        raise ValueError(f"Expected a 1254x1254 frame, received {image.size!r}.")

    pixels = bytearray(image.tobytes())
    width, height = image.size
    left, top, right, bottom = APERTURE_BOUNDS
    matte_pixels_removed = 0
    hidden_rgb_pixels_cleared = 0
    preserved_visible_pixels = 0

    for y in range(height):
        for x in range(width):
            offset = (y * width + x) * 4
            red, green, blue, alpha = pixels[offset : offset + 4]
            if alpha == 0:
                if red or green or blue:
                    pixels[offset : offset + 3] = b"\x00\x00\x00"
                    hidden_rgb_pixels_cleared += 1
                continue

            inside_aperture = left <= x <= right and top <= y <= bottom
            neutral_matte = (
                min(red, green, blue) >= NEUTRAL_MINIMUM
                and max(red, green, blue) - min(red, green, blue)
                <= NEUTRAL_SPREAD_MAXIMUM
            )
            if inside_aperture and neutral_matte:
                pixels[offset : offset + 4] = b"\x00\x00\x00\x00"
                matte_pixels_removed += 1
            else:
                preserved_visible_pixels += 1

    repaired = Image.frombytes("RGBA", image.size, bytes(pixels))
    destination.parent.mkdir(parents=True, exist_ok=True)
    repaired.save(destination, format="PNG", optimize=True)

    output_bytes = destination.read_bytes()
    alpha = repaired.getchannel("A")
    alpha_bytes = alpha.tobytes()
    result = {
        "schema": "reign-tavern-frame-alpha-repair-v1",
        "source": str(source).replace("\\", "/"),
        "destination": str(destination).replace("\\", "/"),
        "dimensions": {"width": width, "height": height},
        "sourceSha256": sha256_bytes(source_bytes),
        "outputSha256": sha256_bytes(output_bytes),
        "alphaPlaneSha256": sha256_bytes(alpha_bytes),
        "apertureBoundsInclusive": {
            "left": left,
            "top": top,
            "right": right,
            "bottom": bottom,
        },
        "neutralMinimum": NEUTRAL_MINIMUM,
        "neutralSpreadMaximum": NEUTRAL_SPREAD_MAXIMUM,
        "mattePixelsRemoved": matte_pixels_removed,
        "hiddenRgbPixelsCleared": hidden_rgb_pixels_cleared,
        "preservedVisiblePixels": preserved_visible_pixels,
        "transparentPixelCount": alpha_bytes.count(0),
        "partialAlphaPixelCount": sum(0 < value < 255 for value in alpha_bytes),
        "opaquePixelCount": alpha_bytes.count(255),
        "centerAlpha": repaired.getpixel((width // 2, height // 2))[3],
        "cornerAlpha": repaired.getpixel((0, 0))[3],
    }
    if evidence is not None:
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--evidence", type=Path)
    args = parser.parse_args()
    repair(args.source, args.destination, args.evidence)


if __name__ == "__main__":
    main()
