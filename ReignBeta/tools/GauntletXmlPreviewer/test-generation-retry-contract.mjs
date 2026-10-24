import assert from 'node:assert/strict';
import fs from 'node:fs';
import {getSampleDataForPrefab} from './js/sample-data.js';

const normal = getSampleDataForPrefab('ReignIndividualChatScreen.xml');
const failed = getSampleDataForPrefab('ReignIndividualChatScreen.xml', 'generation-failed');
const available = getSampleDataForPrefab('ReignIndividualChatScreen.xml', 'recovery-available');
const review = getSampleDataForPrefab('ReignIndividualChatScreen.xml', 'recovery-review');
assert.equal(normal.SendButtonText, 'Send');
assert.equal(failed.SendButtonText, 'Retry');
assert.equal(failed.IsBusy, false);
assert.equal(failed.InputEnabled, true);
assert.match(failed.BusyText, /429.*Retry/);
assert.equal(failed.ChatLines.filter(x => x.Role === 'player').length, 1);
assert.equal(failed.ChatLines.filter(x => x.Role === 'npc').length, 0);
assert.ok(failed.ChatLines.some(x => x.Role === 'system' && /saved.*Retry/.test(x.Text)));
assert.ok(failed.ChatLines.find(x => x.Role === 'system').IsNonPlayerLine, 'System errors must be visible in the existing non-player row');
const catalog = JSON.parse(fs.readFileSync(new URL('./calibration/ui-catalog.json', import.meta.url)));
assert.ok(catalog.interfaces.find(x => x.id === 'individual-chat').previewStates.some(x => x.id === 'individual-chat-generation-failed'));
assert.equal(available.SendButtonText, 'Recover');
assert.equal(available.InputText, '');
assert.equal(available.ChatLines.filter(x => x.Role === 'player').length, 0);
assert.ok(available.ChatLines.some(x => x.Role === 'system' && /saved.*unconfirmed/.test(x.Text)));
assert.equal(review.SendButtonText, 'Send');
assert.equal(review.InputText, 'Good morning.');
assert.equal(review.ChatLines.filter(x => x.Role === 'player').length, 0);
assert.match(review.BusyText, /Review.*new message/);
for (const id of ['individual-chat-recovery-available', 'individual-chat-recovery-review']) {
  const state = catalog.interfaces.find(x => x.id === 'individual-chat').previewStates.find(x => x.id === id);
  assert.equal(state.previewOnly, true);
  assert.equal(state.nativeInjection, false);
}
console.log(JSON.stringify({schema:'reign-conversation-retry-preview-contract-v1',ok:true,checks:22,providerCalls:0}));
