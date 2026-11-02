import assert from 'node:assert/strict';
import fs from 'node:fs';
import { getSampleDataForPrefab } from './js/sample-data.js';

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
