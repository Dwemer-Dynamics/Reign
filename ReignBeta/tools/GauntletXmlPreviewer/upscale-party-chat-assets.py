from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageFilter


def upscale_png(path: Path, scale: int) -> None:
    source = Image.open(path).convert("RGBA")
    size = (source.width * scale, source.height * scale)

    rgb = source.convert("RGB").resize(size, Image.Resampling.LANCZOS)
    rgb = rgb.filter(ImageFilter.UnsharpMask(radius=1.15, percent=135, threshold=2))
    alpha = source.getchannel("A").resize(size, Image.Resampling.LANCZOS)

    output = rgb.convert("RGBA")
    output.putalpha(alpha)
    output.save(path, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description="Create sharper high-resolution Party Chat sprites.")
    parser.add_argument("--input-dir", required=True, type=Path)
    parser.add_argument("--scale", type=int, default=2)
    args = parser.parse_args()

    if args.scale < 1:
        raise SystemExit("Scale must be at least 1.")

    paths = sorted(args.input_dir.glob("reign_party_chat_*.png"))
    if not paths:
        raise SystemExit(f"No Party Chat sprites found in {args.input_dir}")

    for path in paths:
        upscale_png(path, args.scale)

    print(f"Upscaled {len(paths)} Party Chat sprites by {args.scale}x")


if __name__ == "__main__":
    main()
