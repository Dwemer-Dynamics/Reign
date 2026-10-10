import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { getSampleDataForPrefab } from "./js/sample-data.js";

const root = path.dirname(fileURLToPath(import.meta.url));
const prefab = fs.readFileSync(path.resolve(root, "../../GUI/Prefabs/ReignIndividualChatScreen.xml"), "utf8");
const checks = [];
function check(name, run) { run(); checks.push(name); }
const fixture = state => getSampleDataForPrefab("ReignIndividualChatScreen.xml", "outfit-" + state);
check("input accepts the description before showing a comparison", () => {
  const data = fixture("input");
  assert.equal(data.IsOutfitInputVisible, true);
  assert.equal(data.IsOutfitComparisonVisible, false);
  assert.equal(data.IsOutfitReady, false);
  assert.match(prefab, /What kind of clothes did you find\? I want every little detail about them\./);
});
check("changing and failure cannot confirm; cancellation remains available", () => {
  for (const state of ["changing", "failed"]) {
    const data = fixture(state);
    assert.equal(data.IsOutfitComparisonVisible, true);
    assert.equal(data.IsOutfitReady, false);
    assert.equal(data.CanCancelOutfit, true);
    assert.equal(data.OutfitAfterPath, "");
    assert.equal(data.InputEnabled, false);
  }
  assert.equal(fixture("changing").IsOutfitChanging, true);
  assert.ok(fixture("failed").OutfitStatus.length > 0);
});
check("ready has two actual offline full-body portrait sources", () => {
  const data = fixture("ready");
  assert.equal(data.IsOutfitReady, true);
  assert.equal(data.IsOutfitChanging, false);
  for (const source of [data.OutfitBeforePath, data.OutfitAfterPath]) {
    const bytes = fs.readFileSync(path.resolve(root, source));
    assert.equal(bytes.toString("hex", 0, 8), "89504e470d0a1a0a");
    assert.ok(bytes.readUInt32BE(20) > bytes.readUInt32BE(16), "Use a full-body vertical source.");
  }
});
check("ineligible state hides the entry without hiding Look at them", () => {
  assert.equal(fixture("ineligible").CanChangeOutfit, false);
  assert.match(prefab, /Id="ReignChangeClothesButton"[^>]*IsVisible="@CanChangeOutfit"/);
  assert.match(prefab, /Command.Click="ExecuteLookAtThem"/);
});
check("untinted portrait sources are below one integrated alpha shell", () => {
  const before = prefab.indexOf('Id="ReignOutfitBefore"');
  const after = prefab.indexOf('Id="ReignOutfitAfter"');
  const shell = prefab.indexOf('Id="ReignOutfitShell"');
  assert.ok(before >= 0 && before < after && after < shell);
  for (const tag of prefab.match(/<ReignOutfitPortraitWidget\b[^>]+>/g)) {
    assert.doesNotMatch(tag, /(?:Color|Brush|Sprite|Mask)=/);
    assert.match(tag, /ImagePath="@Outfit(?:Before|After)Path"/);
  }
  assert.match(prefab, /Id="ReignOutfitConfirm"[^>]*IsVisible="@IsOutfitReady"/);
  assert.match(prefab, /Id="ReignOutfitCancel"[^>]*Brush="ConversationItem.SoundBrush"/);
});
console.log(JSON.stringify({ schema: "reign-outfit-ui-contract-v1", passed: true, checks }, null, 2));
