const PATCH_SCHEMA = "reign-ui-calibration-patch-v1";

export class CalibrationEditor {
  constructor(options) {
    this.stage = options.stage;
    this.stageContent = options.stageContent;
    this.renderer = options.renderer;
    this.requestRender = options.requestRender;
    this.getSurfaceState = options.getSurfaceState;
    this.setStatus = options.setStatus;
    this.onSourceApplied = options.onSourceApplied;
    this.fileName = "";
    this.sourceXml = "";
    this.sourceFingerprint = "";
    this.sourceOutOfDate = false;
    this.editsByFile = new Map();
    this.undoStack = [];
    this.redoStack = [];
    this.selectedElement = null;
    this.drag = null;
    this.runtimeSnapshot = null;
    this.installSync = null;
    this.installSummary = null;
    this.referenceObjectUrl = "";
    this.referenceSource = "";
    this.elements = collectElements();
    this.bind();
  }

  setDocument(fileName, sourceXml) {
    const nextFingerprint = fingerprint(sourceXml);
    const changedSource = this.fileName === fileName && this.sourceFingerprint && this.sourceFingerprint !== nextFingerprint;
    this.fileName = fileName || "Loaded.xml";
    this.sourceXml = sourceXml || "";
    this.sourceFingerprint = nextFingerprint;
    if (changedSource && this.getEdits().size) {
      this.feedback("The latest source was loaded and your staged edits were reapplied. Review the patch before applying it.");
    }
    this.sourceOutOfDate = false;
    this.refreshPatchReview();
    void this.refreshInstallStatus();
  }

  applyEdits(xmlText, fileName) {
    const edits = this.editsByFile.get(fileName);
    if (!edits?.size) return xmlText;
    const documentNode = new DOMParser().parseFromString(xmlText, "application/xml");
    if (documentNode.querySelector("parsererror")) return xmlText;
    const resolvedEdits = [...edits.values()].map((edit) => ({ edit, node: findByPath(documentNode, edit.path) }));
    for (const { edit, node } of resolvedEdits) {
      if (!node) continue;
      if (edit.operation === "delete") continue;
      for (const [name, value] of Object.entries(edit.attributes)) {
        if (value == null || value === "") node.removeAttribute(name);
        else node.setAttribute(name, String(value));
      }
    }
    for (const { edit, node } of resolvedEdits) {
      if (node && edit.operation === "delete") node.remove();
    }
    return new XMLSerializer().serializeToString(documentNode);
  }

  setSelection(element) {
    this.selectedElement = element?.__gauntlet ? element : null;
    this.elements.inspector.hidden = !this.selectedElement;
    this.populateInputs();
    this.refreshOverlay();
    this.refreshRuntimeMeasurement();
  }

  clearSelection() {
    this.selectedElement = null;
    this.elements.inspector.hidden = true;
    this.elements.overlay.hidden = true;
    this.elements.runtimeOverlay.hidden = true;
  }

  refresh() {
    this.refreshOverlay();
    this.refreshRuntimeMeasurement();
  }

  getState() {
    return {
      editing: this.elements.editMode.checked,
      fileName: this.fileName,
      editCount: this.getEdits().size,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.length > 0,
      sourceOutOfDate: this.sourceOutOfDate,
      runtimeSnapshot: this.runtimeSnapshot?.movieName || "",
      hasReferenceImage: Boolean(this.elements.reference.getAttribute("src")),
      referenceSource: this.referenceSource,
      installSync: this.installSync,
      installSummary: this.installSummary
    };
  }

  hasPendingEdits() {
    return this.getEdits().size > 0;
  }

  setSourceOutOfDate(value) {
    this.sourceOutOfDate = Boolean(value);
    this.refreshPatchReview();
  }

  bind() {
    this.elements.editMode.addEventListener("change", () => {
      this.stage.classList.toggle("editing", this.elements.editMode.checked);
      this.refreshOverlay();
    });
    this.elements.overlay.addEventListener("pointerdown", (event) => {
      const handle = event.target.closest("[data-calibration-handle]")?.dataset.calibrationHandle;
      if (!handle || !this.selectedElement) return;
      this.beginDrag(event, handle);
    });
    window.addEventListener("pointermove", (event) => this.updateDrag(event));
    window.addEventListener("pointerup", (event) => this.endDrag(event));
    window.addEventListener("resize", () => this.refresh());
    document.addEventListener("keydown", (event) => this.handleKeyDown(event));
    this.elements.undo.addEventListener("click", () => this.undo());
    this.elements.redo.addEventListener("click", () => this.redo());
    this.elements.applyValues.addEventListener("click", () => this.applyNumericValues());
    this.elements.savePatch.addEventListener("click", () => this.savePatch());
    this.elements.downloadPatch.addEventListener("click", () => this.downloadPatch());
    this.elements.applySource.addEventListener("click", () => this.applyToSource(false));
    this.elements.applyGame.addEventListener("click", () => this.applyToSource(true));
    this.elements.refreshInstall.addEventListener("click", () => this.refreshInstallStatus());
    this.elements.installSource.addEventListener("click", () => this.installCurrentSource());
    this.elements.runtimeInput.addEventListener("change", (event) => this.importRuntimeSnapshot(event));
    this.elements.applyRuntimeEdits.addEventListener("click", () => this.importRuntimeEdits());
    this.elements.referenceInput.addEventListener("change", (event) => this.importReference(event));
    this.elements.referenceOpacity.addEventListener("input", () => this.updateReferenceStyle());
    this.elements.difference.addEventListener("change", () => this.updateReferenceStyle());
    this.elements.clearReference.addEventListener("click", () => this.clearReference());
  }

  beginDrag(event, handle) {
    event.preventDefault();
    event.stopPropagation();
    const targetElement = handle === "move" ? this.getMovementTarget(event.altKey) : this.selectedElement;
    const metadata = targetElement.__gauntlet;
    const attributes = { ...metadata.attributes };
    const rect = targetElement.getBoundingClientRect();
    const logicalScale = rect.width > 0 && targetElement.offsetWidth > 0
      ? rect.width / targetElement.offsetWidth
      : 1;
    this.drag = {
      pointerId: event.pointerId,
      handle,
      targetElement,
      startX: event.clientX,
      startY: event.clientY,
      logicalScale,
      width: targetElement.offsetWidth,
      height: targetElement.offsetHeight,
      attributes,
      aspect: Math.max(0.01, targetElement.offsetWidth / Math.max(1, targetElement.offsetHeight)),
      dx: 0,
      dy: 0
    };
    if (targetElement !== this.selectedElement) {
      this.feedback(`Moving parent ${metadata.id || metadata.tag}; the selected ${this.selectedElement.__gauntlet.tag} stretches to fill it. Hold Alt while dragging to move only the selected child.`);
    }
    this.elements.overlay.setPointerCapture?.(event.pointerId);
    this.refreshOverlay();
  }

  updateDrag(event) {
    if (!this.drag || event.pointerId !== this.drag.pointerId || !this.selectedElement) return;
    const snap = Math.max(1, Number(this.elements.snap.value) || 1);
    let dx = snapValue((event.clientX - this.drag.startX) / this.drag.logicalScale, snap);
    let dy = snapValue((event.clientY - this.drag.startY) / this.drag.logicalScale, snap);
    const handle = this.drag.handle;
    const lockAspect = this.elements.aspectLock.checked || event.shiftKey;
    if (lockAspect && handle.length === 2) {
      const width = Math.max(1, this.drag.width + (handle.includes("e") ? dx : -dx));
      const height = Math.max(1, this.drag.height + (handle.includes("s") ? dy : -dy));
      if (Math.abs(width - this.drag.width) >= Math.abs(height - this.drag.height)) {
        const targetHeight = width / this.drag.aspect;
        dy = (targetHeight - this.drag.height) * (handle.includes("s") ? 1 : -1);
      } else {
        const targetWidth = height * this.drag.aspect;
        dx = (targetWidth - this.drag.width) * (handle.includes("e") ? 1 : -1);
      }
    }
    this.drag.dx = dx;
    this.drag.dy = dy;
    this.previewDrag(handle, dx, dy);
  }

  previewDrag(handle, dx, dy) {
    const element = this.drag.targetElement;
    if (handle === "move") {
      element.style.translate = `${dx}px ${dy}px`;
    } else {
      const width = Math.max(1, this.drag.width + (handle.includes("e") ? dx : handle.includes("w") ? -dx : 0));
      const height = Math.max(1, this.drag.height + (handle.includes("s") ? dy : handle.includes("n") ? -dy : 0));
      element.style.width = `${width}px`;
      element.style.height = `${height}px`;
      element.style.translate = `${handle.includes("w") ? dx : 0}px ${handle.includes("n") ? dy : 0}px`;
    }
    this.refreshOverlay();
  }

  endDrag(event) {
    if (!this.drag || event.pointerId !== this.drag.pointerId || !this.selectedElement) return;
    const { handle, dx, dy, attributes, width, height, targetElement } = this.drag;
    targetElement.style.translate = "";
    targetElement.style.width = "";
    targetElement.style.height = "";
    this.drag = null;
    if (dx === 0 && dy === 0) {
      this.refreshOverlay();
      return;
    }
    const changes = {};
    if (handle === "move") {
      changes.PositionXOffset = numberAttribute(attributes, "PositionXOffset") + dx;
      changes.PositionYOffset = numberAttribute(attributes, "PositionYOffset") + dy;
    } else {
      this.applyResizeChanges(changes, attributes, handle, dx, dy, width, height);
    }
    this.commit(changes, `Drag ${handle}`, targetElement);
  }

  getMovementTarget(forceSelectedChild = false) {
    if (forceSelectedChild || !this.selectedElement?.__gauntlet) return this.selectedElement;
    const attributes = this.selectedElement.__gauntlet.attributes || {};
    const stretchesBothWays = attributes.WidthSizePolicy === "StretchToParent"
      && attributes.HeightSizePolicy === "StretchToParent";
    const isInnerLabel = this.selectedElement.__gauntlet.tag === "TextWidget"
      || attributes.DoNotAcceptEvents === "true";
    if (!stretchesBothWays || !isInnerLabel) return this.selectedElement;
    let parent = this.selectedElement.parentElement?.closest(".g-node");
    while (parent?.__gauntlet) {
      if (parent.__gauntlet.tag === "ButtonWidget") return parent;
      const parentAttributes = parent.__gauntlet.attributes || {};
      if (parentAttributes.WidthSizePolicy !== "StretchToParent" || parentAttributes.HeightSizePolicy !== "StretchToParent") return parent;
      parent = parent.parentElement?.closest(".g-node");
    }
    return this.selectedElement;
  }

  applyResizeChanges(changes, attributes, handle, dx, dy, width, height) {
    const widthPolicy = attributes.WidthSizePolicy || "Fixed";
    const heightPolicy = attributes.HeightSizePolicy || "Fixed";
    if (handle.includes("w") || handle.includes("e")) {
      if (widthPolicy === "StretchToParent") {
        if (handle.includes("w")) changes.MarginLeft = numberAttribute(attributes, "MarginLeft") + dx;
        if (handle.includes("e")) changes.MarginRight = numberAttribute(attributes, "MarginRight") - dx;
      } else {
        if (widthPolicy === "CoverChildren") changes.WidthSizePolicy = "Fixed";
        changes.SuggestedWidth = Math.max(1, width + (handle.includes("e") ? dx : -dx));
        if (handle.includes("w")) changes.PositionXOffset = numberAttribute(attributes, "PositionXOffset") + dx;
      }
    }
    if (handle.includes("n") || handle.includes("s")) {
      if (heightPolicy === "StretchToParent") {
        if (handle.includes("n")) changes.MarginTop = numberAttribute(attributes, "MarginTop") + dy;
        if (handle.includes("s")) changes.MarginBottom = numberAttribute(attributes, "MarginBottom") - dy;
      } else {
        if (heightPolicy === "CoverChildren") changes.HeightSizePolicy = "Fixed";
        changes.SuggestedHeight = Math.max(1, height + (handle.includes("s") ? dy : -dy));
        if (handle.includes("n")) changes.PositionYOffset = numberAttribute(attributes, "PositionYOffset") + dy;
      }
    }
  }

  applyNumericValues() {
    if (!this.selectedElement) return;
    const changes = {
      PositionXOffset: numericInput(this.elements.positionX),
      PositionYOffset: numericInput(this.elements.positionY),
      MarginLeft: numericInput(this.elements.marginLeft),
      MarginTop: numericInput(this.elements.marginTop),
      MarginRight: numericInput(this.elements.marginRight),
      MarginBottom: numericInput(this.elements.marginBottom)
    };
    const widthPolicy = this.selectedElement.__gauntlet.attributes.WidthSizePolicy || "Fixed";
    const heightPolicy = this.selectedElement.__gauntlet.attributes.HeightSizePolicy || "Fixed";
    if (widthPolicy !== "StretchToParent") changes.SuggestedWidth = Math.max(1, numericInput(this.elements.width));
    if (heightPolicy !== "StretchToParent") changes.SuggestedHeight = Math.max(1, numericInput(this.elements.height));
    if (widthPolicy === "CoverChildren") changes.WidthSizePolicy = "Fixed";
    if (heightPolicy === "CoverChildren") changes.HeightSizePolicy = "Fixed";
    this.commit(changes, "Apply exact values");
  }

  commit(changes, label, targetElement = this.selectedElement) {
    const metadata = targetElement?.__gauntlet;
    if (!metadata || !Object.keys(changes).length) return;
    const meaningful = Object.fromEntries(Object.entries(normalizeAttributes(changes)).filter(([name, value]) => {
      const before = metadata.attributes[name];
      if (before == null || before === "") return !(typeof value === "number" && value === 0);
      const beforeNumber = Number(before);
      return typeof value === "number" && Number.isFinite(beforeNumber) ? Math.abs(beforeNumber - value) > 0.0001 : String(before) !== String(value);
    }));
    if (!Object.keys(meaningful).length) return;
    this.undoStack.push(this.snapshotEdits());
    this.redoStack.length = 0;
    const edits = this.getEdits();
    const current = edits.get(metadata.path) || {
      operation: "attributes",
      path: metadata.path,
      tag: metadata.tag,
      id: metadata.id,
      line: metadata.line,
      sourceOffset: metadata.source?.offset ?? null,
      originalAttributes: { ...metadata.attributes },
      attributes: {}
    };
    current.operation = "attributes";
    Object.assign(current.attributes, meaningful);
    edits.set(metadata.path, current);
    this.updateHistoryButtons();
    this.feedback(`${label} recorded for ${metadata.id || metadata.tag}.`);
    this.requestRender();
  }

  undo() {
    if (!this.undoStack.length) return;
    this.redoStack.push(this.snapshotEdits());
    this.restoreEdits(this.undoStack.pop());
    this.updateHistoryButtons();
    this.requestRender();
  }

  redo() {
    if (!this.redoStack.length) return;
    this.undoStack.push(this.snapshotEdits());
    this.restoreEdits(this.redoStack.pop());
    this.updateHistoryButtons();
    this.requestRender();
  }

  handleKeyDown(event) {
    if (isFormField(event.target)) return;
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
      event.preventDefault();
      event.shiftKey ? this.redo() : this.undo();
      return;
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") {
      event.preventDefault();
      this.redo();
      return;
    }
    if (event.key === "Delete" && this.selectedElement) {
      event.preventDefault();
      this.deleteSelection();
      return;
    }
    if (!this.elements.editMode.checked || !this.selectedElement) return;
    const deltas = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (!deltas[event.key]) return;
    event.preventDefault();
    const multiplier = event.shiftKey ? 10 : Math.max(1, Number(this.elements.snap.value) || 1);
    const [dx, dy] = deltas[event.key];
    const targetElement = this.getMovementTarget(event.altKey);
    const attributes = targetElement.__gauntlet.attributes;
    this.commit({
      PositionXOffset: numberAttribute(attributes, "PositionXOffset") + dx * multiplier,
      PositionYOffset: numberAttribute(attributes, "PositionYOffset") + dy * multiplier
    }, "Nudge", targetElement);
  }

  deleteSelection() {
    const metadata = this.selectedElement?.__gauntlet;
    if (!metadata) return;
    this.undoStack.push(this.snapshotEdits());
    this.redoStack.length = 0;
    const edits = this.getEdits();
    for (const path of [...edits.keys()]) {
      if (path.startsWith(`${metadata.path}/`)) edits.delete(path);
    }
    edits.set(metadata.path, {
      operation: "delete",
      path: metadata.path,
      tag: metadata.tag,
      id: metadata.id,
      line: metadata.line,
      sourceOffset: metadata.source?.offset ?? null,
      instanceTrail: [...metadata.instanceTrail],
      originalAttributes: { ...metadata.attributes },
      attributes: {}
    });
    this.updateHistoryButtons();
    const repeated = metadata.instanceTrail.length ? " The source template is shared, so every rendered instance of this node is removed." : "";
    this.feedback(`Deletion staged for ${metadata.id || metadata.tag}.${repeated}`);
    this.requestRender();
  }

  populateInputs() {
    if (!this.selectedElement) return;
    const attributes = this.selectedElement.__gauntlet.attributes;
    this.elements.positionX.value = numberAttribute(attributes, "PositionXOffset");
    this.elements.positionY.value = numberAttribute(attributes, "PositionYOffset");
    this.elements.width.value = numberAttribute(attributes, "SuggestedWidth", this.selectedElement.offsetWidth);
    this.elements.height.value = numberAttribute(attributes, "SuggestedHeight", this.selectedElement.offsetHeight);
    this.elements.marginLeft.value = numberAttribute(attributes, "MarginLeft");
    this.elements.marginTop.value = numberAttribute(attributes, "MarginTop");
    this.elements.marginRight.value = numberAttribute(attributes, "MarginRight");
    this.elements.marginBottom.value = numberAttribute(attributes, "MarginBottom");
    this.refreshPatchReview();
  }

  refreshOverlay() {
    const visible = this.elements.editMode.checked && this.selectedElement?.isConnected;
    this.elements.overlay.hidden = !visible;
    if (!visible) return;
    const stageRect = this.stage.getBoundingClientRect();
    const overlayElement = this.drag?.targetElement?.isConnected ? this.drag.targetElement : this.selectedElement;
    const rect = overlayElement.getBoundingClientRect();
    const fitScale = stageRect.width / Math.max(1, this.stage.offsetWidth);
    setRect(this.elements.overlay, {
      x: (rect.left - stageRect.left) / fitScale,
      y: (rect.top - stageRect.top) / fitScale,
      width: rect.width / fitScale,
      height: rect.height / fitScale
    });
    const label = this.elements.overlay.querySelector(".calibration-size-label");
    label.textContent = `${overlayElement.offsetWidth} × ${overlayElement.offsetHeight}${overlayElement === this.selectedElement ? "" : ` · moving parent ${overlayElement.__gauntlet?.id || overlayElement.__gauntlet?.tag || "widget"}`}`;
  }

  async importRuntimeSnapshot(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    try {
      const snapshot = JSON.parse(await file.text());
      this.setRuntimeSnapshot(snapshot);
    } catch (error) {
      this.feedback(`Could not import runtime snapshot: ${error.message}`, true);
    } finally {
      event.target.value = "";
    }
  }

  setRuntimeSnapshot(snapshot, { announce = true } = {}) {
    if (snapshot?.schema !== "reign-ui-runtime-snapshot-v1" || !Array.isArray(snapshot.widgets)) {
      throw new Error("Unsupported runtime snapshot schema.");
    }
    this.runtimeSnapshot = snapshot;
    this.elements.applyRuntimeEdits.disabled = !snapshot.widgets.some((row) => row.changes && Object.keys(row.changes).length);
    if (announce) this.feedback(`Loaded live snapshot for ${snapshot.movieName || "Reign UI"}.`);
    this.refreshRuntimeMeasurement();
  }

  clearRuntimeSnapshot() {
    this.runtimeSnapshot = null;
    this.elements.applyRuntimeEdits.disabled = true;
    this.refreshRuntimeMeasurement();
  }

  importRuntimeEdits() {
    const changed = (this.runtimeSnapshot?.widgets || []).filter((row) => row.changes && Object.keys(row.changes).length);
    if (!changed.length) return;
    const edits = this.getEdits();
    const planned = [];
    for (const row of changed) {
      const matches = this.renderer.renderedElements.filter((element) => row.id && element.__gauntlet?.id === row.id);
      if (matches.length !== 1) continue;
      planned.push({ row, metadata: matches[0].__gauntlet });
    }
    if (!planned.length) {
      this.feedback("No live changes had a unique XML Id match in this prefab.", true);
      return;
    }
    this.undoStack.push(this.snapshotEdits());
    this.redoStack.length = 0;
    for (const { row, metadata } of planned) {
      const current = edits.get(metadata.path) || {
        operation: "attributes",
        path: metadata.path,
        tag: metadata.tag,
        id: metadata.id,
        line: metadata.line,
        sourceOffset: metadata.source?.offset ?? null,
        originalAttributes: { ...metadata.attributes },
        attributes: {}
      };
      current.operation = "attributes";
      Object.assign(current.attributes, normalizeAttributes(row.changes));
      edits.set(metadata.path, current);
    }
    this.updateHistoryButtons();
    this.feedback(`Imported ${planned.length} live widget adjustment${planned.length === 1 ? "" : "s"}; unmatched or duplicate Ids were left unchanged.`);
    this.requestRender();
  }

  refreshRuntimeMeasurement() {
    const metadata = this.selectedElement?.__gauntlet;
    const widgets = this.runtimeSnapshot?.widgets || [];
    const matches = metadata ? widgets.filter((row) => metadata.id ? row.id === metadata.id : row.path === metadata.path) : [];
    const row = matches.length === 1 ? matches[0] : matches.find((candidate) => candidate.path === metadata?.path);
    if (!row || !this.selectedElement) {
      this.elements.runtimeText.textContent = this.runtimeSnapshot ? "No unique runtime widget matches this XML identity." : "No matching runtime snapshot loaded.";
      this.elements.runtimeOverlay.hidden = true;
      return;
    }
    this.elements.runtimeText.textContent = `${round(row.logicalX)}, ${round(row.logicalY)} · ${round(row.logicalWidth)} × ${round(row.logicalHeight)} logical px · ${row.widthPolicy}/${row.heightPolicy}`;
    const stageRect = this.stage.getBoundingClientRect();
    const fitScale = stageRect.width / Math.max(1, this.stage.offsetWidth);
    const selectedRect = this.selectedElement.getBoundingClientRect();
    const renderScale = selectedRect.width / Math.max(1, this.selectedElement.offsetWidth * fitScale);
    setRect(this.elements.runtimeOverlay, {
      x: row.logicalX * renderScale,
      y: row.logicalY * renderScale,
      width: row.logicalWidth * renderScale,
      height: row.logicalHeight * renderScale
    });
    this.elements.runtimeOverlay.hidden = false;
  }

  importReference(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    this.clearReference();
    this.referenceObjectUrl = URL.createObjectURL(file);
    this.referenceSource = "local-file";
    this.elements.reference.src = this.referenceObjectUrl;
    this.elements.reference.hidden = false;
    this.elements.clearReference.disabled = false;
    this.updateReferenceStyle();
    event.target.value = "";
  }

  setReferenceUrl(url, { visible = true, source = "native-capture" } = {}) {
    this.clearReference();
    if (!url) return;
    this.referenceSource = source;
    this.elements.reference.src = url;
    this.elements.reference.hidden = !visible;
    this.elements.clearReference.disabled = false;
    this.updateReferenceStyle();
  }

  setReferenceVisible(visible) {
    if (!this.elements.reference.getAttribute("src")) return;
    this.elements.reference.hidden = !visible;
  }

  updateReferenceStyle() {
    this.elements.reference.style.opacity = String((Number(this.elements.referenceOpacity.value) || 0) / 100);
    this.elements.reference.classList.toggle("difference", this.elements.difference.checked);
  }

  clearReference() {
    if (this.referenceObjectUrl) URL.revokeObjectURL(this.referenceObjectUrl);
    this.referenceObjectUrl = "";
    this.referenceSource = "";
    this.elements.reference.removeAttribute("src");
    this.elements.reference.hidden = true;
    this.elements.clearReference.disabled = true;
  }

  async buildPatch() {
    const sourceSha256 = await sha256(this.sourceXml);
    const surface = this.getSurfaceState();
    return {
      schema: PATCH_SCHEMA,
      createdUtc: new Date().toISOString(),
      fileName: this.fileName,
      sourceSha256,
      viewport: surface.viewport,
      uiScale: surface.uiScale,
      runtimeScale: surface.runtimeScale,
      edits: [...this.getEdits().values()].map((edit) => structuredCloneSafe(edit))
    };
  }

  async savePatch() {
    try {
      const patch = await this.buildPatch();
      const response = await fetch("api/calibration/save", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(patch) });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error || `${response.status} ${response.statusText}`);
      this.feedback(`Saved non-destructive patch: ${result.path}`);
    } catch (error) {
      this.feedback(`Could not save patch: ${error.message}`, true);
    }
  }

  async downloadPatch() {
    const patch = await this.buildPatch();
    const blob = new Blob([JSON.stringify(patch, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${this.fileName.replace(/\.xml$/i, "")}.calibration.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  async applyToSource(installInGame) {
    if (!this.getEdits().size) return;
    const destination = installInGame ? "the workspace source and installed game module" : "the workspace source";
    const confirmed = window.confirm(`Apply ${this.getEdits().size} calibrated widget change(s) to ${destination}?\n\nHash guards, timestamped backups, and verified atomic replacement protect concurrent work. If Bannerlord is running, close and reopen the affected interface after installation.`);
    if (!confirmed) return;
    let appliedResult = null;
    try {
      const patch = await this.buildPatch();
      patch.confirmation = installInGame ? "apply and install Reign XML calibration" : "apply Reign XML calibration";
      const endpoint = installInGame ? "api/calibration/apply-and-install" : "api/calibration/apply";
      const response = await fetch(endpoint, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(patch) });
      const result = await response.json();
      if (result.sourceApplied && (!response.ok || !result.ok)) {
        appliedResult = result;
        this.undoStack.length = 0;
        this.redoStack.length = 0;
        this.editsByFile.set(this.fileName, new Map());
        this.updateHistoryButtons();
        this.refreshPatchReview();
        if (this.onSourceApplied) await this.onSourceApplied(result);
        await this.refreshInstallStatus();
        this.feedback(`Applied ${result.applied} XML change(s) to workspace source, but the installed game file was not changed: ${result.error}`, true);
        return;
      }
      if (!response.ok || !result.ok) throw new Error(result.error || `${response.status} ${response.statusText}`);
      appliedResult = result;
      this.undoStack.length = 0;
      this.redoStack.length = 0;
      this.editsByFile.set(this.fileName, new Map());
      this.updateHistoryButtons();
      this.refreshPatchReview();
      if (this.onSourceApplied) await this.onSourceApplied(result);
      await this.refreshInstallStatus();
      const installed = installInGame ? " Installed hash verified; close and reopen the interface in Bannerlord to reload the movie." : "";
      this.feedback(`Applied ${result.applied} XML change(s) and reloaded the latest source. Backup: ${result.backup}.${installed}`);
    } catch (error) {
      const prefix = appliedResult ? "Source changed, but the preview reload failed" : "Source was not changed";
      this.feedback(`${prefix}: ${error.message}`, true);
    }
  }

  async installCurrentSource() {
    if (!this.isCatalogOwned() || this.getEdits().size || this.sourceOutOfDate) return;
    const confirmed = window.confirm(`Install the current workspace ${this.fileName} into the Bannerlord ReignBeta module?\n\nThe installed XML is backed up and atomically replaced. If Bannerlord is running, close and reopen the affected interface afterward.`);
    if (!confirmed) return;
    try {
      const request = {
        fileName: this.fileName,
        sourceSha256: await sha256(this.sourceXml),
        confirmation: "install Reign XML preview change"
      };
      const response = await fetch("api/calibration/install", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(request) });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error || `${response.status} ${response.statusText}`);
      await this.refreshInstallStatus();
      this.feedback(`Installed ${this.fileName} with hash verification. Close and reopen that interface in Bannerlord to load it.`);
    } catch (error) {
      this.feedback(`Game XML was not changed: ${error.message}`, true);
      await this.refreshInstallStatus();
    }
  }

  async refreshInstallStatus() {
    const supported = /^[A-Za-z0-9_.-]+\.xml$/i.test(this.fileName);
    if (!supported) {
      this.installSync = null;
      this.elements.installStatus.textContent = "This document is not a catalog-owned Reign prefab.";
      await this.refreshInstallSummary();
      this.updateInstallButtons();
      return;
    }
    this.elements.installStatus.textContent = "Checking workspace and installed hashes…";
    try {
      const url = new URL("api/prefab-sync", document.baseURI);
      url.searchParams.set("file", this.fileName);
      const response = await fetch(url, { cache: "no-store" });
      const result = await response.json();
      if (!response.ok || result.ok === false) throw new Error(result.error || `${response.status} ${response.statusText}`);
      this.installSync = result;
      if (!result.installedModuleExists) this.elements.installStatus.textContent = "Installed ReignBeta module not found.";
      else if (result.inSync) this.elements.installStatus.textContent = `Installed matches workspace · ${shortHash(result.sourceSha256)}`;
      else if (!result.installedExists) this.elements.installStatus.textContent = "Prefab is missing from the installed module.";
      else this.elements.installStatus.textContent = `Installed copy is stale · workspace ${shortHash(result.sourceSha256)} / game ${shortHash(result.installedSha256)}`;
      if (result.bannerlordRunning) this.elements.installStatus.textContent += " · Bannerlord is running (install available; reopen the interface afterward)";
    } catch (error) {
      this.installSync = null;
      this.elements.installStatus.textContent = `Install status unavailable: ${error.message}`;
    }
    await this.refreshInstallSummary();
    this.updateInstallButtons();
  }

  async refreshInstallSummary() {
    this.elements.installSummary.textContent = "Checking every Reign UI prefab…";
    try {
      const response = await fetch("api/prefab-sync-all", { cache: "no-store" });
      const result = await response.json();
      if (!response.ok || result.ok === false) throw new Error(result.error || `${response.status} ${response.statusText}`);
      this.installSummary = result;
      const parts = [`${result.matchingCount}/${result.prefabCount} Reign UI prefabs match the installed module`];
      if (result.staleCount) parts.push(`${result.staleCount} stale`);
      if (result.missingCount) parts.push(`${result.missingCount} missing`);
      if (result.bannerlordRunning) parts.push("live install available; reopen the affected interface afterward");
      this.elements.installSummary.textContent = parts.join(" · ");
      const mismatches = (result.prefabs || []).filter((row) => !row.inSync).map((row) => row.fileName);
      this.elements.installSummary.title = mismatches.length ? `Not synchronized: ${mismatches.join(", ")}` : "All discovered Reign UI prefabs are synchronized.";
    } catch (error) {
      this.installSummary = null;
      this.elements.installSummary.textContent = `Full prefab sync unavailable: ${error.message}`;
      this.elements.installSummary.title = "";
    }
  }

  updateInstallButtons() {
    const tracked = this.isCatalogOwned();
    const dirty = this.getEdits().size > 0;
    const locked = !this.installSync?.installedModuleExists;
    this.elements.installSource.disabled = !tracked || dirty || this.sourceOutOfDate || locked || Boolean(this.installSync?.inSync);
    this.elements.applyGame.disabled = !tracked || !dirty || this.sourceOutOfDate || locked;
  }

  refreshPatchReview() {
    const edits = [...this.getEdits().values()];
    const lines = [];
    for (const edit of edits) {
      if (edit.operation === "delete") {
        lines.push(`DELETE ${edit.tag}${edit.id ? `#${edit.id}` : ""} · ${edit.path}`);
        if (edit.instanceTrail?.length) lines.push(`  removes the shared source node for every rendered instance (selected: ${edit.instanceTrail.join(" / ")})`);
        continue;
      }
      lines.push(`${edit.tag}${edit.id ? `#${edit.id}` : ""} · ${edit.path}`);
      for (const [name, value] of Object.entries(edit.attributes)) {
        const before = edit.originalAttributes[name] ?? "(unset)";
        lines.push(`  ${name}: ${before} -> ${value}`);
      }
    }
    this.elements.patchReview.value = lines.join("\n");
    const dirty = edits.length > 0;
    this.elements.dirtyBadge.textContent = dirty ? `${edits.length} change${edits.length === 1 ? "" : "s"}` : "clean";
    this.elements.savePatch.disabled = !dirty;
    this.elements.downloadPatch.disabled = !dirty;
    this.elements.applySource.disabled = !dirty || this.sourceOutOfDate || !this.isCatalogOwned();
    this.updateInstallButtons();
  }

  isCatalogOwned() {
    return Boolean(this.installSync?.catalogOwned);
  }

  getEdits() {
    if (!this.editsByFile.has(this.fileName)) this.editsByFile.set(this.fileName, new Map());
    return this.editsByFile.get(this.fileName);
  }

  snapshotEdits() {
    return [...this.getEdits().entries()].map(([key, value]) => [key, structuredCloneSafe(value)]);
  }

  restoreEdits(snapshot) {
    this.editsByFile.set(this.fileName, new Map(snapshot.map(([key, value]) => [key, structuredCloneSafe(value)])));
    this.refreshPatchReview();
  }

  updateHistoryButtons() {
    this.elements.undo.disabled = this.undoStack.length === 0;
    this.elements.redo.disabled = this.redoStack.length === 0;
    this.refreshPatchReview();
  }

  feedback(message, isError = false) {
    this.elements.feedback.textContent = message;
    this.elements.feedback.classList.toggle("bad", isError);
    this.setStatus?.(message, isError ? "bad" : "ok");
  }
}

function collectElements() {
  const byId = (id) => document.getElementById(id);
  return {
    editMode: byId("editModeToggle"), aspectLock: byId("aspectLockToggle"), snap: byId("snapSelect"),
    undo: byId("undoCalibration"), redo: byId("redoCalibration"), runtimeInput: byId("runtimeSnapshotInput"), applyRuntimeEdits: byId("applyRuntimeEdits"),
    referenceInput: byId("referenceImageInput"), referenceOpacity: byId("referenceOpacity"),
    difference: byId("differenceToggle"), clearReference: byId("clearReference"), reference: byId("referenceOverlay"),
    overlay: byId("calibrationOverlay"), runtimeOverlay: byId("runtimeMeasurementOverlay"), inspector: byId("calibrationInspector"),
    positionX: byId("editPositionX"), positionY: byId("editPositionY"), width: byId("editWidth"), height: byId("editHeight"),
    marginLeft: byId("editMarginLeft"), marginTop: byId("editMarginTop"), marginRight: byId("editMarginRight"), marginBottom: byId("editMarginBottom"),
    applyValues: byId("applyCalibrationValues"), runtimeText: byId("runtimeMeasurementText"), patchReview: byId("calibrationPatchReview"),
    dirtyBadge: byId("calibrationDirtyBadge"), savePatch: byId("saveCalibrationPatch"), downloadPatch: byId("downloadCalibrationPatch"),
    applySource: byId("applyCalibrationSource"), applyGame: byId("applyCalibrationGame"), feedback: byId("calibrationFeedback"),
    installSummary: byId("prefabInstallSummary"), installStatus: byId("prefabInstallStatus"), refreshInstall: byId("refreshPrefabInstallStatus"), installSource: byId("installCurrentPrefab")
  };
}

function findByPath(documentNode, path) {
  const segments = String(path || "").split("/").filter(Boolean);
  let current = documentNode;
  for (const segment of segments) {
    const match = segment.match(/^([^[]+)\[(\d+)]$/);
    if (!match) return null;
    const children = Array.from(current.childNodes).filter((node) => node.nodeType === 1 && node.nodeName === match[1]);
    current = children[Number(match[2]) - 1];
    if (!current) return null;
  }
  return current;
}

function normalizeAttributes(changes) {
  return Object.fromEntries(Object.entries(changes).map(([name, value]) => [name, round(value)]));
}

function numberAttribute(attributes, name, fallback = 0) {
  const value = Number(attributes?.[name]);
  return Number.isFinite(value) ? value : fallback;
}

function numericInput(input) {
  const value = Number(input.value);
  return Number.isFinite(value) ? value : 0;
}

function snapValue(value, increment) {
  return Math.round(value / increment) * increment;
}

function round(value) {
  const number = Number(value);
  return Number.isFinite(number) ? Math.round(number * 1000) / 1000 : value;
}

function shortHash(value) {
  return String(value || "").slice(0, 12) || "missing";
}

function setRect(element, rect) {
  element.style.left = `${rect.x}px`;
  element.style.top = `${rect.y}px`;
  element.style.width = `${Math.max(1, rect.width)}px`;
  element.style.height = `${Math.max(1, rect.height)}px`;
}

function fingerprint(text) {
  let hash = 2166136261;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return `${text.length}:${hash >>> 0}`;
}

async function sha256(text) {
  const bytes = new TextEncoder().encode(text);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function structuredCloneSafe(value) {
  return JSON.parse(JSON.stringify(value));
}

function isFormField(target) {
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement;
}
