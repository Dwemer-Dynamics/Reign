import { analyzeDiagnostics } from "./diagnostics.js";
import { CalibrationEditor } from "./calibration-editor.js";
import { CodexPreviewChat } from "./codex-chat.js";
import { GauntletRenderer } from "./renderer.js";
import { loadNativeAugmentation } from "./native-augmentations.js";
import {
  applyCourtState,
  ASSET_BY_IMAGE_ID,
  ASSET_BY_WIDGET_ID,
  getNativeAugmentationSampleData,
  getSampleDataForPrefab,
  SAMPLE_DATA
} from "./sample-data.js";
import { loadRuntimeSpriteState } from "./sprite-runtime.js";

const elements = {
  stage: document.getElementById("stage"),
  stageContent: document.getElementById("stageContent"),
  stageWrap: document.getElementById("stageWrap"),
  stageShell: document.getElementById("stageShell"),
  viewportReadout: document.getElementById("viewportReadout"),
  viewportBadge: document.getElementById("viewportBadge"),
  viewportSelect: document.getElementById("viewportSelect"),
  uiScaleSelect: document.getElementById("uiScaleSelect"),
  runtimeScaleSelect: document.getElementById("runtimeScaleSelect"),
  courtStateSelect: document.getElementById("courtStateSelect"),
  fitToggle: document.getElementById("fitToggle"),
  outlineToggle: document.getElementById("outlineToggle"),
  labelToggle: document.getElementById("labelToggle"),
  prefabGrid: document.getElementById("prefabGrid"),
  prefabCatalogStatus: document.getElementById("prefabCatalogStatus"),
  augmentationGrid: document.getElementById("augmentationGrid"),
  augmentationCatalogStatus: document.getElementById("augmentationCatalogStatus"),
  fileInput: document.getElementById("fileInput"),
  xmlPaste: document.getElementById("xmlPaste"),
  loadPaste: document.getElementById("loadPaste"),
  fileTitle: document.getElementById("fileTitle"),
  fileMeta: document.getElementById("fileMeta"),
  status: document.getElementById("status"),
  statusDot: document.getElementById("statusDot"),
  sourceSyncStatus: document.getElementById("sourceSyncStatus"),
  reloadCurrentSource: document.getElementById("reloadCurrentSource"),
  nativeAuthorityToggle: document.getElementById("nativeAuthorityToggle"),
  nativeDataToggle: document.getElementById("nativeDataToggle"),
  nativeCaptureToggle: document.getElementById("nativeCaptureToggle"),
  refreshNativeEvidence: document.getElementById("refreshNativeEvidence"),
  nativeEvidenceStatus: document.getElementById("nativeEvidenceStatus"),
  nativeParityStatus: document.getElementById("nativeParityStatus"),
  nativeParityDetail: document.getElementById("nativeParityDetail"),
  refreshNativeParity: document.getElementById("refreshNativeParity"),
  selectionTitle: document.getElementById("selectionTitle"),
  selectionSubtitle: document.getElementById("selectionSubtitle"),
  selectionToken: document.getElementById("selectionToken"),
  copyToken: document.getElementById("copyToken"),
  copyFeedback: document.getElementById("copyFeedback"),
  inspectorEmpty: document.getElementById("inspectorEmpty"),
  inspectorDetails: document.getElementById("inspectorDetails"),
  identityDetails: document.getElementById("identityDetails"),
  boundProperties: document.getElementById("boundProperties"),
  attributeList: document.getElementById("attributeList"),
  issueCount: document.getElementById("issueCount"),
  missingCount: document.getElementById("missingCount"),
  assetCount: document.getElementById("assetCount"),
  overflowCount: document.getElementById("overflowCount"),
  clippingCount: document.getElementById("clippingCount"),
  fitCount: document.getElementById("fitCount"),
  diagnosticList: document.getElementById("diagnosticList")
};

const renderer = new GauntletRenderer(elements.stageContent, ASSET_BY_IMAGE_ID, ASSET_BY_WIDGET_ID);
const calibrationEditor = new CalibrationEditor({
  stage: elements.stage,
  stageContent: elements.stageContent,
  renderer,
  requestRender: () => {
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
  },
  getSurfaceState: () => ({
    viewport: elements.viewportSelect.value,
    uiScale: getUiScale(),
    runtimeScale: getRuntimeScaleProfile()
  }),
  setStatus,
  onSourceApplied: async () => {
    await reloadCurrentSource({ reason: "applied", refreshAssets: true });
    await loadNativeParityReadiness();
  }
});
const codexChat = new CodexPreviewChat({
  getContext: buildCodexContext,
  onTurnComplete: async () => {
    await checkCurrentSource();
    await calibrationEditor.refreshInstallStatus();
    await loadNativeParityReadiness();
  }
});
const VIEWPORT_PREFERENCE_KEY = "reign-gauntlet-preview-viewport-v2";
const UI_SCALE_PREFERENCE_KEY = "reign-gauntlet-preview-ui-scale";
const RUNTIME_SCALE_PREFERENCE_KEY = "reign-gauntlet-preview-runtime-scale";
const GAUNTLET_REFERENCE_WIDTH = 1920;
const GAUNTLET_REFERENCE_HEIGHT = 1080;
const GAUNTLET_NARROW_ASPECT_TOLERANCE = 0.98;
const SOURCE_SYNC_INTERVAL_MS = 1500;
const PREFAB_RUNTIME_SCALE_PROFILES = new Map([
  ["ReignSocialEventScreen.xml", "reference-locked"],
  ["ReignCastleLayoutScreen.xml", "reference-locked"]
]);
let currentXmlText = "";
let currentFileName = "";
let currentLabel = "";
let currentPreset = "";
let currentInterfaceId = "";
let currentPrefabPath = "";
let currentIssues = [];
let selectedElement = null;
let selectedKey = "";
let lastSelectedMetadata = null;
let renderVersion = 0;
let resizeTimer = 0;
let clickCycle = { x: -100, y: -100, signature: "", index: -1 };
let runtimeSpriteState = null;
let sourceSyncTimer = 0;
let sourceSyncInFlight = false;
let sourceOutOfDate = false;
let currentNativeEvidence = null;
let nativeEvidenceRequest = 0;
let currentNativeAugmentation = null;
let currentNativeParityReadiness = null;

bootstrap();

window.__reignPreviewer = {
  renderer,
  calibrationEditor,
  codexChat,
  loadPrefab,
  reloadCurrentSource,
  checkCurrentSource,
  loadNativeEvidence,
  getState: () => ({
    fileName: currentFileName,
    label: currentLabel,
    preset: currentPreset,
    interfaceId: currentInterfaceId,
    catalogEntries: elements.prefabGrid.querySelectorAll("[data-prefab]").length,
    augmentationEntries: elements.augmentationGrid.querySelectorAll("[data-augmentation]").length,
    nativeAugmentation: currentNativeAugmentation,
    nativeParity: currentNativeParityReadiness ? {
      providerPreviewReady: currentNativeParityReadiness.providerPreviewReady,
      installedStandaloneReady: currentNativeParityReadiness.installedStandaloneReady,
      nativeReady: currentNativeParityReadiness.nativeReady,
      counts: currentNativeParityReadiness.counts,
      generatedUtc: currentNativeParityReadiness.generatedUtc
    } : null,
    renderedNodes: renderer.renderedElements.length,
    selectedToken: lastSelectedMetadata?.token || "",
    viewport: elements.viewportSelect.value,
    uiScale: Number(elements.uiScaleSelect.value),
    runtimeScale: getRuntimeScaleProfile(),
    effectiveRenderScale: getEffectiveRenderScale(...getViewport()),
    logicalViewport: {
      width: getViewport()[0] / getEffectiveRenderScale(...getViewport()),
      height: getViewport()[1] / getEffectiveRenderScale(...getViewport())
    },
    nativeEvidence: {
      available: Boolean(currentNativeEvidence?.available),
      movieName: currentNativeEvidence?.movieName || "",
      snapshotName: currentNativeEvidence?.snapshotName || "",
      captureName: currentNativeEvidence?.captureName || "",
      captureAvailable: Boolean(currentNativeEvidence?.captureAvailable),
      authorityEnabled: elements.nativeAuthorityToggle.checked,
      capturedStateEnabled: elements.nativeDataToggle.checked,
      captureVisible: elements.nativeCaptureToggle.checked,
      effectiveScale: getNativeEvidenceScale(...getViewport())
    },
    sourceSync: {
      path: currentPrefabPath,
      outOfDate: sourceOutOfDate,
      tracked: Boolean(currentPrefabPath),
      sourceLength: currentXmlText.length,
      message: elements.sourceSyncStatus.textContent
    },
    runtimeSprites: {
      ready: Boolean(runtimeSpriteState?.ready),
      parts: runtimeSpriteState?.parts?.size || 0,
      problems: runtimeSpriteState?.problems?.length || 0,
      message: runtimeSpriteState?.statusMessage || "Not validated"
    },
    calibration: calibrationEditor.getState(),
    diagnostics: currentIssues.reduce((counts, issue) => {
      counts[issue.kind] = (counts[issue.kind] || 0) + 1;
      return counts;
    }, {}),
    diagnosticDetails: currentIssues.map((issue) => ({
      kind: issue.kind,
      message: issue.message,
      fileName: issue.fileName || currentFileName,
      line: issue.line ?? null,
      path: issue.path || "",
      label: issue.label || ""
    }))
  }),
  selectByPath: (path) => {
    const match = renderer.renderedElements.find((element) => element.__gauntlet?.path === path);
    if (match) selectElement(match);
    return Boolean(match);
  }
};

async function bootstrap() {
  bindControls();
  if (new URLSearchParams(window.location.search).get("codex") === "off") {
    codexChat.disableForEvidence();
  } else {
    void codexChat.start();
  }
  restoreTestSurfacePreferences();
  applyUrlTestSurfaceOverrides();
  applyCourtState(elements.courtStateSelect.value);
  applyStageGeometry();
  setStatus("Validating browser sprites against Bannerlord runtime sheets...");
  runtimeSpriteState = await loadRuntimeSpriteState();
  renderer.setRuntimeSpriteState(runtimeSpriteState);
  await hydratePrefabCatalog();
  await hydrateNativeAugmentationCatalog();
  const query = new URLSearchParams(window.location.search);
  if (query.get("codex") === "off") {
    elements.nativeParityStatus.textContent = "Disabled during provider-free capture audits.";
    elements.nativeParityStatus.className = "";
    elements.nativeParityDetail.textContent = "Use an ordinary preview session to recompute installed and native readiness.";
  } else {
    await loadNativeParityReadiness();
  }
  const requestedInterface = query.get("interface")?.trim().toLowerCase();
  const requestedPrefab = query.get("prefab")?.trim().toLowerCase();
  const requestedAugmentation = query.get("augmentation")?.trim().toLowerCase();
  const requestedAugmentationButton = requestedAugmentation
    ? [...elements.augmentationGrid.querySelectorAll("[data-augmentation]")].find((button) => requestedAugmentation === button.dataset.augmentation.toLowerCase() || requestedAugmentation === button.textContent.trim().toLowerCase())
    : null;
  const requestedInterfaceButton = requestedInterface
    ? [...elements.prefabGrid.querySelectorAll("[data-interface]")].find((button) => requestedInterface === button.dataset.interface.toLowerCase())
    : null;
  const requestedButton = requestedInterfaceButton || (requestedPrefab
    ? [...elements.prefabGrid.querySelectorAll("[data-prefab]")].find((button) => {
        const fileName = button.dataset.prefab.split("/").pop().replace(/\.xml$/i, "").toLowerCase();
        const label = button.textContent.trim().toLowerCase();
        return requestedPrefab === fileName || requestedPrefab === label;
      })
    : null);
  if (requestedAugmentationButton) {
    await loadNativeAugmentationTarget(JSON.parse(requestedAugmentationButton.dataset.entry));
  } else if (requestedButton) {
    await loadPrefab(
      requestedButton.dataset.prefab,
      requestedButton.dataset.label || requestedButton.textContent.trim(),
      requestedButton.dataset.preset || "",
      requestedButton.dataset.interface || ""
    );
  } else {
    await loadPrefab("../../GUI/Prefabs/ReignSocialEventScreen.xml", "Social Event", "social", "social-event");
  }
  startSourceMonitor();
}

function bindControls() {
  elements.prefabGrid.addEventListener("click", (event) => {
    const button = event.target.closest("[data-prefab]");
    if (!button || !elements.prefabGrid.contains(button)) return;
    loadPrefab(button.dataset.prefab, button.dataset.label || button.textContent.trim(), button.dataset.preset || "", button.dataset.interface || "");
  });
  elements.augmentationGrid.addEventListener("click", (event) => {
    const button = event.target.closest("[data-augmentation]");
    if (!button || !elements.augmentationGrid.contains(button)) return;
    void loadNativeAugmentationTarget(JSON.parse(button.dataset.entry));
  });

  elements.fileInput.addEventListener("change", async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;
    currentPreset = "";
    currentInterfaceId = "";
    setTrackedSource("");
    markActivePrefab("", "");
    renderXml(await file.text(), file.name, file.name);
    event.target.value = "";
  });

  elements.loadPaste.addEventListener("click", () => {
    const text = elements.xmlPaste.value.trim();
    if (!text) {
      setStatus("Paste XML before rendering.", "bad");
      return;
    }
    currentPreset = "";
    currentInterfaceId = "";
    setTrackedSource("");
    markActivePrefab("", "");
    renderXml(text, "PastedPrefab.xml", "Pasted XML");
  });

  elements.viewportSelect.addEventListener("change", async () => {
    persistTestSurfacePreferences();
    if (currentPrefabPath) await loadNativeEvidence(currentFileName);
    applyStageGeometry();
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
  });

  elements.uiScaleSelect.addEventListener("change", async () => {
    persistTestSurfacePreferences();
    if (currentPrefabPath) await loadNativeEvidence(currentFileName);
    applyStageGeometry();
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
  });

  elements.runtimeScaleSelect.addEventListener("change", () => {
    persistTestSurfacePreferences();
    applyStageGeometry();
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
  });

  elements.nativeAuthorityToggle.addEventListener("change", () => {
    applyStageGeometry();
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
    updateNativeEvidenceStatus();
  });
  elements.nativeDataToggle.addEventListener("change", () => {
    syncRendererRuntimeSnapshot();
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
    updateNativeEvidenceStatus();
  });
  elements.nativeCaptureToggle.addEventListener("change", () => {
    calibrationEditor.setReferenceVisible(elements.nativeCaptureToggle.checked);
    updateNativeEvidenceStatus();
  });
  elements.refreshNativeEvidence.addEventListener("click", async () => {
    await loadNativeEvidence(currentFileName, { force: true });
    if (currentXmlText) await renderXml(currentXmlText, currentFileName, currentLabel, true);
  });
  elements.refreshNativeParity.addEventListener("click", () => loadNativeParityReadiness());

  elements.courtStateSelect.addEventListener("change", () => {
    applyCourtState(elements.courtStateSelect.value);
    if (currentXmlText) renderXml(currentXmlText, currentFileName, currentLabel, true);
  });

  elements.fitToggle.addEventListener("change", applyFit);
  elements.outlineToggle.addEventListener("change", applyDebugClasses);
  elements.labelToggle.addEventListener("change", applyDebugClasses);
  elements.copyToken.addEventListener("click", copySelectionToken);
  elements.reloadCurrentSource.addEventListener("click", () => reloadCurrentSource({ reason: "manual", refreshAssets: true }));

  elements.stage.addEventListener("click", handleStageClick, true);
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && selectedElement) clearSelection();
  });

  window.addEventListener("resize", () => {
    applyFit();
    window.clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(runDiagnostics, 140);
  });

  ["dragenter", "dragover"].forEach((name) => {
    document.addEventListener(name, (event) => {
      event.preventDefault();
      document.body.classList.add("dragging");
    });
  });
  ["dragleave", "drop"].forEach((name) => {
    document.addEventListener(name, (event) => {
      event.preventDefault();
      document.body.classList.remove("dragging");
    });
  });
  document.addEventListener("drop", async (event) => {
    const file = event.dataTransfer?.files?.[0];
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".xml")) {
      setStatus(`Cannot preview ${file.name}; choose an XML prefab.`, "bad");
      return;
    }
    currentPreset = "";
    currentInterfaceId = "";
    setTrackedSource("");
    markActivePrefab("", "");
    renderXml(await file.text(), file.name, file.name);
  });
}

async function loadPrefab(path, label, preset = "", interfaceId = "") {
  try {
    setStatus(`Loading ${path}…`);
    const text = await fetchPrefabText(path);
    const fileName = path.split("/").at(-1) || label || "ReignPrefab.xml";
    currentPreset = preset;
    currentInterfaceId = interfaceId;
    setTrackedSource(path);
    await loadNativeEvidence(fileName);
    await renderXml(text, fileName, label || fileName);
    markActivePrefab(path, preset);
    updateSourceSyncStatus(`Source synced · ${shortSourcePath(path)}`, "ok");
  } catch (error) {
    setStatus(`Could not load ${path}.\nStart the local preview server or choose the XML file directly.\n${error.message}`, "bad");
  }
}

async function loadNativeAugmentationTarget(entry) {
  try {
    setStatus(`Composing installed ${entry.target} with current Reign patch source…`);
    const composition = await loadNativeAugmentation(entry);
    currentPreset = "";
    currentInterfaceId = entry.id || "";
    setTrackedSource("");
    currentNativeAugmentation = {
      id: entry.id,
      label: entry.label,
      target: entry.target,
      basePrefab: composition.basePrefab,
      baseSha256: composition.baseSha256,
      patches: composition.patches.map((patch) => ({ patchClass: patch.patchClass, source: patch.source, line: patch.line, xpath: patch.xpath, type: patch.type, index: patch.index }))
    };
    const virtualFileName = `NativeAugmentation-${entry.target}.xml`;
    await renderXml(composition.xml, virtualFileName, `${entry.label} · native composed`);
    markActivePrefab("", "");
    markActiveAugmentation(entry.id);
    updateNativeEvidenceStatus(`Composed from installed ${composition.basePrefab} · ${composition.baseSha256.slice(0, 12)} · ${composition.patches.length} current Reign patch application${composition.patches.length === 1 ? "" : "s"}. Native screenshot parity remains required.`);
    updateSourceSyncStatus(`Reign patch source: ${[...new Set(composition.patches.map((patch) => patch.source))].join(", ")} · use the Codex dock to edit; XML apply is intentionally disabled.`);
  } catch (error) {
    setStatus(`Could not compose ${entry.target}: ${error.message}`, "bad");
  }
}

async function loadNativeParityReadiness() {
  elements.refreshNativeParity.disabled = true;
  elements.nativeParityStatus.textContent = "Recomputing native parity surfaces…";
  elements.nativeParityStatus.className = "";
  try {
    const response = await fetch("api/native-parity-readiness", { cache: "no-store" });
    const report = await response.json();
    if (!response.ok || report.ok === false) throw new Error(report.error || `Parity readiness request failed (${response.status}).`);
    currentNativeParityReadiness = report;
    const counts = report.counts || {};
    elements.nativeParityStatus.textContent = `Provider ${counts.providerPreviewPassed || 0}/${counts.totalSurfaces || 0} · installed ${counts.installedExact || 0}/${counts.standaloneInterfaceStates || 0} · native ${counts.nativeAccepted || 0}/${counts.totalSurfaces || 0}`;
    elements.nativeParityStatus.className = report.nativeReady
      ? "ok"
      : (Number(counts.structuralErrors || 0) > 0 || !report.providerPreviewReady ? "bad" : "pending");
    const deploymentPending = (report.surfaces || [])
      .filter((surface) => (surface.reasons || []).some((reason) => reason.includes("installed prefab")))
      .map((surface) => surface.id);
    const nativePending = (report.surfaces || []).filter((surface) => !surface.nativeAccepted).map((surface) => surface.id);
    elements.nativeParityDetail.textContent = report.nativeReady
      ? `All ${counts.totalSurfaces} surfaces have current reviewed native evidence.`
      : [
          deploymentPending.length ? `Install pending: ${deploymentPending.join(", ")}.` : "Installed standalone hashes are current.",
          `Native evidence pending: ${nativePending.length}/${counts.totalSurfaces || 0}.`,
          `Report ${new Date(report.generatedUtc).toLocaleString()}.`
        ].join(" ");
    elements.nativeParityDetail.title = (report.surfaces || [])
      .filter((surface) => !surface.nativeAccepted)
      .map((surface) => `${surface.id}: ${(surface.reasons || []).join("; ")}`)
      .join("\n");
    codexChat.updateSelectionContext(buildCodexContext());
  } catch (error) {
    currentNativeParityReadiness = null;
    elements.nativeParityStatus.textContent = "Parity readiness unavailable.";
    elements.nativeParityStatus.className = "bad";
    elements.nativeParityDetail.textContent = error.message;
    elements.nativeParityDetail.title = error.message;
  } finally {
    elements.refreshNativeParity.disabled = false;
  }
}

async function loadNativeEvidence(fileName, { force = false } = {}) {
  const movieName = String(fileName || "").replace(/\.xml$/i, "");
  const request = ++nativeEvidenceRequest;
  currentNativeEvidence = null;
  renderer.setRuntimeSnapshot(null);
  calibrationEditor.clearRuntimeSnapshot();
  calibrationEditor.clearReference();
  elements.nativeDataToggle.checked = false;
  elements.nativeDataToggle.disabled = true;
  elements.nativeCaptureToggle.checked = false;
  if (!/^[A-Za-z][A-Za-z0-9_-]+$/.test(movieName)) {
    updateNativeEvidenceStatus("This document is not connected to a cataloged Reign movie.");
    return;
  }

  updateNativeEvidenceStatus(`Finding latest engine evidence for ${movieName}…`);
  try {
    const url = new URL("api/native-evidence", document.baseURI);
    url.searchParams.set("movie", movieName);
    url.searchParams.set("resolution", elements.viewportSelect.value);
    url.searchParams.set("uiScale", String(getUiScale()));
    if (force) url.searchParams.set("revision", String(Date.now()));
    const response = await fetch(url, { cache: "no-store" });
    const evidence = await response.json();
    if (!response.ok) throw new Error(evidence.error || `${response.status} ${response.statusText}`);
    if (request !== nativeEvidenceRequest) return;
    currentNativeEvidence = evidence.available ? evidence : null;
    if (!currentNativeEvidence?.snapshot) {
      updateNativeEvidenceStatus(`No engine snapshot exists for ${movieName}. Capture one in Bannerlord before trusting browser geometry.`);
      return;
    }

    const snapshotSourceMismatch = Boolean(currentNativeEvidence.snapshotPrefabSha256) && !currentNativeEvidence.snapshotMatchesSource;
    elements.nativeDataToggle.disabled = snapshotSourceMismatch;
    if (snapshotSourceMismatch) elements.nativeDataToggle.checked = false;
    syncRendererRuntimeSnapshot();
    calibrationEditor.setRuntimeSnapshot(currentNativeEvidence.snapshot, { announce: false });
    if (currentNativeEvidence.captureUrl) {
      calibrationEditor.setReferenceUrl(currentNativeEvidence.captureUrl, { visible: false, source: "native-capture" });
    }
    applyStageGeometry();
    updateNativeEvidenceStatus();
  } catch (error) {
    if (request !== nativeEvidenceRequest) return;
    currentNativeEvidence = null;
    renderer.setRuntimeSnapshot(null);
    elements.nativeDataToggle.checked = false;
    elements.nativeDataToggle.disabled = true;
    updateNativeEvidenceStatus(`Native evidence unavailable: ${error.message}`);
  }
}

function syncRendererRuntimeSnapshot() {
  const snapshot = elements.nativeDataToggle.checked ? currentNativeEvidence?.snapshot : null;
  renderer.setRuntimeSnapshot(snapshot || null);
}

function updateNativeEvidenceStatus(message = "") {
  if (message) {
    elements.nativeEvidenceStatus.textContent = message;
    return;
  }
  const snapshot = currentNativeEvidence?.snapshot;
  if (!snapshot) {
    elements.nativeEvidenceStatus.textContent = "No native evidence loaded.";
    return;
  }
  const [width, height] = getViewport();
  const exact = getNativeEvidenceScale(width, height);
  const scaleText = exact ? `exact engine scale ${formatPercent(exact)}` : `evidence is ${snapshot.physicalWidth}×${snapshot.physicalHeight} at ${formatPercent(snapshot.uiScale)}`;
  const contentText = elements.nativeDataToggle.checked ? "captured state text active" : "coherent sample-state content";
  const sourceText = currentNativeEvidence.snapshotPrefabSha256
    ? (currentNativeEvidence.snapshotMatchesSource ? "snapshot XML hash verified" : "snapshot XML differs from workspace source")
    : "legacy snapshot has no XML hash";
  const captureText = currentNativeEvidence.captureAvailable
    ? `${currentNativeEvidence.captureName}${elements.nativeCaptureToggle.checked ? " shown" : " attached"}`
    : "no game capture attached";
  elements.nativeEvidenceStatus.textContent = `${currentNativeEvidence.snapshotName} · ${scaleText} · ${contentText} · ${sourceText} · ${captureText}`;
}

async function fetchPrefabText(path) {
  const url = new URL(path, document.baseURI);
  url.searchParams.set("reignPreviewRevision", String(Date.now()));
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.text();
}

function setTrackedSource(path) {
  currentPrefabPath = path || "";
  currentNativeAugmentation = null;
  sourceOutOfDate = false;
  calibrationEditor.setSourceOutOfDate(false);
  elements.reloadCurrentSource.disabled = !currentPrefabPath;
  if (!currentPrefabPath) {
    nativeEvidenceRequest += 1;
    currentNativeEvidence = null;
    renderer.setRuntimeSnapshot(null);
    calibrationEditor.clearRuntimeSnapshot();
    calibrationEditor.clearReference();
    elements.nativeDataToggle.checked = false;
    elements.nativeDataToggle.disabled = true;
    elements.nativeCaptureToggle.checked = false;
    updateNativeEvidenceStatus("Local or pasted XML has no automatic native evidence.");
    updateSourceSyncStatus("Local or pasted XML is not connected to a workspace source.");
  }
}

function startSourceMonitor() {
  window.clearInterval(sourceSyncTimer);
  sourceSyncTimer = window.setInterval(() => checkCurrentSource(), SOURCE_SYNC_INTERVAL_MS);
  window.addEventListener("focus", () => checkCurrentSource());
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) checkCurrentSource();
  });
}

async function checkCurrentSource() {
  if (!currentPrefabPath || sourceSyncInFlight || document.hidden) return;
  sourceSyncInFlight = true;
  const path = currentPrefabPath;
  try {
    const latestText = await fetchPrefabText(path);
    if (path !== currentPrefabPath) return;
    if (latestText === currentXmlText) {
      if (!sourceOutOfDate) updateSourceSyncStatus(`Source synced · ${shortSourcePath(path)}`, "ok");
      return;
    }

    if (calibrationEditor.hasPendingEdits()) {
      sourceOutOfDate = true;
      calibrationEditor.setSourceOutOfDate(true);
      updateSourceSyncStatus("Source changed on disk. Reload it to rebase and review your staged edits before applying.", "bad");
      return;
    }

    currentXmlText = latestText;
    await renderXml(latestText, currentFileName, currentLabel, true);
    updateSourceSyncStatus(`Auto-reloaded latest source · ${shortSourcePath(path)}`, "ok");
  } catch (error) {
    updateSourceSyncStatus(`Source sync failed: ${error.message}`, "bad");
  } finally {
    sourceSyncInFlight = false;
  }
}

async function reloadCurrentSource({ reason = "manual", refreshAssets = true } = {}) {
  if (!currentPrefabPath) return;
  if (sourceSyncInFlight) return;
  sourceSyncInFlight = true;
  const path = currentPrefabPath;
  elements.reloadCurrentSource.disabled = true;
  updateSourceSyncStatus("Reloading workspace source and runtime assets…");
  try {
    const latestText = await fetchPrefabText(path);
    if (refreshAssets) {
      runtimeSpriteState = await loadRuntimeSpriteState();
      renderer.setRuntimeSpriteState(runtimeSpriteState);
    }
    if (path !== currentPrefabPath) return;
    sourceOutOfDate = false;
    calibrationEditor.setSourceOutOfDate(false);
    await renderXml(latestText, currentFileName, currentLabel, true);
    const prefix = reason === "applied" ? "Applied changes and reloaded" : "Reloaded";
    updateSourceSyncStatus(`${prefix} latest source · ${shortSourcePath(path)}`, "ok");
  } catch (error) {
    updateSourceSyncStatus(`Reload failed: ${error.message}`, "bad");
    throw error;
  } finally {
    sourceSyncInFlight = false;
    elements.reloadCurrentSource.disabled = !currentPrefabPath;
  }
}

function updateSourceSyncStatus(message, state = "") {
  elements.sourceSyncStatus.textContent = message;
  elements.sourceSyncStatus.className = `source-sync-status ${state}`.trim();
}

function shortSourcePath(path) {
  return path.split("/").at(-1) || path;
}

async function hydratePrefabCatalog() {
  try {
    const response = await fetch("api/reign-prefabs", { cache: "no-store" });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    const catalog = await response.json();
    const prefabs = Array.isArray(catalog) ? catalog : [...(catalog.prefabs || []), ...(catalog.auxiliaryPrefabs || [])];
    if (!Array.isArray(prefabs)) throw new Error("Catalog response did not contain a prefab list.");

    const existingFiles = new Set(
      [...elements.prefabGrid.querySelectorAll("[data-prefab]")]
        .map((button) => button.dataset.prefab.split("/").at(-1)?.toLowerCase())
        .filter(Boolean)
    );

    for (const prefab of prefabs) {
      const fileName = String(prefab.fileName || "");
      if (!fileName || existingFiles.has(fileName.toLowerCase())) continue;
      const button = document.createElement("button");
      button.className = "prefab-button";
      button.dataset.prefab = prefab.path || `../../GUI/Prefabs/${fileName}`;
      button.dataset.label = prefab.label || humanizePrefabName(fileName);
      button.textContent = button.dataset.label;
      elements.prefabGrid.append(button);
      existingFiles.add(fileName.toLowerCase());
    }

    // The authoritative catalog may add states sharing an existing movie. File-name
    // deduplication alone omitted those states and made direct audit URLs open a default.
    const stateResponse = await fetch("calibration/ui-catalog.json", { cache: "no-store" });
    if (!stateResponse.ok) throw new Error(`State catalog: ${stateResponse.status}`);
    const stateCatalog = await stateResponse.json();
    const existingIds = new Set([...elements.prefabGrid.querySelectorAll("[data-interface]")]
      .map((button) => button.dataset.interface));
    for (const entry of stateCatalog.interfaces || []) {
      for (const state of [entry, ...(entry.previewStates || []).map((variant) => ({ ...entry, ...variant }))]) {
        if (!state.id || !state.prefab || existingIds.has(state.id)) continue;
        // Discovery may already have added this new movie before its catalog ID
        // was known. Adopt that button instead of creating a duplicate base state.
        const stateFileName = state.prefab.split("/").at(-1)?.toLowerCase();
        const button = [...elements.prefabGrid.querySelectorAll("[data-prefab]:not([data-interface])")]
          .find((candidate) => candidate.dataset.prefab.split("/").at(-1)?.toLowerCase() === stateFileName)
          || document.createElement("button");
        button.className = "prefab-button";
        button.dataset.interface = state.id;
        button.dataset.prefab = `../../${state.prefab}`;
        button.dataset.label = state.label || humanizePrefabName(state.prefab.split("/").at(-1));
        button.dataset.preset = state.variant || "";
        button.textContent = button.dataset.label;
        elements.prefabGrid.append(button);
        existingIds.add(state.id);
      }
    }

    const runtimeVariantCount = countRuntimeVariants();
    const supportCount = prefabs.filter((prefab) => prefab.kind === "auxiliary").length;
    const standaloneCount = prefabs.length - supportCount;
    elements.prefabCatalogStatus.textContent = `${standaloneCount} standalone interfaces · ${runtimeVariantCount} runtime variant${runtimeVariantCount === 1 ? "" : "s"} · ${supportCount} support interface${supportCount === 1 ? "" : "s"} · catalog synced`;
  } catch (error) {
    const runtimeVariantCount = countRuntimeVariants();
    const fallbackCount = elements.prefabGrid.querySelectorAll("[data-prefab]").length - runtimeVariantCount;
    elements.prefabCatalogStatus.textContent = `${fallbackCount} standalone interfaces · ${runtimeVariantCount} runtime variant${runtimeVariantCount === 1 ? "" : "s"} · static fallback`;
    console.warn("Could not refresh the Reign prefab catalog.", error);
  }
}

async function hydrateNativeAugmentationCatalog() {
  try {
    const response = await fetch("calibration/ui-catalog.json", { cache: "no-store" });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    const catalog = await response.json();
    const declaration = catalog.nativeAugmentations || {};
    const targets = Array.isArray(declaration.targets) ? declaration.targets : [];
    elements.augmentationGrid.replaceChildren();
    for (const entry of targets) {
      const button = document.createElement("button");
      button.className = "prefab-button augmentation";
      button.dataset.augmentation = entry.id;
      button.dataset.entry = JSON.stringify(entry);
      button.textContent = entry.label;
      elements.augmentationGrid.append(button);
    }
    elements.augmentationCatalogStatus.textContent = `${targets.length} native interfaces · ${Number(declaration.patchApplicationCount) || 0} current Reign patch applications · source discovered`;
  } catch (error) {
    elements.augmentationGrid.replaceChildren();
    elements.augmentationCatalogStatus.textContent = `Native augmentation catalog unavailable: ${error.message}`;
  }
}

function countRuntimeVariants() {
  const buttons = [...elements.prefabGrid.querySelectorAll("[data-prefab]")];
  const uniqueSources = new Set(buttons.map((button) => button.dataset.prefab.toLowerCase()));
  return Math.max(0, buttons.length - uniqueSources.size);
}

function humanizePrefabName(fileName) {
  return fileName
    .replace(/\.xml$/i, "")
    .replace(/^Reign/, "")
    .replace(/Screen$/, "")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .trim() || fileName;
}

async function renderXml(xmlText, fileName, label, preserveStatus = false) {
  const version = ++renderVersion;
  const isSameDocument = currentFileName === fileName;
  const priorSelectionKey = isSameDocument ? selectedKey : "";
  if (!isSameDocument) clearSelection();

  currentXmlText = xmlText;
  currentFileName = fileName;
  currentLabel = label || fileName;
  calibrationEditor.setDocument(fileName, xmlText);
  applyStageGeometry();
  const start = performance.now();

  try {
    updateViewportDerivedSampleData(fileName);
    const effectiveXmlText = calibrationEditor.applyEdits(xmlText, fileName);
    const sampleData = currentNativeAugmentation
      ? { ...getSampleDataForPrefab(fileName, currentPreset), ...getNativeAugmentationSampleData(currentNativeAugmentation.target) }
      : getSampleDataForPrefab(fileName, currentPreset);
    const result = renderer.render(effectiveXmlText, fileName, sampleData);
    applyDebugClasses();
    await nextFrame();
    await nextFrame();
    if (version !== renderVersion) return;

    applyFit();
    calibrationEditor.refresh();
    currentIssues = analyzeDiagnostics(renderer, elements.stageContent);
    renderDiagnostics(currentIssues);

    elements.fileTitle.textContent = currentLabel;
    const elapsed = performance.now() - start;
    const [width, height] = getViewport();
    const nativeScale = getNativeGauntletScale(width, height);
    const runtimeProfile = getRuntimeScaleProfile(fileName);
    const renderScale = getEffectiveRenderScale(width, height, runtimeProfile);
    const logicalWidth = width / renderScale;
    const logicalHeight = height / renderScale;
    elements.fileMeta.textContent = `${fileName} · ${result.nodeCount} rendered objects · ${xmlText.length.toLocaleString()} characters · ${width}×${height} physical / ${formatDimension(logicalWidth)}×${formatDimension(logicalHeight)} logical · native ${formatPercent(nativeScale)} · ${formatRuntimeScaleProfile(runtimeProfile)} · UI ${formatPercent(getUiScale())} · effective ${formatPercent(renderScale)} · ${elapsed.toFixed(1)} ms`;

    if (priorSelectionKey) {
      const replacement = renderer.elementBySelectionKey.get(priorSelectionKey);
      if (replacement) selectElement(replacement, null, true);
      else preserveStaleSelection();
    }

    if (!preserveStatus) {
      const observationText = currentIssues.length ? ` · ${currentIssues.length} diagnostic observation${currentIssues.length === 1 ? "" : "s"}` : " · no diagnostic observations";
      setStatus(`Loaded ${currentLabel}${observationText}.`, currentIssues.length ? "" : "ok");
    }
    codexChat.updateSelectionContext(buildCodexContext());
  } catch (error) {
    elements.stageContent.innerHTML = '<div class="drop-hint"><span>XML parse error</span><small>Correct the source and render again.</small></div>';
    elements.fileTitle.textContent = label || fileName || "XML parse error";
    elements.fileMeta.textContent = error.message;
    currentIssues = [];
    calibrationEditor.clearSelection();
    renderDiagnostics([]);
    setStatus(error.message, "bad");
  }
}

function applyStageGeometry() {
  const [width, height] = getViewport();
  const uiScale = getUiScale();
  const nativeScale = getNativeGauntletScale(width, height);
  const runtimeProfile = getRuntimeScaleProfile();
  const renderScale = getEffectiveRenderScale(width, height, runtimeProfile);
  const logicalWidth = width / renderScale;
  const logicalHeight = height / renderScale;
  elements.stage.style.width = `${width}px`;
  elements.stage.style.height = `${height}px`;
  elements.stageContent.style.width = `${logicalWidth}px`;
  elements.stageContent.style.height = `${logicalHeight}px`;
  elements.stageContent.style.transform = renderScale === 1 ? "none" : `scale(${renderScale})`;
  elements.viewportReadout.textContent = `${width} × ${height} · logical ${formatDimension(logicalWidth)} × ${formatDimension(logicalHeight)} · native ${formatPercent(nativeScale)} · ${formatRuntimeScaleProfile(runtimeProfile)} · UI ${formatPercent(uiScale)} · effective ${formatPercent(renderScale)}`;
  elements.viewportBadge.textContent = `${width} × ${height}`;
  updateNativeEvidenceStatus();
  applyFit();
  calibrationEditor.refresh();
}

function updateViewportDerivedSampleData(fileName) {
  if (fileName === "ReignCourtScreen.xml") {
    SAMPLE_DATA.UseLargeCourtLayout = false;
    SAMPLE_DATA.LargeHomeVisible = false;
    SAMPLE_DATA.ReferenceHomeVisible = SAMPLE_DATA.HomeVisible;
  }
  if (fileName !== "ReignSocialEventScreen.xml") return;
  const [width, height] = getViewport();
  const logicalHeight = height / getEffectiveRenderScale(width, height, getRuntimeScaleProfile(fileName));
  const artSize = Math.round(Math.min(876, Math.max(236, logicalHeight - 484)));
  SAMPLE_DATA.EventArtSize = artSize;
  SAMPLE_DATA.EventArtRowHeight = artSize + 20;
}

function getNativeGauntletScale(width, height) {
  let scale = height / GAUNTLET_REFERENCE_HEIGHT;
  const aspectRatio = width / height;
  const narrowAspectRatioThreshold = (GAUNTLET_REFERENCE_WIDTH / GAUNTLET_REFERENCE_HEIGHT) * GAUNTLET_NARROW_ASPECT_TOLERANCE;
  if (aspectRatio < narrowAspectRatioThreshold) {
    scale *= aspectRatio / narrowAspectRatioThreshold;
  }
  return scale;
}

function getRuntimeScaleProfile(fileName = currentFileName) {
  const selectedProfile = elements.runtimeScaleSelect.value;
  if (selectedProfile !== "auto") return selectedProfile;
  if (prefabIgnoresCustomScale(currentXmlText)) return "custom-scale-locked";
  return PREFAB_RUNTIME_SCALE_PROFILES.get(fileName) || "native";
}

function prefabIgnoresCustomScale(xmlText) {
  if (!xmlText?.includes('DoNotUseCustomScaleAndChildren="true"')) return false;
  const documentNode = new DOMParser().parseFromString(xmlText, "application/xml");
  if (documentNode.querySelector("parsererror")) return false;
  const windowRoot = documentNode.querySelector("Prefab > Window > Widget");
  if (!windowRoot) return false;
  if (windowRoot.getAttribute("DoNotUseCustomScaleAndChildren") === "true") return true;
  const childrenContainer = [...windowRoot.children].find((child) => child.tagName === "Children");
  return [...(childrenContainer?.children || [])]
    .some((child) => child.getAttribute("DoNotUseCustomScaleAndChildren") === "true");
}

function getEffectiveRenderScale(width, height, runtimeProfile = getRuntimeScaleProfile()) {
  const nativeScale = getNativeGauntletScale(width, height);
  if (runtimeProfile === "custom-scale-locked") return nativeScale;
  const evidenceScale = getNativeEvidenceScale(width, height);
  if (evidenceScale) return evidenceScale;
  const runtimeScaleModifier = runtimeProfile === "reference-locked" ? 1 / nativeScale : 1;
  return nativeScale * runtimeScaleModifier * getUiScale();
}

function getNativeEvidenceScale(width, height) {
  if (!elements.nativeAuthorityToggle.checked || !currentNativeEvidence?.snapshot) return null;
  if (!currentNativeEvidence.snapshotMatchesSource || !currentNativeEvidence.exactCaseMatch) return null;
  const snapshot = currentNativeEvidence.snapshot;
  const physicalWidth = Number(snapshot.physicalWidth);
  const physicalHeight = Number(snapshot.physicalHeight);
  const customScale = Number(snapshot.uiScale);
  if (!Number.isFinite(customScale) || customScale <= 0) return null;
  if (Math.round(physicalWidth) !== Math.round(width) || Math.round(physicalHeight) !== Math.round(height)) return null;
  if (Math.abs(Number(currentNativeEvidence.requestedUiScale) - getUiScale()) > 0.0001) return null;
  return customScale;
}

function formatRuntimeScaleProfile(profile) {
  if (profile === "custom-scale-locked") return "player-scale locked";
  if (profile === "reference-locked") return "fixed reference";
  return "native sizing";
}

function formatDimension(value) {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

function restoreTestSurfacePreferences() {
  try {
    const viewport = localStorage.getItem(VIEWPORT_PREFERENCE_KEY);
    const uiScale = localStorage.getItem(UI_SCALE_PREFERENCE_KEY);
    const runtimeScale = localStorage.getItem(RUNTIME_SCALE_PREFERENCE_KEY);
    if (viewport && [...elements.viewportSelect.options].some((option) => option.value === viewport)) {
      elements.viewportSelect.value = viewport;
    }
    if (uiScale && [...elements.uiScaleSelect.options].some((option) => option.value === uiScale)) {
      elements.uiScaleSelect.value = uiScale;
    }
    if (runtimeScale && [...elements.runtimeScaleSelect.options].some((option) => option.value === runtimeScale)) {
      elements.runtimeScaleSelect.value = runtimeScale;
    }
  } catch {
    // Storage can be disabled. The selected HTML defaults remain valid.
  }
}

function applyUrlTestSurfaceOverrides() {
  const query = new URLSearchParams(window.location.search);
  const assignments = [
    [elements.viewportSelect, query.get("viewport")],
    [elements.uiScaleSelect, query.get("uiScale")],
    [elements.runtimeScaleSelect, query.get("runtimeScale")]
  ];
  for (const [select, requested] of assignments) {
    if (requested && [...select.options].some((option) => option.value === requested)) select.value = requested;
  }
}

function persistTestSurfacePreferences() {
  try {
    localStorage.setItem(VIEWPORT_PREFERENCE_KEY, elements.viewportSelect.value);
    localStorage.setItem(UI_SCALE_PREFERENCE_KEY, elements.uiScaleSelect.value);
    localStorage.setItem(RUNTIME_SCALE_PREFERENCE_KEY, elements.runtimeScaleSelect.value);
  } catch {
    // A blocked storage area must not prevent viewport testing.
  }
}

function applyFit() {
  const width = elements.stage.offsetWidth;
  const height = elements.stage.offsetHeight;
  if (!elements.fitToggle.checked) {
    elements.stageWrap.style.transform = "none";
    elements.stageWrap.style.width = `${width}px`;
    elements.stageWrap.style.height = `${height}px`;
    calibrationEditor.refresh();
    return;
  }

  const availableWidth = Math.max(320, elements.stageShell.clientWidth - 48);
  const availableHeight = Math.max(240, elements.stageShell.clientHeight - 48);
  const scale = Math.min(1, availableWidth / width, availableHeight / height);
  elements.stageWrap.style.transform = `scale(${scale})`;
  elements.stageWrap.style.width = `${width * scale}px`;
  elements.stageWrap.style.height = `${height * scale}px`;
  calibrationEditor.refresh();
}

function applyDebugClasses() {
  elements.stageContent.classList.toggle("outline", elements.outlineToggle.checked);
  renderer.renderedElements.forEach((element) => element.classList.toggle("xml-label", elements.labelToggle.checked));
}

function handleStageClick(event) {
  if (!currentXmlText) return;
  event.preventDefault();
  event.stopPropagation();
  const stack = document.elementsFromPoint(event.clientX, event.clientY)
    .filter((element) => element.classList?.contains("g-node") && elements.stageContent.contains(element));
  if (!stack.length) return;

  const unique = [...new Map(stack.map((element) => [element.__gauntlet?.selectionKey, element])).values()]
    .filter((element) => element.__gauntlet);
  const signature = unique.map((element) => element.__gauntlet.selectionKey).join("|");
  const samePoint = Math.abs(clickCycle.x - event.clientX) < 5 && Math.abs(clickCycle.y - event.clientY) < 5;
  const index = samePoint && clickCycle.signature === signature
    ? (clickCycle.index + 1) % unique.length
    : 0;
  clickCycle = { x: event.clientX, y: event.clientY, signature, index };
  selectElement(unique[index], { index, total: unique.length });
}

function selectElement(element, stackInfo = null, restoring = false) {
  if (!element?.__gauntlet) return;
  selectedElement?.classList.remove("selected-node");
  selectedElement = element;
  selectedElement.classList.add("selected-node");
  const metadata = element.__gauntlet;
  selectedKey = metadata.selectionKey;
  lastSelectedMetadata = metadata;

  elements.selectionTitle.textContent = metadata.id ? `${metadata.tag} #${metadata.id}` : metadata.tag;
  const layerText = stackInfo && stackInfo.total > 1 ? ` · layer ${stackInfo.index + 1} of ${stackInfo.total}` : "";
  elements.selectionSubtitle.textContent = `Line ${metadata.line ?? "?"}${layerText} · selection persists across preview changes${restoring ? " · restored" : ""}`;
  elements.selectionToken.value = metadata.token;
  elements.copyToken.disabled = false;
  elements.copyFeedback.textContent = "";
  elements.inspectorEmpty.hidden = true;
  elements.inspectorDetails.hidden = false;

  renderIdentity(metadata, element);
  renderBoundProperties(metadata.boundProperties);
  renderAttributes(metadata.attributes);
  calibrationEditor.setSelection(element);
  codexChat.updateSelectionContext(buildCodexContext());
}

function renderIdentity(metadata, element) {
  const attributes = metadata.attributes;
  const rows = [
    ["XML path", metadata.path],
    ["Element type", metadata.tag],
    ["Id", metadata.id || "—"],
    ["DataSource", metadata.dataSource || "—"],
    ["Instance", metadata.instanceTrail.join(" / ") || "root"],
    ["Source", metadata.line == null ? "unknown" : `line ${metadata.line}, column ${metadata.column}`],
    ["Rendered size", `${element.offsetWidth} × ${element.offsetHeight} logical px`],
    ["Physical size", `${formatDimension(element.offsetWidth * getEffectiveRenderScale(...getViewport()))} × ${formatDimension(element.offsetHeight * getEffectiveRenderScale(...getViewport()))} px`],
    ["Size policies", `${attributes.WidthSizePolicy || "Fixed"} × ${attributes.HeightSizePolicy || "Fixed"}`],
    ["Suggested", `${attributes.SuggestedWidth || "—"} × ${attributes.SuggestedHeight || "—"}`],
    ["Margins", `T ${attributes.MarginTop || 0} · R ${attributes.MarginRight || 0} · B ${attributes.MarginBottom || 0} · L ${attributes.MarginLeft || 0}`],
    ["Alignment", `${attributes.HorizontalAlignment || "Left"} / ${attributes.VerticalAlignment || "Top"}`],
    ["Brush", metadata.brush || "—"],
    ["Sprite", metadata.sprite || "—"]
  ];
  elements.identityDetails.replaceChildren();
  rows.forEach(([name, value]) => {
    const term = document.createElement("dt");
    const description = document.createElement("dd");
    term.textContent = name;
    description.textContent = value;
    elements.identityDetails.append(term, description);
  });
}

function renderBoundProperties(properties) {
  elements.boundProperties.replaceChildren();
  if (!properties.length) {
    elements.boundProperties.append(makePropertyRow("None", "This object has no bound XML attributes."));
    return;
  }
  properties.forEach((property) => {
    const value = property.missing
      ? `${property.expression} → missing sample binding`
      : `${property.expression} → ${describeValue(property.value)}`;
    elements.boundProperties.append(makePropertyRow(property.attribute, value, property.missing ? "missing-row" : "binding-row"));
  });
}

function renderAttributes(attributes) {
  elements.attributeList.replaceChildren();
  const entries = Object.entries(attributes);
  if (!entries.length) {
    elements.attributeList.append(makePropertyRow("None", "No XML attributes."));
    return;
  }
  entries.forEach(([name, value]) => elements.attributeList.append(makePropertyRow(name, value)));
}

function makePropertyRow(name, value, extraClass = "") {
  const row = document.createElement("div");
  row.className = `property-row ${extraClass}`.trim();
  const key = document.createElement("span");
  const content = document.createElement("span");
  key.className = "property-name";
  content.className = "property-value";
  key.textContent = name;
  content.textContent = value;
  row.append(key, content);
  return row;
}

function preserveStaleSelection() {
  selectedElement?.classList.remove("selected-node");
  selectedElement = null;
  calibrationEditor.clearSelection();
  codexChat.updateSelectionContext(buildCodexContext());
  if (!lastSelectedMetadata) return;
  elements.selectionSubtitle.textContent = "The selected XML object is not rendered in this example state; its identity remains pinned.";
}

function clearSelection() {
  selectedElement?.classList.remove("selected-node");
  selectedElement = null;
  selectedKey = "";
  lastSelectedMetadata = null;
  elements.selectionTitle.textContent = "Nothing selected";
  elements.selectionSubtitle.textContent = "Click any rendered object. Repeated clicks at the same point cycle through overlapping layers.";
  elements.selectionToken.value = "";
  elements.copyToken.disabled = true;
  elements.copyFeedback.textContent = "";
  elements.inspectorEmpty.hidden = false;
  elements.inspectorDetails.hidden = true;
  calibrationEditor.clearSelection();
  codexChat.updateSelectionContext(buildCodexContext());
}

function buildCodexContext() {
  const metadata = lastSelectedMetadata;
  const selection = metadata ? {
    token: metadata.token,
    path: metadata.path,
    line: metadata.line,
    column: metadata.column,
    tag: metadata.tag,
    id: metadata.id,
    dataSource: metadata.dataSource,
    instanceTrail: metadata.instanceTrail,
    attributes: metadata.attributes,
    bindings: metadata.boundProperties.map((property) => ({
      attribute: property.attribute,
      expression: property.expression,
      missing: property.missing,
      value: describeValue(property.value)
    })),
    renderedGeometry: selectedElement?.isConnected ? {
      x: selectedElement.offsetLeft,
      y: selectedElement.offsetTop,
      width: selectedElement.offsetWidth,
      height: selectedElement.offsetHeight
    } : null
  } : null;
  const relevantIssues = metadata
    ? currentIssues.filter((issue) => issue.selectionKey === metadata.selectionKey || issue.path === metadata.path)
    : currentIssues.slice(0, 30);
  return {
    schema: "reign-ui-codex-selection-context-v1",
    fileName: currentFileName,
    label: currentLabel,
    preset: currentPreset,
    interfaceId: currentInterfaceId,
    workspacePrefabPath: currentPrefabPath,
    viewport: elements.viewportSelect.value,
    uiScale: getUiScale(),
    runtimeScale: getRuntimeScaleProfile(),
    selection,
    diagnostics: relevantIssues.map((issue) => ({ kind: issue.kind, message: issue.message, path: issue.path, line: issue.line })),
    diagnosticCount: currentIssues.length,
    pendingXmlChanges: document.getElementById("calibrationPatchReview")?.value || "",
    sourceOutOfDate,
    installSync: calibrationEditor.getState().installSync,
    nativeAugmentation: currentNativeAugmentation,
    nativeParity: currentNativeParityReadiness ? {
      providerPreviewReady: currentNativeParityReadiness.providerPreviewReady,
      installedStandaloneReady: currentNativeParityReadiness.installedStandaloneReady,
      nativeReady: currentNativeParityReadiness.nativeReady,
      counts: currentNativeParityReadiness.counts,
      pending: (currentNativeParityReadiness.surfaces || [])
        .filter((surface) => !surface.nativeAccepted)
        .map((surface) => ({ id: surface.id, reasons: surface.reasons }))
    } : null
  };
}

function runDiagnostics() {
  if (!currentXmlText || !renderer.renderedElements.length) return;
  currentIssues = analyzeDiagnostics(renderer, elements.stageContent);
  renderDiagnostics(currentIssues);
}

function renderDiagnostics(issues) {
  const counts = { missing: 0, asset: 0, overflow: 0, clipping: 0, fit: 0 };
  issues.forEach((issue) => { counts[issue.kind] = (counts[issue.kind] || 0) + 1; });
  elements.issueCount.textContent = String(issues.length);
  elements.issueCount.classList.toggle("has-issues", issues.length > 0);
  elements.missingCount.textContent = String(counts.missing || 0);
  elements.assetCount.textContent = String(counts.asset || 0);
  elements.overflowCount.textContent = String(counts.overflow || 0);
  elements.clippingCount.textContent = String(counts.clipping || 0);
  elements.fitCount.textContent = String(counts.fit || 0);
  elements.diagnosticList.replaceChildren();

  if (!issues.length) {
    const ok = document.createElement("p");
    ok.className = "diagnostic-ok";
    ok.textContent = "No missing bindings, runtime asset mismatches, overflow, clipping, or frame-fit contract risks detected at this test surface.";
    elements.diagnosticList.append(ok);
    return;
  }

  issues.slice(0, 100).forEach((issue) => {
    const item = document.createElement("button");
    item.className = `diagnostic-item ${issue.kind}`;
    const title = document.createElement("strong");
    const details = document.createElement("small");
    title.textContent = `${issue.kind.toUpperCase()} · ${issue.label || issue.binding || "XML binding"}`;
    details.textContent = `L${issue.line ?? "?"} · ${issue.message}`;
    item.title = `${issue.path || ""}\n${issue.message}`;
    item.append(title, details);
    if (issue.selectionKey && renderer.elementBySelectionKey.has(issue.selectionKey)) {
      item.addEventListener("click", () => selectElement(renderer.elementBySelectionKey.get(issue.selectionKey)));
    } else {
      item.disabled = true;
    }
    elements.diagnosticList.append(item);
  });

  if (issues.length > 100) {
    const note = document.createElement("p");
    note.className = "microcopy";
    note.textContent = `${issues.length - 100} additional observations are counted but not listed.`;
    elements.diagnosticList.append(note);
  }
}

function copySelectionToken() {
  const token = elements.selectionToken.value;
  if (!token) return;
  const fallbackCopy = () => {
    elements.selectionToken.select();
    document.execCommand("copy");
    elements.selectionToken.setSelectionRange(0, 0);
  };
  fallbackCopy();
  if (navigator.clipboard?.writeText) {
    navigator.clipboard.writeText(token).catch(fallbackCopy);
  }
  elements.copyFeedback.textContent = "Selection token copied.";
  window.setTimeout(() => { elements.copyFeedback.textContent = ""; }, 1800);
}

function markActivePrefab(path, preset = "") {
  document.querySelectorAll("[data-prefab]").forEach((button) => {
    button.classList.toggle(
      "active",
      button.dataset.prefab === path && (button.dataset.preset || "") === preset
    );
  });
  if (path) markActiveAugmentation("");
}

function markActiveAugmentation(id) {
  document.querySelectorAll("[data-augmentation]").forEach((button) => {
    button.classList.toggle("active", button.dataset.augmentation === id);
  });
}

function setStatus(message, state = "") {
  elements.status.textContent = message;
  elements.status.className = `status-copy ${state}`.trim();
  elements.statusDot.className = `status-dot ${state}`.trim();
}

function getViewport() {
  return elements.viewportSelect.value.split("x").map(Number);
}

function getUiScale() {
  return Number(elements.uiScaleSelect.value) || 1;
}

function formatPercent(value) {
  return `${Math.round(value * 100)}%`;
}

function describeValue(value) {
  if (Array.isArray(value)) return `[${value.length} items]`;
  if (value && typeof value === "object") {
    const json = JSON.stringify(value);
    return json.length > 90 ? `${json.slice(0, 87)}…` : json;
  }
  if (value == null || value === "") return value == null ? "null" : '""';
  return String(value);
}

function nextFrame() {
  return new Promise((resolve) => requestAnimationFrame(resolve));
}
