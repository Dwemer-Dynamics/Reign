import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import { getPreviewFixtureInventory, getSampleDataForPrefab } from "./js/sample-data.js";
import { CalibrationEditor } from "./js/calibration-editor.js";
import { buildCodexTurnPrompt } from "./js/codex-chat.js";
import { discoverPrefabExtensions } from "./js/native-augmentations.js";
import { auditRuntimeFontAssets, liveFontAlias } from "./audit-typography-parity.mjs";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const moduleRoot = path.resolve(toolRoot, "../..");
const workspaceRoot = path.resolve(moduleRoot, "..");
const prefabRoot = path.join(moduleRoot, "GUI", "Prefabs");
const sourceRoot = path.join(moduleRoot, "src");
const catalogPath = path.join(toolRoot, "calibration", "ui-catalog.json");
const modernStyleContractPath = path.join(moduleRoot, "GUI", "UiCalibration", "modern-style-contract.json");
const palettePath = path.join(moduleRoot, "artwork", "ui-modern-style-kit", "palette.json");
const outputArgument = process.argv.indexOf("--output");
const outputPath = outputArgument >= 0 && process.argv[outputArgument + 1]
  ? path.resolve(process.argv[outputArgument + 1])
  : "";

const prefabFiles = fs.readdirSync(prefabRoot)
  .filter((name) => name.toLowerCase().endsWith(".xml"))
  .sort((left, right) => left.localeCompare(right));
const fixtureInventory = getPreviewFixtureInventory();
const fixtureFiles = fixtureInventory.map((entry) => entry.fileName).sort((left, right) => left.localeCompare(right));
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const modernStyleContract = JSON.parse(fs.readFileSync(modernStyleContractPath, "utf8"));
const modernPalette = JSON.parse(fs.readFileSync(palettePath, "utf8"));
const catalogFiles = [...new Set(catalog.interfaces.map((entry) => path.basename(entry.prefab)))].sort((left, right) => left.localeCompare(right));
const runtimeCatalogMovies = [...new Set(catalog.interfaces.filter((entry) => !entry.supportUi).map((entry) => entry.movie))]
  .sort((left, right) => left.localeCompare(right));
const sourceFiles = walkFiles(sourceRoot).filter((file) => file.endsWith(".cs"));
const sourceDocuments = sourceFiles.map((file) => ({ file, text: fs.readFileSync(file, "utf8") }));
const classIndex = buildClassIndex(sourceDocuments);
const runtimeMovieInventory = discoverRuntimeMovies(sourceDocuments);
const errors = [];
const warnings = [];
const interfaceReports = [];
const intentionalEmptyScopes = new Map([
  ["ReignCourtScreen.xml:DailyAgenda", "The current Court view has no player-facing daily docket items; the empty list is the production empty state."],
  ["ReignSpymasterScreen.xml:SocialItems", "The default Spymaster social archive is intentionally empty until an operation produces social intelligence."],
  ["ReignSpymasterScreen.xml:Reports", "The default Spymaster report archive is intentionally empty until an operation produces a report."]
]);

compareSets("fixture inventory", prefabFiles, fixtureFiles, errors);
compareSets("UI catalog", prefabFiles, catalogFiles, errors);
compareSets("runtime movie catalog", runtimeMovieInventory.movies, runtimeCatalogMovies, errors);
runtimeMovieInventory.unresolved.forEach((message) => errors.push(`runtime movie discovery: ${message}`));
const editingContract = auditEditingContract();
editingContract.errors.forEach((message) => errors.push(`preview editor: ${message}`));
const toolContract = auditToolContract();
toolContract.errors.forEach((message) => errors.push(`preview tooling: ${message}`));
const nativeCalibrationContract = auditNativeCalibrationContract();
nativeCalibrationContract.errors.forEach((message) => errors.push(`native calibration: ${message}`));
const calibrationOverlayInputContract = auditCalibrationOverlayInputContract();
calibrationOverlayInputContract.errors.forEach((message) => errors.push(`calibration overlay input: ${message}`));
const standaloneEditableInputContract = auditStandaloneEditableInputContract();
standaloneEditableInputContract.errors.forEach((message) => errors.push(`editable input: ${message}`));
const castleLayoutInteractionContract = auditCastleLayoutInteractionContract();
castleLayoutInteractionContract.errors.forEach((message) => errors.push(`castle layout interaction: ${message}`));
const nativeAugmentationContract = auditNativeAugmentationContract();
nativeAugmentationContract.errors.forEach((message) => errors.push(`native augmentation: ${message}`));
const fontAssetContract = auditRuntimeFontAssets({ moduleRoot });
fontAssetContract.errors.forEach((issue) => errors.push(`runtime font ${issue.code}: ${issue.message}`));
const typographyContract = auditTypographyContract();
typographyContract.errors.forEach((message) => errors.push(`typography: ${message}`));
const visualStyleContract = auditVisualStyleContract();
visualStyleContract.errors.forEach((message) => errors.push(`visual style: ${message}`));

for (const fileName of prefabFiles) {
  const xml = fs.readFileSync(path.join(prefabRoot, fileName), "utf8");
  const documentNode = parseXml(xml);
  const fixture = getSampleDataForPrefab(fileName);
  const report = {
    fileName,
    rootFixtureKeys: Object.keys(fixture).filter((key) => !key.startsWith("$")),
    checkedBindings: 0,
    checkedDataSources: 0,
    emptyDataSources: [],
    intentionalEmptyStates: [],
    runtimeContract: null,
    errors: []
  };
  for (const child of documentNode.children) auditNode(child, fixture, xml, report, `/${child.tag}[1]`);
  report.runtimeContract = auditRuntimeContract(fileName, documentNode, xml);
  report.runtimeContract.errors.forEach((message) => report.errors.push(`runtime contract: ${message}`));
  if (fileName === "ReignRoyalCouncilScreen.xml" && JSON.stringify(fixture).includes("A Pact Proclaimed Before the Lords of Calradia")) {
    report.errors.push("Royal Council fixture contains diplomacy-announcement title text.");
  }
  report.errors.forEach((message) => errors.push(`${fileName}: ${message}`));
  report.emptyDataSources.forEach((scope) => {
    const key = `${fileName}:${scope}`;
    const rationale = intentionalEmptyScopes.get(key);
    if (rationale) report.intentionalEmptyStates.push({ dataSource: scope, rationale });
    else warnings.push(`${fileName}: ${scope} has no default sample item and no audited empty-state rationale.`);
  });
  interfaceReports.push(report);
}

const result = {
  schema: "reign-ui-preview-contract-audit-v1",
  generatedUtc: new Date().toISOString(),
  workspaceRoot,
  prefabRoot,
  catalogPath,
  counts: {
    prefabFiles: prefabFiles.length,
    catalogedPrefabFiles: catalogFiles.length,
    fixtureFiles: fixtureFiles.length,
    checkedBindings: interfaceReports.reduce((sum, report) => sum + report.checkedBindings, 0),
    checkedDataSources: interfaceReports.reduce((sum, report) => sum + report.checkedDataSources, 0),
    checkedRuntimeBindings: interfaceReports.reduce((sum, report) => sum + (report.runtimeContract?.checkedBindings || 0), 0),
    checkedRuntimeCommands: interfaceReports.reduce((sum, report) => sum + (report.runtimeContract?.checkedCommands || 0), 0),
    checkedEditingContracts: editingContract.checked,
    checkedToolContracts: toolContract.checked,
    checkedNativeCalibrationTargets: nativeCalibrationContract.checkedTargets,
    checkedCalibrationOverlayInputTargets: calibrationOverlayInputContract.checkedTargets,
    checkedStandaloneEditableInputPrefabs: standaloneEditableInputContract.checkedTargets,
    checkedCastleLayoutSlotLists: castleLayoutInteractionContract.checkedTargets,
    checkedNativeAugmentationTargets: nativeAugmentationContract.checkedTargets,
    checkedNativePatchApplications: nativeAugmentationContract.checkedApplications,
    checkedRuntimeFontFiles: fontAssetContract.checkedFileCount,
    checkedTypographyWidgets: typographyContract.checkedWidgets,
    checkedVisualColorAttributes: visualStyleContract.checkedColorAttributes,
    checkedVisualPatchColors: visualStyleContract.checkedPatchColors,
    checkedLegacyBrushAttributes: visualStyleContract.checkedBrushAttributes,
    discoveredRuntimeMovies: runtimeMovieInventory.movies.length,
    intentionalEmptyStates: interfaceReports.reduce((sum, report) => sum + report.intentionalEmptyStates.length, 0),
    errors: errors.length,
    warnings: warnings.length
  },
  errors,
  warnings,
  editingContract,
  toolContract,
  nativeCalibrationContract,
  calibrationOverlayInputContract,
  standaloneEditableInputContract,
  castleLayoutInteractionContract,
  nativeAugmentationContract,
  fontAssetContract,
  typographyContract,
  visualStyleContract,
  runtimeMovieInventory,
  interfaces: interfaceReports
};

if (outputPath) {
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
}
process.stdout.write(`${JSON.stringify({
  schema: result.schema,
  generatedUtc: result.generatedUtc,
  counts: result.counts,
  errors,
  warnings,
  outputPath: outputPath || null
}, null, 2)}\n`);
if (errors.length) process.exitCode = 1;

function discoverRuntimeMovies(documents) {
  const stringConstants = new Map();
  for (const document of documents) {
    const constantPattern = /\b(?:const|static\s+readonly)\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"([A-Za-z][A-Za-z0-9_-]+)"/g;
    for (const match of document.text.matchAll(constantPattern)) stringConstants.set(match[1], match[2]);
  }

  const movies = new Set();
  const calls = [];
  const unresolved = [];
  for (const document of documents) {
    const callPattern = /\bLoadMovie\s*\(([^;\r\n]+)/g;
    for (const match of document.text.matchAll(callPattern)) {
      const firstArgument = match[1].split(",", 1)[0].trim();
      const literals = [...firstArgument.matchAll(/"([A-Za-z][A-Za-z0-9_-]+)"/g)].map((item) => item[1]);
      const resolved = literals.length
        ? literals
        : [...firstArgument.matchAll(/\b([A-Za-z_][A-Za-z0-9_]*)\b/g)].map((item) => stringConstants.get(item[1])).filter(Boolean);
      const line = document.text.slice(0, match.index).split("\n").length;
      if (!resolved.length) {
        unresolved.push(`${path.relative(workspaceRoot, document.file)}:${line} has an unresolved LoadMovie argument '${firstArgument}'.`);
        continue;
      }
      resolved.forEach((movie) => movies.add(movie));
      calls.push({ file: path.relative(workspaceRoot, document.file), line, argument: firstArgument, movies: [...new Set(resolved)] });
    }
  }
  return {
    movies: [...movies].sort((left, right) => left.localeCompare(right)),
    calls,
    unresolved
  };
}

function auditNode(node, inheritedContext, xml, report, xmlPath) {
  const dataSourceExpression = node.attributes.DataSource || "";
  const dataSourceName = directBinding(dataSourceExpression);
  let scopedContext = inheritedContext;
  if (dataSourceExpression) {
    report.checkedDataSources += 1;
    if (!dataSourceName || !hasOwn(inheritedContext, dataSourceName)) {
      report.errors.push(`${location(xml, node.offset)} ${xmlPath} DataSource ${dataSourceExpression} is absent from its parent fixture context.`);
    } else if (node.tag !== "ListPanel") {
      const value = inheritedContext[dataSourceName];
      if (!value || typeof value !== "object" || Array.isArray(value)) {
        report.errors.push(`${location(xml, node.offset)} ${xmlPath} DataSource ${dataSourceExpression} must resolve to an object.`);
      } else {
        scopedContext = value;
      }
    }
  }

  auditAttributes(node, scopedContext, xml, report, xmlPath);

  if (node.tag === "ListPanel" && dataSourceName && hasOwn(inheritedContext, dataSourceName)) {
    const items = inheritedContext[dataSourceName];
    if (!Array.isArray(items)) {
      report.errors.push(`${location(xml, node.offset)} ${xmlPath} DataSource ${dataSourceExpression} must resolve to an array.`);
    } else {
      const template = node.children.find((child) => child.tag === "ItemTemplate");
      const ordinaryChildren = node.children.filter((child) => child !== template);
      ordinaryChildren.forEach((child, index) => auditNode(child, inheritedContext, xml, report, `${xmlPath}/${child.tag}[${index + 1}]`));
      if (template) {
        if (!items.length) {
          report.emptyDataSources.push(dataSourceName);
        } else {
          template.children.forEach((child, index) => auditNode(child, items[0], xml, report, `${xmlPath}/ItemTemplate[1]/${child.tag}[${index + 1}]`));
        }
      }
      return;
    }
  }

  const counts = new Map();
  for (const child of node.children) {
    const index = (counts.get(child.tag) || 0) + 1;
    counts.set(child.tag, index);
    auditNode(child, scopedContext, xml, report, `${xmlPath}/${child.tag}[${index}]`);
  }
}

function auditAttributes(node, context, xml, report, xmlPath) {
  for (const [attribute, expression] of Object.entries(node.attributes)) {
    if (attribute === "DataSource") continue;
    for (const binding of extractBindingNames(expression)) {
      report.checkedBindings += 1;
      if (!hasOwn(context, binding)) {
        report.errors.push(`${location(xml, node.offset)} ${xmlPath} ${attribute} references ${binding}, absent from the active fixture context.`);
      }
    }
  }
}

function auditRuntimeContract(fileName, documentNode, xml) {
  const entries = catalog.interfaces.filter((entry) => path.basename(entry.prefab) === fileName);
  const primary = entries[0];
  const report = {
    catalogIds: entries.map((entry) => entry.id),
    movie: primary?.movie || "",
    manager: primary?.manager || "",
    viewModel: primary?.viewModel || "",
    checkedBindings: 0,
    checkedCommands: 0,
    unresolvedTypes: [],
    errors: []
  };
  if (!primary) {
    report.errors.push("No UI catalog entry supplies a runtime movie, manager, and view-model contract.");
    return report;
  }

  const manager = classIndex.get(primary.manager);
  const viewModel = classIndex.get(primary.viewModel);
  if (primary.supportUi) {
    if (primary.nativeInjection !== false) report.errors.push("Previewer support UI must explicitly declare nativeInjection=false.");
    if (primary.liveAction !== "previewer only") report.errors.push("Previewer support UI must explicitly declare liveAction=previewer only.");
  } else if (!manager) report.errors.push(`Manager ${primary.manager} is absent from Reign client source.`);
  else {
    if (!manager.fullText.includes("LoadMovie")) report.errors.push(`Manager ${primary.manager} does not load a Gauntlet movie.`);
    if (!manager.fullText.includes(`\"${primary.movie}\"`)) report.errors.push(`Manager ${primary.manager} does not reference movie ${primary.movie}.`);
    if (!manager.fullText.includes(primary.viewModel)) report.errors.push(`Manager ${primary.manager} does not reference view model ${primary.viewModel}.`);
  }
  if (!viewModel) {
    report.errors.push(`View model ${primary.viewModel} is absent from Reign client source.`);
    return report;
  }

  for (const child of documentNode.children) auditRuntimeNode(child, primary.viewModel, report, xml, `/${child.tag}[1]`);
  report.alternateViewModels = [];
  for (const name of primary.alternateViewModels || []) {
    report.alternateViewModels.push(name);
    if (!classIndex.has(name)) { report.errors.push(`Alternate view model ${name} is absent from Reign client source.`); continue; }
    if (!manager?.fullText.includes(name)) report.errors.push(`Manager ${primary.manager} does not reference alternate view model ${name}.`);
    for (const child of documentNode.children) auditRuntimeNode(child, name, report, xml, `/${child.tag}[1]`);
  }
  report.unresolvedTypes = [...new Set(report.unresolvedTypes)].sort((left, right) => left.localeCompare(right));
  return report;
}

function auditEditingContract() {
  const errors = [];
  const button = fakeRenderedElement("ButtonWidget", { WidthSizePolicy: "Fixed", HeightSizePolicy: "Fixed" });
  const stretchedLabel = fakeRenderedElement("TextWidget", { WidthSizePolicy: "StretchToParent", HeightSizePolicy: "StretchToParent", DoNotAcceptEvents: "true" }, button);
  const stretchedDecoration = fakeRenderedElement("Widget", { WidthSizePolicy: "StretchToParent", HeightSizePolicy: "StretchToParent", DoNotAcceptEvents: "true" }, button);
  const ordinaryFixedText = fakeRenderedElement("TextWidget", { WidthSizePolicy: "Fixed", HeightSizePolicy: "Fixed" }, button);
  const stretchedInteractiveWidget = fakeRenderedElement("Widget", { WidthSizePolicy: "StretchToParent", HeightSizePolicy: "StretchToParent" }, button);
  const target = (selected, alt = false) => CalibrationEditor.prototype.getMovementTarget.call({ selectedElement: selected }, alt);
  if (target(stretchedLabel) !== button) errors.push("Stretch-to-parent TextWidget movement does not promote to its owning button.");
  if (target(stretchedDecoration) !== button) errors.push("Event-transparent stretch child movement does not promote to its owning button.");
  if (target(stretchedLabel, true) !== stretchedLabel) errors.push("Alt movement does not preserve exact-child targeting.");
  if (target(ordinaryFixedText) !== ordinaryFixedText) errors.push("Fixed TextWidget movement is incorrectly promoted to a parent.");
  if (target(stretchedInteractiveWidget) !== stretchedInteractiveWidget) errors.push("Interactive stretch Widget movement is incorrectly promoted to a parent.");
  const resize = (attributes, handle, dx, dy, width = 100, height = 80) => {
    const changes = {};
    CalibrationEditor.prototype.applyResizeChanges(changes, attributes, handle, dx, dy, width, height);
    return changes;
  };
  const fixedEast = resize({ WidthSizePolicy: "Fixed", HeightSizePolicy: "Fixed" }, "e", 20, 0);
  if (fixedEast.SuggestedWidth !== 120) errors.push("Fixed east-edge resize does not change SuggestedWidth predictably.");
  const fixedWest = resize({ WidthSizePolicy: "Fixed", HeightSizePolicy: "Fixed", PositionXOffset: "4" }, "w", 10, 0);
  if (fixedWest.SuggestedWidth !== 90 || fixedWest.PositionXOffset !== 14) errors.push("Fixed west-edge resize does not preserve the opposite edge.");
  const stretchWest = resize({ WidthSizePolicy: "StretchToParent", HeightSizePolicy: "Fixed", MarginLeft: "5" }, "w", 15, 0);
  if (stretchWest.MarginLeft !== 20 || "SuggestedWidth" in stretchWest) errors.push("Stretch west-edge resize does not edit MarginLeft.");
  const stretchEast = resize({ WidthSizePolicy: "StretchToParent", HeightSizePolicy: "Fixed", MarginRight: "5" }, "e", 15, 0);
  if (stretchEast.MarginRight !== -10 || "SuggestedWidth" in stretchEast) errors.push("Stretch east-edge resize does not edit MarginRight.");
  const coverSouthEast = resize({ WidthSizePolicy: "CoverChildren", HeightSizePolicy: "CoverChildren" }, "se", 12, 8);
  if (coverSouthEast.WidthSizePolicy !== "Fixed" || coverSouthEast.HeightSizePolicy !== "Fixed" || coverSouthEast.SuggestedWidth !== 112 || coverSouthEast.SuggestedHeight !== 88) {
    errors.push("CoverChildren resize does not convert both affected axes to fixed dimensions.");
  }
  const stretchNorth = resize({ WidthSizePolicy: "Fixed", HeightSizePolicy: "StretchToParent", MarginTop: "20" }, "n", 0, -10);
  if (stretchNorth.MarginTop !== 10 || "SuggestedHeight" in stretchNorth) errors.push("Stretch north-edge resize does not edit MarginTop.");
  const fixedNorth = resize({ WidthSizePolicy: "Fixed", HeightSizePolicy: "Fixed", PositionYOffset: "2" }, "n", 0, -10, 100, 80);
  if (fixedNorth.SuggestedHeight !== 90 || fixedNorth.PositionYOffset !== -8) errors.push("Fixed north-edge resize does not preserve the opposite edge.");
  return { checked: 12, errors };
}

function auditToolContract() {
  const checks = [
    [path.join(toolRoot, "index.html"), 'id="nativeDataToggle"', "Captured game-state content does not have a separate opt-in control."],
    [path.join(toolRoot, "index.html"), 'id="prefabInstallSummary"', "The previewer does not expose the all-prefab source/install summary."],
    [path.join(toolRoot, "index.html"), 'class="codex-dock"', "The bottom Codex editing dock is absent."],
    [path.join(toolRoot, "index.html"), 'id="augmentationGrid"', "The previewer does not expose Reign additions to native Bannerlord prefabs."],
    [path.join(toolRoot, "index.html"), 'data-interface="wilderness-event"', "Runtime variants do not have stable catalog identities in the previewer."],
    [path.join(toolRoot, "index.html"), 'id="nativeParityStatus"', "Every-interface parity readiness is not visible in the previewer."],
    [path.join(toolRoot, "index.html"), 'value="custom-scale-locked"', "The previewer cannot explicitly inspect the native player-UI-scale lock."],
    [path.join(toolRoot, "js", "app.js"), "elements.nativeDataToggle.checked ? currentNativeEvidence?.snapshot : null", "Captured runtime content is not explicitly gated from coherent fixtures."],
    [path.join(toolRoot, "js", "app.js"), 'get("codex") === "off"', "Provider-free browser evidence cannot disable Codex task creation."],
    [path.join(toolRoot, "js", "app.js"), 'get("interface")', "Catalog interface states cannot be opened deterministically by id."],
    [path.join(toolRoot, "js", "app.js"), 'fetch("api/native-parity-readiness"', "The previewer cannot recompute native parity readiness."],
    [path.join(toolRoot, "js", "app.js"), "loadNativeAugmentation(entry)", "Native augmentation catalog entries cannot be composed in the previewer."],
    [path.join(toolRoot, "js", "app.js"), "prefabIgnoresCustomScale(currentXmlText)", "Auto scaling does not derive the player-UI-scale lock from current XML source."],
    [path.join(toolRoot, "js", "app.js"), 'runtimeProfile === "custom-scale-locked"', "The previewer does not ignore player UI scale for locked native canvases."],
    [path.join(toolRoot, "js", "native-augmentations.js"), "composeNativePrefab", "The source-discovered native prefab composer is absent."],
    [path.join(toolRoot, "js", "native-augmentations.js"), "ReignPreviewResolvedValue", "Native brush-derived constants are not applied to composed previews."],
    [path.join(toolRoot, "serve-preview.ps1"), "/api/prefab-sync-all", "The server does not expose an all-prefab source/install summary."],
    [path.join(toolRoot, "serve-preview.ps1"), "/api/native-parity-readiness", "The server does not expose current fail-closed parity readiness."],
    [path.join(toolRoot, "serve-preview.ps1"), "exactCaseMatch = [bool]($hasExactCaseRequest -and $snapshot)", "Native evidence is not selected by the exact preview resolution and configured UI-scale case."],
    [path.join(toolRoot, "serve-preview.ps1"), 'runtime-snapshot.json', "The previewer cannot load the exact matrix-case runtime snapshot."],
    [path.join(toolRoot, "serve-preview.ps1"), "/api/native-prefab", "The server does not expose allowlisted installed native base prefabs."],
    [path.join(toolRoot, "serve-preview.ps1"), "Get-ReignNativePrefabConstants", "Native brush and sprite dimensions are not resolved for composed previews."],
    [path.join(toolRoot, "serve-preview.ps1"), "ProcessName -like 'Bannerlord*'", "Bannerlord process detection does not cover launcher/process variants."],
    [path.join(toolRoot, "serve-preview.ps1"), "screenReloadRequired = $bannerlordRunning", "Live prefab installation does not report that the affected Gauntlet movie must be closed and reopened."],
    [path.join(toolRoot, "serve-preview.ps1"), '".codex-build\\ui-preview\\calibration-backups"', "Generated calibration backups are not confined to the workspace build/evidence root."],
    [path.join(toolRoot, "js", "calibration-editor.js"), "live install available; reopen the affected interface afterward", "The previewer does not expose the supported live-install/reopen workflow."],
    [path.join(toolRoot, "serve-preview.ps1"), "replacementRollbackPath", "Installed prefab replacement does not provide the non-null rollback file required by Windows File.Replace."],
    [path.join(toolRoot, "serve-preview.ps1"), "snapshotMatchesSource", "Native evidence does not reject snapshots from a different prefab revision."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), '_loadedPrefabSha256 = PrefabSha256(movieName)', "Native calibration does not retain the prefab hash at movie-load time."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'snapshot.Add("prefabSha256", _loadedPrefabSha256', "Native snapshots do not attribute geometry to the prefab revision actually loaded by Gauntlet."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'snapshot.Add("installedPrefabSha256AtCapture", PrefabSha256(MovieName))', "Native snapshots cannot diagnose a prefab installed after the current movie was loaded."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'supportUiInjected = false', "Native snapshots do not attest that the previewer-only calibration surface was absent from Bannerlord."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "reign-ui-rendered-preview-audit-v1", "The provider-free dual-resolution render audit is absent."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "interfaces.filter((entry) => !entry.supportUi)", "The rendered readiness assertion still counts the previewer-only calibration asset as a native surface."],
    [path.join(toolRoot, "audit-typography-parity.mjs"), 'fixed-literal', "Typography parity does not classify and freeze remaining runtime literals."],
    [path.join(toolRoot, "audit-typography-parity.mjs"), 'dynamic-bound', "Typography parity does not limit content exemptions to runtime-bound Text values."],
    [path.join(toolRoot, "audit-typography-parity.mjs"), 'fixed-text-mismatch', "Typography parity does not report fixed-label wording or capitalization drift."],
    [path.join(toolRoot, "audit-typography-parity.mjs"), 'required-attribute-value-mismatch', "Typography parity does not reject an explicit but wrong native font face."],
    [path.join(toolRoot, "audit-typography-parity.mjs"), 'forbidden-synthetic-text-effect', "Typography parity does not reject active synthetic text effects."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'fixed-literal-capitalization-divergence', "Typography parity self-tests do not prove runtime-literal capitalization drift is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'missing-explicit-font-face-divergence', "Typography parity self-tests do not prove a missing explicit live font is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'wrong-explicit-font-face-divergence', "Typography parity self-tests do not prove a wrong explicit face is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'missing-runtime-font-atlas-divergence', "Typography parity self-tests do not prove a missing packed font atlas is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'font-page-alias-divergence', "Typography parity self-tests do not prove a mismatched FNT page alias is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'missing-font-provenance-divergence', "Typography parity self-tests do not prove missing font provenance is rejected."],
    [path.join(toolRoot, "test-government-scroll-and-native-thumbnail-contract.mjs"), 'government scroll and native thumbnail contract: PASS', "Government snap scrolling and native square-thumbnail regression coverage is absent."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'missing-font-license-divergence', "Typography parity self-tests do not prove a missing font license is rejected."],
    [path.join(toolRoot, "test-audit-typography-parity.mjs"), 'synthetic-outline-divergence', "Typography parity self-tests do not prove an active synthetic outline is rejected."],
    [modernStyleContractPath, `"requiredExplicitFontFace": "${liveFontAlias}"`, `Modern style authority does not bind every live text widget to the packaged ${liveFontAlias} serif face.`],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "catalog.interfaces", "The rendered audit deduplicates XML files instead of exercising every cataloged interface state."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "Provider-free capture did not disable recursive native-readiness refresh", "The rendered audit does not enforce bounded parity refresh behavior."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "Ordinary preview did not hydrate every-interface parity readiness", "The rendered audit does not prove the ordinary parity card hydrates."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "reign-ui-direct-manipulation-audit-v1", "The real browser click, drag, resize, undo, and approval-boundary audit is absent."],
    [path.join(toolRoot, "audit-rendered-preview.mjs"), "reign-ui-custom-scale-lock-audit-v1", "The rendered audit does not prove XML-derived player-UI-scale locking and explicit override behavior."],
    [path.join(toolRoot, "styles.css"), "min-height: 11px", "Calibration resize handles can inherit global button sizing and cover the move surface."],
    [path.join(toolRoot, "audit-codex-chat-e2e.mjs"), "reign-ui-codex-chat-e2e-v1", "The selected-widget Codex chat end-to-end audit is absent."],
    [path.join(toolRoot, "audit-native-parity.mjs"), "reign-ui-native-parity-readiness-v1", "The fail-closed every-interface native parity readiness audit is absent."],
    [path.join(toolRoot, "audit-native-parity.mjs"), "reign-ui-window-capture-v1", "Native acceptance is not bound to a verified foreground Bannerlord capture receipt."],
    [path.join(toolRoot, "audit-native-parity.mjs"), 'surfaceType !== "native-augmentation"', "Native parity does not distinguish standalone runtime snapshots from screenshot-authoritative native augmentations."],
    [path.join(toolRoot, "audit-native-parity.mjs"), 'native visual review criterion not accepted', "Native parity does not fail closed when a fixed-art or typography review criterion is false."],
    [path.join(toolRoot, "audit-native-parity.mjs"), 'unknown native visual review criterion', "Native parity silently accepts unknown visual-review criteria."],
    [path.join(toolRoot, "calibration_workflow.py"), 'accept-native-case', "The reviewed native-evidence recorder is absent."],
    [path.join(toolRoot, "calibration_workflow.py"), 'reign-ui-native-contact-sheet-v1', "Native capture batches cannot produce a bounded visual review sheet."],
    [path.join(toolRoot, "calibration_workflow.py"), 'reign-ui-native-visual-review-attestation-v1', "Reviewed native cases are not bound to a versioned fixed-art and typography attestation."],
    [path.join(toolRoot, "calibration_workflow.py"), 'I confirm this native capture matches the approved Reign reference for all fixed visuals and typography', "Native evidence promotion does not require the exact fixed-art and typography confirmation."],
    [path.join(toolRoot, "calibration_workflow.py"), 'nativeAcceptanceRequired=not previewer_only', "The calibration inventory does not distinguish previewer-only support assets from native acceptance surfaces."],
    [path.join(toolRoot, "calibration_workflow.py"), 'Previewer-only support assets are not native acceptance surfaces.', "The calibration dashboard still claims browser-only support assets require native evidence."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), 'reign-ui-native-capture-batch-v1', "The reusable standalone native matrix capture runner is absent."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), 'requiresHumanReview = $true', "The native matrix runner can silently promote unreviewed captures."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), '$configuredUiScale = 0.0', "The native matrix runner does not distinguish Bannerlord's configured UIScale from Gauntlet runtime scaling."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), '$bridgeArmMinutes = 240', "The standalone native matrix runner does not request a fresh bounded 240-minute live-test bridge arm after campaign start."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), "Invoke-LiveTest -Arguments @('arm', '--minutes', [string]$bridgeArmMinutes, '--json')", "The standalone native matrix runner still relies on external or stale bridge arming instead of obtaining its own arm receipt."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), '$bridgeArmReceipt.ok -ne $true -or $bridgeArmReceipt.armed -ne $true', "The standalone native matrix runner does not fail closed when its fresh arm receipt is invalid."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'session.SaveSnapshot(false, out error)', "The live-test snapshot route does not retain its headless target-widget snapshot seam."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'supportUiInjected = false', "Native runtime snapshots do not explicitly attest that visual support UI was not injected."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), 'supportUiInjected', "The standalone native matrix runner does not fail closed on an injected calibration support surface."],
    [path.join(toolRoot, "calibration_workflow.py"), '"runtimeUiScale":runtime_ui_scale', "Native acceptance does not retain the snapshot's runtime UIContext.CustomScale."],
    [path.join(toolRoot, "audit-native-parity.mjs"), 'runtime snapshot UIContext.CustomScale is invalid', "Native parity does not validate the recorded runtime Gauntlet scale."],
    [path.join(moduleRoot, "src", "Modules", "Court", "UI", "ViewModels", "ReignRoyalCouncilScreenVM.cs"), 'PopulateCalibrationBriefings', "Royal Council native calibration has no non-persistent full-transcript fixture."],
    [path.join(moduleRoot, "src", "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionUiCalibrationHost.cs"), 'OpenForCalibration(court)', "Royal Council ui-open still enters the production provider-backed briefing path."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), 'Royal Council calibration made', "The native matrix runner does not fail closed on Royal Council calibration provider calls."],
    [path.join(toolRoot, "capture-window.ps1"), 'AttachThreadInput', "Verified native capture cannot reliably acquire a target when another desktop window owns foreground input."],
    [path.join(toolRoot, "capture-window.ps1"), 'SetWindowPos', "Native capture cannot resize an engine-rendered client beyond the physical desktop."],
    [path.join(toolRoot, "capture-window.ps1"), 'PrintWindow', "Native capture cannot retain an exact off-screen full-content client image."],
    [path.join(toolRoot, "capture-window.ps1"), 'SetThreadDpiAwarenessContext', "Native capture reads DPI-virtualized client dimensions instead of exact physical pixels."],
    [path.join(toolRoot, "capture-window.ps1"), 'virtualizedClientBounds', "Off-screen capture does not retain the OS-reported client-size discrepancy for review."],
    [path.join(toolRoot, "capture-window.ps1"), 'verified-foreground-steam-backbuffer', "Oversized native capture cannot use the exact Steam DirectX backbuffer screenshot."],
    [path.join(toolRoot, "capture-window.ps1"), 'Steam backbuffer screenshot is', "Steam backbuffer capture does not fail closed on wrong pixel dimensions."],
    [path.join(toolRoot, "capture-window.ps1"), "ChangeExtension($resolved, '.steam-backbuffer.jpg')", "Steam backbuffer evidence is not retained beside the exact native PNG."],
    [path.join(toolRoot, "capture-window.ps1"), 'Retained Steam backbuffer hash mismatch', "Steam backbuffer cleanup is not gated by an exact retained-source hash check."],
    [path.join(toolRoot, "capture-window.ps1"), 'Remove-Item -LiteralPath $steamOriginalPath -Force', "The exact run-created Steam screenshot is not cleaned up after durable evidence retention."],
    [path.join(toolRoot, "capture-window.ps1"), 'effectively black Bannerlord client image', "Off-screen native capture does not reject blank DirectX output."],
    [path.join(toolRoot, "capture-native-matrix.ps1"), '$acceptanceRoot = $output', "The native batch writes reviewed evidence outside the root consumed by the parity audit."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), 'reign-ui-native-display-matrix-v1', "The safe catalog-wide native display-matrix orchestrator is absent."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), 'originalBannerlordConfigBytes', "The display-matrix orchestrator does not retain a byte-for-byte Bannerlord configuration backup."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), 'originalEngineConfigBytes', "The display-matrix orchestrator does not retain a byte-for-byte engine configuration backup."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), "Set-SingleConfigValue -Path $bannerlordConfig -Key 'UIScale'", "The display-matrix orchestrator does not configure the exact catalog UI scale."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), "Set-SingleConfigValue -Path $engineConfig -Key 'display_width'", "The display-matrix orchestrator does not configure the exact catalog width."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), "Set-SingleConfigValue -Path $engineConfig -Key 'display_height'", "The display-matrix orchestrator does not configure the exact catalog height."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), "Set-SingleConfigValue -Path $engineConfig -Key 'display_mode'", "The display-matrix orchestrator does not select an explicit validated capture display mode."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), '[System.IO.File]::WriteAllBytes($bannerlordConfig, $originalBannerlordConfigBytes)', "The display-matrix orchestrator does not restore BannerlordConfig.txt from its exact backup."],
    [path.join(toolRoot, "capture-native-display-matrix.ps1"), '[System.IO.File]::WriteAllBytes($engineConfig, $originalEngineConfigBytes)', "The display-matrix orchestrator does not restore engine_config.txt from its exact backup."],
    [path.join(toolRoot, "capture-native-augmentation.ps1"), 'reign-ui-native-augmentation-capture-v1', "Native Bannerlord augmentation screens have no guarded capture helper."],
    [path.join(toolRoot, "capture-native-augmentation-batch.ps1"), '$bridgeArmMinutes = 240', "The native augmentation batch does not request a fresh bounded 240-minute live-test bridge arm after campaign start."],
    [path.join(toolRoot, "capture-native-augmentation-batch.ps1"), "Invoke-LiveTest -Arguments @('arm', '--minutes', [string]$bridgeArmMinutes, '--json')", "The native augmentation batch still relies on external or stale bridge arming instead of obtaining its own arm receipt."],
    [path.join(toolRoot, "capture-native-augmentation-batch.ps1"), '$bridgeArmReceipt.ok -ne $true -or $bridgeArmReceipt.armed -ne $true', "The native augmentation batch does not fail closed when its fresh arm receipt is invalid."],
    [path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), 'never be loaded into the live Gauntlet widget tree', "The native calibration service does not declare the previewer-only support-UI boundary."]
  ];
  const contractErrors = [];
  for (const [file, marker, message] of checks) {
    if (!fs.existsSync(file) || !fs.readFileSync(file, "utf8").includes(marker)) contractErrors.push(message);
  }
  const calibrationService = fs.readFileSync(path.join(moduleRoot, "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"), "utf8");
  const calibrationHost = fs.readFileSync(path.join(moduleRoot, "src", "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionUiCalibrationHost.cs"), "utf8");
  if (calibrationService.includes("layer.LoadMovie(OverlayMovieName")) {
    contractErrors.push("The previewer-only calibration overlay is still loaded into the native Bannerlord Gauntlet tree.");
  }
  if (calibrationHost.includes('target == "calibration-overlay"') || calibrationHost.includes('case "calibration-overlay"')) {
    contractErrors.push("The native live-test host still exposes calibration-overlay actions or target normalization.");
  }
  const previewServer = fs.readFileSync(path.join(toolRoot, "serve-preview.ps1"), "utf8");
  if (previewServer.includes("[IO.File]::Replace($temporaryPath, $paths.Installed, $null)")) {
    contractErrors.push("Installed prefab replacement still uses a null Windows File.Replace backup path and will fail before the game can receive the previewed source.");
  }
  if (previewServer.includes("if (Test-BannerlordRunning) { throw")) {
    contractErrors.push("Installed prefab replacement is still blocked while Bannerlord runs, preventing the intended edit-install-close-reopen workflow.");
  }
  const notablePopup = path.join(moduleRoot, "GUI", "SpriteParts", "ui_reignbeta_generation", "reign_notable_generation_popup.png");
  const cleanedPopupSha256 = "61234d4978beee9a0abd289433cfcb343646807b1a48d389122674ef0bc2c4a7";
  const actualPopupSha256 = fs.existsSync(notablePopup)
    ? createHash("sha256").update(fs.readFileSync(notablePopup)).digest("hex")
    : "";
  if (actualPopupSha256 !== cleanedPopupSha256) {
    contractErrors.push("The Notable Generation shell is not the audited text-free sprite; obsolete baked prose can overlap its dynamic XML fields.");
  }
  const promptContext = {
    fileName: "ReignRoyalCouncilScreen.xml",
    selection: { token: "reignxml://select?file=ReignRoyalCouncilScreen.xml&line=81", path: "/Prefab[1]/Window[1]/TextWidget[1]", bindings: [{ attribute: "Text", expression: "@Title", value: "ROYAL COUNCIL" }], renderedGeometry: { x: 40, y: 20, width: 300, height: 48 } },
    diagnostics: [{ kind: "clipping", message: "sample", path: "/Prefab[1]/Window[1]/TextWidget[1]", line: 81 }],
    pendingXmlChanges: "MarginTop: 4 -> 12",
    installSync: { catalogOwned: true, inSync: false }
  };
  const prompt = buildCodexTurnPrompt("Move the title down.", promptContext);
  for (const required of ["Move the title down.", promptContext.selection.token, promptContext.selection.path, "bindings", "renderedGeometry", "diagnostics", "pendingXmlChanges", "installSync", "Do not install or deploy it."]) {
    if (!prompt.includes(required)) contractErrors.push(`Codex turn prompt omits required selected-widget context '${required}'.`);
  }
  return { checked: checks.length + 11, errors: contractErrors, notablePopupSha256: actualPopupSha256 };
}

function auditNativeCalibrationContract() {
  const runtimeEntries = catalog.interfaces.filter((entry) => !entry.supportUi);
  const previewerSupportEntries = catalog.interfaces.filter((entry) => entry.supportUi);
  const declared = catalog.nativeCalibration || {};
  const hostPath = path.join(moduleRoot, "src", "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionUiCalibrationHost.cs");
  const liveTestPath = path.join(workspaceRoot, "ReignBetaServer", "ReignLiveTest", "Program.cs");
  const correspondenceManagerPath = path.join(sourceRoot, "Modules", "Dialogue", "UI", "ReignCorrespondenceScreenManager.cs");
  const individualManagerPath = path.join(sourceRoot, "Modules", "Dialogue", "UI", "ReignIndividualChatScreenManager.cs");
  const socialManagerPath = path.join(sourceRoot, "Modules", "WorldSimulation", "UI", "ReignSocialEventScreenManager.cs");
  const correspondenceVmPath = path.join(sourceRoot, "Modules", "Dialogue", "UI", "ViewModels", "ReignCorrespondenceScreenVM.cs");
  const individualVmPath = path.join(sourceRoot, "Modules", "Dialogue", "UI", "ViewModels", "ReignIndividualChatScreenVM.cs");
  const socialVmPath = path.join(sourceRoot, "Modules", "WorldSimulation", "UI", "ViewModels", "ReignSocialEventScreenVM.cs");
  const memoriesManagerPath = path.join(sourceRoot, "Modules", "Portraits", "AIPortraits", "MemoriesBookOverlay.cs");
  const memoriesVmPath = path.join(sourceRoot, "Modules", "Portraits", "AIPortraits", "MemoriesBookVM.cs");
  const memoriesPrefabPath = path.join(moduleRoot, "GUI", "Prefabs", "AIPortraitsMemoriesBook.xml");
  const eventArtFactoryPath = path.join(sourceRoot, "Modules", "Portraits", "UI", "EventArt", "ReignEventArtTextureFactory.cs");
  const notableManagerPath = path.join(sourceRoot, "Modules", "Characters", "UI", "ReignNotableGenerationPopupManager.cs");
  const calibrationServicePath = path.join(sourceRoot, "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs");
  const files = [hostPath, liveTestPath, correspondenceManagerPath, individualManagerPath,
    socialManagerPath, correspondenceVmPath, individualVmPath, socialVmPath, memoriesManagerPath, memoriesVmPath,
    memoriesPrefabPath, eventArtFactoryPath, notableManagerPath, calibrationServicePath];
  const contractErrors = [];
  for (const file of files) {
    if (!fs.existsSync(file)) contractErrors.push(`Required native calibration source is missing: ${path.relative(workspaceRoot, file)}.`);
  }
  if (contractErrors.length) return { checkedTargets: runtimeEntries.length, targetIds: runtimeEntries.map((entry) => entry.id), errors: contractErrors };

  const host = fs.readFileSync(hostPath, "utf8");
  const liveTest = fs.readFileSync(liveTestPath, "utf8");
  const memoriesVm = fs.readFileSync(memoriesVmPath, "utf8");
  const memoriesPrefab = fs.readFileSync(memoriesPrefabPath, "utf8");
  for (const entry of runtimeEntries) {
    const expectedAction = `ui-open --target ${entry.id}`;
    if (entry.liveAction !== expectedAction) contractErrors.push(`${entry.id} does not use the shared native calibration command.`);
    if (!host.includes(`case "${entry.id}":`)) contractErrors.push(`${entry.id} is absent from the client ui-open target switch.`);
    if (!liveTest.includes(entry.id)) contractErrors.push(`${entry.id} is absent from ReignLiveTest command help.`);
  }
  if (declared.targetCount !== runtimeEntries.length) {
    contractErrors.push(`nativeCalibration.targetCount ${declared.targetCount} does not match ${runtimeEntries.length} runtime catalog targets.`);
  }
  const uniqueMovieCount = new Set(runtimeEntries.map((entry) => entry.movie)).size;
  if (declared.uniqueRuntimeMovieCount !== uniqueMovieCount) {
    contractErrors.push(`nativeCalibration.uniqueRuntimeMovieCount ${declared.uniqueRuntimeMovieCount} does not match ${uniqueMovieCount}.`);
  }
  if (previewerSupportEntries.length !== 1) {
    contractErrors.push(`Expected one previewer-only support UI entry, found ${previewerSupportEntries.length}.`);
  } else {
    const support = previewerSupportEntries[0];
    if (support.movie !== "ReignUiCalibrationOverlay" || support.nativeInjection !== false || support.liveAction !== "previewer only") {
      contractErrors.push("ReignUiCalibrationOverlay must be explicitly cataloged as previewer-only with nativeInjection=false.");
    }
  }

  const providerFreeMarkers = [
    [correspondenceManagerPath, "OpenForCalibration", "Correspondence lacks a calibration-only open path."],
    [individualManagerPath, "OpenForCalibration", "Individual Chat lacks a calibration-only open path."],
    [socialManagerPath, "OpenForCalibration", "Social Event lacks a calibration-only open path."],
    [memoriesManagerPath, "OpenForCalibration", "Memories Book lacks a calibration-only open path."],
    [correspondenceVmPath, "Calibration mode does not send letters.", "Correspondence calibration can still send or mutate letters."],
    [individualVmPath, "Calibration mode does not send dialogue.", "Individual Chat calibration can still call dialogue providers."],
    [socialVmPath, "Provider-free UI calibration fixture.", "Social Event calibration can still start a server event."],
    [memoriesVmPath, "CalibrationCaptions", "Memories Book calibration does not use deterministic memory fixtures."],
    [memoriesVmPath, "BuildMemorySceneImageId(text)", "Memories Book production entries do not retain generated scene-image behavior."],
    [memoriesVmPath, '"feast_empire", "toasts_and_table_talk"', "Memories Book calibration lacks its provider-free packaged scene."],
    [memoriesPrefabPath, '<ReignEventArtWidget Id="AIPortraitsMemoryBookScene"', "Memories Book does not render its rectangular scene through the event-art widget."],
    [eventArtFactoryPath, 'MemorySceneTemplateId = "memory_scene"', "Memory Book scene files lack a bounded event-art image-id route."],
    [hostPath, "ui_calibration_diplomacy", "Diplomacy calibration does not use an isolated synthetic announcement."],
    [hostPath, "ShowCalibrationFixture()", "Notable Generation calibration does not use its distinct temporary fixture."],
    [notableManagerPath, '"PREPARING CALRADIA"', "Notable Generation calibration does not use the approved title."],
    [notableManagerPath, '"Generating the notable characters and relationships needed for this campaign."', "Notable Generation calibration does not use the approved explanatory state."],
    [notableManagerPath, '"Building notable 18 of 42"', "Notable Generation calibration does not use the approved progress state."],
    [calibrationServicePath, "supportUiInjected = false", "Headless native snapshots do not attest that visual calibration support UI was absent."],
    [hostPath, "AutomationLateUpdateCount >= 2", "War Council ui-open can complete before its native map surface has rendered."],
    [notableManagerPath, "_activeScreen.UpdateStatus(_title, _detail, _progress, _hasError, _retry)", "A newly created Notable Generation popup does not receive the requested calibration fixture state."]
  ];
  for (const [file, marker, message] of providerFreeMarkers) {
    if (!fs.readFileSync(file, "utf8").includes(marker)) contractErrors.push(message);
  }
  const calibrationService = fs.readFileSync(calibrationServicePath, "utf8");
  if (calibrationService.includes("layer.LoadMovie(OverlayMovieName")) {
    contractErrors.push("Native calibration still injects ReignUiCalibrationOverlay into Bannerlord.");
  }
  if (["TrySetExpanded", "TryGetExpanded", "TrySelectWidgetForCapture"].some((name) => calibrationService.includes(`public static bool ${name}`))) {
    contractErrors.push("Native calibration still exposes launcher, expansion, or widget-selection actions for the previewer-only overlay.");
  }
  if (host.includes('target == "calibration-overlay"') || host.includes('case "calibration-overlay"')) {
    contractErrors.push("The native live-test host still exposes previewer-only calibration-overlay actions.");
  }
  if (memoriesPrefab.includes("MemoryBookTextureProviderName") || memoriesPrefab.includes("MemoryBookAdditionalArgs")
      || memoriesVm.includes("CharacterImageTextureProvider")) {
    contractErrors.push("Memories Book still exposes a CharacterImageTextureProvider portrait fallback inside its scene viewport.");
  }
  if (host.includes('"UI CALIBRATION"')) {
    contractErrors.push("Notable Generation ui-open still exposes the temporary UI CALIBRATION placeholder state.");
  }
  return {
    checkedTargets: runtimeEntries.length,
    targetIds: runtimeEntries.map((entry) => entry.id),
    uniqueRuntimeMovies: uniqueMovieCount,
    previewerSupportMovie: previewerSupportEntries[0]?.movie || "",
    errors: contractErrors
  };
}

function auditCalibrationOverlayInputContract() {
  const overlayPath = path.join(prefabRoot, "ReignUiCalibrationOverlay.xml");
  const contractErrors = [];
  if (!fs.existsSync(overlayPath)) {
    return {
      prefab: path.relative(workspaceRoot, overlayPath).replaceAll("\\", "/"),
      checkedTargets: 0,
      targets: [],
      errors: ["The calibration support overlay prefab is missing."]
    };
  }

  const xml = fs.readFileSync(overlayPath, "utf8");
  const documentNode = parseXml(xml);
  const wrapperIds = ["ReignUiCalibrationLauncherCanvas", "ReignUiCalibrationReferenceCanvas"];
  const targets = [];
  for (const id of wrapperIds) {
    const node = findNodeById(documentNode, id);
    const report = {
      id,
      found: Boolean(node),
      doNotAcceptEvents: node?.attributes.DoNotAcceptEvents || "",
      doNotPassEventsToChildren: node?.attributes.DoNotPassEventsToChildren || ""
    };
    targets.push(report);
    if (!node) {
      contractErrors.push(`Required full-size support wrapper ${id} is missing.`);
      continue;
    }
    if (node.attributes.DoNotAcceptEvents !== "true") {
      contractErrors.push(`${id} must set DoNotAcceptEvents=\"true\" so its full-size bounds cannot intercept the underlying interface.`);
    }
    if (Object.prototype.hasOwnProperty.call(node.attributes, "DoNotPassEventsToChildren")) {
      contractErrors.push(`${id} must not set DoNotPassEventsToChildren; its interactive child controls require normal propagation.`);
    }
  }

  const launcher = findNodeById(documentNode, "ReignUiCalibrationLauncher");
  const launcherReport = {
    id: "ReignUiCalibrationLauncher",
    found: Boolean(launcher),
    tag: launcher?.tag || "",
    clickCommand: launcher?.attributes["Command.Click"] || ""
  };
  targets.push(launcherReport);
  if (!launcher) contractErrors.push("The calibration launcher button is missing.");
  else {
    if (launcher.tag !== "ButtonWidget") contractErrors.push("ReignUiCalibrationLauncher must remain a ButtonWidget.");
    if (launcher.attributes["Command.Click"] !== "ExecuteToggle") {
      contractErrors.push("ReignUiCalibrationLauncher must retain Command.Click=\"ExecuteToggle\".");
    }
  }

  const selection = findNodeById(documentNode, "ReignUiCalibrationSelection");
  const selectionReport = {
    id: "ReignUiCalibrationSelection",
    found: Boolean(selection),
    doNotAcceptEvents: selection?.attributes.DoNotAcceptEvents || ""
  };
  targets.push(selectionReport);
  if (!selection) contractErrors.push("The calibration selection outline is missing.");
  else {
    if (selection.attributes.DoNotAcceptEvents !== "true") {
      contractErrors.push("ReignUiCalibrationSelection must set DoNotAcceptEvents=\"true\".");
    }
    const visualContainer = selection.children.find((child) => child.tag === "Children");
    const visualChildren = visualContainer?.children || [];
    if (!visualChildren.length) contractErrors.push("ReignUiCalibrationSelection has no visual children to audit.");
    visualChildren.forEach((child, index) => {
      const id = `ReignUiCalibrationSelection/visual[${index + 1}]`;
      targets.push({
        id,
        found: true,
        tag: child.tag,
        doNotAcceptEvents: child.attributes.DoNotAcceptEvents || ""
      });
      if (child.attributes.DoNotAcceptEvents !== "true") {
        contractErrors.push(`${id} must set DoNotAcceptEvents=\"true\" so selection chrome cannot intercept the calibrated interface.`);
      }
    });
  }

  return {
    prefab: path.relative(workspaceRoot, overlayPath).replaceAll("\\", "/"),
    checkedTargets: targets.length,
    targets,
    errors: contractErrors
  };
}

function auditStandaloneEditableInputContract() {
  const contractErrors = [];
  const targets = [];
  const entriesByPrefab = new Map();
  for (const entry of catalog.interfaces.filter((item) => !item.supportUi)) {
    const fileName = path.basename(entry.prefab);
    if (!entriesByPrefab.has(fileName)) entriesByPrefab.set(fileName, entry);
  }

  for (const [fileName, entry] of entriesByPrefab) {
    const prefabPath = path.join(prefabRoot, fileName);
    if (!fs.existsSync(prefabPath)) continue;
    const documentNode = parseXml(fs.readFileSync(prefabPath, "utf8"));
    const editableWidgets = findNodesByTag(documentNode, "EditableTextWidget");
    if (!editableWidgets.length) continue;

    const manager = classIndex.get(entry.manager);
    // Some catalog managers delegate their Gauntlet layer to a companion ScreenBase
    // class in the same source file, so audit the complete catalog-manager source.
    const managerBody = manager?.fullText || "";
    const permitsAllInput = /SetInputRestrictions\s*\(\s*true\s*,\s*InputUsageMask\.All\s*\)/.test(managerBody);
    const explicitlyAllowsKeys = /\.Input\.IsKeysAllowed\s*=\s*true\s*;/.test(managerBody);
    const permitsMouseOnly = /SetInputRestrictions\s*\(\s*true\s*,\s*InputUsageMask\.Mouse\s*\)/.test(managerBody);
    const explicitlyDisallowsKeys = /\.Input\.IsKeysAllowed\s*=\s*false\s*;/.test(managerBody);
    const target = {
      prefab: fileName,
      movie: entry.movie,
      manager: entry.manager,
      managerSource: manager?.file || "",
      editableWidgetIds: editableWidgets.map((node) => node.attributes.Id || "(unnamed)"),
      permitsAllInput,
      explicitlyAllowsKeys,
      permitsMouseOnly,
      explicitlyDisallowsKeys
    };
    targets.push(target);

    if (!manager) {
      contractErrors.push(`${fileName} contains EditableTextWidget controls but catalog manager ${entry.manager} is absent.`);
      continue;
    }
    if (!permitsAllInput) {
      contractErrors.push(`${fileName} contains EditableTextWidget controls but ${entry.manager} does not set InputUsageMask.All.`);
    }
    if (!explicitlyAllowsKeys) {
      contractErrors.push(`${fileName} contains EditableTextWidget controls but ${entry.manager} does not explicitly set Input.IsKeysAllowed = true.`);
    }
    if (permitsMouseOnly) {
      contractErrors.push(`${fileName} contains EditableTextWidget controls but ${entry.manager} still applies mouse-only input restrictions.`);
    }
    if (explicitlyDisallowsKeys) {
      contractErrors.push(`${fileName} contains EditableTextWidget controls but ${entry.manager} still explicitly disables keyboard input.`);
    }
  }

  return {
    checkedTargets: targets.length,
    targets,
    errors: contractErrors
  };
}

function auditCastleLayoutInteractionContract() {
  const castleLayoutPath = path.join(prefabRoot, "ReignCastleLayoutScreen.xml");
  const contractErrors = [];
  const targets = [];
  const expectedDecorativeDataSources = [
    "{CastleGardenSlots}",
    "{NobleSolarSlots}",
    "{LibrarySlots}",
    "{TrainingYardSlots}",
    "{InnerCourtyardSlots}",
    "{DiningChamberSlots}",
    "{StableCourtyardSlots}",
    "{PortraitGallerySlots}",
    "{MainHallSlots}",
    "{RoyalBedroomSlots}",
    "{ChapelSlots}",
    "{ThroneRoomSlots}",
    "{BathsSlots}",
    "{BattlementSlots}"
  ];
  const expectedGuestDataSources = [
    "{GuestBedroomRow1}",
    "{GuestBedroomRow2}",
    "{GuestBedroomRow3}",
    "{GuestBedroomRow4}"
  ];
  if (!fs.existsSync(castleLayoutPath)) {
    return {
      prefab: path.relative(workspaceRoot, castleLayoutPath).replaceAll("\\", "/"),
      checkedTargets: 0,
      targets,
      errors: ["The Castle Layout prefab is missing."]
    };
  }

  const documentNode = parseXml(fs.readFileSync(castleLayoutPath, "utf8"));
  const listPanels = findNodesByTag(documentNode, "ListPanel");
  const discoveredDecorativeDataSources = listPanels
    .map((node) => node.attributes.DataSource || "")
    .filter((value) => /^\{[A-Za-z_][A-Za-z0-9_]*Slots\}$/.test(value));
  compareSets("Castle Layout decorative slot lists", expectedDecorativeDataSources, discoveredDecorativeDataSources, contractErrors);

  for (const dataSource of expectedDecorativeDataSources) {
    const matches = listPanels.filter((node) => node.attributes.DataSource === dataSource);
    const panel = matches[0];
    const target = {
      dataSource,
      found: matches.length === 1,
      doNotAcceptEvents: panel?.attributes.DoNotAcceptEvents || "",
      doNotPassEventsToChildren: panel?.attributes.DoNotPassEventsToChildren || "",
      interactiveDescendants: panel ? findNodes(panel, (node) => node.tag === "ButtonWidget" || node.tag === "EditableTextWidget" || Object.keys(node.attributes || {}).some((name) => name.startsWith("Command."))).length : 0
    };
    targets.push(target);
    if (matches.length !== 1) {
      contractErrors.push(`${dataSource} must identify exactly one decorative Castle Layout ListPanel; found ${matches.length}.`);
      continue;
    }
    if (panel.attributes.DoNotAcceptEvents !== "true" || panel.attributes.DoNotPassEventsToChildren !== "true") {
      contractErrors.push(`${dataSource} must set both DoNotAcceptEvents=\"true\" and DoNotPassEventsToChildren=\"true\" so its decorative slot subtree cannot block the room hit region.`);
    }
    if (target.interactiveDescendants) {
      contractErrors.push(`${dataSource} is a decorative slot list but contains ${target.interactiveDescendants} interactive descendant(s).`);
    }
  }

  for (const dataSource of expectedGuestDataSources) {
    const matches = listPanels.filter((node) => node.attributes.DataSource === dataSource);
    const panel = matches[0];
    const buttons = panel ? findNodesByTag(panel, "ButtonWidget") : [];
    targets.push({
      dataSource,
      found: matches.length === 1,
      doNotPassEventsToChildren: panel?.attributes.DoNotPassEventsToChildren || "",
      buttonCount: buttons.length,
      clickCommands: buttons.map((node) => node.attributes["Command.Click"] || ""),
      eventTransparentButtons: buttons.filter((node) => node.attributes.DoNotAcceptEvents === "true").length
    });
    if (matches.length !== 1) {
      contractErrors.push(`${dataSource} must identify exactly one interactive Guest Bedroom ListPanel; found ${matches.length}.`);
      continue;
    }
    if (panel.attributes.DoNotPassEventsToChildren === "true") {
      contractErrors.push(`${dataSource} must not block events from reaching its Guest Bedroom item buttons.`);
    }
    if (buttons.length !== 1 || buttons.some((node) => node.attributes["Command.Click"] !== "ExecuteOpen" || node.attributes.DoNotAcceptEvents === "true")) {
      contractErrors.push(`${dataSource} must retain one event-accepting ItemTemplate ButtonWidget with Command.Click=\"ExecuteOpen\".`);
    }
  }

  return {
    prefab: path.relative(workspaceRoot, castleLayoutPath).replaceAll("\\", "/"),
    checkedTargets: targets.length,
    targets,
    errors: contractErrors
  };
}

function auditNativeAugmentationContract() {
  const declared = catalog.nativeAugmentations || {};
  const declaredTargets = Array.isArray(declared.targets) ? declared.targets : [];
  const applications = [];
  const contractErrors = [];
  for (const document of sourceDocuments) {
    const classPattern = /((?:\s*\[PrefabExtension\([^\r\n]+\)\]\s*)+)(?:internal|public)[^{;\r\n]*\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/g;
    for (const classMatch of document.text.matchAll(classPattern)) {
      const attributeBlock = classMatch[1];
      const className = classMatch[2];
      for (const attribute of attributeBlock.matchAll(/\[PrefabExtension\("([^"]+)",\s*"([^"]+)"\)\]/g)) {
        applications.push({
          target: attribute[1],
          xpath: attribute[2],
          patchClass: className,
          source: path.relative(moduleRoot, document.file).replaceAll("\\", "/"),
          line: document.text.slice(0, classMatch.index + attribute.index).split("\n").length
        });
      }
    }
  }
  applications.sort((left, right) => left.target.localeCompare(right.target) || left.patchClass.localeCompare(right.patchClass) || left.xpath.localeCompare(right.xpath));
  const discoveredTargets = [...new Set(applications.map((entry) => entry.target))].sort((left, right) => left.localeCompare(right));
  const catalogTargets = declaredTargets.map((entry) => entry.target).sort((left, right) => left.localeCompare(right));
  compareSets("native augmentation target catalog", discoveredTargets, catalogTargets, contractErrors);
  if (declared.targetCount !== discoveredTargets.length) contractErrors.push(`nativeAugmentations.targetCount ${declared.targetCount} does not match ${discoveredTargets.length}.`);
  if (declared.patchApplicationCount !== applications.length) contractErrors.push(`nativeAugmentations.patchApplicationCount ${declared.patchApplicationCount} does not match ${applications.length}.`);
  for (const entry of declaredTargets) {
    if (!entry.id || !entry.label || !entry.target || !entry.basePrefab || !Array.isArray(entry.sources) || !entry.sources.length) {
      contractErrors.push(`Native augmentation target '${entry.target || entry.id || "unknown"}' lacks id, label, basePrefab, or sources.`);
      continue;
    }
    const discoveredSources = [...new Set(applications.filter((item) => item.target === entry.target).map((item) => item.source))].sort((left, right) => left.localeCompare(right));
    const declaredSources = [...new Set(entry.sources)].sort((left, right) => left.localeCompare(right));
    compareSets(`${entry.target} augmentation sources`, discoveredSources, declaredSources, contractErrors);
    for (const source of entry.sources) {
      const sourcePath = path.join(moduleRoot, source);
      if (!fs.existsSync(sourcePath)) continue;
      const runtimePatches = discoverPrefabExtensions(fs.readFileSync(sourcePath, "utf8"), source)
        .filter((patch) => patch.target === entry.target);
      for (const patch of runtimePatches) {
        const hasRuntimeContent = patch.attributes?.length > 0 || patch.contentXml?.trim().startsWith("<");
        if (!hasRuntimeContent) {
          contractErrors.push(`${patch.patchClass} has no source-discoverable runtime XML content; named const strings and inline XML must compose identically.`);
        }
      }
    }
  }
  return {
    checkedTargets: discoveredTargets.length,
    checkedApplications: applications.length,
    targets: discoveredTargets,
    applications,
    errors: contractErrors
  };
}

function auditTypographyContract() {
  const typography = modernStyleContract.typography || {};
  const acceptance = typography.acceptance || {};
  const allowedBrushes = [...new Set(typography.canonicalBrush?.allowedNativeSerifBrushes || [])];
  const allowedColors = [...new Set(Object.values(typography.colorRoles || {}).map(normalizeColorToken))];
  const explicitFontAttribute = String(typography.canonicalBrush?.explicitFontAttribute || "").trim();
  const requiredFontFace = String(typography.canonicalBrush?.requiredExplicitFontFace || "").trim();
  const forbiddenSyntheticTextEffects = [...new Set(typography.textEffects?.forbiddenNonzeroAttributes || [])];
  const contractErrors = [];
  const brushCounts = {};
  const fontFaceCounts = {};
  const colorCounts = {};
  const fontSizeCounts = {};
  const syntheticEffectCounts = {};
  const interfaces = [];
  let checkedWidgets = 0;

  if (!acceptance.fontBrushMustBeExplicit) contractErrors.push("Modern style contract does not require an explicit native text brush.");
  if (!acceptance.fontFaceMustBeExplicit) contractErrors.push("Modern style contract does not require an explicit native font face.");
  if (!acceptance.fontFamilyAndFaceMustMatch) contractErrors.push("Modern style contract does not require the approved font family and face.");
  if (acceptance.requiredFontFace !== requiredFontFace) contractErrors.push("Modern style contract has inconsistent required native font-face declarations.");
  if (explicitFontAttribute !== "Brush.Font") contractErrors.push(`Modern style contract must require Brush.Font, found '${explicitFontAttribute || "missing"}'.`);
  if (requiredFontFace !== liveFontAlias) contractErrors.push(`Modern style contract must require the packaged ${liveFontAlias} face, found '${requiredFontFace || "missing"}'.`);
  if (!acceptance.syntheticTextEffectsMustBeInactive) contractErrors.push("Modern style contract does not prohibit active synthetic text effects.");
  if (!acceptance.textColorMustUseExactFrozenToken) contractErrors.push("Modern style contract does not require exact frozen text-color tokens.");
  if (!acceptance.closestSupportedSizeRequired) contractErrors.push("Modern style contract does not require the closest supported font size.");
  if (!allowedBrushes.length) contractErrors.push("Modern style contract declares no allowed native serif brushes.");
  if (!allowedColors.length) contractErrors.push("Modern style contract declares no frozen text-color roles.");
  if (!forbiddenSyntheticTextEffects.length) contractErrors.push("Modern style contract declares no synthetic text-effect attributes to audit.");

  for (const fileName of prefabFiles) {
    const xml = fs.readFileSync(path.join(prefabRoot, fileName), "utf8");
    const documentNode = parseXml(xml);
    const report = {
      fileName,
      checkedWidgets: 0,
      brushCounts: {},
      fontFaceCounts: {},
      colorCounts: {},
      fontSizeCounts: {},
      syntheticEffectCounts: {},
      errors: []
    };
    for (const child of documentNode.children) auditTypographyNode(child, xml, report, `/${child.tag}[1]`, allowedBrushes, allowedColors, explicitFontAttribute, requiredFontFace, forbiddenSyntheticTextEffects);
    checkedWidgets += report.checkedWidgets;
    mergeCounts(brushCounts, report.brushCounts);
    mergeCounts(fontFaceCounts, report.fontFaceCounts);
    mergeCounts(colorCounts, report.colorCounts);
    mergeCounts(fontSizeCounts, report.fontSizeCounts);
    mergeCounts(syntheticEffectCounts, report.syntheticEffectCounts);
    report.errors.forEach((message) => contractErrors.push(`${fileName}: ${message}`));
    interfaces.push(report);
  }

  return {
    contractPath: path.relative(workspaceRoot, modernStyleContractPath).replaceAll("\\", "/"),
    contractVersion: modernStyleContract.version || "",
    allowedBrushes,
    allowedColors,
    explicitFontAttribute,
    requiredFontFace,
    forbiddenSyntheticTextEffects,
    checkedWidgets,
    brushCounts: sortCountObject(brushCounts),
    fontFaceCounts: sortCountObject(fontFaceCounts),
    colorCounts: sortCountObject(colorCounts),
    fontSizeCounts: sortCountObject(fontSizeCounts, true),
    syntheticEffectCounts: sortCountObject(syntheticEffectCounts),
    interfaces,
    errors: contractErrors
  };
}

function auditVisualStyleContract() {
  const paletteTokens = Object.fromEntries(Object.entries(modernPalette.tokens || {})
    .map(([name, value]) => [name, normalizeColorToken(value)]));
  const exactColors = new Set(Object.values(paletteTokens));
  const paletteRgb = new Set([...exactColors].map((value) => value.slice(0, 7)));
  const forbiddenBrushes = new Set(["ButtonBrush2"]);
  const errors = [];
  const interfaces = [];
  let checkedColorAttributes = 0;
  let checkedBrushAttributes = 0;
  let checkedPatchColors = 0;

  if (modernPalette.schema !== "reign-ui-modern-palette-v1") {
    errors.push(`Palette schema must be 'reign-ui-modern-palette-v1', found '${modernPalette.schema || "missing"}'.`);
  }
  if (!exactColors.size) errors.push("Modern palette declares no exact color tokens.");

  for (const fileName of prefabFiles) {
    const xml = fs.readFileSync(path.join(prefabRoot, fileName), "utf8");
    const documentNode = parseXml(xml);
    const report = { fileName, checkedColorAttributes: 0, checkedBrushAttributes: 0, exceptions: [], errors: [] };
    for (const child of documentNode.children) {
      auditVisualStyleNode(child, xml, fileName, report, `/${child.tag}[1]`, exactColors, paletteRgb, forbiddenBrushes);
    }
    checkedColorAttributes += report.checkedColorAttributes;
    checkedBrushAttributes += report.checkedBrushAttributes;
    report.errors.forEach((message) => errors.push(`${fileName}: ${message}`));
    interfaces.push(report);
  }

  const augmentationSources = [...new Set((catalog.nativeAugmentations?.targets || []).flatMap((entry) => entry.sources || []))];
  const nativeAugmentations = [];
  for (const source of augmentationSources) {
    const sourcePath = path.join(moduleRoot, source);
    if (!fs.existsSync(sourcePath)) continue;
    const text = fs.readFileSync(sourcePath, "utf8");
    const report = { source, checkedColors: 0, errors: [] };
    for (const match of text.matchAll(/#[0-9A-Fa-f]{8}/g)) {
      report.checkedColors += 1;
      checkedPatchColors += 1;
      const token = normalizeColorToken(match[0]);
      if (isApprovedVisualColor(token, exactColors, paletteRgb)) continue;
      report.errors.push(`${lineForOffset(text, match.index)} uses off-palette native augmentation color '${token}'.`);
    }
    report.errors.forEach((message) => errors.push(`${source}: ${message}`));
    nativeAugmentations.push(report);
  }

  return {
    schema: "reign-ui-visual-style-contract-audit-v1",
    palettePath: path.relative(workspaceRoot, palettePath).replaceAll("\\", "/"),
    paletteVersion: modernPalette.version || "",
    exactColors: [...exactColors].sort(),
    forbiddenBrushes: [...forbiddenBrushes].sort(),
    checkedColorAttributes,
    checkedPatchColors,
    checkedBrushAttributes,
    interfaces,
    nativeAugmentations,
    errors
  };
}

function auditVisualStyleNode(node, xml, fileName, report, xmlPath, exactColors, paletteRgb, forbiddenBrushes) {
  for (const [attribute, rawValue] of Object.entries(node.attributes || {})) {
    if (attribute === "Brush") {
      report.checkedBrushAttributes += 1;
      if (forbiddenBrushes.has(String(rawValue).trim())) {
        report.errors.push(`${location(xml, node.offset)} ${xmlPath} uses legacy brush '${rawValue}'.`);
      }
      continue;
    }
    if (!(attribute === "Color" || attribute === "Brush.Color" || attribute.endsWith("FontColor"))) continue;
    const token = normalizeColorToken(rawValue);
    if (!/^#[0-9A-F]{8}$/.test(token)) continue;
    report.checkedColorAttributes += 1;

    if (fileName === "ReignUiCalibrationOverlay.xml"
      && new Set(["#00000001", "#65E1FFFF", "#FFFFFFFF"]).has(token)) {
      report.exceptions.push({ path: xmlPath, attribute, token, reason: "calibration-editor-control" });
      continue;
    }
    if (token === "#FFFFFFFF" && node.attributes.Sprite && !/^BlankWhiteSquare(?:_9)?$/.test(node.attributes.Sprite)) {
      report.exceptions.push({ path: xmlPath, attribute, token, reason: "identity-tint-for-approved-image-sprite" });
      continue;
    }
    if (isApprovedVisualColor(token, exactColors, paletteRgb)) continue;
    report.errors.push(`${location(xml, node.offset)} ${xmlPath} ${attribute} uses off-palette color '${rawValue}'.`);
  }

  const counts = new Map();
  for (const child of node.children || []) {
    const index = (counts.get(child.tag) || 0) + 1;
    counts.set(child.tag, index);
    auditVisualStyleNode(child, xml, fileName, report, `${xmlPath}/${child.tag}[${index}]`, exactColors, paletteRgb, forbiddenBrushes);
  }
}

function isApprovedVisualColor(token, exactColors, paletteRgb) {
  if (exactColors.has(token)) return true;
  // Modal dimmers may lower alpha, but their RGB identity must still be one
  // of the exact palette tokens. This prevents a new hue masquerading as a
  // transparency exception.
  return /^#[0-9A-F]{8}$/.test(token) && paletteRgb.has(token.slice(0, 7));
}

function lineForOffset(text, offset) {
  return `line ${text.slice(0, offset).split(/\r?\n/).length}`;
}

function auditTypographyNode(node, xml, report, xmlPath, allowedBrushes, allowedColors, explicitFontAttribute, requiredFontFace, forbiddenSyntheticTextEffects) {
  if (node.tag === "TextWidget" || node.tag === "RichTextWidget" || node.tag === "EditableTextWidget") {
    report.checkedWidgets += 1;
    const brush = String(node.attributes.Brush || "").trim();
    const fontFace = String(node.attributes[explicitFontAttribute] || "").trim();
    const fontSizeText = String(node.attributes["Brush.FontSize"] || "").trim();
    const color = normalizeColorToken(node.attributes["Brush.FontColor"] || "");
    if (!brush) report.errors.push(`${location(xml, node.offset)} ${xmlPath} has no explicit Brush.`);
    else if (!allowedBrushes.includes(brush) && !(node.tag === "RichTextWidget"
      && node.attributes.Text === "@RichText"
      && (modernStyleContract.typography.conversationActions?.brushes || []).includes(brush)))
      report.errors.push(`${location(xml, node.offset)} ${xmlPath} uses unapproved Brush '${brush}'.`);
    if (brush) incrementCount(report.brushCounts, brush);

    if (!fontFace) report.errors.push(`${location(xml, node.offset)} ${xmlPath} has no explicit ${explicitFontAttribute || "Brush.Font"}.`);
    else if (fontFace !== requiredFontFace) report.errors.push(`${location(xml, node.offset)} ${xmlPath} uses unapproved ${explicitFontAttribute} '${fontFace}'; expected '${requiredFontFace}'.`);
    if (fontFace) incrementCount(report.fontFaceCounts, fontFace);

    const fontSize = Number(fontSizeText);
    if (!fontSizeText) report.errors.push(`${location(xml, node.offset)} ${xmlPath} has no explicit Brush.FontSize.`);
    else if (!Number.isFinite(fontSize) || fontSize <= 0) report.errors.push(`${location(xml, node.offset)} ${xmlPath} has invalid Brush.FontSize '${fontSizeText}'.`);
    if (fontSizeText) incrementCount(report.fontSizeCounts, fontSizeText);

    if (!color) report.errors.push(`${location(xml, node.offset)} ${xmlPath} has no explicit Brush.FontColor.`);
    else if (!allowedColors.includes(color)) report.errors.push(`${location(xml, node.offset)} ${xmlPath} uses off-palette Brush.FontColor '${node.attributes["Brush.FontColor"]}'.`);
    if (color) incrementCount(report.colorCounts, color);

    for (const attribute of forbiddenSyntheticTextEffects) {
      const value = node.attributes[attribute];
      if (value === undefined || value === null || isInactiveSyntheticTextEffect(attribute, value)) continue;
      report.errors.push(`${location(xml, node.offset)} ${xmlPath} uses active synthetic text effect ${attribute}='${value}'.`);
      incrementCount(report.syntheticEffectCounts, `${attribute}=${value}`);
    }
  }

  const counts = new Map();
  for (const child of node.children) {
    const index = (counts.get(child.tag) || 0) + 1;
    counts.set(child.tag, index);
    auditTypographyNode(child, xml, report, `${xmlPath}/${child.tag}[${index}]`, allowedBrushes, allowedColors, explicitFontAttribute, requiredFontFace, forbiddenSyntheticTextEffects);
  }
}

function isInactiveSyntheticTextEffect(name, value) {
  const normalized = String(value || "").trim();
  if (!normalized) return true;
  if (name.endsWith("Color")) return /^#[0-9A-Fa-f]{6}00$/.test(normalized);
  const numericParts = normalized.match(/-?(?:\d+(?:\.\d+)?|\.\d+)/g);
  return Boolean(numericParts?.length) && numericParts.every((part) => Number(part) === 0);
}

function normalizeColorToken(value) {
  const token = String(value || "").trim().toUpperCase();
  if (!token) return "";
  return token.startsWith("#") ? token : `#${token}`;
}

function incrementCount(target, key) {
  target[key] = (target[key] || 0) + 1;
}

function mergeCounts(target, source) {
  for (const [key, count] of Object.entries(source)) target[key] = (target[key] || 0) + count;
}

function sortCountObject(source, numeric = false) {
  const entries = Object.entries(source).sort(([left], [right]) => numeric
    ? Number(left) - Number(right)
    : left.localeCompare(right));
  return Object.fromEntries(entries);
}

function fakeRenderedElement(tag, attributes, parent = null) {
  return {
    __gauntlet: { tag, attributes },
    parentElement: parent ? { closest: () => parent } : null
  };
}

function auditRuntimeNode(node, inheritedType, report, xml, xmlPath) {
  const dataSourceExpression = node.attributes.DataSource || "";
  const dataSourceName = directBinding(dataSourceExpression);
  let scopedType = inheritedType;
  let listItemType = "";
  if (dataSourceExpression) {
    const member = findRuntimeMember(inheritedType, dataSourceName);
    if (!member) {
      report.errors.push(`${location(xml, node.offset)} ${xmlPath} DataSource ${dataSourceExpression} is absent from runtime type ${inheritedType}.`);
    } else if (node.tag === "ListPanel") {
      listItemType = genericItemType(member.type);
      if (!listItemType) report.errors.push(`${location(xml, node.offset)} ${xmlPath} DataSource ${dataSourceExpression} on ${inheritedType} is not a typed list.`);
    } else {
      scopedType = runtimeTypeName(member.type);
    }
  }

  auditRuntimeAttributes(node, scopedType, report, xml, xmlPath);

  if (node.tag === "ListPanel") {
    const template = node.children.find((child) => child.tag === "ItemTemplate");
    const ordinaryChildren = node.children.filter((child) => child !== template);
    const counts = new Map();
    for (const child of ordinaryChildren) {
      const index = (counts.get(child.tag) || 0) + 1;
      counts.set(child.tag, index);
      auditRuntimeNode(child, inheritedType, report, xml, `${xmlPath}/${child.tag}[${index}]`);
    }
    if (template && listItemType) {
      template.children.forEach((child, index) => auditRuntimeNode(child, listItemType, report, xml, `${xmlPath}/ItemTemplate[1]/${child.tag}[${index + 1}]`));
    }
    return;
  }

  const counts = new Map();
  for (const child of node.children) {
    const index = (counts.get(child.tag) || 0) + 1;
    counts.set(child.tag, index);
    auditRuntimeNode(child, scopedType, report, xml, `${xmlPath}/${child.tag}[${index}]`);
  }
}

function auditRuntimeAttributes(node, typeName, report, xml, xmlPath) {
  const definition = classIndex.get(typeName);
  if (!definition) {
    if (typeName) report.unresolvedTypes.push(typeName);
    return;
  }
  for (const [attribute, expression] of Object.entries(node.attributes)) {
    if (attribute === "DataSource") continue;
    if (attribute.startsWith("Command.")) {
      const command = directCommand(expression);
      if (!command) continue;
      report.checkedCommands += 1;
      if (!findRuntimeMethod(typeName, command)) {
        report.errors.push(`${location(xml, node.offset)} ${xmlPath} ${attribute} references ${command}, absent from runtime type ${typeName}.`);
      }
      continue;
    }
    for (const binding of extractBindingNames(expression)) {
      report.checkedBindings += 1;
      if (!findRuntimeMember(typeName, binding)) {
        report.errors.push(`${location(xml, node.offset)} ${xmlPath} ${attribute} references ${binding}, absent from runtime type ${typeName}.`);
      }
    }
  }
}

function buildClassIndex(documents) {
  const index = new Map();
  const pattern = /\bclass\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*([^\{]+))?\s*\{/g;
  for (const document of documents) {
    for (const match of document.text.matchAll(pattern)) {
      const opening = document.text.indexOf("{", match.index);
      const closing = findClosingBrace(document.text, opening);
      if (closing < 0) continue;
      const body = document.text.slice(opening + 1, closing);
      const baseType = runtimeTypeName(String(match[2] || "").split(",")[0]);
      const members = new Map();
      const memberPattern = /(?:\[DataSourceProperty\]\s*)?(?:public|internal)\s+(?:static\s+)?(?:readonly\s+)?([A-Za-z_][A-Za-z0-9_.?<>,\[\]\s]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?=\{|=>|;)/g;
      for (const member of body.matchAll(memberPattern)) members.set(member[2], { name: member[2], type: member[1].trim() });
      const methods = new Set();
      const methodPattern = /(?:public|internal)\s+(?:static\s+)?(?:async\s+)?[A-Za-z_][A-Za-z0-9_.?<>,\[\]\s]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(/g;
      for (const method of body.matchAll(methodPattern)) methods.add(method[1]);
      index.set(match[1], {
        name: match[1],
        baseType,
        file: path.relative(workspaceRoot, document.file),
        body,
        fullText: document.text,
        members,
        methods
      });
    }
  }
  return index;
}

function findRuntimeMember(typeName, memberName, visited = new Set()) {
  if (!typeName || !memberName || visited.has(typeName)) return null;
  visited.add(typeName);
  const definition = classIndex.get(typeName);
  if (!definition) return null;
  return definition.members.get(memberName) || findRuntimeMember(definition.baseType, memberName, visited);
}

function findRuntimeMethod(typeName, methodName, visited = new Set()) {
  if (!typeName || !methodName || visited.has(typeName)) return false;
  visited.add(typeName);
  const definition = classIndex.get(typeName);
  if (!definition) return false;
  return definition.methods.has(methodName) || findRuntimeMethod(definition.baseType, methodName, visited);
}

function runtimeTypeName(value) {
  const withoutConstraints = String(value || "").trim().replace(/\?$/, "");
  const withoutGenerics = withoutConstraints.replace(/<.*>/s, "").trim();
  const simple = withoutGenerics.split(".").at(-1) || "";
  return simple.replace(/\[\]$/, "").trim();
}

function genericItemType(value) {
  const match = String(value || "").match(/<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>/);
  return match ? runtimeTypeName(match[1]) : "";
}

function directCommand(value) {
  return String(value || "").trim().replace(/^[@{]/, "").replace(/}$/, "").match(/^([A-Za-z_][A-Za-z0-9_]*)$/)?.[1] || "";
}

function findClosingBrace(text, opening) {
  let depth = 0;
  let quote = "";
  let escaped = false;
  let verbatim = false;
  let lineComment = false;
  let blockComment = false;
  for (let index = opening; index < text.length; index += 1) {
    const character = text[index];
    const next = text[index + 1] || "";
    if (lineComment) {
      if (character === "\n") lineComment = false;
      continue;
    }
    if (blockComment) {
      if (character === "*" && next === "/") { blockComment = false; index += 1; }
      continue;
    }
    if (quote) {
      if (verbatim && quote === '"' && character === '"' && next === '"') { index += 1; continue; }
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) { quote = ""; verbatim = false; }
      continue;
    }
    if (character === "/" && next === "/") { lineComment = true; index += 1; continue; }
    if (character === "/" && next === "*") { blockComment = true; index += 1; continue; }
    if (character === '"' || character === "'") {
      quote = character;
      verbatim = character === '"' && text[index - 1] === "@";
      continue;
    }
    if (character === "{") depth += 1;
    if (character === "}" && --depth === 0) return index;
  }
  return -1;
}

function walkFiles(root) {
  const files = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const candidate = path.join(root, entry.name);
    if (entry.isDirectory()) files.push(...walkFiles(candidate));
    else if (entry.isFile()) files.push(candidate);
  }
  return files;
}

function parseXml(xml) {
  const root = { tag: "#document", attributes: {}, children: [], offset: 0 };
  const stack = [root];
  const tokenPattern = /<!--[\s\S]*?-->|<\?[\s\S]*?\?>|<![^>]*>|<\/?[A-Za-z_][^>]*>/g;
  for (const match of xml.matchAll(tokenPattern)) {
    const token = match[0];
    if (token.startsWith("<!--") || token.startsWith("<?") || token.startsWith("<!")) continue;
    if (/^<\//.test(token)) {
      if (stack.length > 1) stack.pop();
      continue;
    }
    const name = token.match(/^<([A-Za-z_][A-Za-z0-9_.:-]*)/)?.[1];
    if (!name) continue;
    const node = { tag: name, attributes: parseAttributes(token), children: [], offset: match.index };
    stack.at(-1).children.push(node);
    if (!/\/\s*>$/.test(token)) stack.push(node);
  }
  return root;
}

function parseAttributes(token) {
  const result = {};
  const pattern = /([A-Za-z_][A-Za-z0-9_.:-]*)\s*=\s*(["'])([\s\S]*?)\2/g;
  for (const match of token.matchAll(pattern)) result[match[1]] = decodeXml(match[3]);
  return result;
}

function findNodeById(node, id) {
  if (node.attributes?.Id === id) return node;
  for (const child of node.children || []) {
    const match = findNodeById(child, id);
    if (match) return match;
  }
  return null;
}

function findNodes(node, predicate, matches = []) {
  if (predicate(node)) matches.push(node);
  for (const child of node.children || []) findNodes(child, predicate, matches);
  return matches;
}

function findNodesByTag(node, tag) {
  return findNodes(node, (candidate) => candidate.tag === tag);
}

function extractBindingNames(value) {
  const names = [];
  const pattern = /(?:@|\{)([A-Za-z_][A-Za-z0-9_]*)(?:\})?/g;
  for (const match of String(value || "").matchAll(pattern)) names.push(match[1]);
  return [...new Set(names)];
}

function directBinding(value) {
  return String(value || "").match(/^(?:@|\{)([A-Za-z_][A-Za-z0-9_]*)(?:\})?$/)?.[1] || "";
}

function compareSets(label, expected, actual, target) {
  const missing = expected.filter((item) => !actual.includes(item));
  const extra = actual.filter((item) => !expected.includes(item));
  if (missing.length) target.push(`${label} is missing: ${missing.join(", ")}`);
  if (extra.length) target.push(`${label} contains unknown entries: ${extra.join(", ")}`);
}

function location(xml, offset) {
  const before = xml.slice(0, offset);
  const line = before.split("\n").length;
  const column = offset - before.lastIndexOf("\n");
  return `line ${line}, column ${column}`;
}

function hasOwn(value, key) {
  return Boolean(value && typeof value === "object" && Object.prototype.hasOwnProperty.call(value, key));
}

function decodeXml(value) {
  return value.replaceAll("&quot;", '"').replaceAll("&apos;", "'").replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&amp;", "&");
}
