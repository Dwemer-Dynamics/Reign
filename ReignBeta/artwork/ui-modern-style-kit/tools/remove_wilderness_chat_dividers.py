"""Remove the two baked row rules inside the Wilderness Event transcript.

The user-approved outer frame and scrollbar are outside the edit region. This is
a one-time, hash-guarded refinement of the exact fixed source and runtime sprite.
"""

from hashlib import sha256
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[3]
FILES = {
    ROOT / "artwork/ui-modern-style-kit/generated-sources/screen-shells/wilderness-event-modern-shell-exact-fixed-source.png":
        "60fe9cc17450c4fa623c69357efc22ac1b9ff0ec15d11c441292cff92c58c7ae",
    ROOT / "GUI/SpriteParts/ui_reignbeta_social_event/reign_wilderness_event_modern_shell.png":
        "6c26d1f677db92ae97b866f9366f68a5f0fee8b87935cb96867c014df268b42a",
}

# Inclusive changed rows, with untouched source rows on either side.
DIVIDERS = ((734, 739, 733, 740), (778, 784, 777, 785))
LEFT, RIGHT = 624, 1630


def refine(path: Path, expected_hash: str) -> None:
    actual_hash = sha256(path.read_bytes()).hexdigest()
    if actual_hash != expected_hash:
        raise ValueError(f"Unexpected source hash for {path}: {actual_hash}")

    with Image.open(path) as source:
        image = source.convert("RGBA")
    if image.size != (1672, 941):
        raise ValueError(f"Unexpected shell dimensions: {image.size}")

    for first, last, upper, lower in DIVIDERS:
        for x in range(LEFT, RIGHT):
            above = image.getpixel((x, upper))
            below = image.getpixel((x, lower))
            for y in range(first, last + 1):
                fraction = (y - upper) / (lower - upper)
                pixel = tuple(round(a + (b - a) * fraction) for a, b in zip(above, below))
                image.putpixel((x, y), pixel)

    image.save(path, format="PNG", optimize=True)
    print(f"{path}: {sha256(path.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    for target, source_hash in FILES.items():
        refine(target, source_hash)
