export async function loadNativeAugmentation(entry) {
  const url = new URL("api/native-prefab", document.baseURI);
  url.searchParams.set("target", entry.target);
  const response = await fetch(url, { cache: "no-store" });
  const base = await response.json();
  if (!response.ok || base.ok === false) throw new Error(base.error || `${response.status} ${response.statusText}`);

  const patches = [];
  for (const source of entry.sources || []) {
    const sourceUrl = new URL(`../../../${source}`, import.meta.url);
    const sourceResponse = await fetch(sourceUrl, { cache: "no-store" });
    if (!sourceResponse.ok) throw new Error(`Could not load ${source}: ${sourceResponse.status} ${sourceResponse.statusText}`);
    patches.push(...discoverPrefabExtensions(await sourceResponse.text(), source).filter((patch) => patch.target === entry.target));
  }
  if (!patches.length) throw new Error(`No current PrefabExtension source targets ${entry.target}.`);

  return {
    xml: composeNativePrefab(base.xml, patches, base.resolvedConstants || {}),
    baseSha256: base.sha256,
    basePrefab: base.basePrefab,
    target: entry.target,
    patches
  };
}

export function discoverPrefabExtensions(sourceText, sourcePath = "") {
  const patches = [];
  const classPattern = /((?:\s*\[PrefabExtension\([^\r\n]+\)\]\s*)+)(?:internal|public)[^{;\r\n]*\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/g;
  for (const classMatch of sourceText.matchAll(classPattern)) {
    const opening = sourceText.indexOf("{", classMatch.index + classMatch[0].length - 1);
    const closing = findClosing(sourceText, opening, "{", "}");
    if (opening < 0 || closing < 0) continue;
    const body = sourceText.slice(opening + 1, closing);
    const type = body.match(/InsertType\s+Type\s*=>\s*InsertType\.([A-Za-z_][A-Za-z0-9_]*)/)?.[1]
      || (body.includes("PrefabExtensionSetAttributePatch") ? "SetAttribute" : "");
    const index = Number(body.match(/\bint\s+Index\s*=>\s*(-?\d+)/)?.[1] || (type === "Child" ? 0 : 0));
    const attributes = [...body.matchAll(/new\s+Attribute\("((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\)/g)]
      .map((match) => ({ name: decodeCSharpString(match[1]), value: decodeCSharpString(match[2]) }));
    const contentXml = extractPatchXml(body);
    for (const attribute of classMatch[1].matchAll(/\[PrefabExtension\("((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\)\]/g)) {
      patches.push({
        target: decodeCSharpString(attribute[1]),
        xpath: decodeCSharpString(attribute[2]),
        patchClass: classMatch[2],
        source: sourcePath,
        line: sourceText.slice(0, classMatch.index + attribute.index).split("\n").length,
        type,
        index,
        attributes,
        contentXml
      });
    }
  }
  return patches;
}

export function composeNativePrefab(baseXml, patches, resolvedConstants = {}) {
  const parser = new DOMParser();
  const documentNode = parser.parseFromString(baseXml, "application/xml");
  const parseError = documentNode.querySelector("parsererror");
  if (parseError) throw new Error(`Native base prefab is not valid XML: ${parseError.textContent.trim()}`);
  applyResolvedConstants(documentNode, resolvedConstants);

  const applied = [];
  for (const patch of patches) {
    const targets = evaluateXPath(documentNode, patch.xpath);
    if (!targets.length) throw new Error(`${patch.patchClass} XPath did not match ${patch.target}: ${patch.xpath}`);
    for (const target of targets) {
      const node = applyPatch(documentNode, target, patch, parser);
      if (node) applied.push({ node, patch });
    }
  }
  annotateAppliedNodes(applied);
  return new XMLSerializer().serializeToString(documentNode);
}

function applyResolvedConstants(documentNode, resolvedConstants) {
  for (const constant of documentNode.querySelectorAll("Constants > Constant[Name]")) {
    const name = constant.getAttribute("Name");
    if (name && Object.prototype.hasOwnProperty.call(resolvedConstants, name)) {
      constant.setAttribute("ReignPreviewResolvedValue", String(resolvedConstants[name]));
    }
  }
}

function applyPatch(documentNode, target, patch, parser) {
  if (patch.attributes?.length) {
    for (const attribute of patch.attributes) target.setAttribute(attribute.name, attribute.value);
    return target;
  }
  if (!patch.contentXml) throw new Error(`${patch.patchClass} has no source-discoverable XML content.`);
  const fragmentDocument = parser.parseFromString(patch.contentXml, "application/xml");
  const fragmentError = fragmentDocument.querySelector("parsererror");
  if (fragmentError || !fragmentDocument.documentElement) throw new Error(`${patch.patchClass} contains invalid patch XML.`);
  const replacement = documentNode.importNode(fragmentDocument.documentElement, true);

  if (patch.type === "Replace" || patch.type === "ReplaceKeepChildren") {
    if (patch.type === "ReplaceKeepChildren") {
      const oldChildren = directChild(target, "Children");
      if (oldChildren) {
        let newChildren = directChild(replacement, "Children");
        if (!newChildren) {
          newChildren = documentNode.createElement("Children");
          replacement.appendChild(newChildren);
        }
        while (oldChildren.firstChild) newChildren.appendChild(oldChildren.firstChild);
      }
    }
    target.parentNode?.replaceChild(replacement, target);
    return replacement;
  }

  const container = target.nodeName === "Children" ? target : directChild(target, "Children") || target;
  const elementChildren = [...container.childNodes].filter((node) => node.nodeType === Node.ELEMENT_NODE);
  const insertionIndex = Math.max(0, Math.min(elementChildren.length, Number.isFinite(patch.index) ? patch.index : elementChildren.length));
  const before = elementChildren[insertionIndex] || null;
  container.insertBefore(replacement, before);
  return replacement;
}

function annotateAppliedNodes(applied) {
  for (const entry of applied) {
    appendPreviewMetadata(entry.node, "ReignPreviewPatchClass", entry.patch.patchClass);
    appendPreviewMetadata(entry.node, "ReignPreviewSource", `${entry.patch.source}:${entry.patch.line}`);
    appendPreviewMetadata(entry.node, "ReignPreviewXPath", entry.patch.xpath);
  }
}

function appendPreviewMetadata(node, name, value) {
  const current = node.getAttribute(name);
  const values = current ? current.split(" | ") : [];
  if (!values.includes(value)) values.push(value);
  node.setAttribute(name, values.join(" | "));
}

function evaluateXPath(documentNode, xpath) {
  const result = documentNode.evaluate(xpath, documentNode, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
  const nodes = [];
  for (let index = 0; index < result.snapshotLength; index += 1) nodes.push(result.snapshotItem(index));
  return nodes;
}

function directChild(node, name) {
  return [...node.childNodes].find((child) => child.nodeType === Node.ELEMENT_NODE && child.nodeName === name) || null;
}

function extractPatchXml(body) {
  const constants = extractConstStrings(body);
  const callPattern = /(?:\bLoadXml|\bReignPrefabXml\.Load)\s*\(/g;
  let selected = "";
  for (const match of body.matchAll(callPattern)) {
    const opening = body.indexOf("(", match.index);
    const closing = findClosing(body, opening, "(", ")");
    if (closing < 0) continue;
    const argument = body.slice(opening + 1, closing).trim();
    const constantName = argument.match(/^([A-Za-z_][A-Za-z0-9_]*)$/)?.[1] || "";
    const decoded = constantName && constants.has(constantName)
      ? constants.get(constantName)
      : [...argument.matchAll(/"((?:\\.|[^"\\])*)"/g)].map((literal) => decodeCSharpString(literal[1])).join("");
    if (decoded.trim().startsWith("<")) selected = decoded;
  }
  return selected;
}

function extractConstStrings(body) {
  const constants = new Map();
  const declarationPattern = /\bconst\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=/g;
  for (const declaration of body.matchAll(declarationPattern)) {
    const expressionStart = declaration.index + declaration[0].length;
    const expressionEnd = findStatementTerminator(body, expressionStart);
    if (expressionEnd < 0) continue;
    const expression = body.slice(expressionStart, expressionEnd);
    const decoded = [...expression.matchAll(/"((?:\\.|[^"\\])*)"/g)]
      .map((literal) => decodeCSharpString(literal[1]))
      .join("");
    if (decoded) constants.set(declaration[1], decoded);
  }
  return constants;
}

function findStatementTerminator(text, start) {
  let quote = "";
  let escaped = false;
  for (let index = start; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") {
      quote = character;
      continue;
    }
    if (character === ";") return index;
  }
  return -1;
}

function decodeCSharpString(value) {
  let result = "";
  for (let index = 0; index < value.length; index += 1) {
    const character = value[index];
    if (character !== "\\" || index + 1 >= value.length) {
      result += character;
      continue;
    }
    const escaped = value[++index];
    if (escaped === "n") result += "\n";
    else if (escaped === "r") result += "\r";
    else if (escaped === "t") result += "\t";
    else if (escaped === "0") result += "\0";
    else result += escaped;
  }
  return result;
}

function findClosing(text, opening, openCharacter, closeCharacter) {
  if (opening < 0) return -1;
  let depth = 0;
  let quote = "";
  let escaped = false;
  let lineComment = false;
  let blockComment = false;
  for (let index = opening; index < text.length; index += 1) {
    const character = text[index];
    const next = text[index + 1] || "";
    if (lineComment) { if (character === "\n") lineComment = false; continue; }
    if (blockComment) { if (character === "*" && next === "/") { blockComment = false; index += 1; } continue; }
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === "/" && next === "/") { lineComment = true; index += 1; continue; }
    if (character === "/" && next === "*") { blockComment = true; index += 1; continue; }
    if (character === '"' || character === "'") { quote = character; continue; }
    if (character === openCharacter) depth += 1;
    if (character === closeCharacter && --depth === 0) return index;
  }
  return -1;
}
