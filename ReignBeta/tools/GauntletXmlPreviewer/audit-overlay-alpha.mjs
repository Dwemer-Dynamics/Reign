#!/usr/bin/env node

import { createHash } from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { deflateSync, inflateSync } from "node:zlib";

const REPORT_SCHEMA = "reign-ui-overlay-alpha-audit-v1";
const CONTRACT_SCHEMA = "reign-ui-modern-style-contract-v1";
const PORTRAIT_FRAME_SPEC_SCHEMA = "reign-ui-portrait-aperture-plate-spec-v2";
const SUPPORTED_SHAPES = new Set(["circle", "oval", "rectangle", "memory", "polygon", "compound"]);
const DEFAULT_OUTPUT = path.resolve(".codex-build", "ui-preview", "overlay-alpha-audit.json");

const DEFAULT_THRESHOLDS = Object.freeze({
  transparentAlphaMax: 16,
  opaqueAlphaMin: 240,
  apertureInsetPixels: 1.5,
  edgeBandPixels: 2,
  surroundBandPixels: 8,
  boundsTolerancePixels: 3,
  minimumApertureTransparentRatio: 0.97,
  minimumApertureCenterTransparentRatio: 0.995,
  minimumApertureBoundsCoverage: 0.94,
  minimumSurroundOpaqueRatio: 0.25,
  minimumExteriorOpaqueRatio: 0,
  maximumHaloPixelRatio: 0.02,
  maximumCheckerboardContaminationRatio: 0.002,
  maximumMatteContaminationRatio: 0.002,
  minimumCanvasTransparentRatio: 0,
  minimumCanvasOpaqueRatio: 0,
  maximumCanvasOpaqueRatio: 1,
  maximumLightNeutralPixelRatio: 1,
  maximumTransparentRgbLeakRatio: 1,
  lightNeutralLuminanceFloor: 170,
  lightNeutralChromaCeiling: 28,
  maximumOpaqueLeakRatio: null,
  maximumTransparentFringeRatio: null,
  circleAspectTolerance: 0.04,
  memoryAspectTolerance: 0.08,
  allowedTransparentFringePixels: null,
  allowedOpaqueLeakPixels: null
});

const APERTURE_PLATE_THRESHOLDS = Object.freeze({
  minimumSurroundOpaqueRatio: 0.95,
  minimumExteriorOpaqueRatio: 0.985,
  minimumCanvasOpaqueRatio: 0.1,
  maximumLightNeutralPixelRatio: 0,
  maximumTransparentRgbLeakRatio: 0
});

let crcTable = null;

const argv = process.argv.slice(2);

if (argv.includes("--help") || argv.includes("-h")) {
  printHelp();
  process.exit(0);
}

const outputPath = path.resolve(argumentValue("--output") || DEFAULT_OUTPUT);

try {
  const report = argv.includes("--self-test")
    ? runSelfTest()
    : auditContractFile(requiredArgument("--contract"));
  writeReport(outputPath, report);
  printSummary(report, outputPath);
  if (!report.passed) process.exitCode = 1;
} catch (error) {
  const report = {
    schema: REPORT_SCHEMA,
    generatedUtc: new Date().toISOString(),
    mode: argv.includes("--self-test") ? "self-test" : "contract",
    passed: false,
    errors: [error instanceof Error ? error.message : String(error)],
    assetCount: 0,
    passedCount: 0,
    failedCount: 0,
    assets: []
  };
  writeReport(outputPath, report);
  printSummary(report, outputPath);
  process.exitCode = 1;
}

function printHelp() {
  process.stdout.write(`Reign overlay alpha audit

Usage:
  node audit-overlay-alpha.mjs --contract <modern-style-contract.json> [--output <report.json>]
  node audit-overlay-alpha.mjs --self-test [--output <report.json>]
  node audit-overlay-alpha.mjs --help

The contract must declare portable paths relative to the contract file:

{
  "schema": "${CONTRACT_SCHEMA}",
  "defaults": {
    "surroundBandPixels": 8,
    "thresholds": {
      "minimumTransparentApertureRatio": 0.97,
      "minimumOpaqueSurroundRatio": 0.35,
      "allowedOpaqueLeakPixels": 20,
      "allowedTransparentFringePixels": 50
    }
  },
  "overlayAssets": [
    {
      "id": "compact-portrait-circle",
      "path": "assets/compact-portrait-circle.png",
      "shape": "circle",
      "presentation": "opaque-aperture-plate",
      "expectedSha256": "optional lowercase SHA-256",
      "aperture": {
        "units": "pixels",
        "x": 24,
        "y": 24,
        "width": 80,
        "height": 80
      },
      "thresholds": {
        "minimumSurroundOpaqueRatio": 0.35,
        "surroundBandPixels": 10
      }
    }
  ]
}

Supported shapes are circle, oval, rectangle, memory, polygon, and compound. A memory aperture is
rectangular and is required to remain approximately square. Aperture units may
be pixels or normalized. Each PNG must be non-interlaced 8-bit true RGBA (PNG
color type 6). The audit records exact contract and source hashes, aperture
center and edge-band alpha evidence, transparent-component bounds, surround
opacity, leak/fringe counts, and suspicious halo/checkerboard/matte colors.
Entries with presentation "opaque-aperture-plate" additionally require opaque
black-marble exterior corners, a transparent declared aperture, enough opaque
canvas to conceal an untouched rectangular portrait, zero pale-neutral halo pixels,
and zero RGB data in fully transparent pixels. A portraitFrameOverlaySpec declaration
can register the complete contextual circle/oval/rectangle plate set without duplication.

A compound owner declares "shape":"compound" and an "apertures" array of 1..16
children: {"shape":"circle","aperture":{"units":"pixels","x":10,"y":10,"width":40,"height":40}}.
Each child uses the same owner thresholds; nested compounds and child threshold
overrides are rejected. Polygon points belong inside the child's aperture object.
Optional outer aperture bounds are descriptive only, never an exterior exclusion.
Every child must pass independently; only the UNION of declared child shapes and
their existing edge bands is excluded from exterior/surround opacity checks.
All opaque corner, contamination and zero-alpha RGB gates remain in force.
An optional rectangle-child "alphaFade":{"fromX":10,"toX":40,"opaqueToTransparent":true}
declares a horizontal linear alpha ramp in owner-image pixels (false reverses it).
Every interior ramp pixel must match within one alpha unit; the remaining clear
region still passes normal transparency/component/bounds checks. A fade must lie
inside its child and leave a clear region wider than two pixels.
`);
}

function requiredArgument(name) {
  const value = argumentValue(name);
  if (!value) throw new Error(`Missing required ${name}. Use --help for the contract format.`);
  return value;
}

function argumentValue(name) {
  const index = argv.indexOf(name);
  return index >= 0 ? argv[index + 1] || "" : "";
}

function auditContractFile(contractValue) {
  const contractPath = path.resolve(contractValue);
  if (!fs.existsSync(contractPath)) throw new Error(`Modern-style contract does not exist: ${contractPath}`);
  const contractBytes = fs.readFileSync(contractPath);
  const contract = parseJson(contractBytes, contractPath);
  const contractRoot = path.dirname(contractPath);
  const entries = contract?.overlayAssets;
  const supportEntries = Array.isArray(contract?.supportAssets) ? contract.supportAssets : [];
  if (!Array.isArray(entries) || entries.length === 0) {
    throw new Error("Modern-style contract must contain a non-empty overlayAssets array.");
  }

  const errors = [];
  if (contract.schema !== CONTRACT_SCHEMA) {
    errors.push(`Contract schema must be '${CONTRACT_SCHEMA}', found '${contract.schema || "missing"}'.`);
  }

  const portraitFrameSpec = loadPortraitFrameOverlaySpec(contract?.portraitFrameOverlaySpec, contractRoot, errors);

  const ids = new Set();
  const overlayAssets = entries.map((entry, index) => {
    const id = String(entry?.id || "").trim();
    if (!id) return failedAsset(`asset-${index + 1}`, "asset_id_missing", "Overlay entry has no id.");
    if (ids.has(id)) return failedAsset(id, "asset_id_duplicate", `Duplicate overlay id '${id}'.`);
    ids.add(id);
    return { kind: "overlay", ...auditAsset(entry, contractRoot, contract.defaults || {}) };
  });
  for (const entry of portraitFrameSpec.entries) {
    if (ids.has(entry.id)) continue;
    ids.add(entry.id);
    overlayAssets.push({ kind: "portrait-frame-overlay", ...auditAsset(entry, contractRoot, contract.defaults || {}) });
  }
  const supportAssets = supportEntries.map((entry, index) => {
    const id = String(entry?.id || "").trim();
    if (!id) return { kind: "support", ...failedAsset(`support-asset-${index + 1}`, "asset_id_missing", "Support asset entry has no id.") };
    if (ids.has(id)) return { kind: "support", ...failedAsset(id, "asset_id_duplicate", `Duplicate asset id '${id}'.`) };
    ids.add(id);
    return { kind: "support", ...auditSupportAsset(entry, contractRoot) };
  });
  const assets = [...overlayAssets, ...supportAssets];
  const runtimeAtlas = auditRuntimeAtlas(contract?.runtimeAtlas, contractRoot, assets);
  const provenanceCorrection = auditProvenanceCorrection(contract?.provenanceCorrection, contractRoot);

  const passedCount = assets.filter((asset) => asset.passed).length;
  const report = {
    schema: REPORT_SCHEMA,
    generatedUtc: new Date().toISOString(),
    mode: "contract",
    passed: errors.length === 0
      && passedCount === assets.length
      && (!runtimeAtlas || runtimeAtlas.passed)
      && (!provenanceCorrection || provenanceCorrection.passed),
    contract: {
      schema: contract.schema || "",
      path: contractPath,
      sha256: sha256(contractBytes),
      overlayAssetCount: entries.length,
      effectiveOverlayAssetCount: overlayAssets.length,
      supportAssetCount: supportEntries.length
    },
    portraitFrameOverlaySpec: portraitFrameSpec.report,
    runtimeAtlas,
    provenanceCorrection,
    thresholds: normalizeThresholds(mergeThresholdInputs(contract.defaults || {})),
    errors,
    assetCount: assets.length,
    passedCount,
    failedCount: assets.length - passedCount,
    assets
  };
  return report;
}

function auditProvenanceCorrection(declaration, contractRoot) {
  if (!declaration) return null;
  const failures = [];
  const relativePath = String(declaration.path || "").trim();
  const workspaceRootValue = String(declaration.workspaceRoot || "").trim();
  const recordId = String(declaration.recordId || "").trim();
  if (!relativePath || path.isAbsolute(relativePath)) addFailure(failures, "provenance_path_not_portable", "provenanceCorrection.path must be relative to the contract file.");
  if (!workspaceRootValue || path.isAbsolute(workspaceRootValue)) addFailure(failures, "provenance_workspace_root_not_portable", "provenanceCorrection.workspaceRoot must be relative to the contract file.");
  if (!recordId) addFailure(failures, "provenance_record_id_missing", "provenanceCorrection.recordId is required.");
  if (failures.length > 0) return { passed: false, recordId, failures, hashChecks: [] };

  const provenancePath = path.resolve(contractRoot, relativePath);
  const workspaceRoot = path.resolve(contractRoot, workspaceRootValue);
  if (!fs.existsSync(provenancePath)) {
    addFailure(failures, "provenance_missing", `Generation provenance does not exist: ${provenancePath}`);
    return { passed: false, provenancePath, workspaceRoot, recordId, failures, hashChecks: [] };
  }
  const provenanceBytes = fs.readFileSync(provenancePath);
  let provenance;
  try {
    provenance = parseJson(provenanceBytes, provenancePath);
  } catch (error) {
    addFailure(failures, "provenance_invalid", error instanceof Error ? error.message : String(error));
    return { passed: false, provenancePath, provenanceSha256: sha256(provenanceBytes), workspaceRoot, recordId, failures, hashChecks: [] };
  }
  const records = provenance?.deterministicProductionLedger?.focusedCorrections;
  const record = Array.isArray(records) ? records.find((entry) => entry?.id === recordId) : null;
  if (!record) {
    addFailure(failures, "provenance_record_missing", `focusedCorrections does not contain '${recordId}'.`);
    return { passed: false, provenancePath, provenanceSha256: sha256(provenanceBytes), workspaceRoot, recordId, failures, hashChecks: [] };
  }

  const hashChecks = [];
  const checkHash = (label, declaredPath, expectedSha256) => {
    const relative = String(declaredPath || "").trim();
    const expected = String(expectedSha256 || "").trim().toLowerCase();
    const checkFailures = [];
    if (!relative || path.isAbsolute(relative)) {
      addFailure(checkFailures, "provenance_reference_path_invalid", `${label} path must be workspace-relative.`);
    } else if (!/^[a-f0-9]{64}$/.test(expected)) {
      addFailure(checkFailures, "provenance_reference_hash_invalid", `${label} SHA-256 must be 64 lowercase hexadecimal characters.`);
    } else {
      const sourcePath = path.resolve(workspaceRoot, relative);
      if (!fs.existsSync(sourcePath)) {
        addFailure(checkFailures, "provenance_reference_missing", `${label} does not exist: ${sourcePath}`);
      } else {
        const actualSha256 = sha256(fs.readFileSync(sourcePath));
        if (actualSha256 !== expected) addFailure(checkFailures, "provenance_reference_hash_mismatch", `${label} expected ${expected}, found ${actualSha256}.`);
        hashChecks.push({ label, path: relative, sourcePath, expectedSha256: expected, actualSha256, passed: checkFailures.length === 0, failures: checkFailures });
        failures.push(...checkFailures.map((failure) => ({ ...failure, label })));
        return;
      }
    }
    hashChecks.push({ label, path: relative, expectedSha256: expected, passed: false, failures: checkFailures });
    failures.push(...checkFailures.map((failure) => ({ ...failure, label })));
  };

  checkHash("implementation.spec", record.implementation?.specPath, record.implementation?.specSha256);
  checkHash("implementation.tool", record.implementation?.toolPath, record.implementation?.toolSha256);
  checkHash("implementation.modernStyleContract", record.implementation?.modernStyleContractPath, record.implementation?.modernStyleContractSha256);
  checkHash("implementation.overlayAuditTool", record.implementation?.overlayAuditToolPath, record.implementation?.overlayAuditToolSha256);
  checkHash("evidence.extraction", record.evidence?.extractionPath, record.evidence?.extractionSha256);
  checkHash("evidence.auditSelfTest", record.evidence?.auditSelfTestPath, record.evidence?.auditSelfTestSha256);
  checkHash("evidence.contractAndAtlasAudit", record.evidence?.contractAndAtlasAuditPath, record.evidence?.contractAndAtlasAuditSha256);
  checkHash("runtimeAtlas.manifest", record.runtimeAtlas?.manifestPath, record.runtimeAtlas?.manifestSha256);
  checkHash("runtimeAtlas.spriteData", record.runtimeAtlas?.spriteDataPath, record.runtimeAtlas?.spriteDataSha256);
  for (const [index, sheet] of (record.runtimeAtlas?.affectedSheets || []).entries()) {
    checkHash(`runtimeAtlas.affectedSheets[${index}]`, sheet.path, sheet.sha256);
  }

  return {
    passed: failures.length === 0,
    provenancePath,
    provenanceSha256: sha256(provenanceBytes),
    workspaceRoot,
    recordId,
    recordStatus: String(record.status || ""),
    hashCheckCount: hashChecks.length,
    passedHashCheckCount: hashChecks.filter((entry) => entry.passed).length,
    failures,
    hashChecks
  };
}

function auditRuntimeAtlas(declaration, contractRoot, assets) {
  if (!declaration) return null;
  const failures = [];
  const manifestRelativePath = String(declaration.manifestPath || "").trim();
  const moduleRootRelativePath = String(declaration.moduleRoot || "").trim();
  if (!manifestRelativePath || path.isAbsolute(manifestRelativePath)) {
    addFailure(failures, "atlas_manifest_path_not_portable", "runtimeAtlas.manifestPath must be relative to the contract file.");
  }
  if (!moduleRootRelativePath || path.isAbsolute(moduleRootRelativePath)) {
    addFailure(failures, "atlas_module_root_not_portable", "runtimeAtlas.moduleRoot must be relative to the contract file.");
  }
  if (failures.length > 0) return { passed: false, failures, registrations: [] };

  const manifestPath = path.resolve(contractRoot, manifestRelativePath);
  const moduleRoot = path.resolve(contractRoot, moduleRootRelativePath);
  if (!fs.existsSync(manifestPath)) {
    addFailure(failures, "atlas_manifest_missing", `Runtime atlas manifest does not exist: ${manifestPath}`);
    return { passed: false, manifestPath, moduleRoot, failures, registrations: [] };
  }
  const manifestBytes = fs.readFileSync(manifestPath);
  let manifest;
  try {
    manifest = parseJson(manifestBytes, manifestPath);
  } catch (error) {
    addFailure(failures, "atlas_manifest_invalid", error instanceof Error ? error.message : String(error));
    return { passed: false, manifestPath, moduleRoot, manifestSha256: sha256(manifestBytes), failures, registrations: [] };
  }

  const spriteDataRelativePath = String(manifest.spriteDataPath || "").trim();
  const spriteDataPath = spriteDataRelativePath ? path.resolve(moduleRoot, spriteDataRelativePath) : "";
  const spriteDataSha256 = spriteDataPath && fs.existsSync(spriteDataPath) ? sha256(fs.readFileSync(spriteDataPath)) : "";
  if (!spriteDataPath || !fs.existsSync(spriteDataPath)) {
    addFailure(failures, "atlas_sprite_data_missing", `Runtime atlas manifest SpriteData does not exist: ${spriteDataPath || "missing path"}`);
  } else if (String(manifest.spriteDataSha256 || "").toLowerCase() !== spriteDataSha256) {
    addFailure(failures, "atlas_sprite_data_hash_mismatch", `Runtime atlas manifest records ${manifest.spriteDataSha256 || "no hash"}; current SpriteData is ${spriteDataSha256}.`);
  }

  const registrationsBySource = new Map();
  for (const [categoryName, category] of Object.entries(manifest.categories || {})) {
    const sheetsById = new Map((category.sheets || []).map((sheet) => [Number(sheet.id), sheet]));
    for (const [partName, part] of Object.entries(category.parts || {})) {
      const sourcePath = path.resolve(moduleRoot, String(part.sourcePath || ""));
      registrationsBySource.set(pathKey(sourcePath), { categoryName, partName, part, sheet: sheetsById.get(Number(part.sheetId)) || null });
    }
  }

  const atlasCache = new Map();
  const registrations = [];
  for (const asset of assets) {
    if (!asset.sourcePath || !asset.sourceSha256) continue;
    const registration = registrationsBySource.get(pathKey(asset.sourcePath));
    const itemFailures = [];
    if (!registration) {
      addFailure(itemFailures, "atlas_registration_missing", `No runtime atlas registration references ${asset.sourcePath}.`);
    } else {
      const { categoryName, partName, part, sheet } = registration;
      if (String(part.sourceSha256 || "").toLowerCase() !== asset.sourceSha256) {
        addFailure(itemFailures, "atlas_source_hash_mismatch", `Manifest source hash ${part.sourceSha256 || "missing"} does not match ${asset.sourceSha256}.`);
      }
      if (!sheet) {
        addFailure(itemFailures, "atlas_sheet_registration_missing", `Sprite ${partName} references missing sheet ${part.sheetId}.`);
      } else {
        const atlasPath = path.resolve(moduleRoot, String(sheet.path || ""));
        if (!fs.existsSync(atlasPath)) {
          addFailure(itemFailures, "atlas_sheet_missing", `Runtime atlas sheet does not exist: ${atlasPath}`);
        } else {
          const atlasBytes = fs.readFileSync(atlasPath);
          const atlasSha256 = sha256(atlasBytes);
          if (String(sheet.sha256 || "").toLowerCase() !== atlasSha256) {
            addFailure(itemFailures, "atlas_sheet_hash_mismatch", `Manifest sheet hash ${sheet.sha256 || "missing"} does not match ${atlasSha256}.`);
          }
          let atlas = atlasCache.get(atlasPath);
          if (!atlas) {
            atlas = decodePng(atlasBytes);
            atlasCache.set(atlasPath, atlas);
          }
          const source = decodePng(fs.readFileSync(asset.sourcePath));
          if (!source.pixels || !atlas.pixels) {
            addFailure(itemFailures, "atlas_region_decode_failed", `Sprite ${partName} or its atlas sheet is not decodable RGBA.`);
          } else if (source.width !== Number(part.width) || source.height !== Number(part.height)) {
            addFailure(itemFailures, "atlas_region_dimensions_mismatch", `Source is ${source.width}x${source.height}; manifest region is ${part.width}x${part.height}.`);
          } else if (!rgbaRegionEquals(source, atlas, Number(part.x), Number(part.y))) {
            addFailure(itemFailures, "atlas_region_pixel_mismatch", `Packed atlas region for ${partName} is not pixel-identical to its source PNG.`);
          }
        }
      }
      registrations.push({
        id: asset.id,
        categoryName,
        partName,
        sheetId: Number(part.sheetId),
        x: Number(part.x),
        y: Number(part.y),
        width: Number(part.width),
        height: Number(part.height),
        sourceSha256: asset.sourceSha256,
        passed: itemFailures.length === 0,
        failures: itemFailures
      });
      failures.push(...itemFailures.map((failure) => ({ ...failure, assetId: asset.id, partName })));
      continue;
    }
    registrations.push({ id: asset.id, passed: false, failures: itemFailures });
    failures.push(...itemFailures.map((failure) => ({ ...failure, assetId: asset.id })));
  }

  return {
    passed: failures.length === 0,
    manifestPath,
    manifestSha256: sha256(manifestBytes),
    moduleRoot,
    spriteDataPath,
    spriteDataSha256,
    categoryCount: Object.keys(manifest.categories || {}).length,
    registrationCount: registrations.length,
    passedRegistrationCount: registrations.filter((entry) => entry.passed).length,
    failures,
    registrations
  };
}

function pathKey(value) {
  return path.resolve(value).toLowerCase();
}

function rgbaRegionEquals(source, atlas, atlasX, atlasY) {
  if (atlasX < 0 || atlasY < 0 || atlasX + source.width > atlas.width || atlasY + source.height > atlas.height) return false;
  for (let y = 0; y < source.height; y += 1) {
    for (let x = 0; x < source.width; x += 1) {
      const sourceOffset = (y * source.width + x) * 4;
      const atlasOffset = ((atlasY + y) * atlas.width + atlasX + x) * 4;
      for (let channel = 0; channel < 4; channel += 1) {
        if (source.pixels[sourceOffset + channel] !== atlas.pixels[atlasOffset + channel]) return false;
      }
    }
  }
  return true;
}

function loadPortraitFrameOverlaySpec(declaration, contractRoot, errors) {
  if (!declaration) return { entries: [], report: null };
  const relativePath = String(declaration.path || "").trim();
  if (!relativePath || path.isAbsolute(relativePath)) {
    errors.push("portraitFrameOverlaySpec.path must be a non-empty path relative to the contract file.");
    return { entries: [], report: null };
  }
  const specPath = path.resolve(contractRoot, relativePath);
  if (!fs.existsSync(specPath)) {
    errors.push(`Portrait aperture-plate spec does not exist: ${specPath}`);
    return { entries: [], report: { path: specPath, relativePath, assetCount: 0 } };
  }
  const specBytes = fs.readFileSync(specPath);
  const specSha256 = sha256(specBytes);
  const expectedSha256 = String(declaration.expectedSha256 || "").trim().toLowerCase();
  if (expectedSha256 && !/^[a-f0-9]{64}$/.test(expectedSha256)) {
    errors.push("portraitFrameOverlaySpec.expectedSha256 must contain exactly 64 lowercase hexadecimal characters.");
  } else if (expectedSha256 && expectedSha256 !== specSha256) {
    errors.push(`Portrait frame overlay spec hash mismatch: expected ${expectedSha256}, found ${specSha256}.`);
  }

  let spec;
  try {
    spec = parseJson(specBytes, specPath);
  } catch (error) {
    errors.push(error instanceof Error ? error.message : String(error));
    return { entries: [], report: { path: specPath, relativePath, sha256: specSha256, expectedSha256, assetCount: 0 } };
  }
  if (spec.schema !== PORTRAIT_FRAME_SPEC_SCHEMA) {
    errors.push(`Portrait aperture-plate spec schema must be '${PORTRAIT_FRAME_SPEC_SCHEMA}', found '${spec.schema || "missing"}'.`);
  }
  const workspaceRootValue = String(spec.workspaceRoot || "").trim();
  if (!workspaceRootValue || path.isAbsolute(workspaceRootValue)) {
    errors.push("Portrait frame overlay spec workspaceRoot must be a portable path relative to the spec file.");
    return { entries: [], report: { schema: spec.schema || "", path: specPath, relativePath, sha256: specSha256, expectedSha256, assetCount: 0 } };
  }
  const workspaceRoot = path.resolve(path.dirname(specPath), workspaceRootValue);
  const assets = Array.isArray(spec.assets) ? spec.assets : [];
  if (assets.length === 0) errors.push("Portrait frame overlay spec must contain a non-empty assets array.");
  const entries = [];
  const specIds = new Set();
  for (const [index, asset] of assets.entries()) {
    const id = String(asset?.id || "").trim();
    const assetPath = String(asset?.path || "").trim();
    if (!id) {
      errors.push(`Portrait frame overlay spec asset ${index + 1} has no id.`);
      continue;
    }
    if (specIds.has(id)) {
      errors.push(`Portrait frame overlay spec contains duplicate id '${id}'.`);
      continue;
    }
    specIds.add(id);
    if (!assetPath || path.isAbsolute(assetPath)) {
      errors.push(`Portrait frame overlay spec asset '${id}' must use a workspace-relative path.`);
      continue;
    }
    const width = finite(asset.width, `${id}.width`);
    const height = finite(asset.height, `${id}.height`);
    const aperture = asset.aperture || {};
    const apertureWidth = finite(aperture.width, `${id}.aperture.width`);
    const apertureHeight = finite(aperture.height, `${id}.aperture.height`);
    const declaredShape = String(asset.shape || "").trim().toLowerCase();
    const shape = declaredShape || (Math.abs(apertureWidth / apertureHeight - 1) <= DEFAULT_THRESHOLDS.circleAspectTolerance ? "circle" : "oval");
    entries.push({
      id,
      path: portableRelativePath(contractRoot, path.resolve(workspaceRoot, assetPath)),
      shape,
      presentation: "opaque-aperture-plate",
      expectedSha256: String(asset.expectedOutputSha256 || "").trim().toLowerCase(),
      aperture: { units: "pixels", ...aperture },
      thresholds: { ...APERTURE_PLATE_THRESHOLDS, ...(asset.thresholds || {}) },
      declaredDimensions: { width, height }
    });
  }
  return {
    entries,
    report: {
      schema: spec.schema || "",
      path: specPath,
      relativePath,
      sha256: specSha256,
      expectedSha256,
      workspaceRoot,
      assetCount: assets.length,
      loadedAssetCount: entries.length
    }
  };
}

function portableRelativePath(from, to) {
  return path.relative(from, to).split(path.sep).join("/");
}

function auditSupportAsset(entry, contractRoot) {
  const id = String(entry.id || "").trim();
  const failures = [];
  const relativePath = String(entry.path || "").trim();
  if (!relativePath) return failedAsset(id, "asset_path_missing", `Support asset '${id}' has no path.`);
  if (path.isAbsolute(relativePath)) {
    return failedAsset(id, "asset_path_not_portable", `Support asset '${id}' must use a path relative to its contract file.`);
  }

  const sourcePath = path.resolve(contractRoot, relativePath);
  if (!fs.existsSync(sourcePath)) {
    return failedAsset(id, "asset_missing", `Support asset does not exist: ${sourcePath}`, { sourcePath, relativePath });
  }

  const sourceBytes = fs.readFileSync(sourcePath);
  const sourceSha256 = sha256(sourceBytes);
  const expectedSha256 = String(entry.expectedSha256 || "").trim().toLowerCase();
  if (expectedSha256 && !/^[a-f0-9]{64}$/.test(expectedSha256)) {
    addFailure(failures, "expected_hash_invalid", "expectedSha256 must contain exactly 64 lowercase hexadecimal characters.");
  } else if (expectedSha256 && expectedSha256 !== sourceSha256) {
    addFailure(failures, "source_hash_mismatch", `Expected ${expectedSha256}, found ${sourceSha256}.`);
  }

  let png;
  try {
    png = decodePng(sourceBytes);
  } catch (error) {
    return failedAsset(id, "png_decode_failed", error instanceof Error ? error.message : String(error), {
      sourcePath,
      relativePath,
      sourceSha256,
      expectedSha256
    });
  }

  const pngInfo = {
    width: png.width,
    height: png.height,
    bitDepth: png.bitDepth,
    colorType: png.colorType,
    colorTypeName: png.colorType === 6 ? "truecolor-with-alpha" : png.colorType === 2 ? "truecolor" : `type-${png.colorType}`,
    interlaceMethod: png.interlaceMethod,
    rgba: png.bitDepth === 8 && png.colorType === 6 && png.interlaceMethod === 0
  };
  if (!pngInfo.rgba) {
    addFailure(
      failures,
      "png_not_rgba",
      `PNG must be non-interlaced 8-bit true RGBA (color type 6); found bitDepth=${png.bitDepth}, colorType=${png.colorType}, interlace=${png.interlaceMethod}.`
    );
  }
  if (!png.pixels) {
    return finishAsset({ id, relativePath, sourcePath, sourceSha256, expectedSha256, png: pngInfo, failures });
  }

  const samples = [];
  for (let y = 0; y < png.height; y += 1) {
    for (let x = 0; x < png.width; x += 1) samples.push(pixelAt(png.pixels, png.width, x, y));
  }
  const alpha = summarizeAlpha(samples, DEFAULT_THRESHOLDS.transparentAlphaMax, DEFAULT_THRESHOLDS.opaqueAlphaMin);
  const corners = [
    pixelAt(png.pixels, png.width, 0, 0),
    pixelAt(png.pixels, png.width, png.width - 1, 0),
    pixelAt(png.pixels, png.width, 0, png.height - 1),
    pixelAt(png.pixels, png.width, png.width - 1, png.height - 1)
  ];
  const transparentCorners = corners.filter((pixel) => pixel.a <= DEFAULT_THRESHOLDS.transparentAlphaMax).length;
  if (entry.requiredAlphaOutsideArtwork === true) {
    if (alpha.transparentPixels === 0) addFailure(failures, "transparent_background_missing", "Support asset requires transparent pixels outside its artwork.");
    if (alpha.opaquePixels === 0) addFailure(failures, "opaque_artwork_missing", "Support asset contains no opaque artwork pixels.");
    if (transparentCorners !== 4) addFailure(failures, "transparent_corners_missing", `Support asset requires four transparent outer corners; found ${transparentCorners}.`);
  }

  return finishAsset({
    id,
    relativePath,
    sourcePath,
    sourceSha256,
    expectedSha256,
    png: pngInfo,
    analysis: {
      role: String(entry.role || ""),
      requiredAlphaOutsideArtwork: entry.requiredAlphaOutsideArtwork === true,
      alpha,
      cornerAlpha: corners.map((pixel) => pixel.a),
      transparentCorners
    },
    failures
  });
}

function auditAsset(entry, contractRoot, contractDefaults, compoundContext = null) {
  const id = String(entry.id || "").trim();
  const failures = [];
  const shape = String(entry.shape || "").trim().toLowerCase();
  const presentation = String(entry.presentation || "").trim().toLowerCase();
  const relativePath = String(entry.path || "").trim();
  const thresholds = normalizeThresholds(mergeThresholdInputs(
    contractDefaults,
    presentation === "opaque-aperture-plate" ? APERTURE_PLATE_THRESHOLDS : {},
    entry
  ));

  if (!relativePath) return failedAsset(id, "asset_path_missing", `Overlay '${id}' has no path.`);
  if (path.isAbsolute(relativePath)) {
    return failedAsset(id, "asset_path_not_portable", `Overlay '${id}' must use a path relative to its contract file.`);
  }
  if (!SUPPORTED_SHAPES.has(shape)) {
    return failedAsset(id, "shape_unsupported", `Overlay '${id}' has unsupported shape '${shape || "missing"}'.`);
  }

  const sourcePath = path.resolve(contractRoot, relativePath);
  if (!fs.existsSync(sourcePath)) {
    return failedAsset(id, "asset_missing", `Overlay source does not exist: ${sourcePath}`, { sourcePath, relativePath, shape, thresholds });
  }

  const sourceBytes = fs.readFileSync(sourcePath);
  const sourceSha256 = sha256(sourceBytes);
  const expectedSha256 = String(entry.expectedSha256 || "").trim().toLowerCase();
  if (expectedSha256 && !/^[a-f0-9]{64}$/.test(expectedSha256)) {
    addFailure(failures, "expected_hash_invalid", "expectedSha256 must contain exactly 64 lowercase hexadecimal characters.");
  } else if (expectedSha256 && expectedSha256 !== sourceSha256) {
    addFailure(failures, "source_hash_mismatch", `Expected ${expectedSha256}, found ${sourceSha256}.`);
  }

  let png;
  try {
    png = decodePng(sourceBytes);
  } catch (error) {
    return failedAsset(id, "png_decode_failed", error instanceof Error ? error.message : String(error), {
      sourcePath,
      relativePath,
      sourceSha256,
      expectedSha256,
      shape,
      thresholds
    });
  }

  const pngInfo = {
    width: png.width,
    height: png.height,
    bitDepth: png.bitDepth,
    colorType: png.colorType,
    colorTypeName: png.colorType === 6 ? "truecolor-with-alpha" : png.colorType === 2 ? "truecolor" : `type-${png.colorType}`,
    interlaceMethod: png.interlaceMethod,
    rgba: png.bitDepth === 8 && png.colorType === 6 && png.interlaceMethod === 0
  };
  if (entry.declaredDimensions
      && (png.width !== entry.declaredDimensions.width || png.height !== entry.declaredDimensions.height)) {
    addFailure(
      failures,
      "declared_dimensions_mismatch",
      `Spec declares ${entry.declaredDimensions.width}x${entry.declaredDimensions.height}; PNG is ${png.width}x${png.height}.`
    );
  }
  if (!pngInfo.rgba) {
    addFailure(
      failures,
      "png_not_rgba",
      `PNG must be non-interlaced 8-bit true RGBA (color type 6); found bitDepth=${png.bitDepth}, colorType=${png.colorType}, interlace=${png.interlaceMethod}.`
    );
  }
  if (!png.pixels) {
    return finishAsset({
      id,
      relativePath,
      sourcePath,
      sourceSha256,
      expectedSha256,
      shape,
      thresholds,
      png: pngInfo,
      failures
    });
  }

  if (shape === "compound") {
    let children;
    try {
      children = resolveCompoundApertures(entry.apertures, png.width, png.height);
    } catch (error) {
      addFailure(failures, "compound_apertures_invalid", error instanceof Error ? error.message : String(error));
      return finishAsset({ id, relativePath, sourcePath, sourceSha256, expectedSha256, shape, presentation, thresholds, png: pngInfo, failures });
    }
    const results = children.map((child, index) => auditAsset({
      ...entry, id: `${id}/aperture-${index + 1}`, shape: child.shape,
      aperture: entry.apertures[index].aperture
    }, contractRoot, contractDefaults, { children, child }));
    for (const [index, result] of results.entries()) {
      failures.push(...result.failures.map((failure) => ({ ...failure, apertureIndex: index,
        message: `Aperture ${index + 1}: ${failure.message}` })));
    }
    const x = Math.min(...children.map((child) => child.aperture.x));
    const y = Math.min(...children.map((child) => child.aperture.y));
    const right = Math.max(...children.map((child) => child.aperture.x + child.aperture.width));
    const bottom = Math.max(...children.map((child) => child.aperture.y + child.aperture.height));
    return finishAsset({ id, relativePath, sourcePath, sourceSha256, expectedSha256, shape, presentation,
      aperture: { units: "pixels", x, y, width: right - x, height: bottom - y }, thresholds, png: pngInfo,
      analysis: { canvas: results[0].analysis?.canvas, exterior: results[0].analysis?.exterior,
        apertures: results.map((result, index) => ({ index, shape: children[index].shape,
          declaredAperture: children[index].aperture, measuredAperture: result.aperture,
          alphaFade: children[index].alphaFade, passed: result.passed, analysis: result.analysis, failures: result.failures })) },
      failures });
  }

  let aperture;
  try {
    aperture = resolveAperture(entry.aperture, png.width, png.height, shape);
  } catch (error) {
    addFailure(failures, "aperture_invalid", error instanceof Error ? error.message : String(error));
    return finishAsset({ id, relativePath, sourcePath, sourceSha256, expectedSha256, shape, thresholds, png: pngInfo, failures });
  }

  const aspect = aperture.width / aperture.height;
  if (shape === "circle" && Math.abs(aspect - 1) > thresholds.circleAspectTolerance) {
    addFailure(failures, "circle_aspect_invalid", `Circle aperture aspect ${round(aspect)} exceeds tolerance ${thresholds.circleAspectTolerance}.`);
  }
  if (shape === "memory" && Math.abs(aspect - 1) > thresholds.memoryAspectTolerance) {
    addFailure(failures, "memory_aspect_invalid", `Memory aperture must be approximately square; aspect ${round(aspect)} exceeds tolerance ${thresholds.memoryAspectTolerance}.`);
  }

  const declaredAperture = aperture;
  if (compoundContext?.child.alphaFade) {
    aperture = transparentFadeAperture(aperture, compoundContext.child.alphaFade, thresholds.transparentAlphaMax);
  }
  const analysis = analyzePixels(png, shape, aperture, thresholds, compoundContext?.children);
  if (compoundContext?.child.alphaFade) {
    analysis.alphaFade = analyzeAlphaFade(png, declaredAperture, compoundContext.child.alphaFade);
    if (analysis.alphaFade.mismatchedPixels > 0) {
      addFailure(failures, "aperture_alpha_fade_mismatch", `${analysis.alphaFade.mismatchedPixels} pixels differ from the declared linear alpha fade by more than one alpha unit.`);
    }
  }
  if (analysis.aperture.transparentRatio < thresholds.minimumApertureTransparentRatio) {
    addFailure(
      failures,
      "aperture_transparency",
      `Aperture transparency ${round(analysis.aperture.transparentRatio)} is below ${thresholds.minimumApertureTransparentRatio}.`
    );
  }
  if (analysis.apertureCenter.transparentRatio < thresholds.minimumApertureCenterTransparentRatio) {
    addFailure(
      failures,
      "aperture_center_transparency",
      `Aperture-center transparency ${round(analysis.apertureCenter.transparentRatio)} is below ${thresholds.minimumApertureCenterTransparentRatio}.`
    );
  }
  if (thresholds.allowedOpaqueLeakPixels != null && analysis.aperture.opaqueLeakPixels > thresholds.allowedOpaqueLeakPixels) {
    addFailure(
      failures,
      "opaque_leak_exceeded",
      `Aperture contains ${analysis.aperture.opaqueLeakPixels} opaque/leaking pixels; ${thresholds.allowedOpaqueLeakPixels} allowed.`
    );
  }
  if (thresholds.maximumOpaqueLeakRatio != null && analysis.aperture.opaqueLeakRatio > thresholds.maximumOpaqueLeakRatio) {
    addFailure(
      failures,
      "opaque_leak_ratio_exceeded",
      `Aperture opaque-leak ratio ${round(analysis.aperture.opaqueLeakRatio)} exceeds ${thresholds.maximumOpaqueLeakRatio}.`
    );
  }
  if (!analysis.transparentComponent.seed) {
    addFailure(failures, "aperture_component_missing", "No transparent pixel was found near the declared aperture center.");
  } else {
    if (analysis.transparentComponent.boundsCoverage < thresholds.minimumApertureBoundsCoverage) {
      addFailure(
        failures,
        "aperture_bounds_coverage",
        `Transparent aperture bounds coverage ${round(analysis.transparentComponent.boundsCoverage)} is below ${thresholds.minimumApertureBoundsCoverage}.`
      );
    }
    if (analysis.transparentComponent.boundsEscaped) {
      addFailure(failures, "aperture_component_escaped", "Transparent aperture connects beyond the declared bounds tolerance, indicating an open or broken surround.");
    }
  }
  if (analysis.surround.opaqueRatio < thresholds.minimumSurroundOpaqueRatio) {
    addFailure(
      failures,
      "surround_opacity",
      `Surround opacity ${round(analysis.surround.opaqueRatio)} is below ${thresholds.minimumSurroundOpaqueRatio}.`
    );
  }
  if (thresholds.allowedTransparentFringePixels != null && analysis.surround.transparentFringePixels > thresholds.allowedTransparentFringePixels) {
    addFailure(
      failures,
      "transparent_fringe_exceeded",
      `Surround contains ${analysis.surround.transparentFringePixels} transparent fringe pixels; ${thresholds.allowedTransparentFringePixels} allowed.`
    );
  }
  if (thresholds.maximumTransparentFringeRatio != null && analysis.surround.transparentFringeRatio > thresholds.maximumTransparentFringeRatio) {
    addFailure(
      failures,
      "transparent_fringe_ratio_exceeded",
      `Surround transparent-fringe ratio ${round(analysis.surround.transparentFringeRatio)} exceeds ${thresholds.maximumTransparentFringeRatio}.`
    );
  }
  if (analysis.contamination.haloPixelRatio > thresholds.maximumHaloPixelRatio) {
    addFailure(failures, "halo_contamination", `Suspicious halo ratio ${round(analysis.contamination.haloPixelRatio)} exceeds ${thresholds.maximumHaloPixelRatio}.`);
  }
  if (analysis.contamination.checkerboardContaminationRatio > thresholds.maximumCheckerboardContaminationRatio) {
    addFailure(
      failures,
      "checkerboard_contamination",
      `Checkerboard contamination ratio ${round(analysis.contamination.checkerboardContaminationRatio)} exceeds ${thresholds.maximumCheckerboardContaminationRatio}.`
    );
  }
  if (analysis.contamination.matteContaminationRatio > thresholds.maximumMatteContaminationRatio) {
    addFailure(
      failures,
      "matte_contamination",
      `Magenta/white matte contamination ratio ${round(analysis.contamination.matteContaminationRatio)} exceeds ${thresholds.maximumMatteContaminationRatio}.`
    );
  }
  if (presentation === "opaque-aperture-plate") {
    if (!analysis.canvas.opaqueCorners) {
      addFailure(
        failures,
        "plate_exterior_corners",
        `Aperture-plate corners must be opaque; alpha values are ${analysis.canvas.cornerAlpha.join(", ")}.`
      );
    }
    if (analysis.exterior.opaqueRatio < thresholds.minimumExteriorOpaqueRatio) {
      addFailure(
        failures,
        "plate_exterior_opacity",
        `Aperture-plate exterior opacity ${round(analysis.exterior.opaqueRatio)} is below ${thresholds.minimumExteriorOpaqueRatio}.`
      );
    }
    if (analysis.canvas.alpha.opaqueRatio < thresholds.minimumCanvasOpaqueRatio) {
      addFailure(
        failures,
        "plate_canvas_opacity",
        `Aperture-plate canvas opacity ${round(analysis.canvas.alpha.opaqueRatio)} is below ${thresholds.minimumCanvasOpaqueRatio}.`
      );
    }
    if (analysis.canvas.lightNeutralPixelRatio > thresholds.maximumLightNeutralPixelRatio) {
      addFailure(
        failures,
        "plate_light_neutral",
        `Aperture plate contains ${analysis.canvas.lightNeutralPixels} pale neutral pixels; ratio ${round(analysis.canvas.lightNeutralPixelRatio)} exceeds ${thresholds.maximumLightNeutralPixelRatio}.`
      );
    }
    if (analysis.canvas.transparentRgbLeakRatio > thresholds.maximumTransparentRgbLeakRatio) {
      addFailure(
        failures,
        "plate_transparent_rgb_leak",
        `Aperture plate contains ${analysis.canvas.transparentRgbLeakPixels} transparent pixels with nonzero RGB; ratio ${round(analysis.canvas.transparentRgbLeakRatio)} exceeds ${thresholds.maximumTransparentRgbLeakRatio}.`
      );
    }
  }

  return finishAsset({
    id,
    relativePath,
    sourcePath,
    sourceSha256,
    expectedSha256,
    shape,
    presentation,
    aperture,
    thresholds,
    png: pngInfo,
    analysis,
    failures
  });
}

function analyzePixels(png, shape, aperture, thresholds, compoundChildren = null) {
  const { width, height, pixels } = png;
  const allPixels = [];
  const innerPixels = [];
  const centerPixels = [];
  const edgePixels = [];
  const surroundPixels = [];
  const exteriorPixels = [];
  const transparentMax = thresholds.transparentAlphaMax;
  const opaqueMin = thresholds.opaqueAlphaMin;

  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const px = x + 0.5;
      const py = y + 0.5;
      const pixel = pixelAt(pixels, width, x, y);
      allPixels.push({ x, y, ...pixel });
      const insideInner = insideShape(px, py, aperture, shape, -thresholds.apertureInsetPixels);
      const insideAperture = insideShape(px, py, aperture, shape, 0);
      const insideCenter = insideScaledShape(px, py, aperture, shape, 0.5);
      const insideExpandedEdge = insideShape(px, py, aperture, shape, thresholds.edgeBandPixels);
      const insideDeclaredUnion = compoundChildren
        ? compoundChildren.some((child) => insideShape(px, py, child.aperture, child.shape, thresholds.edgeBandPixels))
        : insideExpandedEdge;
      const insideShrunkEdge = insideShape(px, py, aperture, shape, -thresholds.edgeBandPixels);
      const insideSurround = insideShape(
        px,
        py,
        aperture,
        shape,
        thresholds.edgeBandPixels + thresholds.surroundBandPixels
      ) && !insideExpandedEdge;
      if (insideInner) innerPixels.push({ x, y, ...pixel });
      if (insideCenter) centerPixels.push({ x, y, ...pixel });
      if (insideExpandedEdge && !insideShrunkEdge) edgePixels.push({ x, y, ...pixel });
      if (insideSurround && !insideDeclaredUnion) surroundPixels.push({ x, y, ...pixel });
      // Exclude the antialiased aperture transition band from the opaque-exterior proof.
      if (!insideDeclaredUnion) exteriorPixels.push({ x, y, ...pixel });
    }
  }

  const apertureSummary = summarizeAlpha(innerPixels, transparentMax, opaqueMin);
  apertureSummary.opaqueLeakPixels = innerPixels.filter((pixel) => pixel.a > transparentMax).length;
  apertureSummary.opaqueLeakRatio = ratio(apertureSummary.opaqueLeakPixels, innerPixels.length);
  const centerSummary = summarizeAlpha(centerPixels, transparentMax, opaqueMin);
  centerSummary.centerPixel = pixelAt(pixels, width, clamp(Math.floor(aperture.x + aperture.width / 2), 0, width - 1), clamp(Math.floor(aperture.y + aperture.height / 2), 0, height - 1));
  const edgeSummary = summarizeAlpha(edgePixels, transparentMax, opaqueMin);
  const surroundSummary = summarizeAlpha(surroundPixels, transparentMax, opaqueMin);
  surroundSummary.transparentFringePixels = surroundPixels.filter((pixel) => pixel.a <= transparentMax).length;
  surroundSummary.transparentFringeRatio = ratio(surroundSummary.transparentFringePixels, surroundPixels.length);
  const exteriorSummary = summarizeAlpha(exteriorPixels, transparentMax, opaqueMin);

  const component = transparentComponent(png, aperture, transparentMax, thresholds.boundsTolerancePixels);
  const suspiciousHalo = edgePixels.filter((pixel) => pixel.a > transparentMax && pixel.a < opaqueMin && suspiciousMatteColor(pixel));
  const mattePixels = innerPixels.filter((pixel) => pixel.a > transparentMax && suspiciousMatteColor(pixel));
  const checkerboard = checkerboardEvidence(innerPixels, transparentMax);
  const lightNeutralPixels = allPixels.filter((pixel) => {
    if (pixel.a <= transparentMax) return false;
    const maximum = Math.max(pixel.r, pixel.g, pixel.b);
    const minimum = Math.min(pixel.r, pixel.g, pixel.b);
    return minimum >= thresholds.lightNeutralLuminanceFloor
      && maximum - minimum <= thresholds.lightNeutralChromaCeiling;
  });
  const transparentPixels = allPixels.filter((pixel) => pixel.a === 0);
  const transparentRgbLeakPixels = transparentPixels.filter((pixel) => pixel.r !== 0 || pixel.g !== 0 || pixel.b !== 0);
  const cornerPixels = [
    pixelAt(pixels, width, 0, 0),
    pixelAt(pixels, width, width - 1, 0),
    pixelAt(pixels, width, 0, height - 1),
    pixelAt(pixels, width, width - 1, height - 1)
  ];
  const suspiciousColors = topColors([
    ...suspiciousHalo.map((pixel) => ({ ...pixel, category: "halo" })),
    ...mattePixels.map((pixel) => ({ ...pixel, category: "matte" })),
    ...checkerboard.pixels.map((pixel) => ({ ...pixel, category: "checkerboard" }))
  ]);

  return {
    canvas: {
      alpha: summarizeAlpha(allPixels, transparentMax, opaqueMin),
      cornerAlpha: cornerPixels.map((pixel) => pixel.a),
      transparentCorners: cornerPixels.every((pixel) => pixel.a <= transparentMax),
      opaqueCorners: cornerPixels.every((pixel) => pixel.a >= opaqueMin),
      lightNeutralPixels: lightNeutralPixels.length,
      lightNeutralPixelRatio: ratio(lightNeutralPixels.length, allPixels.length),
      lightNeutralThresholds: {
        luminanceFloor: thresholds.lightNeutralLuminanceFloor,
        chromaCeiling: thresholds.lightNeutralChromaCeiling
      },
      transparentRgbPixels: transparentPixels.length,
      transparentRgbLeakPixels: transparentRgbLeakPixels.length,
      transparentRgbLeakRatio: ratio(transparentRgbLeakPixels.length, transparentPixels.length)
    },
    aperture: apertureSummary,
    apertureCenter: centerSummary,
    apertureEdgeTransitionBand: edgeSummary,
    surround: surroundSummary,
    exterior: exteriorSummary,
    transparentComponent: component,
    contamination: {
      heuristic: "reign-overlay-contamination-heuristics-v1",
      suspiciousHaloPixels: suspiciousHalo.length,
      haloPixelRatio: ratio(suspiciousHalo.length, edgePixels.length),
      checkerboardCandidatePixels: checkerboard.candidateCount,
      checkerboardPatternPixels: checkerboard.pixels.length,
      checkerboardContaminationRatio: ratio(checkerboard.pixels.length, innerPixels.length),
      matteContaminationPixels: mattePixels.length,
      matteContaminationRatio: ratio(mattePixels.length, innerPixels.length),
      colors: suspiciousColors
    }
  };
}

function summarizeAlpha(samples, transparentMax, opaqueMin) {
  let transparentPixels = 0;
  let opaquePixels = 0;
  let minimumAlpha = null;
  let maximumAlpha = null;
  for (const pixel of samples) {
    if (pixel.a <= transparentMax) transparentPixels += 1;
    if (pixel.a >= opaqueMin) opaquePixels += 1;
    minimumAlpha = minimumAlpha == null ? pixel.a : Math.min(minimumAlpha, pixel.a);
    maximumAlpha = maximumAlpha == null ? pixel.a : Math.max(maximumAlpha, pixel.a);
  }
  const transitionPixels = samples.length - transparentPixels - opaquePixels;
  return {
    pixelCount: samples.length,
    transparentPixels,
    transitionPixels,
    opaquePixels,
    transparentRatio: ratio(transparentPixels, samples.length),
    transitionRatio: ratio(transitionPixels, samples.length),
    opaqueRatio: ratio(opaquePixels, samples.length),
    alphaBounds: { minimum: minimumAlpha, maximum: maximumAlpha }
  };
}

function transparentComponent(png, aperture, transparentMax, tolerance) {
  const { width, height, pixels } = png;
  const centerX = clamp(Math.floor(aperture.x + aperture.width / 2), 0, width - 1);
  const centerY = clamp(Math.floor(aperture.y + aperture.height / 2), 0, height - 1);
  const seed = nearestTransparentSeed(pixels, width, height, centerX, centerY, transparentMax);
  if (!seed) {
    return { seed: null, pixelCount: 0, bounds: null, expectedBounds: boundsOfAperture(aperture), boundsCoverage: 0, boundsEscaped: false };
  }

  const visited = new Uint8Array(width * height);
  const queueX = new Int32Array(width * height);
  const queueY = new Int32Array(width * height);
  let head = 0;
  let tail = 0;
  queueX[tail] = seed.x;
  queueY[tail] = seed.y;
  tail += 1;
  visited[seed.y * width + seed.x] = 1;
  let minX = seed.x;
  let minY = seed.y;
  let maxX = seed.x;
  let maxY = seed.y;

  while (head < tail) {
    const x = queueX[head];
    const y = queueY[head];
    head += 1;
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
    for (const [dx, dy] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
      const index = ny * width + nx;
      if (visited[index]) continue;
      if (pixelAt(pixels, width, nx, ny).a > transparentMax) continue;
      visited[index] = 1;
      queueX[tail] = nx;
      queueY[tail] = ny;
      tail += 1;
    }
  }

  const bounds = { x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1, right: maxX + 1, bottom: maxY + 1 };
  const expectedBounds = boundsOfAperture(aperture);
  const intersection = intersectBounds(bounds, expectedBounds);
  const boundsCoverage = ratio(intersection.width * intersection.height, expectedBounds.width * expectedBounds.height);
  const boundsEscaped = bounds.x < expectedBounds.x - tolerance
    || bounds.y < expectedBounds.y - tolerance
    || bounds.right > expectedBounds.right + tolerance
    || bounds.bottom > expectedBounds.bottom + tolerance;
  return { seed, pixelCount: tail, bounds, expectedBounds, boundsCoverage, boundsEscaped };
}

function nearestTransparentSeed(pixels, width, height, centerX, centerY, transparentMax) {
  for (let radius = 0; radius <= 8; radius += 1) {
    for (let y = centerY - radius; y <= centerY + radius; y += 1) {
      for (let x = centerX - radius; x <= centerX + radius; x += 1) {
        if (x < 0 || y < 0 || x >= width || y >= height) continue;
        if (Math.max(Math.abs(x - centerX), Math.abs(y - centerY)) !== radius) continue;
        if (pixelAt(pixels, width, x, y).a <= transparentMax) return { x, y };
      }
    }
  }
  return null;
}

function checkerboardEvidence(samples, transparentMax) {
  const candidates = samples.filter((pixel) => {
    if (pixel.a <= transparentMax) return false;
    const maximum = Math.max(pixel.r, pixel.g, pixel.b);
    const minimum = Math.min(pixel.r, pixel.g, pixel.b);
    const luminance = (pixel.r + pixel.g + pixel.b) / 3;
    return maximum - minimum <= 8 && luminance >= 145 && luminance <= 252;
  });
  const bins = new Map();
  for (const pixel of candidates) {
    const bin = Math.round(((pixel.r + pixel.g + pixel.b) / 3) / 8) * 8;
    bins.set(bin, (bins.get(bin) || 0) + 1);
  }
  const common = [...bins.entries()].sort((left, right) => right[1] - left[1]).slice(0, 4);
  let pair = null;
  for (let i = 0; i < common.length; i += 1) {
    for (let j = i + 1; j < common.length; j += 1) {
      if (Math.abs(common[i][0] - common[j][0]) >= 12) {
        pair = [common[i][0], common[j][0]];
        break;
      }
    }
    if (pair) break;
  }
  const pixels = pair
    ? candidates.filter((pixel) => pair.includes(Math.round(((pixel.r + pixel.g + pixel.b) / 3) / 8) * 8))
    : [];
  return { candidateCount: candidates.length, bins: common.map(([value, count]) => ({ value, count })), pair, pixels };
}

function suspiciousMatteColor(pixel) {
  const magentaSignal = Math.min(pixel.r, pixel.b) - pixel.g;
  const nearWhite = Math.min(pixel.r, pixel.g, pixel.b) >= 235 && Math.max(pixel.r, pixel.g, pixel.b) - Math.min(pixel.r, pixel.g, pixel.b) <= 12;
  return magentaSignal >= 70 || nearWhite;
}

function topColors(samples) {
  const colors = new Map();
  for (const sample of samples) {
    const key = rgbaHex(sample);
    const current = colors.get(key) || { rgba: key, count: 0, categories: new Set() };
    current.count += 1;
    current.categories.add(sample.category);
    colors.set(key, current);
  }
  return [...colors.values()]
    .sort((left, right) => right.count - left.count || left.rgba.localeCompare(right.rgba))
    .slice(0, 12)
    .map((entry) => ({ rgba: entry.rgba, count: entry.count, categories: [...entry.categories].sort() }));
}

function resolveCompoundApertures(values, imageWidth, imageHeight) {
  if (!Array.isArray(values) || values.length < 1 || values.length > 16) {
    throw new Error("Compound aperture owner must declare between one and sixteen child apertures.");
  }
  return values.map((child, index) => {
    if (!child || typeof child !== "object" || Array.isArray(child)) throw new Error(`apertures[${index}] must be an object.`);
    const shape = String(child.shape || "").toLowerCase();
    if (!SUPPORTED_SHAPES.has(shape) || shape === "compound") throw new Error(`apertures[${index}] has unsupported or nested shape '${shape}'.`);
    if (child.thresholds != null || Object.keys(DEFAULT_THRESHOLDS).some((key) => child[key] != null)) {
      throw new Error(`apertures[${index}] cannot override its owner's alpha thresholds.`);
    }
    const aperture = resolveAperture(child.aperture, imageWidth, imageHeight, shape);
    if (aperture.width <= 2 || aperture.height <= 2) throw new Error(`apertures[${index}] must exceed two pixels in both dimensions.`);
    if (shape === "polygon") {
      for (const key of ["x", "y", "width", "height"]) {
        if (child.aperture[key] == null) continue;
        const factor = aperture.units === "normalized" ? (["x", "width"].includes(key) ? imageWidth : imageHeight) : 1;
        if (Math.abs(finite(child.aperture[key], `apertures[${index}].aperture.${key}`) * factor - aperture[key]) > 1e-6) {
          throw new Error(`apertures[${index}] polygon ${key} does not match its point bounds.`);
        }
      }
    }
    let alphaFade = null;
    if (child.alphaFade != null) {
      const fade = child.alphaFade;
      if (shape !== "rectangle" || typeof fade !== "object" || Array.isArray(fade)
          || typeof fade.opaqueToTransparent !== "boolean") throw new Error(`apertures[${index}] alphaFade requires a rectangle and an explicit boolean direction.`);
      const fromX = finite(fade.fromX, `apertures[${index}].alphaFade.fromX`);
      const toX = finite(fade.toX, `apertures[${index}].alphaFade.toX`);
      if (fromX < aperture.x || toX <= fromX || toX > aperture.x + aperture.width) {
        throw new Error(`apertures[${index}] fade bounds must increase within the declared rectangle.`);
      }
      const clearWidth = fade.opaqueToTransparent ? aperture.x + aperture.width - toX : fromX - aperture.x;
      if (clearWidth <= 2) throw new Error(`apertures[${index}] fade must retain a clear region wider than two pixels.`);
      alphaFade = { fromX, toX, opaqueToTransparent: fade.opaqueToTransparent, maximumAlphaError: 1 };
    }
    return { shape, aperture, alphaFade };
  });
}

function transparentFadeAperture(aperture, fade, transparentMax) {
  const span = fade.toX - fade.fromX;
  if (fade.opaqueToTransparent) {
    const x = fade.fromX + (1 - transparentMax / 255) * span;
    return { ...aperture, x, width: aperture.x + aperture.width - x };
  }
  return { ...aperture, width: fade.fromX + transparentMax / 255 * span - aperture.x };
}

function analyzeAlphaFade(png, aperture, fade) {
  let comparedPixels = 0;
  let mismatchedPixels = 0;
  let maximumAlphaError = 0;
  for (let y = Math.max(0, Math.floor(aperture.y)); y < Math.min(png.height, Math.ceil(aperture.y + aperture.height)); y += 1) {
    for (let x = Math.max(0, Math.floor(aperture.x)); x < Math.min(png.width, Math.ceil(aperture.x + aperture.width)); x += 1) {
      if (!insideShape(x + 0.5, y + 0.5, aperture, "rectangle", 0)) continue;
      const progress = clamp((x - fade.fromX) / (fade.toX - fade.fromX), 0, 1);
      const expected = Math.round(255 * (fade.opaqueToTransparent ? 1 - progress : progress));
      const error = Math.abs(pixelAt(png.pixels, png.width, x, y).a - expected);
      comparedPixels += 1;
      if (error > 1) mismatchedPixels += 1;
      maximumAlphaError = Math.max(maximumAlphaError, error);
    }
  }
  return { ...fade, comparedPixels, mismatchedPixels, observedMaximumAlphaError: maximumAlphaError };
}

function resolveAperture(value, imageWidth, imageHeight, shape) {
  if (!value || typeof value !== "object") throw new Error("Overlay must declare an aperture object.");
  const units = String(value.units || "pixels").toLowerCase();
  if (!new Set(["pixels", "normalized"]).has(units)) throw new Error(`Aperture units must be pixels or normalized, found '${units}'.`);
  const factorX = units === "normalized" ? imageWidth : 1;
  const factorY = units === "normalized" ? imageHeight : 1;
  if (shape === "polygon") {
    if (!Array.isArray(value.points) || value.points.length < 3) throw new Error("Polygon aperture must declare at least three [x, y] points.");
    const points = value.points.map((point, index) => {
      if (!Array.isArray(point) || point.length !== 2) throw new Error(`aperture.points[${index}] must be a two-value [x, y] array.`);
      const x = finite(point[0], `aperture.points[${index}][0]`) * factorX;
      const y = finite(point[1], `aperture.points[${index}][1]`) * factorY;
      if (x < 0 || y < 0 || x > imageWidth || y > imageHeight) throw new Error(`aperture.points[${index}] lies outside ${imageWidth}x${imageHeight}.`);
      return { x, y };
    });
    const doubledArea = Math.abs(points.reduce((sum, point, index) => {
      const next = points[(index + 1) % points.length];
      return sum + point.x * next.y - next.x * point.y;
    }, 0));
    if (doubledArea <= 1e-6) throw new Error("Polygon aperture has zero area.");
    const x = Math.min(...points.map((point) => point.x));
    const y = Math.min(...points.map((point) => point.y));
    const right = Math.max(...points.map((point) => point.x));
    const bottom = Math.max(...points.map((point) => point.y));
    return { units, x, y, width: right - x, height: bottom - y, right, bottom, cornerRadiusPixels: 0, points };
  }
  const aperture = {
    units,
    x: finite(value.x, "aperture.x") * factorX,
    y: finite(value.y, "aperture.y") * factorY,
    width: finite(value.width, "aperture.width") * factorX,
    height: finite(value.height, "aperture.height") * factorY,
    cornerRadiusPixels: value.cornerRadiusPixels == null ? 0 : finite(value.cornerRadiusPixels, "aperture.cornerRadiusPixels")
  };
  if (aperture.width <= 2 || aperture.height <= 2) throw new Error("Aperture width and height must exceed two pixels.");
  if (aperture.x < 0 || aperture.y < 0 || aperture.x + aperture.width > imageWidth || aperture.y + aperture.height > imageHeight) {
    throw new Error(`Aperture ${JSON.stringify(aperture)} is outside ${imageWidth}x${imageHeight}.`);
  }
  return aperture;
}

function mergeThresholdInputs(...inputs) {
  const aliases = {
    minimumTransparentApertureRatio: "minimumApertureTransparentRatio",
    minimumOpaqueSurroundRatio: "minimumSurroundOpaqueRatio"
  };
  const result = {};
  const collect = (value) => {
    if (!value || typeof value !== "object" || Array.isArray(value)) return;
    for (const key of Object.keys(DEFAULT_THRESHOLDS)) {
      if (value[key] !== undefined) result[key] = value[key];
    }
    for (const [alias, canonical] of Object.entries(aliases)) {
      if (value[alias] !== undefined) result[canonical] = value[alias];
    }
    if (value.thresholds && value.thresholds !== value) collect(value.thresholds);
  };
  inputs.forEach(collect);
  return result;
}

function normalizeThresholds(value) {
  const result = { ...DEFAULT_THRESHOLDS };
  for (const key of Object.keys(DEFAULT_THRESHOLDS)) {
    if (value[key] === undefined) continue;
    if (["allowedTransparentFringePixels", "allowedOpaqueLeakPixels", "maximumOpaqueLeakRatio", "maximumTransparentFringeRatio"].includes(key) && value[key] == null) {
      result[key] = null;
      continue;
    }
    result[key] = finite(value[key], key);
  }
  for (const key of [
    "minimumApertureTransparentRatio",
    "minimumApertureCenterTransparentRatio",
    "minimumApertureBoundsCoverage",
    "minimumSurroundOpaqueRatio",
    "minimumExteriorOpaqueRatio",
    "maximumHaloPixelRatio",
    "maximumCheckerboardContaminationRatio",
    "maximumMatteContaminationRatio",
    "minimumCanvasTransparentRatio",
    "minimumCanvasOpaqueRatio",
    "maximumCanvasOpaqueRatio",
    "maximumLightNeutralPixelRatio",
    "maximumTransparentRgbLeakRatio",
    "maximumOpaqueLeakRatio",
    "maximumTransparentFringeRatio",
    "circleAspectTolerance",
    "memoryAspectTolerance"
  ]) {
    if (result[key] != null && (result[key] < 0 || result[key] > 1)) throw new Error(`${key} must be between 0 and 1 or null.`);
  }
  for (const key of ["transparentAlphaMax", "opaqueAlphaMin", "lightNeutralLuminanceFloor", "lightNeutralChromaCeiling"]) {
    if (result[key] < 0 || result[key] > 255) throw new Error(`${key} must be between 0 and 255.`);
  }
  if (result.transparentAlphaMax >= result.opaqueAlphaMin) throw new Error("transparentAlphaMax must be lower than opaqueAlphaMin.");
  for (const key of ["apertureInsetPixels", "edgeBandPixels", "surroundBandPixels", "boundsTolerancePixels"]) {
    if (result[key] < 0) throw new Error(`${key} cannot be negative.`);
  }
  for (const key of ["allowedTransparentFringePixels", "allowedOpaqueLeakPixels"]) {
    if (result[key] != null && (!Number.isInteger(result[key]) || result[key] < 0)) throw new Error(`${key} must be a non-negative integer or null.`);
  }
  return result;
}

function insideScaledShape(x, y, aperture, shape, scale) {
  if (shape === "polygon") {
    return pointInPolygon(x, y, scalePolygon(aperture.points, aperture, scale, scale));
  }
  const width = aperture.width * scale;
  const height = aperture.height * scale;
  const scaled = {
    ...aperture,
    x: aperture.x + (aperture.width - width) / 2,
    y: aperture.y + (aperture.height - height) / 2,
    width,
    height,
    cornerRadiusPixels: aperture.cornerRadiusPixels * scale
  };
  return insideShape(x, y, scaled, shape, 0);
}

function insideShape(x, y, aperture, shape, expansion) {
  if (shape === "polygon") {
    const scaleX = (aperture.width + expansion * 2) / aperture.width;
    const scaleY = (aperture.height + expansion * 2) / aperture.height;
    if (scaleX <= 0 || scaleY <= 0) return false;
    return pointInPolygon(x, y, scalePolygon(aperture.points, aperture, scaleX, scaleY));
  }
  const rect = {
    x: aperture.x - expansion,
    y: aperture.y - expansion,
    width: aperture.width + expansion * 2,
    height: aperture.height + expansion * 2,
    cornerRadiusPixels: Math.max(0, aperture.cornerRadiusPixels + expansion)
  };
  if (rect.width <= 0 || rect.height <= 0) return false;
  if (shape === "circle" || shape === "oval") {
    const radiusX = rect.width / 2;
    const radiusY = rect.height / 2;
    const centerX = rect.x + radiusX;
    const centerY = rect.y + radiusY;
    return ((x - centerX) ** 2) / (radiusX ** 2) + ((y - centerY) ** 2) / (radiusY ** 2) <= 1;
  }
  return insideRoundedRectangle(x, y, rect);
}

function insideRoundedRectangle(x, y, rect) {
  if (x < rect.x || y < rect.y || x > rect.x + rect.width || y > rect.y + rect.height) return false;
  const radius = Math.min(rect.cornerRadiusPixels || 0, rect.width / 2, rect.height / 2);
  if (radius <= 0) return true;
  const nearestX = clamp(x, rect.x + radius, rect.x + rect.width - radius);
  const nearestY = clamp(y, rect.y + radius, rect.y + rect.height - radius);
  return (x - nearestX) ** 2 + (y - nearestY) ** 2 <= radius ** 2;
}

function scalePolygon(points, aperture, scaleX, scaleY) {
  const centerX = aperture.x + aperture.width / 2;
  const centerY = aperture.y + aperture.height / 2;
  return points.map((point) => ({
    x: centerX + (point.x - centerX) * scaleX,
    y: centerY + (point.y - centerY) * scaleY
  }));
}

function pointInPolygon(x, y, points) {
  let inside = false;
  for (let current = 0, previous = points.length - 1; current < points.length; previous = current, current += 1) {
    const a = points[current];
    const b = points[previous];
    const crosses = (a.y > y) !== (b.y > y)
      && x < ((b.x - a.x) * (y - a.y)) / ((b.y - a.y) || Number.EPSILON) + a.x;
    if (crosses) inside = !inside;
  }
  return inside;
}

function boundsOfAperture(aperture) {
  return { x: aperture.x, y: aperture.y, width: aperture.width, height: aperture.height, right: aperture.x + aperture.width, bottom: aperture.y + aperture.height };
}

function intersectBounds(left, right) {
  const x = Math.max(left.x, right.x);
  const y = Math.max(left.y, right.y);
  const rightEdge = Math.min(left.right, right.right);
  const bottom = Math.min(left.bottom, right.bottom);
  return { x, y, width: Math.max(0, rightEdge - x), height: Math.max(0, bottom - y) };
}

function pixelAt(pixels, width, x, y) {
  const offset = (y * width + x) * 4;
  return { r: pixels[offset], g: pixels[offset + 1], b: pixels[offset + 2], a: pixels[offset + 3] };
}

function decodePng(bytes) {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  if (bytes.length < 8 || !bytes.subarray(0, 8).equals(signature)) throw new Error("Source is not a PNG file.");
  let offset = 8;
  let ihdr = null;
  let sawIend = false;
  const idat = [];
  while (offset + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(offset);
    const typeStart = offset + 4;
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    const crcEnd = dataEnd + 4;
    if (crcEnd > bytes.length) throw new Error("PNG chunk extends beyond the source file.");
    const typeBytes = bytes.subarray(typeStart, dataStart);
    const type = typeBytes.toString("ascii");
    const data = bytes.subarray(dataStart, dataEnd);
    const storedCrc = bytes.readUInt32BE(dataEnd);
    const computedCrc = crc32(Buffer.concat([typeBytes, data]));
    if (storedCrc !== computedCrc) throw new Error(`PNG ${type} chunk CRC is invalid.`);
    if (type === "IHDR") {
      if (length !== 13) throw new Error("PNG IHDR chunk has an invalid length.");
      ihdr = {
        width: data.readUInt32BE(0),
        height: data.readUInt32BE(4),
        bitDepth: data[8],
        colorType: data[9],
        compressionMethod: data[10],
        filterMethod: data[11],
        interlaceMethod: data[12]
      };
    } else if (type === "IDAT") {
      idat.push(data);
    } else if (type === "IEND") {
      sawIend = true;
      break;
    }
    offset = crcEnd;
  }
  if (!ihdr) throw new Error("PNG contains no IHDR chunk.");
  if (!sawIend) throw new Error("PNG contains no IEND chunk.");
  if (ihdr.width <= 0 || ihdr.height <= 0 || ihdr.width * ihdr.height > 100_000_000) throw new Error("PNG dimensions are invalid or exceed the audit safety limit.");
  if (ihdr.compressionMethod !== 0 || ihdr.filterMethod !== 0) throw new Error("PNG uses an unsupported compression or filter method.");
  if (ihdr.interlaceMethod !== 0) return { ...ihdr, pixels: null };
  if (ihdr.bitDepth !== 8 || ![2, 6].includes(ihdr.colorType)) return { ...ihdr, pixels: null };
  const channels = ihdr.colorType === 6 ? 4 : 3;
  const scanlineLength = ihdr.width * channels;
  const inflated = inflateSync(Buffer.concat(idat));
  const expectedLength = (scanlineLength + 1) * ihdr.height;
  if (inflated.length !== expectedLength) throw new Error(`PNG decoded to ${inflated.length} bytes, expected ${expectedLength}.`);
  const decoded = new Uint8Array(scanlineLength * ihdr.height);
  let sourceOffset = 0;
  for (let y = 0; y < ihdr.height; y += 1) {
    const filter = inflated[sourceOffset];
    sourceOffset += 1;
    const rowOffset = y * scanlineLength;
    for (let x = 0; x < scanlineLength; x += 1) {
      const raw = inflated[sourceOffset + x];
      const left = x >= channels ? decoded[rowOffset + x - channels] : 0;
      const up = y > 0 ? decoded[rowOffset - scanlineLength + x] : 0;
      const upperLeft = y > 0 && x >= channels ? decoded[rowOffset - scanlineLength + x - channels] : 0;
      decoded[rowOffset + x] = unfilter(filter, raw, left, up, upperLeft);
    }
    sourceOffset += scanlineLength;
  }
  const pixels = new Uint8Array(ihdr.width * ihdr.height * 4);
  for (let i = 0, target = 0; i < decoded.length; i += channels, target += 4) {
    pixels[target] = decoded[i];
    pixels[target + 1] = decoded[i + 1];
    pixels[target + 2] = decoded[i + 2];
    pixels[target + 3] = channels === 4 ? decoded[i + 3] : 255;
  }
  return { ...ihdr, pixels };
}

function unfilter(filter, raw, left, up, upperLeft) {
  if (filter === 0) return raw;
  if (filter === 1) return (raw + left) & 0xff;
  if (filter === 2) return (raw + up) & 0xff;
  if (filter === 3) return (raw + Math.floor((left + up) / 2)) & 0xff;
  if (filter === 4) return (raw + paeth(left, up, upperLeft)) & 0xff;
  throw new Error(`PNG uses unsupported row filter ${filter}.`);
}

function paeth(left, up, upperLeft) {
  const prediction = left + up - upperLeft;
  const leftDistance = Math.abs(prediction - left);
  const upDistance = Math.abs(prediction - up);
  const cornerDistance = Math.abs(prediction - upperLeft);
  if (leftDistance <= upDistance && leftDistance <= cornerDistance) return left;
  return upDistance <= cornerDistance ? up : upperLeft;
}

function runSelfTest() {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "reign-overlay-alpha-"));
  try {
    const assetsRoot = path.join(temporaryRoot, "assets");
    fs.mkdirSync(assetsRoot, { recursive: true });
    const fixtures = [
      fixture("pass-circle", "circle", { x: 24, y: 24, width: 80, height: 80 }),
      fixture("pass-oval", "oval", { x: 20, y: 30, width: 88, height: 68 }),
      fixture("pass-rectangle", "rectangle", { x: 22, y: 28, width: 84, height: 72 }),
      fixture("pass-memory", "memory", { x: 30, y: 30, width: 68, height: 68 }),
      fixture("pass-polygon", "polygon", { points: [[28, 28], [100, 28], [100, 80], [64, 104], [28, 80]] }),
      fixture("fail-not-rgba", "circle", { x: 24, y: 24, width: 80, height: 80 }, "rgb"),
      fixture("fail-checkerboard", "circle", { x: 24, y: 24, width: 80, height: 80 }, "checkerboard"),
      fixture("fail-halo", "circle", { x: 24, y: 24, width: 80, height: 80 }, "halo"),
      fixture("pass-aperture-plate", "circle", { x: 24, y: 24, width: 80, height: 80 }, "opaque-exterior"),
      fixture("fail-plate-ring-only", "circle", { x: 24, y: 24, width: 80, height: 80 }, "transparent-frame"),
      fixture("fail-plate-neutral-halo", "circle", { x: 24, y: 24, width: 80, height: 80 }, "neutral-frame"),
      fixture("fail-plate-backing-disk", "circle", { x: 24, y: 24, width: 80, height: 80 }, "backing-disk")
    ];
    fixtures.push(
      compoundFixture("pass-compound", "pass"),
      compoundFixture("pass-compound-fade", "fade"),
      compoundFixture("pass-compound-reverse-fade", "reverse-fade"),
      compoundFixture("fail-compound-second-blocked", "blocked"),
      compoundFixture("fail-compound-union-gap", "gap"),
      compoundFixture("fail-compound-corner", "corner"),
      compoundFixture("fail-compound-rgb-leak", "rgb-leak"),
      compoundFixture("fail-compound-fade-profile", "bad-fade"),
      compoundFixture("fail-compound-bounds", "bounds"),
      compoundFixture("fail-compound-fade-bounds", "fade-bounds"),
      compoundFixture("fail-compound-threshold-override", "threshold-override"),
      compoundFixture("fail-compound-nested", "nested")
    );
    const contract = {
      schema: CONTRACT_SCHEMA,
      defaults: {
        surroundBandPixels: 8,
        thresholds: {
          minimumTransparentApertureRatio: 0.97,
          minimumOpaqueSurroundRatio: 0.95,
          maximumOpaqueLeakRatio: 0.01,
          maximumTransparentFringeRatio: 0.01,
          allowedTransparentFringePixels: 0,
          allowedOpaqueLeakPixels: 0
        }
      },
      overlayAssets: []
    };
    for (const item of fixtures) {
      const fileName = `${item.id}.png`;
      const filePath = path.join(assetsRoot, fileName);
      fs.writeFileSync(filePath, encodeFixture(item));
      if (item.shape === "compound") {
        const apertures = JSON.parse(JSON.stringify(item.apertures));
        if (item.mode === "bounds") apertures[1].aperture.x += 1;
        if (item.mode === "fade-bounds") apertures[1].alphaFade.toX = 140;
        if (item.mode === "threshold-override") apertures[1].thresholds = { minimumApertureTransparentRatio: 0 };
        if (item.mode === "nested") apertures[1].shape = "compound";
        contract.overlayAssets.push({ id: item.id, path: `assets/${fileName}`, shape: "compound",
          presentation: "opaque-aperture-plate", apertures,
          thresholds: { ...APERTURE_PLATE_THRESHOLDS, minimumExteriorOpaqueRatio: 1 } });
        continue;
      }
      contract.overlayAssets.push({
        id: item.id,
        path: `assets/${fileName}`,
        shape: item.shape,
        presentation: ["transparent-frame", "opaque-exterior", "neutral-frame", "backing-disk"].includes(item.mode) ? "opaque-aperture-plate" : undefined,
        aperture: { units: "pixels", ...item.aperture },
        thresholds: item.mode === "halo"
          ? { allowedOpaqueLeakPixels: null }
          : ["transparent-frame", "opaque-exterior", "neutral-frame", "backing-disk"].includes(item.mode)
            ? APERTURE_PLATE_THRESHOLDS
            : undefined
      });
    }
    const contractPath = path.join(temporaryRoot, "modern-style-contract.json");
    fs.writeFileSync(contractPath, `${JSON.stringify(contract, null, 2)}\n`, "utf8");
    const audit = auditContractFile(contractPath);
    const byId = new Map(audit.assets.map((asset) => [asset.id, asset]));
    const expectations = [
      ...["pass-circle", "pass-oval", "pass-rectangle", "pass-memory", "pass-polygon"].map((id) => ({ id, expectedPass: true })),
      { id: "fail-not-rgba", expectedPass: false, expectedFailure: "png_not_rgba" },
      { id: "fail-checkerboard", expectedPass: false, expectedFailure: "checkerboard_contamination" },
      { id: "fail-halo", expectedPass: false, expectedFailure: "halo_contamination" },
      { id: "pass-aperture-plate", expectedPass: true },
      { id: "fail-plate-ring-only", expectedPass: false, expectedFailure: "plate_exterior_corners" },
      { id: "fail-plate-neutral-halo", expectedPass: false, expectedFailure: "plate_light_neutral" },
      { id: "fail-plate-backing-disk", expectedPass: false, expectedFailure: "aperture_center_transparency" },
      ...["pass-compound", "pass-compound-fade", "pass-compound-reverse-fade"].map((id) => ({ id, expectedPass: true })),
      { id: "fail-compound-second-blocked", expectedPass: false, expectedFailure: "aperture_transparency" },
      { id: "fail-compound-union-gap", expectedPass: false, expectedFailure: "plate_exterior_opacity" },
      { id: "fail-compound-corner", expectedPass: false, expectedFailure: "plate_exterior_corners" },
      { id: "fail-compound-rgb-leak", expectedPass: false, expectedFailure: "plate_transparent_rgb_leak" },
      { id: "fail-compound-fade-profile", expectedPass: false, expectedFailure: "aperture_alpha_fade_mismatch" },
      ...["fail-compound-bounds", "fail-compound-fade-bounds", "fail-compound-threshold-override", "fail-compound-nested"]
        .map((id) => ({ id, expectedPass: false, expectedFailure: "compound_apertures_invalid" }))
    ].map((expectation) => {
      const actual = byId.get(expectation.id);
      const failureCodes = (actual?.failures || []).map((failure) => failure.code);
      const matched = Boolean(actual)
        && actual.passed === expectation.expectedPass
        && (!expectation.expectedFailure || failureCodes.includes(expectation.expectedFailure));
      return { ...expectation, actualPass: actual?.passed ?? null, failureCodes, matched };
    });
    const passed = expectations.every((expectation) => expectation.matched);
    return {
      ...audit,
      generatedUtc: new Date().toISOString(),
      mode: "self-test",
      passed,
      selfTest: {
        schema: "reign-ui-overlay-alpha-self-test-v2",
        passed,
        expectationCount: expectations.length,
        matchedCount: expectations.filter((expectation) => expectation.matched).length,
        expectations
      }
    };
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
}

function fixture(id, shape, aperture, mode = "pass") {
  return { id, shape, aperture, mode, width: 128, height: 128 };
}

function encodeFixture(item) {
  if (item.shape === "compound") return encodeCompoundFixture(item);
  const rgba = new Uint8Array(item.width * item.height * 4);
  const band = 12;
  const resolvedAperture = resolveAperture(
    { units: "pixels", ...item.aperture },
    item.width,
    item.height,
    item.shape
  );
  for (let y = 0; y < item.height; y += 1) {
    for (let x = 0; x < item.width; x += 1) {
      const px = x + 0.5;
      const py = y + 0.5;
      const insideAperture = insideShape(px, py, resolvedAperture, item.shape, 0);
      const insideFrame = insideShape(px, py, resolvedAperture, item.shape, band);
      let color = { r: 0, g: 0, b: 0, a: 0 };
      if (["opaque-exterior", "neutral-frame", "backing-disk"].includes(item.mode) && !insideAperture) color = { r: 10, g: 10, b: 9, a: 255 };
      if (insideFrame && !insideAperture) color = { r: 151, g: 120, b: 63, a: 255 };
      if (item.mode === "neutral-frame" && insideFrame && !insideAperture) color = { r: 196, g: 196, b: 192, a: 255 };
      if (item.mode === "backing-disk" && insideAperture) color = { r: 6, g: 6, b: 6, a: 255 };
      if (item.mode === "checkerboard" && insideAperture) {
        const bright = (Math.floor(x / 8) + Math.floor(y / 8)) % 2 === 0 ? 192 : 224;
        color = { r: bright, g: bright, b: bright, a: 255 };
      }
      if (item.mode === "halo" && insideAperture && !insideShape(px, py, resolvedAperture, item.shape, -4)) {
        color = { r: 255, g: 0, b: 255, a: 128 };
      }
      const offset = (y * item.width + x) * 4;
      rgba[offset] = color.r;
      rgba[offset + 1] = color.g;
      rgba[offset + 2] = color.b;
      rgba[offset + 3] = color.a;
    }
  }
  return item.mode === "rgb" ? encodePng(item.width, item.height, rgba, 2) : encodePng(item.width, item.height, rgba, 6);
}

function compoundFixture(id, mode) {
  const circle = { shape: "circle", aperture: { units: "pixels", x: 12, y: 20, width: 44, height: 44 } };
  const polygon = { shape: "polygon", aperture: { units: "pixels", x: 62, y: 18, width: 34, height: 54,
    points: [[62, 18], [96, 18], [96, 56], [79, 72], [62, 56]] } };
  const fading = ["fade", "reverse-fade", "bad-fade", "fade-bounds"].includes(mode);
  const second = fading
    ? { shape: "rectangle", aperture: { units: "pixels", x: 80, y: 16, width: 60, height: 50 },
      alphaFade: mode === "reverse-fade" ? { fromX: 110, toX: 140, opaqueToTransparent: false }
        : { fromX: 80, toX: 105, opaqueToTransparent: true } }
    : polygon;
  return { id, shape: "compound", mode, width: 160, height: 96, apertures: [circle, second] };
}

function encodeCompoundFixture(item) {
  const rgba = new Uint8Array(item.width * item.height * 4);
  const children = resolveCompoundApertures(item.apertures, item.width, item.height);
  for (let y = 0; y < item.height; y += 1) {
    for (let x = 0; x < item.width; x += 1) {
      let alpha = 255;
      for (const [index, child] of children.entries()) {
        if (!insideShape(x + .5, y + .5, child.aperture, child.shape, 0)) continue;
        if (item.mode === "blocked" && index === 1) continue;
        if (child.alphaFade) {
          const progress = clamp((x - child.alphaFade.fromX) / (child.alphaFade.toX - child.alphaFade.fromX), 0, 1);
          alpha = Math.round(255 * (child.alphaFade.opaqueToTransparent ? 1 - progress : progress));
        } else alpha = 0;
      }
      if (item.mode === "gap" && x >= 58 && x < 60 && y >= 30 && y < 50) alpha = 0;
      if (item.mode === "corner" && x === 0 && y === 0) alpha = 0;
      if (item.mode === "bad-fade" && x === 90 && y === 35) alpha = 50;
      const offset = (y * item.width + x) * 4;
      rgba[offset] = item.mode === "rgb-leak" && x === 34 && y === 42 ? 1 : alpha === 0 ? 0 : 10;
      rgba[offset + 1] = alpha === 0 ? 0 : 10;
      rgba[offset + 2] = alpha === 0 ? 0 : 9;
      rgba[offset + 3] = alpha;
    }
  }
  return encodePng(item.width, item.height, rgba, 6);
}

function encodePng(width, height, rgba, colorType) {
  const channels = colorType === 6 ? 4 : 3;
  const raw = Buffer.alloc((width * channels + 1) * height);
  let target = 0;
  for (let y = 0; y < height; y += 1) {
    raw[target] = 0;
    target += 1;
    for (let x = 0; x < width; x += 1) {
      const source = (y * width + x) * 4;
      raw[target] = rgba[source];
      raw[target + 1] = rgba[source + 1];
      raw[target + 2] = rgba[source + 2];
      if (channels === 4) raw[target + 3] = rgba[source + 3];
      target += channels;
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = colorType;
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(raw)),
    pngChunk("IEND", Buffer.alloc(0))
  ]);
}

function pngChunk(type, data) {
  const typeBytes = Buffer.from(type, "ascii");
  const chunk = Buffer.alloc(12 + data.length);
  chunk.writeUInt32BE(data.length, 0);
  typeBytes.copy(chunk, 4);
  data.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])), 8 + data.length);
  return chunk;
}

function crc32(bytes) {
  if (!crcTable) {
    crcTable = new Uint32Array(256);
    for (let index = 0; index < 256; index += 1) {
      let value = index;
      for (let bit = 0; bit < 8; bit += 1) value = (value & 1) ? (0xedb88320 ^ (value >>> 1)) : (value >>> 1);
      crcTable[index] = value >>> 0;
    }
  }
  let crc = 0xffffffff;
  for (const byte of bytes) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function finishAsset(value) {
  const failures = value.failures || [];
  return {
    id: value.id,
    passed: failures.length === 0,
    path: value.relativePath || "",
    sourcePath: value.sourcePath || "",
    sourceSha256: value.sourceSha256 || "",
    expectedSha256: value.expectedSha256 || "",
    shape: value.shape || "",
    presentation: value.presentation || "",
    aperture: value.aperture || null,
    thresholds: value.thresholds || null,
    png: value.png || null,
    analysis: value.analysis || null,
    failures
  };
}

function failedAsset(id, code, message, extra = {}) {
  return finishAsset({ id, ...extra, failures: [{ code, message }] });
}

function addFailure(failures, code, message) {
  failures.push({ code, message });
}

function parseJson(bytes, sourcePath) {
  try {
    return JSON.parse(bytes.toString("utf8").replace(/^\uFEFF/, ""));
  } catch (error) {
    throw new Error(`Invalid JSON in ${sourcePath}: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function writeReport(output, report) {
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, `${JSON.stringify(report, null, 2)}\n`, "utf8");
}

function printSummary(report, output) {
  process.stdout.write(`${JSON.stringify({
    schema: report.schema,
    mode: report.mode,
    passed: report.passed,
    assetCount: report.assetCount,
    passedCount: report.passedCount,
    failedCount: report.failedCount,
    selfTest: report.selfTest ? { passed: report.selfTest.passed, matchedCount: report.selfTest.matchedCount, expectationCount: report.selfTest.expectationCount } : undefined,
    errors: report.errors || [],
    outputPath: output
  }, null, 2)}\n`);
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function finite(value, name) {
  const number = Number(value);
  if (!Number.isFinite(number)) throw new Error(`${name} must be a finite number.`);
  return number;
}

function clamp(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, value));
}

function ratio(numerator, denominator) {
  return denominator ? numerator / denominator : 0;
}

function round(value) {
  return Number(Number(value || 0).toFixed(6));
}

function rgbaHex(pixel) {
  return `#${[pixel.r, pixel.g, pixel.b, pixel.a].map((value) => value.toString(16).padStart(2, "0")).join("")}`;
}
