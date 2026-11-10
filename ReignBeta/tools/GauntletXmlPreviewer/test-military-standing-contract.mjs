import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { getSampleDataForPrefab } from './js/sample-data.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const xml = read('GUI/Prefabs/ReignWarCouncilScreen.xml');
const contract = JSON.parse(read('GUI/UiCalibration/modern-style-contract.json')).warCouncilMilitaryStanding;
const hash = relative => createHash('sha256').update(fs.readFileSync(path.join(root, relative))).digest('hex');
for (const asset of contract.sourceAssets) assert.equal(hash(asset.path.replace(/^ReignBeta\//, '')), asset.sha256);
assert.equal(hash('GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_calradia.png'), 'e9aeb15807953280e596f189b05bc61e9812af434eafcac3c116af873d632887');
for (const [id, bounds] of [['PeaceStandingButton', contract.peaceControl], ['WarStandingButton', contract.warControl], ['PeaceForceDropdownButton', contract.peacePercentageControl]]) {
  const attrs = xml.match(new RegExp(`<ButtonWidget Id="${id}"[^>]+>`))[0];
  for (const [attribute, value] of [['MarginLeft', bounds[0]], ['MarginTop', bounds[1]], ['SuggestedWidth', bounds[2]], ['SuggestedHeight', bounds[3]]])
    assert.ok(attrs.includes(`${attribute}="${value}"`), `${id} matches the user-authorized bounds`);
  assert.ok(attrs.includes(`IsEnabled="@${id === 'PeaceForceDropdownButton' ? 'CanChoosePeaceForce' : 'CanChangeStanding'}"`));

}
assert.ok(xml.includes('Sprite="reign_war_standing_tabs"'));
assert.ok(xml.includes('Sprite="reign_war_standing_popup"'));
assert.ok(!xml.includes('Sprite="reign_government_button"'));
assert.ok(contract.peaceControl[0] + contract.peaceControl[2] < 450, 'Peace control clears the crowned R');
assert.ok(contract.peaceControl[0] + contract.peaceControl[2] <= contract.peacePercentageControl[0], 'Peace selection and percentage selector have separate click targets');
assert.ok(contract.peacePercentageControl[0] + contract.peacePercentageControl[2] < 450, 'Percentage selector clears the crowned R');
assert.ok(contract.warControl[0] > 540, 'War control clears the crowned R');
assert.ok(xml.indexOf('Id="PeaceForceOutsideClick"') > xml.indexOf('Id="WarCouncilMapInput"'));
assert.ok(xml.indexOf('Id="PeaceForcePopup"') > xml.indexOf('Id="PeaceForceOutsideClick"'), 'Popup sits above its outside-click shield');
assert.match(xml, /Command.Click="ExecuteClosePeaceForce"/);
const base = getSampleDataForPrefab('ReignWarCouncilScreen.xml');
assert.equal(base.WarStandingSelected, true);
for (const [variant, percent] of [['war-council-peace-40', 40], ['war-council-peace-90', 90], ['war-council-peace-dropdown', 60]]) {
  const state = getSampleDataForPrefab('ReignWarCouncilScreen.xml', variant);
  assert.equal(state.PeaceStandingSelected, true); assert.equal(state.WarStandingSelected, false);
  assert.equal(state.PeaceForceOptions.length, 12);
  assert.deepEqual([...state.PeaceForceLeftOptions, ...state.PeaceForceRightOptions], state.PeaceForceOptions);
  assert.equal(state.PeaceForceLeftOptions.length, 6); assert.equal(state.PeaceForceRightOptions.length, 6);
  assert.equal(state.PeaceForceOptions.filter(o => o.Label.endsWith(' •')).length, 1);
  assert.ok(state.PeaceForceOptions.find(o => o.Label.startsWith(`${percent}%`) && o.Label.endsWith(' •')));
  assert.deepEqual(state.PeaceForceOptions.slice(1).map(o => parseInt(o.Label)), [40,45,50,55,60,65,70,75,80,85,90]);
}
const readonly = getSampleDataForPrefab('ReignWarCouncilScreen.xml', 'war-council-standing-readonly');
assert.equal(readonly.CanChangeStanding, false); assert.equal(readonly.CanChoosePeaceForce, false);
const independent = getSampleDataForPrefab('ReignWarCouncilScreen.xml', 'war-council-no-realm');
assert.equal(independent.CanChangeStanding, false); assert.equal(independent.CanChoosePeaceForce, false);
assert.equal(independent.PeaceStandingSelected, false); assert.equal(independent.WarStandingSelected, false);
assert.equal(independent.PeaceStandingUnselected, true); assert.equal(independent.WarStandingUnselected, true);
assert.equal(independent.PeaceForceText, '--');
console.log('Military standing artwork, crest clearance, popup layering and preview boundary cases passed. Native authority and click-through acceptance remain separate.');
