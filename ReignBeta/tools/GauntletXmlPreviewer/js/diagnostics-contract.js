export const REGISTERED_APERTURE_PLATE_SPRITES = Object.freeze([
  "reign_modern_portrait_circle_overlay",
  "reign_modern_portrait_oval_overlay",
  "reign_modern_portrait_oval_thin_frame",
  "reign_modern_portrait_rectangle_overlay",
  "reign_diplomacy_actor_portrait_mask",
  "reign_diplomacy_target_portrait_mask",
  "reign_family_chambers_portrait_mask",
  "reign_correspondence_contact_portrait_mask",
  "reign_individual_chat_player_portrait_mask",
  "reign_individual_chat_npc_portrait_mask",
  "reign_individual_chat_zoom_oval_mask",
  "reign_party_chat_portrait_mask_active",
  "reign_party_chat_portrait_mask_inactive",
  "reign_party_chat_preview_oval_mask",
  "reign_social_event_active_portrait_mask",
  "reign_social_event_available_portrait_mask",
  "reign_social_event_preview_oval_mask",
  "reign_war_council_lord_portrait_mask",
  "reign_war_council_lord_portrait_mask_active",
  "reign_war_council_assigned_portrait_mask",
  "reign_war_council_candidate_portrait_mask",
  "reign_war_council_lord_card_overlay",
  "reign_war_council_assigned_card_overlay",
  "reign_royal_council_war_portrait_mask",
  "reign_royal_council_spymaster_portrait_mask",
  "reign_royal_council_economic_portrait_mask",
  "reign_royal_council_foreign_portrait_mask"
]);

const registeredAperturePlateSprites = new Set(REGISTERED_APERTURE_PLATE_SPRITES);
const portraitSourceTags = new Set(["ImageIdentifierWidget", "ReignPortraitWidget"]);
const visualAttributeNames = ["Sprite", "Color", "Brush", "Mask", "MaskSprite", "AlphaMask", "AlphaMaskSprite"];

// This exception is deliberately stricter than a generic portrait crop. It
// describes the production aperture-stack contract: an untouched, centered
// square source modestly overfills a visually empty clip, then a registered
// opaque aperture plate is the final/topmost sibling. Any incomplete or
// malformed stack remains an overflow/clipping diagnostic.
export function isValidApertureCoveredPortraitOverscan(contract) {
  const source = contract?.source || {};
  const clip = contract?.clip || {};
  const plates = Array.isArray(contract?.plates) ? contract.plates : [];
  const laterSiblings = Array.isArray(contract?.laterSiblings) ? contract.laterSiblings : [];
  const sourceRect = source.rect || {};
  const clipRect = clip.rect || {};
  const sourceAttributes = source.attributes || {};
  const clipAttributes = clip.attributes || {};

  if (!portraitSourceTags.has(source.tag)) return false;
  if (contract?.sourceIsDirectClipChild !== true) return false;
  if (clip.isClip !== true || clipAttributes.ClipContents !== "true") return false;
  if (sourceAttributes.WidthSizePolicy !== "Fixed" || sourceAttributes.HeightSizePolicy !== "Fixed") return false;
  if (clipAttributes.WidthSizePolicy !== "Fixed" || clipAttributes.HeightSizePolicy !== "Fixed") return false;
  if (sourceAttributes.HorizontalAlignment !== "Center" || sourceAttributes.VerticalAlignment !== "Center") return false;
  if (hasVisualAttributes(sourceAttributes) || hasVisualAttributes(clipAttributes)) return false;

  const sourceWidth = finiteNumber(sourceAttributes.SuggestedWidth);
  const sourceHeight = finiteNumber(sourceAttributes.SuggestedHeight);
  const clipWidth = finiteNumber(clipAttributes.SuggestedWidth);
  const clipHeight = finiteNumber(clipAttributes.SuggestedHeight);
  if (![sourceWidth, sourceHeight, clipWidth, clipHeight].every(Number.isFinite)) return false;
  if (Math.abs(sourceWidth - sourceHeight) > 0.01) return false;

  const widthCover = sourceWidth - clipWidth;
  const heightCover = sourceHeight - clipHeight;
  if (widthCover < 4 || heightCover < 4) return false;
  if (widthCover > Math.min(16, clipWidth * 0.2) || heightCover > Math.min(16, clipHeight * 0.2)) return false;

  if (!isFiniteRect(sourceRect) || !isFiniteRect(clipRect)) return false;
  const bleeds = {
    left: clipRect.left - sourceRect.left,
    top: clipRect.top - sourceRect.top,
    right: sourceRect.right - clipRect.right,
    bottom: sourceRect.bottom - clipRect.bottom
  };
  if (Object.values(bleeds).some((value) => value <= 0.5)) return false;
  if (Math.abs(bleeds.left - bleeds.right) > 1 || Math.abs(bleeds.top - bleeds.bottom) > 1) return false;

  if (!plates.length || laterSiblings.length !== plates.length) return false;
  if (!laterSiblings.every((entry) => entry?.isRegisteredAperturePlate === true)) return false;
  return plates.every((plate) => plate?.tag === "ImageWidget"
    && registeredAperturePlateSprites.has(plate.sprite)
    && plate.attributes?.WidthSizePolicy === "StretchToParent"
    && plate.attributes?.HeightSizePolicy === "StretchToParent"
    && plate.attributes?.DoNotAcceptEvents === "true");
}

export function isRegisteredAperturePlateSprite(sprite) {
  return registeredAperturePlateSprites.has(sprite);
}

// Event scenes are authored at 16:9 and some interfaces deliberately expose
// them through a shallower panoramic aperture. This exception accepts only a
// centered, width-matched vertical crop inside an explicitly named scene
// aperture; arbitrary oversized images remain diagnostics.
export function isValidIntentionalEventArtCrop(contract) {
  const source = contract?.source || {};
  const clip = contract?.clip || {};
  const sourceAttributes = source.attributes || {};
  const clipAttributes = clip.attributes || {};
  const sourceRect = source.rect || {};
  const clipRect = clip.rect || {};

  if (source.tag !== "ReignEventArtWidget") return false;
  if (contract?.sourceIsDirectClipChild !== true) return false;
  if (!/SceneAperture$/.test(String(clip.id || ""))) return false;
  if (clip.isClip !== true || clipAttributes.ClipContents !== "true") return false;
  if (sourceAttributes.WidthSizePolicy !== "Fixed" || sourceAttributes.HeightSizePolicy !== "Fixed") return false;
  if (clipAttributes.WidthSizePolicy !== "Fixed" || clipAttributes.HeightSizePolicy !== "Fixed") return false;
  if (sourceAttributes.HorizontalAlignment !== "Center" || sourceAttributes.VerticalAlignment !== "Center") return false;

  const sourceWidth = finiteNumber(sourceAttributes.SuggestedWidth);
  const sourceHeight = finiteNumber(sourceAttributes.SuggestedHeight);
  const clipWidth = finiteNumber(clipAttributes.SuggestedWidth);
  const clipHeight = finiteNumber(clipAttributes.SuggestedHeight);
  if (![sourceWidth, sourceHeight, clipWidth, clipHeight].every(Number.isFinite)) return false;
  if (Math.abs(sourceWidth - clipWidth) > 0.01 || sourceHeight <= clipHeight) return false;
  if (Math.abs((sourceWidth / sourceHeight) - (16 / 9)) > 0.01) return false;
  if (sourceHeight > clipHeight * 2) return false;

  if (!isFiniteRect(sourceRect) || !isFiniteRect(clipRect)) return false;
  const horizontalBleed = Math.max(
    Math.abs(sourceRect.left - clipRect.left),
    Math.abs(sourceRect.right - clipRect.right));
  const topBleed = clipRect.top - sourceRect.top;
  const bottomBleed = sourceRect.bottom - clipRect.bottom;
  return horizontalBleed <= 1
    && topBleed > 1
    && bottomBleed > 1
    && Math.abs(topBleed - bottomBleed) <= 1.5;
}

function hasVisualAttributes(attributes) {
  return visualAttributeNames.some((name) => String(attributes?.[name] || "").trim().length > 0);
}

function finiteNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : Number.NaN;
}

function isFiniteRect(rect) {
  return [rect.left, rect.top, rect.right, rect.bottom].every((value) => Number.isFinite(Number(value)));
}
