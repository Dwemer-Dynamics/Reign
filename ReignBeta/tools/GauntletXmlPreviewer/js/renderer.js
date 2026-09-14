import { appendActionRichText, previewActionRichText } from "./action-text.js";
import {
  attributesToObject,
  childElements,
  createSourceIndex,
  extractBindingNames,
  firstChildElement,
  readPrefabConstants
} from "./xml-source.js";

const STRUCTURAL_TAGS = new Set(["Prefab", "Window", "Children", "ItemTemplate", "Constants", "Constant"]);
const REFERENCE_EVENT_BUTTON_VARIANTS = new Map([
  ["WAIT APPROACH", "reference-event-button--wait-approach"],
  ["NEXT PHASE", "reference-event-button--next-phase"],
  ["END EVENT", "reference-event-button--end-event"]
]);
const FONT_FAMILY_BY_GAUNTLET_FONT = new Map([
  ["reignserifdynamic", '"Reign Cormorant Garamond", serif'],
  ["firasansextracondensed-light", '"Reign Fira Sans Extra Condensed", "Arial Narrow", sans-serif'],
  ["firasansextracondensed-medium", '"Reign Fira Sans Extra Condensed", "Arial Narrow", sans-serif'],
  ["firasansextracondensed-regular", '"Reign Fira Sans Extra Condensed", "Arial Narrow", sans-serif'],
  ["galahad", 'Georgia, "Times New Roman", serif']
]);
const FONT_WEIGHT_BY_GAUNTLET_FONT = new Map([
  ["reignserifdynamic", "500"],
  ["firasansextracondensed-light", "300"],
  ["firasansextracondensed-medium", "500"],
  ["firasansextracondensed-regular", "400"],
  ["galahad", "400"]
]);
const BRUSH_TYPOGRAPHY = new Map([
  ["PreAlpha.Text", {
    family: '"Reign Fira Sans Extra Condensed", "Arial Narrow", sans-serif',
    weight: "400",
    lineHeight: "1.18",
    textShadow: "none"
  }],
  ["Info.Text", {
    family: 'Georgia, "Times New Roman", serif',
    weight: "400",
    lineHeight: "1.08",
    textShadow: "none"
  }]
]);

function ensurePreviewBgrToRgbFilter() {
  if (document.getElementById("reign-preview-bgr-to-rgb")) return;
  const namespace = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(namespace, "svg");
  svg.setAttribute("aria-hidden", "true");
  svg.style.position = "absolute";
  svg.style.width = "0";
  svg.style.height = "0";
  const definitions = document.createElementNS(namespace, "defs");
  const filter = document.createElementNS(namespace, "filter");
  filter.id = "reign-preview-bgr-to-rgb";
  filter.setAttribute("color-interpolation-filters", "sRGB");
  const matrix = document.createElementNS(namespace, "feColorMatrix");
  matrix.setAttribute("type", "matrix");
  matrix.setAttribute("values", "0 0 1 0 0  0 1 0 0 0  1 0 0 0 0  0 0 0 1 0");
  filter.appendChild(matrix);
  definitions.appendChild(filter);
  svg.appendChild(definitions);
  document.body.appendChild(svg);
}

export class GauntletRenderer {
  constructor(stageContent, assetByImageId = {}, assetByWidgetId = {}) {
    this.stageContent = stageContent;
    this.assetByImageId = assetByImageId;
    this.assetByWidgetId = assetByWidgetId;
    this.runtimeSpriteState = null;
    this.runtimeSnapshot = null;
    this.resetState();
  }

  setRuntimeSpriteState(state) {
    this.runtimeSpriteState = state;
  }

  setRuntimeSnapshot(snapshot) {
    this.runtimeSnapshot = snapshot?.schema === "reign-ui-runtime-snapshot-v1" && Array.isArray(snapshot.widgets)
      ? snapshot
      : null;
  }

  resetState() {
    this.documentNode = null;
    this.sourceIndex = new WeakMap();
    this.fileName = "Loaded XML";
    this.constants = {};
    this.rootData = {};
    this.renderedElements = [];
    this.elementBySelectionKey = new Map();
    this.elementByToken = new Map();
    this.missingBindings = [];
    this.assetIssues = [];
    this.assetIssueSprites = new Set();
  }

  render(xmlText, fileName, sampleData) {
    this.resetState();
    this.fileName = fileName || "Loaded XML";
    const parser = new DOMParser();
    const documentNode = parser.parseFromString(xmlText, "application/xml");
    const parseError = documentNode.querySelector("parsererror");
    if (parseError) throw new Error(cleanParseError(parseError.textContent));

    this.documentNode = documentNode;
    this.sourceIndex = createSourceIndex(xmlText, documentNode);
    const constants = readPrefabConstants(documentNode);
    this.constants = constants;
    this.rootData = { ...sampleData };
    this.stageContent.replaceChildren();
    const context = this.makeContext(this.rootData, [], "", false, null);
    const root = documentNode.querySelector("Window") || documentNode.documentElement;
    const rootInfo = {
      flow: false,
      horizontal: false,
      parentWidth: this.stageContent.clientWidth,
      parentHeight: this.stageContent.clientHeight
    };

    if (root.nodeName === "Window") {
      const childrenContainer = firstChildElement(root, "Children");
      const roots = childrenContainer ? childElements(childrenContainer) : childElements(root);
      roots.forEach((child) => this.renderElement(child, this.stageContent, context, rootInfo));
    } else if (root.nodeName === "Prefab") {
      childElements(root)
        .filter((child) => !STRUCTURAL_TAGS.has(child.nodeName))
        .forEach((child) => this.renderElement(child, this.stageContent, context, rootInfo));
    } else {
      this.renderElement(root, this.stageContent, context, rootInfo);
    }

    this.hydrateBindingIssues();
    return {
      documentNode,
      constants,
      nodeCount: this.renderedElements.length,
      missingBindings: this.missingBindings,
      assetIssues: this.assetIssues
    };
  }

  renderElement(xmlNode, parentElement, context, parentInfo) {
    if (!xmlNode || xmlNode.nodeType !== 1) return null;
    if (xmlNode.nodeName === "Children" || xmlNode.nodeName === "ItemTemplate") {
      childElements(xmlNode).forEach((child) => this.renderElement(child, parentElement, context, parentInfo));
      return null;
    }
    if (STRUCTURAL_TAGS.has(xmlNode.nodeName)) return null;

    const tag = xmlNode.nodeName;
    const dataSourceExpression = xmlNode.getAttribute("DataSource") || "";
    const dataSourceName = getDataSourceName(dataSourceExpression);
    const scopedValue = tag === "ListPanel" || !dataSourceExpression
      ? null
      : this.resolveValue(dataSourceExpression, context);
    const scopedContext = scopedValue && typeof scopedValue === "object" && !Array.isArray(scopedValue)
      ? this.makeContext(scopedValue, context.$trail || [], dataSourceName, true, context)
      : context;
    const id = xmlNode.getAttribute("Id") || "";
    const source = this.sourceIndex.get(xmlNode) || { path: `/${tag}[1]`, line: null, column: null };
    const instanceTrail = scopedContext.$trail || [];
    const runtimeRow = this.findRuntimeWidgetRow({ id, path: source.path, instanceTrail });
    const fixtureVisible = this.isVisible(xmlNode, scopedContext);
    const resolvedVisible = runtimeRow && typeof runtimeRow.isVisible === "boolean" ? runtimeRow.isVisible : fixtureVisible;
    if (!resolvedVisible) return null;

    const isAttendeeCard = tag === "Widget"
      && xmlNode.parentElement?.nodeName === "ItemTemplate"
      && ["ActiveParticipants", "AvailableAttendees"].includes(scopedContext.$dataSource);
    const directXmlChildren = firstChildElement(xmlNode, "Children");
    const directXmlChildNodes = directXmlChildren ? childElements(directXmlChildren) : [];
    const isEventChatPanel = tag === "Widget"
      && directXmlChildNodes.some((child) => child.nodeName.includes("AutoScrollPanel") && child.getAttribute("InnerPanel")?.includes("EventChatClip"))
      && directXmlChildNodes.some((child) => child.getAttribute("Id") === "EventChatScrollbar");
    const isEventPhaseBanner = tag === "Widget"
      && directXmlChildNodes.some((child) => child.nodeName === "TextWidget" && child.getAttribute("Text") === "@PhaseTitle")
      && directXmlChildNodes.some((child) => child.nodeName === "TextWidget" && child.getAttribute("Text") === "@PhaseDescription");
    const isEventHeaderBanner = tag === "Widget"
      && directXmlChildNodes.some((child) => child.nodeName === "TextWidget" && child.getAttribute("Text") === "@EventTitle")
      && directXmlChildNodes.some((child) => child.nodeName === "TextWidget" && child.getAttribute("Text") === "@PhaseStatus");
    const isEventAttendeesPanel = tag === "Widget"
      && directXmlChildNodes.some((child) => child.nodeName === "TextWidget" && child.getAttribute("Text") === "ATTENDEES")
      && directXmlChildNodes.some((child) => child.getAttribute("Id") === "EventAttendeeScrollbar");
    const isIndividualChatShell = tag === "Widget"
      && directXmlChildNodes.some((child) => child.getAttribute("Id") === "ReignIndividualChatBackground")
      && directXmlChildNodes.some((child) => child.getAttribute("Id") === "ReignChatLogPanelBackground")
      && directXmlChildNodes.some((child) => child.getAttribute("Id") === "ReignChatScrollbar");
    const isEventInterfaceBackground = tag === "Widget"
      && (xmlNode.getAttribute("Sprite") === "reign_social_event_background"
        || (xmlNode.getAttribute("Sprite") === "BlankWhiteSquare_9"
          && xmlNode.getAttribute("Color")?.toUpperCase() === "#020201E8"))
      && xmlNode.parentElement?.nodeName === "Children"
      && xmlNode.parentElement?.parentElement?.nodeName === "Widget"
      && xmlNode.parentElement?.parentElement?.parentElement?.nodeName === "Window";
    const element = document.createElement("div");
    const brush = this.resolveText(xmlNode.getAttribute("Brush"), scopedContext) || "";
    const sprite = this.resolveText(xmlNode.getAttribute("Sprite"), scopedContext) || "";
    const attributes = attributesToObject(xmlNode);
    const boundProperties = this.resolveBoundProperties(attributes, scopedContext, context);
    const selectionKey = `${source.path}::${instanceTrail.join("/")}`;
    const token = makeSelectionToken(this.fileName, source, instanceTrail, tag, id);

    element.className = "g-node";
    element.dataset.nodeLabel = id ? `${tag}#${id}` : tag;
    element.dataset.elementType = tag;
    if (id) element.dataset.elementId = id;
    if (brush) element.dataset.brush = brush;
    if (sprite) element.dataset.sprite = sprite;
    const textureProvider = this.resolveText(xmlNode.getAttribute("TextureProviderName"), scopedContext);
    const additionalArgs = this.resolveText(xmlNode.getAttribute("AdditionalArgs"), scopedContext);
    if (textureProvider) element.dataset.textureProvider = textureProvider;
    if (additionalArgs) element.dataset.additionalArgs = additionalArgs;
    if (xmlNode.getAttribute("DoNotAcceptEvents") === "true") element.dataset.doNotAcceptEvents = "true";
    element.dataset.selectionKey = selectionKey;

    const isList = tag === "ListPanel";
    const isText = tag === "TextWidget" || tag === "RichTextWidget";
    const isEditable = tag === "EditableTextWidget";
    const isButton = tag === "ButtonWidget";
    const buttonChildren = isButton ? firstChildElement(xmlNode, "Children") : null;
    const buttonTextNode = buttonChildren ? firstChildElement(buttonChildren, "TextWidget") : null;
    const referenceEventButtonVariant = REFERENCE_EVENT_BUTTON_VARIANTS.get(buttonTextNode?.getAttribute("Text")) || "";
    const isEventArt = tag === "ReignEventArtWidget";
    const isAspectMaskedPortrait = tag === "ReignAspectMaskedTextureWidget";
    const isIdentifierImage = tag === "ImageIdentifierWidget" || tag === "MaskedTextureWidget" || isAspectMaskedPortrait;
    const isImage = tag === "ImageWidget" || isIdentifierImage || isEventArt || tag === "ReignWarCouncilMapWidget";
    const isPortrait = tag.includes("PortraitWidget")
      || (isIdentifierImage && /character|portrait/i.test(`${textureProvider || ""} ${id} ${this.resolveText(xmlNode.getAttribute("ImageId"), scopedContext) || ""}`));
    const isScroll = tag === "ScrollablePanel" || tag.endsWith("ScrollPanel");
    if (isList) element.classList.add("flow");
    if (isText) element.classList.add("text");
    if (isEditable) element.classList.add("editable");
    if (isButton) element.classList.add("button");
    if (referenceEventButtonVariant) element.classList.add("reference-event-button", referenceEventButtonVariant);
    if (isImage || tag.includes("PortraitWidget")) element.classList.add(isPortrait ? "portrait" : "image");
    if (isEventArt) element.classList.add("event-art");
    if (isScroll) element.classList.add("scroll");
    if (xmlNode.getAttribute("ClipContents") === "true") element.classList.add("clip");
    if (isAttendeeCard) {
      element.classList.add("attendee-card", scopedContext.$dataSource === "ActiveParticipants" ? "attendee-card--active" : "attendee-card--available");
    }
    if (isEventChatPanel) element.classList.add("event-chat-panel");
    if (isEventPhaseBanner) element.classList.add("event-phase-banner");
    if (isEventHeaderBanner) element.classList.add("event-header-banner");
    if (isEventAttendeesPanel) element.classList.add("event-attendees-panel");
    if (isIndividualChatShell) element.classList.add("individual-chat-shell");
    if (isEventInterfaceBackground) element.classList.add("event-interface-background");
    if (tag === "MaskedTextureWidget" || isAspectMaskedPortrait) element.classList.add("masked-texture");
    if (isAspectMaskedPortrait) element.classList.add("aspect-masked-portrait");

    parentElement.appendChild(element);
    this.applyNodeStyle(element, xmlNode, parentInfo, scopedContext);

    if (isText || isEditable) {
      const rawText = xmlNode.getAttribute("Text") || "";
      let renderedText = this.resolveText(rawText, scopedContext);
      const textIsBound = rawText.startsWith("@") || rawText.includes("{");
      if (textIsBound && runtimeRow && typeof runtimeRow.text === "string") renderedText = runtimeRow.text;
      const positionXOffset = runtimeRow && Number.isFinite(Number(runtimeRow.positionXOffset))
        ? Number(runtimeRow.positionXOffset)
        : numeric(this.resolveText(xmlNode.getAttribute("PositionXOffset"), scopedContext));
      const positionYOffset = runtimeRow && Number.isFinite(Number(runtimeRow.positionYOffset))
        ? Number(runtimeRow.positionYOffset)
        : numeric(this.resolveText(xmlNode.getAttribute("PositionYOffset"), scopedContext));
      if (positionXOffset || positionYOffset) {
        const textContent = document.createElement("span");
        textContent.className = "g-node-text-content";
        textContent.textContent = renderedText;
        textContent.style.transform = `translate(${positionXOffset}px, ${positionYOffset}px)`;
        element.replaceChildren(textContent);
      } else {
        element.textContent = renderedText;
      }
      if (tag === "RichTextWidget") {
        appendActionRichText(element, renderedText);
        element.style.display = "block";
      }
      this.applyTextStyle(element, xmlNode, scopedContext);
      if (runtimeRow && Number(runtimeRow.fontSize) > 0) element.style.fontSize = `${Number(runtimeRow.fontSize)}px`;
      if (runtimeRow?.brush) element.dataset.runtimeBrush = runtimeRow.brush;
    }

    if (isImage || tag.includes("PortraitWidget")) {
      const label = this.resolveText(
        xmlNode.getAttribute("ImageId") || xmlNode.getAttribute("EventImageId") || xmlNode.getAttribute("PortraitCacheKey"),
        scopedContext
      ) || id || tag;
      element.dataset.label = label;
      const asset = this.resolveImageAsset(xmlNode, scopedContext, label);
      if (asset) {
        element.classList.add("has-asset");
        element.style.backgroundImage = cssUrl(asset);
        if (isPortrait) {
          element.classList.add(`portrait-presentation--${classifyPortraitPresentation({
            id,
            width: numeric(this.resolveText(xmlNode.getAttribute("SuggestedWidth"), scopedContext)),
            height: numeric(this.resolveText(xmlNode.getAttribute("SuggestedHeight"), scopedContext)),
            useFullBody: xmlNode.getAttribute("UseFullBody") === "true"
          })}`);
        }
        if (id === "WarCouncilMap") {
          ensurePreviewBgrToRgbFilter();
          element.style.filter = "url(#reign-preview-bgr-to-rgb)";
        }
      } else if (/banner/i.test(label)) {
        element.classList.add("has-asset");
        element.style.backgroundImage = "conic-gradient(from 45deg, #7b211c, #d5ad4e, #151008, #7b211c)";
      }
    }

    const metadata = {
      xmlNode,
      element,
      tag,
      id,
      dataSource: xmlNode.getAttribute("DataSource") || scopedContext.$dataSource || "",
      brush,
      sprite,
      textureProvider,
      additionalArgs,
      source,
      path: source.path,
      line: source.line,
      column: source.column,
      token,
      selectionKey,
      instanceTrail: [...instanceTrail],
      attributes,
      boundProperties,
      elementContext: scopedContext
    };
    element.__gauntlet = metadata;
    this.renderedElements.push(element);
    this.elementBySelectionKey.set(selectionKey, element);
    this.elementByToken.set(token, element);
    this.recordMissingBindings(metadata);
    this.collectAssetIssue(metadata);

    const children = firstChildElement(xmlNode, "Children");
    const itemTemplate = firstChildElement(xmlNode, "ItemTemplate");
    const nextParentInfo = {
      flow: isList,
      horizontal: getLayoutDirection(xmlNode) === "horizontal",
      parentWidth: element.clientWidth || numeric(element.style.width),
      parentHeight: element.clientHeight || numeric(element.style.height)
    };

    if (isList && itemTemplate) {
      const sourceName = getDataSourceName(xmlNode.getAttribute("DataSource"));
      const items = this.resolveValue(xmlNode.getAttribute("DataSource"), context);
      if (Array.isArray(items)) {
        const runtimeCount = this.getRuntimeListItemCount(source.path, instanceTrail);
        const renderedItems = runtimeCount == null ? items : items.slice(0, runtimeCount);
        renderedItems.forEach((item, index) => {
          const itemTrail = [...instanceTrail, `${sourceName || "Item"}[${index + 1}]`];
          const itemContext = this.makeContext(item, itemTrail, sourceName, true, context);
          childElements(itemTemplate).forEach((child) => this.renderElement(child, element, itemContext, nextParentInfo));
        });
      }
    } else if (children) {
      childElements(children).forEach((child) => this.renderElement(child, element, scopedContext, nextParentInfo));
    }

    if (xmlNode.getAttribute("HeightSizePolicy") === "CoverChildren") this.fitCoverChildren(element, xmlNode, "height");
    if (xmlNode.getAttribute("WidthSizePolicy") === "CoverChildren") this.fitCoverChildren(element, xmlNode, "width");
    if (isAttendeeCard) decorateAttendeeCard(element);
    if (isEventChatPanel) decorateEventChatPanel(element);
    if (isEventPhaseBanner) decorateEventPhaseBanner(element);
    if (isEventHeaderBanner) decorateEventHeaderBanner(element);
    if (isEventAttendeesPanel) decorateEventAttendeesPanel(element);
    if (isIndividualChatShell) decorateIndividualChatShell(element, this.rootData);
    if (referenceEventButtonVariant) decorateReferenceEventButton(element);
    return element;
  }

  getRuntimeListItemCount(sourcePath, instanceTrail) {
    if (!this.runtimeSnapshot?.widgets?.length) return null;
    const suffix = runtimePathSuffix(sourcePath, instanceTrail);
    const expression = new RegExp(`${escapeRegExp(suffix)}/Widget\\[(\\d+)](?:/|$)`);
    const indexes = this.runtimeSnapshot.widgets
      .map((row) => String(row.path || "").match(expression))
      .filter(Boolean)
      .map((match) => Number(match[1]))
      .filter(Number.isFinite);
    return indexes.length ? Math.max(...indexes) : null;
  }

  findRuntimeWidgetRow(metadata) {
    const widgets = this.runtimeSnapshot?.widgets || [];
    if (!widgets.length) return null;
    if (metadata.id) {
      const idMatches = widgets.filter((row) => row.id === metadata.id);
      if (idMatches.length === 1) return idMatches[0];
    }
    const suffix = runtimePathSuffix(metadata.path, metadata.instanceTrail);
    const matches = widgets.filter((row) => String(row.path || "").endsWith(suffix));
    return matches.length === 1 ? matches[0] : null;
  }

  applyNodeStyle(element, node, parentInfo, context) {
    const widthPolicy = node.getAttribute("WidthSizePolicy") || "Fixed";
    const heightPolicy = node.getAttribute("HeightSizePolicy") || "Fixed";
    const suggestedWidth = numeric(this.resolveText(node.getAttribute("SuggestedWidth"), context));
    const suggestedHeight = numeric(this.resolveText(node.getAttribute("SuggestedHeight"), context));
    const minWidth = numeric(this.resolveText(node.getAttribute("MinWidth"), context));
    const maxWidth = numeric(this.resolveText(node.getAttribute("MaxWidth"), context));
    const minHeight = numeric(this.resolveText(node.getAttribute("MinHeight"), context));
    const maxHeight = numeric(this.resolveText(node.getAttribute("MaxHeight"), context));
    const positionXOffset = numeric(this.resolveText(node.getAttribute("PositionXOffset"), context));
    const positionYOffset = numeric(this.resolveText(node.getAttribute("PositionYOffset"), context));
    const margins = {
      left: numeric(this.resolveText(node.getAttribute("MarginLeft"), context)),
      right: numeric(this.resolveText(node.getAttribute("MarginRight"), context)),
      top: numeric(this.resolveText(node.getAttribute("MarginTop"), context)),
      bottom: numeric(this.resolveText(node.getAttribute("MarginBottom"), context))
    };
    const isTextNode = node.nodeName === "TextWidget" || node.nodeName === "RichTextWidget" || node.nodeName === "EditableTextWidget";
    if (isTextNode && widthPolicy === "CoverChildren") {
      element.style.whiteSpace = "pre";
      element.style.overflowWrap = "normal";
    }
    const colorAttribute = isTextNode
      ? node.getAttribute("Brush.FontColor") || node.getAttribute("Brush.TextColor") || node.getAttribute("Brush.Color")
      : node.getAttribute("Color") || node.getAttribute("Brush.Color");
    const color = colorToCss(this.resolveText(colorAttribute, context));
    const sprite = this.resolveText(node.getAttribute("Sprite"), context) || "";
    const brush = this.resolveText(node.getAttribute("Brush"), context) || "";

    this.applySpriteStyle(element, sprite, brush, color);
    if (node.getAttribute("AlphaFactor")) element.style.opacity = this.resolveText(node.getAttribute("AlphaFactor"), context);
    if (node.getAttribute("RenderLate") === "true") element.style.zIndex = "1000";

    if (parentInfo.flow) {
      this.setFlowBox(element, parentInfo, {
        widthPolicy,
        heightPolicy,
        suggestedWidth,
        suggestedHeight,
        margins,
        horizontalAlignment: node.getAttribute("HorizontalAlignment") || "Left",
        verticalAlignment: node.getAttribute("VerticalAlignment") || "Top"
      });
    } else {
      this.setAbsoluteBox(element, node, { widthPolicy, heightPolicy, suggestedWidth, suggestedHeight, margins });
    }
    // Gauntlet reports Position*Offset separately from a widget's layout
    // bounds. Moving the browser element itself made its measured rectangle
    // disagree with engine snapshots (notably settlement headings by 10 px).
    if ((positionXOffset || positionYOffset) && !isTextNode) {
      element.style.transform = addTransform(element.style.transform, `translate(${positionXOffset}px, ${positionYOffset}px)`);
    }
    element.dataset.positionXOffset = String(positionXOffset);
    element.dataset.positionYOffset = String(positionYOffset);

    applyDimensionConstraints(element, { minWidth, maxWidth, minHeight, maxHeight });

    if (node.nodeName === "ListPanel") {
      const layoutMethod = getLayoutMethod(node);
      const horizontal = layoutMethod.includes("horizontal");
      const reverse = layoutMethod.includes("righttoleft") || layoutMethod.includes("bottomtotop");
      element.style.display = "flex";
      element.style.flexDirection = horizontal
        ? reverse ? "row-reverse" : "row"
        : reverse ? "column-reverse" : "column";
      element.style.alignItems = "stretch";
      element.style.alignContent = "stretch";
      const maxHorizontalItems = numeric(this.resolveText(node.getAttribute("StackLayout.MaxHorizontalItems"), context));
      if (horizontal && maxHorizontalItems > 0) {
        element.style.flexWrap = "wrap";
        element.style.alignContent = "flex-start";
      }
    }
  }

  applySpriteStyle(element, sprite, brush, color) {
    const lowerSprite = sprite.toLowerCase();
    const runtimeSprite = this.runtimeSpriteState?.sprites?.get(sprite);
    const runtimePart = this.runtimeSpriteState?.parts?.get(runtimeSprite?.partName || sprite);
    if (lowerSprite.includes("conversation_frame") || lowerSprite.includes("gold_frame") || lowerSprite.includes("button_frame")) {
      element.classList.add("sprite-frame", "sprite-decoration");
      if (color) element.style.color = color;
    } else if (lowerSprite.includes("title_divider")) {
      element.classList.add("sprite-divider", "sprite-decoration");
      if (color) element.style.color = color;
    } else if (brush.toLowerCase().includes("scrollbar.handle")) {
      element.classList.add("sprite-scroll-handle", "sprite-decoration");
    } else if (runtimePart?.sourceUrl) {
      element.classList.add("has-asset", "sprite-decoration");
      if (runtimeSprite?.nineRegion) {
        const { left, right, top, bottom } = runtimeSprite.nineRegion;
        element.classList.add("runtime-nine-region");
        element.style.setProperty("border-style", "solid", "important");
        element.style.setProperty("border-width", `${top}px ${right}px ${bottom}px ${left}px`, "important");
        element.style.setProperty("border-image-source", cssUrl(runtimePart.sourceUrl), "important");
        element.style.setProperty("border-image-slice", `${top} ${right} ${bottom} ${left} fill`, "important");
        element.style.setProperty("border-image-width", `${top}px ${right}px ${bottom}px ${left}px`, "important");
        element.style.setProperty("border-image-repeat", "stretch", "important");
      } else {
        element.style.backgroundImage = cssUrl(runtimePart.sourceUrl);
        element.style.backgroundPosition = "center";
        element.style.backgroundRepeat = "no-repeat";
        element.style.backgroundSize = "100% 100%";
      }
    } else if (lowerSprite.includes("reign_court_throne_background")) {
      element.classList.add("has-asset", "sprite-decoration");
      element.style.backgroundImage = cssUrl("../../GUI/SpriteParts/ui_reignbeta_court/reign_court_throne_background.png");
      element.style.backgroundPosition = "center";
      element.style.backgroundSize = "cover";
    } else if (lowerSprite.startsWith("reign_social_event_")) {
      element.classList.add("has-asset", "sprite-decoration");
      element.style.backgroundImage = cssUrl(`../../GUI/SpriteParts/ui_reignbeta_social_event/${sprite}.png`);
      element.style.backgroundPosition = "center";
      element.style.backgroundRepeat = "no-repeat";
      element.style.backgroundSize = "100% 100%";
    } else if (color) {
      if (element.classList.contains("text") || element.classList.contains("editable")) element.style.color = color;
      else element.style.backgroundColor = color;
    }
  }

  setFlowBox(element, parentInfo, values) {
    const { widthPolicy, heightPolicy, suggestedWidth, suggestedHeight, margins } = values;
    element.style.position = "relative";
    setMargins(element, margins);

    if (parentInfo.horizontal) {
      if (widthPolicy === "StretchToParent") {
        element.style.flex = "1 1 0";
        element.style.width = "auto";
      } else if (widthPolicy === "CoverChildren") {
        element.style.flex = "0 0 auto";
        element.style.width = "fit-content";
      } else {
        const width = suggestedWidth || defaultWidth(element);
        element.style.width = `${width}px`;
        element.style.flex = `0 0 ${width}px`;
      }

      if (heightPolicy === "StretchToParent") {
        element.style.height = "auto";
        element.style.alignSelf = "stretch";
      } else if (heightPolicy === "CoverChildren") {
        element.style.height = "auto";
        element.style.minHeight = `${suggestedHeight || 1}px`;
      } else {
        element.style.height = `${suggestedHeight || defaultHeight(element)}px`;
      }
    } else {
      if (widthPolicy === "StretchToParent") {
        element.style.width = "auto";
        element.style.alignSelf = "stretch";
      } else if (widthPolicy === "CoverChildren") {
        element.style.width = "fit-content";
      } else {
        element.style.width = `${suggestedWidth || defaultWidth(element)}px`;
      }

      if (heightPolicy === "StretchToParent") {
        element.style.flex = "1 1 0";
        element.style.height = "auto";
        element.style.minHeight = "0";
      } else if (heightPolicy === "CoverChildren") {
        element.style.flex = "0 0 auto";
        element.style.height = "auto";
        element.style.minHeight = `${suggestedHeight || 1}px`;
      } else {
        const height = suggestedHeight || defaultHeight(element);
        element.style.height = `${height}px`;
        element.style.flex = `0 0 ${height}px`;
      }
    }

    applyFlowAlignment(element, values, parentInfo);
  }

  setAbsoluteBox(element, node, values) {
    const { widthPolicy, heightPolicy, suggestedWidth, suggestedHeight, margins } = values;
    const horizontalAlignment = node.getAttribute("HorizontalAlignment") || "Left";
    const verticalAlignment = node.getAttribute("VerticalAlignment") || "Top";
    element.style.position = "absolute";

    if (widthPolicy === "StretchToParent") {
      element.style.left = `${margins.left}px`;
      element.style.right = `${margins.right}px`;
    } else if (widthPolicy === "CoverChildren") {
      if (node.nodeName === "TextWidget" || node.nodeName === "RichTextWidget" || node.nodeName === "EditableTextWidget") {
        element.style.width = "max-content";
        element.style.minWidth = `${suggestedWidth || 1}px`;
      } else {
        element.style.width = `${suggestedWidth || 1}px`;
      }
    } else {
      element.style.width = `${suggestedWidth || defaultWidth(element)}px`;
    }

    if (heightPolicy === "StretchToParent") {
      element.style.top = `${margins.top}px`;
      element.style.bottom = `${margins.bottom}px`;
    } else if (heightPolicy === "CoverChildren") {
      element.style.height = node.nodeName === "TextWidget" || node.nodeName === "RichTextWidget" || node.nodeName === "EditableTextWidget"
        ? "max-content"
        : "auto";
      element.style.minHeight = `${suggestedHeight || 1}px`;
    } else {
      element.style.height = `${suggestedHeight || defaultHeight(element)}px`;
    }

    if (widthPolicy !== "StretchToParent") {
      if (horizontalAlignment === "Right") {
        element.style.right = `${margins.right}px`;
      } else if (horizontalAlignment === "Center") {
        element.style.left = `calc(50% + ${(margins.left - margins.right) / 2}px)`;
        element.style.transform = addTransform(element.style.transform, "translateX(-50%)");
      } else {
        element.style.left = `${margins.left}px`;
      }
    }

    if (heightPolicy !== "StretchToParent") {
      if (verticalAlignment === "Bottom") {
        element.style.bottom = `${margins.bottom}px`;
      } else if (verticalAlignment === "Center") {
        element.style.top = `calc(50% + ${(margins.top - margins.bottom) / 2}px)`;
        element.style.transform = addTransform(element.style.transform, "translateY(-50%)");
      } else {
        element.style.top = `${margins.top}px`;
      }
    }
  }

  applyTextStyle(element, node, context) {
    const fontSize = numeric(this.resolveText(node.getAttribute("Brush.FontSize"), context));
    const alignment = node.getAttribute("Brush.TextHorizontalAlignment") || "Center";
    const brush = this.resolveText(node.getAttribute("Brush"), context) || "";
    const gauntletFont = (this.resolveText(node.getAttribute("Brush.Font"), context) || "").trim();
    const color = colorToCss(this.resolveText(
      node.getAttribute("Brush.FontColor") || node.getAttribute("Brush.TextColor") || node.getAttribute("Brush.Color"),
      context
    ));
    if (fontSize) element.style.fontSize = `${fontSize}px`;
    if (color) element.style.color = color;

    const brushTypography = BRUSH_TYPOGRAPHY.get(brush);
    if (brushTypography) {
      element.style.fontFamily = brushTypography.family;
      element.style.fontWeight = brushTypography.weight;
      element.style.lineHeight = brushTypography.lineHeight;
      element.style.textShadow = brushTypography.textShadow;
    }

    const normalizedFont = gauntletFont.toLowerCase();
    const mappedFontFamily = FONT_FAMILY_BY_GAUNTLET_FONT.get(normalizedFont);
    if (mappedFontFamily) element.style.fontFamily = mappedFontFamily;
    if (node.nodeName === "RichTextWidget") { element.style.fontFamily = '"Reign Cormorant Garamond", serif'; element.style.fontWeight = "500"; }
    const mappedFontWeight = FONT_WEIGHT_BY_GAUNTLET_FONT.get(normalizedFont);
    if (mappedFontWeight) element.style.fontWeight = mappedFontWeight;
    if (normalizedFont === "reignserifdynamic") {
      // The registered Reign face has no synthetic styling or inherited editor shadow.
      element.style.fontSynthesis = "none";
      element.style.textShadow = "none";
    }

    const effectAttributes = ["Brush.TextOutlineAmount", "Brush.TextGlowRadius", "Brush.TextBlur"];
    const hasExplicitEffects = effectAttributes.every((attribute) => node.hasAttribute(attribute));
    const disablesAllEffects = hasExplicitEffects
      && effectAttributes.every((attribute) => numeric(this.resolveText(node.getAttribute(attribute), context)) <= 0);
    if (disablesAllEffects) element.style.textShadow = "none";

    element.style.justifyContent = alignment === "Left" ? "flex-start" : alignment === "Right" ? "flex-end" : "center";
    element.style.textAlign = alignment.toLowerCase();
  }

  fitCoverChildren(element, node, axis) {
    // Text uses the browser's intrinsic wrapping size. Inline action/offset spans
    // are formatting, not Gauntlet child widgets: measuring only those elements
    // discards adjacent text nodes and can freeze a multiline label to one line.
    if (["TextWidget", "RichTextWidget", "EditableTextWidget"].includes(node.nodeName)) return;
    const children = Array.from(element.children);
    if (!children.length) return;
    const isFlow = element.classList.contains("flow");
    const horizontalFlow = isFlow && getLayoutDirection(node) === "horizontal";
    if (isFlow) {
      const outerSize = (child, measuredAxis) => {
        const style = getComputedStyle(child);
        if (measuredAxis === "width") {
          return child.offsetWidth + numeric(style.marginLeft) + numeric(style.marginRight);
        }
        return child.offsetHeight + numeric(style.marginTop) + numeric(style.marginBottom);
      };
      const sizes = children.map((child) => outerSize(child, axis));
      const extent = axis === "width"
        ? horizontalFlow ? sizes.reduce((sum, value) => sum + value, 0) : Math.max(...sizes)
        : horizontalFlow ? Math.max(...sizes) : sizes.reduce((sum, value) => sum + value, 0);
      const suggested = numeric(node.getAttribute(axis === "width" ? "SuggestedWidth" : "SuggestedHeight"));
      element.style[axis] = `${Math.ceil(Math.max(suggested, extent))}px`;
      return;
    }
    if (axis === "height") {
      let extent = numeric(node.getAttribute("SuggestedHeight"));
      children.forEach((child) => {
        extent = Math.max(extent, child.offsetTop + child.offsetHeight + numeric(child.style.marginBottom));
      });
      if (extent > 0) element.style.height = `${Math.ceil(extent)}px`;
    } else {
      let extent = numeric(node.getAttribute("SuggestedWidth"));
      children.forEach((child) => {
        extent = Math.max(extent, child.offsetLeft + child.offsetWidth + numeric(child.style.marginRight));
      });
      if (extent > 0) element.style.width = `${Math.ceil(extent)}px`;
    }
  }

  collectMissingBindings() {
    const availableKeys = collectAvailableKeys(this.rootData);
    const seen = new Set();
    for (const element of Array.from(this.documentNode.querySelectorAll("*"))) {
      for (const attribute of Array.from(element.attributes || [])) {
        for (const binding of extractBindingNames(attribute.value)) {
          if (availableKeys.has(binding) || seen.has(binding)) continue;
          seen.add(binding);
          const source = this.sourceIndex.get(element) || { path: `/${element.nodeName}[1]`, line: null };
          this.missingBindings.push({
            kind: "missing",
            binding,
            message: `${attribute.name} references ${attribute.value}`,
            path: source.path,
            line: source.line,
            selectionKey: ""
          });
        }
      }
    }
  }

  recordMissingBindings(metadata) {
    for (const property of metadata.boundProperties.filter((candidate) => candidate.missing)) {
      for (const binding of property.bindingNames.filter((name) => !hasValue(property.bindingContext || metadata.elementContext || {}, name))) {
        const key = `${metadata.selectionKey}|${property.attribute}|${binding}`;
        if (this.missingBindings.some((issue) => issue.key === key)) continue;
        this.missingBindings.push({
          key,
          kind: "missing",
          binding,
          message: `${property.attribute} references ${property.expression} in ${metadata.dataSource || "root"} context`,
          path: metadata.path,
          line: metadata.line,
          selectionKey: metadata.selectionKey,
          token: metadata.token,
          label: `${metadata.id ? `${metadata.tag}#${metadata.id}` : metadata.tag}${metadata.instanceTrail.length ? ` · ${metadata.instanceTrail.join("/")}` : ""}`
        });
      }
    }
  }

  collectAssetIssue(metadata) {
    const sprite = metadata.sprite || "";
    if (!sprite.toLowerCase().startsWith("reign_") || this.assetIssueSprites.has(sprite)) return;

    const part = this.runtimeSpriteState?.parts?.get(sprite);
    if (part?.ready) return;
    this.assetIssueSprites.add(sprite);
    this.assetIssues.push({
      kind: "asset",
      message: part?.message || this.runtimeSpriteState?.statusMessage || `${sprite} is not declared in the generated runtime sprite manifest.`,
      path: metadata.path,
      line: metadata.line,
      token: metadata.token,
      selectionKey: metadata.selectionKey,
      label: sprite
    });
  }

  hydrateBindingIssues() {
    for (const issue of this.missingBindings) {
      const element = this.renderedElements.find((candidate) => candidate.__gauntlet?.path === issue.path);
      if (!element) continue;
      const metadata = element.__gauntlet;
      issue.selectionKey = metadata.selectionKey;
      issue.token = metadata.token;
      issue.label = `${metadata.id ? `${metadata.tag}#${metadata.id}` : metadata.tag}${metadata.instanceTrail.length ? ` · ${metadata.instanceTrail.join("/")}` : ""}`;
    }
  }

  resolveBoundProperties(attributes, context, dataSourceContext = context) {
    const properties = [];
    for (const [attribute, expression] of Object.entries(attributes)) {
      // ReignPreview* attributes are editor provenance, not Gauntlet bindings.
      // Their XPath text legitimately contains @ and {...} tokens.
      if (attribute.startsWith("ReignPreview")) continue;
      const names = extractBindingNames(expression);
      if (!names.length) continue;
      const bindingContext = attribute === "DataSource" ? dataSourceContext : context;
      const resolved = this.resolveValue(expression, bindingContext);
      properties.push({
        attribute,
        expression,
        bindingNames: names,
        value: resolved,
        bindingContext,
        missing: names.some((name) => !hasValue(bindingContext, name))
      });
    }
    return properties;
  }

  resolveImageAsset(node, context, resolvedLabel) {
    const widgetAsset = this.assetByWidgetId[node.getAttribute("Id") || ""];
    if (widgetAsset) return widgetAsset;
    if (node.nodeName === "ReignEventArtWidget") return this.assetByImageId[resolvedLabel] || context.EventImageAsset || "";
    if ((node.nodeName.includes("PortraitWidget") || node.nodeName === "ReignAspectMaskedTextureWidget") && context.PortraitAsset) return context.PortraitAsset;

    const expression = node.getAttribute("ImageId") || node.getAttribute("EventImageId") || "";
    const binding = extractBindingNames(expression)[0];
    if (binding) {
      const candidates = [`${binding}Asset`];
      if (binding.endsWith("ImageId")) candidates.push(`${binding.slice(0, -"ImageId".length)}Asset`);
      if (binding.endsWith("Id")) candidates.push(`${binding.slice(0, -"Id".length)}Asset`);
      for (const candidate of candidates) {
        if (context[candidate]) return context[candidate];
      }
    }
    if (context.PortraitAsset && /portrait/i.test(resolvedLabel || expression)) return context.PortraitAsset;
    return this.assetByImageId[resolvedLabel] || "";
  }

  isVisible(node, context) {
    const expression = node.getAttribute("IsVisible");
    if (expression == null || expression === "") return true;
    const resolved = this.resolveValue(expression, context);
    if (typeof resolved === "boolean") return resolved;
    if (typeof resolved === "string") return !["false", "0", "no", ""].includes(resolved.toLowerCase());
    return Boolean(resolved);
  }

  resolveText(value, context) {
    const resolved = this.resolveValue(value, context);
    if (resolved == null) return "";
    if (typeof resolved !== "string") return String(resolved);
    return resolved.replace(/@([A-Za-z_][A-Za-z0-9_]*)/g, (match, key) => {
      const item = getContextValue(context, key);
      return item == null ? match : String(item);
    });
  }

  resolveValue(value, context) {
    if (value == null) return null;
    if (typeof value !== "string") return value;
    const constant = value.match(/^!([A-Za-z_][A-Za-z0-9_.]*)$/);
    if (constant) return Object.prototype.hasOwnProperty.call(this.constants || {}, constant[1])
      ? this.constants[constant[1]]
      : null;
    if (value === "{..}") return context.$parent || context.$root || null;
    const direct = value.match(/^@([A-Za-z_][A-Za-z0-9_]*(?:[\\/][A-Za-z_][A-Za-z0-9_]*)*)$/)
      || value.match(/^\{([A-Za-z_][A-Za-z0-9_]*(?:[\\/][A-Za-z_][A-Za-z0-9_]*)*)\}$/);
    if (direct) return getContextPathValue(context, direct[1]);
    return value;
  }

  makeContext(values, trail, dataSource, strict, parent) {
    if (dataSource === "ChatLines" && values && typeof values.Text === "string" && values.RichText == null) {
      values = { ...values, RichText: previewActionRichText(values.Text, values.IsNpcLine || values.Role === "npc") };
    }
    return {
      ...(values || {}),
      $root: this.rootData,
      $parent: parent || null,
      $trail: trail,
      $dataSource: dataSource,
      $strict: Boolean(strict)
    };
  }
}

function applyDimensionConstraints(element, constraints) {
  if (constraints.minWidth > 0) element.style.minWidth = `${constraints.minWidth}px`;
  if (constraints.maxWidth > 0) element.style.maxWidth = `${constraints.maxWidth}px`;
  if (constraints.minHeight > 0) element.style.minHeight = `${constraints.minHeight}px`;
  if (constraints.maxHeight > 0) element.style.maxHeight = `${constraints.maxHeight}px`;
}

function getContextValue(context, key) {
  if (Object.prototype.hasOwnProperty.call(context, key)) return context[key];
  if (!context.$strict && context.$root && Object.prototype.hasOwnProperty.call(context.$root, key)) return context.$root[key];
  return null;
}

function getContextPathValue(context, path) {
  const segments = String(path || "").split(/[\\/]+/).filter(Boolean);
  if (!segments.length) return null;
  const fromContext = readPath(context, segments);
  if (fromContext.found) return fromContext.value;
  if (context.$root) {
    const fromRoot = readPath(context.$root, segments);
    if (fromRoot.found) return fromRoot.value;
  }
  return null;
}

function readPath(value, segments) {
  let current = value;
  for (const segment of segments) {
    if (!current || typeof current !== "object" || !Object.prototype.hasOwnProperty.call(current, segment)) {
      return { found: false, value: null };
    }
    current = current[segment];
  }
  return { found: true, value: current };
}

function hasValue(context, key) {
  return Object.prototype.hasOwnProperty.call(context, key)
    || Boolean(!context.$strict && context.$root && Object.prototype.hasOwnProperty.call(context.$root, key));
}

function collectAvailableKeys(value, keys = new Set(), visited = new Set()) {
  if (!value || typeof value !== "object" || visited.has(value)) return keys;
  visited.add(value);
  if (!Array.isArray(value)) Object.keys(value).forEach((key) => keys.add(key));
  Object.values(value).forEach((child) => {
    if (child && typeof child === "object") collectAvailableKeys(child, keys, visited);
  });
  return keys;
}

function getLayoutDirection(node) {
  return getLayoutMethod(node).includes("horizontal") ? "horizontal" : "vertical";
}

function getLayoutMethod(node) {
  return (node.getAttribute("StackLayout.LayoutMethod") || "").toLowerCase();
}

function getDataSourceName(value) {
  if (!value) return "";
  return value.replace(/[{}@]/g, "").trim();
}

function setMargins(element, margins) {
  element.style.marginLeft = `${margins.left}px`;
  element.style.marginRight = `${margins.right}px`;
  element.style.marginTop = `${margins.top}px`;
  element.style.marginBottom = `${margins.bottom}px`;
}

function applyFlowAlignment(element, values, parentInfo) {
  const alignment = values.horizontalAlignment || "Left";
  if (parentInfo.horizontal) {
    if (values.verticalAlignment === "Center") element.style.alignSelf = "center";
    if (values.verticalAlignment === "Bottom") element.style.alignSelf = "flex-end";
    return;
  }
  if (alignment === "Right") element.style.marginLeft = "auto";
  if (alignment === "Center") {
    element.style.marginLeft = "auto";
    element.style.marginRight = "auto";
  }
}

function defaultWidth(element) {
  if (element.classList.contains("text")) return 120;
  if (element.classList.contains("portrait")) return 96;
  return 80;
}

function defaultHeight(element) {
  if (element.classList.contains("text")) return 30;
  if (element.classList.contains("portrait")) return 96;
  return 30;
}

function numeric(value) {
  if (value == null || value === "") return 0;
  const number = Number(String(value).replace("px", ""));
  return Number.isFinite(number) ? number : 0;
}

function colorToCss(value) {
  if (!value || typeof value !== "string" || !value.startsWith("#")) return "";
  const hex = value.slice(1);
  if (hex.length !== 6 && hex.length !== 8) return value;
  const red = parseInt(hex.slice(0, 2), 16);
  const green = parseInt(hex.slice(2, 4), 16);
  const blue = parseInt(hex.slice(4, 6), 16);
  const alpha = hex.length === 8 ? parseInt(hex.slice(6, 8), 16) / 255 : 1;
  return `rgba(${red}, ${green}, ${blue}, ${alpha.toFixed(3)})`;
}

function cssUrl(path) {
  return `url("${String(path).replaceAll("\\", "/").replaceAll('"', "%22")}")`;
}

function addTransform(existing, addition) {
  return existing ? `${existing} ${addition}` : addition;
}

function runtimePathSuffix(sourcePath, instanceTrail = []) {
  const structuralNames = new Set(["Prefab", "Window", "Children", "ItemTemplate"]);
  const segments = String(sourcePath || "")
    .split("/")
    .filter(Boolean)
    .filter((segment) => !structuralNames.has(segment.replace(/\[\d+]$/, "")));
  const indexes = instanceTrail
    .map((entry) => Number(String(entry).match(/\[(\d+)]$/)?.[1]))
    .filter((value) => Number.isFinite(value) && value > 0);
  let trailIndex = 0;
  for (let index = 0; index < segments.length - 1 && trailIndex < indexes.length; index += 1) {
    if (!/^ListPanel\[\d+]$/.test(segments[index]) || !/^\w*Widget\[\d+]$/.test(segments[index + 1])) continue;
    segments[index + 1] = segments[index + 1].replace(/\[\d+]$/, `[${indexes[trailIndex]}]`);
    trailIndex += 1;
  }
  return `/${segments.join("/")}`;
}

function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function makeSelectionToken(fileName, source, instanceTrail, tag, id) {
  const params = new URLSearchParams({
    file: fileName,
    line: source.line == null ? "unknown" : String(source.line),
    path: source.path,
    type: tag
  });
  if (id) params.set("id", id);
  if (instanceTrail.length) params.set("instance", instanceTrail.join("/"));
  return `reignxml://select?${params.toString()}`;
}

function cleanParseError(message) {
  return String(message || "XML parse error")
    .replace(/This page contains the following errors?:/i, "")
    .replace(/Below is a rendering of the page up to the first error\./i, "")
    .trim();
}

export function resolvePortraitComposition(container) {
  const layers = Array.from(container?.children || []);
  const directSources = layers.filter((layer) => layer.classList?.contains?.("portrait"));
  if (directSources.length) {
    const lastSourceIndex = Math.max(...directSources.map((source) => layers.indexOf(source)));
    const apertureCandidates = layers.slice(lastSourceIndex + 1).filter((layer) =>
      layer.dataset?.sprite
      || layer.classList?.contains?.("image")
      || layer.classList?.contains?.("sprite-decoration")
    );
    return {
      backing: null,
      clip: container,
      aperture: apertureCandidates.at(-1) || layers[lastSourceIndex + 1] || null,
      sources: directSources
    };
  }
  const clipIndex = layers.findIndex((layer) => layer.querySelector?.(".portrait"));
  if (clipIndex < 0) {
    return { backing: null, clip: null, aperture: null, sources: [] };
  }

  const clip = layers[clipIndex];
  const apertureCandidates = layers.slice(clipIndex + 1).filter((layer) =>
    layer.dataset?.sprite
    || layer.classList?.contains?.("image")
    || layer.classList?.contains?.("sprite-decoration")
  );
  return {
    backing: clipIndex > 0 ? layers[clipIndex - 1] : null,
    clip,
    aperture: apertureCandidates.at(-1) || layers[clipIndex + 1] || null,
    sources: Array.from(clip.querySelectorAll?.(".portrait") || [])
  };
}

export function classifyPortraitPresentation({ id = "", width = 0, height = 0, useFullBody = false } = {}) {
  if (/AIInfluenceMemoryImage|MemoryBookScene/i.test(id)) return "scene";
  if (useFullBody || /(?:ZoomPortrait|PortraitPreview)/i.test(id) || (height > width * 1.2 && /preview/i.test(id))) {
    return "full-body";
  }
  return "headshot";
}

function decorateAttendeeCard(card) {
  const [surface, portrait, name, status, action] = Array.from(card.children);
  surface?.classList.add("attendee-card__surface");
  portrait?.classList.add("attendee-card__portrait");
  name?.classList.add("attendee-card__name");
  status?.classList.add("attendee-card__status");
  action?.classList.add("attendee-card__action");

  const surfaceLayers = Array.from(surface?.children || []);
  surfaceLayers[0]?.classList.add("attendee-card__base");
  surfaceLayers[1]?.classList.add("attendee-card__canvas");
  surfaceLayers[2]?.classList.add("attendee-card__outline");
  surfaceLayers[3]?.classList.add("attendee-card__ornament");

  const portraitComposition = resolvePortraitComposition(portrait);
  portraitComposition.backing?.classList.add("attendee-card__portrait-base");
  portraitComposition.clip?.classList.add("attendee-card__portrait-clip");
  portraitComposition.aperture?.classList.add("attendee-card__portrait-frame");
  portraitComposition.sources.forEach((source) => source.classList.add("attendee-card__portrait-image"));
  name?.querySelector(".text")?.classList.add("attendee-card__name-text");
}

function decorateEventChatPanel(panel) {
  if (panel.clientHeight >= 110 && panel.clientHeight < 220) panel.classList.add("event-chat-panel--compact");
  const [base, canvas, outline, ornament, log, scrollbar, busy, inputFrame, send] = Array.from(panel.children);
  base?.classList.add("event-chat-panel__base");
  canvas?.classList.add("event-chat-panel__canvas");
  outline?.classList.add("event-chat-panel__outline");
  ornament?.classList.add("event-chat-panel__ornament");
  log?.classList.add("event-chat-panel__log");
  scrollbar?.classList.add("event-chat-panel__scrollbar");
  busy?.classList.add("event-chat-panel__busy");
  inputFrame?.classList.add("event-chat-panel__input-frame");
  send?.classList.add("event-chat-panel__send");

  const clip = log?.querySelector('[data-element-id="EventChatClip"]');
  const lines = log?.querySelector('[data-element-id="EventChatList"]');
  clip?.classList.add("event-chat-panel__clip");
  lines?.classList.add("event-chat-panel__lines");
  Array.from(lines?.children || []).forEach((line) => {
    line.classList.add("event-chat-panel__line");
    const [speaker, message] = Array.from(line.children);
    speaker?.classList.add("event-chat-panel__speaker");
    message?.classList.add("event-chat-panel__message");
  });

  const [scrollbarTrack, scrollbarHandle] = Array.from(scrollbar?.children || []);
  scrollbarTrack?.classList.add("event-chat-panel__scrollbar-track");
  scrollbarHandle?.classList.add("event-chat-panel__scrollbar-handle");

  const [inputOutline, input] = Array.from(inputFrame?.children || []);
  inputOutline?.classList.add("event-chat-panel__input-outline");
  input?.classList.add("event-chat-panel__input");
  const sendLabel = send?.querySelector(".text");
  sendLabel?.classList.add("event-chat-panel__send-label");
  if (sendLabel) sendLabel.textContent = "Send";
}

function decorateIndividualChatShell(shell, rootData) {
  const children = Array.from(shell.children);
  const background = children.find((child) => child.dataset.elementId === "ReignIndividualChatBackground");
  const shellArt = children.find((child) => child.dataset.elementId === "ReignIndividualChatShell"
    && child.dataset.sprite === "reign_individual_chat_modern_shell");
  background?.classList.add("individual-chat__legacy-layer");
  shellArt?.classList.add("individual-chat__shell-art");

  const playerPortraitImage = shell.querySelector('[data-element-id="ReignChatPlayerPortrait"]');
  const npcPortraitImage = shell.querySelector('[data-element-id="ReignChatNpcPortrait"]');
  const playerPortraitButton = playerPortraitImage?.closest(".button");
  const npcPortraitButton = npcPortraitImage?.closest(".button");
  const playerPortraitClip = playerPortraitImage?.parentElement;
  const npcPortraitClip = npcPortraitImage?.parentElement;
  playerPortraitButton?.classList.add("individual-chat__portrait", "individual-chat__portrait--player");
  npcPortraitButton?.classList.add("individual-chat__portrait", "individual-chat__portrait--npc");
  playerPortraitClip?.classList.add("individual-chat__portrait-clip");
  npcPortraitClip?.classList.add("individual-chat__portrait-clip");
  playerPortraitImage?.classList.add("individual-chat__portrait-image");
  npcPortraitImage?.classList.add("individual-chat__portrait-image");

  const textClasses = new Map([
    ["@PlayerName", "individual-chat__player-name"],
    ["@PlayerSubtitle", "individual-chat__player-subtitle"],
    ["@PlayerInfo", "individual-chat__player-info"],
    ["@NpcName", "individual-chat__npc-name"],
    ["@NpcSubtitle", "individual-chat__npc-subtitle"],
    ["@NpcInfo", "individual-chat__npc-info"],
    ["@LocationText", "individual-chat__context-title"],
    ["@BusyText", "individual-chat__busy"]
  ]);
  Array.from(shell.querySelectorAll(".text")).forEach((child) => {
    const textBinding = child.__gauntlet?.xmlNode?.getAttribute("Text") || "";
    const className = textClasses.get(textBinding);
    if (!className) return;
    const usesClippingContainer = textBinding === "@PlayerName" || textBinding === "@NpcName";
    const target = usesClippingContainer && child.parentElement !== shell ? child.parentElement : child;
    target.classList.add(className);
    if (target !== child) child.classList.add("individual-chat__name-text");
  });

  Array.from(shell.querySelectorAll(".button")).forEach((child) => {
    const command = child.__gauntlet?.xmlNode?.getAttribute("Command.Click");
    if (command === "ExecuteClose") child.classList.add("individual-chat__leave");
    if (command === "ExecuteLookAtThem") child.classList.add("individual-chat__look");
  });

  const location = shell.querySelector(".individual-chat__context-title");
  if (location) {
    const locationText = String(rootData?.LocationText || "").trim();
    location.textContent = locationText;
    location.dataset.previewBinding = "@LocationText";
    const textBinding = location.__gauntlet?.boundProperties?.find((property) => property.attribute === "Text");
    if (textBinding) {
      textBinding.expression = "@LocationText";
      textBinding.bindingNames = ["LocationText"];
      textBinding.value = locationText;
      textBinding.missing = !Object.prototype.hasOwnProperty.call(rootData || {}, "LocationText");
    }
  }

  const logPanel = shell.querySelector('[data-element-id="ReignChatLogPanelBackground"]');
  const log = shell.querySelector('[data-element-id="ReignChatClip"]')?.parentElement;
  const list = shell.querySelector('[data-element-id="ReignChatList"]');
  const scrollbar = shell.querySelector('[data-element-id="ReignChatScrollbar"]');
  const input = shell.querySelector(".editable");
  const inputFrame = input?.parentElement;
  const send = Array.from(shell.querySelectorAll(".button")).find((button) =>
    button.__gauntlet?.xmlNode?.getAttribute("Command.Click") === "ExecuteSend"
  );
  logPanel?.classList.add("individual-chat__log-panel");
  log?.classList.add("individual-chat__log");
  list?.classList.add("individual-chat__lines");
  scrollbar?.classList.add("individual-chat__scrollbar");
  inputFrame?.classList.add("individual-chat__input-frame");
  input?.classList.add("individual-chat__input");
  send?.classList.add("individual-chat__send");
  send?.querySelector(".text")?.classList.add("individual-chat__send-label");

  Array.from(list?.children || []).forEach((line) => {
    line.classList.add("individual-chat__line");
    const bubble = Array.from(line.children).find((child) => child.offsetParent !== null) || line.children[0];
    if (!bubble) return;
    const visibilityBinding = bubble.__gauntlet?.xmlNode?.getAttribute("IsVisible");
    if (visibilityBinding === "@IsPlayerLine") bubble.classList.add("individual-chat__bubble", "individual-chat__bubble--player");
    else if (visibilityBinding === "@IsNpcLine") bubble.classList.add("individual-chat__bubble", "individual-chat__bubble--npc");
    else bubble.classList.add("individual-chat__bubble", "individual-chat__bubble--system");

    const textNodes = Array.from(bubble.children).filter((child) => child.classList.contains("text"));
    if (textNodes.length > 1) {
      textNodes[0].classList.add("individual-chat__speaker");
      textNodes.at(-1).classList.add("individual-chat__message");
    } else {
      textNodes[0]?.classList.add("individual-chat__message");
    }
  });

  fitIndividualChatText(shell.querySelector(".individual-chat__player-name .text"), 17, 13, true);
  fitIndividualChatText(shell.querySelector(".individual-chat__npc-name .text"), 17, 13, true);
  fitIndividualChatText(shell.querySelector(".individual-chat__player-subtitle"), 15, 11);
  fitIndividualChatText(shell.querySelector(".individual-chat__npc-subtitle"), 15, 11);
}

function fitIndividualChatText(element, maximumSize, minimumSize, singleLine = false) {
  if (!element) return;
  element.style.setProperty("white-space", singleLine ? "nowrap" : "normal", "important");
  element.style.setProperty("overflow", "hidden", "important");

  for (let size = maximumSize; size >= minimumSize; size -= 1) {
    element.style.setProperty("font-size", `${size}px`, "important");
    if (element.scrollWidth <= element.clientWidth && element.scrollHeight <= element.clientHeight) break;
  }
}

function decorateEventPhaseBanner(banner) {
  const [base, canvas, ornament, innerFrame, title, description] = Array.from(banner.children);
  base?.classList.add("event-phase-banner__base");
  canvas?.classList.add("event-phase-banner__canvas");
  ornament?.classList.add("event-phase-banner__ornament");
  innerFrame?.classList.add("event-phase-banner__inner-frame");
  title?.classList.add("event-phase-banner__title");
  description?.classList.add("event-phase-banner__description");
}

function decorateEventHeaderBanner(banner) {
  const [base, canvas, ornament, innerFrame, title, status] = Array.from(banner.children);
  base?.classList.add("event-header-banner__base");
  canvas?.classList.add("event-header-banner__canvas");
  ornament?.classList.add("event-header-banner__ornament");
  innerFrame?.classList.add("event-header-banner__inner-frame");
  title?.classList.add("event-header-banner__title");
  status?.classList.add("event-header-banner__status");
}

function decorateEventAttendeesPanel(panel) {
  const [base, canvas, ornament, innerFrame, title, titleDivider, scroll, scrollbar] = Array.from(panel.children);
  base?.classList.add("event-attendees-panel__base");
  canvas?.classList.add("event-attendees-panel__canvas");
  ornament?.classList.add("event-attendees-panel__ornament");
  innerFrame?.classList.add("event-attendees-panel__inner-frame");
  title?.classList.add("event-attendees-panel__title");
  titleDivider?.classList.add("event-attendees-panel__title-divider");
  scroll?.classList.add("event-attendees-panel__scroll");
  scrollbar?.classList.add("event-attendees-panel__scrollbar");

  const clip = scroll?.querySelector('[data-element-id="EventAttendeeClip"]');
  const stack = scroll?.querySelector('[data-element-id="EventAttendeeStack"]');
  clip?.classList.add("event-attendees-panel__clip");
  stack?.classList.add("event-attendees-panel__stack");

  const [activeSection, activeList, availableSection, availableList] = Array.from(stack?.children || []);
  activeSection?.classList.add("event-attendees-panel__section", "event-attendees-panel__section--active");
  activeList?.classList.add("event-attendees-panel__cards", "event-attendees-panel__cards--active");
  availableSection?.classList.add("event-attendees-panel__section", "event-attendees-panel__section--available");
  availableList?.classList.add("event-attendees-panel__cards", "event-attendees-panel__cards--available");

  [activeSection, availableSection].forEach((section) => {
    const [startLine, label, endLine] = Array.from(section?.children || []);
    startLine?.classList.add("event-attendees-panel__section-line", "event-attendees-panel__section-line--start");
    label?.classList.add("event-attendees-panel__section-label");
    endLine?.classList.add("event-attendees-panel__section-line", "event-attendees-panel__section-line--end");
  });

  const [scrollbarTrack, scrollbarHandle] = Array.from(scrollbar?.children || []);
  scrollbarTrack?.classList.add("event-attendees-panel__scrollbar-track");
  scrollbarHandle?.classList.add("event-attendees-panel__scrollbar-handle");
}

function decorateReferenceEventButton(button) {
  button.querySelector(".text")?.classList.add("reference-event-button__label");
}
