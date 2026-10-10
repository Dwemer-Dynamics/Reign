// Candidates have already passed visibility, aspect, and fixed-size checks.
// Explicit compact owning artwork uses its declared aspect ratio; automatic
// discovery retains the existing full 16:9 canvas restrictions.
export function isReferenceSurfaceCandidate(candidate, requestedId = "") {
  const { attributes, rect, style, metadata } = candidate;
  // A registered nine-region owning plate is intentionally resizable. Accept
  // only an explicitly declared surface with runtime-verified sprite metadata;
  // ordinary stretched widgets cannot become fidelity authority by accident.
  if (requestedId && metadata.id === requestedId && attributes.WidthSizePolicy === "StretchToParent"
    && attributes.HeightSizePolicy === "StretchToParent" && candidate.element?.classList?.contains("runtime-nine-region")) {
    return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity || 1) > 0
      && rect.width > 0 && rect.height > 0;
  }
  if (attributes.WidthSizePolicy !== "Fixed" || attributes.HeightSizePolicy !== "Fixed"
    || style.display === "none" || style.visibility === "hidden" || Number(style.opacity || 1) <= 0) return false;
  if (requestedId) {
    const width = Number(attributes.SuggestedWidth), height = Number(attributes.SuggestedHeight);
    return metadata.id === requestedId && width > 0 && height > 0 && rect.width > 0 && rect.height > 0
      && Math.abs(rect.width / rect.height - width / height) <= .03;
  }
  return rect.width >= 350 && rect.height >= 190 && Math.abs(rect.width / rect.height - 16 / 9) <= .03;
}

// A declared owning artwork must resolve uniquely; falling back would compare
// the wrong canvas and conceal an incorrect popup scale or missing asset.
export function selectReferenceSurface(candidates, requestedId = "") {
  if (!requestedId) return candidates[0] || null;
  const matches = candidates.filter(candidate => candidate.metadata.id === requestedId);
  if (matches.length !== 1) throw new Error(`Reference surface ${requestedId} requires one visible fixed candidate; found ${matches.length}.`);
  return matches[0];
}

export function selectRenderedPreviewTargets(catalog, requestedValue) {
  const allInterfaces = catalog.interfaces.flatMap((entry) => [entry,
    ...(entry.previewStates || []).map((state) => ({ ...entry, ...state, parentInterfaceId: entry.id, previewState: true }))
  ]).sort((left, right) => left.id.localeCompare(right.id));
  const allAugmentations = [...(catalog.nativeAugmentations?.targets || [])].sort((left, right) => left.target.localeCompare(right.target));
  const partialScope = requestedValue !== undefined;
  const requestedTargets = partialScope ? requestedValue.split(",").map((value) => value.trim()) : [];
  if (partialScope && requestedTargets.some((value) => !value)) throw new Error("--targets requires non-empty catalog IDs.");
  if (new Set(requestedTargets).size !== requestedTargets.length) throw new Error("--targets contains duplicate catalog IDs.");
  const knownIds = new Set([...allInterfaces.map((entry) => entry.id), ...allAugmentations.map((entry) => entry.target)]);
  for (const id of requestedTargets) if (!knownIds.has(id)) throw new Error(`Unknown preview target: ${id}`);
  return {
    partialScope, requestedTargets, catalogInterfaceStateCount: allInterfaces.length, catalogAugmentationCount: allAugmentations.length,
    catalogPrefabCount: new Set(allInterfaces.map((entry) => entry.prefab.split("/").at(-1))).size,
    interfaces: allInterfaces.filter((entry) => !partialScope || requestedTargets.includes(entry.id) || requestedTargets.includes(entry.parentInterfaceId)),
    augmentations: allAugmentations.filter((entry) => !partialScope || requestedTargets.includes(entry.target))
  };
}
