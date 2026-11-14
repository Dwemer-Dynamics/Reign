import assert from "node:assert/strict";
import fs from "node:fs";
import { getSampleDataForPrefab, getPreviewFixtureInventory } from "./js/sample-data.js";
import { TAVERN_HOUSE_STATES } from "./js/tavern-house-sample-data.js";
import { selectRenderedPreviewTargets } from "./js/rendered-preview-targets.js";

const catalog = JSON.parse(fs.readFileSync(new URL("./calibration/ui-catalog.json", import.meta.url), "utf8"));
const entry = catalog.interfaces.find(item => item.id === "tavern-house");
assert.ok(entry);
assert.equal(entry.movie, "ReignTavernHouseScreen");
assert.equal(entry.liveAction, "ui-open --target tavern-house");
assert.ok(catalog.nativeCalibration.providerFreeFixtureTargets.includes("tavern-house"));
assert.ok(getPreviewFixtureInventory().some(item => item.fileName === "ReignTavernHouseScreen.xml"));
const selected = selectRenderedPreviewTargets(catalog, "tavern-house");
assert.equal(selected.partialScope, true, "A feature-only render must never claim a complete product matrix.");
assert.equal(selected.interfaces.length, TAVERN_HOUSE_STATES.length + 1);
const fixture = (state = "") => getSampleDataForPrefab("ReignTavernHouseScreen.xml", state && `tavern-house-${state}`);
for (const person of [...fixture().Madam, ...fixture().Workers]) {
  assert.match(person.PortraitAsset, /\/portrait\.png$/);
  assert.ok(fs.existsSync(new URL(person.PortraitAsset, import.meta.url)), "Portrait fixture must resolve a tracked original.");
  assert.ok(fs.existsSync(new URL(person.PortraitAsset.replace(/\/portrait\.png$/, "/portrait_chest.png"), import.meta.url)), "Headshot fixture must resolve its unchanged chest derivative.");
}
for (const state of TAVERN_HOUSE_STATES) {
  const variant = entry.previewStates.find(item => item.id === `tavern-house-${state}`);
  assert.ok(variant?.previewOnly && variant.nativeInjection === false);
  const data = fixture(state);
  assert.equal(data.Madam.length, 1);
  assert.equal(data.Workers.length, 5, "The maximum supported native cast contains five workers plus the madam.");
  assert.ok(data.ChatLines.length >= 2);
  assert.ok(data.ChatLines.every(line => typeof line.RichText === "string"));
  assert.ok(data.Workers.every(person => person.Name && person.PortraitCacheKey));
}
assert.equal(fixture().IsVisiting, false);
assert.equal(fixture().CanLookAgain, false);
assert.equal(fixture("quote").HasQuote, false, "A visit draft uses the protected sentence, not the old payment popup.");
assert.match(fixture("quote").InputText, /pay you 300 denars[\s\S]+Beatrice Voss: 140[\s\S]+Oren Mercato: 160/);
assert.equal(fixture("payment-pending").HasQuote, false);
assert.equal(fixture("payment-pending").PaymentConfirmationPending, true);
assert.equal(fixture("payment-pending").CanSend, false);
assert.equal(fixture("payment-pending").CanAccept, true);
assert.equal(fixture("payment-pending").AcceptText, "RETRY CONFIRMATION");
assert.equal(fixture("paid").CanLookAgain, true);
assert.equal(fixture("image-pending").CanLookAgain, false);
assert.equal(fixture("image-pending").CanSend, true);
assert.equal(fixture("image-pending").HasSceneImage, true, "Keep the last successful scene visible during replacement.");
assert.equal(fixture("image-failed").CanLookAgain, true);
assert.equal(fixture("images-disabled").CanLookAgain, false);
assert.equal(fixture("images-disabled").ImagesEnabled, false);
assert.ok(fixture("portraits-missing").Workers.every(person => !person.HasGeneratedPortrait));
assert.equal(fixture("max-workers-long-chat").ChatLines.length, 30);
assert.equal(fixture("max-workers-long-chat").Workers.filter(person => person.IsActive).length, 4);
assert.equal(fixture("portrait-preview").IsPortraitPreviewVisible, true);
assert.equal(fixture("recruitment-offer").HasQuote, true);
assert.equal(fixture("recruitment-offer").AcceptText, "ACCEPT RECRUITMENT");
assert.match(fixture("recruitment-offer").QuoteText, /Oren Mercato[\s\S]+500 denars/);
assert.equal(fixture("image-pending").HasImageStatus, true);
assert.equal(fixture("image-failed").HasImageStatus, true);
assert.throws(() => fixture("unregistered-state"));

for (const state of ["names", "price", "free", "offer"]) {
  const id = `action-builder-tavern-${state}`;
  assert.ok(catalog.interfaces.find(item => item.id === "action-builder").previewStates.some(item => item.id === id && item.previewOnly));
  const data = getSampleDataForPrefab("ReignActionBuilder.xml", id);
  assert.ok(data.Title && data.Sentence);
  assert.equal(data.Recipient, "To Livia Bellori");
  if (state === "offer") {
    assert.match(data.Sentence, /Livia Bellori: 450.*Beatrice Voss: 140.*Oren Mercato: 0.*Tobias Marin: 160/);
    assert.equal(data.Choices.length, 2);
    assert.ok(data.Choices.every(choice => !/another action|timing|installments|give…/.test(choice.Label)));
  }
}

const xml = fs.readFileSync(new URL("../../GUI/Prefabs/ReignTavernHouseScreen.xml", import.meta.url), "utf8");
const node = (id) => xml.match(new RegExp(`<[^>]+\\bId="${id}"[^>]*>`))?.[0] || "";
assert.match(node("TavernHouseCanvas"), /SuggestedWidth="1672"/);
assert.match(node("TavernHouseCanvas"), /SuggestedHeight="941"/);
assert.match(node("TavernHouseCanvas"), /DoNotUseCustomScaleAndChildren="true"/);
assert.match(node("TavernSceneViewport"), /SuggestedWidth="532"[^>]+SuggestedHeight="532"/);
assert.match(node("TavernSceneViewport"), /ClipContents="true"/);
assert.match(node("TavernWorkerViewport"), /MouseScrollAxis="Horizontal"/);
assert.match(node("TavernWorkerViewport"), /ClipRect="TavernWorkerClip"/);
assert.match(node("TavernWorkerList"), /DataSource="\{Workers\}"/);
assert.ok(xml.indexOf('Id="TavernHouseShell"') < xml.indexOf('DataSource="{Workers}"'), "Cards move above the fixed shell.");
assert.ok(xml.indexOf('DataSource="{Workers}"') < xml.indexOf('Id="TavernWorkerCardPlate"'));
assert.ok(xml.includes('Command.Click="ExecutePreviewPortrait"') && xml.includes('Command.Click="ExecuteGenerateAIPortrait"'));
assert.match(xml, /Command.Click="ExecuteLookAgain"[^>]+IsEnabled="@CanLookAgain"/);
assert.match(xml, /IsVisible="@PaymentConfirmationPending"[^>]+IsEnabled="@CanAccept"/);
assert.match(xml, /IsVisible="@HasQuote"/);
for (const tag of xml.matchAll(/<(?:TextWidget|EditableTextWidget)\b[^>]*>/g))
  assert.match(tag[0], /Brush.Font="ReignSerifDynamic"/);
console.log(`Tavern house preview contract: PASS (${TAVERN_HOUSE_STATES.length + 1} declared views; native acceptance remains required).`);
