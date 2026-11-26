import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import { getSampleDataForPrefab } from './js/sample-data.js';

const readModule = path => fs.readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8');
const contract = JSON.parse(readModule('GUI/UiCalibration/modern-style-contract.json'));
const manifest = JSON.parse(readModule('artwork/ui-modern-style-kit/asset-manifest.json'));
const shell = manifest.generatedRuntimeAssets.find(asset => asset.id === 'individual-chat-modern-shell');
const shellBytes = fs.readFileSync(new URL(`../../../${shell.path}`, import.meta.url));
assert.equal(createHash('sha256').update(shellBytes).digest('hex'), shell.sha256);
const fixedRegionSpec = JSON.parse(fs.readFileSync(new URL(`../../../${shell.fixedRegionSpecPath}`, import.meta.url), 'utf8'));
const viewMask = fixedRegionSpec.sceneDependentMasks.find(mask => mask.id === 'conditional-chat-view-control');

const individual = readModule('GUI/Prefabs/ReignIndividualChatScreen.xml');
const outfit = individual.match(/<ButtonWidget Id="ReignChangeClothesButton"[^>]*>/)[0];
const view = individual.match(/<Widget Id="ChatViewControl"[^>]*>/)[0];
const numberAttribute = (widget, name) => Number(widget.match(new RegExp(`${name}="([0-9.]+)"`))[1]);
const geometry = ['MarginLeft', 'MarginTop', 'SuggestedWidth', 'SuggestedHeight'].map(name => numberAttribute(view, name));
assert.deepEqual(geometry, contract.chatView.controls.individual);
assert.deepEqual(geometry, [viewMask.x, viewMask.y, viewMask.width, viewMask.height]);
const [, viewTop, , viewHeight] = geometry;
const outfitBottom = numberAttribute(outfit, 'MarginTop') + numberAttribute(outfit, 'SuggestedHeight');
assert.ok(outfitBottom < viewTop && viewTop + viewHeight < contract.chatView.individualLocationOrnamentTop,
  'View must clear the optional clothes button and the Location frame ornament');
assert.match(individual, /<ButtonWidget Id="ChatViewButton"[^>]*Command\.Click="ExecuteView"[^>]*IsEnabled="@CanView"/);

const vmRoot = 'src/Modules/Dialogue/UI/ViewModels/';
const individualVm = readModule(vmRoot + 'ReignIndividualChatScreenVM.cs');
const outfitVm = readModule(vmRoot + 'ReignIndividualChatOutfit.cs');
assert.match(individualVm, /_presentationInitializing = false;\s+RefreshInputAvailability\(\);/,
  'Completing presentation must refresh the nested View binding without waiting for a dialogue send');
assert.match(individualVm, /_isPregnancyWarningVisible = value;\s+OnPropertyChangedWithValue\(value\);\s+RefreshInputAvailability\(\);/);
assert.match(outfitVm, /private void RefreshOutfit\([\s\S]*?OnPropertyChanged\(property\);\s+RefreshInputAvailability\(\);/);
assert.match(individualVm, /private void RefreshInputAvailability\(\)\s*\{\s*_view\?\.RefreshAvailability\(\);/);

let checks = 0;
for (const screen of ['Individual', 'Party', 'Castle']) {
  const file = `Reign${screen}ChatScreen.xml`;
  const xml = fs.readFileSync(new URL(`../../GUI/Prefabs/${file}`, import.meta.url), 'utf8');
  assert.match(xml, /DataSource="\{View\}"/);
  assert.match(xml, /Command.Click="ExecuteView"/);
  assert.match(xml, /Command.Click="ExecuteOpenScene"/);
  assert.match(xml, /Command.Click="ExecuteCloseImage"/);
  assert.match(xml, /EventImageId="@EnlargedImageId"/);
  assert.match(xml, /IsVisible="@IsViewLine"/);
  for (const state of ['pending', 'ready', 'enlarged', 'failed', 'long']) {
    const data = getSampleDataForPrefab(file, `view-${state}`);
    const row = data.ChatLines.find(line => line.IsViewLine);
    assert.ok(row, `${file}/${state}: missing image entry`);
    assert.equal(row.IsNonPlayerLine, false, 'Image entry must not be rendered again as dialogue');
    assert.equal(row.IsTextLine, false);
    assert.equal(row.HasSceneImage, ['ready', 'enlarged', 'long'].includes(state));
    assert.equal(data.View.IsEnlarged, state === 'enlarged');
    assert.equal(data.InputEnabled, true, 'Image work must leave typing available');
    checks++;
  }
}
console.log(JSON.stringify({ok:true, checks}));
