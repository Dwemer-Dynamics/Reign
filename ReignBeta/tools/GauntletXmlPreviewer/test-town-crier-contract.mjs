import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { ASSET_BY_IMAGE_ID, ASSET_BY_WIDGET_ID, getSampleDataForPrefab } from './js/sample-data.js';
import { selectRenderedPreviewTargets } from './js/rendered-preview-targets.js';
import { GauntletRenderer, resolvePortraitPreviewAsset } from './js/renderer.js';
import { PORTRAIT_FOCUS, framedBustCrop, applyFramedBustPreview } from './js/portrait-framing.js';

const root = path.dirname(fileURLToPath(import.meta.url));
const xml = fs.readFileSync(path.resolve(root, '../../GUI/Prefabs/ReignDiplomacyAnnouncementScreen.xml'), 'utf8');
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'calibration/ui-catalog.json'), 'utf8'));
const portraitRoot = path.resolve(root, '../../PortraitCache');
const inventory = JSON.parse(fs.readFileSync(path.join(portraitRoot, 'shared-portrait-inventory.json'), 'utf8'));
for (const side of ['Actor', 'Target']) {
  const widget = xml.match(new RegExp(`<ReignPortraitWidget Id="${side}AIPortrait"[^>]+>`))[0];
  for (const property of ['SuggestedWidth="143"', 'SuggestedHeight="240"', 'UseFramedBust="true"', 'TargetAspect="0.5958333"']) assert.ok(widget.includes(property));
  const fallback = xml.match(new RegExp(`<ImageIdentifierWidget Id="${side}NativePortrait"[^>]+>`))[0];
  for (const property of ['SuggestedWidth="143"', 'SuggestedHeight="143"', 'HorizontalAlignment="Center"', 'VerticalAlignment="Center"']) assert.ok(fallback.includes(property), 'Native fallback fits inside the aperture without cropping its head.');
  const sample = getSampleDataForPrefab('ReignDiplomacyAnnouncementScreen.xml', 'news-capture');
  const asset = sample[side + 'PortraitAsset'];
  assert.equal(ASSET_BY_IMAGE_ID[sample[side + 'PortraitCacheKey']], asset, 'AI and native preview layers identify the same person.');
  const renderer = new GauntletRenderer(null, ASSET_BY_IMAGE_ID, ASSET_BY_WIDGET_ID);
  for (const layer of ['Native', 'AI']) {
    const attrs = { Id: side + layer + 'Portrait', ...(layer === 'Native' ? { ImageId: '@' + side + 'PortraitId' } : { PortraitCacheKey: '@' + side + 'PortraitCacheKey' }) };
    const node = { nodeName: layer === 'Native' ? 'ImageIdentifierWidget' : 'ReignPortraitWidget', getAttribute: key => attrs[key] ?? null };
    assert.equal(renderer.resolveImageAsset(node, sample, sample[side + 'PortraitCacheKey']), asset, 'Actual renderer may not override the named person with a fixed preview portrait.');
  }
  const source = path.resolve(root, resolvePortraitPreviewAsset(asset, 'framed-bust'));
  const bytes = fs.readFileSync(source);
  assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', 'Portrait is hydrated PNG data, not a missing asset or LFS pointer.');
  const relative = path.relative(path.join(portraitRoot, '_shared'), source).replaceAll('\\', '/');
  const expected = inventory.files.find(f => f.path === relative);
  assert.ok(expected, 'Portrait is part of the tracked shipped inventory.');
  assert.equal(createHash('sha256').update(bytes).digest('hex'), expected.sha256);
}
for (const [name, focus] of Object.entries(PORTRAIT_FOCUS)) {
  const manifest = JSON.parse(fs.readFileSync(path.join(portraitRoot, '_shared', name, '.portrait_derivatives.json'), 'utf8'));
  assert.deepEqual(focus, { width: manifest.SourceWidth, height: manifest.SourceHeight, x: manifest.FaceX, y: manifest.FaceY, faceWidth: manifest.FaceWidth, faceHeight: manifest.FaceHeight });
  const crop = framedBustCrop(focus.width, focus.height, 143 / 240, focus);
  assert.ok(crop.x >= 0 && crop.y >= 0 && crop.x + crop.width <= focus.width && crop.y + crop.height <= focus.height);
  assert.ok(crop.y <= manifest.CropY, `${name}: the full portrait retains all available headroom`);
  assert.ok(focus.faceHeight * focus.height / crop.height <= .3, `${name}: the face must occupy at most thirty percent of the oval height, leaving hair and shoulder context`);
  const element = { style: {}, dataset: {} };
  applyFramedBustPreview(element, `../../PortraitCache/_shared/${name}/portrait.png`, 143, 240);
  assert.deepEqual(JSON.parse(element.dataset.framedBustCrop), crop);
  assert.equal(element.style.backgroundRepeat, 'no-repeat');
}
const states = selectRenderedPreviewTargets(catalog, 'diplomacy-announcement').interfaces;
assert.equal(states.filter(s => !s.id.startsWith('government-news-')).length, 9, 'Existing diplomacy and all news layouts remain included.');
assert.equal(states.filter(s => s.id.startsWith('government-news-')).length, 4, 'Government editions include all, filtered, long and empty cases.');
assert.match(xml, /Sprite="reign_diplomacy_modern_shell"/);
assert.equal((xml.match(/<ScrollablePanel Id="DiplomacyReportViewport"/g) || []).length, 1, 'The standard report retains its complete reading viewport.');
assert.match(xml, /IsVisible="@IsStandardAnnouncement"/);
assert.match(xml, /IsVisible="@IsGovernmentNews"/);
assert.match(xml, /NewsKingdomCount/);
const viewport = xml.match(/<ScrollablePanel Id="DiplomacyReportViewport"[^>]+>/)[0];
for (const expected of ['SuggestedWidth="732"', 'SuggestedHeight="480"', 'MarginTop="260"', 'ClipRect="ReportClip"', 'InnerPanel="ReportClip\\ReportFlow"', 'VerticalScrollbar="..\\DiplomacyReportScrollbar"', 'MouseScrollAxis="Vertical"']) assert.ok(viewport.includes(expected), expected);
const content = xml.match(/<TextWidget Id="DiplomacyReportText"[^>]+>/)[0];
for (const expected of ['HeightSizePolicy="CoverChildren"', 'SuggestedWidth="708"', 'Brush.FontSize="24"', 'Brush.TextHorizontalAlignment="Left"', 'Brush.Font="ReignSerifDynamic"', 'Brush.FontColor="#C5BDAFFF"', 'Text="@ReportText"']) assert.ok(content.includes(expected), expected);
assert.doesNotMatch(xml, /DiplomacySummaryViewport|DiplomacyTermsViewport|DiplomacyDivider/);
const longReport = getSampleDataForPrefab('ReignDiplomacyAnnouncementScreen.xml', 'diplomacy-long');
assert.ok(longReport.ReportText.length > 2000);
assert.ok(longReport.ReportText.endsWith('whose losses have yet to be answered.”'), 'The final ruler explanation is retained.');
assert.ok(longReport.ActorReason.length < 30 && longReport.TargetReason.length < 30, 'Portrait captions contain short roles.');
const samples = states.filter(s => s.id.startsWith('news-')).map(s => ({ id: s.id, data: getSampleDataForPrefab('ReignDiplomacyAnnouncementScreen.xml', s.variant) }));
for (const { id, data } of samples) {
  assert.ok(data.Title && data.Summary && data.Outcome, id);
  assert.equal(data.ReportText, [data.Summary, data.Terms].filter(Boolean).join('\n\n'), 'Complete news copy flows into the reading column.');
  assert.doesNotMatch(data.Summary + data.Terms, /charm_roll|support_lords|preserve_relations|political pressure|affinity|trait score|Petitioners:/i);
  assert.ok(data.ActorPortraitAsset.includes('Caladog'), 'Preview portrait matches the named ruler.');
}
const byId = Object.fromEntries(samples.map(s => [s.id, s.data]));
assert.match(byId['news-sentence'].Summary, /fifteen days after this sentence/);
assert.match(byId['news-sentence'].Terms, /No ransom/);
assert.doesNotMatch(byId['news-execution'].Summary, /will be|fifteen days/);
assert.match(byId['news-clan'].Terms, /favor of Fen Gruffendoc/);
assert.ok(byId['news-foreign'].Terms.length > 300, 'Foreign ruling exercises the long-copy field.');
assert.match(byId['news-foreign'].Summary, /Caladog of Battania rejected the demand for stronger action against protected smugglers/);
assert.match(byId['news-foreign'].Summary, /Derthert of Vlandia rejected the demand/);
assert.match(byId['news-foreign'].Terms, /Both refusals angered the petitioning nobles/);
assert.match(byId['news-foreign'].Terms, /did not launch enforcement action or change a treaty/);
assert.doesNotMatch(byId['news-foreign'].Summary + byId['news-foreign'].Terms, /backed the petitioning nobles and their appeal|the other realm have eased/);
assert.equal(byId['news-world'].ShowTarget, false);
assert.equal(byId['news-golden-age'].ShowTarget, false);
assert.match(byId['news-world'].Summary, /less food/);
assert.match(byId['news-golden-age'].Summary, /Construction/);
assert.doesNotMatch(byId['news-golden-age'].Summary, /prosperity/);
console.log(JSON.stringify({ schema: 'reign-town-crier-preview-contract-v1', ok: true, variants: samples.map(s => s.id), nativeScrollingProven: false }));
