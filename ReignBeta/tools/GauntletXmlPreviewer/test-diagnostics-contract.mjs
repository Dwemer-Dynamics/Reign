import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  REGISTERED_APERTURE_PLATE_SPRITES,
  isValidApertureCoveredPortraitOverscan,
  isValidIntentionalEventArtCrop
} from "./js/diagnostics-contract.js";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const workspaceRoot = path.resolve(toolRoot, "../../..");
const outputPath = argumentValue("--output")
  ? path.resolve(argumentValue("--output"))
  : path.join(workspaceRoot, ".codex-build", "ui-preview", "diagnostics-contract-self-test.json");
const specPath = path.join(workspaceRoot, "ReignBeta", "artwork", "ui-modern-style-kit", "specs", "portrait-frame-only-overlays.json");

const valid = validFixture();
const cases = [
  testCase("valid-image-identifier-stack", valid, true),
  testCase("valid-generated-portrait-stack", mutate(valid, (value) => { value.source.tag = "ReignPortraitWidget"; }), true),
  testCase("ordinary-image-overflow-remains-visible", mutate(valid, (value) => { value.source.tag = "ImageWidget"; }), false),
  testCase("missing-aperture-plate-remains-visible", mutate(valid, (value) => { value.plates = []; value.laterSiblings = []; }), false),
  testCase("unregistered-overlay-remains-visible", mutate(valid, (value) => {
    value.plates[0].sprite = "reign_unregistered_portrait_overlay";
    value.laterSiblings[0].isRegisteredAperturePlate = false;
  }), false),
  testCase("plate-must-be-topmost", mutate(valid, (value) => { value.laterSiblings.push({ isRegisteredAperturePlate: false }); }), false),
  testCase("plate-must-cover-container", mutate(valid, (value) => { value.plates[0].attributes.WidthSizePolicy = "Fixed"; }), false),
  testCase("source-must-be-direct-child", mutate(valid, (value) => { value.sourceIsDirectClipChild = false; }), false),
  testCase("clip-must-be-visual-free", mutate(valid, (value) => { value.clip.attributes.Color = "#FFFFFFFF"; }), false),
  testCase("source-must-not-be-masked", mutate(valid, (value) => { value.source.attributes.MaskSprite = "legacy_circle"; }), false),
  testCase("source-must-be-square", mutate(valid, (value) => { value.source.attributes.SuggestedHeight = "112"; }), false),
  testCase("source-must-be-centered", mutate(valid, (value) => { value.source.attributes.HorizontalAlignment = "Left"; }), false),
  testCase("source-must-cover-both-axes", mutate(valid, (value) => { value.source.attributes.SuggestedHeight = "96"; }), false),
  testCase("cover-must-prevent-edge-seams", mutate(valid, (value) => { value.source.attributes.SuggestedWidth = "99"; value.source.attributes.SuggestedHeight = "99"; }), false),
  testCase("excessive-bleed-remains-visible", mutate(valid, (value) => { value.source.attributes.SuggestedWidth = "140"; value.source.attributes.SuggestedHeight = "140"; }), false),
  testCase("off-center-source-remains-visible", mutate(valid, (value) => { value.source.rect.left -= 3; value.source.rect.right -= 3; }), false)
];

const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
const specSprites = spec.assets.map((entry) => path.basename(entry.path, path.extname(entry.path))).sort();
const contractSprites = [...REGISTERED_APERTURE_PLATE_SPRITES].sort();
const permittedCompositeApertureSprites = [
  "reign_war_council_assigned_card_overlay",
  "reign_war_council_lord_card_overlay"
];
const expectedContractSprites = [...specSprites, ...permittedCompositeApertureSprites].sort();
const registryMatchesSpec = JSON.stringify(expectedContractSprites) === JSON.stringify(contractSprites);
const registryCase = {
  id: "registered-aperture-sprites-match-production-spec",
  expected: true,
  actual: registryMatchesSpec,
  passed: registryMatchesSpec,
  specSprites,
  permittedCompositeApertureSprites,
  contractSprites
};
cases.push(registryCase);
const eventArt = validEventArtCropFixture();
cases.push(
  eventArtCase("valid-centered-16x9-event-art-crop", eventArt, true),
  eventArtCase("event-art-crop-requires-scene-aperture", mutate(eventArt, (value) => { value.clip.id = "OrdinaryClip"; }), false),
  eventArtCase("event-art-crop-requires-16x9-source", mutate(eventArt, (value) => { value.source.attributes.SuggestedHeight = "640"; }), false),
  eventArtCase("event-art-crop-requires-width-match", mutate(eventArt, (value) => { value.source.attributes.SuggestedWidth = "1280"; }), false),
  eventArtCase("event-art-crop-requires-centered-vertical-bleed", mutate(eventArt, (value) => { value.source.rect.top -= 10; value.source.rect.bottom -= 10; }), false),
  eventArtCase("ordinary-image-is-not-event-art-crop", mutate(eventArt, (value) => { value.source.tag = "ImageWidget"; }), false)
);

const report = {
  schema: "reign-ui-diagnostics-contract-self-test-v1",
  generatedUtc: new Date().toISOString(),
  specPath,
  caseCount: cases.length,
  passedCount: cases.filter((entry) => entry.passed).length,
  failedCount: cases.filter((entry) => !entry.passed).length,
  passed: cases.every((entry) => entry.passed),
  cases
};
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
console.log(JSON.stringify({ passed: report.passed, caseCount: report.caseCount, passedCount: report.passedCount, failedCount: report.failedCount, outputPath }, null, 2));
if (!report.passed) process.exitCode = 1;

function validFixture() {
  return {
    sourceIsDirectClipChild: true,
    source: {
      tag: "ImageIdentifierWidget",
      attributes: {
        WidthSizePolicy: "Fixed",
        HeightSizePolicy: "Fixed",
        SuggestedWidth: "102",
        SuggestedHeight: "102",
        HorizontalAlignment: "Center",
        VerticalAlignment: "Center"
      },
      rect: { left: 97, top: 97, right: 199, bottom: 199 }
    },
    clip: {
      isClip: true,
      attributes: {
        WidthSizePolicy: "Fixed",
        HeightSizePolicy: "Fixed",
        SuggestedWidth: "96",
        SuggestedHeight: "96",
        ClipContents: "true"
      },
      rect: { left: 100, top: 100, right: 196, bottom: 196 }
    },
    plates: [{
      tag: "ImageWidget",
      sprite: "reign_modern_portrait_circle_overlay",
      attributes: {
        WidthSizePolicy: "StretchToParent",
        HeightSizePolicy: "StretchToParent",
        DoNotAcceptEvents: "true"
      }
    }],
    laterSiblings: [{ isRegisteredAperturePlate: true }]
  };
}

function validEventArtCropFixture() {
  return {
    sourceIsDirectClipChild: true,
    source: {
      tag: "ReignEventArtWidget",
      attributes: {
        WidthSizePolicy: "Fixed",
        HeightSizePolicy: "Fixed",
        SuggestedWidth: "1264",
        SuggestedHeight: "711",
        HorizontalAlignment: "Center",
        VerticalAlignment: "Center"
      },
      rect: { left: 100, top: 25.5, right: 1364, bottom: 736.5 }
    },
    clip: {
      id: "CourtPetitionSceneAperture",
      isClip: true,
      attributes: {
        WidthSizePolicy: "Fixed",
        HeightSizePolicy: "Fixed",
        SuggestedWidth: "1264",
        SuggestedHeight: "464",
        ClipContents: "true"
      },
      rect: { left: 100, top: 149, right: 1364, bottom: 613 }
    }
  };
}

function testCase(id, fixture, expected) {
  const actual = isValidApertureCoveredPortraitOverscan(fixture);
  return { id, expected, actual, passed: actual === expected };
}

function eventArtCase(id, fixture, expected) {
  const actual = isValidIntentionalEventArtCrop(fixture);
  return { id, expected, actual, passed: actual === expected };
}

function mutate(value, callback) {
  const copy = structuredClone(value);
  callback(copy);
  return copy;
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : "";
}
