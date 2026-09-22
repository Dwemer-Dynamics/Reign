#!/usr/bin/env python3
"""Prepare the supplied Royal Court artwork for Gauntlet and the XML previewer.

ChatGPT Image exports sometimes contain a baked checkerboard instead of a real
alpha channel.  This script removes that neutral, bright backing, scales every
asset to the logical 1920x1080 Court specification, and writes stable sprite
filenames into the module's Court sprite-parts directory.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


ASSETS = {
    # filename: (sprite filename, output size, trim_subject)
    "ChatGPT Image Jul 16, 2026, 09_07_35 PM (1).png": ("reign_court_throne_background.png", (1938, 811), False),
    "ChatGPT Image Jul 16, 2026, 09_07_35 PM (2).png": ("reign_court_outer_frame.png", (1920, 1080), False),
    "ChatGPT Image Jul 16, 2026, 09_07_35 PM (3).png": ("reign_court_status_bar.png", (1856, 88), False),
    "ChatGPT Image Jul 16, 2026, 09_07_36 PM (4).png": ("reign_court_identity_frame.png", (350, 88), False),
    "ChatGPT Image Jul 16, 2026, 09_07_36 PM (5).png": ("reign_court_status_frame.png", (251, 88), False),
    "ChatGPT Image Jul 16, 2026, 09_07_36 PM (6).png": ("reign_court_docket_panel.png", (520, 828), False),
    "ChatGPT Image Jul 16, 2026, 09_07_36 PM (7).png": ("reign_court_docket_header.png", (472, 68), False),
    "ChatGPT Image Jul 16, 2026, 09_07_37 PM (8).png": ("reign_court_docket_card.png", (440, 112), False),
    "ChatGPT Image Jul 16, 2026, 09_07_37 PM (9).png": ("reign_court_command_rail.png", (392, 828), False),
    "ChatGPT Image Jul 16, 2026, 09_07_37 PM (10).png": ("reign_court_title_plaque.png", (468, 74), False),
    "ChatGPT Image Jul 16, 2026, 09_07_42 PM (1).png": ("reign_court_command_button.png", (336, 96), False),
    "ChatGPT Image Jul 16, 2026, 09_07_42 PM (2).png": ("reign_court_command_button_hover.png", (336, 96), False),
    "ChatGPT Image Jul 16, 2026, 09_07_42 PM (3).png": ("reign_court_command_button_pressed.png", (336, 96), False),
    "ChatGPT Image Jul 16, 2026, 09_07_42 PM (4).png": ("reign_court_bottom_bar.png", (1856, 84), False),
    "ChatGPT Image Jul 16, 2026, 09_07_43 PM (5).png": ("reign_court_control_button.png", (150, 50), False),
    "ChatGPT Image Jul 16, 2026, 09_07_43 PM (6).png": ("reign_court_advance_button.png", (300, 50), False),
    "ChatGPT Image Jul 16, 2026, 09_07_43 PM (7).png": ("reign_court_banner_backing.png", (150, 300), False),
    "ChatGPT Image Jul 16, 2026, 09_07_43 PM (8).png": ("reign_court_banner_overlay.png", (180, 340), False),
    "ChatGPT Image Jul 16, 2026, 09_07_44 PM (9).png": ("reign_court_scroll_track.png", (12, 680), False),
    "ChatGPT Image Jul 16, 2026, 09_07_44 PM (10).png": ("reign_court_scroll_handle.png", (18, 80), False),
    "ChatGPT Image Jul 16, 2026, 09_07_52 PM (1).png": ("reign_court_status_funds.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_52 PM (2).png": ("reign_court_status_influence.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_53 PM (3).png": ("reign_court_status_renown.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_53 PM (4).png": ("reign_court_status_supply.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_53 PM (5).png": ("reign_court_status_strength.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_53 PM (6).png": ("reign_court_status_presence.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_54 PM (7).png": ("reign_court_icon_war_council.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_54 PM (8).png": ("reign_court_icon_spymaster.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_54 PM (9).png": ("reign_court_icon_ambassadors.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_07_54 PM (10).png": ("reign_court_icon_economic_report.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (1).png": ("reign_court_icon_keep.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (2).png": ("reign_court_docket_justice.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (3).png": ("reign_court_docket_diplomacy.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (4).png": ("reign_court_docket_accounts.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (5).png": ("reign_court_docket_social.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (6).png": ("reign_court_docket_military.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (7).png": ("reign_court_docket_correspondence.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (8).png": ("reign_court_docket_urgent.png", (64, 64), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (9).png": ("reign_court_reign_medallion.png", (96, 96), True),
    "ChatGPT Image Jul 16, 2026, 09_08_00 PM (10).png": ("reign_court_divider.png", (256, 24), False),
}


def remove_checkerboard(image: Image.Image) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA")).copy()
    rgb = rgba[:, :, :3].astype(np.int16)
    low = rgb.min(axis=2)
    chroma = rgb.max(axis=2) - low
    # The supplied checkerboards are neutral and bright.  Antique gold remains
    # opaque because it has substantially greater chroma.
    rgba[:, :, 3][(low >= 188) & (chroma <= 18)] = 0
    return Image.fromarray(rgba, "RGBA")


def fit_subject(image: Image.Image, size: tuple[int, int], padding: int = 2) -> Image.Image:
    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if not bbox:
        return Image.new("RGBA", size)
    subject = image.crop(bbox)
    available = (max(1, size[0] - padding * 2), max(1, size[1] - padding * 2))
    subject.thumbnail(available, Image.Resampling.LANCZOS)
    output = Image.new("RGBA", size)
    output.alpha_composite(subject, ((size[0] - subject.width) // 2, (size[1] - subject.height) // 2))
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, default=Path.home() / "Downloads")
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[2] / "GUI" / "SpriteParts" / "ui_reignbeta_court",
    )
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    missing = [name for name in ASSETS if not (args.source_dir / name).is_file()]
    if missing:
        raise SystemExit("Missing Court source images:\n" + "\n".join(missing))

    for source_name, (output_name, size, trim_subject) in ASSETS.items():
        source = Image.open(args.source_dir / source_name)
        if output_name == "reign_court_throne_background.png":
            result = source.convert("RGBA").resize(size, Image.Resampling.LANCZOS)
        else:
            keyed = remove_checkerboard(source)
            result = fit_subject(keyed, size) if trim_subject else keyed.resize(size, Image.Resampling.LANCZOS)
        result.save(args.output_dir / output_name, optimize=True)
        print(f"{output_name}: {result.width}x{result.height}")


if __name__ == "__main__":
    main()
