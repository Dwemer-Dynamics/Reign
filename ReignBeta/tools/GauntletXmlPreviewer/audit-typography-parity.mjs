import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const defaultModuleRoot = path.resolve(toolRoot, "../..");
const defaultPrefabRoot = path.join(defaultModuleRoot, "GUI", "Prefabs");
const defaultCatalogPath = path.join(toolRoot, "calibration", "ui-catalog.json");
const defaultPalettePath = path.join(defaultModuleRoot, "artwork", "ui-modern-style-kit", "palette.json");
const defaultContractPath = path.join(toolRoot, "calibration", "full-catalog-typography-parity-contract.json");

export const schema = "reign-ui-typography-parity-contract-v1";
export const auditSchema = "reign-ui-typography-parity-audit-v1";
export const fontAssetAuditSchema = "reign-ui-runtime-font-asset-audit-v1";
export const liveFontAlias = "ReignSerifDynamic";
export const liveFontCategory = "ui_reignbeta_fonts";

const requiredDynamicGlyphCodePoints = Object.freeze([
  ...Array.from({ length: 95 }, (_, index) => index + 32),
  160,
  171,
  187,
  8211,
  8212,
  8216,
  8217,
  8220,
  8221,
  8226,
  8230
]);

export function auditRuntimeFontAssets(options = {}) {
  const moduleRoot = path.resolve(options.moduleRoot || defaultModuleRoot);
  const paths = resolveFontAssetPaths(moduleRoot, options);
  const errors = [];
  const checkedFiles = [];
  const addError = (code, message, filePath = "") => errors.push({
    code,
    message,
    path: filePath ? toModuleRelative(filePath, moduleRoot) : ""
  });
  const readRequired = (filePath, code, label) => {
    if (!fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) {
      addError(code, `${label} is missing at ${toModuleRelative(filePath, moduleRoot)}.`, filePath);
      return null;
    }
    checkedFiles.push(toModuleRelative(filePath, moduleRoot));
    return fs.readFileSync(filePath);
  };

  const fntBytes = readRequired(paths.fnt, "missing-font-definition", "Runtime font definition");
  const atlasBytes = readRequired(paths.atlas, "missing-font-atlas", "Runtime font source atlas");
  const provenanceBytes = readRequired(paths.provenance, "missing-font-provenance", "Runtime font provenance");
  const sourceLicenseBytes = readRequired(paths.sourceLicense, "missing-font-license", "Provenance font license");
  const packagedLicenseBytes = readRequired(paths.packagedLicense, "missing-packaged-font-license", "Packaged runtime font license");
  const spriteDataBytes = readRequired(paths.spriteData, "missing-font-sprite-data", "Reign SpriteData registry");
  const manifestBytes = readRequired(paths.manifest, "missing-runtime-font-manifest", "Runtime sprite-sheet manifest");

  let fntText = "";
  let fntPage = "";
  let fntWidth = 0;
  let fntHeight = 0;
  let declaredGlyphCount = 0;
  const glyphIds = new Set();
  if (fntBytes) {
    fntText = fntBytes.toString("utf8");
    if (path.basename(paths.fnt, path.extname(paths.fnt)) !== liveFontAlias) {
      addError("font-alias-basename-mismatch", `Font definition basename must be '${liveFontAlias}'.`, paths.fnt);
    }
    const pageTags = [...fntText.matchAll(/<page\b([^<>]*?)(?:\/>|>)/gis)];
    if (pageTags.length !== 1) {
      addError("font-page-count-mismatch", `Font definition must declare exactly one page; found ${pageTags.length}.`, paths.fnt);
    } else {
      const pageAttributes = parseAttributes(pageTags[0][1] || "");
      fntPage = String(pageAttributes.file || "").trim();
      if (pageAttributes.id !== "0") addError("font-page-id-mismatch", `Font page must use id='0', found '${pageAttributes.id ?? "missing"}'.`, paths.fnt);
      if (path.basename(fntPage) !== `${liveFontAlias}.png` || path.basename(fntPage, path.extname(fntPage)) !== liveFontAlias) {
        addError("font-page-alias-mismatch", `Font page '${fntPage || "missing"}' does not resolve to alias '${liveFontAlias}'.`, paths.fnt);
      }
    }
    const commonMatch = fntText.match(/<common\b([^<>]*?)(?:\/>|>)/is);
    if (!commonMatch) {
      addError("invalid-font-definition", "Font definition has no <common> metrics element.", paths.fnt);
    } else {
      const common = parseAttributes(commonMatch[1] || "");
      fntWidth = positiveInteger(common.scaleW);
      fntHeight = positiveInteger(common.scaleH);
      if (!fntWidth || !fntHeight) addError("invalid-font-atlas-dimensions", `Font definition has invalid scaleW/scaleH '${common.scaleW ?? "missing"}x${common.scaleH ?? "missing"}'.`, paths.fnt);
    }
    const charsMatch = fntText.match(/<chars\b([^<>]*?)>/is);
    declaredGlyphCount = charsMatch ? nonNegativeInteger(parseAttributes(charsMatch[1] || "").count) : -1;
    for (const match of fntText.matchAll(/<char\b([^<>]*?)(?:\/>|>)/gis)) {
      const id = nonNegativeInteger(parseAttributes(match[1] || "").id);
      if (id >= 0) glyphIds.add(id);
    }
    if (declaredGlyphCount < 0) addError("invalid-font-definition", "Font definition has no valid <chars count> declaration.", paths.fnt);
    else if (declaredGlyphCount !== glyphIds.size) addError("font-glyph-count-mismatch", `Font definition declares ${declaredGlyphCount} glyphs but contains ${glyphIds.size} unique character IDs.`, paths.fnt);
    const missingRequiredGlyphs = requiredDynamicGlyphCodePoints.filter((codePoint) => !glyphIds.has(codePoint));
    if (missingRequiredGlyphs.length) {
      addError("missing-required-font-glyph", `Runtime font lacks required dynamic-text code points: ${missingRequiredGlyphs.join(", ")}.`, paths.fnt);
    }
  }

  const atlasDimensions = atlasBytes ? readPngDimensions(atlasBytes) : null;
  if (atlasBytes && !atlasDimensions) addError("invalid-font-atlas", "Runtime font source atlas is not a valid PNG with an IHDR header.", paths.atlas);
  if (atlasDimensions && fntWidth && fntHeight && (atlasDimensions.width !== fntWidth || atlasDimensions.height !== fntHeight)) {
    addError("font-atlas-dimension-mismatch", `FNT metrics expect ${fntWidth}x${fntHeight}, but the source atlas is ${atlasDimensions.width}x${atlasDimensions.height}.`, paths.atlas);
  }

  let provenance = null;
  if (provenanceBytes) {
    try {
      provenance = JSON.parse(provenanceBytes.toString("utf8"));
    } catch (error) {
      addError("invalid-font-provenance", `Font provenance is not valid JSON: ${error.message}`, paths.provenance);
    }
  }
  const sourceLicensePath = paths.sourceLicense;
  if (provenance) {
    if (provenance.schema !== "reign-ui-font-provenance/v1") addError("invalid-font-provenance", `Unsupported provenance schema '${provenance.schema || "missing"}'.`, paths.provenance);
    if (provenance.runtimeAlias !== liveFontAlias) addError("font-provenance-alias-mismatch", `Provenance runtimeAlias must be '${liveFontAlias}', found '${provenance.runtimeAlias || "missing"}'.`, paths.provenance);
    if (provenance.source?.family !== "Cormorant Garamond" || provenance.source?.style !== "Medium" || Number(provenance.source?.weight) !== 500) {
      addError("font-provenance-source-mismatch", "Provenance must bind Cormorant Garamond Medium weight 500.", paths.provenance);
    }
    const provenanceFntPath = resolveConfinedPath(path.dirname(paths.provenance), provenance.runtimeFiles?.fnt);
    const provenanceAtlasPath = resolveConfinedPath(path.dirname(paths.provenance), provenance.runtimeFiles?.atlas);
    if (provenanceFntPath !== path.resolve(paths.fnt)) addError("provenance-fnt-path-mismatch", "Provenance runtimeFiles.fnt does not resolve to the registered FNT path.", paths.provenance);
    if (provenanceAtlasPath !== path.resolve(paths.atlas)) addError("provenance-atlas-path-mismatch", "Provenance runtimeFiles.atlas does not resolve to the registered source atlas path.", paths.provenance);
    if (fntBytes && normalizeHash(provenance.runtimeFiles?.fntSha256) !== sha256(fntBytes)) addError("provenance-fnt-hash-mismatch", "Provenance FNT SHA-256 does not match the packaged definition.", paths.provenance);
    if (atlasBytes && normalizeHash(provenance.runtimeFiles?.atlasSha256) !== sha256(atlasBytes)) addError("provenance-atlas-hash-mismatch", "Provenance atlas SHA-256 does not match the packaged source atlas.", paths.provenance);
    if (Number(provenance.generation?.atlasWidth) !== fntWidth || Number(provenance.generation?.atlasHeight) !== fntHeight) {
      addError("provenance-atlas-dimension-mismatch", "Provenance atlas dimensions do not match the FNT metrics.", paths.provenance);
    }
    if (Number(provenance.generation?.glyphCount) !== declaredGlyphCount) addError("provenance-glyph-count-mismatch", "Provenance glyphCount does not match the FNT definition.", paths.provenance);
    const declaredLicensePath = resolveConfinedPath(path.dirname(paths.provenance), provenance.source?.licenseFile);
    if (!declaredLicensePath || !isPathWithin(path.dirname(paths.provenance), declaredLicensePath)) {
      addError("invalid-font-license-path", "Provenance source.licenseFile must resolve inside the font-sources directory.", paths.provenance);
    } else if (declaredLicensePath !== path.resolve(paths.sourceLicense)) {
      addError("font-license-path-mismatch", "Provenance source.licenseFile does not resolve to the canonical Cormorant license path.", paths.provenance);
    } else if (sourceLicenseBytes && normalizeHash(provenance.source?.licenseSha256) !== sha256(sourceLicenseBytes)) {
      addError("license-hash-mismatch", "Provenance license SHA-256 does not match its referenced license file.", sourceLicensePath);
    }
  }
  if (sourceLicenseBytes && packagedLicenseBytes && !sourceLicenseBytes.equals(packagedLicenseBytes)) {
    addError("packaged-license-mismatch", "Packaged runtime font license differs from the provenance license.", paths.packagedLicense);
  }

  let spriteCategory = null;
  let spritePart = null;
  let genericSprite = null;
  if (spriteDataBytes) {
    const spriteDataText = spriteDataBytes.toString("utf8");
    const categories = findNamedXmlBlocks(spriteDataText, "SpriteCategory", liveFontCategory);
    const parts = findNamedXmlBlocks(spriteDataText, "SpritePart", liveFontAlias);
    const sprites = findNamedXmlBlocks(spriteDataText, "GenericSprite", liveFontAlias);
    if (categories.length !== 1) addError("font-sprite-category-count-mismatch", `SpriteData must contain exactly one '${liveFontCategory}' category; found ${categories.length}.`, paths.spriteData);
    else {
      spriteCategory = {
        name: liveFontCategory,
        alwaysLoad: /<AlwaysLoad\b[^>]*\/?\s*>/i.test(categories[0]),
        spriteSheetCount: positiveInteger(xmlElementText(categories[0], "SpriteSheetCount")),
        sheet: parseSpriteSheetSize(categories[0], 1)
      };
      if (!spriteCategory.alwaysLoad) addError("font-sprite-category-not-always-loaded", `Sprite category '${liveFontCategory}' must declare <AlwaysLoad/>.`, paths.spriteData);
      if (spriteCategory.spriteSheetCount !== 1 || !spriteCategory.sheet?.width || !spriteCategory.sheet?.height) {
        addError("font-sprite-category-sheet-mismatch", `Sprite category '${liveFontCategory}' must declare one valid sheet with ID 1.`, paths.spriteData);
      }
    }
    if (parts.length !== 1) addError("font-sprite-part-count-mismatch", `SpriteData must contain exactly one SpritePart named '${liveFontAlias}'; found ${parts.length}.`, paths.spriteData);
    else {
      spritePart = {
        name: liveFontAlias,
        categoryName: xmlElementText(parts[0], "CategoryName"),
        sheetId: positiveInteger(xmlElementText(parts[0], "SheetID")),
        x: nonNegativeInteger(xmlElementText(parts[0], "SheetX")),
        y: nonNegativeInteger(xmlElementText(parts[0], "SheetY")),
        width: positiveInteger(xmlElementText(parts[0], "Width")),
        height: positiveInteger(xmlElementText(parts[0], "Height"))
      };
      if (spritePart.categoryName !== liveFontCategory || spritePart.sheetId !== 1 || spritePart.width !== fntWidth || spritePart.height !== fntHeight) {
        addError("font-sprite-part-mismatch", `SpritePart '${liveFontAlias}' does not bind the FNT atlas to '${liveFontCategory}' sheet 1 with matching dimensions.`, paths.spriteData);
      }
    }
    if (sprites.length !== 1) addError("font-generic-sprite-count-mismatch", `SpriteData must contain exactly one GenericSprite named '${liveFontAlias}'; found ${sprites.length}.`, paths.spriteData);
    else {
      genericSprite = { name: liveFontAlias, spritePartName: xmlElementText(sprites[0], "SpritePartName") };
      if (genericSprite.spritePartName !== liveFontAlias) addError("font-generic-sprite-mismatch", `GenericSprite '${liveFontAlias}' must resolve SpritePart '${liveFontAlias}'.`, paths.spriteData);
    }
  }

  let manifest = null;
  let manifestPart = null;
  let runtimeSheet = null;
  let runtimeSheetPath = "";
  let runtimeSheetBytes = null;
  if (manifestBytes) {
    try {
      manifest = JSON.parse(manifestBytes.toString("utf8"));
    } catch (error) {
      addError("invalid-runtime-font-manifest", `Runtime sprite-sheet manifest is not valid JSON: ${error.message}`, paths.manifest);
    }
  }
  if (manifest) {
    if (spriteDataBytes && normalizeHash(manifest.spriteDataSha256) !== sha256(spriteDataBytes)) addError("runtime-manifest-sprite-data-hash-mismatch", "Runtime manifest SpriteData SHA-256 does not match ReignBetaSpriteData.xml.", paths.manifest);
    const category = manifest.categories?.[liveFontCategory];
    if (!category) {
      addError("missing-runtime-font-category", `Runtime manifest has no '${liveFontCategory}' category.`, paths.manifest);
    } else {
      manifestPart = category.parts?.[liveFontAlias] || null;
      if (!manifestPart) {
        addError("missing-runtime-font-part", `Runtime manifest has no '${liveFontAlias}' part.`, paths.manifest);
      } else {
        const expectedSourcePath = toPosix(path.relative(moduleRoot, paths.atlas));
        if (manifestPart.sourcePath !== expectedSourcePath) addError("runtime-font-source-path-mismatch", `Manifest sourcePath must be '${expectedSourcePath}', found '${manifestPart.sourcePath || "missing"}'.`, paths.manifest);
        if (atlasBytes && normalizeHash(manifestPart.sourceSha256) !== sha256(atlasBytes)) addError("runtime-font-source-hash-mismatch", "Manifest font source SHA-256 does not match the source atlas.", paths.manifest);
        if (spritePart && (Number(manifestPart.sheetId) !== spritePart.sheetId || Number(manifestPart.x) !== spritePart.x || Number(manifestPart.y) !== spritePart.y || Number(manifestPart.width) !== spritePart.width || Number(manifestPart.height) !== spritePart.height)) {
          addError("runtime-font-part-geometry-mismatch", "Runtime manifest font-part geometry does not match SpriteData.", paths.manifest);
        }
        runtimeSheet = (category.sheets || []).find((sheet) => Number(sheet.id) === Number(manifestPart.sheetId)) || null;
      }
      if (manifestPart && !runtimeSheet) addError("missing-runtime-font-sheet", `Runtime manifest has no sheet ${manifestPart.sheetId} for '${liveFontAlias}'.`, paths.manifest);
      if (runtimeSheet) {
        runtimeSheetPath = resolveConfinedPath(moduleRoot, runtimeSheet.path);
        if (!runtimeSheetPath || !isPathWithin(moduleRoot, runtimeSheetPath)) {
          addError("invalid-runtime-font-sheet-path", "Runtime font sheet path escapes the module root.", paths.manifest);
        } else {
          runtimeSheetBytes = readRequired(runtimeSheetPath, "missing-runtime-font-atlas", "Packed runtime font atlas");
          if (runtimeSheetBytes && normalizeHash(runtimeSheet.sha256) !== sha256(runtimeSheetBytes)) addError("runtime-font-atlas-hash-mismatch", "Packed runtime font atlas SHA-256 does not match the manifest.", runtimeSheetPath);
          const runtimeDimensions = runtimeSheetBytes ? readPngDimensions(runtimeSheetBytes) : null;
          if (runtimeSheetBytes && !runtimeDimensions) addError("invalid-runtime-font-atlas", "Packed runtime font atlas is not a valid PNG.", runtimeSheetPath);
          if (runtimeDimensions && (runtimeDimensions.width !== Number(runtimeSheet.width) || runtimeDimensions.height !== Number(runtimeSheet.height))) addError("runtime-font-atlas-dimension-mismatch", "Packed runtime font atlas dimensions do not match the manifest.", runtimeSheetPath);
          if (spriteCategory?.sheet && (spriteCategory.sheet.width !== Number(runtimeSheet.width) || spriteCategory.sheet.height !== Number(runtimeSheet.height))) addError("runtime-font-category-sheet-mismatch", "SpriteData font-category sheet dimensions do not match the runtime manifest.", paths.spriteData);
        }
      }
    }
  }

  const authority = {
    alias: liveFontAlias,
    category: liveFontCategory,
    fnt: {
      path: toModuleRelative(paths.fnt, moduleRoot),
      sha256: fntBytes ? sha256(fntBytes) : "",
      page: fntPage,
      width: fntWidth,
      height: fntHeight,
      glyphCount: declaredGlyphCount >= 0 ? declaredGlyphCount : 0
    },
    atlas: {
      path: toModuleRelative(paths.atlas, moduleRoot),
      sha256: atlasBytes ? sha256(atlasBytes) : "",
      width: atlasDimensions?.width || 0,
      height: atlasDimensions?.height || 0
    },
    provenance: {
      path: toModuleRelative(paths.provenance, moduleRoot),
      sha256: provenanceBytes ? sha256(provenanceBytes) : ""
    },
    licenses: {
      sourcePath: sourceLicensePath ? toModuleRelative(sourceLicensePath, moduleRoot) : "",
      sourceSha256: sourceLicenseBytes ? sha256(sourceLicenseBytes) : "",
      packagedPath: toModuleRelative(paths.packagedLicense, moduleRoot),
      packagedSha256: packagedLicenseBytes ? sha256(packagedLicenseBytes) : ""
    },
    spriteRegistration: {
      spriteDataPath: toModuleRelative(paths.spriteData, moduleRoot),
      category: spriteCategory,
      part: spritePart,
      genericSprite
    },
    runtimeAtlas: {
      manifestPath: toModuleRelative(paths.manifest, moduleRoot),
      sourcePath: manifestPart?.sourcePath || "",
      sheetPath: runtimeSheetPath ? toModuleRelative(runtimeSheetPath, moduleRoot) : "",
      sheetSha256: runtimeSheetBytes ? sha256(runtimeSheetBytes) : ""
    }
  };
  const result = {
    schema: fontAssetAuditSchema,
    version: 1,
    ok: errors.length === 0,
    alias: liveFontAlias,
    category: liveFontCategory,
    assetFingerprintSha256: sha256(JSON.stringify(authority)),
    checkedFileCount: checkedFiles.length,
    checkedFiles: [...new Set(checkedFiles)].sort(naturalCompare),
    authority,
    errors
  };
  Object.defineProperty(result, "glyphIds", { value: glyphIds, enumerable: false });
  return result;
}

// Literal Text is frozen separately as fixedText so capitalization and fixed
// labels cannot drift. Only Text values whose trimmed XML value begins with @
// are classified as dynamic-bound and excluded from content comparison. These
// attributes remain the immutable typographic treatment and geometry.
export const trackedAttributes = Object.freeze([
  "Brush",
  "Brush.Font",
  "Brush.FontSize",
  "Brush.FontColor",
  "Brush.FontFace",
  "Brush.FontFamily",
  "Brush.FontName",
  "Brush.FontWeight",
  "Brush.FontStyle",
  "Brush.FontScale",
  "Brush.TextOutlineAmount",
  "Brush.TextGlowRadius",
  "Brush.TextBlur",
  "Brush.TextShadowAmount",
  "Brush.TextShadowOffset",
  "Brush.TextShadowColor",
  "Brush.TextHorizontalAlignment",
  "Brush.TextVerticalAlignment",
  "Brush.TextWrapMode",
  "Brush.TextTrimming",
  "Brush.CharacterSpacing",
  "Brush.LetterSpacing",
  "Brush.TextTracking",
  "Brush.LineSpacing",
  "FontFace",
  "FontFamily",
  "FontName",
  "FontWeight",
  "FontStyle",
  "FontScale",
  "TextHorizontalAlignment",
  "TextVerticalAlignment",
  "TextWrapMode",
  "TextTrimming",
  "MaxLines",
  "MinLines",
  "WidthSizePolicy",
  "HeightSizePolicy",
  "SuggestedWidth",
  "SuggestedHeight",
  "MinWidth",
  "MinHeight",
  "MaxWidth",
  "MaxHeight",
  "HorizontalAlignment",
  "VerticalAlignment",
  "PositionXOffset",
  "PositionYOffset",
  "MarginLeft",
  "MarginRight",
  "MarginTop",
  "MarginBottom",
  "AlphaFactor",
  "ColorFactor"
]);

export const requiredAttributes = Object.freeze([
  "Brush",
  "Brush.Font",
  "Brush.FontSize",
  "Brush.FontColor"
]);

export const requiredAttributeValues = Object.freeze({
  "Brush.Font": liveFontAlias
});

export const forbiddenSyntheticTextEffects = Object.freeze([
  "Brush.TextOutlineAmount",
  "Brush.TextGlowRadius",
  "Brush.TextBlur",
  "Brush.TextShadowAmount",
  "Brush.TextShadowOffset",
  "Brush.TextShadowColor"
]);

export function buildContract(options = {}) {
  const resolved = resolveOptions(options);
  const fontAssets = auditRuntimeFontAssets({ ...options, moduleRoot: resolved.moduleRoot });
  const catalogText = fs.readFileSync(resolved.catalogPath, "utf8");
  const paletteText = fs.readFileSync(resolved.palettePath, "utf8");
  const catalog = JSON.parse(catalogText);
  const palette = JSON.parse(paletteText);
  const paletteIndex = buildPaletteIndex(palette);
  const groups = groupCatalogPrefabs(catalog);
  const screens = [];
  const errors = [];

  for (const issue of fontAssets.errors) errors.push(`runtime font ${issue.code}: ${issue.message}`);

  for (const group of groups) {
    const prefabPath = path.join(resolved.prefabRoot, group.fileName);
    if (!fs.existsSync(prefabPath)) {
      errors.push(`${group.fileName}: cataloged prefab does not exist at ${prefabPath}.`);
      continue;
    }
    const xml = fs.readFileSync(prefabPath, "utf8");
    const widgets = extractTypographyWidgets(xml).map((widget) => freezeWidget(widget, paletteIndex, errors, group.fileName, false, fontAssets.glyphIds));
    const fixedLiteralTextCount = widgets.filter((widget) => widget.textMode === "fixed-literal").length;
    const dynamicBoundTextCount = widgets.filter((widget) => widget.textMode === "dynamic-bound").length;
    const prefab = toPosix(path.join("GUI", "Prefabs", group.fileName));
    const typographyAuthorityId = group.ids[0];
    screens.push({
      id: typographyAuthorityId,
      catalogIds: group.ids,
      catalogCoverage: group.ids.map((catalogId, index) => ({
        catalogId,
        mode: index === 0 ? "direct-prefab" : "shared-prefab-inheritance",
        typographyAuthorityId,
        prefab
      })),
      prefab,
      sourceSha256: sha256(xml),
      widgetCount: widgets.length,
      fixedLiteralTextCount,
      dynamicBoundTextCount,
      widgets
    });
  }

  if (errors.length) {
    const error = new Error(`Cannot freeze an invalid typography baseline:\n${errors.join("\n")}`);
    error.details = errors;
    throw error;
  }

  return {
    schema,
    version: 1,
    generatedUtc: new Date().toISOString(),
    authority: {
      rule: `Fixed shell typography is baked into approved artwork. Every remaining live TextWidget and EditableTextWidget must explicitly declare Brush.Font=${liveFontAlias}; runtime literal content is frozen while dynamic-bound Text values whose trimmed XML Text attribute begins with @ may vary. Per-widget brush, size, exact frozen color token, type treatment, alignment, and wrapping geometry may not change without an explicit contract review. Synthetic outline, glow, blur, and shadow effects must remain absent or inactive.`,
      catalogPath: toWorkspaceRelative(resolved.catalogPath, resolved.moduleRoot),
      catalogSha256: sha256(catalogText),
      palettePath: toWorkspaceRelative(resolved.palettePath, resolved.moduleRoot),
      paletteSha256: sha256(paletteText),
      paletteSchema: palette.schema || "",
      paletteVersion: palette.version || "",
      liveFontAssets: {
        ...fontAssets.authority,
        assetFingerprintSha256: fontAssets.assetFingerprintSha256
      }
    },
    proofScope: {
      xmlExactness: {
        status: "mechanically-enforced",
        proves: [
          "every catalog surface through either a direct prefab or declared shared-prefab inheritance",
          "per-widget brush identity",
          `explicit packaged-serif Brush.Font=${liveFontAlias} on every remaining live TextWidget and EditableTextWidget`,
          "FNT alias, page, glyph coverage, provenance, license, SpriteData registration, and packed runtime atlas resolution",
          "font-size hierarchy",
          "exact frozen color token",
          "declared face/weight/style/tracking attributes",
          "declared opacity and typographic position offsets",
          "absence of active synthetic outline, glow, blur, and shadow effects",
          "text alignment attributes",
          "width/height and margin geometry that controls wrapping",
          "remaining runtime literal Text content and capitalization"
        ],
        deliberatelyIgnores: ["dynamic-bound Text values whose trimmed XML Text attribute begins with @"]
      },
      renderedGlyphParity: {
        status: "not-proven-by-xml",
        reason: "Gauntlet may resolve brush resources, glyph metrics, baselines, hinting, weight, and rasterization differently from their XML declarations.",
        requiredEvidence: [
          "native 1920x1080 and 3440x1440 captures",
          "approved-reference text-region comparison where dynamic-text masks are available",
          "human acceptance of font face, weight, baseline, tracking, and hierarchy"
        ],
        renderedTextRegionSummaryPath: null
      }
    },
    expectedCatalogSurfaceCount: screens.reduce((sum, screen) => sum + screen.catalogIds.length, 0),
    expectedPrefabCount: screens.length,
    expectedWidgetCount: screens.reduce((sum, screen) => sum + screen.widgetCount, 0),
    expectedFixedLiteralTextCount: screens.reduce((sum, screen) => sum + screen.fixedLiteralTextCount, 0),
    expectedDynamicBoundTextCount: screens.reduce((sum, screen) => sum + screen.dynamicBoundTextCount, 0),
    trackedAttributes: [...trackedAttributes],
    requiredAttributes: [...requiredAttributes],
    requiredAttributeValues: { ...requiredAttributeValues },
    forbiddenSyntheticTextEffects: [...forbiddenSyntheticTextEffects],
    screens
  };
}

export function auditContract(options = {}) {
  const resolved = resolveOptions(options);
  const fontAssets = auditRuntimeFontAssets({ ...options, moduleRoot: resolved.moduleRoot });
  const contractText = options.contract
    ? `${JSON.stringify(options.contract)}\n`
    : fs.readFileSync(resolved.contractPath, "utf8");
  const contract = options.contract || JSON.parse(contractText);
  const catalogText = fs.readFileSync(resolved.catalogPath, "utf8");
  const paletteText = fs.readFileSync(resolved.palettePath, "utf8");
  const catalog = JSON.parse(catalogText);
  const palette = JSON.parse(paletteText);
  const paletteIndex = buildPaletteIndex(palette);
  const errors = [];
  const warnings = [];
  const screenReports = [];

  for (const issue of fontAssets.errors) errors.push(`runtime font ${issue.code}: ${issue.message}`);
  validateContractHeader(contract, errors);
  validateFontAssetAuthority(contract.authority?.liveFontAssets, fontAssets, errors);
  const currentCatalogGroups = groupCatalogPrefabs(catalog);
  const currentCatalogGroupByPrefab = new Map(currentCatalogGroups.map((group) => [group.fileName, group]));
  const currentPrefabNames = currentCatalogGroups.map((group) => group.fileName).sort(naturalCompare);
  const contractedPrefabNames = (contract.screens || []).map((screen) => path.basename(screen.prefab || "")).sort(naturalCompare);
  compareSets("cataloged prefab", contractedPrefabNames, currentPrefabNames, errors);

  const paletteSha256 = sha256(paletteText);
  if (contract.authority?.paletteSha256 !== paletteSha256) {
    errors.push(`Frozen palette SHA-256 mismatch: expected ${contract.authority?.paletteSha256 || "missing"}, actual ${paletteSha256}.`);
  }
  const catalogSha256 = sha256(catalogText);
  if (contract.authority?.catalogSha256 !== catalogSha256) {
    warnings.push(`UI catalog bytes changed (${contract.authority?.catalogSha256 || "missing"} -> ${catalogSha256}); prefab-set parity is checked separately.`);
  }

  for (const screen of contract.screens || []) {
    const fileName = path.basename(screen.prefab || "");
    const prefabPath = path.join(resolved.prefabRoot, fileName);
    const report = {
      id: screen.id || fileName,
      catalogIds: screen.catalogIds || [],
      catalogCoverage: screen.catalogCoverage || [],
      expectedCatalogIds: [...(screen.catalogIds || [])].sort(naturalCompare),
      actualCatalogIds: [...(currentCatalogGroupByPrefab.get(fileName)?.ids || [])].sort(naturalCompare),
      prefab: screen.prefab || "",
      expectedWidgets: Number(screen.widgetCount || 0),
      actualWidgets: 0,
      matchedWidgets: 0,
      expectedFixedLiteralTexts: Number(screen.fixedLiteralTextCount || 0),
      actualFixedLiteralTexts: 0,
      expectedDynamicBoundTexts: Number(screen.dynamicBoundTextCount || 0),
      actualDynamicBoundTexts: 0,
      sourceSha256Expected: screen.sourceSha256 || "",
      sourceSha256Actual: "",
      sourceBytesChanged: false,
      divergences: []
    };
    for (const catalogId of report.expectedCatalogIds) {
      if (!report.actualCatalogIds.includes(catalogId)) {
        report.divergences.push(divergence("missing-catalog-surface", report, null, "catalogIds", catalogId, catalogId, null));
      }
    }
    for (const catalogId of report.actualCatalogIds) {
      if (!report.expectedCatalogIds.includes(catalogId)) {
        report.divergences.push(divergence("unexpected-catalog-surface", report, null, "catalogIds", null, null, catalogId));
      }
    }
    if (!fileName || !fs.existsSync(prefabPath)) {
      report.divergences.push(divergence("missing-prefab", report, null, null, null, screen.prefab || fileName, null));
      screenReports.push(finalizeScreenReport(report));
      continue;
    }

    const xml = fs.readFileSync(prefabPath, "utf8");
    report.sourceSha256Actual = sha256(xml);
    report.sourceBytesChanged = report.sourceSha256Actual !== report.sourceSha256Expected;
    const actualWidgets = extractTypographyWidgets(xml).map((widget) => freezeWidget(widget, paletteIndex, report.divergences, fileName, true, fontAssets.glyphIds));
    report.actualWidgets = actualWidgets.length;
    report.actualFixedLiteralTexts = actualWidgets.filter((widget) => widget.textMode === "fixed-literal").length;
    report.actualDynamicBoundTexts = actualWidgets.filter((widget) => widget.textMode === "dynamic-bound").length;
    const expectedByKey = indexWidgets(screen.widgets || [], "contract", report.divergences, report);
    const actualByKey = indexWidgets(actualWidgets, "prefab", report.divergences, report);
    const keys = [...new Set([...expectedByKey.keys(), ...actualByKey.keys()])].sort(naturalCompare);

    for (const key of keys) {
      const expected = expectedByKey.get(key);
      const actual = actualByKey.get(key);
      if (!expected) {
        report.divergences.push(divergence("unexpected-widget", report, actual, null, null, null, summarizeWidget(actual)));
        continue;
      }
      if (!actual) {
        report.divergences.push(divergence("missing-widget", report, expected, null, null, summarizeWidget(expected), null));
        continue;
      }
      let matches = true;
      if (expected.kind !== actual.kind) {
        matches = false;
        report.divergences.push(divergence("widget-kind-mismatch", report, actual, "kind", expected.kind, expected.kind, actual.kind));
      }
      if (expected.textMode !== actual.textMode) {
        matches = false;
        report.divergences.push(divergence("text-mode-mismatch", report, actual, "textMode", expected.textMode, expected.textMode, actual.textMode));
      } else if (expected.textMode === "fixed-literal" && expected.fixedText !== actual.fixedText) {
        matches = false;
        report.divergences.push(divergence("fixed-text-mismatch", report, actual, "Text", expected.fixedText, expected.fixedText, actual.fixedText));
      }
      for (const attribute of contract.trackedAttributes || []) {
        const expectedValue = expected.attributes?.[attribute] ?? null;
        const actualValue = actual.attributes?.[attribute] ?? null;
        if (expectedValue === actualValue) continue;
        matches = false;
        report.divergences.push(divergence("attribute-mismatch", report, actual, attribute, expectedValue, expectedValue, actualValue));
      }
      if ((expected.colorToken ?? null) !== (actual.colorToken ?? null)) {
        matches = false;
        report.divergences.push(divergence("color-token-mismatch", report, actual, "colorToken", expected.colorToken ?? null, expected.colorToken ?? null, actual.colorToken ?? null));
      }
      if (matches) report.matchedWidgets += 1;
    }
    screenReports.push(finalizeScreenReport(report));
  }

  const divergenceCount = screenReports.reduce((sum, report) => sum + report.divergenceCount, 0);
  const expectedWidgetCount = screenReports.reduce((sum, report) => sum + report.expectedWidgets, 0);
  const actualWidgetCount = screenReports.reduce((sum, report) => sum + report.actualWidgets, 0);
  const expectedFixedLiteralTextCount = screenReports.reduce((sum, report) => sum + report.expectedFixedLiteralTexts, 0);
  const actualFixedLiteralTextCount = screenReports.reduce((sum, report) => sum + report.actualFixedLiteralTexts, 0);
  const expectedDynamicBoundTextCount = screenReports.reduce((sum, report) => sum + report.expectedDynamicBoundTexts, 0);
  const actualDynamicBoundTextCount = screenReports.reduce((sum, report) => sum + report.actualDynamicBoundTexts, 0);
  const expectedCatalogSurfaceCount = screenReports.reduce((sum, report) => sum + report.expectedCatalogIds.length, 0);
  const actualCatalogSurfaceCount = currentCatalogGroups.reduce((sum, group) => sum + group.ids.length, 0);
  if (contract.expectedCatalogSurfaceCount !== expectedCatalogSurfaceCount) {
    errors.push(`Contract expectedCatalogSurfaceCount ${contract.expectedCatalogSurfaceCount} does not match ${expectedCatalogSurfaceCount} declared catalog surfaces.`);
  }
  if (contract.expectedPrefabCount !== screenReports.length) {
    errors.push(`Contract expectedPrefabCount ${contract.expectedPrefabCount} does not match ${screenReports.length} screen records.`);
  }
  if (contract.expectedWidgetCount !== expectedWidgetCount) {
    errors.push(`Contract expectedWidgetCount ${contract.expectedWidgetCount} does not match ${expectedWidgetCount} frozen widgets.`);
  }
  if (contract.expectedFixedLiteralTextCount !== expectedFixedLiteralTextCount) {
    errors.push(`Contract expectedFixedLiteralTextCount ${contract.expectedFixedLiteralTextCount} does not match ${expectedFixedLiteralTextCount} frozen literal labels.`);
  }
  if (contract.expectedDynamicBoundTextCount !== expectedDynamicBoundTextCount) {
    errors.push(`Contract expectedDynamicBoundTextCount ${contract.expectedDynamicBoundTextCount} does not match ${expectedDynamicBoundTextCount} dynamic-bound widgets.`);
  }

  const sourceFingerprintSha256 = sha256(JSON.stringify({
    contractSha256: sha256(contractText),
    paletteSha256,
    fontAssetFingerprintSha256: fontAssets.assetFingerprintSha256,
    screens: screenReports.map((report) => ({ prefab: report.prefab, sourceSha256: report.sourceSha256Actual }))
  }));
  return {
    schema: auditSchema,
    version: 1,
    generatedUtc: new Date().toISOString(),
    ok: errors.length === 0 && divergenceCount === 0,
    contractPath: toWorkspaceRelative(resolved.contractPath, resolved.moduleRoot),
    contractSha256: sha256(contractText),
    catalogPath: toWorkspaceRelative(resolved.catalogPath, resolved.moduleRoot),
    palettePath: toWorkspaceRelative(resolved.palettePath, resolved.moduleRoot),
    fontAssets,
    sourceFingerprintSha256,
    proofScope: {
      xmlExactness: {
        status: errors.length === 0 && divergenceCount === 0 ? "passed" : "failed",
        proves: contract.proofScope?.xmlExactness?.proves || [],
        deliberatelyIgnores: contract.proofScope?.xmlExactness?.deliberatelyIgnores || ["dynamic-bound Text values whose trimmed XML Text attribute begins with @"]
      },
      renderedGlyphParity: contract.proofScope?.renderedGlyphParity || {
        status: "not-proven-by-xml",
        reason: "Rendered glyph parity requires native capture and human visual acceptance.",
        requiredEvidence: ["native capture", "human acceptance"],
        renderedTextRegionSummaryPath: null
      }
    },
    trackedAttributes: contract.trackedAttributes || [],
    requiredAttributes: contract.requiredAttributes || [],
    requiredAttributeValues: contract.requiredAttributeValues || {},
    forbiddenSyntheticTextEffects: contract.forbiddenSyntheticTextEffects || [],
    summary: {
      expectedPrefabs: contract.expectedPrefabCount || 0,
      auditedPrefabs: screenReports.length,
      expectedCatalogSurfaces: contract.expectedCatalogSurfaceCount || 0,
      auditedCatalogSurfaces: actualCatalogSurfaceCount,
      expectedWidgets: contract.expectedWidgetCount || 0,
      auditedWidgets: actualWidgetCount,
      matchedWidgets: screenReports.reduce((sum, report) => sum + report.matchedWidgets, 0),
      expectedFixedLiteralTexts: contract.expectedFixedLiteralTextCount || 0,
      auditedFixedLiteralTexts: actualFixedLiteralTextCount,
      expectedDynamicBoundTexts: contract.expectedDynamicBoundTextCount || 0,
      auditedDynamicBoundTexts: actualDynamicBoundTextCount,
      divergenceCount,
      fontAssetErrorCount: fontAssets.errors.length,
      errorCount: errors.length,
      warningCount: warnings.length
    },
    errors,
    warnings,
    screens: screenReports
  };
}

function freezeWidget(widget, paletteIndex, errors, fileName, auditMode = false, glyphIds = new Set()) {
  const attributes = {};
  for (const name of trackedAttributes) attributes[name] = normalizeAttribute(name, widget.attributes[name]);
  const text = classifyText(widget.attributes.Text);
  for (const name of requiredAttributes) {
    if (attributes[name] !== null) continue;
    const message = `${fileName}:${widget.line} ${widget.path} has no explicit ${name}.`;
    if (auditMode) {
      errors.push(divergence("missing-required-attribute", { id: fileName, prefab: fileName }, widget, name, null, "explicit value", null));
      if (name === "Brush.Font") errors.push(divergence("silent-font-fallback-risk", { id: fileName, prefab: fileName }, widget, name, liveFontAlias, liveFontAlias, null));
    } else {
      errors.push(message);
      if (name === "Brush.Font") errors.push(`${fileName}:${widget.line} ${widget.path} would silently fall back because Brush.Font is not '${liveFontAlias}'.`);
    }
  }
  for (const [name, expectedValue] of Object.entries(requiredAttributeValues)) {
    const actualValue = attributes[name];
    if (actualValue === null || actualValue === expectedValue) continue;
    if (auditMode) {
      errors.push(divergence("required-attribute-value-mismatch", { id: fileName, prefab: fileName }, widget, name, expectedValue, expectedValue, actualValue));
      if (name === "Brush.Font") errors.push(divergence("silent-font-fallback-risk", { id: fileName, prefab: fileName }, widget, name, expectedValue, expectedValue, actualValue));
    } else {
      errors.push(`${fileName}:${widget.line} ${widget.path} must declare ${name}=\"${expectedValue}\", found '${actualValue}'.`);
      if (name === "Brush.Font") errors.push(`${fileName}:${widget.line} ${widget.path} would silently fall back because Brush.Font is not '${liveFontAlias}'.`);
    }
  }
  for (const name of forbiddenSyntheticTextEffects) {
    const value = attributes[name];
    if (isInactiveSyntheticTextEffect(name, value)) continue;
    if (auditMode) {
      errors.push(divergence("forbidden-synthetic-text-effect", { id: fileName, prefab: fileName }, widget, name, null, "absent or inactive", value));
    } else {
      errors.push(`${fileName}:${widget.line} ${widget.path} uses active synthetic text effect ${name}='${value}'.`);
    }
  }
  const color = attributes["Brush.FontColor"];
  const colorToken = color ? paletteIndex.get(color) || null : null;
  if (color && !colorToken) {
    if (auditMode) errors.push(divergence("off-palette-color", { id: fileName, prefab: fileName }, widget, "Brush.FontColor", null, "frozen palette token", color));
    else errors.push(`${fileName}:${widget.line} ${widget.path} uses off-palette Brush.FontColor '${color}'.`);
  }
  if (text.mode === "fixed-literal" && glyphIds.size) {
    const missingGlyphs = [...new Set([...text.fixedText]
      .map((character) => character.codePointAt(0))
      .filter((codePoint) => ![9, 10, 13].includes(codePoint) && !glyphIds.has(codePoint)))];
    if (missingGlyphs.length) {
      const expected = `glyphs in ${liveFontAlias}`;
      const actual = missingGlyphs.join(", ");
      if (auditMode) errors.push(divergence("missing-live-literal-glyph", { id: fileName, prefab: fileName }, widget, "Text", null, expected, actual));
      else errors.push(`${fileName}:${widget.line} ${widget.path} contains literal code points absent from ${liveFontAlias}: ${actual}.`);
    }
  }
  return {
    key: widget.path,
    kind: widget.kind,
    widgetId: widget.widgetId,
    path: widget.path,
    sourceLine: widget.line,
    textMode: text.mode,
    fixedText: text.fixedText,
    colorToken,
    attributes
  };
}

export function extractTypographyWidgets(xml) {
  const widgets = [];
  const stack = [];
  const rootCounts = new Map();
  const lineStarts = buildLineStarts(xml);
  const tagPattern = /<(\/)?([A-Za-z_][A-Za-z0-9_.:-]*)([^<>]*?)(\/?)>/gs;
  for (const match of xml.matchAll(tagPattern)) {
    const closing = Boolean(match[1]);
    const tag = match[2];
    if (closing) {
      const frame = stack.pop();
      if (!frame || frame.tag !== tag) throw new Error(`Malformed XML near line ${lineAt(lineStarts, match.index)}: expected closing ${frame?.tag || "root"}, found ${tag}.`);
      continue;
    }
    const parent = stack.at(-1);
    const counts = parent ? parent.childCounts : rootCounts;
    const ordinal = (counts.get(tag) || 0) + 1;
    counts.set(tag, ordinal);
    const xmlPath = `${parent?.path || ""}/${tag}[${ordinal}]`;
    const attributes = parseAttributes(match[3] || "");
    if (tag === "TextWidget" || tag === "RichTextWidget" || tag === "EditableTextWidget") {
      widgets.push({
        key: xmlPath,
        kind: tag,
        widgetId: String(attributes.Id || ""),
        path: xmlPath,
        line: lineAt(lineStarts, match.index),
        attributes
      });
    }
    const selfClosing = Boolean(match[4]) || /\/\s*$/.test(match[3] || "");
    if (!selfClosing) stack.push({ tag, path: xmlPath, childCounts: new Map() });
  }
  if (stack.length) throw new Error(`Malformed XML: unclosed ${stack.at(-1).tag} at ${stack.at(-1).path}.`);
  return widgets;
}

function parseAttributes(source) {
  const attributes = {};
  const attributePattern = /([A-Za-z_][A-Za-z0-9_.:-]*)\s*=\s*(?:"([^"]*)"|'([^']*)')/gs;
  for (const match of source.matchAll(attributePattern)) attributes[match[1]] = match[2] ?? match[3] ?? "";
  return attributes;
}

function validateContractHeader(contract, errors) {
  if (contract.schema !== schema) errors.push(`Unsupported typography contract schema '${contract.schema || "missing"}'.`);
  if (contract.version !== 1) errors.push(`Unsupported typography contract version '${contract.version ?? "missing"}'.`);
  if (!Array.isArray(contract.screens) || !contract.screens.length) errors.push("Typography contract declares no screens.");
  if (!Array.isArray(contract.trackedAttributes)) errors.push("Typography contract has no trackedAttributes array.");
  if (!Array.isArray(contract.requiredAttributes)) errors.push("Typography contract has no requiredAttributes array.");
  if (!contract.requiredAttributeValues || typeof contract.requiredAttributeValues !== "object" || Array.isArray(contract.requiredAttributeValues)) errors.push("Typography contract has no requiredAttributeValues object.");
  if (!Array.isArray(contract.forbiddenSyntheticTextEffects)) errors.push("Typography contract has no forbiddenSyntheticTextEffects array.");
  if (!Number.isInteger(contract.expectedFixedLiteralTextCount) || contract.expectedFixedLiteralTextCount < 0) errors.push("Typography contract has no valid expectedFixedLiteralTextCount.");
  if (!Number.isInteger(contract.expectedDynamicBoundTextCount) || contract.expectedDynamicBoundTextCount < 0) errors.push("Typography contract has no valid expectedDynamicBoundTextCount.");
  compareAttributeSets("trackedAttributes", trackedAttributes, contract.trackedAttributes, errors);
  compareAttributeSets("requiredAttributes", requiredAttributes, contract.requiredAttributes, errors);
  compareRequiredAttributeValues(contract.requiredAttributeValues, errors);
  compareAttributeSets("forbiddenSyntheticTextEffects", forbiddenSyntheticTextEffects, contract.forbiddenSyntheticTextEffects, errors);
  for (const screen of contract.screens || []) validateCatalogCoverage(screen, errors);
}

function validateCatalogCoverage(screen, errors) {
  const prefix = screen.id || screen.prefab || "unnamed screen";
  if (!Array.isArray(screen.catalogIds) || !screen.catalogIds.length) {
    errors.push(`${prefix}: no catalogIds are declared.`);
    return;
  }
  if (!Array.isArray(screen.catalogCoverage)) {
    errors.push(`${prefix}: no catalogCoverage inheritance records are declared.`);
    return;
  }
  const catalogIds = [...new Set(screen.catalogIds)].sort(naturalCompare);
  const coveredIds = [...new Set(screen.catalogCoverage.map((entry) => entry?.catalogId).filter(Boolean))].sort(naturalCompare);
  if (catalogIds.length !== screen.catalogIds.length) errors.push(`${prefix}: catalogIds contains duplicates.`);
  if (!catalogIds.includes(screen.id)) errors.push(`${prefix}: catalogIds does not include its typography authority ID '${screen.id}'.`);
  if (coveredIds.length !== screen.catalogCoverage.length) errors.push(`${prefix}: catalogCoverage contains missing or duplicate catalog IDs.`);
  compareSets(`${prefix} catalog coverage`, catalogIds, coveredIds, errors);
  for (const entry of screen.catalogCoverage) {
    if (!entry || !["direct-prefab", "shared-prefab-inheritance"].includes(entry.mode)) {
      errors.push(`${prefix}: catalog coverage for '${entry?.catalogId || "missing"}' has unsupported mode '${entry?.mode || "missing"}'.`);
    }
    if (entry?.typographyAuthorityId !== screen.id) {
      errors.push(`${prefix}: catalog coverage for '${entry?.catalogId || "missing"}' has typographyAuthorityId '${entry?.typographyAuthorityId || "missing"}' instead of '${screen.id}'.`);
    }
    const expectedMode = entry?.catalogId === screen.id ? "direct-prefab" : "shared-prefab-inheritance";
    if (entry?.mode !== expectedMode) {
      errors.push(`${prefix}: catalog coverage for '${entry?.catalogId || "missing"}' must use mode '${expectedMode}'.`);
    }
    if (entry?.prefab !== screen.prefab) {
      errors.push(`${prefix}: catalog coverage for '${entry?.catalogId || "missing"}' points to '${entry?.prefab || "missing"}' instead of '${screen.prefab}'.`);
    }
  }
}

function compareAttributeSets(fieldName, expected, actual, errors) {
  if (!Array.isArray(actual)) return;
  const expectedSet = new Set(expected);
  const actualSet = new Set(actual);
  for (const name of expectedSet) {
    if (!actualSet.has(name)) errors.push(`Typography contract ${fieldName} is missing canonical field ${name}.`);
  }
  for (const name of actualSet) {
    if (!expectedSet.has(name)) errors.push(`Typography contract ${fieldName} contains unsupported field ${name} for schema version 1.`);
  }
  if (actual.length !== actualSet.size) errors.push(`Typography contract ${fieldName} contains duplicate fields.`);
}

function compareRequiredAttributeValues(actual, errors) {
  if (!actual || typeof actual !== "object" || Array.isArray(actual)) return;
  const expectedEntries = Object.entries(requiredAttributeValues).sort(([left], [right]) => naturalCompare(left, right));
  const actualEntries = Object.entries(actual).sort(([left], [right]) => naturalCompare(left, right));
  if (JSON.stringify(actualEntries) !== JSON.stringify(expectedEntries)) {
    errors.push(`Typography contract requiredAttributeValues must equal ${JSON.stringify(requiredAttributeValues)}.`);
  }
}

function validateFontAssetAuthority(expected, actualAudit, errors) {
  if (!expected || typeof expected !== "object" || Array.isArray(expected)) {
    errors.push("Typography contract has no frozen liveFontAssets authority.");
    return;
  }
  if (expected.alias !== liveFontAlias) errors.push(`Typography contract liveFontAssets.alias must be '${liveFontAlias}'.`);
  if (expected.category !== liveFontCategory) errors.push(`Typography contract liveFontAssets.category must be '${liveFontCategory}'.`);
  if (!expected.assetFingerprintSha256) errors.push("Typography contract liveFontAssets has no assetFingerprintSha256.");
  if (expected.assetFingerprintSha256 !== actualAudit.assetFingerprintSha256) {
    errors.push(`Frozen live-font asset fingerprint mismatch: expected ${expected.assetFingerprintSha256 || "missing"}, actual ${actualAudit.assetFingerprintSha256}.`);
  }
}

function resolveFontAssetPaths(moduleRoot, options) {
  return {
    fnt: path.resolve(options.fntPath || path.join(moduleRoot, "GUI", "Fonts", liveFontAlias, `${liveFontAlias}.fnt`)),
    atlas: path.resolve(options.fontAtlasPath || path.join(moduleRoot, "GUI", "SpriteParts", liveFontCategory, `${liveFontAlias}.png`)),
    provenance: path.resolve(options.fontProvenancePath || path.join(moduleRoot, "artwork", "ui-modern-style-kit", "font-sources", "provenance.json")),
    sourceLicense: path.resolve(options.sourceFontLicensePath || path.join(moduleRoot, "artwork", "ui-modern-style-kit", "font-sources", "Cormorant-v4.002-OFL.txt")),
    packagedLicense: path.resolve(options.packagedFontLicensePath || path.join(moduleRoot, "GUI", "Fonts", liveFontAlias, "OFL-Cormorant.txt")),
    spriteData: path.resolve(options.spriteDataPath || path.join(moduleRoot, "GUI", "ReignBetaSpriteData.xml")),
    manifest: path.resolve(options.runtimeSpriteManifestPath || path.join(moduleRoot, "GUI", "RuntimeSpriteSheets", "manifest.json"))
  };
}

function readPngDimensions(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length < 24) return null;
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  if (!bytes.subarray(0, 8).equals(signature) || bytes.toString("ascii", 12, 16) !== "IHDR") return null;
  const width = bytes.readUInt32BE(16);
  const height = bytes.readUInt32BE(20);
  return width > 0 && height > 0 ? { width, height } : null;
}

function positiveInteger(value) {
  const number = Number(value);
  return Number.isInteger(number) && number > 0 ? number : 0;
}

function nonNegativeInteger(value) {
  const number = Number(value);
  return Number.isInteger(number) && number >= 0 ? number : -1;
}

function normalizeHash(value) {
  return String(value || "").trim().toLowerCase();
}

function resolveConfinedPath(basePath, relativePath) {
  const value = String(relativePath || "").trim();
  if (!value) return "";
  return path.resolve(basePath, value.replaceAll("/", path.sep));
}

function isPathWithin(root, candidate) {
  const relative = path.relative(path.resolve(root), path.resolve(candidate));
  return Boolean(relative) && !relative.startsWith("..") && !path.isAbsolute(relative);
}

function findNamedXmlBlocks(xml, tagName, expectedName) {
  const blocks = [...String(xml).matchAll(new RegExp(`<${tagName}\\b[\\s\\S]*?<\\/${tagName}>`, "gi"))].map((match) => match[0]);
  return blocks.filter((block) => xmlElementText(block, "Name") === expectedName);
}

function xmlElementText(xml, tagName) {
  const match = String(xml).match(new RegExp(`<${tagName}\\b[^>]*>([\\s\\S]*?)<\\/${tagName}>`, "i"));
  return match ? match[1].trim() : "";
}

function parseSpriteSheetSize(categoryXml, expectedId) {
  for (const match of String(categoryXml).matchAll(/<SpriteSheetSize\b([^<>]*?)(?:\/>|>)/gis)) {
    const attributes = parseAttributes(match[1] || "");
    if (Number(attributes.ID) !== expectedId) continue;
    return { id: expectedId, width: positiveInteger(attributes.Width), height: positiveInteger(attributes.Height) };
  }
  return null;
}

function toModuleRelative(absolutePath, moduleRoot) {
  const relative = path.relative(moduleRoot, absolutePath);
  return toPosix(relative || path.basename(absolutePath));
}

function resolveOptions(options) {
  const moduleRoot = path.resolve(options.moduleRoot || defaultModuleRoot);
  return {
    moduleRoot,
    prefabRoot: path.resolve(options.prefabRoot || path.join(moduleRoot, "GUI", "Prefabs")),
    catalogPath: path.resolve(options.catalogPath || defaultCatalogPath),
    palettePath: path.resolve(options.palettePath || path.join(moduleRoot, "artwork", "ui-modern-style-kit", "palette.json")),
    contractPath: path.resolve(options.contractPath || defaultContractPath)
  };
}

function groupCatalogPrefabs(catalog) {
  const groups = new Map();
  for (const entry of catalog.interfaces || []) {
    const fileName = path.basename(String(entry.prefab || ""));
    if (!fileName) continue;
    if (!groups.has(fileName)) groups.set(fileName, []);
    groups.get(fileName).push(String(entry.id || fileName));
  }
  return [...groups.entries()]
    .map(([fileName, ids]) => ({ fileName, ids: [...new Set(ids)].sort(naturalCompare) }))
    .sort((left, right) => naturalCompare(left.fileName, right.fileName));
}

function buildPaletteIndex(palette) {
  const index = new Map();
  for (const [name, value] of Object.entries(palette.tokens || {})) {
    const color = normalizeColor(value);
    if (!color) continue;
    if (index.has(color)) throw new Error(`Frozen palette color ${color} is assigned to both ${index.get(color)} and ${name}.`);
    index.set(color, name);
  }
  return index;
}

function normalizeAttribute(name, value) {
  if (value === undefined || value === null) return null;
  const normalized = String(value).trim();
  return name === "Brush.FontColor" ? normalizeColor(normalized) : normalized;
}

function isInactiveSyntheticTextEffect(name, value) {
  if (value === null || value === undefined || String(value).trim() === "") return true;
  const normalized = String(value).trim();
  if (name.endsWith("Color")) return /^#[0-9A-Fa-f]{6}00$/.test(normalized);
  const numericParts = normalized.match(/-?(?:\d+(?:\.\d+)?|\.\d+)/g);
  return Boolean(numericParts?.length) && numericParts.every((part) => Number(part) === 0);
}

function classifyText(value) {
  if (value === undefined || value === null) return { mode: "missing", fixedText: null };
  const raw = decodeXmlAttributeEntities(String(value));
  return raw.trim().startsWith("@")
    ? { mode: "dynamic-bound", fixedText: null }
    : { mode: "fixed-literal", fixedText: raw };
}

function decodeXmlAttributeEntities(value) {
  return String(value).replace(/&#x([0-9a-f]+);|&#([0-9]+);|&(quot|apos|lt|gt|amp);/gi, (match, hex, decimal, named) => {
    if (hex || decimal) {
      const codePoint = Number.parseInt(hex || decimal, hex ? 16 : 10);
      return Number.isInteger(codePoint) && codePoint >= 0 && codePoint <= 0x10FFFF
        ? String.fromCodePoint(codePoint)
        : match;
    }
    return ({ quot: '"', apos: "'", lt: "<", gt: ">", amp: "&" })[String(named).toLowerCase()] || match;
  });
}

function normalizeColor(value) {
  const token = String(value || "").trim().toUpperCase();
  if (!token) return "";
  return token.startsWith("#") ? token : `#${token}`;
}

function indexWidgets(widgets, source, divergences, report) {
  const index = new Map();
  for (const widget of widgets) {
    if (!widget?.key) {
      divergences.push(divergence("missing-widget-key", report, widget, "key", null, "non-empty structural path", widget?.key ?? null));
      continue;
    }
    if (index.has(widget.key)) {
      divergences.push(divergence("duplicate-widget-key", report, widget, "key", null, `unique ${source} key`, widget.key));
      continue;
    }
    index.set(widget.key, widget);
  }
  return index;
}

function divergence(code, report, widget, attribute, expected, expectedValue, actualValue) {
  return {
    code,
    screenId: report.id || "",
    prefab: report.prefab || "",
    widgetKey: widget?.key || widget?.path || "",
    widgetId: widget?.widgetId || "",
    xmlPath: widget?.path || "",
    sourceLine: widget?.sourceLine || widget?.line || null,
    attribute,
    expected: expectedValue ?? expected ?? null,
    actual: actualValue ?? null
  };
}

function summarizeWidget(widget) {
  return widget ? { kind: widget.kind, widgetId: widget.widgetId, path: widget.path } : null;
}

function finalizeScreenReport(report) {
  return { ...report, ok: report.divergences.length === 0, divergenceCount: report.divergences.length };
}

function compareSets(label, expected, actual, errors) {
  const expectedSet = new Set(expected);
  const actualSet = new Set(actual);
  for (const item of expectedSet) if (!actualSet.has(item)) errors.push(`${label} '${item}' is required by the contract but absent from the current catalog.`);
  for (const item of actualSet) if (!expectedSet.has(item)) errors.push(`${label} '${item}' exists in the current catalog but is absent from the contract.`);
}

function buildLineStarts(text) {
  const starts = [0];
  for (let index = text.indexOf("\n"); index >= 0; index = text.indexOf("\n", index + 1)) starts.push(index + 1);
  return starts;
}

function lineAt(starts, offset) {
  let low = 0;
  let high = starts.length;
  while (low + 1 < high) {
    const middle = (low + high) >> 1;
    if (starts[middle] <= offset) low = middle;
    else high = middle;
  }
  return low + 1;
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function naturalCompare(left, right) {
  return String(left).localeCompare(String(right), "en", { numeric: true });
}

function toPosix(value) {
  return String(value).replaceAll("\\", "/");
}

function toWorkspaceRelative(absolutePath, moduleRoot) {
  const workspaceRoot = path.dirname(moduleRoot);
  const relative = path.relative(workspaceRoot, absolutePath);
  return toPosix(relative || path.basename(absolutePath));
}

function parseCli(argv) {
  const options = {};
  let outputPath = "";
  let writeContractPath = "";
  let confirmFreeze = false;
  let force = false;
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    const value = () => {
      const next = argv[++index];
      if (!next) throw new Error(`${argument} requires a value.`);
      return next;
    };
    if (argument === "--output") outputPath = path.resolve(value());
    else if (argument === "--contract") options.contractPath = path.resolve(value());
    else if (argument === "--catalog") options.catalogPath = path.resolve(value());
    else if (argument === "--palette") options.palettePath = path.resolve(value());
    else if (argument === "--prefab-root") options.prefabRoot = path.resolve(value());
    else if (argument === "--module-root") options.moduleRoot = path.resolve(value());
    else if (argument === "--write-contract") writeContractPath = path.resolve(value());
    else if (argument === "--confirm-freeze-current-typography") confirmFreeze = true;
    else if (argument === "--force") force = true;
    else throw new Error(`Unknown argument '${argument}'.`);
  }
  return { options, outputPath, writeContractPath, confirmFreeze, force };
}

async function main() {
  const cli = parseCli(process.argv.slice(2));
  if (cli.writeContractPath) {
    if (!cli.confirmFreeze) throw new Error("Writing a typography baseline requires --confirm-freeze-current-typography.");
    if (fs.existsSync(cli.writeContractPath) && !cli.force) throw new Error(`Refusing to replace existing contract without --force: ${cli.writeContractPath}`);
    const contract = buildContract(cli.options);
    fs.mkdirSync(path.dirname(cli.writeContractPath), { recursive: true });
    fs.writeFileSync(cli.writeContractPath, `${JSON.stringify(contract, null, 2)}\n`, "utf8");
    process.stdout.write(`${JSON.stringify({ schema: contract.schema, contractPath: cli.writeContractPath, expectedCatalogSurfaceCount: contract.expectedCatalogSurfaceCount, expectedPrefabCount: contract.expectedPrefabCount, expectedWidgetCount: contract.expectedWidgetCount, expectedFixedLiteralTextCount: contract.expectedFixedLiteralTextCount, expectedDynamicBoundTextCount: contract.expectedDynamicBoundTextCount }, null, 2)}\n`);
    return;
  }

  const result = auditContract(cli.options);
  if (cli.outputPath) {
    fs.mkdirSync(path.dirname(cli.outputPath), { recursive: true });
    fs.writeFileSync(cli.outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  }
  process.stdout.write(`${JSON.stringify({ schema: result.schema, ok: result.ok, summary: result.summary, errors: result.errors, warnings: result.warnings, outputPath: cli.outputPath || null }, null, 2)}\n`);
  if (!result.ok) process.exitCode = 1;
}

const isMain = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) main().catch((error) => {
  process.stderr.write(`${error.stack || error.message || error}\n`);
  process.exitCode = 1;
});
