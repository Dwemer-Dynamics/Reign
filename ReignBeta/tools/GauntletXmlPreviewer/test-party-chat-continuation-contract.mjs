import assert from 'node:assert/strict';
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getSampleDataForPrefab } from './js/sample-data.js';

const xml = fs.readFileSync(new URL('../../GUI/Prefabs/ReignPartyChatScreen.xml', import.meta.url), 'utf8');
const tag = id => xml.match(new RegExp(`<[^>]+Id="${id}"[^>]*>`))?.[0] || '';
for (const [id,x] of [['ChatViewControl',1358],['PartyChatContinueControl',1218]]) {
  const owner = tag(id);
  for (const [key,value] of Object.entries({ SuggestedWidth:120, SuggestedHeight:36, MarginLeft:x, MarginTop:98 }))
    assert.ok(owner.includes(`${key}="${value}"`), `${id}/${key}`);
}
const view = xml.slice(xml.indexOf('<Widget Id="ChatViewControl"'), xml.indexOf('<Widget Id="PartyChatHeader"'));
assert.ok(!view.includes('reign_outfit_action_button') && !view.includes('reign_tavern_house_eye'));
assert.equal((view.match(/reign_party_chat_compact_button/g)||[]).length, 2);
assert.equal((view.match(/Brush.FontSize="15"/g)||[]).length, 4);
assert.ok(view.includes('Command.Click="ExecuteContinuePartyChat"'));
assert.ok(view.includes('IsEnabled="@CanContinuePartyChat"'));
assert.ok(view.includes('IsVisible="@ShowPartyContinuation"'));
for (const [variant,canContinue] of [['continue-available',true],['continue-unavailable',false],['continue-restored',false]]) {
  const data = getSampleDataForPrefab('ReignPartyChatScreen.xml',variant);
  assert.equal(data.ShowPartyContinuation,true);
  assert.equal(data.CanContinuePartyChat,canContinue);
  if (variant==='continue-restored') assert.ok(data.ChatLines.some(l=>l.IsNpcLine));
}
for (const variant of ['homes-directory','homes-private-chat'])
  assert.equal(getSampleDataForPrefab('ReignPartyChatScreen.xml',variant).ShowPartyContinuation,false);
const pixels = spawnSync('python',[fileURLToPath(new URL('./prepare-party-chat-continuation-assets.py',import.meta.url))],{encoding:'utf8'});
assert.equal(pixels.status,0,pixels.stderr);
console.log(JSON.stringify({ok:true, geometry:'Clear-identical126x40 art,120x36 hitboxes', pixelContract:JSON.parse(pixels.stdout)}));
