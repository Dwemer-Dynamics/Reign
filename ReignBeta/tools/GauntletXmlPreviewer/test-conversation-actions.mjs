import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import { previewActionRichText, appendActionRichText } from "./js/action-text.js";
import { getSampleDataForPrefab } from "./js/sample-data.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const cases = [];
function test(id, fn) { try { fn(); cases.push({ id, passed: true }); } catch (e) { cases.push({ id, passed: false, error: e.message }); } }
const read = (p) => fs.readFileSync(path.join(root, p), "utf8");
test("mixed_actions", () => assert.equal(previewActionRichText("*He nods.* Yes. *He turns.*"), '<span style="Action">He nods.</span> Yes. <span style="Action">He turns.</span>'));
test("unmatched_and_escaped", () => { assert.equal(previewActionRichText("*unfinished"), "*unfinished"); assert.equal(previewActionRichText("\\*literal\\*"), "*literal*"); });
test("paragraphs_and_unicode", () => assert.equal(previewActionRichText("*Él hoche la tête.*\n\nOui."), '<span style="Action">Él hoche la tête.</span>\n\nOui.'));
test("historical_text_and_player", () => { assert.equal(previewActionRichText("He nods. Yes."), "He nods. Yes."); assert.equal(previewActionRichText("*I nod.*", false), "*I nod.*"); });
test("safe_dom", () => {
  const node = (tag, text = "") => ({ tag, textContent: text, dataset: {}, children: [], replaceChildren() { this.children = []; }, appendChild(child) { this.children.push(child); } });
  globalThis.document = { createTextNode: (text) => node("#text", text), createElement: (tag) => node(tag) };
  const element = node("div");
  appendActionRichText(element, '<img src="x" onerror="evil()"><span style="Action"><script>evil()</script></span>');
  assert.deepEqual(element.children.map(x => x.tag), ["#text", "em", "#text"]);
  assert.equal(element.children[1].textContent, "<script>evil()</script>");
  delete globalThis.document;
});
for (const name of ["ReignIndividualChatScreen", "ReignPartyChatScreen", "ReignCastleChatScreen", "ReignSocialEventScreen"]) {
  test(`surface_${name}`, () => {
    const xml = read(`ReignBeta/GUI/Prefabs/${name}.xml`);
    assert.match(xml, /<RichTextWidget[^>]*Brush="Reign.Chat.ActionText.\d+"[^>]*Text="@RichText"/);
    assert.doesNotMatch(xml, /<TextWidget[^>]*Text="@Text"/);
    const data = getSampleDataForPrefab(`${name}.xml`);
    assert(data.ChatLines.length > 0 && data.ChatLines.every(x => typeof x.RichText === "string"));
  });
}
test("genuine_italic_font_and_atlas", () => {
  const provenance = JSON.parse(read("ReignBeta/artwork/ui-modern-style-kit/font-sources/action-italic-provenance.json"));
  for (const [file, expected] of Object.entries(provenance.sha256)) assert.equal(createHash("sha256").update(fs.readFileSync(path.join(root, file))).digest("hex"), expected);
  assert.match(read("ReignBeta/GUI/Fonts/ReignSerifActionItalic/ReignSerifActionItalic.fnt"), /italic="1"/);
  const manifest = JSON.parse(read("ReignBeta/GUI/RuntimeSpriteSheets/manifest.json"));
  const category = manifest.categories.ui_reignbeta_action_fonts;
  assert.equal(category.parts.ReignSerifActionItalic.width, 2048);
  for (const sheet of category.sheets) assert.equal(createHash("sha256").update(fs.readFileSync(path.join(root, "ReignBeta", sheet.path))).digest("hex"), sheet.sha256);
  const brushes = read("ReignBeta/GUI/Brushes/ReignChatActions.xml");
  for (const size of [15, 16, 17]) assert(brushes.includes(`Name="Action" Font="ReignSerifActionItalic" FontSize="${size}" FontColor="#C5BDAFFF"`));
});
test("catalog_drift", () => {
  const catalog = JSON.parse(read("reign.testing.json")).conversationIntoxication;
  assert.equal(catalog.minimumDrinks, 5); assert.equal(catalog.enduranceMultiplier, 2);
  assert.equal(catalog.hoursPerDrink, 2); assert.equal(catalog.veryIntoxicatedRatio, .5);
  assert.equal(catalog.releaseAlcoholBelowRatio, .5); assert.equal(catalog.overwhelmedRatio, 1);
  assert.deepEqual(catalog.servings, { drink: 1, half: .5, sip: .25 });
  const server = read("ReignServer/src/Modules/Dialogue/ConversationIntoxication.cs");
  assert(server.includes(catalog.stateSchema) && server.includes(catalog.receiptSchema));
  assert(read(catalog.guide).includes("conversation_intoxication"));
  assert(read("docs/agent/TESTING_TOOL_GUIDE.md").includes("conversation_intoxication"));
  assert(read("ReignServer/src/Modules/Platform/VerificationLab.cs").includes('"contracts.conversation_intoxication"'));
});
const result = { schema: "reign-conversation-action-preview-contract-v1", ok: cases.every(x => x.passed), cases };
const output = process.argv[2] || path.join(root, ".codex-build/intoxication-actions/action-preview-contract.json");
fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2)); process.exitCode = result.ok ? 0 : 1;
