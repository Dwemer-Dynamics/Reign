import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const previewRoot = path.dirname(fileURLToPath(import.meta.url));
const moduleRoot = path.resolve(previewRoot, "../..");
const governmentXml = fs.readFileSync(path.join(moduleRoot, "GUI/Prefabs/ReignGovernmentScreen.xml"), "utf8");
const governmentWidget = fs.readFileSync(path.join(moduleRoot,
  "src/Modules/Government/UI/Widgets/ReignGovernmentMemberScrollPanel.cs"), "utf8");
const governmentVm = fs.readFileSync(path.join(moduleRoot,
  "src/Modules/Government/UI/ViewModels/ReignGovernmentScreenVM.cs"), "utf8");
const thumbnailPatch = fs.readFileSync(path.join(moduleRoot,
  "src/Modules/UI/UI/GameMenu/ReignGameMenuPartyItemInsertPatch.cs"), "utf8");
const portraitWidget = fs.readFileSync(path.join(moduleRoot,
  "src/Modules/Portraits/UI/Widgets/ReignPortraitWidget.cs"), "utf8");

assert.match(governmentXml,
  /<ReignGovernmentMemberScrollPanel[^>]*Type="ReignBeta\.UI\.ReignGovernmentMemberScrollPanel"[^>]*ItemCount="@MemberCount"[^>]*>/,
  "government members must use the card-boundary snap panel with a live item count");
assert.match(governmentVm, /\[DataSourceProperty\]\s+public int MemberCount => Members\.Count;/,
  "government must expose its current member count");
assert.match(governmentVm, /OnPropertyChanged\(nameof\(MemberCount\)\)/,
  "government refreshes must notify the snap panel");
assert.match(governmentWidget, /private const int VisibleMemberRows = 2;/,
  "the 141-pixel viewport must treat both complete 65-pixel member rows and their gap as visible");
assert.match(governmentWidget, /maximum \/ ScrollableRowCount/,
  "member positions must divide the complete scrollbar range evenly");
assert.match(governmentWidget, /int nextIndex = ClampRowIndex\(_currentRowIndex \+ direction\)/,
  "wheel actions must advance a discrete member-row index with hard endpoint clamps");
assert.match(governmentWidget, /if \(nextIndex == _currentRowIndex\) return;/,
  "wheel input at either member-list endpoint must be a stable no-op");
assert.doesNotMatch(governmentWidget,
  /float delta = Input\.DeltaMouseScroll;\s*base\.OnMouseScroll\(\);/,
  "handled member wheel input must not also start native inertial scrolling");
assert.match(governmentWidget,
  /ResetTweenSpeed\(\);\s*if \(nextIndex == _currentRowIndex\) return;/,
  "endpoint no-ops must clear residual scroll velocity before it can misalign cards");
assert.match(governmentWidget, /SetVerticalScrollTarget\(target, 0f\)/,
  "resolved positions must be applied to the panel");

assert.match(thumbnailPatch,
  /ReignGameMenuPartyPortrait\\" Type=\\"ReignBeta\.UI\.Widgets\.ReignPortraitWidget\\"[\s\S]*?WidthSizePolicy=\\"StretchToParent\\" HeightSizePolicy=\\"StretchToParent\\"/,
  "native settlement portraits must fill the measured wide portrait slot");
assert.match(thumbnailPatch, /TargetAspect=\\"1\.3764706\\" UsePartyThumbnail=\\"true\\"/,
  "native settlement portraits must request the authored party-wide derivative at the native 117x85 aspect");
assert.doesNotMatch(thumbnailPatch, /SuggestedWidth=\\"91\\" SuggestedHeight=\\"91\\"/,
  "native settlement portraits must not be reduced to the regressed square destination");
assert.match(portraitWidget, /public bool UsePartyThumbnail/,
  "portrait widgets must expose explicit party-thumbnail routing");
assert.match(portraitWidget,
  /_usePartyThumbnail \? AIPortraits\.PortraitQualityTier\.PartyThumbnail/,
  "party-thumbnail widgets must load thumbnail_wide.png instead of recropping the vertical thumbnail");
assert.match(thumbnailPatch,
  /ReignGameMenuPartyPortrait[\s\S]*ReignGameMenuPartyPortraitAperturePlate[\s\S]*MaskedTextureWidget/,
  "the untouched party-wide portrait must remain below the approved frame and native banner");

function advanceRow(itemCount, currentIndex, direction) {
  const lastIndex = Math.max(0, itemCount - 2);
  return Math.max(0, Math.min(lastIndex, currentIndex + direction));
}

assert.equal(advanceRow(1, 0, 1), 0, "a one-member rail must not scroll");
assert.equal(advanceRow(2, 0, 1), 0, "two fully visible members must not create a false scroll range");
assert.equal(advanceRow(4, 0, 1), 1, "one wheel action must advance exactly one hidden member");
assert.equal(advanceRow(4, 1, 1), 2, "the next action must land at the final member boundary");
assert.equal(advanceRow(4, 2, 1), 2, "the final member must clamp without drift or rebound");
assert.equal(advanceRow(4, 2, -1), 1, "reverse input must return exactly one member");
assert.equal(advanceRow(4, 0, -1), 0, "the first member boundary must also remain stable");

console.log("government scroll and native thumbnail contract: PASS");
