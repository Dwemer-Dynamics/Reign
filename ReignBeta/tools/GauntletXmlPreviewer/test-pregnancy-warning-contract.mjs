import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { getSampleDataForPrefab } from "./js/sample-data.js";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const workspaceRoot = path.resolve(toolRoot, "../../..");
const prefabPath = path.join(workspaceRoot, "ReignBeta", "GUI", "Prefabs", "ReignIndividualChatScreen.xml");
const managerPath = path.join(workspaceRoot, "ReignBeta", "src", "Modules", "Dialogue", "UI", "ReignIndividualChatScreenManager.cs");
const viewModelPath = path.join(workspaceRoot, "ReignBeta", "src", "Modules", "Dialogue", "UI", "ViewModels", "ReignIndividualChatScreenVM.cs");
const catalogPath = path.join(toolRoot, "calibration", "ui-catalog.json");
const nativeParityAuditPath = path.join(toolRoot, "audit-native-parity.mjs");
const indexPath = path.join(toolRoot, "index.html");
const outputPath = argumentValue("--output")
  ? path.resolve(argumentValue("--output"))
  : path.join(workspaceRoot, ".codex-build", "ui-preview", "pregnancy-warning-contract.json");

const xml = fs.readFileSync(prefabPath, "utf8");
const manager = fs.readFileSync(managerPath, "utf8");
const viewModel = fs.readFileSync(viewModelPath, "utf8");
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const nativeParityAudit = fs.readFileSync(nativeParityAuditPath, "utf8");
const indexHtml = fs.readFileSync(indexPath, "utf8");
const documentNode = parseXml(xml);
const checks = [];

check("warning-is-last-render-late-root-layer", () => {
  const warning = requireNode("ReignPregnancyWarningLayer");
  const zoom = requireNode("ReignChatZoomBackdrop");
  assert.equal(warning.parent, zoom.parent, "warning and portrait zoom must share the root visual stack");
  assert.equal(warning.parent.children.at(-1), warning, "the warning must be the final direct root child so it renders above Individual Chat");
  assert.equal(warning.attributes.RenderLate, "true");
  assert.equal(warning.attributes.IsVisible, "@IsPregnancyWarningVisible");
  assert.equal(warning.attributes.WidthSizePolicy, "StretchToParent");
  assert.equal(warning.attributes.HeightSizePolicy, "StretchToParent");
});

check("shield-is-translucent-nonwhite-input-blocker", () => {
  const shield = requireNode("ReignPregnancyWarningInputShield");
  const rgba = parseColor(shield.attributes.Color || shield.attributes["Brush.Color"] || "");
  assert.ok(rgba.a > 0 && rgba.a < 255, `shield alpha ${rgba.a} must preserve the underlying interface while visibly dimming it`);
  assert.notDeepEqual([rgba.r, rgba.g, rgba.b], [255, 255, 255], "the shield must never recreate the old white full-screen backing");
  assert.equal(shield.attributes["Command.Click"], "ExecuteNoOp");
  assert.equal(shield.attributes.DoNotPassEventsToChildren, "true");
  assert.equal(shield.attributes.WidthSizePolicy, "StretchToParent");
  assert.equal(shield.attributes.HeightSizePolicy, "StretchToParent");
});

check("compact-panel-and-hitboxes-are-contained", () => {
  const wrapper = parentWidgetById("ReignPregnancyWarningPanel");
  const panel = requireNode("ReignPregnancyWarningPanel");
  const wrapperSize = fixedSize(wrapper);
  const panelSize = fixedSize(panel);
  assert.deepEqual(wrapperSize, { width: 1672, height: 941 });
  assert.deepEqual(panelSize, { width: 900, height: 675 });
  assert.ok(panelSize.width < 1100 && panelSize.height < 825, "the modern warning must remain materially smaller than the obsolete 1100x825 panel");
  assert.equal(wrapper.attributes.HorizontalAlignment, "Center");
  assert.equal(wrapper.attributes.VerticalAlignment, "Center");
  assert.equal(panel.attributes.HorizontalAlignment, "Center");
  assert.equal(panel.attributes.VerticalAlignment, "Center");

  const pullOut = fixedRect(requireNode("ReignPregnancyPullOutButton"));
  const proceed = fixedRect(requireNode("ReignPregnancyProceedButton"));
  assertRectInside(pullOut, panelSize, "Pull Out");
  assertRectInside(proceed, panelSize, "Proceed");
  assert.ok(pullOut.x + pullOut.width <= proceed.x, "the two button hitboxes must not overlap");
});

check("sprite-and-dynamic-copy-contract", () => {
  const panel = requireNode("ReignPregnancyWarningPanel");
  const spriteLayer = descendants(panel).find((node) => node.attributes.Sprite === "reign_pregnancy_warning");
  assert.ok(spriteLayer, "the compact textless modern warning sprite must be present");
  assert.equal(spriteLayer.attributes.DoNotAcceptEvents, "true");

  const title = requireNode("ReignPregnancyWarningTitle");
  const message = requireNode("ReignPregnancyWarningMessage");
  const footer = requireNode("ReignPregnancyWarningFooter");
  assert.equal(title.attributes.Text, "Warning");
  assert.equal(message.attributes.Text, "This choice may result in pregnancy.");
  assert.equal(footer.attributes.Text, "Choose carefully.");
  assert.equal(requireNode("ReignPregnancyPullOutButton").attributes["Command.Click"], "ExecutePullOut");
  assert.equal(requireNode("ReignPregnancyProceedButton").attributes["Command.Click"], "ExecuteProceedPregnancy");
});

check("popup-font-and-frozen-colors", () => {
  const panel = requireNode("ReignPregnancyWarningPanel");
  const textNodes = descendants(panel).filter((node) => node.tag === "TextWidget");
  assert.equal(textNodes.length, 5, "warning copy must remain five live text widgets: title, body, two actions, and footer");
  const allowed = new Set(["#C5AC83FF", "#C5BDAFFF", "#8A8883FF"]);
  for (const node of textNodes) {
    assert.equal(node.attributes["Brush.Font"], "ReignSerifDynamic", `${node.attributes.Id || node.attributes.Text} must use the packaged dynamic serif`);
    assert.ok(allowed.has(String(node.attributes["Brush.FontColor"] || "").toUpperCase()), `${node.attributes.Id || node.attributes.Text} uses an unfrozen popup text color`);
    assert.equal(node.attributes.DoNotAcceptEvents, "true");
  }
  assert.equal(requireNode("ReignPregnancyWarningTitle").attributes["Brush.FontColor"].toUpperCase(), "#C5AC83FF");
  assert.equal(requireNode("ReignPregnancyWarningMessage").attributes["Brush.FontColor"].toUpperCase(), "#C5BDAFFF");
  assert.equal(requireNode("ReignPregnancyWarningFooter").attributes["Brush.FontColor"].toUpperCase(), "#8A8883FF");
});

check("chat-input-and-manager-guards", () => {
  assert.match(xml, /<EditableTextWidget\b[^>]*\bIsEnabled="@InputEnabled"/s);
  assert.match(manager, /_dataSource\s*==\s*null\s*\|\|\s*!_dataSource\.IsPregnancyWarningVisible/);
  assert.match(manager, /_dataSource\s*!=\s*null[\s\S]{0,240}!_dataSource\.IsPregnancyWarningVisible[\s\S]{0,240}ExecuteSend\s*\(/);
  assert.match(viewModel, /public\s+bool\s+InputEnabled[\s\S]{0,180}return\s+!IsBusy\s*&&\s*!IsPregnancyWarningVisible\s*;/);
  assert.match(viewModel, /string\.IsNullOrWhiteSpace\(text\)\s*\|\|\s*IsBusy\s*\|\|\s*IsPregnancyWarningVisible/);
  assert.match(viewModel, /OnPropertyChanged\(nameof\(InputEnabled\)\)/);
});

check("preview-preset-and-nested-catalog-state", () => {
  const normal = getSampleDataForPrefab("ReignIndividualChatScreen.xml");
  const warning = getSampleDataForPrefab("ReignIndividualChatScreen.xml", "pregnancy-warning");
  assert.equal(normal.IsPregnancyWarningVisible, false);
  assert.equal(normal.InputEnabled, true);
  assert.equal(normal.IsBusy, false);
  assert.equal(warning.IsPregnancyWarningVisible, true);
  assert.equal(warning.InputEnabled, false);
  assert.equal(warning.IsBusy, true);
  assert.equal(warning.BusyText, "Choose whether to proceed.");

  const individualChat = catalog.interfaces.find((entry) => entry.id === "individual-chat");
  assert.ok(individualChat, "Individual Chat must remain a normal top-level native calibration target");
  const state = individualChat.previewStates?.find((entry) => entry.id === "individual-chat-pregnancy-warning");
  assert.ok(state, "the warning must be a nested preview state rather than a second native calibration target");
  assert.equal(state.variant, "pregnancy-warning");
  assert.equal(state.previewOnly, true);
  assert.equal(state.nativeInjection, false);
  assert.equal(catalog.nativeCalibration.targetCount, catalog.interfaces.filter((entry) => !entry.supportUi).length,
    "nested preview states must not change the native target count");
  assert.match(indexHtml, /data-interface="individual-chat-pregnancy-warning"[^>]*data-prefab="\.\.\/\.\.\/GUI\/Prefabs\/ReignIndividualChatScreen\.xml"[^>]*data-preset="pregnancy-warning"/);
});

check("native-runtime-state-evidence-is-fail-closed-and-nested", () => {
  const individualChat = catalog.interfaces.find((entry) => entry.id === "individual-chat");
  const state = individualChat?.previewStates?.find((entry) => entry.id === "individual-chat-pregnancy-warning");
  const evidence = state?.nativeEvidence;
  assert.ok(evidence?.required, "the pregnancy warning must require durable native runtime-state evidence");
  assert.equal(evidence.setupAction, "show-pregnancy-warning");
  assert.equal(evidence.matrix, "inherit-parent");
  assert.equal(evidence.providerRenderId, state.id);
  assert.equal(evidence.acceptanceRelativePath, `states/${state.id}/acceptance.json`);
  assert.deepEqual(evidence.requiredVisibleWidgetIds, [
    "ReignPregnancyWarningLayer",
    "ReignPregnancyWarningInputShield",
    "ReignPregnancyWarningPanel",
    "ReignPregnancyPullOutButton",
    "ReignPregnancyProceedButton"
  ]);
  assert.deepEqual(evidence.requiredEnabledWidgetIds, [
    "ReignPregnancyWarningInputShield",
    "ReignPregnancyPullOutButton",
    "ReignPregnancyProceedButton"
  ]);
  assert.equal(evidence.statusReceiptCommand, "ui_status");
  assert.equal(evidence.statusAssertions.target, "individual-chat");
  assert.equal(evidence.statusAssertions.movieName, "ReignIndividualChatScreen");
  assert.equal(evidence.statusAssertions.individualChatPregnancy?.warningVisible, undefined,
    "status assertions must use explicit dotted paths rather than an unchecked nested object");
  assert.equal(evidence.statusAssertions["individualChatPregnancy.warningVisible"], true);
  assert.equal(evidence.statusAssertions["individualChatPregnancy.inputEnabled"], false);
  assert.equal(evidence.statusAssertions["individualChatPregnancy.providerFreeFixture"], true);

  const runtimeSurfaceCount = catalog.interfaces.filter((entry) => !entry.supportUi).length;
  assert.equal(runtimeSurfaceCount + catalog.nativeAugmentations.targets.length, 33,
    "nested runtime states must not increase the 33 movie/augmentation surface count");
  assert.match(nativeParityAudit, /const nativeRuntimeStates = standalone\.flatMap/);
  assert.match(nativeParityAudit, /requiredRuntimeStates: nativeRuntimeStates\.length/);
  assert.match(nativeParityAudit, /reign-ui-native-runtime-state-acceptance-v1/);
  assert.match(nativeParityAudit, /native state required widget must appear exactly once/);
  assert.match(nativeParityAudit, /native state status receipt command was not completed exactly once/);
  assert.match(nativeParityAudit, /native state status assertion failed/);
  assert.match(nativeParityAudit, /provider render linkage incomplete or failed/);
});

const report = {
  schema: "reign-ui-pregnancy-warning-contract-v1",
  generatedUtc: new Date().toISOString(),
  prefabPath,
  managerPath,
  viewModelPath,
  catalogPath,
  nativeParityAuditPath,
  checkCount: checks.length,
  passedCount: checks.filter((entry) => entry.passed).length,
  failedCount: checks.filter((entry) => !entry.passed).length,
  passed: checks.every((entry) => entry.passed),
  checks
};
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
console.log(JSON.stringify({
  schema: report.schema,
  passed: report.passed,
  checkCount: report.checkCount,
  passedCount: report.passedCount,
  failedCount: report.failedCount,
  outputPath
}, null, 2));
if (!report.passed) process.exitCode = 1;

function check(id, action) {
  try {
    action();
    checks.push({ id, passed: true, error: "" });
  } catch (error) {
    checks.push({ id, passed: false, error: error.message || String(error) });
  }
}

function requireNode(id) {
  const node = descendants(documentNode).find((entry) => entry.attributes.Id === id);
  assert.ok(node, `required widget ${id} is missing`);
  return node;
}

function parentWidgetById(id) {
  let parent = requireNode(id).parent;
  while (parent?.tag === "Children") parent = parent.parent;
  assert.ok(parent, `${id} has no containing widget`);
  return parent;
}

function descendants(node) {
  return [node, ...node.children.flatMap(descendants)];
}

function fixedSize(node) {
  assert.equal(node.attributes.WidthSizePolicy, "Fixed");
  assert.equal(node.attributes.HeightSizePolicy, "Fixed");
  const width = Number(node.attributes.SuggestedWidth);
  const height = Number(node.attributes.SuggestedHeight);
  assert.ok(Number.isFinite(width) && width > 0);
  assert.ok(Number.isFinite(height) && height > 0);
  return { width, height };
}

function fixedRect(node) {
  return {
    x: Number(node.attributes.MarginLeft || 0),
    y: Number(node.attributes.MarginTop || 0),
    ...fixedSize(node)
  };
}

function assertRectInside(rect, bounds, label) {
  assert.ok(rect.x >= 0 && rect.y >= 0, `${label} begins outside the panel`);
  assert.ok(rect.x + rect.width <= bounds.width, `${label} extends beyond the panel width`);
  assert.ok(rect.y + rect.height <= bounds.height, `${label} extends beyond the panel height`);
}

function parseColor(value) {
  const match = /^#(?<r>[0-9a-f]{2})(?<g>[0-9a-f]{2})(?<b>[0-9a-f]{2})(?<a>[0-9a-f]{2})$/i.exec(value);
  assert.ok(match, `expected #RRGGBBAA color, found '${value || "missing"}'`);
  return Object.fromEntries(Object.entries(match.groups).map(([key, hex]) => [key, Number.parseInt(hex, 16)]));
}

function parseXml(source) {
  const root = { tag: "#document", attributes: {}, children: [], parent: null };
  const stack = [root];
  const pattern = /<!--[\s\S]*?-->|<\?[^>]*\?>|<\/?[A-Za-z_][A-Za-z0-9_.:-]*(?:\s+(?:"[^"]*"|'[^']*'|[^>"'])*)?\s*\/?>/g;
  for (const match of source.matchAll(pattern)) {
    const token = match[0];
    if (token.startsWith("<!--") || token.startsWith("<?")) continue;
    const closing = /^<\//.test(token);
    const tag = /^<\/?\s*([A-Za-z_][A-Za-z0-9_.:-]*)/.exec(token)?.[1] || "";
    if (closing) {
      assert.equal(stack.at(-1)?.tag, tag, `mismatched closing tag ${tag}`);
      stack.pop();
      continue;
    }
    const node = {
      tag,
      attributes: parseAttributes(token),
      children: [],
      parent: stack.at(-1),
      offset: match.index || 0
    };
    stack.at(-1).children.push(node);
    if (!/\/\s*>$/.test(token)) stack.push(node);
  }
  assert.equal(stack.length, 1, `unclosed XML element ${stack.at(-1)?.tag}`);
  return root;
}

function parseAttributes(token) {
  const attributes = {};
  const pattern = /([A-Za-z_][A-Za-z0-9_.:-]*)\s*=\s*("([^"]*)"|'([^']*)')/g;
  for (const match of token.matchAll(pattern)) attributes[match[1]] = match[3] ?? match[4] ?? "";
  return attributes;
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : "";
}
