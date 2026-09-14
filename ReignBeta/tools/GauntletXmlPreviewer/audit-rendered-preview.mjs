import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { selectRenderedPreviewTargets } from "./js/rendered-preview-targets.js";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const catalogPath = path.join(toolRoot, "calibration", "ui-catalog.json");
const outputRoot = argumentValue("--output")
  ? path.resolve(argumentValue("--output"))
  : path.resolve(toolRoot, "../../../.codex-build/ui-preview/render-matrix");
const previewBaseUrl = argumentValue("--url") || "http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html";
const edgePath = argumentValue("--edge") || "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const viewports = (argumentValue("--viewports") || "1920x1080,3440x1440").split(",").map((value) => value.trim()).filter(Boolean);
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const selection = selectRenderedPreviewTargets(catalog, process.argv.includes("--targets") ? argumentValue("--targets") : undefined);
const { interfaces, augmentations, partialScope, requestedTargets, catalogInterfaceStateCount, catalogAugmentationCount, catalogPrefabCount } = selection;
const prefabCount = new Set(interfaces.map((entry) => path.basename(entry.prefab))).size;
const debugPort = 19000 + Math.floor(Math.random() * 5000);
const profileRoot = path.join(outputRoot, `edge-profile-${process.pid}-${Date.now()}`);
const screenshotsRoot = path.join(outputRoot, "screenshots");
const reportPath = path.join(outputRoot, "rendered-preview-audit.json");
const errors = [];
const cases = [];
let parityUi = null;
let interactionContract = null;
let scaleContract = null;

fs.mkdirSync(profileRoot, { recursive: true });
fs.mkdirSync(screenshotsRoot, { recursive: true });

class CdpClient {
  constructor(url) {
    this.url = url;
    this.socket = null;
    this.nextId = 0;
    this.pending = new Map();
  }

  async connect() {
    this.socket = new WebSocket(this.url);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("Edge DevTools WebSocket connection timed out.")), 5000);
      this.socket.addEventListener("open", () => { clearTimeout(timeout); resolve(); }, { once: true });
      this.socket.addEventListener("error", () => { clearTimeout(timeout); reject(new Error("Edge DevTools WebSocket connection failed.")); }, { once: true });
    });
    this.socket.addEventListener("message", (event) => {
      const message = JSON.parse(String(event.data));
      if (message.id == null) return;
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message || "DevTools command failed."));
      else pending.resolve(message.result || {});
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  close() {
    this.socket?.close();
  }
}

if (!fs.existsSync(edgePath)) throw new Error(`Microsoft Edge was not found at ${edgePath}. Pass --edge with its exact path.`);

const edge = spawn(edgePath, [
  "--headless=new",
  "--disable-gpu",
  "--hide-scrollbars",
  "--no-first-run",
  "--disable-features=msEdgeFirstRunExperience",
  `--remote-debugging-port=${debugPort}`,
  `--user-data-dir=${profileRoot}`,
  "about:blank"
], { stdio: "ignore", windowsHide: true });

try {
  await waitForEndpoint(`http://127.0.0.1:${debugPort}/json/version`, 12000);
  const target = await createTarget(debugPort);
  const cdp = new CdpClient(target.webSocketDebuggerUrl);
  await cdp.connect();
  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });

  for (const viewport of viewports) {
    for (const entry of interfaces) {
      const fileName = path.basename(entry.prefab);
      const url = new URL(previewBaseUrl);
      url.searchParams.set("interface", entry.id);
      url.searchParams.set("viewport", viewport);
      url.searchParams.set("uiScale", "1");
      url.searchParams.set("runtimeScale", "auto");
      url.searchParams.set("codex", "off");
      await cdp.send("Page.navigate", { url: url.href });
      let state = await waitForPreviewState(cdp, fileName, viewport, 15000, entry.id);
      await settleFrames(cdp);
      state = await evaluateState(cdp);
      const capture = await cdp.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false, fromSurface: true });
      const caseId = `${entry.id}-${viewport}`;
      const screenshotPath = path.join(screenshotsRoot, `${caseId}.png`);
      fs.writeFileSync(screenshotPath, Buffer.from(capture.data, "base64"));
      const issueCount = Number(state.issueCount || 0);
      const caseErrors = [];
      if (entry.variant === "intoxication-actions") {
        const result = await cdp.send("Runtime.evaluate", { expression: `JSON.stringify([...document.querySelectorAll('em')].filter(e => e.textContent.includes('They') || e.textContent.includes('Their')).map(e => ({ text: e.textContent, style: getComputedStyle(e).fontStyle })))`, returnByValue: true });
        const actions = JSON.parse(result.result?.value || "[]");
        if (actions.length < 3 || actions.some(e => e.style !== "italic" || e.text.includes("*"))) caseErrors.push("Action fixture must render three marker-free italic actions.");
      }
      if (state.fileName !== fileName) caseErrors.push(`Loaded ${state.fileName || "nothing"} instead of ${fileName}.`);
      if (state.interfaceId !== entry.id) caseErrors.push(`Loaded interface identity ${state.interfaceId || "missing"} instead of ${entry.id}.`);
      if ((state.preset || "") !== (entry.variant || "")) caseErrors.push(`Loaded preset ${state.preset || "empty"} instead of ${entry.variant || "empty"}.`);
      if (state.viewport !== viewport) caseErrors.push(`Rendered ${state.viewport || "unknown"} instead of ${viewport}.`);
      if (state.parseError) caseErrors.push(`Browser reported an XML parse error: ${state.parseError}`);
      if (issueCount !== 0) caseErrors.push(`Browser diagnostics reported ${issueCount} issue(s): ${JSON.stringify(state.diagnosticDetails || state.diagnostics)}`);
      if (Number(state.catalogEntries) !== catalogInterfaceStateCount) caseErrors.push(`${state.catalogEntries} interface buttons were rendered instead of ${catalogInterfaceStateCount}.`);
      if (Number(state.installSummary?.prefabCount) !== catalogPrefabCount) caseErrors.push(`Install summary covers ${state.installSummary?.prefabCount ?? "no"} prefabs instead of ${catalogPrefabCount}.`);
      if (state.parityStatus !== "Disabled during provider-free capture audits.") caseErrors.push("Provider-free capture did not disable recursive native-readiness refresh.");
      if (!state.referenceSurfaceBounds) caseErrors.push("No visible fixed 16:9 Gauntlet reference surface could be identified for deterministic fidelity registration.");
      if (entry.id === "calibration-overlay" && !(state.fidelityDynamicExclusions || []).length) caseErrors.push("Calibration Overlay did not expose its movable selection geometry for fidelity masking.");
      if (entry.id === "wilderness-event" && !String(state.stageText || "").includes("Wilderness Encounter")) caseErrors.push("Wilderness Event did not render its dedicated fixture state.");
      if (entry.id === "social-event" && String(state.stageText || "").includes("Wilderness Encounter")) caseErrors.push("Social Event was contaminated by the Wilderness Event fixture state.");
      if (entry.id === "individual-chat-pregnancy-warning") {
        const warningText = String(state.stageText || "");
        for (const expected of ["Warning", "This choice may result in pregnancy.", "Pull Out", "Proceed", "Choose carefully."]) {
          if (!warningText.includes(expected)) caseErrors.push(`Pregnancy Warning fixture is missing ${expected}.`);
        }
      }
      if (entry.id === "royal-council") {
        const royalText = state.stageText || "";
        for (const expected of ["Gwydan", "Gwyin", "Elideth", "Branara"]) {
          if (!royalText.includes(expected)) caseErrors.push(`Royal Council fixture is missing ${expected}.`);
        }
        for (const fixedCaption of ["ROYAL COUNCIL", "WAR COUNCILOR", "SPYMASTER", "ECONOMIC ADVISOR", "FOREIGN ADVISOR", "SEND"]) {
          if (royalText.includes(fixedCaption)) caseErrors.push(`Royal Council rendered a duplicate live fixed caption: ${fixedCaption}.`);
        }
        if (royalText.includes("A Pact Proclaimed Before the Lords of Calradia")) caseErrors.push("Royal Council render contains the diplomacy-announcement fixture title.");
        if (royalText.includes("VACANT")) caseErrors.push("Royal Council coherent fixture was contaminated by a captured vacant-seat state.");
      }
      caseErrors.forEach((message) => errors.push(`${caseId}: ${message}`));
      cases.push({
        caseId,
        interfaceId: entry.id,
        interfaceType: entry.previewState ? "preview-state" : entry.supportUi ? "support" : "runtime",
        fileName,
        viewport,
        fileTitle: state.fileTitle,
        fileMeta: state.fileMeta,
        renderedObjects: state.renderedObjects,
        issueCount,
        diagnostics: state.diagnostics,
        diagnosticDetails: state.diagnosticDetails || [],
        catalogEntries: state.catalogEntries,
        installSummary: summarizeInstall(state.installSummary),
        nativeEvidence: state.nativeEvidence,
        codexStatus: state.codexStatus,
        parityStatus: state.parityStatus,
        portraitAssetCount: state.portraitAssetCount,
        referenceSurfaceBounds: state.referenceSurfaceBounds,
        fidelityDynamicExclusions: state.fidelityDynamicExclusions || [],
        screenshotPath,
        screenshotSha256: await fileSha256(screenshotPath),
        errors: caseErrors
      });
    }
    for (const augmentation of augmentations) {
      const url = new URL(previewBaseUrl);
      url.searchParams.set("augmentation", augmentation.id);
      url.searchParams.set("viewport", viewport);
      url.searchParams.set("uiScale", "1");
      url.searchParams.set("runtimeScale", "auto");
      url.searchParams.set("codex", "off");
      const fileName = `NativeAugmentation-${augmentation.target}.xml`;
      await cdp.send("Page.navigate", { url: url.href });
      let state = await waitForPreviewState(cdp, fileName, viewport, 20000, augmentation.id);
      await settleFrames(cdp);
      state = await evaluateState(cdp);
      const capture = await cdp.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false, fromSurface: true });
      const caseId = `NativeAugmentation-${augmentation.target}-${viewport}`;
      const screenshotPath = path.join(screenshotsRoot, `${caseId}.png`);
      fs.writeFileSync(screenshotPath, Buffer.from(capture.data, "base64"));
      const issueCount = Number(state.issueCount || 0);
      const caseErrors = [];
      if (state.fileName !== fileName) caseErrors.push(`Loaded ${state.fileName || "nothing"} instead of ${fileName}.`);
      if (state.viewport !== viewport) caseErrors.push(`Rendered ${state.viewport || "unknown"} instead of ${viewport}.`);
      if (state.parseError) caseErrors.push(`Browser reported an XML parse error: ${state.parseError}`);
      if (issueCount !== 0) caseErrors.push(`Browser diagnostics reported ${issueCount} issue(s): ${JSON.stringify(state.diagnosticDetails || state.diagnostics)}`);
      if (Number(state.augmentationEntries) !== catalogAugmentationCount) caseErrors.push(`Only ${state.augmentationEntries} augmentation buttons were rendered for ${catalogAugmentationCount} cataloged native targets.`);
      if (state.nativeAugmentation?.target !== augmentation.target) caseErrors.push(`Composed augmentation context is ${state.nativeAugmentation?.target || "missing"} instead of ${augmentation.target}.`);
      if (!state.nativeAugmentation?.patches?.length) caseErrors.push("No source-discovered Reign patch applications were attached to the composed preview.");
      if (state.parityStatus !== "Disabled during provider-free capture audits.") caseErrors.push("Provider-free native-composition capture did not disable recursive native-readiness refresh.");
      caseErrors.forEach((message) => errors.push(`${caseId}: ${message}`));
      cases.push({
        caseId,
        fileName,
        viewport,
        fileTitle: state.fileTitle,
        fileMeta: state.fileMeta,
        renderedObjects: state.renderedObjects,
        issueCount,
        diagnostics: state.diagnostics,
        diagnosticDetails: state.diagnosticDetails || [],
        augmentationEntries: state.augmentationEntries,
        nativeAugmentation: state.nativeAugmentation,
        codexStatus: state.codexStatus,
        parityStatus: state.parityStatus,
        screenshotPath,
        screenshotSha256: await fileSha256(screenshotPath),
        errors: caseErrors
      });
    }
  }
  interactionContract = await auditDirectManipulation(cdp);
  interactionContract.errors.forEach((message) => errors.push(`direct-manipulation: ${message}`));
  scaleContract = await auditCustomScaleLock(cdp);
  scaleContract.errors.forEach((message) => errors.push(`custom-scale-lock: ${message}`));
  fs.writeFileSync(reportPath, `${JSON.stringify(buildReport(), null, 2)}\n`, "utf8");
  if (!partialScope) {
  const parityUrl = new URL(previewBaseUrl);
  parityUrl.searchParams.set("interface", "royal-council");
  parityUrl.searchParams.set("viewport", viewports[0]);
  parityUrl.searchParams.set("uiScale", "1");
  parityUrl.searchParams.set("runtimeScale", "auto");
  await cdp.send("Page.navigate", { url: parityUrl.href });
  // Every-surface parity recomputes hashes and native-review readiness for the
  // complete catalog. On slower disks that bounded work legitimately exceeds
  // the per-screen render timeout, so give this final aggregate gate its own
  // bounded window instead of misclassifying a still-running recomputation as
  // an empty Royal Council render.
  const parityState = await waitForPreviewState(cdp, "ReignRoyalCouncilScreen.xml", viewports[0], 180000, "royal-council", true);
  parityUi = {
    status: parityState.parityStatus,
    detail: parityState.parityDetail,
    readiness: parityState.nativeParity
  };
  const parityCounts = parityState.nativeParity?.counts || {};
  const runtimeInterfaces = catalog.interfaces.filter((entry) => !entry.supportUi);
  const expectedSurfaceCount = runtimeInterfaces.length + augmentations.length;
  const expectedParityStatus = new RegExp(`^Provider \\d+\\/${expectedSurfaceCount} · installed \\d+\\/${runtimeInterfaces.length} · native \\d+\\/${expectedSurfaceCount}$`);
  if (parityState.nativeParity?.providerPreviewReady !== true
      || Number(parityCounts.totalSurfaces) !== expectedSurfaceCount
      || Number(parityCounts.providerPreviewPassed) !== expectedSurfaceCount
      || Number(parityCounts.nativeAccepted || 0) + Number(parityCounts.nativePending || 0) !== expectedSurfaceCount
      || !expectedParityStatus.test(parityState.parityStatus || "")
      || !parityState.parityDetail) {
    errors.push(`Ordinary preview did not hydrate every-interface parity readiness: ${JSON.stringify(parityUi)}`);
  }
  } else {
    parityUi = { status: "not-run-partial-scope", detail: "Full-catalog native parity readiness requires an unfiltered render audit." };
  }
  cdp.close();
} catch (error) {
  // Keep completed cases reviewable even when a later readiness gate fails.
  errors.push(`Rendered preview interrupted: ${error?.stack || error?.message || String(error)}`);
} finally {
  if (!edge.killed) edge.kill();
  if (edge.exitCode == null) {
    await Promise.race([
      new Promise((resolve) => edge.once("exit", resolve)),
      delay(3000)
    ]);
  }
  try {
    fs.rmSync(profileRoot, { recursive: true, force: true });
  } catch {
    // A lingering Edge helper can briefly retain its run-owned profile on Windows.
  }
}

const result = buildReport();
fs.writeFileSync(reportPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
process.stdout.write(`${JSON.stringify({
  schema: result.schema,
  generatedUtc: result.generatedUtc,
  previewBaseUrl,
  prefabCount: result.prefabCount,
  interfaceCount: result.interfaceCount,
  augmentationCount: result.augmentationCount,
  viewports,
  caseCount: result.caseCount,
  passedCount: result.passedCount,
  failedCount: result.failedCount,
  parityUi: result.parityUi,
  interactionContract: result.interactionContract,
  scaleContract: result.scaleContract,
  errors,
  reportPath,
  cases: cases.map((entry) => ({
    caseId: entry.caseId,
    renderedObjects: entry.renderedObjects,
    issueCount: entry.issueCount,
    screenshotSha256: entry.screenshotSha256,
    errors: entry.errors
  }))
}, null, 2)}\n`);
if (errors.length) process.exitCode = 1;

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] || "" : "";
}

function summarizeInstall(summary) {
  if (!summary) return null;
  return {
    schema: summary.schema,
    prefabCount: summary.prefabCount,
    matchingCount: summary.matchingCount,
    staleCount: summary.staleCount,
    missingCount: summary.missingCount,
    bannerlordRunning: summary.bannerlordRunning,
    mismatches: (summary.prefabs || []).filter((row) => !row.inSync).map((row) => row.fileName)
  };
}

async function createTarget(port) {
  const response = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" });
  if (!response.ok) throw new Error(`Edge target creation failed: ${response.status} ${response.statusText}`);
  return response.json();
}

async function waitForEndpoint(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { cache: "no-store" });
      if (response.ok) return response.json();
      lastError = new Error(`${response.status} ${response.statusText}`);
    } catch (error) {
      lastError = error;
    }
    await delay(100);
  }
  throw new Error(`Edge DevTools did not become ready: ${lastError?.message || "timeout"}`);
}

async function waitForPreviewState(cdp, fileName, viewport, timeoutMs, interfaceId = "", requireParity = false) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;
  while (Date.now() < deadline) {
    try {
      lastState = await evaluateState(cdp);
      if (lastState?.fileName === fileName
        && lastState?.viewport === viewport
        && (!interfaceId || lastState?.interfaceId === interfaceId)
        && lastState?.renderedObjects > 0
        && lastState?.installSummary?.prefabCount
        && (!requireParity || lastState?.nativeParity?.counts?.totalSurfaces)) return lastState;
    } catch {
      // The execution context is replaced during Page.navigate. Retry against the new context.
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${fileName} at ${viewport}; last state: ${JSON.stringify(lastState)}`);
}

async function settleFrames(cdp) {
  await cdp.send("Runtime.evaluate", {
    expression: "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))",
    awaitPromise: true,
    returnByValue: true
  });
}

async function evaluateState(cdp) {
  const expression = `(() => {
    const api = window.__reignPreviewer;
    if (!api) return null;
    const state = api.getState();
    const text = id => document.getElementById(id)?.textContent?.trim() || "";
    const renderedObjects = Number(text("fileMeta").match(/([0-9,]+) rendered objects/)?.[1]?.replace(/,/g, "") || 0);
    const targetAspect = 1672 / 941;
    const surfaceCandidates = [...document.querySelectorAll("#stageContent .g-node")]
      .map((element) => {
        const metadata = element.__gauntlet || {};
        const attributes = metadata.attributes || {};
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        const aspect = rect.height > 0 ? rect.width / rect.height : 0;
        return {
          element,
          metadata,
          attributes,
          rect,
          style,
          aspect,
          area: rect.width * rect.height
        };
      })
      .filter((candidate) => candidate.attributes.WidthSizePolicy === "Fixed"
        && candidate.attributes.HeightSizePolicy === "Fixed"
        && candidate.rect.width >= 350
        && candidate.rect.height >= 190
        && Math.abs(candidate.aspect - targetAspect) <= 0.03
        && candidate.style.display !== "none"
        && candidate.style.visibility !== "hidden"
        && Number(candidate.style.opacity || 1) > 0)
      .sort((left, right) => right.area - left.area || String(left.metadata.path || "").localeCompare(String(right.metadata.path || "")));
    const surface = surfaceCandidates[0] || null;
    const referenceSurfaceBounds = surface ? {
      method: "largest-visible-fixed-16x9-gauntlet-root-v1",
      x: surface.rect.x,
      y: surface.rect.y,
      width: surface.rect.width,
      height: surface.rect.height,
      aspectRatio: surface.aspect,
      elementId: surface.metadata.id || "",
      elementTag: surface.metadata.tag || "",
      elementPath: surface.metadata.path || "",
      suggestedWidth: surface.attributes.SuggestedWidth || "",
      suggestedHeight: surface.attributes.SuggestedHeight || "",
      candidateCount: surfaceCandidates.length
    } : null;
    const calibrationSelection = document.querySelector('#stageContent [data-element-id="ReignUiCalibrationSelection"]');
    const fidelityDynamicExclusions = calibrationSelection && getComputedStyle(calibrationSelection).display !== "none"
      ? [...calibrationSelection.children]
        .filter((element) => element.classList?.contains("g-node"))
        .map((element, index) => {
          const rect = element.getBoundingClientRect();
          return {
            id: "calibration-selection-runtime-part-" + (index + 1),
            category: "unprovided-state",
            shape: "rectangle",
            coordinateSpace: "screenshot",
            x: rect.x,
            y: rect.y,
            width: rect.width,
            height: rect.height
          };
        })
        .filter((region) => region.width > 0 && region.height > 0)
      : [];
    return {
      ...state,
      fileTitle: text("fileTitle"),
      fileMeta: text("fileMeta"),
      issueCount: Number(text("issueCount") || 0),
      diagnostics: state.diagnostics || {},
      renderedObjects,
      parseError: document.querySelector(".drop-hint span")?.textContent?.trim() || "",
      stageText: document.getElementById("stageContent")?.textContent || "",
      portraitAssetCount: document.querySelectorAll("#stageContent .portrait.has-asset").length,
      installSummary: state.calibration?.installSummary || null,
      nativeEvidence: state.nativeEvidence || null,
      codexStatus: text("codexStatus"),
      parityStatus: text("nativeParityStatus"),
      parityDetail: text("nativeParityDetail"),
      referenceSurfaceBounds,
      fidelityDynamicExclusions
    };
  })()`;
  const response = await cdp.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.text || "Browser evaluation failed.");
  return response.result?.value ?? null;
}

async function evaluateValue(cdp, expression) {
  const response = await cdp.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.text || "Browser evaluation failed.");
  return response.result?.value ?? null;
}

async function auditCustomScaleLock(cdp) {
  const contractErrors = [];
  const observations = [];
  await cdp.send("Emulation.setDeviceMetricsOverride", {
    width: 1920,
    height: 1080,
    deviceScaleFactor: 1,
    mobile: false
  });
  const inspect = async (interfaceId, fileName, runtimeScale) => {
    const url = new URL(previewBaseUrl);
    url.searchParams.set("interface", interfaceId);
    url.searchParams.set("viewport", "1920x1080");
    url.searchParams.set("uiScale", "1.20");
    url.searchParams.set("runtimeScale", runtimeScale);
    url.searchParams.set("codex", "off");
    await cdp.send("Page.navigate", { url: url.href });
    const state = await waitForPreviewState(cdp, fileName, "1920x1080", 15000, interfaceId);
    observations.push({
      interfaceId,
      requestedProfile: runtimeScale,
      resolvedProfile: state.runtimeScale,
      effectiveRenderScale: state.effectiveRenderScale,
      logicalViewport: state.logicalViewport
    });
    return state;
  };

  const locked = await inspect("royal-council", "ReignRoyalCouncilScreen.xml", "auto");
  if (locked.runtimeScale !== "custom-scale-locked") contractErrors.push(`Royal Council auto profile resolved to ${locked.runtimeScale || "nothing"}.`);
  if (Math.abs(Number(locked.effectiveRenderScale) - 1) > 0.0001) contractErrors.push(`Royal Council locked scale was ${locked.effectiveRenderScale} instead of 1 at 1920x1080.`);
  if (Math.abs(Number(locked.logicalViewport?.width) - 1920) > 0.1 || Math.abs(Number(locked.logicalViewport?.height) - 1080) > 0.1) {
    contractErrors.push(`Royal Council locked logical viewport was ${JSON.stringify(locked.logicalViewport)} instead of 1920x1080.`);
  }

  const unlocked = await inspect("notable-generation", "ReignNotableGenerationPopup.xml", "auto");
  if (unlocked.runtimeScale !== "native") contractErrors.push(`Notable Generation auto profile resolved to ${unlocked.runtimeScale || "nothing"} instead of native.`);
  if (Math.abs(Number(unlocked.effectiveRenderScale) - 1.2) > 0.0001) contractErrors.push(`Unlocked prefab scale was ${unlocked.effectiveRenderScale} instead of 1.2.`);

  const override = await inspect("royal-council", "ReignRoyalCouncilScreen.xml", "native");
  const overrideScale = Number(override.effectiveRenderScale);
  const lockedScale = Number(locked.effectiveRenderScale);
  const overrideUsesNativeScale = Number.isFinite(overrideScale)
    && Number.isFinite(lockedScale)
    && overrideScale > lockedScale + 0.0001
    && Number(override.logicalViewport?.width) < Number(locked.logicalViewport?.width)
    && Number(override.logicalViewport?.height) < Number(locked.logicalViewport?.height);
  if (override.runtimeScale !== "native" || !overrideUsesNativeScale) {
    contractErrors.push(`Explicit native override was not honored: ${JSON.stringify(observations.at(-1))}.`);
  }
  return { schema: "reign-ui-custom-scale-lock-audit-v1", observations, errors: contractErrors };
}

async function auditDirectManipulation(cdp) {
  const fileName = "ReignRoyalCouncilScreen.xml";
  const interfaceId = "royal-council";
  const viewport = viewports[0] || "1920x1080";
  const [viewportWidth, viewportHeight] = viewport.split("x").map(Number);
  const sourcePath = path.resolve(toolRoot, "../../GUI/Prefabs", fileName);
  const sourceSha256Before = await fileSha256(sourcePath);
  const result = {
    schema: "reign-ui-direct-manipulation-audit-v1",
    interfaceId,
    fileName,
    viewport,
    sourceSha256Before,
    sourceSha256After: "",
    selection: null,
    move: null,
    resize: null,
    undoRedo: null,
    approvalBoundary: null,
    errors: []
  };

  const url = new URL(previewBaseUrl);
  url.searchParams.set("interface", interfaceId);
  url.searchParams.set("viewport", viewport);
  url.searchParams.set("uiScale", "1");
  url.searchParams.set("runtimeScale", "native");
  url.searchParams.set("codex", "off");
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: viewportWidth, height: viewportHeight, deviceScaleFactor: 1, mobile: false });
  await cdp.send("Page.navigate", { url: url.href });
  await waitForPreviewState(cdp, fileName, viewport, 15000, interfaceId);
  await settleFrames(cdp);

  const setup = await evaluateValue(cdp, `(() => {
    const nodes = [...document.querySelectorAll("#stageContent .g-node")];
    const sendButton = nodes.find(node => node.__gauntlet?.tag === "ButtonWidget" && node.__gauntlet?.xmlNode?.getAttribute("Command.Click") === "ExecuteSend");
    if (!sendButton) return { error: "The Royal Council transparent SEND ButtonWidget was not rendered." };
    const rect = sendButton.getBoundingClientRect();
    sendButton.dispatchEvent(new MouseEvent("click", { bubbles: true, clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2 }));
    const toggle = document.getElementById("editModeToggle");
    toggle.checked = true;
    toggle.dispatchEvent(new Event("change", { bubbles: true }));
    const editor = window.__reignPreviewer?.calibrationEditor;
    const selected = editor?.selectedElement;
    const target = editor?.getMovementTarget(false);
    const moveSurface = document.querySelector("#calibrationOverlay [data-calibration-handle='move']");
    const moveRect = moveSurface?.getBoundingClientRect();
    const targetRect = target?.getBoundingClientRect();
    const movePoint = moveRect ? { x: moveRect.left + moveRect.width / 2, y: moveRect.top + moveRect.height / 2 } : null;
    const hit = movePoint ? document.elementFromPoint(movePoint.x, movePoint.y) : null;
    document.getElementById("snapSelect").value = "1";
    document.getElementById("aspectLockToggle").checked = false;
    return {
      selectedTag: selected?.__gauntlet?.tag || "",
      selectedPath: selected?.__gauntlet?.path || "",
      targetTag: target?.__gauntlet?.tag || "",
      targetPath: target?.__gauntlet?.path || "",
      targetId: target?.__gauntlet?.id || "",
      targetRect: targetRect ? { x: targetRect.x, y: targetRect.y, width: targetRect.width, height: targetRect.height } : null,
      movePoint,
      moveHandleAtPoint: hit?.closest?.("[data-calibration-handle]")?.dataset.calibrationHandle || "",
      overlayRect: moveSurface?.parentElement ? (() => { const box = moveSurface.parentElement.getBoundingClientRect(); return { x: box.x, y: box.y, width: box.width, height: box.height }; })() : null,
      handleRects: [...document.querySelectorAll("#calibrationOverlay [data-calibration-handle]")].map(node => { const box = node.getBoundingClientRect(); return { handle: node.dataset.calibrationHandle, x: box.x, y: box.y, width: box.width, height: box.height }; }),
      overlayVisible: !document.getElementById("calibrationOverlay")?.hidden,
      pending: document.getElementById("calibrationPatchReview")?.value || ""
    };
  })()`);
  result.selection = setup;
  if (setup?.error) result.errors.push(setup.error);
  if (setup?.selectedTag !== "ButtonWidget") result.errors.push(`Click selection resolved ${setup?.selectedTag || "nothing"} instead of the transparent SEND ButtonWidget.`);
  if (setup?.targetTag !== "ButtonWidget") result.errors.push(`The SEND hit area targets ${setup?.targetTag || "nothing"} instead of its ButtonWidget.`);
  if (!setup?.overlayVisible || !setup?.movePoint || !setup?.targetRect) result.errors.push("The move overlay did not become available after click selection and edit-mode activation.");

  if (!result.errors.length) {
    await dragMouse(cdp, setup.movePoint, { x: setup.movePoint.x + 24, y: setup.movePoint.y + 18 });
    await delay(150);
    await settleFrames(cdp);
    const moved = await evaluateValue(cdp, `(() => {
      const editor = window.__reignPreviewer.calibrationEditor;
      const selected = editor.selectedElement;
      const target = [...document.querySelectorAll("#stageContent .g-node")].find(node => node.__gauntlet?.path === ${JSON.stringify(setup.targetPath)});
      const targetRect = target?.getBoundingClientRect();
      return {
        selectedTag: selected?.__gauntlet?.tag || "",
        selectedText: selected?.textContent?.trim() || "",
        targetRect: targetRect ? { x: targetRect.x, y: targetRect.y, width: targetRect.width, height: targetRect.height } : null,
        pending: document.getElementById("calibrationPatchReview")?.value || "",
        issueCount: Number(document.getElementById("issueCount")?.textContent || 0),
        issues: [...document.querySelectorAll("#diagnosticList .diagnostic-item")].map(node => node.textContent?.trim() || ""),
        applySourceEnabled: !document.getElementById("applyCalibrationSource")?.disabled,
        applyGameEnabled: !document.getElementById("applyCalibrationGame")?.disabled
      };
    })()`);
    const movedX = Number(moved?.targetRect?.x) - Number(setup.targetRect.x);
    const movedY = Number(moved?.targetRect?.y) - Number(setup.targetRect.y);
    result.move = { selectedPath: setup.selectedPath, targetPath: setup.targetPath, deltaX: movedX, deltaY: movedY, pending: moved?.pending || "", issueCount: moved?.issueCount, issues: moved?.issues || [] };
    if (Math.abs(movedX - 24) > 1 || Math.abs(movedY - 18) > 1) result.errors.push(`Button drag produced ${movedX}×${movedY} instead of 24×18.`);
    if (moved?.selectedTag !== "ButtonWidget") result.errors.push("The transparent SEND ButtonWidget lost selection after movement.");
    if (!String(moved?.pending).includes(setup.targetPath) || !String(moved?.pending).includes("PositionXOffset") || !String(moved?.pending).includes("PositionYOffset")) {
      result.errors.push("The staged move patch does not target the owning button with both position offsets.");
    }
    if (Number(moved?.issueCount) !== 0) result.errors.push(`Moving the owning button introduced ${moved.issueCount} browser diagnostic issue(s).`);
    if (!moved?.applySourceEnabled || !moved?.applyGameEnabled) result.errors.push("A reviewed staged move does not expose both explicit source and install approval controls.");

    await evaluateValue(cdp, `(() => { document.getElementById("undoCalibration")?.click(); return true; })()`);
    await delay(120);
    await settleFrames(cdp);
    const afterMoveUndo = await directManipulationState(cdp, setup.targetPath);
    if (afterMoveUndo.pending) result.errors.push("Undo did not clear the staged move patch.");
    if (!rectMatches(afterMoveUndo.targetRect, setup.targetRect)) result.errors.push("Undo did not restore the button to its original bounds after movement.");

    const resizeSetup = await evaluateValue(cdp, `(() => {
      const editor = window.__reignPreviewer.calibrationEditor;
      const target = [...document.querySelectorAll("#stageContent .g-node")].find(node => node.__gauntlet?.path === ${JSON.stringify(setup.targetPath)});
      editor.setSelection(target);
      const handle = document.querySelector("#calibrationOverlay [data-calibration-handle='se']");
      const handleRect = handle?.getBoundingClientRect();
      const targetRect = target?.getBoundingClientRect();
      return {
        point: handleRect ? { x: handleRect.left + handleRect.width / 2, y: handleRect.top + handleRect.height / 2 } : null,
        targetRect: targetRect ? { x: targetRect.x, y: targetRect.y, width: targetRect.width, height: targetRect.height } : null
      };
    })()`);
    if (!resizeSetup?.point || !resizeSetup?.targetRect) {
      result.errors.push("The southeast resize handle did not become available for the owning button.");
    } else {
      await dragMouse(cdp, resizeSetup.point, { x: resizeSetup.point.x - 10, y: resizeSetup.point.y - 8 });
      await delay(150);
      await settleFrames(cdp);
      const resized = await directManipulationState(cdp, setup.targetPath);
      const resizedWidth = Number(resized?.targetRect?.width) - Number(resizeSetup.targetRect.width);
      const resizedHeight = Number(resized?.targetRect?.height) - Number(resizeSetup.targetRect.height);
      result.resize = { deltaWidth: resizedWidth, deltaHeight: resizedHeight, pending: resized?.pending || "", issueCount: resized?.issueCount, issues: resized?.issues || [] };
      if (Math.abs(resizedWidth + 10) > 1 || Math.abs(resizedHeight + 8) > 1) result.errors.push(`Button resize produced ${resizedWidth}×${resizedHeight} instead of -10×-8.`);
      if (!String(resized?.pending).includes("SuggestedWidth") || !String(resized?.pending).includes("SuggestedHeight")) result.errors.push("The fixed-size button resize was not staged as SuggestedWidth and SuggestedHeight.");
      if (Number(resized?.issueCount) !== 0) result.errors.push(`Resizing the button introduced ${resized.issueCount} browser diagnostic issue(s).`);
      await evaluateValue(cdp, `(() => { document.getElementById("undoCalibration")?.click(); return true; })()`);
      await delay(120);
      await settleFrames(cdp);
      const afterResizeUndo = await directManipulationState(cdp, setup.targetPath);
      if (afterResizeUndo.pending) result.errors.push("Undo did not clear the staged resize patch.");
      if (!rectMatches(afterResizeUndo.targetRect, setup.targetRect)) result.errors.push("Undo did not restore the button to its original bounds after resizing.");
      result.undoRedo = { moveRestored: rectMatches(afterMoveUndo.targetRect, setup.targetRect), resizeRestored: rectMatches(afterResizeUndo.targetRect, setup.targetRect), pendingCleared: !afterMoveUndo.pending && !afterResizeUndo.pending };
    }
  }

  result.sourceSha256After = await fileSha256(sourcePath);
  result.approvalBoundary = {
    sourceUnchanged: result.sourceSha256After === result.sourceSha256Before,
    explicitSourceControl: "applyCalibrationSource",
    explicitInstallControl: "applyCalibrationGame"
  };
  if (!result.approvalBoundary.sourceUnchanged) result.errors.push("A staged drag/resize interaction changed workspace XML without explicit approval.");
  return result;
}

async function directManipulationState(cdp, targetPath) {
  return evaluateValue(cdp, `(() => {
    const target = [...document.querySelectorAll("#stageContent .g-node")].find(node => node.__gauntlet?.path === ${JSON.stringify(targetPath)});
    const rect = target?.getBoundingClientRect();
    return {
      targetRect: rect ? { x: rect.x, y: rect.y, width: rect.width, height: rect.height } : null,
      pending: document.getElementById("calibrationPatchReview")?.value || "",
      issueCount: Number(document.getElementById("issueCount")?.textContent || 0),
      issues: [...document.querySelectorAll("#diagnosticList .diagnostic-item")].map(node => node.textContent?.trim() || "")
    };
  })()`);
}

async function dragMouse(cdp, start, end) {
  await cdp.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: start.x, y: start.y, button: "none" });
  await cdp.send("Input.dispatchMouseEvent", { type: "mousePressed", x: start.x, y: start.y, button: "left", buttons: 1, clickCount: 1 });
  await cdp.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: end.x, y: end.y, button: "left", buttons: 1 });
  await cdp.send("Input.dispatchMouseEvent", { type: "mouseReleased", x: end.x, y: end.y, button: "left", buttons: 0, clickCount: 1 });
}

function rectMatches(actual, expected, tolerance = 1) {
  return Boolean(actual && expected
    && Math.abs(Number(actual.x) - Number(expected.x)) <= tolerance
    && Math.abs(Number(actual.y) - Number(expected.y)) <= tolerance
    && Math.abs(Number(actual.width) - Number(expected.width)) <= tolerance
    && Math.abs(Number(actual.height) - Number(expected.height)) <= tolerance);
}

async function fileSha256(filePath) {
  const { createHash } = await import("node:crypto");
  return createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function buildReport() {
  return {
    schema: "reign-ui-rendered-preview-audit-v1",
    partialScope,
    requestedTargets,
    catalogInterfaceStateCount,
    catalogAugmentationCount,
    catalogPrefabCount,
    generatedUtc: new Date().toISOString(),
    previewBaseUrl,
    catalogPath,
    outputRoot,
    prefabCount,
    interfaceCount: interfaces.length,
    augmentationCount: augmentations.length,
    viewports,
    caseCount: cases.length,
    passedCount: cases.filter((entry) => !entry.errors.length).length,
    failedCount: cases.filter((entry) => entry.errors.length).length,
    parityUi,
    interactionContract,
    scaleContract,
    errors,
    cases
  };
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
