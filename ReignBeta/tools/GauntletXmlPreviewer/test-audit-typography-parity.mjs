import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { createHash } from "node:crypto";
import { buildContract, auditContract, liveFontAlias, liveFontCategory } from "./audit-typography-parity.mjs";

const outputIndex = process.argv.indexOf("--output");
const outputPath = outputIndex >= 0 && process.argv[outputIndex + 1]
  ? path.resolve(process.argv[outputIndex + 1])
  : "";
const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "reign-typography-parity-"));
const prefabRoot = path.join(tempRoot, "GUI", "Prefabs");
const catalogPath = path.join(tempRoot, "ui-catalog.json");
const palettePath = path.join(tempRoot, "palette.json");
const contractPath = path.join(tempRoot, "typography-contract.json");
const fntPath = path.join(tempRoot, "GUI", "Fonts", liveFontAlias, `${liveFontAlias}.fnt`);
const sourceAtlasPath = path.join(tempRoot, "GUI", "SpriteParts", liveFontCategory, `${liveFontAlias}.png`);
const packagedLicensePath = path.join(tempRoot, "GUI", "Fonts", liveFontAlias, "OFL-Cormorant.txt");
const provenanceRoot = path.join(tempRoot, "artwork", "ui-modern-style-kit", "font-sources");
const provenancePath = path.join(provenanceRoot, "provenance.json");
const sourceLicensePath = path.join(provenanceRoot, "Cormorant-v4.002-OFL.txt");
const spriteDataPath = path.join(tempRoot, "GUI", "ReignBetaSpriteData.xml");
const manifestPath = path.join(tempRoot, "GUI", "RuntimeSpriteSheets", "manifest.json");
const runtimeAtlasPath = path.join(tempRoot, "GUI", "RuntimeSpriteSheets", liveFontCategory, `${liveFontCategory}_1.png`);
const cases = [];
const catalogBaseline = { interfaces: [
  { id: "alpha", prefab: "GUI/Prefabs/Alpha.xml" },
  { id: "alpha-variant", prefab: "GUI/Prefabs/Alpha.xml" },
  { id: "beta", prefab: "GUI/Prefabs/Beta.xml" }
] };

const alphaBaseline = `<Prefab>
  <Window>
    <Widget>
      <Children>
        <TextWidget Id="Title" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="300" SuggestedHeight="40" PositionXOffset="1" AlphaFactor="0.9" Brush="DefaultText" Brush.Font="ReignSerifDynamic" Brush.FontWeight="Regular" Brush.FontSize="24" Brush.FontColor="#C5AC83FF" Brush.CharacterSpacing="0.02" Brush.TextHorizontalAlignment="Center" Text="@Title" />
        <EditableTextWidget WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed" SuggestedHeight="44" Brush="DefaultText" Brush.Font="ReignSerifDynamic" Brush.FontSize="16" Brush.FontColor="#C5BDAFFF" Brush.TextHorizontalAlignment="Left" Text="@InputText" />
      </Children>
    </Widget>
  </Window>
</Prefab>
`;
const betaBaseline = `<Prefab>
  <Window>
    <TextWidget WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed" SuggestedHeight="30" Brush="Info.Text" Brush.Font="ReignSerifDynamic" Brush.FontSize="14" Brush.FontColor="#8A8883FF" Brush.TextVerticalAlignment="Center" Text="STATUS" />
  </Window>
</Prefab>
`;

try {
  fs.mkdirSync(prefabRoot, { recursive: true });
  writeCatalog(catalogBaseline);
  fs.writeFileSync(palettePath, `${JSON.stringify({
    schema: "reign-ui-modern-palette-v1",
    version: "self-test",
    tokens: {
      goldBright: "#C5AC83FF",
      textPrimary: "#C5BDAFFF",
      textSecondary: "#8A8883FF"
    }
  }, null, 2)}\n`, "utf8");
  writePrefabs(alphaBaseline, betaBaseline);
  writeFontAssets();

  const contract = buildContract({ moduleRoot: tempRoot, prefabRoot, catalogPath, palettePath });
  fs.writeFileSync(contractPath, `${JSON.stringify(contract, null, 2)}\n`, "utf8");

  cases.push({
    id: "baseline-text-classification-counts",
    ok: contract.expectedFixedLiteralTextCount === 1 && contract.expectedDynamicBoundTextCount === 2,
    expectedFixedLiteralTexts: 1,
    actualFixedLiteralTexts: contract.expectedFixedLiteralTextCount,
    expectedDynamicBoundTexts: 2,
    actualDynamicBoundTexts: contract.expectedDynamicBoundTextCount
  });
  runCase("baseline-pass", alphaBaseline, true, []);
  runCase("fixed-literal-label-preserved", alphaBaseline, true, [], betaBaseline);
  runCase("dynamic-bound-value-exempt", alphaBaseline.replace('Text="@Title"', 'Text="@DifferentRuntimeTitle"'), true, []);
  runCase("dynamic-bound-to-literal-divergence", alphaBaseline.replace('Text="@Title"', 'Text="A DIFFERENT RUNTIME HEADING"'), false, ["text-mode-mismatch"]);
  runCase("fixed-literal-label-divergence", alphaBaseline, false, ["fixed-text-mismatch"], betaBaseline.replace('Text="STATUS"', 'Text="STATE"'));
  runCase("fixed-literal-capitalization-divergence", alphaBaseline, false, ["fixed-text-mismatch"], betaBaseline.replace('Text="STATUS"', 'Text="Status"'));
  runCase("fixed-literal-to-bound-divergence", alphaBaseline, false, ["text-mode-mismatch"], betaBaseline.replace('Text="STATUS"', 'Text="@RuntimeStatus"'));
  runCase("font-size-divergence", alphaBaseline.replace('Brush.FontSize="24"', 'Brush.FontSize="25"'), false, ["attribute-mismatch"]);
  runCase("missing-explicit-font-face-divergence", alphaBaseline.replace(' Brush.Font="ReignSerifDynamic"', ""), false, ["missing-required-attribute", "silent-font-fallback-risk"]);
  runCase("wrong-explicit-font-face-divergence", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="FallbackSans"'), false, ["required-attribute-value-mismatch", "silent-font-fallback-risk"]);
  runCase("font-face-divergence", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="FallbackSans"'), false, ["attribute-mismatch", "required-attribute-value-mismatch"]);
  runCase("font-weight-divergence", alphaBaseline.replace('Brush.FontWeight="Regular"', 'Brush.FontWeight="Bold"'), false, ["attribute-mismatch"]);
  runCase("synthetic-outline-divergence", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="ReignSerifDynamic" Brush.TextOutlineAmount="0.2"'), false, ["forbidden-synthetic-text-effect"]);
  runCase("synthetic-shadow-divergence", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="ReignSerifDynamic" Brush.TextShadowAmount="1"'), false, ["forbidden-synthetic-text-effect"]);
  runCase("tracking-divergence", alphaBaseline.replace('Brush.CharacterSpacing="0.02"', 'Brush.CharacterSpacing="0.04"'), false, ["attribute-mismatch"]);
  runCase("alignment-divergence", alphaBaseline.replace('Brush.TextHorizontalAlignment="Center"', 'Brush.TextHorizontalAlignment="Right"'), false, ["attribute-mismatch"]);
  runCase("wrapping-geometry-divergence", alphaBaseline.replace('SuggestedWidth="300"', 'SuggestedWidth="301"'), false, ["attribute-mismatch"]);
  runCase("position-divergence", alphaBaseline.replace('PositionXOffset="1"', 'PositionXOffset="2"'), false, ["attribute-mismatch"]);
  runCase("opacity-divergence", alphaBaseline.replace('AlphaFactor="0.9"', 'AlphaFactor="0.8"'), false, ["attribute-mismatch"]);
  runCase("off-palette-color-divergence", alphaBaseline.replace('#C5AC83FF', '#FFFFFFFF'), false, ["off-palette-color", "attribute-mismatch", "color-token-mismatch"]);
  runCase("brush-divergence", alphaBaseline.replace('Brush="DefaultText"', 'Brush="UnapprovedText"'), false, ["attribute-mismatch"]);
  runCase("unexpected-widget-divergence", alphaBaseline.replace("      </Children>", '        <TextWidget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" Brush="DefaultText" Brush.Font="ReignSerifDynamic" Brush.FontSize="12" Brush.FontColor="#C5BDAFFF" Text="NEW" />\n      </Children>'), false, ["unexpected-widget"]);
  runCase("missing-widget-divergence", alphaBaseline.replace(/\s*<EditableTextWidget[^>]+\/>\n/, "\n"), false, ["missing-widget"]);
  runBuildRejectionCase("freeze-rejects-missing-explicit-font-face", alphaBaseline.replace(' Brush.Font="ReignSerifDynamic"', ""), ["has no explicit Brush.Font", "would silently fall back"]);
  runBuildRejectionCase("freeze-rejects-wrong-explicit-font-face", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="FallbackSans"'), ['must declare Brush.Font="ReignSerifDynamic"', "would silently fall back"]);
  runBuildRejectionCase("freeze-rejects-active-synthetic-outline", alphaBaseline.replace('Brush.Font="ReignSerifDynamic"', 'Brush.Font="ReignSerifDynamic" Brush.TextOutlineAmount="0.2"'), ["uses active synthetic text effect Brush.TextOutlineAmount"]);
  runBuildRejectionCase("freeze-decodes-numeric-xml-entity-before-glyph-audit", alphaBaseline, ["9671"], betaBaseline.replace('Text="STATUS"', 'Text="&#9671;"'));
  runAssetCase("missing-font-definition-divergence", () => fs.unlinkSync(fntPath), ["missing-font-definition"]);
  runAssetCase("font-page-alias-divergence", () => fs.writeFileSync(fntPath, fs.readFileSync(fntPath, "utf8").replace('file="ReignSerifDynamic.png"', 'file="Galahad.png"'), "utf8"), ["font-page-alias-mismatch", "provenance-fnt-hash-mismatch"]);
  runAssetCase("missing-font-source-atlas-divergence", () => fs.unlinkSync(sourceAtlasPath), ["missing-font-atlas"]);
  runAssetCase("missing-runtime-font-atlas-divergence", () => fs.unlinkSync(runtimeAtlasPath), ["missing-runtime-font-atlas"]);
  runAssetCase("missing-font-provenance-divergence", () => fs.unlinkSync(provenancePath), ["missing-font-provenance"]);
  runAssetCase("missing-font-license-divergence", () => fs.unlinkSync(sourceLicensePath), ["missing-font-license"]);
  runAssetCase("missing-packaged-font-license-divergence", () => fs.unlinkSync(packagedLicensePath), ["missing-packaged-font-license"]);
  runAssetCase("missing-font-sprite-registration-divergence", () => fs.writeFileSync(spriteDataPath, fs.readFileSync(spriteDataPath, "utf8").replace(`<SpritePartName>${liveFontAlias}</SpritePartName>`, "<SpritePartName>MissingAlias</SpritePartName>"), "utf8"), ["font-generic-sprite-mismatch", "runtime-manifest-sprite-data-hash-mismatch"]);
  runAssetCase("missing-live-literal-glyph-divergence", () => removeGlyphFromFnt("83"), ["missing-live-literal-glyph"]);
  runCatalogCase("shared-catalog-inheritance-divergence", {
    interfaces: catalogBaseline.interfaces.filter((entry) => entry.id !== "alpha-variant")
  }, false, ["missing-catalog-surface"]);

  const failedCases = cases.filter((entry) => !entry.ok);
  const result = {
    schema: "reign-ui-typography-parity-self-test-v1",
    generatedUtc: new Date().toISOString(),
    ok: failedCases.length === 0,
    baseline: {
      expectedPrefabs: contract.expectedPrefabCount,
      expectedCatalogSurfaces: contract.expectedCatalogSurfaceCount,
      expectedWidgets: contract.expectedWidgetCount,
      expectedFixedLiteralTexts: contract.expectedFixedLiteralTextCount,
      expectedDynamicBoundTexts: contract.expectedDynamicBoundTextCount,
      trackedAttributeCount: contract.trackedAttributes.length
    },
    summary: { cases: cases.length, passed: cases.length - failedCases.length, failed: failedCases.length },
    cases
  };
  if (outputPath) {
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  }
  process.stdout.write(`${JSON.stringify({ schema: result.schema, ok: result.ok, summary: result.summary, outputPath: outputPath || null }, null, 2)}\n`);
  if (!result.ok) process.exitCode = 1;
} finally {
  fs.rmSync(tempRoot, { recursive: true, force: true });
}

function runCase(id, alphaXml, expectedOk, requiredCodes, betaXml = betaBaseline) {
  writeCatalog(catalogBaseline);
  writePrefabs(alphaXml, betaXml);
  writeFontAssets();
  const audit = auditContract({ moduleRoot: tempRoot, prefabRoot, catalogPath, palettePath, contractPath });
  const divergences = audit.screens.flatMap((screen) => screen.divergences);
  const codes = [...new Set(divergences.map((item) => item.code))].sort();
  const missingCodes = requiredCodes.filter((code) => !codes.includes(code));
  const ok = audit.ok === expectedOk && missingCodes.length === 0;
  cases.push({
    id,
    ok,
    expectedAuditOk: expectedOk,
    actualAuditOk: audit.ok,
    divergenceCount: audit.summary.divergenceCount,
    divergenceCodes: codes,
    sampleDivergences: divergences.slice(0, 5),
    missingExpectedCodes: missingCodes
  });
}

function runCatalogCase(id, catalog, expectedOk, requiredCodes) {
  writePrefabs(alphaBaseline, betaBaseline);
  writeCatalog(catalog);
  writeFontAssets();
  const audit = auditContract({ moduleRoot: tempRoot, prefabRoot, catalogPath, palettePath, contractPath });
  const divergences = audit.screens.flatMap((screen) => screen.divergences);
  const codes = [...new Set(divergences.map((item) => item.code))].sort();
  const missingCodes = requiredCodes.filter((code) => !codes.includes(code));
  const ok = audit.ok === expectedOk && missingCodes.length === 0;
  cases.push({
    id,
    ok,
    expectedAuditOk: expectedOk,
    actualAuditOk: audit.ok,
    divergenceCount: audit.summary.divergenceCount,
    divergenceCodes: codes,
    sampleDivergences: divergences.slice(0, 5),
    errors: audit.errors,
    warnings: audit.warnings,
    missingExpectedCodes: missingCodes
  });
}

function runBuildRejectionCase(id, alphaXml, requiredMessages, betaXml = betaBaseline) {
  writeCatalog(catalogBaseline);
  writePrefabs(alphaXml, betaXml);
  writeFontAssets();
  let errorText = "";
  try {
    buildContract({ moduleRoot: tempRoot, prefabRoot, catalogPath, palettePath });
  } catch (error) {
    errorText = String(error?.stack || error?.message || error);
  }
  const missingMessages = requiredMessages.filter((message) => !errorText.includes(message));
  cases.push({
    id,
    ok: Boolean(errorText) && missingMessages.length === 0,
    rejected: Boolean(errorText),
    requiredMessages,
    missingExpectedMessages: missingMessages
  });
}

function runAssetCase(id, mutate, requiredCodes) {
  writeCatalog(catalogBaseline);
  writePrefabs(alphaBaseline, betaBaseline);
  writeFontAssets();
  mutate();
  const audit = auditContract({ moduleRoot: tempRoot, prefabRoot, catalogPath, palettePath, contractPath });
  const divergences = audit.screens.flatMap((screen) => screen.divergences);
  const codes = [...new Set([
    ...audit.fontAssets.errors.map((issue) => issue.code),
    ...divergences.map((item) => item.code)
  ])].sort();
  const missingCodes = requiredCodes.filter((code) => !codes.includes(code));
  cases.push({
    id,
    ok: !audit.ok && missingCodes.length === 0,
    expectedAuditOk: false,
    actualAuditOk: audit.ok,
    fontAssetErrors: audit.fontAssets.errors,
    divergenceCodes: codes,
    missingExpectedCodes: missingCodes
  });
}

function writeFontAssets() {
  for (const directory of [path.dirname(fntPath), path.dirname(sourceAtlasPath), path.dirname(runtimeAtlasPath), provenanceRoot]) {
    fs.mkdirSync(directory, { recursive: true });
  }
  const pngBytes = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=", "base64");
  const glyphIds = [...new Set([
    ...Array.from({ length: 95 }, (_, index) => index + 32),
    160, 171, 187, 8211, 8212, 8216, 8217, 8220, 8221, 8226, 8230
  ])];
  const chars = glyphIds.map((id) => `    <char id="${id}" x="0" y="0" width="1" height="1" xadvance="1" xoffset="0" yoffset="0" page="0" chnl="0"/>`).join("\n");
  const fnt = `<?xml version="1.0"?>
<font>
  <info size="64" smooth="0" smoothingConstant="0.5" customScale="1"/>
  <common base="59" lineHeight="77" pages="1" scaleH="1" scaleW="1"/>
  <pages><page file="${liveFontAlias}.png" id="0"/></pages>
  <chars count="${glyphIds.length}">
${chars}
  </chars>
</font>
`;
  const license = "SIL OPEN FONT LICENSE Version 1.1\nSelf-test fixture only.\n";
  fs.writeFileSync(fntPath, fnt, "utf8");
  fs.writeFileSync(sourceAtlasPath, pngBytes);
  fs.writeFileSync(runtimeAtlasPath, pngBytes);
  fs.writeFileSync(sourceLicensePath, license, "utf8");
  fs.writeFileSync(packagedLicensePath, license, "utf8");

  const spriteData = `<SpriteData>
  <SpriteCategories>
    <SpriteCategory>
      <Name>${liveFontCategory}</Name>
      <AlwaysLoad/>
      <SpriteSheetCount>1</SpriteSheetCount>
      <SpriteSheetSize ID="1" Width="1" Height="1"/>
    </SpriteCategory>
  </SpriteCategories>
  <SpriteParts>
    <SpritePart>
      <SheetID>1</SheetID>
      <Name>${liveFontAlias}</Name>
      <Width>1</Width>
      <Height>1</Height>
      <SheetX>0</SheetX>
      <SheetY>0</SheetY>
      <CategoryName>${liveFontCategory}</CategoryName>
    </SpritePart>
  </SpriteParts>
  <Sprites>
    <GenericSprite>
      <Name>${liveFontAlias}</Name>
      <SpritePartName>${liveFontAlias}</SpritePartName>
    </GenericSprite>
  </Sprites>
</SpriteData>
`;
  fs.writeFileSync(spriteDataPath, spriteData, "utf8");
  const manifest = {
    version: 1,
    spriteDataPath: "GUI/ReignBetaSpriteData.xml",
    spriteDataSha256: sha256(Buffer.from(spriteData, "utf8")),
    categories: {
      [liveFontCategory]: {
        sheets: [{
          id: 1,
          width: 1,
          height: 1,
          path: `GUI/RuntimeSpriteSheets/${liveFontCategory}/${liveFontCategory}_1.png`,
          sha256: sha256(pngBytes)
        }],
        parts: {
          [liveFontAlias]: {
            sheetId: 1,
            x: 0,
            y: 0,
            width: 1,
            height: 1,
            sourcePath: `GUI/SpriteParts/${liveFontCategory}/${liveFontAlias}.png`,
            sourceSha256: sha256(pngBytes)
          }
        }
      }
    }
  };
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  const provenance = {
    schema: "reign-ui-font-provenance/v1",
    runtimeAlias: liveFontAlias,
    usage: "Dynamic-bound and remaining runtime-literal interface text only.",
    source: {
      family: "Cormorant Garamond",
      style: "Medium",
      weight: 500,
      version: "4.002",
      license: "SIL Open Font License 1.1",
      licenseFile: path.basename(sourceLicensePath),
      licenseSha256: sha256(Buffer.from(license, "utf8"))
    },
    generation: {
      format: "SDF",
      atlasWidth: 1,
      atlasHeight: 1,
      glyphCount: glyphIds.length
    },
    runtimeFiles: {
      fnt: `../../../GUI/Fonts/${liveFontAlias}/${liveFontAlias}.fnt`,
      fntSha256: sha256(Buffer.from(fnt, "utf8")),
      atlas: `../../../GUI/SpriteParts/${liveFontCategory}/${liveFontAlias}.png`,
      atlasSha256: sha256(pngBytes)
    }
  };
  fs.writeFileSync(provenancePath, `${JSON.stringify(provenance, null, 2)}\n`, "utf8");
}

function removeGlyphFromFnt(id) {
  const text = fs.readFileSync(fntPath, "utf8");
  const updated = text
    .replace(new RegExp(`\\s*<char id="${id}"[^>]*\\/>`), "")
    .replace(/<chars count="(\d+)">/, (_, count) => `<chars count="${Number(count) - 1}">`);
  fs.writeFileSync(fntPath, updated, "utf8");
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function writeCatalog(catalog) {
  fs.writeFileSync(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`, "utf8");
}

function writePrefabs(alphaXml, betaXml) {
  fs.writeFileSync(path.join(prefabRoot, "Alpha.xml"), alphaXml, "utf8");
  fs.writeFileSync(path.join(prefabRoot, "Beta.xml"), betaXml, "utf8");
}
