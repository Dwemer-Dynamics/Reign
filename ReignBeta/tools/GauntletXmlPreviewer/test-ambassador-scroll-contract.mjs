import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const previewRoot = path.dirname(fileURLToPath(import.meta.url));
const ambassadorXml = fs.readFileSync(
  path.resolve(previewRoot, "../../GUI/Prefabs/ReignAmbassadorScreen.xml"), "utf8");
const widgetSource = fs.readFileSync(
  path.resolve(previewRoot, "../../src/Modules/Court/UI/Widgets/ReignAmbassadorSnapScrollPanel.cs"), "utf8");
const viewModelSource = fs.readFileSync(
  path.resolve(previewRoot, "../../src/Modules/Court/UI/ViewModels/ReignAmbassadorScreenVM.cs"), "utf8");
const sampleDataSource = fs.readFileSync(path.join(previewRoot, "js/sample-data.js"), "utf8");

assert.match(ambassadorXml,
  /<ReignAmbassadorSnapScrollPanel[^>]*Type="ReignBeta\.UI\.ReignAmbassadorSnapScrollPanel"[^>]*ItemCount="@AmbassadorCount"[^>]*>/,
  "the ambassador rail must use its one-card snap panel and bind the live card count");
assert.match(ambassadorXml,
  /<ReignAmbassadorSnapScrollPanel[^>]*SuggestedWidth="981"[^>]*>[\s\S]*?<Widget[^>]*SuggestedWidth="295"[^>]*MarginRight="32"[^>]*ClipContents="true"/,
  "the snap rail must preserve the approved three-card viewport and card geometry");
assert.equal(981, 3 * (295 + 32),
  "the viewport must equal exactly three complete card pitches, including each slot gap");
assert.match(ambassadorXml,
  /Id="AmbassadorCardsCleanBackdrop"[^>]*SuggestedWidth="1340"[^>]*Sprite="BlankWhiteSquare_9"[^>]*Color="#10100FFF"/,
  "an opaque stage must cover every obsolete fixed card window baked into the shell");
assert.doesNotMatch(ambassadorXml, /Id="AmbassadorFixedRegionOverlay"/,
  "the obsolete fixed-window overlay must not remain above the moving cards");
assert.match(ambassadorXml,
  /SuggestedWidth="295"[^>]*SuggestedHeight="620"[^>]*MarginRight="32"[^>]*ClipContents="true">[\s\S]*?<Widget[^>]*WidthSizePolicy="StretchToParent"[^>]*HeightSizePolicy="StretchToParent"[^>]*Sprite="BlankWhiteSquare_9"[^>]*Color="#10100FFF"[\s\S]*?Sprite="reign_ambassador_modern_envoy_card_overlay"/,
  "every scrolling envoy must own an opaque full-card base and its frame overlay");
assert.match(viewModelSource, /\[DataSourceProperty\]\s+public int AmbassadorCount => Ambassadors\.Count;/,
  "the view model must expose the live ambassador count to the snap panel");
assert.match(viewModelSource, /OnPropertyChanged\(nameof\(AmbassadorCount\)\)/,
  "ambassador refreshes must notify the snap panel when the count changes");
assert.match(sampleDataSource, /"ReignAmbassadorScreen\.xml":\s*\{\s*AmbassadorCount: SAMPLE_DATA\.Ambassadors\.length,/,
  "the preview harness must supply the same live-count binding used in game");
assert.match(widgetSource, /private const int VisibleCards = 3;/,
  "the rail must define its three complete visible slots");
assert.match(widgetSource, /maximum \/ ScrollableCardCount/,
  "card positions must divide the complete scrollbar range evenly");
assert.match(widgetSource, /int nextIndex = ClampCardIndex\(_currentCardIndex \+ direction\)/,
  "each wheel action must advance a discrete card index with hard endpoint clamps");
assert.match(widgetSource, /if \(nextIndex == _currentCardIndex\) return;/,
  "wheel input at the first or last card must be a stable no-op");
assert.doesNotMatch(widgetSource,
  /float delta = Input\.DeltaMouseScroll;\s*base\.OnMouseScroll\(\);/,
  "handled ambassador wheel input must not also start native inertial scrolling");
assert.match(widgetSource,
  /ResetTweenSpeed\(\);\s*if \(nextIndex == _currentCardIndex\) return;/,
  "endpoint no-ops must clear any residual native scroll velocity");
assert.match(widgetSource, /SetHorizontalScrollTarget\(target, 0f\)/,
  "resolved card positions must be applied to the horizontal panel itself");

function advanceCard(itemCount, currentIndex, direction) {
  const lastIndex = Math.max(0, itemCount - 3);
  return Math.max(0, Math.min(lastIndex, currentIndex + direction));
}

assert.equal(advanceCard(3, 0, 1), 0,
  "a fully visible rail must not scroll");
assert.equal(advanceCard(5, 0, 1), 1,
  "one forward wheel action must advance exactly one of two hidden cards");
assert.equal(advanceCard(5, 1, 1), 2,
  "the second action must land exactly on the far-right boundary");
assert.equal(advanceCard(5, 2, 1), 2,
  "repeated input at the far-right boundary must remain on the last card");
assert.equal(advanceCard(5, 2, -1), 1,
  "one reverse action must return exactly one card");
assert.equal(advanceCard(5, 0, -1), 0,
  "repeated input at the far-left boundary must remain on the first card");

console.log("ambassador scroll contract: PASS");
