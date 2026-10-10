import assert from 'node:assert/strict';
import fs from 'node:fs';
import { isReferenceSurfaceCandidate, selectReferenceSurface } from './js/rendered-preview-targets.js';

const candidate = {
  attributes: { WidthSizePolicy: 'StretchToParent', HeightSizePolicy: 'StretchToParent' },
  rect: { width: 640, height: 380 }, style: { display: 'block', visibility: 'visible', opacity: '1' },
  metadata: { id: 'BanterArtwork' }, element: { classList: { contains: value => value === 'runtime-nine-region' } }
};
assert.equal(isReferenceSurfaceCandidate(candidate, 'BanterArtwork'), true);
assert.equal(isReferenceSurfaceCandidate(candidate), false);
assert.equal(isReferenceSurfaceCandidate(candidate, 'DifferentArtwork'), false);
assert.equal(isReferenceSurfaceCandidate({ ...candidate, element: null }, 'BanterArtwork'), false);
assert.equal(isReferenceSurfaceCandidate({ ...candidate, style: { ...candidate.style, display: 'none' } }, 'BanterArtwork'), false);
assert.throws(() => selectReferenceSurface([candidate, candidate], 'BanterArtwork'));

const xml = fs.readFileSync(new URL('../../GUI/Prefabs/ReignBanterPanel.xml', import.meta.url), 'utf8');
const element = id => xml.match(new RegExp(`<[^>]+\\bId="${id}"[^>]*>`))?.[0] || '';
const attribute = (id, name) => element(id).match(new RegExp(`\\b${name}="([^"]+)"`))?.[1];
const number = (id, name) => Number(attribute(id, name) || 0);
// Real production XML: the drag strip must not cover Minimize, Join or resize.
for (const width of [320, 420, 640, 1000]) {
  const minimizeLeft = width - number('MinimizeBanter', 'MarginRight') - number('MinimizeBanter', 'SuggestedWidth');
  assert.ok(number('BanterDragHandle', 'MarginLeft') + width - 164 <= minimizeLeft);
  const joinRight = width - number('JoinBanter', 'MarginRight');
  const gripLeft = width - number('BanterResizeHandle', 'MarginRight') - number('BanterResizeHandle', 'SuggestedWidth');
  assert.ok(joinRight < gripLeft);
}
assert.equal(attribute('JoinBanter', 'Command.Click'), 'ExecuteJoin');
assert.equal(attribute('MinimizeBanter', 'Command.Click'), 'ExecuteCollapse');
assert.equal(attribute('BanterResizeHandle', 'IsVisible'), '@Expanded');
assert.equal(attribute('BanterScroll', 'SuggestedHeight'), '@TranscriptHeight');
assert.equal(attribute('BanterScrollbar', 'SuggestedHeight'), '@TranscriptHeight');
console.log('Banter window reference, independent hit targets and responsive-scroll contracts passed.');
