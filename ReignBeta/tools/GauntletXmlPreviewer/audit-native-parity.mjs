import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const moduleRoot = path.resolve(toolRoot, "../..");
const workspaceRoot = path.resolve(moduleRoot, "..");
const catalogPath = path.join(toolRoot, "calibration", "ui-catalog.json");
const renderReportPath = resolveArgument("--render-report", path.join(workspaceRoot, ".codex-build", "ui-preview", "render-matrix-interface-complete", "rendered-preview-audit.json"));
const evidenceRoot = resolveArgument("--evidence-root", path.join(workspaceRoot, ".codex-build", "ui-calibration", "native-parity"));
const outputPath = resolveArgument("--output", path.join(workspaceRoot, ".codex-build", "ui-preview", "native-parity-readiness.json"));
const installedRootValue = argumentValue("--installed-root");
const installedRoot = installedRootValue ? path.resolve(installedRootValue) : "";
const requirePass = process.argv.includes("--require-pass");
const catalog = readJson(catalogPath);
const renderReport = readJson(renderReportPath);
const requiredNativeCases = catalog.targetMatrix.flatMap((entry) => entry.uiScales.map((uiScale) => ({ resolution: entry.resolution, uiScale })));
const requiredPreviewViewports = [...(renderReport.viewports || [])];
const renderCases = new Map((renderReport.cases || []).map((entry) => [entry.caseId, entry]));
const nativeReviewAttestationSchema = "reign-ui-native-visual-review-attestation-v1";
const nativeReviewConfirmation = "I confirm this native capture matches the approved Reign reference for all fixed visuals and typography";
const nativeReviewCriteria = [
  "approvedFixedVisualsExact",
  "differencesConfinedToDeclaredDynamicRegions",
  "serifFaceAndWeightMatch",
  "typographicHierarchyMatches",
  "capitalizationAndTrackingMatch",
  "semanticTextColorsMatch",
  "alignmentBaselineAndWrappingMatch",
  "nativeGlyphRenderingAccepted",
  "noUnexpectedColorsOrShapes"
];
const errors = [];
const previewOnlyInterfaceStates = catalog.interfaces.reduce(
  (sum, entry) => sum + (Array.isArray(entry.previewStates) ? entry.previewStates.length : 0),
  0
);
const expectedRenderedInterfaceStates = catalog.interfaces.length + previewOnlyInterfaceStates;

if (renderReport.schema !== "reign-ui-rendered-preview-audit-v1") errors.push(`Unexpected rendered-preview schema '${renderReport.schema || "missing"}'.`);
if (renderReport.failedCount !== 0) errors.push(`Rendered-preview audit contains ${renderReport.failedCount} failed case(s).`);
if (renderReport.interfaceCount !== expectedRenderedInterfaceStates) errors.push(`Rendered-preview interface-state count ${renderReport.interfaceCount} does not match catalog count ${expectedRenderedInterfaceStates}.`);
if (renderReport.augmentationCount !== catalog.nativeAugmentations.targets.length) errors.push(`Rendered-preview augmentation count ${renderReport.augmentationCount} does not match catalog count ${catalog.nativeAugmentations.targets.length}.`);

const runtimeInterfaces = catalog.interfaces.filter((entry) => !entry.supportUi);
const standalone = runtimeInterfaces.map((entry) => auditStandalone(entry));
const augmentations = catalog.nativeAugmentations.targets.map((entry) => auditAugmentation(entry));
const surfaces = [...standalone, ...augmentations];
const nativeRuntimeStates = standalone.flatMap((entry) => entry.nativeStates || []);
const report = {
  schema: "reign-ui-native-parity-readiness-v1",
  generatedUtc: new Date().toISOString(),
  catalogPath,
  renderReportPath,
  evidenceRoot,
  installedRoot,
  requirePass,
  requiredPreviewViewports,
  requiredNativeCases,
  counts: {
    standaloneInterfaceStates: standalone.length,
    uniqueStandalonePrefabs: new Set(standalone.map((entry) => entry.fileName)).size,
    runtimeInterfaceStates: standalone.filter((entry) => entry.surfaceType === "runtime").length,
    previewOnlyInterfaceStates,
    supportInterfaceStates: catalog.interfaces.filter((entry) => entry.supportUi).length,
    nativeAugmentationTargets: augmentations.length,
    totalSurfaces: surfaces.length,
    requiredRuntimeStates: nativeRuntimeStates.length,
    nativeRuntimeStatesAccepted: nativeRuntimeStates.filter((entry) => entry.nativeAccepted).length,
    nativeRuntimeStatesPending: nativeRuntimeStates.filter((entry) => !entry.nativeAccepted).length,
    providerPreviewPassed: surfaces.filter((entry) => entry.providerPreviewPassed).length,
    installedExact: standalone.filter((entry) => entry.installedMatchesSource).length,
    nativeAccepted: surfaces.filter((entry) => entry.nativeAccepted).length,
    nativePending: surfaces.filter((entry) => !entry.nativeAccepted).length,
    structuralErrors: errors.length
  },
  providerPreviewReady: surfaces.every((entry) => entry.providerPreviewPassed),
  installedStandaloneReady: standalone.every((entry) => entry.installedMatchesSource),
  nativeReady: errors.length === 0 && surfaces.every((entry) => entry.nativeAccepted),
  errors,
  surfaces
};
report.ok = errors.length === 0 && (!requirePass || report.nativeReady);
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
process.stdout.write(`${JSON.stringify({
  schema: report.schema,
  ok: report.ok,
  providerPreviewReady: report.providerPreviewReady,
  installedStandaloneReady: report.installedStandaloneReady,
  nativeReady: report.nativeReady,
  counts: report.counts,
  pending: surfaces.filter((entry) => !entry.nativeAccepted).map((entry) => ({ id: entry.id, surfaceType: entry.surfaceType, reasons: entry.reasons })),
  errors,
  outputPath
}, null, 2)}\n`);
if (!report.ok) process.exitCode = 1;

function auditStandalone(entry) {
  const fileName = path.basename(entry.prefab);
  const sourcePath = path.join(moduleRoot, entry.prefab);
  const sourceExists = fs.existsSync(sourcePath);
  const sourceSha256 = sourceExists ? fileSha256(sourcePath) : "";
  const installedPath = installedRoot ? path.join(installedRoot, entry.prefab) : "";
  const installedExists = Boolean(installedPath && fs.existsSync(installedPath));
  const installedSha256 = installedExists ? fileSha256(installedPath) : "";
  const installedMatchesSource = sourceExists && installedExists && sourceSha256 === installedSha256;
  const expectedPreviewCaseIds = requiredPreviewViewports.map((viewport) => `${entry.id}-${viewport}`);
  const providerCases = expectedPreviewCaseIds.map((caseId) => summarizeProviderCase(caseId));
  const baseProviderPreviewPassed = providerCases.every((item) => item.passed);
  const surfaceType = "runtime";
  const acceptancePath = path.join(evidenceRoot, entry.id, "acceptance.json");
  const acceptance = loadAcceptance(acceptancePath);
  const acceptanceAudit = auditAcceptance({
    acceptance,
    acceptancePath,
    id: entry.id,
    surfaceType,
    sourceSha256,
    installedSha256,
    basePrefabSha256: "",
    patchSourceSha256: ""
  });
  const nativeStates = (entry.previewStates || [])
    .filter((state) => state?.nativeEvidence?.required === true)
    .map((state) => auditNativeRuntimeState(entry, state, sourceSha256, installedSha256));
  const stateProviderPreviewPassed = nativeStates.every((state) => state.providerPreviewPassed);
  const nativeStatesAccepted = nativeStates.every((state) => state.nativeAccepted);
  const providerPreviewPassed = baseProviderPreviewPassed && stateProviderPreviewPassed;
  const reasons = [];
  if (!sourceExists) reasons.push("workspace prefab missing");
  if (!installedRoot) reasons.push("installed module root not supplied");
  else if (!installedExists) reasons.push("installed prefab missing");
  else if (!installedMatchesSource) reasons.push("installed prefab hash differs from workspace source");
  if (!baseProviderPreviewPassed) reasons.push("provider-free preview matrix incomplete or failed");
  reasons.push(...acceptanceAudit.reasons);
  for (const state of nativeStates) {
    reasons.push(...state.reasons.map((reason) => `native runtime state ${state.id}: ${reason}`));
  }
  return {
    id: entry.id,
    label: entry.label || entry.movie,
    surfaceType,
    movie: entry.movie,
    variant: entry.variant || "",
    fileName,
    sourcePath,
    sourceExists,
    sourceSha256,
    installedPath,
    installedExists,
    installedSha256,
    installedMatchesSource,
    providerPreviewPassed,
    providerCases,
    nativeStates,
    acceptancePath,
    acceptancePresent: Boolean(acceptance),
    nativeAccepted: sourceExists && installedMatchesSource && providerPreviewPassed && acceptanceAudit.accepted && nativeStatesAccepted,
    nativeCaseCount: acceptanceAudit.caseCount,
    reasons
  };
}

function auditNativeRuntimeState(parent, state, sourceSha256, installedSha256) {
  const evidence = state.nativeEvidence || {};
  const expectedRelativePath = `states/${state.id}/acceptance.json`;
  const acceptanceRelativePath = String(evidence.acceptanceRelativePath || "").replaceAll("\\", "/");
  const acceptancePath = path.join(evidenceRoot, parent.id, acceptanceRelativePath || expectedRelativePath);
  const providerRenderId = String(evidence.providerRenderId || "");
  const providerCases = requiredPreviewViewports.map((viewport) => summarizeProviderCase(`${providerRenderId}-${viewport}`));
  const providerPreviewPassed = providerRenderId === state.id && providerCases.every((item) => item.passed);
  const contractReasons = [];
  if (evidence.matrix !== "inherit-parent") contractReasons.push("catalog matrix must inherit the parent native matrix");
  if (!evidence.setupAction) contractReasons.push("catalog setup action missing");
  if (providerRenderId !== state.id) contractReasons.push("catalog provider render id must match the nested state id");
  if (acceptanceRelativePath !== expectedRelativePath) contractReasons.push("catalog acceptance path does not match the nested state convention");
  if (!Array.isArray(evidence.requiredVisibleWidgetIds) || !evidence.requiredVisibleWidgetIds.length) contractReasons.push("catalog required visible widget ids missing");
  if (!Array.isArray(evidence.requiredEnabledWidgetIds) || !evidence.requiredEnabledWidgetIds.length) contractReasons.push("catalog required enabled widget ids missing");
  if ((evidence.requiredEnabledWidgetIds || []).some((id) => !(evidence.requiredVisibleWidgetIds || []).includes(id))) contractReasons.push("catalog enabled widget ids must be a subset of visible widget ids");
  if (!evidence.statusReceiptCommand) contractReasons.push("catalog status receipt command missing");
  if (!evidence.statusAssertions || typeof evidence.statusAssertions !== "object" || Array.isArray(evidence.statusAssertions) || !Object.keys(evidence.statusAssertions).length) contractReasons.push("catalog status assertions missing");
  if (!sourceSha256) contractReasons.push("workspace parent prefab hash missing");
  if (!installedSha256) contractReasons.push("installed parent prefab hash missing");
  else if (sourceSha256 !== installedSha256) contractReasons.push("installed parent prefab hash differs from workspace source");
  if (!providerPreviewPassed) contractReasons.push("provider render linkage incomplete or failed");

  const acceptance = loadAcceptance(acceptancePath);
  const acceptanceAudit = auditRuntimeStateAcceptance({
    acceptance,
    acceptancePath,
    parent,
    state,
    sourceSha256,
    installedSha256
  });
  const reasons = [...contractReasons, ...acceptanceAudit.reasons];
  return {
    id: state.id,
    label: state.label || state.id,
    parentTargetId: parent.id,
    movie: parent.movie,
    setupAction: evidence.setupAction || "",
    matrix: evidence.matrix || "",
    providerRenderId,
    providerPreviewPassed,
    providerCases,
    acceptancePath,
    acceptancePresent: Boolean(acceptance),
    nativeAccepted: reasons.length === 0,
    nativeCaseCount: acceptanceAudit.caseCount,
    reasons
  };
}

function auditRuntimeStateAcceptance({ acceptance, acceptancePath, parent, state, sourceSha256, installedSha256 }) {
  if (!acceptance) return { accepted: false, caseCount: 0, reasons: ["current native state acceptance manifest missing"] };
  const reasons = [];
  if (acceptance.schema !== "reign-ui-native-runtime-state-acceptance-v1") reasons.push("native state acceptance schema mismatch");
  if (acceptance.parentTargetId !== parent.id) reasons.push("native state parent target id mismatch");
  if (acceptance.stateId !== state.id) reasons.push("native state id mismatch");
  if (acceptance.movie !== parent.movie) reasons.push("native state movie mismatch");
  if (sourceSha256 && acceptance.sourceSha256 !== sourceSha256) reasons.push("native state source hash is stale");
  if (installedSha256 && acceptance.installedSha256 !== installedSha256) reasons.push("native state installed hash is stale");
  const cases = Array.isArray(acceptance.cases) ? acceptance.cases : [];
  const byKey = new Map(cases.map((entry) => [caseKey(entry.resolution, entry.uiScale), entry]));
  for (const required of requiredNativeCases) {
    const key = caseKey(required.resolution, required.uiScale);
    const current = byKey.get(key);
    if (!current) {
      reasons.push(`native state case missing: ${key}`);
      continue;
    }
    auditNativeCase(current, acceptancePath, key, sourceSha256, "runtime", reasons);
    auditNativeRuntimeStateCase(current, acceptancePath, key, parent, state, reasons);
  }
  for (const key of byKey.keys()) {
    if (!requiredNativeCases.some((entry) => caseKey(entry.resolution, entry.uiScale) === key)) reasons.push(`unexpected native state case: ${key}`);
  }
  return { accepted: reasons.length === 0, caseCount: cases.length, reasons };
}

function auditNativeRuntimeStateCase(entry, acceptancePath, key, parent, state, reasons) {
  const evidence = state.nativeEvidence;
  const manifestRoot = path.dirname(acceptancePath);
  const snapshotPath = resolveEvidencePath(manifestRoot, entry.snapshotPath);
  const snapshot = snapshotPath && fs.existsSync(snapshotPath) ? tryReadJson(snapshotPath) : null;
  if (snapshot) {
    if (snapshot.schema !== "reign-ui-runtime-snapshot-v1") reasons.push(`native state runtime snapshot schema mismatch: ${key}`);
    if (snapshot.movieName !== parent.movie) reasons.push(`native state runtime snapshot movie mismatch: ${key}`);
    const widgets = Array.isArray(snapshot.widgets) ? snapshot.widgets : [];
    for (const id of evidence.requiredVisibleWidgetIds || []) {
      const matches = widgets.filter((widget) => widget?.id === id);
      if (matches.length !== 1) {
        reasons.push(`native state required widget must appear exactly once (${id}): ${key}`);
        continue;
      }
      if (matches[0].isVisible !== true) reasons.push(`native state required widget is not visible (${id}): ${key}`);
      if (matches[0].isConnected !== true) reasons.push(`native state required widget is not connected (${id}): ${key}`);
    }
    for (const id of evidence.requiredEnabledWidgetIds || []) {
      const matches = widgets.filter((widget) => widget?.id === id);
      if (matches.length === 1 && matches[0].isEnabled !== true) reasons.push(`native state required widget is not enabled (${id}): ${key}`);
    }
  }

  const receiptPath = resolveEvidencePath(manifestRoot, entry.stateReceiptPath);
  if (!receiptPath || !fs.existsSync(receiptPath)) {
    reasons.push(`native state status receipt missing: ${key}`);
    return;
  }
  if (fileSha256(receiptPath) !== entry.stateReceiptSha256) reasons.push(`native state status receipt hash mismatch: ${key}`);
  const receipt = tryReadJson(receiptPath);
  if (!receipt) {
    reasons.push(`native state status receipt is not valid JSON: ${key}`);
    return;
  }
  const commands = Array.isArray(receipt.commands) ? receipt.commands : [];
  if (receipt.ok !== true || receipt.found !== true) reasons.push(`native state status receipt did not report a found successful run: ${key}`);
  if (Number(receipt.failedCommands || 0) !== 0 || (Array.isArray(receipt.failures) && receipt.failures.length)) reasons.push(`native state status receipt contains run failures: ${key}`);
  if (Number(receipt.commandCount) !== commands.length) reasons.push(`native state status receipt command count mismatch: ${key}`);
  const command = commands.at(-1);
  const expectedOperation = normalizeOperation(evidence.statusReceiptCommand);
  if (!command || normalizeOperation(command.operation) !== expectedOperation) {
    reasons.push(`native state status receipt final command mismatch: ${key}`);
    return;
  }
  if (command.status !== "completed") reasons.push(`native state status receipt command is not completed: ${key}`);
  if (Number(command.attempt) !== 1) reasons.push(`native state status receipt command was not completed exactly once: ${key}`);
  if (!command.commandId || commands.filter((item) => item?.commandId === command.commandId).length !== 1) reasons.push(`native state status receipt command id is missing or duplicated: ${key}`);
  if (command.error) reasons.push(`native state status receipt command contains an error: ${key}`);
  if (!command.completedUtc || !Number.isFinite(Date.parse(command.completedUtc))) reasons.push(`native state status receipt completion timestamp missing or invalid: ${key}`);
  if (!command.result || typeof command.result !== "object" || Array.isArray(command.result)) {
    reasons.push(`native state status receipt result missing: ${key}`);
    return;
  }
  for (const [assertionPath, expected] of Object.entries(evidence.statusAssertions || {})) {
    const actual = readDottedValue(command.result, assertionPath);
    if (!Object.is(actual, expected)) reasons.push(`native state status assertion failed (${assertionPath} expected ${JSON.stringify(expected)}, found ${JSON.stringify(actual)}): ${key}`);
  }
}

function auditAugmentation(entry) {
  const patchSources = entry.sources.map((relativePath) => path.join(moduleRoot, relativePath));
  const missingPatchSources = patchSources.filter((sourcePath) => !fs.existsSync(sourcePath));
  const patchSourceSha256 = missingPatchSources.length ? "" : combinedFileSha256(patchSources);
  const expectedPreviewCaseIds = requiredPreviewViewports.map((viewport) => `NativeAugmentation-${entry.target}-${viewport}`);
  const providerCases = expectedPreviewCaseIds.map((caseId) => summarizeProviderCase(caseId));
  const providerPreviewPassed = providerCases.every((item) => item.passed);
  const baseHashes = [...new Set(providerCases.map((item) => item.basePrefabSha256).filter(Boolean))];
  const basePrefabSha256 = baseHashes.length === 1 ? baseHashes[0] : "";
  const acceptancePath = path.join(evidenceRoot, entry.id, "acceptance.json");
  const acceptance = loadAcceptance(acceptancePath);
  const acceptanceAudit = auditAcceptance({
    acceptance,
    acceptancePath,
    id: entry.id,
    surfaceType: "native-augmentation",
    sourceSha256: "",
    installedSha256: "",
    basePrefabSha256,
    patchSourceSha256
  });
  const reasons = [];
  if (missingPatchSources.length) reasons.push(`missing patch source: ${missingPatchSources.join(", ")}`);
  if (!providerPreviewPassed) reasons.push("provider-free native composition matrix incomplete or failed");
  if (!basePrefabSha256) reasons.push("one exact installed native base-prefab hash was not proven by the render matrix");
  reasons.push(...acceptanceAudit.reasons);
  return {
    id: entry.id,
    label: entry.label,
    surfaceType: "native-augmentation",
    target: entry.target,
    basePrefab: entry.basePrefab,
    basePrefabSha256,
    patchSources,
    patchSourceSha256,
    providerPreviewPassed,
    providerCases,
    acceptancePath,
    acceptancePresent: Boolean(acceptance),
    nativeAccepted: missingPatchSources.length === 0 && providerPreviewPassed && Boolean(basePrefabSha256) && acceptanceAudit.accepted,
    nativeCaseCount: acceptanceAudit.caseCount,
    reasons
  };
}

function summarizeProviderCase(caseId) {
  const entry = renderCases.get(caseId);
  return {
    caseId,
    present: Boolean(entry),
    passed: Boolean(entry && !(entry.errors || []).length && Number(entry.issueCount || 0) === 0 && entry.screenshotSha256),
    screenshotPath: entry?.screenshotPath || "",
    screenshotSha256: entry?.screenshotSha256 || "",
    basePrefabSha256: entry?.nativeAugmentation?.baseSha256 || ""
  };
}

function auditAcceptance({ acceptance, acceptancePath, id, surfaceType, sourceSha256, installedSha256, basePrefabSha256, patchSourceSha256 }) {
  if (!acceptance) return { accepted: false, caseCount: 0, reasons: ["current native acceptance manifest missing"] };
  const reasons = [];
  if (acceptance.schema !== "reign-ui-native-parity-acceptance-v1") reasons.push("native acceptance schema mismatch");
  if (acceptance.targetId !== id) reasons.push("native acceptance target id mismatch");
  if (acceptance.surfaceType !== surfaceType) reasons.push("native acceptance surface type mismatch");
  if (sourceSha256 && acceptance.sourceSha256 !== sourceSha256) reasons.push("native acceptance source hash is stale");
  if (installedSha256 && acceptance.installedSha256 !== installedSha256) reasons.push("native acceptance installed hash is stale");
  if (basePrefabSha256 && acceptance.basePrefabSha256 !== basePrefabSha256) reasons.push("native acceptance base-prefab hash is stale");
  if (patchSourceSha256 && acceptance.patchSourceSha256 !== patchSourceSha256) reasons.push("native acceptance patch-source hash is stale");
  const cases = Array.isArray(acceptance.cases) ? acceptance.cases : [];
  const byKey = new Map(cases.map((entry) => [caseKey(entry.resolution, entry.uiScale), entry]));
  for (const required of requiredNativeCases) {
    const key = caseKey(required.resolution, required.uiScale);
    const current = byKey.get(key);
    if (!current) {
      reasons.push(`native case missing: ${key}`);
      continue;
    }
    auditNativeCase(current, acceptancePath, key, sourceSha256, surfaceType, reasons);
  }
  for (const key of byKey.keys()) {
    if (!requiredNativeCases.some((entry) => caseKey(entry.resolution, entry.uiScale) === key)) reasons.push(`unexpected native case: ${key}`);
  }
  return { accepted: reasons.length === 0, caseCount: cases.length, reasons };
}

function auditNativeCase(entry, acceptancePath, key, sourceSha256, surfaceType, reasons) {
  const manifestRoot = path.dirname(acceptancePath);
  const screenshotPath = resolveEvidencePath(manifestRoot, entry.screenshotPath);
  const captureMetadataPath = resolveEvidencePath(manifestRoot, entry.captureMetadataPath);
  const snapshotPath = resolveEvidencePath(manifestRoot, entry.snapshotPath);
  if (!entry.reviewed) reasons.push(`native case not reviewed: ${key}`);
  auditNativeReviewAttestation(entry, key, reasons);
  if (Number(entry.diagnosticCount) !== 0) reasons.push(`native case diagnostics are not zero: ${key}`);
  if (!screenshotPath || !fs.existsSync(screenshotPath)) reasons.push(`native screenshot missing: ${key}`);
  else if (fileSha256(screenshotPath) !== entry.screenshotSha256) reasons.push(`native screenshot hash mismatch: ${key}`);
  if (!captureMetadataPath || !fs.existsSync(captureMetadataPath)) reasons.push(`verified native capture metadata missing: ${key}`);
  else {
    if (fileSha256(captureMetadataPath) !== entry.captureMetadataSha256) reasons.push(`native capture metadata hash mismatch: ${key}`);
    const metadata = tryReadJson(captureMetadataPath);
    const [expectedWidth, expectedHeight] = String(entry.resolution || "").split("x").map(Number);
    if (!metadata || metadata.schema !== "reign-ui-window-capture-v1") reasons.push(`native capture metadata schema mismatch: ${key}`);
    else {
      if (!String(metadata.processName || "").toLowerCase().includes("bannerlord")) reasons.push(`native capture process is not Bannerlord: ${key}`);
      if (!["verified-foreground-client-area", "verified-foreground-print-window-client-area", "verified-foreground-steam-backbuffer"].includes(metadata.captureMode)) reasons.push(`native capture was not foreground-client verified: ${key}`);
      if (Number(metadata.width) !== expectedWidth || Number(metadata.height) !== expectedHeight) reasons.push(`native capture metadata dimensions mismatch: ${key}`);
      if (metadata.sha256 !== entry.screenshotSha256) reasons.push(`native capture receipt does not bind the screenshot hash: ${key}`);
      if (!samePath(metadata.path, screenshotPath)) reasons.push(`native capture receipt points to another screenshot: ${key}`);
    }
  }
  const snapshotRequired = surfaceType !== "native-augmentation";
  if (!snapshotPath || !fs.existsSync(snapshotPath)) {
    if (snapshotRequired) reasons.push(`runtime snapshot missing: ${key}`);
  } else {
    if (fileSha256(snapshotPath) !== entry.snapshotSha256) reasons.push(`runtime snapshot hash mismatch: ${key}`);
    const snapshot = tryReadJson(snapshotPath);
    if (!snapshot) reasons.push(`runtime snapshot is not valid JSON: ${key}`);
    else {
      const runtimeUiScale = Number(snapshot.uiScale);
      if (!Number.isFinite(runtimeUiScale) || runtimeUiScale <= 0) reasons.push(`runtime snapshot UIContext.CustomScale is invalid: ${key}`);
      if (!Number.isFinite(Number(entry.runtimeUiScale)) || Number(entry.runtimeUiScale) !== runtimeUiScale) reasons.push(`recorded runtime UI scale does not match the snapshot: ${key}`);
      if (Object.prototype.hasOwnProperty.call(snapshot, "supportUi")) reasons.push(`runtime snapshot contains previewer-only support UI evidence: ${key}`);
      if (!Object.prototype.hasOwnProperty.call(snapshot, "supportUiInjected") || snapshot.supportUiInjected !== false) reasons.push(`runtime snapshot does not explicitly prove supportUiInjected=false: ${key}`);
      if (snapshot.visualSupportUiInjected === true) reasons.push(`runtime snapshot reports visual support UI injection: ${key}`);
      if (sourceSha256 && snapshot.prefabSha256 !== sourceSha256) reasons.push(`runtime snapshot prefab hash is stale: ${key}`);
      if (!Array.isArray(snapshot.widgets) || !snapshot.widgets.length) reasons.push(`runtime snapshot has no widget bounds: ${key}`);
    }
  }
}

function auditNativeReviewAttestation(entry, key, reasons) {
  const attestation = entry.reviewAttestation;
  if (!attestation || typeof attestation !== "object" || Array.isArray(attestation)) {
    reasons.push(`native visual review attestation missing: ${key}`);
    return;
  }
  if (attestation.schema !== nativeReviewAttestationSchema) reasons.push(`native visual review attestation schema mismatch: ${key}`);
  if (Number(attestation.version) !== 1) reasons.push(`native visual review attestation version mismatch: ${key}`);
  if (entry.reviewed) {
    if (attestation.confirmation !== nativeReviewConfirmation) reasons.push(`native visual review confirmation mismatch: ${key}`);
    if (!attestation.reviewedUtc || !Number.isFinite(Date.parse(attestation.reviewedUtc))) reasons.push(`native visual review timestamp missing or invalid: ${key}`);
  }

  const criteria = attestation.criteria;
  if (!criteria || typeof criteria !== "object" || Array.isArray(criteria)) {
    reasons.push(`native visual review criteria missing: ${key}`);
    return;
  }
  for (const criterion of nativeReviewCriteria) {
    if (!(criterion in criteria)) reasons.push(`native visual review criterion missing (${criterion}): ${key}`);
    else if (criteria[criterion] !== true) reasons.push(`native visual review criterion not accepted (${criterion}): ${key}`);
  }
  for (const criterion of Object.keys(criteria)) {
    if (!nativeReviewCriteria.includes(criterion)) reasons.push(`unknown native visual review criterion (${criterion}): ${key}`);
  }
}

function loadAcceptance(acceptancePath) {
  return fs.existsSync(acceptancePath) ? tryReadJson(acceptancePath) : null;
}

function tryReadJson(filePath) {
  try { return readJson(filePath); } catch { return null; }
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
}

function resolveEvidencePath(root, value) {
  if (!value) return "";
  return path.isAbsolute(value) ? value : path.resolve(root, value);
}

function samePath(left, right) {
  if (!left || !right) return false;
  return path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase();
}

function normalizeOperation(value) {
  return String(value || "").trim().toLowerCase().replaceAll("-", "_");
}

function readDottedValue(value, dottedPath) {
  return String(dottedPath || "").split(".").reduce((current, segment) => {
    if (!current || typeof current !== "object" || !Object.prototype.hasOwnProperty.call(current, segment)) return undefined;
    return current[segment];
  }, value);
}

function caseKey(resolution, uiScale) {
  return `${resolution}@${Number(uiScale).toFixed(2)}`;
}

function combinedFileSha256(filePaths) {
  const hash = createHash("sha256");
  for (const filePath of [...filePaths].sort((left, right) => left.localeCompare(right))) {
    hash.update(path.relative(moduleRoot, filePath).replaceAll("\\", "/"));
    hash.update("\0");
    hash.update(fs.readFileSync(filePath));
    hash.update("\0");
  }
  return hash.digest("hex");
}

function fileSha256(filePath) {
  return createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function resolveArgument(name, fallback) {
  const value = argumentValue(name);
  return value ? path.resolve(value) : fallback;
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] || "" : "";
}
