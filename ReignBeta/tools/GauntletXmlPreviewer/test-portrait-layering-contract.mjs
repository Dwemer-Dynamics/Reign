import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { classifyPortraitPresentation, resolvePortraitComposition } from "./js/renderer.js";
import { ASSETS, getNativeAugmentationSampleData, getSampleDataForPrefab } from "./js/sample-data.js";

const moduleRoot = path.dirname(fileURLToPath(import.meta.url));

function fakeClassList(...initial) {
  const values = new Set(initial);
  return {
    add: (...classes) => classes.forEach((className) => values.add(className)),
    contains: (className) => values.has(className)
  };
}

function fakeSource() {
  return { classList: fakeClassList("portrait", "has-asset") };
}

function fakeLayer({ sources = [], sprite = "", classes = [] } = {}) {
  return {
    classList: fakeClassList(...classes),
    dataset: sprite ? { sprite } : {},
    querySelector: (selector) => selector === ".portrait" ? sources[0] || null : null,
    querySelectorAll: (selector) => selector === ".portrait" ? sources : []
  };
}

const currentNative = fakeSource();
const currentAi = fakeSource();
const currentClip = fakeLayer({ sources: [currentNative, currentAi] });
const currentPlate = fakeLayer({
  sprite: "reign_social_event_active_portrait_mask",
  classes: ["image", "sprite-decoration"]
});
const current = resolvePortraitComposition({ children: [currentClip, currentPlate] });
assert.equal(current.backing, null, "the current two-layer XML has no backing layer");
assert.equal(current.clip, currentClip, "the portrait-bearing child must remain the clip");
assert.equal(current.aperture, currentPlate, "the topmost mask must remain the aperture plate");
assert.deepEqual(current.sources, [currentNative, currentAi], "both native and generated portrait sources share the crop contract");

const legacyBacking = fakeLayer();
const legacySource = fakeSource();
const legacyClip = fakeLayer({ sources: [legacySource] });
const legacyPlate = fakeLayer({ sprite: "legacy_portrait_mask", classes: ["image"] });
const legacy = resolvePortraitComposition({ children: [legacyBacking, legacyClip, legacyPlate] });
assert.equal(legacy.backing, legacyBacking, "a real legacy backing remains behind the portrait clip");
assert.equal(legacy.clip, legacyClip, "legacy three-layer XML still resolves its portrait-bearing clip");
assert.equal(legacy.aperture, legacyPlate, "legacy three-layer XML still resolves its topmost mask");

const directSource = fakeSource();
const directPlate = fakeLayer({ sprite: "reign_war_council_lord_portrait_mask", classes: ["image"] });
const directContainer = { children: [directSource, directPlate] };
const direct = resolvePortraitComposition(directContainer);
assert.equal(direct.clip, directContainer, "a clip with a direct portrait source remains the clipping container");
assert.equal(direct.aperture, directPlate, "a direct source still resolves the following aperture plate");
assert.deepEqual(direct.sources, [directSource], "a direct portrait source is never mistaken for its aperture");

assert.equal(classifyPortraitPresentation({ id: "ActorNativePortrait", width: 244, height: 244 }), "headshot");
assert.equal(classifyPortraitPresentation({ id: "AIPortraitsNpcPortrait", width: 168, height: 168 }), "headshot");
assert.equal(classifyPortraitPresentation({ id: "ReignChatZoomPortrait", width: 560, height: 680 }), "full-body");
assert.equal(classifyPortraitPresentation({ id: "", width: 430, height: 645, useFullBody: true }), "full-body");
assert.equal(classifyPortraitPresentation({ id: "AIPortraitsAIInfluenceMemoryImage", width: 516, height: 296 }), "scene");

const conversationSample = getNativeAugmentationSampleData("SPConversation");
assert.equal(conversationSample.AIInfluenceMemoryAsset, ASSETS.eventArt, "the conversation memory panel previews scene art, not a portrait source");
assert.ok(conversationSample.NpcPortraitCropImageWidth >= 184 && conversationSample.NpcPortraitCropImageHeight >= 240,
  "the native NPC source plane must center-cover the complete rectangular aperture");
assert.ok(conversationSample.PlayerPortraitCropImageWidth >= 184 && conversationSample.PlayerPortraitCropImageHeight >= 240,
  "the native player source plane must center-cover the complete rectangular aperture");
assert.equal(conversationSample.AIInfluenceMemoryImageWidth / conversationSample.AIInfluenceMemoryImageHeight, 4 / 3,
  "the native conversation memory sample must use the runtime landscape scene aspect");

const spymasterSample = getSampleDataForPrefab("ReignSpymasterScreen.xml");
assert.equal(spymasterSample.PortraitAsset, ASSETS.spymasterFullBody,
  "the Spymaster preview must render the approved full-body fixture instead of a cropped chest portrait or silhouette fallback");
assert.equal(spymasterSample.SpymasterModel.IsTableauEnabled, false,
  "the preview fixture must not attempt to render Bannerlord's native character tableau");
assert.equal(spymasterSample.ShowSpymasterAiPortrait, true,
  "the preview fixture must expose the generated full-body Spymaster portrait");
assert.equal(spymasterSample.ShowSpymasterTableauFallback, false,
  "the preview fixture must keep the native tableau fallback hidden while generated art exists");

const spymasterXml = fs.readFileSync(
  path.resolve(moduleRoot, "../../GUI/Prefabs/ReignSpymasterScreen.xml"),
  "utf8"
);
assert.doesNotMatch(spymasterXml, /<ImageIdentifierWidget[^>]*IsVisible="@HasSpymaster"[^>]*>/,
  "Spymaster must not stack a native headshot underneath its full-body presentation");
assert.match(spymasterXml, /<ReignPortraitWidget[^>]*IsVisible="@ShowSpymasterAiPortrait"[^>]*UseFullBody="true"[^>]*>/,
  "the generated Spymaster portrait must be the authoritative full-body presentation");
assert.match(spymasterXml, /<Widget[^>]*IsVisible="@ShowSpymasterTableauFallback"[^>]*>[\s\S]*?<CharacterTableauWidget[^>]*>/,
  "the native Spymaster character tableau must remain inside its exclusive unavailable-art fallback wrapper");
assert.equal((spymasterXml.match(/<CharacterTableauWidget/g) || []).length, 1,
  "Spymaster must define exactly one native tableau fallback layer");
assert.doesNotMatch(spymasterXml, /<Widget IsVisible="@IsSubterfuge"[^>]*WidthSizePolicy="Fixed"[^>]*SuggestedWidth="310"[^>]*Sprite="BlankWhiteSquare_9"[^>]*>/,
  "Subterfuge must not render the old generic grey cover plate above TARGET");
assert.match(spymasterXml, /<Widget IsVisible="@IsSubterfuge"[^>]*Sprite="reign_spymaster_scope_clear_patch"[^>]*>/,
  "Subterfuge must use the seamless shell-texture patch to clear baked selector art above TARGET");
assert.match(spymasterXml, /<Widget IsVisible="@IsReputation"[^>]*Sprite="reign_spymaster_scope_clear_patch"[^>]*>/,
  "Rumor and Reputation must use the seamless shell-texture patch behind its social-scope selector");
for (const id of ["SpymasterLandsScope", "SpymasterPeopleScope", "SpymasterPeopleGroupScope", "SpymasterSocialScope", "SpymasterSocialSelector"]) {
  assert.match(spymasterXml, new RegExp(`<ButtonWidget Id="${id}"[\\s\\S]*?Sprite="reign_spymaster_selector_plate"[\\s\\S]*?</ButtonWidget>`),
    `${id} must use the modern image-backed selector plate`);
  assert.match(spymasterXml, new RegExp(`<ButtonWidget Id="${id}"[\\s\\S]*?Sprite="reign_spymaster_selector_plate" Color="#FFFFFFFF"[\\s\\S]*?</ButtonWidget>`),
    `${id} must render the color-id-baked selector without double tinting`);
}
for (const id of ["SpymasterLandsScope", "SpymasterPeopleScope"]) {
  const button = spymasterXml.match(new RegExp(`<ButtonWidget Id="${id}"[\\s\\S]*?</ButtonWidget>`));
  assert.ok(button, `${id} must remain present`);
  assert.doesNotMatch(button[0], /Sprite="(?:BlankWhiteSquare_9|gold_frame_9)"/,
    `${id} must not retain the old generic selector graphics`);
}
assert.match(spymasterXml, /selector raster is baked from the established Spymaster gold-frame color id #7E6A4DFF/,
  "Spymaster selector palette provenance must remain tied to the established gold-frame color id");

for (const preset of ["spymaster-people", "spymaster-subterfuge", "spymaster-reputation", "spymaster-archive"]) {
  const state = getSampleDataForPrefab("ReignSpymasterScreen.xml", preset);
  assert.equal(state.ShowSpymasterAiPortrait, true,
    `${preset} must retain the same exclusive generated full-body portrait`);
}

const styles = fs.readFileSync(path.join(moduleRoot, "styles.css"), "utf8");
const individualChatXml = fs.readFileSync(
  path.resolve(moduleRoot, "../../GUI/Prefabs/ReignIndividualChatScreen.xml"),
  "utf8"
);
const individualChatShellSpec = JSON.parse(fs.readFileSync(
  path.resolve(moduleRoot, "../../artwork/ui-modern-style-kit/specs/individual-chat-shell.json"),
  "utf8"
));
const individualChatClipRule = styles.match(/\.individual-chat__portrait-clip\s*\{(?<body>[^}]*)\}/s)?.groups?.body || "";
assert.ok(individualChatClipRule, "Individual Chat must keep a scoped preview rule for its portrait source clip");
assert.match(individualChatClipRule, /clip-path:\s*none\s*!important/i,
  "the preview source remains rectangular so the runtime aperture plate alone owns the oval");
assert.doesNotMatch(individualChatClipRule, /clip-path:\s*ellipse/i,
  "the preview must not add a second ellipse that can expose seams around the runtime plate");

const individualChatShellRule = styles.match(/\.individual-chat__shell-art\s*\{(?<body>[^}]*)\}/s)?.groups?.body || "";
assert.ok(individualChatShellRule, "Individual Chat must expose a scoped preview rule for the packed runtime shell");
assert.match(individualChatShellRule, /display:\s*block\s*!important/i,
  "the preview must render the actual packed Individual Chat shell");
assert.match(individualChatShellRule, /pointer-events:\s*none\s*!important/i,
  "the packed shell must not intercept live controls");
assert.doesNotMatch(styles, /individual-chat-reference\.png/i,
  "the preview must not replace current XML and sprite assets with a frozen reference screenshot");
assert.doesNotMatch(styles, /\.individual-chat__portrait-frame\s*\{/i,
  "retired per-portrait mask rules must not return after the shell gained integrated apertures");
assert.match(styles, /\.individual-chat-shell\s*>\s*\.g-node:not\(\.individual-chat__shell-art\)/i,
  "the generic live-child reset must explicitly exempt the packed shell sprite");

assert.equal((individualChatXml.match(/Id="ReignIndividualChatShell"/g) || []).length, 1,
  "Individual Chat must contain exactly one integrated portrait-aperture shell");
assert.match(individualChatXml,
  /Id="ReignIndividualChatShell"[^>]*DoNotAcceptEvents="true"[^>]*Sprite="reign_individual_chat_modern_shell"/,
  "the integrated shell must render the packed runtime sprite without accepting events");
assert.doesNotMatch(individualChatXml,
  /ReignChat(?:Player|Npc)PortraitFrame|reign_individual_chat_(?:player|npc)_portrait_mask/,
  "Individual Chat must not reference the retired local portrait masks");

assert.doesNotMatch(individualChatXml,
  /Id="ReignIndividualChatBackground"/,
  "Individual Chat must not retain the obsolete full-surface background seam beneath its portrait apertures");
const playerButtonIndex = individualChatXml.indexOf('Command.Click="ExecuteTogglePlayerPortraitZoom"');
const npcButtonIndex = individualChatXml.indexOf('Command.Click="ExecuteToggleNpcPortraitZoom"');
const playerSourceIndex = individualChatXml.indexOf('Id="ReignChatPlayerPortrait"');
const npcSourceIndex = individualChatXml.indexOf('Id="ReignChatNpcPortrait"');
const shellIndex = individualChatXml.indexOf('Id="ReignIndividualChatShell"');
const liveControlMarkers = [
  'Text="@PlayerName"',
  'Text="@NpcName"',
  'Command.Click="ExecuteClose"',
  'Command.Click="ExecuteLookAtThem"',
  'Id="ReignChatLogPanelBackground"',
  'Id="ReignChatScrollbar"',
  'Text="@InputText"',
  'Command.Click="ExecuteSend"'
];
const liveControlIndexes = liveControlMarkers.map((marker) => individualChatXml.indexOf(marker));
assert.ok(playerButtonIndex >= 0 && npcButtonIndex >= 0
  && playerSourceIndex >= 0 && npcSourceIndex >= 0 && shellIndex >= 0
  && liveControlIndexes.every((index) => index >= 0),
"Individual Chat must retain its portrait commands and sources, integrated shell, and every live control");
assert.ok(playerButtonIndex < shellIndex && npcButtonIndex < shellIndex,
  "both untouched portrait sources must render beneath the integrated shell apertures");
assert.ok(playerSourceIndex < shellIndex && npcSourceIndex < shellIndex,
  "both centered 330x330 source planes must remain below the integrated shell");
assert.ok(liveControlIndexes.every((index) => shellIndex < index),
  "all names, commands, transcript, input, and send controls must remain above the event-transparent shell");

const rendererSource = fs.readFileSync(path.join(moduleRoot, "js/renderer.js"), "utf8");
assert.doesNotMatch(rendererSource, /ReignChat(?:Player|Npc)PortraitFrame|individual-chat__portrait-frame/,
  "the preview decorator must not search for or style retired local portrait masks");

assert.equal(individualChatShellSpec.canvas?.width, 1672, "the shell spec must retain the native logical width");
assert.equal(individualChatShellSpec.canvas?.height, 941, "the shell spec must retain the native logical height");
assert.equal(individualChatShellSpec.transparentRgb, "#121211",
  "antialiased aperture edges must use dark hidden RGB rather than white fringe pixels");
assert.deepEqual(
  individualChatShellSpec.apertures?.map(({ id, shape, x, y, width, height }) => ({ id, shape, x, y, width, height })),
  [
    { id: "player-portrait", shape: "oval", x: 104, y: 57, width: 210, height: 310 },
    { id: "npc-portrait", shape: "oval", x: 1343, y: 57, width: 210, height: 310 }
  ],
  "the one shell must own exactly the two registered oval apertures aligned to the native clips"
);

const frameRule = styles.match(/\.attendee-card \.attendee-card__portrait-frame\s*\{(?<body>[^}]*)\}/s)?.groups?.body || "";
assert.ok(frameRule, "the attendee aperture plate must have an explicit preview rule");
assert.doesNotMatch(frameRule, /attendee-portrait-frame\.svg/i, "the preview must not replace the runtime aperture plate with a square SVG frame");
assert.match(frameRule, /z-index:\s*3\s*!important/i, "the runtime aperture plate must remain topmost");

const attendeePortraitRule = styles.match(/\.attendee-card__portrait\s*\{(?<body>[^}]*)\}/s)?.groups?.body || "";
assert.match(attendeePortraitRule, /width:\s*112px\s*!important/i, "the preview must preserve the square 112px XML portrait button");
assert.match(attendeePortraitRule, /height:\s*112px\s*!important/i, "the preview must preserve the square 112px XML portrait button");
const attendeeSourceRule = styles.match(/\.attendee-card__portrait-image\s*\{(?<body>[^}]*)\}/s)?.groups?.body || "";
assert.match(attendeeSourceRule, /width:\s*104px\s*!important/i, "the untouched source plane must retain its 104px overscan");
assert.match(attendeeSourceRule, /height:\s*104px\s*!important/i, "the untouched source plane must retain its 104px overscan");

console.log("portrait layering contract: PASS");
