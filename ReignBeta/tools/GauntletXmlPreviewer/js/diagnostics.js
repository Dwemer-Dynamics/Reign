import {
  isRegisteredAperturePlateSprite,
  isValidApertureCoveredPortraitOverscan,
  isValidIntentionalEventArtCrop
} from "./diagnostics-contract.js";

export function analyzeDiagnostics(renderer, stageContent) {
  const patchScoped = Boolean(renderer.documentNode?.querySelector?.("[ReignPreviewPatchClass]"));
  const isInScope = (element) => !patchScoped || hasPreviewPatchAncestor(element?.__gauntlet?.xmlNode);
  const findRenderedElement = (path) => renderer.renderedElements.find((candidate) => candidate.__gauntlet?.path === path);
  const issues = [...renderer.missingBindings, ...renderer.assetIssues]
    .filter((issue) => isInScope(findRenderedElement(issue.path)));
  const seen = new Set(issues.map((issue) => `${issue.kind}:${issue.binding || issue.path}`));
  const stageRect = stageContent.getBoundingClientRect();

  for (const element of renderer.renderedElements) {
    if (!element.isConnected || element.offsetWidth === 0 || element.offsetHeight === 0) continue;
    if (!isInScope(element)) continue;
    const metadata = element.__gauntlet;
    const rect = element.getBoundingClientRect();
    const parent = element.parentElement?.closest(".g-node") || stageContent;
    const parentRect = parent.getBoundingClientRect();
    const expectedScrollContent = isExpectedScrollOverflow(element, stageContent, rect);
    const intentionalPortraitCrop = isIntentionalPortraitCrop(element, stageContent)
      || isIntentionalApertureCoveredPortraitOverscan(element, stageContent);
    const intentionalEventArtCrop = isIntentionalEventArtCrop(element, stageContent);
    const intentionalCrop = intentionalPortraitCrop || intentionalEventArtCrop;
    const intentionalPositionOffset = numericData(element.dataset.positionXOffset) !== 0
      || numericData(element.dataset.positionYOffset) !== 0;
    const intentionalBleed = metadata?.sprite === "reign_court_throne_background"
      || isScrollbarHandle(element, parent)
      || isCalibrationHandle(element, parent)
      || isNativeNarrowAspectRootOverscan(element, parent, rect, parentRect)
      || isIntentionalDecorativeBleed(element, parent, rect, parentRect);

    if (isUnclippedFramedBanner(element, metadata, parent)) {
      addIssue(
        issues,
        seen,
        metadata,
        "fit",
        `Banner content is layered directly beneath ${parent.dataset.nodeLabel || "a decorative frame"} without a clipped aperture. Put the banner in a centered ClipContents viewport and keep the wreath/frame as the final overlay.`
      );
    }

    if (element.classList.contains("text") || element.classList.contains("editable")) {
      const intentionalEllipsis = getComputedStyle(element).textOverflow === "ellipsis";
      // Position*Offset moves Gauntlet text content without enlarging its layout
      // box. Measure the untransformed inner span so an intentional offset does
      // not masquerade as wrapped or clipped text.
      const measuredText = element.querySelector(":scope > .g-node-text-content") || element;
      const horizontal = !intentionalEllipsis && measuredText.scrollWidth > element.clientWidth + 2;
      const vertical = measuredText.scrollHeight > element.clientHeight + 2;
      if (horizontal || vertical) {
        addIssue(issues, seen, metadata, "overflow", `Text exceeds its ${horizontal && vertical ? "width and height" : horizontal ? "width" : "height"} (${element.scrollWidth}×${element.scrollHeight} content in ${element.clientWidth}×${element.clientHeight}).`);
      }
    }

    if (!intentionalBleed && !intentionalCrop && !intentionalPositionOffset && !expectedScrollContent && isOutside(rect, parentRect, 1.5)) {
      addIssue(
        issues,
        seen,
        metadata,
        "overflow",
        `Rendered bounds ${formatRect(rect)} extend beyond parent ${parent.classList?.contains("g-node") ? parent.dataset.nodeLabel : "viewport"} ${formatRect(parentRect)}.`
      );
    } else if (!intentionalBleed && !intentionalCrop && !expectedScrollContent && isOutside(rect, stageRect, 1.5) && !hasOutsideAncestor(element, stageContent, stageRect)) {
      addIssue(issues, seen, metadata, "overflow", "Rendered bounds extend beyond the test viewport.");
    }

    const clipAncestor = expectedScrollContent || intentionalCrop || intentionalBleed ? null : findClipAncestor(element, stageContent);
    if (clipAncestor) {
      const clipRect = clipAncestor.getBoundingClientRect();
      if (isOutside(rect, clipRect, 1.5) && !hasOutsideAncestor(element, clipAncestor, clipRect)) {
        addIssue(issues, seen, metadata, "clipping", `Clipped by ${clipAncestor.dataset.nodeLabel || "scroll/clip container"}.`);
      }
    }
  }

  return issues;
}

function numericData(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function formatRect(rect) {
  return `[x=${rect.x.toFixed(1)}, y=${rect.y.toFixed(1)}, w=${rect.width.toFixed(1)}, h=${rect.height.toFixed(1)}]`;
}

function hasPreviewPatchAncestor(xmlNode) {
  let current = xmlNode;
  while (current?.nodeType === Node.ELEMENT_NODE) {
    if (current.hasAttribute("ReignPreviewPatchClass")) return true;
    current = current.parentElement;
  }
  return false;
}

function isUnclippedFramedBanner(element, metadata, parent) {
  if (!["ImageIdentifierWidget", "MaskedTextureWidget"].includes(metadata?.tag) || !/banner/i.test(`${metadata.textureProvider || ""} ${metadata.additionalArgs || ""} ${metadata.id || ""}`)) return false;
  if (!parent?.classList?.contains("g-node") || parent.classList.contains("clip")) return false;
  const width = parent.offsetWidth;
  const height = parent.offsetHeight;
  const compactFrame = width >= 40 && height >= 40 && width <= 180 && height <= 180 && Math.abs(width - height) <= Math.max(width, height) * 0.3;
  const laterOverlay = Array.from(parent.children).some((sibling) => sibling !== element && sibling.__gauntlet?.sprite && sibling.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_PRECEDING);
  return compactFrame && laterOverlay && isOutside(element.getBoundingClientRect(), parent.getBoundingClientRect(), 1.5);
}

function isExpectedScrollOverflow(element, boundary, rect) {
  const scroll = element.parentElement?.closest(".scroll");
  if (!scroll || !boundary.contains(scroll)) return false;
  const clip = findClipAncestor(element, boundary);
  if (!clip || !scroll.contains(clip)) return false;
  const flow = element.classList.contains("flow") ? element : element.parentElement?.closest(".flow");
  if (!flow || !clip.contains(flow)) return false;
  const clipRect = clip.getBoundingClientRect();
  const axis = String(scroll.__gauntlet?.attributes?.MouseScrollAxis || "").toLowerCase();
  const horizontalInside = rect.left >= clipRect.left - 2 && rect.right <= clipRect.right + 2;
  const horizontalOutside = rect.left < clipRect.left - 1.5 || rect.right > clipRect.right + 1.5;
  const verticalInside = rect.top >= clipRect.top - 2 && rect.bottom <= clipRect.bottom + 2;
  const verticalOutside = rect.top < clipRect.top - 1.5 || rect.bottom > clipRect.bottom + 1.5;
  if (axis === "horizontal") return verticalInside && horizontalOutside;
  if (!axis && getComputedStyle(flow).flexDirection === "row") return verticalInside && horizontalOutside;
  return horizontalInside && verticalOutside;
}

function isIntentionalPortraitCrop(element, boundary) {
  const metadata = element.__gauntlet;
  const portraitLike = element.classList.contains("portrait")
    || /portrait|tableau/i.test(`${metadata?.tag || ""} ${metadata?.id || ""}`);
  if (!portraitLike) return false;
  const clip = findClipAncestor(element, boundary);
  if (!clip) return false;
  return /portrait/i.test(`${clip.dataset.elementId || ""} ${clip.dataset.nodeLabel || ""} ${metadata?.id || ""}`);
}

function isIntentionalApertureCoveredPortraitOverscan(element, boundary) {
  const clip = element.parentElement;
  if (!clip?.classList?.contains("g-node") || !clip.classList.contains("clip")) return false;
  if (findClipAncestor(element, boundary) !== clip) return false;
  const plateContainer = clip.parentElement;
  if (!plateContainer?.classList?.contains("g-node")) return false;

  const siblings = Array.from(plateContainer.children).filter((candidate) => candidate.classList?.contains("g-node"));
  const clipIndex = siblings.indexOf(clip);
  if (clipIndex < 0) return false;
  const laterSiblingElements = siblings.slice(clipIndex + 1);
  const laterSiblings = laterSiblingElements.map((candidate) => ({
    element: candidate,
    isRegisteredAperturePlate: candidate.__gauntlet?.tag === "ImageWidget"
      && isRegisteredAperturePlateSprite(candidate.__gauntlet?.sprite)
  }));
  const plates = laterSiblingElements
    .filter((candidate) => candidate.__gauntlet?.tag === "ImageWidget" && isRegisteredAperturePlateSprite(candidate.__gauntlet?.sprite))
    .map((candidate) => ({
      tag: candidate.__gauntlet.tag,
      sprite: candidate.__gauntlet.sprite,
      attributes: candidate.__gauntlet.attributes || {}
    }));
  const sourceRect = element.getBoundingClientRect();
  const clipRect = clip.getBoundingClientRect();

  return isValidApertureCoveredPortraitOverscan({
    sourceIsDirectClipChild: element.parentElement === clip,
    source: {
      tag: element.__gauntlet?.tag,
      attributes: element.__gauntlet?.attributes || {},
      rect: rectContract(sourceRect)
    },
    clip: {
      isClip: clip.classList.contains("clip"),
      attributes: clip.__gauntlet?.attributes || {},
      rect: rectContract(clipRect)
    },
    plates,
    laterSiblings
  });
}

function isIntentionalEventArtCrop(element, boundary) {
  const clip = element.parentElement;
  if (!clip?.classList?.contains("g-node") || !clip.classList.contains("clip")) return false;
  if (findClipAncestor(element, boundary) !== clip) return false;
  return isValidIntentionalEventArtCrop({
    sourceIsDirectClipChild: true,
    source: {
      tag: element.__gauntlet?.tag,
      attributes: element.__gauntlet?.attributes || {},
      rect: rectContract(element.getBoundingClientRect())
    },
    clip: {
      id: clip.dataset.elementId || "",
      isClip: true,
      attributes: clip.__gauntlet?.attributes || {},
      rect: rectContract(clip.getBoundingClientRect())
    }
  });
}

function rectContract(rect) {
  return {
    left: rect.left,
    top: rect.top,
    right: rect.right,
    bottom: rect.bottom
  };
}

function isScrollbarHandle(element, parent) {
  const metadata = element.__gauntlet;
  const parentMetadata = parent?.__gauntlet;
  return parentMetadata?.tag === "ScrollbarWidget"
    && (element.classList.contains("sprite-scroll-handle") || /handle/i.test(`${metadata?.id || ""} ${metadata?.brush || ""}`));
}

function isCalibrationHandle(element, parent) {
  if (parent?.dataset?.elementId === "ReignUiCalibrationSelection") return true;
  return Boolean(element.parentElement?.closest('[data-element-id="ReignUiCalibrationSelection"]'));
}

function isIntentionalDecorativeBleed(element, parent, rect, parentRect) {
  const metadata = element.__gauntlet;
  if (parent?.dataset?.elementId?.endsWith("BannerGroup") && /Frame$/.test(metadata?.id || "")) return true;
  if (!parent?.classList?.contains("button") || !element.classList.contains("sprite-decoration")) return false;
  const bleed = Math.max(
    Math.max(0, parentRect.left - rect.left),
    Math.max(0, parentRect.top - rect.top),
    Math.max(0, rect.right - parentRect.right),
    Math.max(0, rect.bottom - parentRect.bottom)
  );
  return bleed <= 12;
}

function isNativeNarrowAspectRootOverscan(element, parent, rect, parentRect) {
  const metadata = element.__gauntlet;
  if (metadata?.attributes?.DoNotUseCustomScaleAndChildren !== "true") return false;
  if (!parent?.classList?.contains("g-node") || parent.parentElement?.closest(".g-node")) return false;
  const horizontalBleed = Math.max(0, parentRect.left - rect.left, rect.right - parentRect.right);
  const verticalBleed = Math.max(0, parentRect.top - rect.top, rect.bottom - parentRect.bottom);
  // TaleWorlds' native narrow-aspect formula tolerates two percent of the
  // reference aspect before reducing scale. That produces at most roughly one
  // percent centered bleed per side for a fixed 16:9 root on 16:10 displays.
  return verticalBleed <= 1.5 && horizontalBleed <= parentRect.width * 0.011;
}

function addIssue(issues, seen, metadata, kind, message) {
  if (!metadata) return;
  const key = `${kind}:${metadata.selectionKey}`;
  if (seen.has(key)) return;
  seen.add(key);
  issues.push({
    kind,
    message,
    path: metadata.path,
    line: metadata.line,
    token: metadata.token,
    selectionKey: metadata.selectionKey,
    label: `${metadata.id ? `${metadata.tag}#${metadata.id}` : metadata.tag}${metadata.instanceTrail.length ? ` · ${metadata.instanceTrail.join("/")}` : ""}`
  });
}

function findClipAncestor(element, boundary) {
  let current = element.parentElement;
  while (current && current !== boundary) {
    if (current.classList.contains("clip") || current.classList.contains("scroll")) return current;
    current = current.parentElement;
  }
  return null;
}

function isOutside(rect, container, tolerance) {
  return rect.left < container.left - tolerance
    || rect.top < container.top - tolerance
    || rect.right > container.right + tolerance
    || rect.bottom > container.bottom + tolerance;
}

function hasOutsideAncestor(element, boundary, boundaryRect) {
  let current = element.parentElement;
  while (current && current !== boundary) {
    if (current.classList.contains("g-node") && isOutside(current.getBoundingClientRect(), boundaryRect, 1.5)) return true;
    current = current.parentElement;
  }
  return false;
}
