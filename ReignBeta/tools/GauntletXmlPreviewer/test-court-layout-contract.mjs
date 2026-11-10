import assert from 'node:assert/strict';
import fs from 'node:fs';
import { COURT_LAYOUT as p, computeCourtLayout } from './js/court-layout.js';

const moduleRoot = new URL('../../', import.meta.url);
const read = path => fs.readFileSync(new URL(path, moduleRoot), 'utf8');
const production = read('src/Modules/Court/UI/ReignCourtLayout.cs');
for (const [key, value] of Object.entries(p))
  assert.ok(production.includes(`const float ${key} = ${value}f;`), `${key} matches production`);
const xml = read('GUI/Prefabs/ReignCourtScreen.xml');
assert.equal((xml.match(/MarginTop="@CourtWorkspaceTopInset"/g) || []).length, 2);
assert.ok(!xml.includes('DoNotUseCustomScaleAndChildren'), 'Both court states use the fitted layer scale');
assert.ok(xml.includes('SuggestedWidth="280" SuggestedHeight="125" MarginBottom="4"'));
assert.ok(xml.includes('SuggestedWidth="286" SuggestedHeight="516"'));
assert.match(xml, /SuggestedWidth="208" SuggestedHeight="34"[^>]*Brush.FontSize="15"[^>]*Text="@Title"/);
assert.match(xml, /SuggestedWidth="208" SuggestedHeight="56"[^>]*Brush.FontSize="12"[^>]*Text="@Subtitle"/);
const manager = read('src/Modules/Court/UI/ReignCourtScreenManager.cs');
assert.ok(manager.includes('ReignCourtLayout.ComputeNativeLayout(width, height, _layer.UIContext.Scale)'));
assert.ok(!manager.includes('layout.Scale * _layer.Scale'), 'The native adapter must not reapply player scale');
assert.ok(!manager.includes('height / 1080f'), 'Use the observed native base, including narrow-aspect correction');
assert.ok(manager.includes('scaleModifier = context.ScaleModifier, customScale = context.CustomScale'));
const observation = manager.slice(manager.indexOf('internal static object AutomationLayoutSnapshot'), manager.indexOf('private static bool IsPointerInsideCourt'));
assert.ok(!/(LoadMovie|AddLayer|RemoveLayer|SetInputRestrictions|TrySetFocus|PauseTime|\.ScaleModifier\s*=)/.test(observation), 'Geometry observation cannot change UI, input or scale');
const heartbeat = read('src/Modules/Platform/Integration/ReignLiveTestClient.cs');
assert.ok(heartbeat.includes('["courtUi"] = JObject.FromObject(ReignBeta.UI.ReignCourtScreenManager.AutomationLayoutSnapshot)'));
assert.ok(manager.includes('panel.GlobalPosition') && manager.includes('panel.Size'));
const app = read('tools/GauntletXmlPreviewer/js/app.js');
assert.ok(app.includes('return computeCourtLayout(width, height).scale'));
const cases = [];
for (const [width, height] of [[1920,1080],[2560,1440],[2560,1600],[2559,1598],[3440,1440],[3840,1600],[3840,2160]]) {
  const fit = computeCourtLayout(width, height);
  const base = Math.min(height / 1080, width / (1920 * .98));
  const bottom = fit.topInset * fit.scale + p.Height * fit.scale;
  assert.ok(Math.abs(bottom - (height - p.BottomHudInset * base)) < .001);
  assert.ok(p.Width * fit.scale <= width - 2 * p.SideInset * base + .001);
  assert.ok(fit.scale > base);
  cases.push({ width, height, ...fit, bottom, growthPercent: 100 * (fit.scale / base - 1) });
}
console.log(JSON.stringify({ schema: 'reign-court-layout-contract-v1', ok: true, cases }, null, 2));
