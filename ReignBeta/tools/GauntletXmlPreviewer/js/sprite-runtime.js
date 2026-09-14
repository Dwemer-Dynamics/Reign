const MANIFEST_URL = "../../GUI/RuntimeSpriteSheets/manifest.json";

export async function loadRuntimeSpriteState() {
  const state = {
    ready: false,
    manifestUrl: absoluteUrl(MANIFEST_URL),
    parts: new Map(),
    sprites: new Map(),
    problems: [],
    statusMessage: "Runtime sprite validation has not completed."
  };

  try {
    const manifest = await fetchJson(MANIFEST_URL);
    const spriteDataPath = manifest.spriteDataPath || "GUI/ReignBetaSpriteData.xml";
    const spriteDataUrl = moduleUrl(spriteDataPath);
    const spriteDataResult = await verifyFile(spriteDataUrl, manifest.spriteDataSha256);
    state.sprites = await loadSpriteDefinitions(spriteDataUrl);
    if (!spriteDataResult.ready) {
      state.problems.push(`SpriteData ${spriteDataResult.message}`);
    }

    for (const [categoryName, category] of Object.entries(manifest.categories || {})) {
      const sheetResults = new Map();
      await Promise.all((category.sheets || []).map(async (sheet) => {
        const result = await verifyFile(moduleUrl(sheet.path), sheet.sha256);
        sheetResults.set(Number(sheet.id), result);
        if (!result.ready) state.problems.push(`${categoryName} sheet ${sheet.id} ${result.message}`);
      }));

      await Promise.all(Object.entries(category.parts || {}).map(async ([spriteName, part]) => {
        const sourceUrl = moduleUrl(part.sourcePath);
        const sourceResult = await verifyFile(sourceUrl, part.sourceSha256);
        const sheetResult = sheetResults.get(Number(part.sheetId)) || { ready: false, message: "is not declared" };
        const reasons = [];
        if (!spriteDataResult.ready) reasons.push(`SpriteData ${spriteDataResult.message}`);
        if (!sourceResult.ready) reasons.push(`source PNG ${sourceResult.message}`);
        if (!sheetResult.ready) reasons.push(`runtime sheet ${sheetResult.message}`);

        const ready = reasons.length === 0;
        state.parts.set(spriteName, {
          ready,
          category: categoryName,
          sheetId: Number(part.sheetId),
          sourceUrl,
          message: ready
            ? `${spriteName} is synchronized with ${categoryName} sheet ${part.sheetId}.`
            : `${spriteName} is not game-ready: ${reasons.join("; ")}. Run build-runtime-sprite-sheets.ps1.`
        });
        if (!sourceResult.ready) state.problems.push(`${spriteName} source PNG ${sourceResult.message}`);
      }));
    }

    state.ready = spriteDataResult.ready
      && state.parts.size > 0
      && Array.from(state.parts.values()).every((part) => part.ready);
    state.statusMessage = state.ready
      ? `${state.parts.size} Reign sprite parts match their generated game sheets.`
      : `${state.problems.length} runtime sprite contract problem${state.problems.length === 1 ? "" : "s"} detected.`;
  } catch (error) {
    state.statusMessage = `Runtime sprite manifest could not be validated: ${error.message}`;
    state.problems.push(state.statusMessage);
  }

  return state;
}

async function loadSpriteDefinitions(url) {
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText} (${url})`);
  const documentNode = new DOMParser().parseFromString(await response.text(), "application/xml");
  const parseError = documentNode.querySelector("parsererror");
  if (parseError) throw new Error(`SpriteData XML parse error: ${parseError.textContent.trim()}`);

  const definitions = new Map();
  documentNode.querySelectorAll("Sprites > GenericSprite, Sprites > NineRegionSprite").forEach((node) => {
    const name = node.querySelector(":scope > Name")?.textContent?.trim();
    const partName = node.querySelector(":scope > SpritePartName")?.textContent?.trim();
    if (!name || !partName) return;
    const definition = { name, partName, nineRegion: null };
    if (node.nodeName === "NineRegionSprite") {
      definition.nineRegion = {
        left: numberFromChild(node, "LeftWidth"),
        right: numberFromChild(node, "RightWidth"),
        top: numberFromChild(node, "TopHeight"),
        bottom: numberFromChild(node, "BottomHeight")
      };
    }
    definitions.set(name, definition);
  });
  return definitions;
}

function numberFromChild(node, name) {
  return Number(node.querySelector(`:scope > ${name}`)?.textContent) || 0;
}

async function fetchJson(url) {
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText} (${absoluteUrl(url)})`);
  return response.json();
}

async function verifyFile(url, expectedHash) {
  try {
    const response = await fetch(url, { cache: "no-store" });
    if (!response.ok) return { ready: false, message: `is missing (${response.status})` };
    const bytes = await response.arrayBuffer();
    const actualHash = await sha256(bytes);
    if (!expectedHash) return { ready: false, message: "has no manifest hash" };
    if (actualHash.toLowerCase() !== String(expectedHash).toLowerCase()) return { ready: false, message: "is stale (SHA-256 mismatch)" };
    return { ready: true, message: "matches the manifest" };
  } catch (error) {
    return { ready: false, message: `could not be read (${error.message})` };
  }
}

async function sha256(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}

function moduleUrl(path) {
  return absoluteUrl(`../../${String(path || "").replace(/^\/+/, "")}`);
}

function absoluteUrl(path) {
  return new URL(path, document.baseURI).href;
}
