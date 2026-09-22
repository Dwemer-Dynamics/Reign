#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

const TOOL_VERSION = "2026.09.06.1";
const REPORT_SCHEMA = "reign-ui-approved-reference-fidelity-audit-v1";
const CONTRACT_SCHEMA = "reign-ui-approved-reference-fidelity-contract-v1";
const DEFAULT_TARGETS = [
  "ambassador",
  "calibration-overlay",
  "castle-chat",
  "castle-layout",
  "clan-accords",
  "correspondence",
  "court",
  "diplomacy-announcement",
  "economic-report",
  "family-chambers",
  "government",
  "individual-chat",
  "memories-book",
  "notable-generation",
  "party-chat",
  "royal-council",
  "social-event",
  "spymaster",
  "tavern-house",
  "war-council",
  "wilderness-event"
];
const toolPath = fileURLToPath(import.meta.url);
const toolRoot = path.dirname(toolPath);
const workspaceRoot = path.resolve(toolRoot, "../../..");

const DEFAULT_THRESHOLDS = Object.freeze({
  geometry: {
    maximumEdgeMismatch: 0.42,
    maximumObservedFrameAnisotropyRatio: 1.035,
    edgeSearchRadiusPixels: 4
  },
  material: {
    maximumMeanLuminanceDelta: 0.08,
    maximumTextureEnergyDelta: 0.055,
    maximumMeanSaturationDelta: 0.065
  },
  palette: {
    maximumTokenDistributionDistance: 0.24,
    maximumNearestTokenDistanceDelta: 0.055,
    maximumGoldOccupancyDelta: 0.045,
    maximumNeutralOccupancyDelta: 0.11
  },
  pixelDifference: {
    maximumMeanAbsoluteRgb: 0.16,
    maximumChangedPixelRatioAt32: 0.58
  },
  mask: {
    minimumStaticCoverageRatio: 0.28
  }
});

const SCREEN_PROFILES = Object.freeze({
  "diplomacy-announcement": {
    exclusions: [
      rect("dynamic-header-title-and-date", "dynamic-text", 405, 136, 862, 68),
      ellipse("left-runtime-portrait", "portrait-or-scene", 186, 242, 149, 246),
      ellipse("right-runtime-portrait", "portrait-or-scene", 1336, 242, 149, 246),
      rect("left-name-and-affiliation", "dynamic-text", 149, 497, 220, 75),
      rect("right-name-and-affiliation", "dynamic-text", 1302, 497, 219, 75),
      rect("left-runtime-banner", "unprovided-state", 224, 596, 72, 78),
      rect("right-runtime-banner", "unprovided-state", 1375, 596, 72, 78),
      rect("left-runtime-motive", "dynamic-text", 150, 701, 218, 78),
      rect("right-runtime-motive", "dynamic-text", 1303, 701, 217, 78),
      rect("proclamation-heading", "dynamic-text", 470, 278, 732, 49),
      rect("proclamation-body", "dynamic-text", 478, 351, 716, 102),
      rect("proclamation-terms", "dynamic-text", 478, 596, 716, 111),
      rect("acknowledge-label", "dynamic-text", 665, 804, 342, 37)
    ]
  },
  "memories-book": {
    exclusions: [
      rect("runtime-title", "dynamic-text", 603, 44, 466, 50),
      rect("runtime-page-counter", "unprovided-state", 1475, 45, 117, 40),
      rect("runtime-list-header", "dynamic-text", 80, 143, 411, 61),
      rect("runtime-memory-list", "list-or-runtime-content", 80, 215, 411, 573),
      rect("runtime-scroll-thumb", "unprovided-state", 497, 204, 19, 584),
      rect("runtime-memory-scene", "portrait-or-scene", 632, 130, 949, 555),
      rect("runtime-memory-caption", "dynamic-text", 676, 707, 860, 58),
      rect("previous-label", "dynamic-text", 236, 864, 194, 24),
      rect("close-label", "dynamic-text", 766, 864, 140, 24),
      rect("next-label", "dynamic-text", 1243, 864, 140, 24)
    ]
  },
  "castle-layout": {
    exclusions: [
      rect("runtime-castle-scene", "portrait-or-scene", 446, 179, 777, 656),
      ...castleOccupancyExclusions()
    ]
  },
  government: {
    exclusions: [
      rect("realm-title", "dynamic-text", 52, 34, 590, 42),
      rect("session-status", "dynamic-text", 1290, 40, 185, 27),
      rect("government-summary", "dynamic-text", 65, 97, 1535, 48),
      rect("party-list-entry-1", "list-or-runtime-content", 76, 230, 449, 113),
      rect("party-list-entry-2", "list-or-runtime-content", 76, 378, 449, 111),
      rect("party-list-entry-3", "list-or-runtime-content", 76, 527, 449, 111),
      rect("resolution-list", "list-or-runtime-content", 590, 225, 468, 142),
      rect("resolution-details", "dynamic-text", 588, 548, 467, 130),
      rect("member-list", "list-or-runtime-content", 1113, 225, 481, 143),
      rect("lobbying-details", "dynamic-text", 1112, 586, 481, 104),
      rect("government-footer-state", "dynamic-text", 48, 780, 650, 58)
    ]
  }
});

async function main() {
  const args = parseArguments(process.argv.slice(2));
  if (args.help) {
    process.stdout.write(helpText());
    return;
  }

  const outputRoot = path.resolve(args.output || path.join(workspaceRoot, ".codex-build", "ui-preview", "approved-reference-fidelity"));
  fs.mkdirSync(outputRoot, { recursive: true });

  if (args.selfTest) {
    const result = runSelfTest(outputRoot);
    process.stdout.write(`${JSON.stringify(result.consoleSummary, null, 2)}\n`);
    if (!result.ok) process.exitCode = 1;
    return;
  }

  if (!args.renderReport) {
    throw new Error("--render-report is required unless --self-test is used.");
  }

  const manifestPath = path.resolve(args.manifest || path.join(toolRoot, "../../artwork/ui-modern-style-kit/asset-manifest.json"));
  const renderReportPath = path.resolve(args.renderReport);
  const contractPath = args.contract ? path.resolve(args.contract) : null;
  const targetIds = args.targets?.length ? args.targets : DEFAULT_TARGETS;
  const report = runAudit({
    manifestPath,
    renderReportPath,
    contractPath,
    targetIds,
    viewport: args.viewport || "1920x1080",
    outputRoot,
    mode: "audit"
  });
  const reportPath = path.join(outputRoot, "approved-reference-fidelity-audit.json");
  writeJson(reportPath, report);
  process.stdout.write(`${JSON.stringify({
    schema: report.schema,
    ok: report.ok,
    passed: report.summary.passed,
    failed: report.summary.failed,
    blocked: report.summary.blocked,
    reportPath,
    sourceFingerprintSha256: report.sourceFingerprintSha256
  }, null, 2)}\n`);
  if (!report.ok && !args.reportOnly) process.exitCode = 1;
}

function runAudit({ manifestPath, renderReportPath, contractPath, targetIds, viewport, outputRoot, mode }) {
  const errors = [];
  const manifest = readJson(manifestPath, "asset manifest");
  const renderReport = readJson(renderReportPath, "render report");
  const contract = contractPath ? readJson(contractPath, "fidelity contract") : null;
  validateTopLevelInputs(manifest, renderReport, contract, targetIds);

  const manifestRoot = path.dirname(manifestPath);
  const palettePath = resolvePalettePath(manifestRoot, contract);
  const palette = readJson(palettePath, "modern palette");
  validatePalette(palette);
  const contractOverrides = normalizeContractOverrides(contract);
  const artifactsRoot = path.join(outputRoot, "artifacts");
  fs.mkdirSync(artifactsRoot, { recursive: true });

  const screens = [];
  for (const id of targetIds) {
    // A declared state inherits its parent's approved art and exact mask. It does
    // not create a new approval or widen the dynamic regions for the variant.
    const variant = (manifest.referenceVariants || []).find((entry) => entry.id === id);
    const referenceId = variant?.referenceId || id;
    const profile = mergeProfile(SCREEN_PROFILES[referenceId], contractOverrides.screens[referenceId]);
    const manifestEntry = manifest.references.find((entry) => entry.id === referenceId);
    const renderCases = renderReport.cases.filter((entry) => entry.interfaceId === id && entry.viewport === viewport);
    if (variant && renderCases.some((entry) => entry.fileName !== variant.fileName)) {
      throw new Error(`${id} does not render its declared approved-reference prefab ${variant.fileName}.`);
    }
    if (!manifestEntry || renderCases.length !== 1 || !profile) {
      const reasons = [];
      if (!manifestEntry) reasons.push(`No approved reference is declared for ${id}.`);
      if (renderCases.length !== 1) reasons.push(`Expected exactly one ${id} ${viewport} render case; found ${renderCases.length}.`);
      if (!profile) reasons.push(`No explicit static-shell mask profile exists for ${id}.`);
      screens.push(blockedScreen(id, viewport, reasons));
      errors.push(...reasons.map((reason) => `${id}: ${reason}`));
      continue;
    }
    try {
      const screen = auditScreen({
        id,
        viewport,
        profile,
        thresholds: mergeThresholds(DEFAULT_THRESHOLDS, profile.thresholds),
        manifestEntry,
        manifestRoot,
        renderCase: renderCases[0],
        renderReportRoot: path.dirname(renderReportPath),
        referenceCanvas: manifest.referenceCanvas,
        palette,
        artifactsRoot
      });
      screen.approvedReferenceId = referenceId;
      screens.push(screen);
      errors.push(...screen.failures.map((failure) => `${id}: ${failure}`));
    } catch (error) {
      const reason = error.message || String(error);
      screens.push(blockedScreen(id, viewport, [reason]));
      errors.push(`${id}: ${reason}`);
    }
  }

  const hashes = {
    toolSha256: sha256File(toolPath),
    manifestSha256: sha256File(manifestPath),
    renderReportSha256: sha256File(renderReportPath),
    paletteSha256: sha256File(palettePath),
    contractSha256: contractPath ? sha256File(contractPath) : sha256Text(stableStringify({
      schema: CONTRACT_SCHEMA,
      builtInProfiles: Object.fromEntries(targetIds.map((id) => [id, SCREEN_PROFILES[id]])),
      thresholds: DEFAULT_THRESHOLDS
    }))
  };
  const summary = {
    requested: targetIds.length,
    passed: screens.filter((screen) => screen.status === "passed").length,
    failed: screens.filter((screen) => screen.status === "failed").length,
    blocked: screens.filter((screen) => screen.status === "blocked").length
  };
  const sourceFingerprintSha256 = sha256Text(stableStringify({
    hashes,
    viewport,
    targets: screens.map((screen) => ({
      id: screen.id,
      referenceSha256: screen.reference?.actualSha256 || null,
      screenshotSha256: screen.rendered?.actualSha256 || null,
      maskSha256: screen.mask?.sha256 || null
    }))
  }));
  return {
    schema: REPORT_SCHEMA,
    version: TOOL_VERSION,
    mode,
    generatedUtc: new Date().toISOString(),
    ok: summary.failed === 0 && summary.blocked === 0,
    viewport,
    targetIds,
    referenceCanvas: manifest.referenceCanvas,
    inputs: {
      toolPath,
      manifestPath,
      renderReportPath,
      palettePath,
      contractPath,
      hashes
    },
    sourceFingerprintSha256,
    summary,
    screens,
    errors
  };
}

function auditScreen({ id, viewport, profile, thresholds, manifestEntry, manifestRoot, renderCase, renderReportRoot, referenceCanvas, palette, artifactsRoot }) {
  validateProfile(id, profile, referenceCanvas);
  const referencePath = resolveInside(manifestRoot, manifestEntry.path, "approved reference");
  const screenshotPath = path.isAbsolute(renderCase.screenshotPath)
    ? path.normalize(renderCase.screenshotPath)
    : resolveInside(renderReportRoot, renderCase.screenshotPath, "render screenshot");
  requireFile(referencePath, "approved reference");
  requireFile(screenshotPath, "render screenshot");
  const referenceSha256 = sha256File(referencePath);
  const screenshotSha256 = sha256File(screenshotPath);
  if (manifestEntry.sha256 && manifestEntry.sha256.toLowerCase() !== referenceSha256) {
    throw new Error(`Approved reference hash mismatch: manifest ${manifestEntry.sha256}, actual ${referenceSha256}.`);
  }
  if (renderCase.screenshotSha256 && renderCase.screenshotSha256.toLowerCase() !== screenshotSha256) {
    throw new Error(`Rendered screenshot hash mismatch: report ${renderCase.screenshotSha256}, actual ${screenshotSha256}.`);
  }

  const reference = decodePng(fs.readFileSync(referencePath));
  const screenshot = decodePng(fs.readFileSync(screenshotPath));
  if (reference.width !== Number(referenceCanvas.width) || reference.height !== Number(referenceCanvas.height)) {
    throw new Error(`Approved reference is ${reference.width}x${reference.height}; manifest requires ${referenceCanvas.width}x${referenceCanvas.height}.`);
  }
  const registration = registerRenderedSurface(reference, screenshot, renderCase.referenceSurfaceBounds);
  const normalized = resampleRegistered(screenshot, reference.width, reference.height, registration);
  const effectiveExclusions = [
    ...profile.exclusions,
    ...mapReportedDynamicExclusions(renderCase.fidelityDynamicExclusions, registration, reference.width, reference.height)
  ];
  const mask = buildStaticMask(reference.width, reference.height, effectiveExclusions);
  const maskCoverageRatio = countMask(mask) / mask.length;
  const metricComparison = buildMetricComparison(reference, screenshot, registration, mask, thresholds.geometry.edgeSearchRadiusPixels);
  const metrics = computeMetrics(metricComparison.reference, metricComparison.rendered, metricComparison.mask, palette.tokens, metricComparison.edgeSearchRadiusPixels);
  metrics.comparison = metricComparison.evidence;
  const failures = evaluateThresholds(metrics, thresholds, maskCoverageRatio);
  if (registration.observedFrameAnisotropyRatio > thresholds.geometry.maximumObservedFrameAnisotropyRatio) {
    failures.push(`geometry.observedFrameAnisotropyRatio ${registration.observedFrameAnisotropyRatio} exceeds maximum ${thresholds.geometry.maximumObservedFrameAnisotropyRatio}.`);
  }
  if (Number(renderCase.issueCount || 0) !== 0) failures.push(`Preview diagnostics reported ${renderCase.issueCount} issue(s).`);
  if (Array.isArray(renderCase.errors) && renderCase.errors.length) failures.push(`Preview render case contains ${renderCase.errors.length} error(s).`);

  const screenRoot = path.join(artifactsRoot, id);
  fs.mkdirSync(screenRoot, { recursive: true });
  const artifactImages = {
    staticMask: maskToImage(mask, reference.width, reference.height),
    normalizedRendered: normalized,
    alignedOverlay: overlayImage(reference, normalized, mask),
    differenceHeatmap: differenceHeatmap(reference, normalized, mask)
  };
  const artifactFiles = {};
  for (const [name, image] of Object.entries(artifactImages)) {
    const artifactPath = path.join(screenRoot, `${kebabCase(name)}.png`);
    fs.writeFileSync(artifactPath, encodePng(image));
    artifactFiles[name] = { path: artifactPath, sha256: sha256File(artifactPath) };
  }
  const excludedByCategory = summarizeExclusions(effectiveExclusions, reference.width, reference.height);
  const profileFingerprintSha256 = sha256Text(stableStringify({ exclusions: effectiveExclusions, thresholds }));
  return {
    id,
    status: failures.length ? "failed" : "passed",
    viewport,
    reference: {
      path: referencePath,
      width: reference.width,
      height: reference.height,
      expectedSha256: manifestEntry.sha256 || null,
      actualSha256: referenceSha256
    },
    rendered: {
      caseId: renderCase.caseId,
      screenshotPath,
      width: screenshot.width,
      height: screenshot.height,
      declaredSha256: renderCase.screenshotSha256 || null,
      actualSha256: screenshotSha256,
      issueCount: Number(renderCase.issueCount || 0),
      diagnostics: renderCase.diagnostics || null,
      widgetClippingDiagnostics: {
        passed: Number(renderCase.issueCount || 0) === 0 && !(renderCase.errors || []).length,
        issueCount: Number(renderCase.issueCount || 0),
        clippingIssues: (renderCase.diagnosticDetails || []).filter((entry) => /clip/i.test(`${entry?.kind || ""} ${entry?.message || ""}`)),
        source: "reign-ui-rendered-preview-audit-v1"
      },
      errors: renderCase.errors || []
    },
    registration,
    mask: {
      schema: "reign-ui-static-shell-mask-v1",
      profileFingerprintSha256,
      sha256: artifactFiles.staticMask.sha256,
      staticCoverageRatio: round(maskCoverageRatio),
      excludedCoverageRatio: round(1 - maskCoverageRatio),
      exclusions: effectiveExclusions,
      excludedByCategory
    },
    metrics,
    thresholds,
    artifacts: artifactFiles,
    failures
  };
}

function mapReportedDynamicExclusions(reportedRegions, registration, referenceWidth, referenceHeight) {
  if (reportedRegions == null) return [];
  if (!Array.isArray(reportedRegions)) throw new Error("Rendered-preview fidelityDynamicExclusions must be an array.");
  const crop = registration.sourceCrop;
  const padding = 4;
  return reportedRegions.map((region, index) => {
    if (region.coordinateSpace !== "screenshot" || region.shape !== "rectangle") {
      throw new Error(`Unsupported rendered-preview dynamic exclusion ${JSON.stringify(region)}.`);
    }
    const screenshotRect = {
      x: Number(region.x),
      y: Number(region.y),
      width: Number(region.width),
      height: Number(region.height)
    };
    if (![screenshotRect.x, screenshotRect.y, screenshotRect.width, screenshotRect.height].every(Number.isFinite)
        || screenshotRect.width <= 0
        || screenshotRect.height <= 0) {
      throw new Error(`Rendered-preview dynamic exclusion is invalid: ${JSON.stringify(region)}.`);
    }
    const x = ((screenshotRect.x - crop.x) / crop.width) * referenceWidth - padding;
    const y = ((screenshotRect.y - crop.y) / crop.height) * referenceHeight - padding;
    const width = (screenshotRect.width / crop.width) * referenceWidth + padding * 2;
    const height = (screenshotRect.height / crop.height) * referenceHeight + padding * 2;
    return {
      id: `rendered-${region.id || `dynamic-region-${index + 1}`}`,
      category: region.category || "unprovided-state",
      shape: "rectangle",
      x: round(x),
      y: round(y),
      width: round(width),
      height: round(height),
      source: "reign-ui-rendered-preview-audit-v1"
    };
  });
}

function registerRenderedSurface(reference, screenshot, reportedSurfaceBounds = null) {
  if (reportedSurfaceBounds != null) return registerReportedSurfaceBounds(reference, screenshot, reportedSurfaceBounds);
  const referenceSearch = { x: 0, y: 0, width: reference.width, height: reference.height };
  const liveSearch = {
    x: Math.floor(screenshot.width * 0.19),
    y: Math.floor(screenshot.height * 0.09),
    width: Math.ceil(screenshot.width * 0.58),
    height: Math.ceil(screenshot.height * 0.59)
  };
  const referenceFrame = findOuterGoldFrame(reference, referenceSearch);
  const initialLiveFrame = findOuterGoldFrame(screenshot, liveSearch);
  if (!referenceFrame) throw new Error("Could not locate the approved reference's outer antique-gold frame.");
  if (!initialLiveFrame) throw new Error("Could not locate the rendered Gauntlet surface's outer antique-gold frame inside the preview stage.");
  const liveFrame = refineLiveFrameVerticalBounds(referenceFrame, screenshot, initialLiveFrame);
  const observedScaleX = liveFrame.width / referenceFrame.width;
  const observedScaleY = liveFrame.height / referenceFrame.height;
  const uniformScale = Math.sqrt(observedScaleX * observedScaleY);
  const liveFrameCenterX = liveFrame.x + liveFrame.width / 2;
  const liveFrameCenterY = liveFrame.y + liveFrame.height / 2;
  const referenceFrameCenterX = referenceFrame.x + referenceFrame.width / 2;
  const referenceFrameCenterY = referenceFrame.y + referenceFrame.height / 2;
  const sourceCrop = {
    x: liveFrameCenterX - referenceFrameCenterX * uniformScale,
    y: liveFrameCenterY - referenceFrameCenterY * uniformScale,
    width: reference.width * uniformScale,
    height: reference.height * uniformScale
  };
  const coverage = intersectionArea(sourceCrop, { x: 0, y: 0, width: screenshot.width, height: screenshot.height }) / (sourceCrop.width * sourceCrop.height);
  if (coverage < 0.995) throw new Error(`Registered UI crop falls outside the screenshot (${round(coverage)} coverage).`);
  return {
    method: "outer-antique-gold-frame-projection-v2",
    freeformWarpApplied: false,
    transform: "translation-plus-uniform-scale",
    referenceFrameBounds: roundRect(referenceFrame),
    renderedFrameBounds: roundRect(liveFrame),
    sourceCrop: roundRect(sourceCrop),
    uniformScale: round(uniformScale),
    observedFrameScaleX: round(observedScaleX),
    observedFrameScaleY: round(observedScaleY),
    observedFrameAnisotropyRatio: round(Math.max(observedScaleX, observedScaleY) / Math.min(observedScaleX, observedScaleY)),
    screenshotCoverageRatio: round(coverage),
    detection: {
      reference: referenceFrame.detection,
      rendered: liveFrame.detection
    }
  };
}

function registerReportedSurfaceBounds(reference, screenshot, reported) {
  const sourceCrop = {
    x: Number(reported.x),
    y: Number(reported.y),
    width: Number(reported.width),
    height: Number(reported.height)
  };
  if (![sourceCrop.x, sourceCrop.y, sourceCrop.width, sourceCrop.height].every(Number.isFinite)
      || sourceCrop.width <= 0
      || sourceCrop.height <= 0) {
    throw new Error(`Rendered-preview referenceSurfaceBounds are invalid: ${JSON.stringify(reported)}.`);
  }
  const screenshotBounds = { x: 0, y: 0, width: screenshot.width, height: screenshot.height };
  const coverage = intersectionArea(sourceCrop, screenshotBounds) / (sourceCrop.width * sourceCrop.height);
  if (coverage < 0.995) throw new Error(`Rendered-preview reference surface falls outside the screenshot (${round(coverage)} coverage).`);
  const observedScaleX = sourceCrop.width / reference.width;
  const observedScaleY = sourceCrop.height / reference.height;
  const uniformScale = Math.sqrt(observedScaleX * observedScaleY);
  return {
    method: "rendered-preview-fixed-surface-bounds-v1",
    freeformWarpApplied: false,
    transform: "translation-plus-near-uniform-scale",
    referenceFrameBounds: { x: 0, y: 0, width: reference.width, height: reference.height },
    renderedFrameBounds: roundRect(sourceCrop),
    sourceCrop: roundRect(sourceCrop),
    uniformScale: round(uniformScale),
    observedFrameScaleX: round(observedScaleX),
    observedFrameScaleY: round(observedScaleY),
    observedFrameAnisotropyRatio: round(Math.max(observedScaleX, observedScaleY) / Math.min(observedScaleX, observedScaleY)),
    screenshotCoverageRatio: round(coverage),
    detection: {
      reference: {
        source: "approved-reference-canvas",
        width: reference.width,
        height: reference.height
      },
      rendered: {
        source: "reign-ui-rendered-preview-audit-v1",
        method: reported.method || null,
        elementId: reported.elementId || null,
        elementTag: reported.elementTag || null,
        elementPath: reported.elementPath || null,
        suggestedWidth: reported.suggestedWidth || null,
        suggestedHeight: reported.suggestedHeight || null,
        candidateCount: Number.isFinite(Number(reported.candidateCount)) ? Number(reported.candidateCount) : null
      }
    }
  };
}

function refineLiveFrameVerticalBounds(referenceFrame, image, frame) {
  const expectedScale = frame.width / referenceFrame.width;
  const expectedHeight = referenceFrame.height * expectedScale;
  const expectedBottom = frame.y + expectedHeight - 1;
  const tolerance = Math.max(8, Math.round(expectedHeight * 0.12));
  const y0 = clamp(Math.floor(expectedBottom - tolerance), frame.y + Math.floor(expectedHeight * 0.75), image.height - 1);
  const y1 = clamp(Math.ceil(expectedBottom + tolerance), y0, image.height - 1);
  const x0 = clamp(Math.floor(frame.x), 0, image.width - 1);
  const x1 = clamp(Math.ceil(frame.x + frame.width), x0 + 1, image.width);
  const edgeBand = Math.max(4, Math.round((x1 - x0) * 0.08));
  const minimumGoldPixels = Math.max(24, Math.floor((x1 - x0) * 0.25));
  let best = null;

  for (let y = y0; y <= y1; y += 1) {
    let goldPixels = 0;
    let leftEdgeGoldPixels = 0;
    let rightEdgeGoldPixels = 0;
    for (let x = x0; x < x1; x += 1) {
      const offset = (y * image.width + x) * 4;
      if (!isAntiqueGold(image.data[offset], image.data[offset + 1], image.data[offset + 2], image.data[offset + 3])) continue;
      goldPixels += 1;
      if (x < x0 + edgeBand) leftEdgeGoldPixels += 1;
      if (x >= x1 - edgeBand) rightEdgeGoldPixels += 1;
    }
    if (goldPixels < minimumGoldPixels || leftEdgeGoldPixels === 0 || rightEdgeGoldPixels === 0) continue;
    const distance = Math.abs(y - expectedBottom);
    if (!best || distance < best.distance || (distance === best.distance && goldPixels > best.goldPixels)) {
      best = { y, distance, goldPixels, leftEdgeGoldPixels, rightEdgeGoldPixels };
    }
  }

  if (!best) return frame;
  return {
    ...frame,
    height: best.y - frame.y + 1,
    detection: {
      ...frame.detection,
      verticalProjectionRefinement: {
        expectedBottom: round(expectedBottom),
        searchBounds: { x: x0, y: y0, width: x1 - x0, height: y1 - y0 + 1 },
        selectedBottom: best.y,
        selectedGoldPixels: best.goldPixels,
        selectedDistancePixels: round(best.distance),
        edgeBandPixels: edgeBand
      }
    }
  };
}

function findOuterGoldFrame(image, search) {
  const x0 = clamp(Math.floor(search.x), 0, image.width - 1);
  const y0 = clamp(Math.floor(search.y), 0, image.height - 1);
  const x1 = clamp(Math.ceil(search.x + search.width), x0 + 1, image.width);
  const y1 = clamp(Math.ceil(search.y + search.height), y0 + 1, image.height);
  const rowScores = new Uint32Array(y1 - y0);
  const columnScores = new Uint32Array(x1 - x0);
  for (let y = y0; y < y1; y += 1) {
    for (let x = x0; x < x1; x += 1) {
      const offset = (y * image.width + x) * 4;
      if (!isAntiqueGold(image.data[offset], image.data[offset + 1], image.data[offset + 2], image.data[offset + 3])) continue;
      rowScores[y - y0] += 1;
      columnScores[x - x0] += 1;
    }
  }
  const rowMax = Math.max(...rowScores);
  const rowThreshold = Math.max(24, Math.floor(rowMax * 0.28));
  const rowCandidates = indexesAtLeast(rowScores, rowThreshold).map((value) => value + y0);
  if (rowCandidates.length < 2) return null;
  const top = rowCandidates[0];
  const bottom = rowCandidates[rowCandidates.length - 1];
  if (bottom - top < Math.min(120, (y1 - y0) * 0.3)) return null;

  const constrainedColumns = new Uint32Array(x1 - x0);
  for (let y = top; y <= bottom; y += 1) {
    for (let x = x0; x < x1; x += 1) {
      const offset = (y * image.width + x) * 4;
      if (isAntiqueGold(image.data[offset], image.data[offset + 1], image.data[offset + 2], image.data[offset + 3])) constrainedColumns[x - x0] += 1;
    }
  }
  const columnMax = Math.max(...constrainedColumns);
  const columnThreshold = Math.max(20, Math.floor(columnMax * 0.25));
  const columnCandidates = indexesAtLeast(constrainedColumns, columnThreshold).map((value) => value + x0);
  if (columnCandidates.length < 2) return null;
  const left = columnCandidates[0];
  const right = columnCandidates[columnCandidates.length - 1];
  if (right - left < Math.min(240, (x1 - x0) * 0.3)) return null;
  return {
    x: left,
    y: top,
    width: right - left + 1,
    height: bottom - top + 1,
    detection: {
      searchBounds: { x: x0, y: y0, width: x1 - x0, height: y1 - y0 },
      rowPeakGoldPixels: rowMax,
      rowThresholdGoldPixels: rowThreshold,
      columnPeakGoldPixels: columnMax,
      columnThresholdGoldPixels: columnThreshold
    }
  };
}

function isAntiqueGold(r, g, b, a = 255) {
  if (a < 96 || r < 50 || g < 35) return false;
  if (b > g * 0.93 || g > r * 1.08) return false;
  return r - b >= 16 && g - b >= 8;
}

function resampleRegistered(source, targetWidth, targetHeight, registration) {
  const output = createImage(targetWidth, targetHeight);
  const crop = registration.sourceCrop;
  for (let y = 0; y < targetHeight; y += 1) {
    const sy = crop.y + ((y + 0.5) / targetHeight) * crop.height - 0.5;
    for (let x = 0; x < targetWidth; x += 1) {
      const sx = crop.x + ((x + 0.5) / targetWidth) * crop.width - 0.5;
      sampleBilinear(source, sx, sy, output.data, (y * targetWidth + x) * 4);
    }
  }
  return output;
}

function buildMetricComparison(reference, screenshot, registration, referenceMask, configuredEdgeRadius) {
  const crop = registration.sourceCrop;
  const width = Math.max(1, Math.min(reference.width, Math.round(Number(crop.width))));
  const height = Math.max(1, Math.min(reference.height, Math.round(Number(crop.height))));
  const referenceScaleX = width / reference.width;
  const referenceScaleY = height / reference.height;
  const edgeScale = Math.min(referenceScaleX, referenceScaleY);
  const edgeSearchRadiusPixels = Math.max(1, Math.round(Number(configuredEdgeRadius) * edgeScale));
  const fullReferenceRegistration = { sourceCrop: { x: 0, y: 0, width: reference.width, height: reference.height } };
  return {
    reference: resampleRegistered(reference, width, height, fullReferenceRegistration),
    rendered: resampleRegistered(screenshot, width, height, registration),
    mask: resampleStaticMaskConservatively(referenceMask, reference.width, reference.height, width, height),
    edgeSearchRadiusPixels,
    evidence: {
      width,
      height,
      referenceScaleX: round(referenceScaleX),
      referenceScaleY: round(referenceScaleY),
      edgeSearchRadiusPixels,
      referenceFilter: "bilinear-downsample-to-observed-surface",
      renderedFilter: "bilinear-direct-from-registered-crop",
      maskFilter: "conservative-any-exclusion"
    }
  };
}

function resampleStaticMaskConservatively(sourceMask, sourceWidth, sourceHeight, targetWidth, targetHeight) {
  if (sourceWidth === targetWidth && sourceHeight === targetHeight) return sourceMask.slice();
  const target = new Uint8Array(targetWidth * targetHeight);
  target.fill(1);
  for (let y = 0; y < targetHeight; y += 1) {
    const sourceY0 = clamp(Math.floor((y * sourceHeight) / targetHeight), 0, sourceHeight - 1);
    const sourceY1 = clamp(Math.ceil(((y + 1) * sourceHeight) / targetHeight), sourceY0 + 1, sourceHeight);
    for (let x = 0; x < targetWidth; x += 1) {
      const sourceX0 = clamp(Math.floor((x * sourceWidth) / targetWidth), 0, sourceWidth - 1);
      const sourceX1 = clamp(Math.ceil(((x + 1) * sourceWidth) / targetWidth), sourceX0 + 1, sourceWidth);
      let staticPixel = 1;
      for (let sourceY = sourceY0; sourceY < sourceY1 && staticPixel; sourceY += 1) {
        for (let sourceX = sourceX0; sourceX < sourceX1; sourceX += 1) {
          if (!sourceMask[sourceY * sourceWidth + sourceX]) {
            staticPixel = 0;
            break;
          }
        }
      }
      target[y * targetWidth + x] = staticPixel;
    }
  }
  return target;
}

function sampleBilinear(image, x, y, target, targetOffset) {
  const x0 = clamp(Math.floor(x), 0, image.width - 1);
  const y0 = clamp(Math.floor(y), 0, image.height - 1);
  const x1 = clamp(x0 + 1, 0, image.width - 1);
  const y1 = clamp(y0 + 1, 0, image.height - 1);
  const tx = clamp(x - Math.floor(x), 0, 1);
  const ty = clamp(y - Math.floor(y), 0, 1);
  const offsets = [
    (y0 * image.width + x0) * 4,
    (y0 * image.width + x1) * 4,
    (y1 * image.width + x0) * 4,
    (y1 * image.width + x1) * 4
  ];
  for (let channel = 0; channel < 4; channel += 1) {
    const top = image.data[offsets[0] + channel] * (1 - tx) + image.data[offsets[1] + channel] * tx;
    const bottom = image.data[offsets[2] + channel] * (1 - tx) + image.data[offsets[3] + channel] * tx;
    target[targetOffset + channel] = Math.round(top * (1 - ty) + bottom * ty);
  }
}

function buildStaticMask(width, height, exclusions) {
  const mask = new Uint8Array(width * height);
  mask.fill(1);
  for (const region of exclusions) {
    const x0 = clamp(Math.floor(region.x), 0, width);
    const y0 = clamp(Math.floor(region.y), 0, height);
    const x1 = clamp(Math.ceil(region.x + region.width), 0, width);
    const y1 = clamp(Math.ceil(region.y + region.height), 0, height);
    const cx = region.x + region.width / 2;
    const cy = region.y + region.height / 2;
    const rx = Math.max(0.5, region.width / 2);
    const ry = Math.max(0.5, region.height / 2);
    for (let y = y0; y < y1; y += 1) {
      for (let x = x0; x < x1; x += 1) {
        if (region.shape === "ellipse") {
          const dx = (x + 0.5 - cx) / rx;
          const dy = (y + 0.5 - cy) / ry;
          if (dx * dx + dy * dy > 1) continue;
        }
        mask[y * width + x] = 0;
      }
    }
  }
  return mask;
}

function computeMetrics(reference, live, mask, paletteTokens, edgeRadius) {
  const referenceMaterial = materialMetrics(reference, mask);
  const liveMaterial = materialMetrics(live, mask);
  const referencePalette = paletteMetrics(reference, mask, paletteTokens);
  const livePalette = paletteMetrics(live, mask, paletteTokens);
  const pixelDifference = pixelDifferenceMetrics(reference, live, mask);
  const geometry = geometryMetrics(reference, live, mask, edgeRadius);
  return {
    geometry,
    material: {
      reference: referenceMaterial,
      rendered: liveMaterial,
      meanLuminanceDelta: round(Math.abs(referenceMaterial.meanLuminance - liveMaterial.meanLuminance)),
      textureEnergyDelta: round(Math.abs(referenceMaterial.textureEnergy - liveMaterial.textureEnergy)),
      meanSaturationDelta: round(Math.abs(referenceMaterial.meanSaturation - liveMaterial.meanSaturation))
    },
    palette: {
      reference: referencePalette,
      rendered: livePalette,
      tokenDistributionDistance: round(distributionDistance(referencePalette.tokenOccupancy, livePalette.tokenOccupancy)),
      nearestTokenDistanceDelta: round(Math.abs(referencePalette.meanNearestTokenDistance - livePalette.meanNearestTokenDistance)),
      goldOccupancyDelta: round(Math.abs(referencePalette.goldOccupancyRatio - livePalette.goldOccupancyRatio)),
      neutralOccupancyDelta: round(Math.abs(referencePalette.neutralOccupancyRatio - livePalette.neutralOccupancyRatio))
    },
    pixelDifference
  };
}

function materialMetrics(image, mask) {
  let count = 0;
  let luminanceTotal = 0;
  let saturationTotal = 0;
  let textureTotal = 0;
  let textureCount = 0;
  for (let y = 0; y < image.height; y += 1) {
    for (let x = 0; x < image.width; x += 1) {
      const index = y * image.width + x;
      if (!mask[index]) continue;
      const offset = index * 4;
      const lum = luminance(image.data[offset], image.data[offset + 1], image.data[offset + 2]);
      luminanceTotal += lum;
      // Hue noise in near-black pixels produces unstable HSV saturation after
      // browser downsampling. Weight saturation by visible luminance so the
      // material metric measures perceptible color rather than dark-channel noise.
      saturationTotal += saturation(image.data[offset], image.data[offset + 1], image.data[offset + 2]) * Math.sqrt(lum);
      count += 1;
      if (x + 1 < image.width && mask[index + 1]) {
        const right = offset + 4;
        textureTotal += Math.abs(lum - luminance(image.data[right], image.data[right + 1], image.data[right + 2]));
        textureCount += 1;
      }
      if (y + 1 < image.height && mask[index + image.width]) {
        const down = offset + image.width * 4;
        textureTotal += Math.abs(lum - luminance(image.data[down], image.data[down + 1], image.data[down + 2]));
        textureCount += 1;
      }
    }
  }
  return {
    meanLuminance: round(luminanceTotal / Math.max(1, count)),
    meanSaturation: round(saturationTotal / Math.max(1, count)),
    textureEnergy: round(textureTotal / Math.max(1, textureCount))
  };
}

function paletteMetrics(image, mask, tokenObject) {
  const tokens = Object.entries(tokenObject).map(([name, hex]) => ({ name, rgba: parseHexColor(hex) }));
  const counts = Object.fromEntries(tokens.map((token) => [token.name, 0]));
  let total = 0;
  let nearestDistanceTotal = 0;
  for (let index = 0; index < mask.length; index += 1) {
    if (!mask[index]) continue;
    const offset = index * 4;
    let nearest = null;
    let nearestDistance = Number.POSITIVE_INFINITY;
    for (const token of tokens) {
      const distance = rgbDistance(image.data[offset], image.data[offset + 1], image.data[offset + 2], token.rgba[0], token.rgba[1], token.rgba[2]);
      if (distance < nearestDistance) {
        nearestDistance = distance;
        nearest = token;
      }
    }
    counts[nearest.name] += 1;
    nearestDistanceTotal += nearestDistance;
    total += 1;
  }
  const tokenOccupancy = Object.fromEntries(Object.entries(counts).map(([name, count]) => [name, round(count / Math.max(1, total))]));
  const goldNames = new Set(["borderBronze", "goldAntique", "goldHighlight", "goldBright"]);
  const neutralNames = new Set(["canvasVoid", "canvasBlack", "marbleBase", "marbleShadow", "panelSmoke", "panelStandard", "panelRaised", "panelActive", "inputBlack"]);
  return {
    tokenOccupancy,
    meanNearestTokenDistance: round(nearestDistanceTotal / Math.max(1, total)),
    goldOccupancyRatio: round(sumNamedOccupancy(tokenOccupancy, goldNames)),
    neutralOccupancyRatio: round(sumNamedOccupancy(tokenOccupancy, neutralNames))
  };
}

function pixelDifferenceMetrics(reference, live, mask) {
  let count = 0;
  let absoluteTotal = 0;
  let squaredTotal = 0;
  let changed = 0;
  const magnitudes = [];
  for (let index = 0; index < mask.length; index += 1) {
    if (!mask[index]) continue;
    const offset = index * 4;
    const dr = Math.abs(reference.data[offset] - live.data[offset]);
    const dg = Math.abs(reference.data[offset + 1] - live.data[offset + 1]);
    const db = Math.abs(reference.data[offset + 2] - live.data[offset + 2]);
    const mean = (dr + dg + db) / (3 * 255);
    absoluteTotal += mean;
    squaredTotal += (dr * dr + dg * dg + db * db) / (3 * 255 * 255);
    const maximum = Math.max(dr, dg, db);
    if (maximum > 32) changed += 1;
    magnitudes.push(maximum / 255);
    count += 1;
  }
  magnitudes.sort((a, b) => a - b);
  return {
    meanAbsoluteRgb: round(absoluteTotal / Math.max(1, count)),
    rootMeanSquareRgb: round(Math.sqrt(squaredTotal / Math.max(1, count))),
    percentile95MaximumChannel: round(magnitudes[Math.min(magnitudes.length - 1, Math.floor(magnitudes.length * 0.95))] || 0),
    changedPixelRatioAt32: round(changed / Math.max(1, count)),
    comparedPixelCount: count
  };
}

function geometryMetrics(reference, live, mask, radius) {
  const referenceEdges = edgeMap(reference, mask);
  const liveEdges = edgeMap(live, mask);
  const dilatedReference = dilate(referenceEdges.map, reference.width, reference.height, radius);
  const dilatedLive = dilate(liveEdges.map, live.width, live.height, radius);
  let referenceMatched = 0;
  let liveMatched = 0;
  for (let index = 0; index < mask.length; index += 1) {
    if (!mask[index]) continue;
    if (referenceEdges.map[index] && dilatedLive[index]) referenceMatched += 1;
    if (liveEdges.map[index] && dilatedReference[index]) liveMatched += 1;
  }
  const recall = referenceMatched / Math.max(1, referenceEdges.count);
  const precision = liveMatched / Math.max(1, liveEdges.count);
  const f1 = recall + precision ? (2 * recall * precision) / (recall + precision) : 0;
  return {
    edgeSearchRadiusPixels: radius,
    referenceEdgeThreshold: round(referenceEdges.threshold),
    renderedEdgeThreshold: round(liveEdges.threshold),
    referenceEdgePixelCount: referenceEdges.count,
    renderedEdgePixelCount: liveEdges.count,
    referenceEdgeRecall: round(recall),
    renderedEdgePrecision: round(precision),
    edgeF1: round(f1),
    edgeMismatch: round(1 - f1)
  };
}

function edgeMap(image, mask) {
  const strengths = new Float32Array(image.width * image.height);
  const candidates = [];
  for (let y = 1; y < image.height - 1; y += 1) {
    for (let x = 1; x < image.width - 1; x += 1) {
      const index = y * image.width + x;
      if (!mask[index]) continue;
      const left = pixelLuminance(image, index - 1);
      const right = pixelLuminance(image, index + 1);
      const up = pixelLuminance(image, index - image.width);
      const down = pixelLuminance(image, index + image.width);
      const strength = Math.sqrt((right - left) ** 2 + (down - up) ** 2) / Math.SQRT2;
      strengths[index] = strength;
      if (strength > 0.018) candidates.push(strength);
    }
  }
  candidates.sort((a, b) => a - b);
  const adaptive = candidates[Math.floor(candidates.length * 0.78)] || 0;
  const threshold = clamp(adaptive, 0.045, 0.15);
  const map = new Uint8Array(mask.length);
  let count = 0;
  for (let index = 0; index < map.length; index += 1) {
    if (mask[index] && strengths[index] >= threshold) {
      map[index] = 1;
      count += 1;
    }
  }
  return { map, count, threshold };
}

function dilate(source, width, height, radius) {
  const horizontal = new Uint8Array(source.length);
  const output = new Uint8Array(source.length);
  for (let y = 0; y < height; y += 1) {
    let count = 0;
    for (let x = -radius; x < width + radius; x += 1) {
      const added = x + radius;
      const removed = x - radius - 1;
      if (added >= 0 && added < width) count += source[y * width + added];
      if (removed >= 0 && removed < width) count -= source[y * width + removed];
      if (x >= 0 && x < width) horizontal[y * width + x] = count > 0 ? 1 : 0;
    }
  }
  for (let x = 0; x < width; x += 1) {
    let count = 0;
    for (let y = -radius; y < height + radius; y += 1) {
      const added = y + radius;
      const removed = y - radius - 1;
      if (added >= 0 && added < height) count += horizontal[added * width + x];
      if (removed >= 0 && removed < height) count -= horizontal[removed * width + x];
      if (y >= 0 && y < height) output[y * width + x] = count > 0 ? 1 : 0;
    }
  }
  return output;
}

function evaluateThresholds(metrics, thresholds, maskCoverageRatio) {
  const failures = [];
  thresholdFailure(failures, "geometry.edgeMismatch", metrics.geometry.edgeMismatch, thresholds.geometry.maximumEdgeMismatch);
  thresholdFailure(failures, "material.meanLuminanceDelta", metrics.material.meanLuminanceDelta, thresholds.material.maximumMeanLuminanceDelta);
  thresholdFailure(failures, "material.textureEnergyDelta", metrics.material.textureEnergyDelta, thresholds.material.maximumTextureEnergyDelta);
  thresholdFailure(failures, "material.meanSaturationDelta", metrics.material.meanSaturationDelta, thresholds.material.maximumMeanSaturationDelta);
  thresholdFailure(failures, "palette.tokenDistributionDistance", metrics.palette.tokenDistributionDistance, thresholds.palette.maximumTokenDistributionDistance);
  thresholdFailure(failures, "palette.nearestTokenDistanceDelta", metrics.palette.nearestTokenDistanceDelta, thresholds.palette.maximumNearestTokenDistanceDelta);
  thresholdFailure(failures, "palette.goldOccupancyDelta", metrics.palette.goldOccupancyDelta, thresholds.palette.maximumGoldOccupancyDelta);
  thresholdFailure(failures, "palette.neutralOccupancyDelta", metrics.palette.neutralOccupancyDelta, thresholds.palette.maximumNeutralOccupancyDelta);
  thresholdFailure(failures, "pixelDifference.meanAbsoluteRgb", metrics.pixelDifference.meanAbsoluteRgb, thresholds.pixelDifference.maximumMeanAbsoluteRgb);
  thresholdFailure(failures, "pixelDifference.changedPixelRatioAt32", metrics.pixelDifference.changedPixelRatioAt32, thresholds.pixelDifference.maximumChangedPixelRatioAt32);
  if (maskCoverageRatio < thresholds.mask.minimumStaticCoverageRatio) {
    failures.push(`mask.staticCoverageRatio ${round(maskCoverageRatio)} is below minimum ${thresholds.mask.minimumStaticCoverageRatio}.`);
  }
  return failures;
}

function thresholdFailure(failures, label, value, maximum) {
  if (value > maximum) failures.push(`${label} ${value} exceeds maximum ${maximum}.`);
}

function maskToImage(mask, width, height) {
  const image = createImage(width, height);
  for (let index = 0; index < mask.length; index += 1) {
    const offset = index * 4;
    const value = mask[index] ? 255 : 0;
    image.data[offset] = value;
    image.data[offset + 1] = value;
    image.data[offset + 2] = value;
    image.data[offset + 3] = 255;
  }
  return image;
}

function overlayImage(reference, live, mask) {
  const image = createImage(reference.width, reference.height);
  for (let index = 0; index < mask.length; index += 1) {
    const offset = index * 4;
    if (mask[index]) {
      image.data[offset] = Math.round((reference.data[offset] + live.data[offset]) / 2);
      image.data[offset + 1] = Math.round((reference.data[offset + 1] + live.data[offset + 1]) / 2);
      image.data[offset + 2] = Math.round((reference.data[offset + 2] + live.data[offset + 2]) / 2);
    } else {
      image.data[offset] = Math.round(live.data[offset] * 0.18);
      image.data[offset + 1] = Math.round(live.data[offset + 1] * 0.18 + 24);
      image.data[offset + 2] = Math.round(live.data[offset + 2] * 0.18 + 38);
    }
    image.data[offset + 3] = 255;
  }
  return image;
}

function differenceHeatmap(reference, live, mask) {
  const image = createImage(reference.width, reference.height);
  for (let index = 0; index < mask.length; index += 1) {
    const offset = index * 4;
    if (!mask[index]) {
      image.data[offset] = 12;
      image.data[offset + 1] = 24;
      image.data[offset + 2] = 38;
      image.data[offset + 3] = 255;
      continue;
    }
    const difference = Math.max(
      Math.abs(reference.data[offset] - live.data[offset]),
      Math.abs(reference.data[offset + 1] - live.data[offset + 1]),
      Math.abs(reference.data[offset + 2] - live.data[offset + 2])
    ) / 255;
    image.data[offset] = Math.round(255 * Math.min(1, difference * 2.2));
    image.data[offset + 1] = Math.round(255 * Math.max(0, 1 - Math.abs(difference * 2.2 - 0.55) * 2));
    image.data[offset + 2] = Math.round(48 * (1 - difference));
    image.data[offset + 3] = 255;
  }
  return image;
}

function validateTopLevelInputs(manifest, renderReport, contract, targetIds) {
  if (manifest.schema !== "reign-ui-modern-style-kit-v1") throw new Error(`Unsupported asset manifest schema ${manifest.schema || "missing"}.`);
  if (!manifest.referenceCanvas?.width || !manifest.referenceCanvas?.height) throw new Error("Asset manifest has no referenceCanvas.");
  if (!Array.isArray(manifest.references)) throw new Error("Asset manifest references must be an array.");
  if (renderReport.schema !== "reign-ui-rendered-preview-audit-v1") throw new Error(`Unsupported render report schema ${renderReport.schema || "missing"}.`);
  if (!Array.isArray(renderReport.cases)) throw new Error("Render report cases must be an array.");
  if (contract && contract.schema !== CONTRACT_SCHEMA) throw new Error(`Unsupported fidelity contract schema ${contract.schema || "missing"}.`);
  for (const id of targetIds) {
    if (!/^[a-z0-9-]+$/.test(id)) throw new Error(`Invalid target id ${id}.`);
  }
}

function validatePalette(palette) {
  if (palette.schema !== "reign-ui-modern-palette-v1") throw new Error(`Unsupported palette schema ${palette.schema || "missing"}.`);
  if (!palette.tokens || typeof palette.tokens !== "object") throw new Error("Palette has no tokens object.");
  for (const [name, value] of Object.entries(palette.tokens)) {
    if (!/^#[0-9a-f]{8}$/i.test(value)) throw new Error(`Palette token ${name} is not #RRGGBBAA.`);
  }
}

function validateProfile(id, profile, referenceCanvas) {
  if (!Array.isArray(profile.exclusions)) throw new Error(`${id} mask exclusions must be an array.`);
  const allowedCategories = new Set(["dynamic-text", "portrait-or-scene", "list-or-runtime-content", "unprovided-state"]);
  const seenIds = new Set();
  for (const region of profile.exclusions) {
    if (!region?.id || seenIds.has(region.id)) throw new Error(`${id} mask exclusions require unique non-empty ids.`);
    seenIds.add(region.id);
    if (!allowedCategories.has(region.category)) throw new Error(`${id}/${region.id} uses unsupported category ${region.category || "missing"}.`);
    if (!["rectangle", "ellipse"].includes(region.shape)) throw new Error(`${id}/${region.id} uses unsupported shape ${region.shape || "missing"}.`);
    for (const field of ["x", "y", "width", "height"]) {
      if (!Number.isFinite(region[field])) throw new Error(`${id}/${region.id}.${field} must be finite.`);
    }
    if (region.width <= 0 || region.height <= 0) throw new Error(`${id}/${region.id} dimensions must be positive.`);
    if (region.x < 0 || region.y < 0 || region.x + region.width > referenceCanvas.width || region.y + region.height > referenceCanvas.height) {
      throw new Error(`${id}/${region.id} falls outside ${referenceCanvas.width}x${referenceCanvas.height}.`);
    }
  }
  validateThresholdTree(id, profile.thresholds || {});
}

function validateThresholdTree(id, value, prefix = "thresholds") {
  for (const [key, child] of Object.entries(value)) {
    if (isPlainObject(child)) validateThresholdTree(id, child, `${prefix}.${key}`);
    else if (!Number.isFinite(child) || child < 0) throw new Error(`${id} ${prefix}.${key} must be a non-negative finite number.`);
  }
}

function resolvePalettePath(manifestRoot, contract) {
  if (contract?.palettePath) return resolveInside(path.dirname(contract.__path || manifestRoot), contract.palettePath, "palette");
  return path.join(manifestRoot, "palette.json");
}

function normalizeContractOverrides(contract) {
  if (!contract) return { screens: {} };
  const screens = {};
  for (const screen of contract.screens || []) {
    if (!screen?.id) throw new Error("Every fidelity contract screen requires an id.");
    if (screens[screen.id]) throw new Error(`Fidelity contract contains duplicate screen id ${screen.id}.`);
    const fixedRegionExclusions = screen.fixedRegionSpec
      ? loadFixedRegionExclusions(contract, screen)
      : [];
    screens[screen.id] = {
      ...screen,
      exclusions: [...fixedRegionExclusions, ...(screen.exclusions || [])]
    };
  }
  return { screens };
}

function loadFixedRegionExclusions(contract, screen) {
  if (typeof screen.fixedRegionSpec !== "string" || !screen.fixedRegionSpec.trim()) {
    throw new Error(`${screen.id}.fixedRegionSpec must be a non-empty relative path.`);
  }
  const contractRoot = path.dirname(contract.__path || workspaceRoot);
  const specPath = path.resolve(contractRoot, screen.fixedRegionSpec);
  const relativeToWorkspace = path.relative(workspaceRoot, specPath);
  if (relativeToWorkspace.startsWith("..") || path.isAbsolute(relativeToWorkspace)) {
    throw new Error(`${screen.id}.fixedRegionSpec escapes the Reign workspace: ${screen.fixedRegionSpec}.`);
  }
  const spec = readJson(specPath, `${screen.id} fixed-region spec`);
  if (spec.schema !== "reign-fixed-region-composite-v1") {
    throw new Error(`${screen.id}.fixedRegionSpec uses unsupported schema ${spec.schema || "missing"}.`);
  }
  if (!Array.isArray(spec.sceneDependentMasks) || !spec.sceneDependentMasks.length) {
    throw new Error(`${screen.id}.fixedRegionSpec has no sceneDependentMasks.`);
  }
  const categoryOverrides = screen.categoryOverrides || {};
  return spec.sceneDependentMasks.map((region) => fixedRegionMaskToExclusion(region, categoryOverrides));
}

function fixedRegionMaskToExclusion(region, categoryOverrides) {
  if (!region?.id || !region.shape) throw new Error("Fixed-region masks require an id and shape.");
  const shape = String(region.shape).toLowerCase();
  const category = categoryOverrides[region.id] || inferExclusionCategory(region.id, shape);
  if (shape === "rectangle" || shape === "ellipse") {
    return {
      id: region.id,
      category,
      shape,
      x: region.x,
      y: region.y,
      width: region.width,
      height: region.height
    };
  }
  if (shape === "polygon") {
    if (!Array.isArray(region.points) || region.points.length < 3) throw new Error(`${region.id}.points must contain at least three points.`);
    const xs = region.points.map((point) => Number(point?.[0]));
    const ys = region.points.map((point) => Number(point?.[1]));
    if ([...xs, ...ys].some((value) => !Number.isFinite(value))) throw new Error(`${region.id}.points must be finite coordinate pairs.`);
    const left = Math.min(...xs);
    const top = Math.min(...ys);
    const right = Math.max(...xs);
    const bottom = Math.max(...ys);
    return { id: region.id, category, shape: "rectangle", x: left, y: top, width: right - left, height: bottom - top };
  }
  throw new Error(`${region.id} uses unsupported fixed-region shape ${shape}.`);
}

function inferExclusionCategory(identifier, shape) {
  const id = String(identifier).toLowerCase();
  if (shape === "ellipse" || /(portrait|scene|art|banner|herald|map|crest|image)/.test(id)) return "portrait-or-scene";
  if (/(list|roster|chat|transcript|directory|entries|items|cards|rows|grid|slots)/.test(id)) return "list-or-runtime-content";
  if (/(scroll|progress|state|indicator|meter)/.test(id)) return "unprovided-state";
  return "dynamic-text";
}

function mergeProfile(base, override) {
  if (!base && !override) return null;
  return {
    exclusions: override?.exclusions || base?.exclusions || [],
    thresholds: mergeThresholds(base?.thresholds || {}, override?.thresholds || {})
  };
}

function mergeThresholds(base, override) {
  const output = {};
  for (const key of new Set([...Object.keys(base || {}), ...Object.keys(override || {})])) {
    const left = base?.[key];
    const right = override?.[key];
    if (isPlainObject(left) || isPlainObject(right)) output[key] = mergeThresholds(isPlainObject(left) ? left : {}, isPlainObject(right) ? right : {});
    else output[key] = right ?? left;
  }
  return output;
}

function blockedScreen(id, viewport, failures) {
  return { id, status: "blocked", viewport, failures };
}

function summarizeExclusions(exclusions, width, height) {
  const categories = {};
  for (const exclusion of exclusions) {
    if (!categories[exclusion.category]) categories[exclusion.category] = { regionCount: 0, nominalAreaRatio: 0 };
    categories[exclusion.category].regionCount += 1;
    const area = exclusion.shape === "ellipse"
      ? Math.PI * exclusion.width * exclusion.height / 4
      : exclusion.width * exclusion.height;
    categories[exclusion.category].nominalAreaRatio += area / (width * height);
  }
  for (const value of Object.values(categories)) value.nominalAreaRatio = round(value.nominalAreaRatio);
  return categories;
}

function castleOccupancyExclusions() {
  const output = [];
  const leftXs = [136, 205, 275, 345];
  const leftYs = [145, 242, 339, 436, 533, 630, 727, 824];
  for (let row = 0; row < leftYs.length; row += 1) {
    for (let column = 0; column < leftXs.length; column += 1) {
      output.push(ellipse(`left-occupancy-${row + 1}-${column + 1}`, "list-or-runtime-content", leftXs[column] - 21, leftYs[row] - 21, 42, 42));
    }
  }
  const fourXs = [1330, 1400, 1470, 1540];
  for (const [name, y] of [["main-hall", 145], ["royal-bedroom", 242], ["chapel", 339], ["throne-room", 630], ["baths", 727], ["battlements", 824]]) {
    for (let column = 0; column < fourXs.length; column += 1) {
      output.push(ellipse(`${name}-${column + 1}`, "list-or-runtime-content", fourXs[column] - 21, y - 21, 42, 42));
    }
  }
  const guestXs = [1325, 1379, 1433, 1487, 1541];
  const guestYs = [436, 486, 536];
  for (let row = 0; row < guestYs.length; row += 1) {
    for (let column = 0; column < guestXs.length; column += 1) {
      output.push(ellipse(`guest-bedroom-${row + 1}-${column + 1}`, "unprovided-state", guestXs[column] - 19, guestYs[row] - 19, 38, 38));
    }
  }
  return output;
}

function rect(id, category, x, y, width, height) {
  return { id, category, shape: "rectangle", x, y, width, height };
}

function ellipse(id, category, x, y, width, height) {
  return { id, category, shape: "ellipse", x, y, width, height };
}

function runSelfTest(outputRoot) {
  const fixtureRoot = path.join(outputRoot, "self-test-fixtures");
  const artifactsRoot = path.join(outputRoot, "self-test-artifacts");
  fs.mkdirSync(fixtureRoot, { recursive: true });
  fs.mkdirSync(artifactsRoot, { recursive: true });
  const palette = selfTestPalette();
  const reference = syntheticShell(320, 180, false, false);
  const passingSurface = syntheticShell(320, 180, true, false);
  const failingSurface = syntheticShell(320, 180, true, true);
  const passingScreenshot = embedInPreviewChrome(passingSurface, 640, 360, { x: 160, y: 45, width: 320, height: 180 });
  const failingScreenshot = embedInPreviewChrome(failingSurface, 640, 360, { x: 160, y: 45, width: 320, height: 180 });
  const files = {
    reference: path.join(fixtureRoot, "reference.png"),
    passing: path.join(fixtureRoot, "passing-preview.png"),
    failing: path.join(fixtureRoot, "failing-preview.png")
  };
  fs.writeFileSync(files.reference, encodePng(reference));
  fs.writeFileSync(files.passing, encodePng(passingScreenshot));
  fs.writeFileSync(files.failing, encodePng(failingScreenshot));
  const exclusions = [
    rect("dynamic-text-fixture", "dynamic-text", 96, 68, 128, 44),
    rect("portrait-fixture", "portrait-or-scene", 35, 60, 40, 50),
    rect("list-fixture", "list-or-runtime-content", 245, 55, 38, 58),
    rect("unprovided-fixture", "unprovided-state", 130, 130, 60, 18)
  ];
  const profile = { exclusions };
  const manifestEntry = { id: "self-test", path: "reference.png", sha256: sha256File(files.reference) };
  const makeCase = (caseId, screenshotPath) => ({
    caseId,
    interfaceId: "self-test",
    viewport: "640x360",
    screenshotPath,
    screenshotSha256: sha256File(screenshotPath),
    referenceSurfaceBounds: {
      method: "self-test-fixed-surface",
      x: 160,
      y: 45,
      width: 320,
      height: 180,
      elementId: "SelfTestSurface",
      elementTag: "Widget",
      elementPath: "/Prefab/Window/Widget[1]",
      suggestedWidth: "320",
      suggestedHeight: "180",
      candidateCount: 1
    },
    issueCount: 0,
    errors: []
  });
  const common = {
    id: "self-test",
    viewport: "640x360",
    profile,
    thresholds: mergeThresholds(DEFAULT_THRESHOLDS, {
      geometry: { maximumEdgeMismatch: 0.07 },
      material: { maximumMeanLuminanceDelta: 0.025, maximumTextureEnergyDelta: 0.03, maximumMeanSaturationDelta: 0.03 },
      palette: { maximumTokenDistributionDistance: 0.08, maximumNearestTokenDistanceDelta: 0.03, maximumGoldOccupancyDelta: 0.025, maximumNeutralOccupancyDelta: 0.08 },
      pixelDifference: { maximumMeanAbsoluteRgb: 0.035, maximumChangedPixelRatioAt32: 0.06 }
    }),
    manifestEntry,
    manifestRoot: fixtureRoot,
    renderReportRoot: fixtureRoot,
    referenceCanvas: { width: 320, height: 180 },
    palette,
    artifactsRoot
  };
  const passing = auditScreen({ ...common, renderCase: makeCase("self-test-pass", files.passing) });
  const failing = auditScreen({ ...common, renderCase: makeCase("self-test-fail", files.failing) });
  let referenceTamperRejected = false;
  let screenshotTamperRejected = false;
  let invalidSurfaceBoundsRejected = false;
  try {
    auditScreen({ ...common, manifestEntry: { ...manifestEntry, sha256: "0".repeat(64) }, renderCase: makeCase("self-test-reference-tamper", files.passing) });
  } catch (error) {
    referenceTamperRejected = /reference hash mismatch/i.test(error.message || String(error));
  }
  try {
    auditScreen({ ...common, renderCase: { ...makeCase("self-test-screenshot-tamper", files.passing), screenshotSha256: "0".repeat(64) } });
  } catch (error) {
    screenshotTamperRejected = /screenshot hash mismatch/i.test(error.message || String(error));
  }
  try {
    auditScreen({ ...common, renderCase: { ...makeCase("self-test-invalid-surface", files.passing), referenceSurfaceBounds: { x: 700, y: 45, width: 320, height: 180 } } });
  } catch (error) {
    invalidSurfaceBoundsRejected = /falls outside the screenshot/i.test(error.message || String(error));
  }
  const fallbackRegistration = registerRenderedSurface(reference, passingScreenshot);
  const darkMask = new Uint8Array(16 * 16);
  darkMask.fill(1);
  const darkReferenceMaterial = materialMetrics(createImage(16, 16, [6, 4, 3, 255]), darkMask);
  const darkRenderedMaterial = materialMetrics(createImage(16, 16, [5, 5, 4, 255]), darkMask);
  const darkWeightedSaturationDelta = Math.abs(darkReferenceMaterial.meanSaturation - darkRenderedMaterial.meanSaturation);
  const mappedDynamicExclusion = mapReportedDynamicExclusions([
    { id: "moving-outline", category: "unprovided-state", shape: "rectangle", coordinateSpace: "screenshot", x: 180, y: 55, width: 10, height: 2 }
  ], { sourceCrop: { x: 160, y: 45, width: 320, height: 180 } }, 320, 180)[0];
  const assertions = [
    { id: "dynamic-regions-do-not-fail-static-shell", passed: passing.status === "passed", actual: passing.status },
    { id: "static-geometry-regression-fails", passed: failing.status === "failed", actual: failing.status },
    { id: "reported-surface-bounds-are-authoritative", passed: passing.registration.method === "rendered-preview-fixed-surface-bounds-v1" && passing.registration.detection.rendered.elementId === "SelfTestSurface", actual: passing.registration },
    { id: "pixel-frame-fallback-remains-available", passed: fallbackRegistration.method === "outer-antique-gold-frame-projection-v2", actual: fallbackRegistration.method },
    { id: "invalid-reported-surface-bounds-fail-closed", passed: invalidSurfaceBoundsRejected, actual: invalidSurfaceBoundsRejected },
    { id: "metric-grid-records-observed-surface-resolution", passed: passing.metrics.comparison.width === 320 && passing.metrics.comparison.height === 180 && passing.metrics.comparison.referenceFilter === "bilinear-downsample-to-observed-surface", actual: passing.metrics.comparison },
    { id: "near-black-saturation-is-luminance-weighted", passed: darkWeightedSaturationDelta < 0.065, actual: { darkReferenceMaterial, darkRenderedMaterial, delta: round(darkWeightedSaturationDelta) } },
    { id: "reported-dynamic-geometry-maps-into-reference-mask", passed: mappedDynamicExclusion.x === 16 && mappedDynamicExclusion.y === 6 && mappedDynamicExclusion.width === 18 && mappedDynamicExclusion.height === 10, actual: mappedDynamicExclusion },
    { id: "all-exclusion-categories-reported", passed: Object.keys(passing.mask.excludedByCategory).sort().join(",") === ["dynamic-text", "list-or-runtime-content", "portrait-or-scene", "unprovided-state"].sort().join(","), actual: Object.keys(passing.mask.excludedByCategory).sort() },
    { id: "source-hashes-round-trip", passed: passing.reference.actualSha256 === manifestEntry.sha256 && passing.rendered.actualSha256 === sha256File(files.passing), actual: { reference: passing.reference.actualSha256, screenshot: passing.rendered.actualSha256 } },
    { id: "reference-hash-tamper-fails-closed", passed: referenceTamperRejected, actual: referenceTamperRejected },
    { id: "screenshot-hash-tamper-fails-closed", passed: screenshotTamperRejected, actual: screenshotTamperRejected }
  ];
  const ok = assertions.every((assertion) => assertion.passed);
  const report = {
    schema: REPORT_SCHEMA,
    version: TOOL_VERSION,
    mode: "self-test",
    generatedUtc: new Date().toISOString(),
    ok,
    summary: { assertionCount: assertions.length, passed: assertions.filter((entry) => entry.passed).length, failed: assertions.filter((entry) => !entry.passed).length },
    assertions,
    screens: [passing, failing],
    fixtureHashes: Object.fromEntries(Object.entries(files).map(([name, file]) => [name, sha256File(file)]))
  };
  const reportPath = path.join(outputRoot, "approved-reference-fidelity-self-test.json");
  writeJson(reportPath, report);
  return {
    ok,
    consoleSummary: { schema: REPORT_SCHEMA, mode: "self-test", ok, reportPath, summary: report.summary }
  };
}

function syntheticShell(width, height, dynamicVariant, geometryRegression) {
  const image = createImage(width, height, [18, 18, 17, 255]);
  addNoise(image, 4);
  drawRect(image, 5, 5, width - 10, height - 10, [126, 106, 77, 255], 2);
  drawRect(image, 11, 11, width - 22, height - 22, [53, 44, 32, 255], 1);
  drawRect(image, geometryRegression ? 102 : 92, 42, 136, 92, [126, 106, 77, 255], 2);
  drawRect(image, 28, 44, 52, 84, [126, 106, 77, 255], 1);
  drawRect(image, 240, 44, 52, 84, [126, 106, 77, 255], 1);
  drawRect(image, 96, 68, 128, 44, dynamicVariant ? [100, 24, 24, 255] : [36, 36, 34, 255], 0);
  drawRect(image, 35, 60, 40, 50, dynamicVariant ? [30, 80, 120, 255] : [62, 52, 40, 255], 0);
  drawRect(image, 245, 55, 38, 58, dynamicVariant ? [120, 90, 20, 255] : [32, 32, 30, 255], 0);
  drawRect(image, 130, 130, 60, 18, dynamicVariant ? [100, 100, 100, 255] : [26, 26, 25, 255], 0);
  return image;
}

function embedInPreviewChrome(surface, width, height, target) {
  const image = createImage(width, height, [4, 4, 4, 255]);
  drawRect(image, 0, 0, 105, height, [14, 11, 6, 255], 0);
  drawRect(image, 530, 0, 110, height, [14, 11, 6, 255], 0);
  drawRect(image, 110, 25, 415, 260, [7, 7, 7, 255], 1);
  const registered = {
    sourceCrop: { x: 0, y: 0, width: surface.width, height: surface.height }
  };
  const scaled = resampleRegistered(surface, target.width, target.height, registered);
  blit(image, scaled, target.x, target.y);
  // Preview chrome can contain a long gold separator below a scaled target at ultrawide
  // logical viewports. Keep one in the fixture so registration proves it selects the
  // target's own bottom border instead of stretching the crop to the chrome separator.
  drawRect(image, 110, target.y + target.height + 14, 415, 1, [126, 106, 77, 255], 0);
  return image;
}

function selfTestPalette() {
  return {
    schema: "reign-ui-modern-palette-v1",
    tokens: {
      canvasVoid: "#030303FF", canvasBlack: "#070808FF", marbleBase: "#121211FF", marbleShadow: "#0C0C0BFF",
      panelSmoke: "#10100FFF", panelStandard: "#171717FF", panelRaised: "#1A1917FF", panelActive: "#232323FF",
      inputBlack: "#0B0B0BFF", borderBronze: "#352C20FF", goldAntique: "#7E6A4DFF", goldHighlight: "#A88A54FF",
      goldBright: "#C5AC83FF", textPrimary: "#C5BDAFFF", textSecondary: "#8A8883FF", textMuted: "#706F6AFF",
      disabledNeutral: "#4A4945FF", emptySilhouette: "#595956FF"
    }
  };
}

function drawRect(image, x, y, width, height, color, strokeWidth) {
  const x0 = clamp(Math.floor(x), 0, image.width);
  const y0 = clamp(Math.floor(y), 0, image.height);
  const x1 = clamp(Math.ceil(x + width), 0, image.width);
  const y1 = clamp(Math.ceil(y + height), 0, image.height);
  for (let py = y0; py < y1; py += 1) {
    for (let px = x0; px < x1; px += 1) {
      if (strokeWidth > 0 && px >= x0 + strokeWidth && px < x1 - strokeWidth && py >= y0 + strokeWidth && py < y1 - strokeWidth) continue;
      setPixel(image, px, py, color);
    }
  }
}

function addNoise(image, amplitude) {
  for (let index = 0; index < image.width * image.height; index += 1) {
    const offset = index * 4;
    const noise = ((index * 1103515245 + 12345) >>> 16) % (amplitude * 2 + 1) - amplitude;
    image.data[offset] = clamp(image.data[offset] + noise, 0, 255);
    image.data[offset + 1] = clamp(image.data[offset + 1] + noise, 0, 255);
    image.data[offset + 2] = clamp(image.data[offset + 2] + noise, 0, 255);
  }
}

function blit(target, source, x, y) {
  for (let sy = 0; sy < source.height; sy += 1) {
    for (let sx = 0; sx < source.width; sx += 1) {
      const sourceOffset = (sy * source.width + sx) * 4;
      const targetOffset = ((y + sy) * target.width + x + sx) * 4;
      target.data.set(source.data.subarray(sourceOffset, sourceOffset + 4), targetOffset);
    }
  }
}

function createImage(width, height, fill = [0, 0, 0, 0]) {
  const data = new Uint8Array(width * height * 4);
  for (let offset = 0; offset < data.length; offset += 4) data.set(fill, offset);
  return { width, height, data };
}

function setPixel(image, x, y, rgba) {
  const offset = (y * image.width + x) * 4;
  image.data.set(rgba, offset);
}

function decodePng(buffer) {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  if (buffer.length < 33 || !buffer.subarray(0, 8).equals(signature)) throw new Error("Input is not a PNG file.");
  let offset = 8;
  let width = 0;
  let height = 0;
  let bitDepth = 0;
  let colorType = 0;
  let interlace = 0;
  const idat = [];
  while (offset + 12 <= buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("ascii", offset + 4, offset + 8);
    const data = buffer.subarray(offset + 8, offset + 8 + length);
    if (type === "IHDR") {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      bitDepth = data[8];
      colorType = data[9];
      interlace = data[12];
    } else if (type === "IDAT") idat.push(data);
    else if (type === "IEND") break;
    offset += length + 12;
  }
  if (!width || !height) throw new Error("PNG has no valid IHDR dimensions.");
  if (bitDepth !== 8 || interlace !== 0 || ![0, 2, 4, 6].includes(colorType)) {
    throw new Error(`Unsupported PNG format: bit depth ${bitDepth}, color type ${colorType}, interlace ${interlace}.`);
  }
  const channels = { 0: 1, 2: 3, 4: 2, 6: 4 }[colorType];
  const stride = width * channels;
  const raw = zlib.inflateSync(Buffer.concat(idat));
  if (raw.length !== (stride + 1) * height) throw new Error("PNG scanline length does not match IHDR dimensions.");
  const unfiltered = Buffer.alloc(stride * height);
  let inputOffset = 0;
  for (let y = 0; y < height; y += 1) {
    const filter = raw[inputOffset++];
    const rowOffset = y * stride;
    for (let x = 0; x < stride; x += 1) {
      const value = raw[inputOffset++];
      const left = x >= channels ? unfiltered[rowOffset + x - channels] : 0;
      const up = y > 0 ? unfiltered[rowOffset + x - stride] : 0;
      const upLeft = y > 0 && x >= channels ? unfiltered[rowOffset + x - stride - channels] : 0;
      if (filter === 0) unfiltered[rowOffset + x] = value;
      else if (filter === 1) unfiltered[rowOffset + x] = (value + left) & 255;
      else if (filter === 2) unfiltered[rowOffset + x] = (value + up) & 255;
      else if (filter === 3) unfiltered[rowOffset + x] = (value + Math.floor((left + up) / 2)) & 255;
      else if (filter === 4) unfiltered[rowOffset + x] = (value + paeth(left, up, upLeft)) & 255;
      else throw new Error(`Unsupported PNG filter ${filter}.`);
    }
  }
  const image = createImage(width, height);
  for (let pixel = 0; pixel < width * height; pixel += 1) {
    const sourceOffset = pixel * channels;
    const targetOffset = pixel * 4;
    if (colorType === 0) {
      image.data[targetOffset] = unfiltered[sourceOffset];
      image.data[targetOffset + 1] = unfiltered[sourceOffset];
      image.data[targetOffset + 2] = unfiltered[sourceOffset];
      image.data[targetOffset + 3] = 255;
    } else if (colorType === 2) {
      image.data[targetOffset] = unfiltered[sourceOffset];
      image.data[targetOffset + 1] = unfiltered[sourceOffset + 1];
      image.data[targetOffset + 2] = unfiltered[sourceOffset + 2];
      image.data[targetOffset + 3] = 255;
    } else if (colorType === 4) {
      image.data[targetOffset] = unfiltered[sourceOffset];
      image.data[targetOffset + 1] = unfiltered[sourceOffset];
      image.data[targetOffset + 2] = unfiltered[sourceOffset];
      image.data[targetOffset + 3] = unfiltered[sourceOffset + 1];
    } else {
      image.data.set(unfiltered.subarray(sourceOffset, sourceOffset + 4), targetOffset);
    }
  }
  return image;
}

function encodePng(image) {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(image.width, 0);
  ihdr.writeUInt32BE(image.height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  const raw = Buffer.alloc((image.width * 4 + 1) * image.height);
  for (let y = 0; y < image.height; y += 1) {
    const rowOffset = y * (image.width * 4 + 1);
    raw[rowOffset] = 0;
    raw.set(image.data.subarray(y * image.width * 4, (y + 1) * image.width * 4), rowOffset + 1);
  }
  return Buffer.concat([signature, pngChunk("IHDR", ihdr), pngChunk("IDAT", zlib.deflateSync(raw, { level: 9 })), pngChunk("IEND", Buffer.alloc(0))]);
}

function pngChunk(type, data) {
  const typeBuffer = Buffer.from(type, "ascii");
  const chunk = Buffer.alloc(data.length + 12);
  chunk.writeUInt32BE(data.length, 0);
  typeBuffer.copy(chunk, 4);
  data.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])), data.length + 8);
  return chunk;
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) crc = CRC_TABLE[(crc ^ byte) & 255] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

function parseArguments(argv) {
  const output = { targets: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") output.help = true;
    else if (argument === "--self-test") output.selfTest = true;
    else if (argument === "--report-only") output.reportOnly = true;
    else if (["--manifest", "--render-report", "--contract", "--viewport", "--output", "--targets"].includes(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) throw new Error(`${argument} requires a value.`);
      if (argument === "--targets") output.targets = value.split(",").map((entry) => entry.trim()).filter(Boolean);
      else output[toCamel(argument.slice(2))] = value;
    } else throw new Error(`Unknown argument ${argument}. Use --help for usage.`);
  }
  return output;
}

function helpText() {
  return `Approved-reference fidelity audit for rendered Reign Gauntlet interfaces.

Usage:
  node audit-approved-reference-fidelity.mjs --render-report <rendered-preview-audit.json> [options]
  node audit-approved-reference-fidelity.mjs --self-test [--output <directory>]

Options:
  --manifest <file>       Approved modern-style asset manifest. Defaults to the workspace style kit.
  --render-report <file>  Provider-free audit-rendered-preview.mjs JSON evidence (required for an audit).
  --contract <file>       Optional ${CONTRACT_SCHEMA} JSON with per-screen fixed-region specs, exclusions, and threshold overrides.
  --targets <ids>         Comma-separated ids. Default: ${DEFAULT_TARGETS.join(",")}.
  --viewport <WxH>        Render-report case viewport. Default: 1920x1080.
  --output <directory>    Evidence root. Defaults beneath .codex-build/ui-preview.
  --report-only           Emit failed evidence but return exit code 0.
  --self-test             Generate deterministic fixtures proving masked dynamics pass and static drift fails.
  --help                  Show this help.

Contract shape:
  { "schema": "${CONTRACT_SCHEMA}", "screens": [
      { "id": "government", "fixedRegionSpec": "relative/path/to/reign-fixed-region-composite-v1.json",
        "exclusions": [{ "id": "additional-mask", "category": "dynamic-text",
        "shape": "rectangle|ellipse", "x": 0, "y": 0, "width": 10, "height": 10 }],
        "thresholds": { "geometry": { "maximumEdgeMismatch": 0.42 } } }
    ] }

fixedRegionSpec imports sceneDependentMasks from the exact source-composition contract. Polygon masks are
conservatively converted to their bounding rectangles; categoryOverrides may reclassify inferred categories.

The tool registers the actual Gauntlet surface inside previewer chrome using its outer antique-gold frame,
normalizes it to the approved canvas without freeform warping, excludes only declared runtime regions, and emits
per-screen masks, normalized renders, 50/50 overlays, heatmaps, exact hashes, and geometry/material/palette metrics.
`;
}

function readJson(filePath, label) {
  requireFile(filePath, label);
  try {
    const parsed = JSON.parse(fs.readFileSync(filePath, "utf8"));
    Object.defineProperty(parsed, "__path", { value: filePath, enumerable: false });
    return parsed;
  } catch (error) {
    throw new Error(`Could not parse ${label} ${filePath}: ${error.message}`);
  }
}

function writeJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function requireFile(filePath, label) {
  if (!fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) throw new Error(`${label} was not found: ${filePath}`);
}

function resolveInside(root, relativePath, label) {
  const resolved = path.resolve(root, relativePath);
  const normalizedRoot = `${path.resolve(root)}${path.sep}`.toLowerCase();
  if (!`${resolved}${path.sep}`.toLowerCase().startsWith(normalizedRoot)) throw new Error(`${label} escapes its declared root: ${relativePath}`);
  return resolved;
}

function sha256File(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function sha256Text(value) {
  return crypto.createHash("sha256").update(value, "utf8").digest("hex");
}

function stableStringify(value) {
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(",")}]`;
  if (value && typeof value === "object") return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableStringify(value[key])}`).join(",")}}`;
  return JSON.stringify(value);
}

function parseHexColor(value) {
  return [1, 3, 5, 7].map((offset) => Number.parseInt(value.slice(offset, offset + 2), 16));
}

function rgbDistance(r1, g1, b1, r2, g2, b2) {
  return Math.sqrt((r1 - r2) ** 2 + (g1 - g2) ** 2 + (b1 - b2) ** 2) / Math.sqrt(3 * 255 * 255);
}

function luminance(r, g, b) {
  return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
}

function pixelLuminance(image, index) {
  const offset = index * 4;
  return luminance(image.data[offset], image.data[offset + 1], image.data[offset + 2]);
}

function saturation(r, g, b) {
  const maximum = Math.max(r, g, b);
  const minimum = Math.min(r, g, b);
  return maximum === 0 ? 0 : (maximum - minimum) / maximum;
}

function distributionDistance(left, right) {
  const names = new Set([...Object.keys(left), ...Object.keys(right)]);
  let total = 0;
  for (const name of names) total += Math.abs((left[name] || 0) - (right[name] || 0));
  return total / 2;
}

function sumNamedOccupancy(values, names) {
  let total = 0;
  for (const name of names) total += values[name] || 0;
  return total;
}

function countMask(mask) {
  let count = 0;
  for (const value of mask) count += value;
  return count;
}

function indexesAtLeast(values, threshold) {
  const indexes = [];
  for (let index = 0; index < values.length; index += 1) if (values[index] >= threshold) indexes.push(index);
  return indexes;
}

function intersectionArea(left, right) {
  const width = Math.max(0, Math.min(left.x + left.width, right.x + right.width) - Math.max(left.x, right.x));
  const height = Math.max(0, Math.min(left.y + left.height, right.y + right.height) - Math.max(left.y, right.y));
  return width * height;
}

function round(value, digits = 6) {
  return Number(Number(value).toFixed(digits));
}

function roundRect(value) {
  return { x: round(value.x, 3), y: round(value.y, 3), width: round(value.width, 3), height: round(value.height, 3) };
}

function clamp(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, value));
}

function isPlainObject(value) {
  return value && typeof value === "object" && !Array.isArray(value);
}

function toCamel(value) {
  return value.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

function kebabCase(value) {
  return value.replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase();
}

main().catch((error) => {
  process.stderr.write(`${error.stack || error.message || String(error)}\n`);
  process.exitCode = 2;
});
