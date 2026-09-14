# Reign Interface Design Rules

This document is the mandatory human-readable standard for all Reign interface design and implementation. It applies to standalone Gauntlet screens, embedded panels, cards, popups, native-screen augmentations, portraits, banners, generated scene apertures, controls, hover/selected/disabled states, and future redesigns.

The visual target is the restrained black-marble and antique-gold court style shown by the approved Spymaster, Correspondence, Memories Book, Diplomacy Announcement, Family Chambers, and Royal Council designs. The result must feel formal, quiet, readable, symmetrical where appropriate, and materially consistent. It must not become bright fantasy gold, brown parchment UI, generic grey game UI, or a mixture of old and new Reign graphics.

## Authority and precedence

Use these authorities in this order:

1. `ReignBeta/artwork/ui-modern-style-kit/asset-manifest.json` identifies the one latest approved reference for every interface and records its SHA-256. Rejected or superseded concepts are not design authority.
2. `ReignBeta/GUI/UiCalibration/modern-style-contract.json` defines fixed/dynamic composition, portrait apertures, typography, protected content, fidelity, and acceptance thresholds.
3. `ReignBeta/artwork/ui-modern-style-kit/palette.json` defines the only permitted base RGBA tokens for modern neutral materials, lines, ornaments, semantic states, and live text.
4. The applicable PNG in `ReignBeta/artwork/ui-modern-style-kit/approved-references/` is the exact authority for that screen's fixed geometry, visual hierarchy, ornament density, spacing, and material treatment.

Do not estimate a color from a screenshot when a token exists. Do not use a visually similar hex value. Do not copy styling from a legacy XML brush, old baked frame, rejected concept, or temporary mockup. If the authorities conflict, stop and correct the machine-readable contract or manifest deliberately; never create a silent local exception.

The normalized reference canvas is exactly **1672 × 941**. Scaling to another resolution must preserve aspect ratio, line weight, spacing relationships, and declared custom-scale behavior. Stretching a fixed shell, portrait, card, or ornament is forbidden.

## Exact frozen palette

These are RGBA values in `#RRGGBBAA` form. They are IDs, not suggestions.

| Token | Exact RGBA | Required use |
|---|---:|---|
| `canvasVoid` | `#030303FF` | Outside void and deepest negative space |
| `canvasBlack` | `#070808FF` | Main black canvas |
| `marbleBase` | `#121211FF` | Primary black-marble material |
| `marbleShadow` | `#0C0C0BFF` | Recessed marble shadow |
| `marbleVein` | `#23211D5C` | Subordinate marble veins, never above the declared alpha |
| `panelSmoke` | `#10100FFF` | Low-contrast inset/recessed surface |
| `panelStandard` | `#171717FF` | Standard panel surface |
| `panelRaised` | `#1A1917FF` | Ordinary raised card or control |
| `panelActive` | `#232323FF` | Selected, active, or deliberately emphasized surface only |
| `inputBlack` | `#0B0B0BFF` | Text-entry and deep input field |
| `borderBronze` | `#352C20FF` | Recessive hairlines and low-priority structure |
| `goldAntique` | `#7E6A4DFF` | Primary frame lines, dividers, metadata, and restrained ornament |
| `goldHighlight` | `#A88A54FF` | Titles, section headings, focal ornaments, and emphasized names |
| `goldBright` | `#C5AC83FF` | Small highlights only; never a full frame, large button, or broad border |
| `textPrimary` | `#C5BDAFFF` | Main readable body copy |
| `textSecondary` | `#8A8883FF` | Supporting body copy and subordinate facts |
| `textMuted` | `#706F6AFF` | Quiet captions, unavailable explanations, and tertiary information |
| `semanticDanger` | `#C35B32FF` | Negative values, shortages, losses, expenses, or explicitly difficult/challenging states only |
| `disabledNeutral` | `#4A4945FF` | Disabled text and controls |
| `emptySilhouette` | `#595956FF` | Empty portrait/occupancy silhouettes only |

Material rules:

- Neutral panels may use only `canvasBlack`, `marbleBase`, `panelSmoke`, `panelStandard`, `panelRaised`, or `panelActive`. Brown neutral panels are forbidden.
- `panelRaised` is the ordinary lifted-card color. `panelActive` is reserved for selected or emphasized states.
- Marble texture must remain subtle and subordinate to text, portraits, and borders. It may vary luminance locally but may not introduce another base hue.
- `borderBronze` provides depth; `goldAntique` carries most lines; `goldHighlight` establishes hierarchy; `goldBright` is a scarce glint. Broad bright-yellow or orange-gold framing is forbidden.
- `semanticDanger` is informational, not decorative. Never use it for neutral ornaments or ordinary headings.

## Shell, frame, and ornament language

- Use black marble, thin antique-gold lines, clipped or stepped corners, small diamonds, restrained central dividers, and occasional compact crowned-`R` heraldry.
- Frames are thin and precise. Avoid thick bevels, glowing edges, oversized corner filigree, excessive double borders, or ornament that competes with content.
- All framing must be built into the complete artwork of its owning card, panel, control, or interface. Do not assemble a design by positioning separate decorative frames, rings, borders, or corner pieces over items. Independently aligned frame overlays are a known clipping and alignment failure and are forbidden. For framed graphics, the owning artwork supplies the frame and its genuinely transparent center; only the graphic is placed behind that opening. Keep the frame and opening in the owner's coordinate system so layout, scaling, and movement cannot separate them.
- Large screens should have one clear outer boundary and a limited number of nested framed regions. Every nested frame must communicate hierarchy or grouping.
- Fixed titles, section labels, and ornament-integrated captions belong in the approved raster shell when the reference bakes them into the artwork. Do not leave a second hidden or transparent runtime label behind them.
- A replacement shell or card must replace the previous fixed graphic completely. Old rectangles, borders, button plates, portrait holes, and decorative pieces must be removed or made structurally absent—not merely covered by a newer layer.
- Empty, scrolled, selected, hovered, pressed, focused, disabled, and error states must retain the same fixed geometry and token roles. State changes are not permission to use legacy brushes or alternate colors.

## Layout and hierarchy

- Preserve the approved screen's major composition rather than forcing every feature into one template. Use symmetry for ceremonial or council layouts and purposeful asymmetry for operational screens.
- Use generous black negative space. Do not fill every region with decoration, labels, or cards.
- Align text and numbers inside their owning card or panel, not to a neighboring decorative frame. Repeated cards must share identical internal anchors.
- Keep labels and values close enough to read as one row. Do not spread a metric name and its value across unrelated frame pieces.
- Every card type that needs to scroll must consist of complete, individual cards rendered **above the interface shell/background**, inside a bounded clip viewport. This applies equally to portrait, banner, text-only, report, action, and other cards. Never place scrolling cards beneath holes or cutouts in the fixed interface, or scroll one long strip beneath a series of fixed transparent holes.
- Scrolling must land on exact card boundaries, clamp at both ends, show the first and last card fully, and never rebound, drift, half-step, or reveal retired graphics.
- Portrait cards, banners, buttons, text, badges, and icons must move as one card. Decorative frames must not remain stationary while their dynamic content scrolls underneath.

## Typography

- Fixed shell lettering is the exact approved raster artwork.
- Every genuinely dynamic `TextWidget` or `EditableTextWidget` must explicitly set `Brush.Font="ReignSerifDynamic"`. The permitted behavioral brushes are `DefaultText` and `Info.Text`, but the brush alone is not sufficient without the explicit font face.
- Match the approved capitalization, small-cap treatment, deliberate spacing, size hierarchy, wrapping width, baseline, alignment, and truncation behavior. The accepted font-size, baseline, and tracking difference is at most one pixel.
- Use `goldHighlight` for titles and section headings, `goldAntique` for metadata and restrained secondary gold, `goldBright` only for small focal controls/highlights, `textPrimary` for main prose, `textSecondary` for supporting prose, and `textMuted` for tertiary copy.
- Synthetic outline, glow, blur, and shadow are forbidden unless a future approved reference explicitly contains them. The current approved standalone references use none.
- Long dynamic prose must have a clean reading field. Decorative lines, diamonds, crests, or separators must not sit behind text or intersect wrapped lines.

## Portraits, banners, and scene apertures

Portrait sources remain byte-for-byte untouched rectangular images. Never pre-mask, alpha-cut, tint, pad, border, recolor, or bake a frame into a character portrait.

Required layer order:

1. bounded content viewport;
2. unchanged dynamic portrait/banner/scene texture, centered and cover-fitted without aspect-ratio distortion;
3. opaque black-marble aperture plate that conceals the source rectangle outside the opening;
4. approved antique-gold frame and ornament integrated into that same owning aperture artwork, never a separately positioned overlay. Steps 3 and 4 describe regions of one complete asset, not independent frame widgets.

Every card containing a framed portrait, banner, scene, icon, or other graphic
must use one complete card raster asset: its opaque plate, border, graphic frames,
and genuinely transparent aperture centers must all be built into that same card
asset. The dynamic graphic is a sibling beneath the card artwork, above the
interface background, and shows only through its card-owned transparent center.
The graphic's clipping or masking applies to the graphic alone: it must never cut,
cover, erase, or clip any frame linework or ornament. The full frame design stays
intact above the graphic. Building a row from a generic rectangle plus a separate
ring, mask, or frame is forbidden. A fixed screen shell must not retain obsolete
graphic holes behind a card roster; remove those holes from the shell asset itself.
When the card scrolls, its artwork, frames, graphics, text, and controls move together.

Clarified 2026-09-08: above-interface scrolling applies to all card types. Integrated
framing applies everywhere, including cards, panels, controls, and fixed interfaces;
all framed graphics use transparent centers and intact integrated decoration.
Separate frame overlays are prohibited because of their clipping and alignment failures.

Shape roles:

- Compact headshot: circle.
- Large bust or shoulders: vertical oval.
- Full-body character: rectangle.
- Memory artwork: approved memory-scene rectangle.
- Castle occupancy: circle; an empty slot uses only the approved muted-grey silhouette.
- Royal Council advisors: large vertical ovals around the central transcript.
- Spymaster appointment/operational identity: full-body portrait in a rectangular presentation where the approved reference specifies it.

Portrait fit requirements:

- Preserve source aspect ratio. Stretching wide or tall is forbidden.
- Center the face horizontally unless the approved source composition requires a deliberate offset.
- For head-and-shoulder portraits, keep the full head visible; the eyes should sit slightly above the aperture's vertical center, with natural shoulder or upper-torso context below.
- A portrait must fill its opening without exposing a black bar, square corner, pale/white fringe, checkerboard, or unfilled strip inside the frame.
- The aperture plate must remain above the portrait on every edge. Portrait pixels may never cross over or erase part of the frame.
- Do not stack an extra oval or circle over a card whose approved raster already contains that frame.

The exact registered shared apertures and hashes remain in `modern-style-contract.json`; future work must reuse them or formally register a new approved aperture with equivalent alpha, surround, atlas, and fidelity evidence.

## Controls and interaction states

- Buttons use the same black-marble material and thin antique-gold geometry as their parent screen. A selected tab may use `panelActive`; an ordinary button uses `panelRaised` or the approved baked surface.
- Primary actions gain emphasis through hierarchy, placement, title treatment, or a small `goldBright` accent—not through a large bright-gold border.
- Close, cancel, return, previous, and next controls follow the same ornament vocabulary and are placed consistently with the approved reference.
- Dropdowns and text inputs use `inputBlack`, an exact approved border role, and live typography tokens. Native grey rectangles or legacy brown buttons must not remain behind a modern control.
- Disabled controls use `disabledNeutral` and must remain legible without appearing selected.
- Hit targets may be larger than the visible ornament, but invisible controls must not overlap unrelated choices or intercept neighboring content.

## Approved composition patterns

These examples define different layouts within one shared visual system. Use the pattern whose information architecture matches the feature.

### Spymaster operational screen

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/spymaster.png` (`90ea63e898de5e677002ea94f7ea4d50b6406310c540fbd2b9b10eca9d6fb109`).

- Tall full-body identity card on the left.
- One continuous top tab bar.
- Main operational workspace on the upper right, with target and operation columns divided by restrained ornament.
- Reports/archive region below, using equal framed panels.
- No legacy selector rectangles, overlapping tab plates, or unused backing graphics behind modern controls.

### Correspondence

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/correspondence.png` (`7fa74e25cdf023d352da036afffa58a17d9e21053da8681559ecd54eb81850df`).

- Narrow searchable/scrollable contact rail on the left; wide conversation and composer region on the right.
- Circular contact portraits with complete frames and compact metadata.
- Selected contact is one complete raised card.
- Incoming and outgoing messages use restrained raised bands with clear alignment, not speech bubbles or bright chat colors.

### Memories Book

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/memories-book.png` (`ab16c728c6a01749a7d51db01d82fc734767fc22077fed5ce250b7a84d166858`).

- Memory list on the left; dominant approved scene aperture and caption on the right.
- Navigation is quiet and distributed along the bottom edge.
- The scene is the focal point; borders and text support it rather than compete with it.

### Diplomacy announcement

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/diplomacy-announcement.png` (`6ee8dd4623033e226b50815b8fbd95b269673b908942edc7061728398674dd12`).

- Ceremonial symmetry: actor card left, recipient card right, declaration centered.
- Large vertical oval portraits, banners below, and a restrained central geometric watermark/divider.
- One centered acknowledgement action anchors the bottom.

### Family Chambers

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/family-chambers.png` (`d47619f8af70a534def34a56cf2fadb95c42a5e41b2110e95f34f9ea79b98e1c`).

- Complete individual noble cards scroll in the left rail; children use the matching right rail.
- Center panel is reserved for the family scene/empty state and primary action.
- Circular portraits are undistorted and move with their cards.
- This individual-card scrolling behavior is the preferred pattern for other character-card rails.

### Royal Council

Authority: `ReignBeta/artwork/ui-modern-style-kit/approved-references/royal-council.png` (`1b08360102f624d2051371db436ed356ea0b0297c9eab32f05deaac771f7c1bb`).

- Four large advisor portrait-and-label stations frame the central transcript: two left, two right.
- Central transcript is the dominant reading surface; the composer and compact send control sit below it.
- Advisor portraits use consistent vertical-oval geometry, aspect ratio, eye line, and full-head visibility.
- Decorative transcript separators must never sit beneath or collide with long dynamic briefings.

## Prohibited legacy and failure patterns

The following fail the design standard:

- Any off-palette fixed color, including bright yellow-gold, orange-gold, brown neutral panels, native grey backing rectangles, white portrait seams, or silver halos.
- Old frames, buttons, rectangles, labels, or apertures left beneath a replacement design.
- Baked frame art showing behind a moving card or portrait.
- Any item assembled with separately positioned decorative frames, rings, borders, or corner pieces instead of framing integrated into its owning artwork.
- Portraits stretched, cropped through the head, floating above their aperture, failing to fill it, or covering frame pixels.
- One long content strip scrolling beneath stationary holes.
- Half-card scroll increments, endpoint bounce, misalignment after scrolling, or an unreachable first/last card.
- Fixed ornaments behind long dynamic text.
- Hidden duplicate labels behind baked text.
- Whole-screen stretching, freeform reference warping, or per-resolution geometry improvisation.
- Claiming completion from XML inspection, compilation, or browser preview alone when native evidence is required.

## Required workflow and acceptance

Before editing:

1. Identify the catalog interface and latest approved reference in `asset-manifest.json`.
2. Read `modern-style-contract.json`, `palette.json`, and the relevant prefab/patch.
3. Inventory every existing fixed layer so superseded graphics can be removed rather than covered.
4. Obtain the manifest-selected `reign_get_validation_plan` before source changes.

Before handoff:

1. Run the provider-free preview contract audit.
2. Run applicable layout, scrolling, portrait-layering, typography, palette, and native-augmentation contracts.
3. Render the complete provider-free matrix at its required resolutions and inspect the affected screenshots.
4. Run approved-reference fidelity against the retained references.
5. Run the manifest-selected `reign_validate` profile and deploy only exact validated artifacts when deployment is requested.
6. Run or explicitly defer the required native Bannerlord acceptance check. Browser evidence may prove structure and fixed-reference fidelity, but it does not replace in-game confirmation.

An interface is complete only when there are zero unexplained fixed-region differences, zero diagnostics, exact frozen color tokens, stable interaction/scroll boundaries, correct dynamic masking, and current evidence paths recorded in the handoff.


## NPC conversation actions (2026-09-05)

The user-approved action-text extension permits native `RichTextWidget` with `Reign.Chat.ActionText.15`, `.16`, or `.17` only in individual, party, castle and social-event transcript bodies. Speech retains `ReignSerifDynamic`; action spans alone use the genuine matching `ReignSerifActionItalic` font with the same size and `textPrimary` color. This is the explicit exception to the DefaultText/Info.Text behavioral-brush rule. All fixed-shell, layout, portrait and other typography requirements remain in force. See the `conversationActions` entry in the machine-readable style contract and the font provenance record.
