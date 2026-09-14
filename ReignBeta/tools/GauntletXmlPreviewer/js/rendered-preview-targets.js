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
