"""Deterministically materialize the approved Training Yard shell and aperture."""

from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[4]
SOURCE = ROOT / "ReignBeta/artwork/ui-modern-style-kit/generated-sources/screen-shells/training-yard-modern-shell-v1-imagegen.png"
FONT = ROOT / "ReignBeta/artwork/ui-modern-style-kit/font-sources/CormorantGaramond-Medium.ttf"
OUTPUTS = (
    ROOT / "ReignBeta/GUI/SpriteParts/ui_reignbeta_training_yard/reign_training_yard_modern_shell.png",
    ROOT / "ReignBeta/artwork/ui-modern-style-kit/approved-references/training-yard.png",
)
RUNTIME_ATLAS = ROOT / "ReignBeta/GUI/RuntimeSpriteSheets/ui_reignbeta_training_yard/ui_reignbeta_training_yard_1.png"


def clipped_box(draw, bounds, clip, fill):
    left, top, right, bottom = bounds
    draw.polygon(
        (
            (left + clip, top), (right - clip, top), (right, top + clip),
            (right, bottom - clip), (right - clip, bottom), (left + clip, bottom),
            (left, bottom - clip), (left, top + clip),
        ),
        fill=fill,
    )


def make_campaign_overlay(image):
    """Remove the full-screen plate while retaining legible floating work panels."""
    scale = 4
    mask = Image.new("L", (image.width * scale, image.height * scale), 0)
    draw = ImageDraw.Draw(mask)
    regions = (
        ((50, 74, 418, 795), 14),       # trainer rail
        ((466, 106, 898, 194), 12),     # selected-trainer heading
        ((455, 217, 908, 591), 14),     # selected trainer
        ((455, 610, 908, 775), 14),     # elapsed and delivered XP
        ((940, 74, 1626, 795), 14),     # troop rail
        ((52, 821, 369, 899), 12),      # return
        ((385, 821, 1131, 899), 12),    # status
        ((1145, 821, 1383, 899), 12),   # train
        ((1398, 821, 1621, 899), 12),   # stop
    )
    for bounds, clip in regions:
        clipped_box(draw, tuple(value * scale for value in bounds), clip * scale, 255)
    mask = mask.resize(image.size, Image.Resampling.LANCZOS)
    image.putalpha(ImageChops.multiply(image.getchannel("A"), mask))
    clean = Image.new("RGBA", image.size, (0, 0, 0, 0))
    clean.alpha_composite(image)
    image.paste(clean)

    # The title becomes its own compact black-marble plaque instead of relying
    # on the removed full-screen canvas.
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    plaque = ImageDraw.Draw(overlay)
    points = ((620, 4), (1052, 4), (1064, 16), (1064, 46),
              (1052, 58), (620, 58), (608, 46), (608, 16))
    plaque.polygon(points, fill=(16, 16, 15, 255))       # #10100FFF
    plaque.line(points + (points[0],), fill=(126, 106, 77, 255), width=2)  # #7E6A4DFF
    image.alpha_composite(overlay)


def update_runtime_atlas(image):
    if not RUNTIME_ATLAS.exists():
        raise SystemExit(f"Training Yard runtime atlas was not found: {RUNTIME_ATLAS}")
    atlas = Image.open(RUNTIME_ATLAS).convert("RGBA")
    if atlas.size != (2048, 1024):
        raise SystemExit(f"Training Yard runtime atlas must be 2048x1024, got {atlas.size}")
    atlas.paste(image, (4, 4))
    atlas.save(RUNTIME_ATLAS, format="PNG", optimize=False, compress_level=9)


def centered(draw, text, center_x, center_y, size, color, tracking=0):
    font = ImageFont.truetype(str(FONT), size)
    if tracking <= 0:
        draw.text((center_x, center_y), text, font=font, fill=color, anchor="mm")
        return
    advances = [draw.textlength(char, font=font) for char in text]
    width = sum(advances) + tracking * max(0, len(text) - 1)
    x = center_x - width / 2
    for char, advance in zip(text, advances):
        draw.text((x, center_y), char, font=font, fill=color, anchor="lm")
        x += advance + tracking


def add_selected_portrait_aperture(image):
    """Bake the selected portrait plate/frame into the shell with a true alpha hole."""
    scale = 4
    size = 108
    center = size * scale // 2
    overlay = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    gold = (168, 138, 84, 255)       # #A88A54FF
    shadow = (53, 44, 32, 255)       # #352C20FF
    plate = (16, 16, 15, 255)        # #10100FFF

    draw.ellipse((1 * scale, 1 * scale, 107 * scale, 107 * scale), fill=plate, outline=shadow, width=2 * scale)
    draw.ellipse((3 * scale, 3 * scale, 105 * scale, 105 * scale), outline=gold, width=2 * scale)
    draw.ellipse((7 * scale, 7 * scale, 101 * scale, 101 * scale), outline=gold, width=1 * scale)
    for dx, dy in ((0, -51), (51, 0), (0, 51), (-51, 0)):
        cx, cy = center + dx * scale, center + dy * scale
        points = [(cx, cy - 4 * scale), (cx + 4 * scale, cy), (cx, cy + 4 * scale), (cx - 4 * scale, cy)]
        draw.polygon(points, fill=plate, outline=gold)

    overlay = overlay.resize((size, size), Image.Resampling.LANCZOS)
    image.alpha_composite(overlay, (628, 234))

    mask = Image.new("L", image.size, 0)
    mask_draw = ImageDraw.Draw(mask)
    mask_draw.ellipse((638, 244, 726, 332), fill=255)
    # Clear RGB as well as alpha so Bannerlord's atlas filtering cannot reveal a
    # dark seam and the transparent-pixel contract remains deterministic.
    image.paste((0, 0, 0, 0), mask=mask)


def main():
    image = Image.open(SOURCE).convert("RGBA")
    if image.size != (1672, 941):
        raise SystemExit(f"Training Yard source must be 1672x941, got {image.size}")
    make_campaign_overlay(image)
    draw = ImageDraw.Draw(image)
    title = (168, 138, 84, 255)       # #A88A54FF
    focus = (197, 172, 131, 255)     # #C5AC83FF
    body = (197, 189, 175, 255)      # #C5BDAFFF
    centered(draw, "TRAINING YARD", 836, 31, 31, title, 2)
    centered(draw, "TRAINERS", 232, 105, 19, title, 1)
    centered(draw, "PARTY TROOPS", 1283, 105, 19, title, 1)
    centered(draw, "SELECTED TRAINER", 682, 139, 17, title, 1)
    centered(draw, "RETURN TO KEEP", 210, 861, 17, body, 1)
    centered(draw, "TRAIN", 1265, 861, 18, focus, 1)
    centered(draw, "STOP", 1510, 861, 18, focus, 1)
    add_selected_portrait_aperture(image)
    for output in OUTPUTS:
        output.parent.mkdir(parents=True, exist_ok=True)
        image.save(output, format="PNG", optimize=False, compress_level=9)
    update_runtime_atlas(image)


if __name__ == "__main__":
    main()
