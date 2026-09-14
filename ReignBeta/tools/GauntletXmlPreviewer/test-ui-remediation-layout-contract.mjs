import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const previewRoot = path.dirname(fileURLToPath(import.meta.url));
const readPrefab = (name) => fs.readFileSync(
  path.resolve(previewRoot, `../../GUI/Prefabs/${name}`), "utf8");

const spymasterXml = readPrefab("ReignSpymasterScreen.xml");
const economicXml = readPrefab("ReignCourtEconomicReportScreen.xml");
const royalCouncilXml = readPrefab("ReignRoyalCouncilScreen.xml");

assert.match(spymasterXml,
  /IsVisible="@IsIntelligence"[^>]*SuggestedWidth="340"[^>]*SuggestedHeight="50"[^>]*Sprite="reign_spymaster_scope_clear_patch"/,
  "Gather Intelligence must clear the obsolete baked scope rectangles with the texture-matched patch");
const intelligenceScope = spymasterXml.match(
  /<Widget IsVisible="@IsIntelligence" WidthSizePolicy="Fixed"[\s\S]*?<\/Widget>/)?.[0] ?? "";
assert.doesNotMatch(intelligenceScope, /Sprite="BlankWhiteSquare_9" Color="#121211FF"/,
  "Gather Intelligence must not retain the old rectangular backing behind Lands and People");

const settlementRows = [
  ["Prosperity", "130"], ["Food", "151"], ["Security", "172"], ["Loyalty", "193"],
  ["Militia", "275"], ["Garrison", "298"], ["Garrison Food", "321"], ["Garrison Wage", "344"],
  ["Grain", "438"], ["Fish", "459"], ["Meat", "480"], ["Olives", "501"],
  ["Beer", "522"], ["Butter", "543"], ["Grapes", "564"], ["Dates", "585"],
];
for (const [label, top] of settlementRows) {
  assert.match(economicXml, new RegExp(
    `SuggestedWidth="100"[^>]*MarginLeft="28"[^>]*MarginTop="${top}"[^>]*Brush.TextHorizontalAlignment="Right"[^>]*Text="${label}"`),
    `${label} must meet the value column at the visual center of every repeated card`);
}
assert.equal((economicXml.match(/SuggestedWidth="60" SuggestedHeight="18" MarginLeft="136"[^>]*Brush.TextHorizontalAlignment="Left"/g) ?? []).length, 16,
  "every settlement number must meet its label at the visual center of every repeated card");
assert.doesNotMatch(economicXml, /Text="Grain"[^>]*MarginTop="420"/,
  "the first resource row must no longer sit on the decorative divider");

assert.equal((royalCouncilXml.match(/ReignPortraitWidget[^>]*SuggestedWidth="304"[^>]*SuggestedHeight="304"[^>]*HorizontalAlignment="Center"[^>]*VerticalAlignment="Center"[^>]*TargetAspect="1\.0"/g) ?? []).length, 4,
  "all generated council portraits must share the centered square source geometry used by their native fallbacks");
assert.equal((royalCouncilXml.match(/ImageIdentifierWidget[^>]*SuggestedWidth="304"[^>]*SuggestedHeight="304"[^>]*HorizontalAlignment="Center"[^>]*VerticalAlignment="Center"/g) ?? []).length, 4,
  "all native council portraits must share the centered square source geometry used by generated portraits");
assert.match(royalCouncilXml,
  /Id="CouncilTranscriptCleanBackdrop"[^>]*SuggestedWidth="736"[^>]*SuggestedHeight="580"[^>]*MarginLeft="466"[^>]*MarginTop="145"[^>]*Sprite="BlankWhiteSquare_9"[^>]*Color="#10100FFF"/,
  "the transcript must cover the shell's baked dividers with the established dark interface color");
assert.ok(
  royalCouncilXml.indexOf('Id="CouncilTranscriptCleanBackdrop"')
    < royalCouncilXml.indexOf('Id="CouncilTranscriptPanel"'),
  "the clean transcript backing must remain below all runtime council text");

console.log("UI remediation layout contract: PASS");
