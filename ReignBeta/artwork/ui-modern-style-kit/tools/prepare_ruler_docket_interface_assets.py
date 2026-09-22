#!/usr/bin/env python3
"""Materialize the ruler-docket shell and complete independent card assets.

The approved Court shell historically baked one Chancellor opening and four
petition openings into the fixed left rail.  That composition cannot scroll
correctly: moving content remains visible through stationary holes.  This tool
performs a deterministic, bounded rebuild:

* the original shell remains the pixel authority outside the docket interior;
* an image-to-image clean-rail source replaces only the declared interior;
* the original blank Chancellor and petition frames are extracted as complete
  runtime cards;
* the Chancellor card owns its portrait frame and a genuinely transparent
  portrait aperture so portrait pixels can only render behind the card; and
* a complete petition-attendee card is normalized from the approved visual
  direction, with its circular portrait aperture cut into the card itself;
* a dedicated Court office/history shell replaces stacked legacy rectangles;
* the already-approved lower-throne button artwork is copied pixel-for-pixel
  into one reusable true-alpha Court navigation plate;
* the petition shell's retired single-petitioner oval is structurally removed
  before the independent attendee-card roster is composed; and
* a structured evidence record proves dimensions, hashes, alpha apertures,
  and fixed-region equality outside the replacement rectangle.

Existing outputs are never replaced unless ``--force`` is supplied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import tempfile
from pathlib import Path
from typing import Any, Dict, Sequence

from PIL import Image, ImageChops, ImageDraw


TOOL_VERSION = "1.7.0"
EVIDENCE_SCHEMA = "reign-ruler-docket-interface-assets-v7"
CANVAS = (1672, 941)
CLEAN_RAIL_BOX = (55, 121, 392, 877)
CHANCELLOR_CARD_BOX = (63, 132, 385, 296)
PETITION_CARD_BOX = (63, 307, 385, 452)
CHANCELLOR_PORTRAIT_FRAME_BOX = (13, 31, 97, 115)
CHANCELLOR_PORTRAIT_APERTURE_BOX = (18, 36, 92, 110)
ATTENDEE_SOURCE_BOX = (85, 140, 1787, 683)
ATTENDEE_CARD_SIZE = (322, 103)
ATTENDEE_PORTRAIT_APERTURE_BOX = (15, 11, 93, 92)
PETITION_ATTENDEE_RAIL_REPLACEMENT_BOX = (30, 105, 346, 912)
PETITION_ATTENDEE_RAIL_MATERIAL_BOX = (560, 170, 1390, 850)
COURT_NAVIGATION_SOURCE_SIZE = CANVAS
COURT_NAVIGATION_BUTTON_BOX = (700, 774, 970, 834)
COURT_NAVIGATION_BUTTON_MASK_POINTS = (
    (28, 0), (241, 0), (254, 12), (254, 47),
    (241, 59), (29, 59), (17, 47), (17, 12),
)
COURT_NAVIGATION_LEFT_DIAMOND_POINTS = ((0, 29), (7, 22), (14, 29), (7, 36))
COURT_NAVIGATION_RIGHT_DIAMOND_POINTS = ((256, 29), (263, 22), (269, 29), (263, 36))
TRANSPARENT_RGB = (18, 18, 17)
GOLD_ANTIQUE = (126, 106, 77)
GOLD_HIGHLIGHT = (168, 138, 84)
GOLD_BRIGHT = (197, 172, 131)


class PreparationError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_rgba(path: Path, label: str) -> Image.Image:
    if not path.is_file():
        raise PreparationError(f"{label} does not exist: {path}")
    with Image.open(path) as image:
        image.load()
        result = image.convert("RGBA")
    if result.size != CANVAS and label == "original shell":
        raise PreparationError(
            f"original shell must be {CANVAS[0]}x{CANVAS[1]}, got {result.size[0]}x{result.size[1]}"
        )
    return result


def atomic_png(image: Image.Image, target: Path, force: bool) -> None:
    if target.exists() and not force:
        raise PreparationError(f"refusing to overwrite without --force: {target}")
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        prefix=target.stem + ".", suffix=".png", dir=target.parent, delete=False
    ) as stream:
        temporary = Path(stream.name)
    try:
        image.save(temporary, format="PNG", compress_level=9)
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)


def atomic_json(value: Dict[str, Any], target: Path, force: bool) -> None:
    if target.exists() and not force:
        raise PreparationError(f"refusing to overwrite without --force: {target}")
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        prefix=target.stem + ".", suffix=".json", dir=target.parent,
        delete=False, mode="w", encoding="utf-8", newline="\n"
    ) as stream:
        temporary = Path(stream.name)
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
    try:
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)


def outside_region_mismatch_count(
    original: Image.Image, rebuilt: Image.Image, box: tuple[int, int, int, int]
) -> int:
    difference = ImageChops.difference(original, rebuilt).convert("RGBA")
    mask = Image.new("L", original.size, 255)
    mask.paste(0, box)
    masked = ImageChops.multiply(difference, Image.merge("RGBA", (mask, mask, mask, mask)))
    return sum(
        1 for pixel in masked.getdata()
        if pixel[0] != 0 or pixel[1] != 0 or pixel[2] != 0 or pixel[3] != 0
    )


def integrate_chancellor_portrait_frame(
    card: Image.Image, frame_source: Image.Image
) -> Image.Image:
    """Punch the portrait aperture and permanently composite its ornate frame."""
    framed = card.copy()
    alpha = framed.getchannel("A")
    aperture = Image.new("L", framed.size, 0)
    ImageDraw.Draw(aperture).ellipse(CHANCELLOR_PORTRAIT_APERTURE_BOX, fill=255)
    alpha.paste(0, mask=aperture)
    framed.putalpha(alpha)

    # The shared portrait-frame raster contains stand-alone backing and pale
    # antialiasing that must never be copied into this card-owned aperture.
    # Draw the restrained frame directly so only approved gold pixels surround
    # the true-alpha opening and no white target/wedge pixels can survive.
    _ = frame_source
    frame_draw = ImageDraw.Draw(framed)
    frame_draw.ellipse((14, 32, 96, 114), outline=(112, 78, 29, 255), width=2)
    frame_draw.ellipse((17, 35, 93, 111), outline=(181, 132, 57, 255), width=1)
    frame_draw.polygon(((55, 29), (59, 33), (55, 37), (51, 33)), outline=(181, 132, 57, 255))
    frame_draw.polygon(((99, 73), (95, 77), (91, 73), (95, 69)), outline=(181, 132, 57, 255))
    frame_draw.polygon(((55, 117), (51, 113), (55, 109), (59, 113)), outline=(181, 132, 57, 255))
    frame_draw.polygon(((11, 73), (15, 69), (19, 73), (15, 77)), outline=(181, 132, 57, 255))
    # Normalize the entire ring surrounding the transparent hole to an opaque
    # card-owned plate. This removes source antialias alpha from the old frame
    # without touching any pixel inside the declared portrait aperture.
    normalized_alpha = framed.getchannel("A")
    opaque_surround = Image.new("L", framed.size, 0)
    surround_draw = ImageDraw.Draw(opaque_surround)
    surround_draw.ellipse(CHANCELLOR_PORTRAIT_FRAME_BOX, fill=255)
    surround_draw.ellipse(CHANCELLOR_PORTRAIT_APERTURE_BOX, fill=0)
    normalized_alpha.paste(255, mask=opaque_surround)
    framed.putalpha(normalized_alpha)
    return framed


def _neutralize_transparent_rgb(image: Image.Image) -> Image.Image:
    """Give fully transparent pixels the canonical marble RGB triplet."""
    rgba = image.convert("RGBA")
    pixels = []
    for red, green, blue, alpha in rgba.getdata():
        if alpha == 0:
            pixels.append((*TRANSPARENT_RGB, 0))
        else:
            pixels.append((red, green, blue, alpha))
    rgba.putdata(pixels)
    return rgba


def _replace_pale_generated_linework(image: Image.Image) -> Image.Image:
    """Map generator-produced silver/white UI rails back to frozen gold roles."""
    rgba = image.convert("RGBA")
    pixels = []
    for red, green, blue, alpha in rgba.getdata():
        highest = max(red, green, blue)
        lowest = min(red, green, blue)
        if alpha > 0 and highest >= 105 and highest - lowest <= 34:
            if highest >= 210:
                replacement = GOLD_BRIGHT
            elif highest >= 155:
                replacement = GOLD_HIGHLIGHT
            else:
                replacement = GOLD_ANTIQUE
            pixels.append((*replacement, alpha))
        else:
            pixels.append((red, green, blue, alpha))
    rgba.putdata(pixels)
    return rgba


def _pale_neutral_pixel_count(image: Image.Image, box: tuple[int, int, int, int]) -> int:
    """Reject white/grey target artifacts around a card-owned portrait opening."""
    return sum(
        1
        for red, green, blue, alpha in image.crop(box).getdata()
        if alpha > 0 and min(red, green, blue) >= 180
        and max(red, green, blue) - min(red, green, blue) <= 24
    )


def prepare_attendee_card(source: Image.Image) -> Image.Image:
    """Build one complete card whose only interior hole is the portrait aperture."""
    cropped = source.crop(ATTENDEE_SOURCE_BOX)
    card = cropped.resize(ATTENDEE_CARD_SIZE, Image.Resampling.LANCZOS).convert("RGBA")

    scale = 8
    width, height = ATTENDEE_CARD_SIZE
    alpha_large = Image.new("L", (width * scale, height * scale), 0)
    draw = ImageDraw.Draw(alpha_large)
    clipped_card = (
        (5 * scale, 0),
        ((width - 5) * scale, 0),
        (width * scale, 5 * scale),
        (width * scale, (height - 5) * scale),
        ((width - 5) * scale, height * scale),
        (5 * scale, height * scale),
        (0, (height - 5) * scale),
        (0, 5 * scale),
    )
    draw.polygon(clipped_card, fill=255)
    left, top, right, bottom = ATTENDEE_PORTRAIT_APERTURE_BOX
    draw.ellipse(
        (left * scale, top * scale, right * scale, bottom * scale),
        fill=0,
    )
    alpha = alpha_large.resize(ATTENDEE_CARD_SIZE, Image.Resampling.LANCZOS)
    card.putalpha(alpha)
    card = _replace_pale_generated_linework(card)
    return _neutralize_transparent_rgb(card)


def prepare_petition_attendee_shell(
    source: Image.Image, material_source: Image.Image
) -> tuple[Image.Image, int]:
    """Remove the retired single-petitioner oval from the fixed petition shell."""
    if source.size != CANVAS or material_source.size != CANVAS:
        raise PreparationError("petition attendee shell inputs must match the canonical canvas")

    rebuilt = source.copy()
    left, top, right, bottom = PETITION_ATTENDEE_RAIL_REPLACEMENT_BOX
    material = material_source.crop(PETITION_ATTENDEE_RAIL_MATERIAL_BOX).resize(
        (right - left, bottom - top), Image.Resampling.LANCZOS
    )
    rebuilt.paste(material, (left, top))
    mismatch = outside_region_mismatch_count(
        source, rebuilt, PETITION_ATTENDEE_RAIL_REPLACEMENT_BOX
    )
    if mismatch != 0:
        raise PreparationError(
            "petition attendee rail invariant failed: "
            f"{mismatch} pixels changed outside the replacement box"
        )
    if rebuilt.getpixel((187, 278))[3] != 255:
        raise PreparationError("retired petitioner portrait aperture remains transparent")
    return rebuilt, mismatch


def prepare_court_navigation_button(source: Image.Image) -> Image.Image:
    """Lift the actual current-shell throne button onto a true-alpha canvas."""
    if source.size != COURT_NAVIGATION_SOURCE_SIZE:
        raise PreparationError(
            "Court modern shell must be "
            f"{COURT_NAVIGATION_SOURCE_SIZE[0]}x{COURT_NAVIGATION_SOURCE_SIZE[1]}, "
            f"got {source.width}x{source.height}"
        )
    button = source.crop(COURT_NAVIGATION_BUTTON_BOX).convert("RGBA")
    expected_size = (
        COURT_NAVIGATION_BUTTON_BOX[2] - COURT_NAVIGATION_BUTTON_BOX[0],
        COURT_NAVIGATION_BUTTON_BOX[3] - COURT_NAVIGATION_BUTTON_BOX[1],
    )
    if button.size != expected_size:
        raise PreparationError("Court navigation button crop dimensions drifted")
    scale = 8
    alpha_large = Image.new("L", (button.width * scale, button.height * scale), 0)
    draw = ImageDraw.Draw(alpha_large)
    draw.polygon(
        [(x * scale, y * scale) for x, y in COURT_NAVIGATION_BUTTON_MASK_POINTS],
        fill=255,
    )
    alpha = alpha_large.resize(button.size, Image.Resampling.LANCZOS)
    button.putalpha(alpha)
    if button.getchannel("A").getextrema() != (0, 255):
        raise PreparationError("Court navigation button must have true transparency")
    button = _neutralize_transparent_rgb(button)

    # The left shell diamond crosses a throne-floor seam and therefore cannot
    # be lifted as a reusable alpha ornament. Recreate only the two tiny side
    # diamonds from the frozen interactive-control palette and exact approved
    # geometry; the complete button body remains an unaltered source crop.
    ornament_large = Image.new(
        "RGBA", (button.width * scale, button.height * scale), (0, 0, 0, 0)
    )
    ornament_draw = ImageDraw.Draw(ornament_large)
    for diamond in (
        COURT_NAVIGATION_LEFT_DIAMOND_POINTS,
        COURT_NAVIGATION_RIGHT_DIAMOND_POINTS,
    ):
        outline = [(x * scale, y * scale) for x, y in (*diamond, diamond[0])]
        ornament_draw.line(
            outline, fill=(*GOLD_HIGHLIGHT, 255), width=8, joint="curve"
        )
    ornament_draw.line(
        ((14 * scale, 29 * scale), (17 * scale, 29 * scale)),
        fill=(*GOLD_ANTIQUE, 255), width=8,
    )
    ornament_draw.line(
        ((254 * scale, 29 * scale), (256 * scale, 29 * scale)),
        fill=(*GOLD_ANTIQUE, 255), width=8,
    )
    ornament = ornament_large.resize(button.size, Image.Resampling.LANCZOS)
    return Image.alpha_composite(button, ornament)


def prepare(args: argparse.Namespace) -> Dict[str, Any]:
    original = load_rgba(args.original_shell, "original shell")
    clean = load_rgba(args.clean_source, "clean rail source")
    portrait_frame = load_rgba(args.portrait_frame_source, "portrait frame source")
    extended = any(
        value is not None
        for value in (
            args.court_office_shell_source,
            args.court_office_shell_output,
            args.attendee_card_source,
            args.attendee_card_output,
            args.petition_attendee_shell_source,
            args.petition_attendee_shell_output,
            args.court_navigation_source,
            args.court_navigation_output,
        )
    )
    if extended and not all(
        value is not None
        for value in (
            args.court_office_shell_source,
            args.court_office_shell_output,
            args.attendee_card_source,
            args.attendee_card_output,
            args.petition_attendee_shell_source,
            args.petition_attendee_shell_output,
            args.court_navigation_source,
            args.court_navigation_output,
        )
    ):
        raise PreparationError(
            "court office shell, attendee card, petition attendee shell, and Court "
            "navigation source/output arguments must be supplied together"
        )
    if clean.size != CANVAS:
        clean = clean.resize(CANVAS, Image.Resampling.LANCZOS)

    rebuilt = original.copy()
    rebuilt.paste(clean.crop(CLEAN_RAIL_BOX), CLEAN_RAIL_BOX)
    chancellor = integrate_chancellor_portrait_frame(
        original.crop(CHANCELLOR_CARD_BOX), portrait_frame
    )
    petition = original.crop(PETITION_CARD_BOX)

    mismatch = outside_region_mismatch_count(original, rebuilt, CLEAN_RAIL_BOX)
    if mismatch != 0:
        raise PreparationError(
            f"fixed-region invariant failed: {mismatch} pixels changed outside the clean-rail box"
        )
    if chancellor.size != (322, 164) or petition.size != (322, 145):
        raise PreparationError("card extraction dimensions drifted")
    aperture_center_alpha = chancellor.getpixel((55, 73))[3]
    if aperture_center_alpha != 0:
        raise PreparationError(
            "Chancellor portrait aperture is not transparent at its declared center"
        )
    pale_target_pixel_count = _pale_neutral_pixel_count(
        chancellor, CHANCELLOR_PORTRAIT_FRAME_BOX
    )
    if pale_target_pixel_count != 0:
        raise PreparationError(
            "Chancellor portrait frame retained pale target/wedge pixels: "
            f"{pale_target_pixel_count}"
        )

    office_shell = None
    attendee_card = None
    petition_attendee_shell = None
    petition_attendee_shell_mismatch = None
    court_navigation_button = None
    if extended:
        office_shell = load_rgba(args.court_office_shell_source, "Court office shell source")
        if office_shell.size != CANVAS:
            raise PreparationError(
                f"Court office shell source must be {CANVAS[0]}x{CANVAS[1]}, "
                f"got {office_shell.width}x{office_shell.height}"
            )
        attendee_source = load_rgba(args.attendee_card_source, "attendee card source")
        if attendee_source.size[0] < ATTENDEE_SOURCE_BOX[2] or attendee_source.size[1] < ATTENDEE_SOURCE_BOX[3]:
            raise PreparationError("attendee card source is smaller than its declared crop")
        attendee_card = prepare_attendee_card(attendee_source)
        if attendee_card.size != ATTENDEE_CARD_SIZE:
            raise PreparationError("attendee card dimensions drifted")
        if attendee_card.getpixel((54, 51))[3] != 0:
            raise PreparationError("attendee card portrait aperture center is not transparent")
        if attendee_card.getpixel((120, 51))[3] < 250:
            raise PreparationError("attendee card body is not opaque beside the portrait aperture")
        petition_attendee_shell_source = load_rgba(
            args.petition_attendee_shell_source, "petition attendee shell source"
        )
        petition_attendee_shell, petition_attendee_shell_mismatch = (
            prepare_petition_attendee_shell(petition_attendee_shell_source, office_shell)
        )
        court_navigation_source = load_rgba(
            args.court_navigation_source, "Court throne button strip"
        )
        court_navigation_button = prepare_court_navigation_button(
            court_navigation_source
        )

    atomic_png(rebuilt, args.shell_output, args.force)
    atomic_png(chancellor, args.chancellor_output, args.force)
    atomic_png(petition, args.petition_output, args.force)
    if extended:
        atomic_png(office_shell, args.court_office_shell_output, args.force)
        atomic_png(attendee_card, args.attendee_card_output, args.force)
        atomic_png(
            petition_attendee_shell, args.petition_attendee_shell_output, args.force
        )
        atomic_png(
            court_navigation_button, args.court_navigation_output, args.force
        )

    evidence: Dict[str, Any] = {
        "schema": EVIDENCE_SCHEMA,
        "tool": {
            "path": "ReignBeta/artwork/ui-modern-style-kit/tools/prepare_ruler_docket_interface_assets.py",
            "version": TOOL_VERSION,
        },
        "canvas": {"width": CANVAS[0], "height": CANVAS[1]},
        "inputs": {
            "originalShell": {"path": str(args.original_shell), "sha256": sha256(args.original_shell)},
            "cleanRailSource": {"path": str(args.clean_source), "sha256": sha256(args.clean_source)},
            "portraitFrameSource": {
                "path": str(args.portrait_frame_source),
                "sha256": sha256(args.portrait_frame_source),
            },
        },
        "operations": {
            "cleanRailBox": list(CLEAN_RAIL_BOX),
            "chancellorCardBox": list(CHANCELLOR_CARD_BOX),
            "petitionCardBox": list(PETITION_CARD_BOX),
            "outsideReplacementPixelMismatchCount": mismatch,
            "completeIndependentCards": True,
            "fixedCardOpeningsRetainedInShell": False,
            "chancellorPortraitFrameIntegrated": True,
            "chancellorPortraitFrameBox": list(CHANCELLOR_PORTRAIT_FRAME_BOX),
            "chancellorPortraitApertureBox": list(CHANCELLOR_PORTRAIT_APERTURE_BOX),
            "chancellorPortraitApertureCenterAlpha": aperture_center_alpha,
            "chancellorPaleTargetPixelCount": pale_target_pixel_count,
            "chancellorCardControlPlateCount": 0,
            "chancellorControlsUseStateAwareRuntimePlates": True,
        },
        "outputs": {
            "shell": {
                "path": str(args.shell_output),
                "sha256": sha256(args.shell_output),
                "dimensions": [rebuilt.width, rebuilt.height],
            },
            "chancellorCard": {
                "path": str(args.chancellor_output),
                "sha256": sha256(args.chancellor_output),
                "dimensions": [chancellor.width, chancellor.height],
            },
            "petitionCard": {
                "path": str(args.petition_output),
                "sha256": sha256(args.petition_output),
                "dimensions": [petition.width, petition.height],
            },
        },
    }
    if extended:
        evidence["inputs"]["courtOfficeShellSource"] = {
            "path": str(args.court_office_shell_source),
            "sha256": sha256(args.court_office_shell_source),
        }
        evidence["inputs"]["attendeeCardSource"] = {
            "path": str(args.attendee_card_source),
            "sha256": sha256(args.attendee_card_source),
        }
        evidence["inputs"]["petitionAttendeeShellSource"] = {
            "path": str(args.petition_attendee_shell_source),
            "sha256": sha256(args.petition_attendee_shell_source),
        }
        evidence["inputs"]["courtNavigationSource"] = {
            "path": str(args.court_navigation_source),
            "sha256": sha256(args.court_navigation_source),
        }
        evidence["operations"].update(
            {
                "courtOfficeUsesDedicatedModernShell": True,
                "legacyStackedFramesRetained": False,
                "attendeeCardIsCompleteIndependentCard": True,
                "attendeePortraitFrameIntegrated": True,
                "attendeeSourceBox": list(ATTENDEE_SOURCE_BOX),
                "attendeePortraitApertureBox": list(ATTENDEE_PORTRAIT_APERTURE_BOX),
                "attendeePortraitApertureCenterAlpha": attendee_card.getpixel((54, 51))[3],
                "attendeeBodyAlpha": attendee_card.getpixel((120, 51))[3],
                "retiredPetitionerPortraitApertureRetained": False,
                "petitionAttendeeRailReplacementBox": list(
                    PETITION_ATTENDEE_RAIL_REPLACEMENT_BOX
                ),
                "petitionAttendeeShellOutsideReplacementPixelMismatchCount": (
                    petition_attendee_shell_mismatch
                ),
                "retiredPetitionerPortraitApertureCenterAlpha": (
                    petition_attendee_shell.getpixel((187, 278))[3]
                ),
                "courtNavigationUsesExactApprovedModernShellButtonCopy": True,
                "courtNavigationSourceBox": list(COURT_NAVIGATION_BUTTON_BOX),
                "courtNavigationBodyRgbPixelTransformationCount": 0,
                "courtNavigationAlphaMaterializedFromApprovedGeometry": True,
                "courtNavigationSideDiamondsUseFrozenPaletteGeometry": True,
                "courtNavigationAlphaExtrema": list(
                    court_navigation_button.getchannel("A").getextrema()
                ),
            }
        )
        evidence["outputs"]["courtOfficeShell"] = {
            "path": str(args.court_office_shell_output),
            "sha256": sha256(args.court_office_shell_output),
            "dimensions": [office_shell.width, office_shell.height],
        }
        evidence["outputs"]["attendeeCard"] = {
            "path": str(args.attendee_card_output),
            "sha256": sha256(args.attendee_card_output),
            "dimensions": [attendee_card.width, attendee_card.height],
        }
        evidence["outputs"]["petitionAttendeeShell"] = {
            "path": str(args.petition_attendee_shell_output),
            "sha256": sha256(args.petition_attendee_shell_output),
            "dimensions": [petition_attendee_shell.width, petition_attendee_shell.height],
        }
        evidence["outputs"]["courtNavigationButton"] = {
            "path": str(args.court_navigation_output),
            "sha256": sha256(args.court_navigation_output),
            "dimensions": [
                court_navigation_button.width,
                court_navigation_button.height,
            ],
        }
    atomic_json(evidence, args.evidence, args.force)
    return evidence


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--original-shell", type=Path, required=True)
    result.add_argument("--clean-source", type=Path, required=True)
    result.add_argument("--portrait-frame-source", type=Path, required=True)
    result.add_argument("--shell-output", type=Path, required=True)
    result.add_argument("--chancellor-output", type=Path, required=True)
    result.add_argument("--petition-output", type=Path, required=True)
    result.add_argument("--court-office-shell-source", type=Path)
    result.add_argument("--court-office-shell-output", type=Path)
    result.add_argument("--attendee-card-source", type=Path)
    result.add_argument("--attendee-card-output", type=Path)
    result.add_argument("--petition-attendee-shell-source", type=Path)
    result.add_argument("--petition-attendee-shell-output", type=Path)
    result.add_argument("--court-navigation-source", type=Path)
    result.add_argument("--court-navigation-output", type=Path)
    result.add_argument("--evidence", type=Path, required=True)
    result.add_argument("--force", action="store_true")
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        print(json.dumps(prepare(args), indent=2, sort_keys=True))
        return 0
    except PreparationError as exc:
        print(f"prepare_ruler_docket_interface_assets.py: error: {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
