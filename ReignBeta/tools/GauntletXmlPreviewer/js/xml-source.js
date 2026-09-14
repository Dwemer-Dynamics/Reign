const ELEMENT_NODE = 1;

export function childElements(node) {
  if (!node) return [];
  return Array.from(node.childNodes).filter((child) => child.nodeType === ELEMENT_NODE);
}

export function firstChildElement(node, name) {
  return childElements(node).find((child) => child.nodeName === name) || null;
}

export function createSourceIndex(xmlText, documentNode) {
  const openings = scanOpeningTags(xmlText);
  const metadata = new WeakMap();
  const elements = [];
  if (documentNode.documentElement) collectElements(documentNode.documentElement, elements);

  let openingCursor = 0;
  for (const element of elements) {
    while (openingCursor < openings.length && openings[openingCursor].tag !== element.nodeName) {
      openingCursor += 1;
    }
    const source = openings[openingCursor] || { line: null, column: null, offset: null };
    if (openingCursor < openings.length) openingCursor += 1;
    metadata.set(element, {
      path: buildXmlPath(element),
      line: source.line,
      column: source.column,
      offset: source.offset
    });
  }

  return metadata;
}

export function buildXmlPath(element) {
  const segments = [];
  let current = element;
  while (current && current.nodeType === ELEMENT_NODE) {
    const siblings = current.parentNode ? childElements(current.parentNode).filter((node) => node.nodeName === current.nodeName) : [current];
    const index = Math.max(1, siblings.indexOf(current) + 1);
    segments.unshift(`${current.nodeName}[${index}]`);
    current = current.parentNode;
  }
  return `/${segments.join("/")}`;
}

export function attributesToObject(element) {
  const result = {};
  for (const attribute of Array.from(element.attributes || [])) result[attribute.name] = attribute.value;
  return result;
}

export function extractBindingNames(value) {
  if (typeof value !== "string") return [];
  const names = [];
  const pattern = /(?:@|\{)([A-Za-z_][A-Za-z0-9_]*)(?:\})?/g;
  for (const match of value.matchAll(pattern)) names.push(match[1]);
  return [...new Set(names)];
}

export function readPrefabConstants(documentNode) {
  const constants = {};
  const container = documentNode.querySelector("Constants");
  if (!container) return constants;
  for (const node of childElements(container)) {
    const name = node.getAttribute("Name") || node.nodeName;
    const raw = node.hasAttribute("ReignPreviewResolvedValue")
      ? node.getAttribute("ReignPreviewResolvedValue")
      : node.getAttribute("Value");
    if (!name || raw == null) continue;
    const number = Number(raw);
    constants[name] = Number.isFinite(number) && raw.trim() !== "" ? number : raw;
  }
  return constants;
}

function collectElements(element, output) {
  output.push(element);
  for (const child of childElements(element)) collectElements(child, output);
}

function scanOpeningTags(xmlText) {
  const openings = [];
  const pattern = /<\s*([A-Za-z_][A-Za-z0-9_.:-]*)(?=\s|\/?>)/g;
  for (const match of xmlText.matchAll(pattern)) {
    const offset = match.index ?? 0;
    const before = xmlText.slice(0, offset);
    const lines = before.split(/\r\n|\r|\n/);
    openings.push({
      tag: match[1],
      offset,
      line: lines.length,
      column: (lines.at(-1)?.length || 0) + 1
    });
  }
  return openings;
}
