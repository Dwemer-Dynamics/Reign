from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter


@dataclass(frozen=True)
class AssetSpec:
    source: str
    size: tuple[int, int]
    crop: str = "full"
    mode: str = "cover"


ASSETS: dict[str, AssetSpec] = {
    "reign_party_chat_shell.png": AssetSpec("shell", (1660, 947), mode="stretch"),
    "reign_party_chat_header.png": AssetSpec("header", (1800, 124)),
    "reign_party_chat_roster.png": AssetSpec("roster", (1800, 233)),
    "reign_party_chat_card_normal.png": AssetSpec("card_normal", (540, 408)),
    "reign_party_chat_card_active.png": AssetSpec("card_active", (540, 408)),
    "reign_party_chat_card_hover.png": AssetSpec("card_hover", (540, 408)),
    "reign_party_chat_button_normal.png": AssetSpec("button_normal", (480, 108)),
    "reign_party_chat_button_hover.png": AssetSpec("button_hover", (480, 108)),
    "reign_party_chat_log.png": AssetSpec("log", (1800, 522)),
    "reign_party_chat_roster_scroll_track.png": AssetSpec("divider", (1000, 24), crop="top"),
    "reign_party_chat_roster_scroll_handle.png": AssetSpec("divider", (300, 28), crop="bottom"),
    "reign_party_chat_portrait_preview.png": AssetSpec("preview", (1040, 1250)),
    "reign_party_chat_scroll_track.png": AssetSpec("scroll_track", (24, 760)),
    "reign_party_chat_scroll_handle.png": AssetSpec("scroll_handle", (32, 252)),
    "reign_party_chat_player_message.png": AssetSpec("message_left", (1400, 253)),
    "reign_party_chat_npc_message.png": AssetSpec("message_right", (1400, 253)),
    "reign_party_chat_system_message.png": AssetSpec("system", (1560, 255)),
    "reign_party_chat_divider.png": AssetSpec("divider", (1200, 34), crop="top"),
    "reign_party_chat_input.png": AssetSpec("input", (2400, 187)),
    "reign_party_chat_send_normal.png": AssetSpec("button_normal", (480, 108)),
    "reign_party_chat_send_hover.png": AssetSpec("button_hover", (480, 108)),
    "reign_party_chat_preview_scroll_track.png": AssetSpec("scroll_track", (24, 760)),
    "reign_party_chat_preview_scroll_handle.png": AssetSpec("scroll_handle", (32, 252)),
}

CARD_STATE_OUTPUTS = {
    "reign_party_chat_card_normal.png",
    "reign_party_chat_card_active.png",
    "reign_party_chat_card_hover.png",
}


def alpha_bbox(image: Image.Image, threshold: int = 32) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value >= threshold else 0)
    bbox = mask.getbbox()
    if bbox is None:
        raise ValueError("asset became fully transparent after chroma-key removal")
    left, top, right, bottom = bbox
    return max(0, left - 2), max(0, top - 2), min(image.width, right + 2), min(image.height, bottom + 2)


def crop_component(image: Image.Image, component: str) -> Image.Image:
    if component == "full":
        return image.crop(alpha_bbox(image))

    midpoint = image.height // 2
    if component == "top":
        section = image.crop((0, 0, image.width, midpoint))
    elif component == "bottom":
        section = image.crop((0, midpoint, image.width, image.height))
    else:
        raise ValueError(f"unknown component crop: {component}")
    return section.crop(alpha_bbox(section))


def resize_cover(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    target_width, target_height = size
    scale = max(target_width / image.width, target_height / image.height)
    scaled_size = (
        max(target_width, round(image.width * scale)),
        max(target_height, round(image.height * scale)),
    )
    resized = image.resize(scaled_size, Image.Resampling.LANCZOS)
    left = (resized.width - target_width) // 2
    top = (resized.height - target_height) // 2
    return resized.crop((left, top, left + target_width, top + target_height))


def prepare_asset(source: Image.Image, spec: AssetSpec) -> Image.Image:
    if spec.mode == "stretch":
        prepared = source.convert("RGBA").resize(spec.size, Image.Resampling.LANCZOS)
    else:
        prepared = resize_cover(crop_component(source.convert("RGBA"), spec.crop), spec.size)
    if prepared.width < source.width or prepared.height < source.height:
        prepared = prepared.filter(ImageFilter.UnsharpMask(radius=0.7, percent=65, threshold=3))
    return prepared


def reflow_card_eye_control(image: Image.Image) -> Image.Image:
    """Move the baked eye control into the unused third metadata slot."""
    if image.size != (540, 408):
        raise ValueError(f"party card reflow expects 540x408, got {image.size}")

    result = image.copy()

    # Clear the eye panel's former location with clean card texture sampled
    # from the undecorated upper-right field of this same card.
    old_eye_bounds = (204, 269, 520, 378)
    texture = image.crop((204, 14, 520, 58)).resize(
        (old_eye_bounds[2] - old_eye_bounds[0], old_eye_bounds[3] - old_eye_bounds[1]),
        Image.Resampling.LANCZOS,
    )
    feather = Image.new("L", texture.size, 0)
    draw = ImageDraw.Draw(feather)
    draw.rectangle((12, 12, texture.width - 13, texture.height - 13), fill=255)
    feather = feather.filter(ImageFilter.GaussianBlur(radius=8))
    result.paste(texture, old_eye_bounds[:2], feather)

    # Cover the unused third strip with the original eye panel, moved upward.
    eye_panel = image.crop((214, 278, 510, 369))
    result.paste(eye_panel, (214, 201), eye_panel)
    return result


def prepare_card_state(source: Image.Image, spec: AssetSpec, output_name: str) -> Image.Image:
    prepared = reflow_card_eye_control(prepare_asset(source, spec))
    if output_name == "reign_party_chat_card_hover.png":
        prepared = ImageEnhance.Brightness(prepared).enhance(1.06)
    elif output_name == "reign_party_chat_card_active.png":
        prepared = ImageEnhance.Contrast(prepared).enhance(1.08)
        prepared = ImageEnhance.Brightness(prepared).enhance(1.08)
    return prepared


def validate_asset(name: str, image: Image.Image, keyed: bool) -> None:
    if image.mode != "RGBA":
        raise ValueError(f"{name} is not RGBA")
    if keyed and image.getchannel("A").getextrema()[0] != 0:
        raise ValueError(f"{name} has no transparent pixels after chroma-key removal")
    magenta = 0
    for red, green, blue, alpha in image.get_flattened_data():
        if alpha > 32 and red > 210 and blue > 210 and green < 80:
            magenta += 1
    if magenta > max(16, image.width * image.height // 5000):
        raise ValueError(f"{name} retains {magenta} visible magenta-key pixels")


def main() -> int:
    parser = argparse.ArgumentParser(description="Prepare Party Chat sprites from standalone source assets.")
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    expected = set(ASSETS)
    for existing in args.output_dir.glob("reign_party_chat_*.png"):
        if existing.name not in expected:
            existing.unlink()

    cache: dict[str, Image.Image] = {}
    for output_name, spec in ASSETS.items():
        source_key = "card_normal" if output_name in CARD_STATE_OUTPUTS else spec.source
        if source_key not in cache:
            source_path = args.input_dir / f"{source_key}.png"
            if not source_path.exists():
                raise FileNotFoundError(source_path)
            cache[source_key] = Image.open(source_path).convert("RGBA")
        if output_name in CARD_STATE_OUTPUTS:
            prepared = prepare_card_state(cache[source_key], spec, output_name)
        else:
            prepared = prepare_asset(cache[source_key], spec)
        validate_asset(output_name, prepared, keyed=spec.source != "shell")
        prepared.save(args.output_dir / output_name, optimize=True)
        print(f"{output_name}|{prepared.width}x{prepared.height}|source={source_key}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
