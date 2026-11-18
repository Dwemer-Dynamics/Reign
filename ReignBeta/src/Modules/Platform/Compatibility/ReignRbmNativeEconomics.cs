using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.ObjectSystem;
using ReignBeta.Platform.Integration;

namespace ReignBeta.Platform.Compatibility
{
    internal static class ReignRbmNativeEconomics
    {
        internal sealed class Baseline
        {
            internal int Value;
            internal float Tier, Appearance;
            internal ItemCategory Category;
            internal BasicCultureObject Culture;
            internal ItemModifierGroup ModifierGroup;
            internal bool NotMerchandise, Multiplayer, Food;

            internal void Apply(ItemObject item)
            {
                Set(item, "Value", Value);
                Set(item, "ItemCategory", Category);
                Set(item, "Appearance", Appearance);
                Set(item, "Culture", Culture);
                Set(item, "NotMerchandise", NotMerchandise);
                Set(item, "MultiplayerItem", Multiplayer);
                Set(item, "IsFood", Food);
                if (item.ItemComponent != null) Set(item.ItemComponent, "ItemModifierGroup", ModifierGroup);
            }
        }

        private sealed class CraftedCache { internal WeaponDesign Design; internal Baseline Baseline; }
        private static Func<string[]> OfficialModules = () => ModuleHelper.GetModules((Func<ModuleInfo, bool>)null)
            .Where(module => module.IsOfficial).Select(module => module.Id).Distinct(StringComparer.Ordinal).ToArray();
        private static readonly string[] EconomicModifierAttributes = { "price_factor", "loot_drop_score", "production_drop_score", "modifier_group", "quality" };
        private static Dictionary<string, XmlElement> Items = NewXmlMap(), Pieces = NewXmlMap(), Templates = NewXmlMap(), Descriptions = NewXmlMap(), Modifiers = NewXmlMap();
        private static readonly Dictionary<string, Dictionary<string, XmlElement[]>> DefinitionGroups = new Dictionary<string, Dictionary<string, XmlElement[]>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Baseline> Values = new Dictionary<string, Baseline>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CraftingPiece> ShadowPieces = new Dictionary<string, CraftingPiece>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CraftingTemplate> ShadowTemplates = new Dictionary<string, CraftingTemplate>(StringComparer.Ordinal);
        private static readonly Dictionary<string, WeaponDescription> ShadowDescriptions = new Dictionary<string, WeaponDescription>(StringComparer.Ordinal);
        private static readonly HashSet<string> RbmItemIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Gaps = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static ConditionalWeakTable<ItemObject, CraftedCache> Crafted = new ConditionalWeakTable<ItemObject, CraftedCache>();
        private static Action<string> Report = message => ReignLog.Warn(message);
        private static Func<string, string> ModuleRoot = ResolveModuleRoot;
        private static Game _game;
        private static readonly object Sync = new object();
        private static bool _itemsCaptured, _piecesCaptured, _templatesCaptured, _descriptionsCaptured;
        [ThreadStatic] private static int _shadowDepth;
        [ThreadStatic] private static bool _nativeXml, _rbmXmlContext;
        internal static bool InShadow => _shadowDepth != 0;

        private static Dictionary<string, XmlElement> NewXmlMap() => new Dictionary<string, XmlElement>(StringComparer.Ordinal);

        internal static void Install(Harmony harmony)
        {
            Reverse(harmony, "CreateMergedXmlFile", nameof(NativeMergeFiles));
            Reverse(harmony, "MergeTwoXmls", nameof(NativeMergeDocuments));
            Reverse(harmony, "CreateDocumentFromXmlFile", nameof(NativeReadDocument));
            Reverse(harmony, "ApplyXslt", nameof(NativeTransform));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(MBObjectManager), "CreateMergedXmlFile"),
                prefix: Prefix(nameof(BeforeMergeFiles)), postfix: Last(nameof(AfterMergeFiles)), finalizer: Last(nameof(RestoreMergeContext)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(MBObjectManager), "MergeTwoXmls"), prefix: Prefix(nameof(BeforeMergeDocuments)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), nameof(Game.RegisterTypes)), prefix: Prefix(nameof(Reset)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), nameof(Game.ItemObjectDeserialized)), prefix: Prefix(nameof(BeforeItemEvent)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Crafting), nameof(Crafting.GenerateItem)), postfix: Last(nameof(AfterCraft)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemObject), "DetermineValue"), postfix: Last(nameof(AfterValue)));
            ReignRbmNativeCalculations.Install(harmony);
        }

        private static HarmonyMethod Prefix(string name) => new HarmonyMethod(typeof(ReignRbmNativeEconomics), name) {
            priority = int.MaxValue, before = new[] { "com.rbmmain", "com.rbm", "com.rbmcombat" }
        };
        private static HarmonyMethod Last(string name) => new HarmonyMethod(typeof(ReignRbmNativeEconomics), name) { priority = Priority.Last };

        private static void Reverse(Harmony harmony, string original, string standin)
        {
            MethodInfo method = AccessTools.DeclaredMethod(typeof(MBObjectManager), original);
            if (method == null) throw new MissingMethodException(typeof(MBObjectManager).FullName, original);
            harmony.CreateReversePatcher(method, new HarmonyMethod(typeof(ReignRbmNativeEconomics), standin)).Patch(HarmonyReversePatchType.Original);
        }

        [MethodImpl(MethodImplOptions.NoInlining)] private static XmlDocument NativeMergeFiles(List<Tuple<string, string>> files, List<string> transforms, bool skipValidation)
        {
            // Harmony discovers this local transpiler on the reverse stand-in.
            IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions) {
                    if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(MBObjectManager)) {
                        string replacement = method.Name == "MergeTwoXmls" ? nameof(NativeMergeDocuments)
                            : method.Name == "CreateDocumentFromXmlFile" ? nameof(NativeReadDocument)
                            : method.Name == "ApplyXslt" ? nameof(NativeTransform) : null;
                        if (replacement != null) instruction.operand = AccessTools.DeclaredMethod(typeof(ReignRbmNativeEconomics), replacement);
                    }
                    yield return instruction;
                }
            }
            _ = Redirect(null);
            throw new InvalidOperationException("Native XML merge has not been bound.");
        }
        [MethodImpl(MethodImplOptions.NoInlining)] private static XmlDocument NativeMergeDocuments(XmlDocument first, XmlDocument second, string schema, bool duplicates)
            => throw new InvalidOperationException("Native XML document merge has not been bound.");
        [MethodImpl(MethodImplOptions.NoInlining)] private static XmlDocument NativeReadDocument(string file, string schema, bool skipValidation)
            => throw new InvalidOperationException("Native XML reader has not been bound.");
        [MethodImpl(MethodImplOptions.NoInlining)] private static XmlDocument NativeTransform(string file, XmlDocument document)
            => throw new InvalidOperationException("Native XSLT has not been bound.");

        internal static void Reset()
        {
            lock (Sync) {
            Items.Clear(); Pieces.Clear(); Templates.Clear(); Descriptions.Clear(); Modifiers.Clear(); RbmItemIds.Clear();
            DefinitionGroups.Clear();
            ClearComputed(); Reported.Clear(); Gaps.Clear(); _game = null;
            _itemsCaptured = _piecesCaptured = _templatesCaptured = _descriptionsCaptured = false;
            }
        }

        private static void ClearComputed()
        {
            Values.Clear(); ShadowPieces.Clear(); ShadowTemplates.Clear(); ShadowDescriptions.Clear();
            Crafted = new ConditionalWeakTable<ItemObject, CraftedCache>();
        }

        private static bool InsideModule(string file, string module)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string root = ModuleRoot(module);
            if (string.IsNullOrEmpty(root)) return false;
            string full = Path.GetFullPath(file), directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveModuleRoot(string id)
        {
            // Absence is normal for optional DLC. A failure resolving an installed
            // module must propagate, rather than silently truncating the baseline.
            return ModuleHelper.GetModules((Func<ModuleInfo, bool>)null).Any(module => module.Id == id)
                ? ModuleHelper.GetModuleFullPath(id) : null;
        }

        internal static void BeforeMergeFiles(List<Tuple<string, string>> __0, List<string> __1, bool __2, ref bool __state)
        {
            __state = _rbmXmlContext;
            if (_nativeXml) return;
            try {
                if (__0 == null || __1 == null || __0.Count == 0 || __0.Count != __1.Count) throw new InvalidOperationException("Native XML input matrix is incomplete.");
                string first = __0.Select(f => f.Item1).FirstOrDefault(f => !string.IsNullOrEmpty(f));
                if (first == null) return;
                string collection;
                using (var reader = XmlReader.Create(first, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) {
                    reader.MoveToContent(); collection = reader.Name;
                }
                if (!new[] { "Items", "CraftingPieces", "CraftingTemplates", "WeaponDescriptions", "ItemModifiers" }.Contains(collection)) return;
                if (string.IsNullOrEmpty(ModuleRoot("Native"))) throw new InvalidOperationException("The Native module root could not be resolved.");
                string[] officialModules = OfficialModules();
                if (!officialModules.Contains("Native")) throw new InvalidOperationException("The official module catalogue does not include Native.");
                var files = new List<Tuple<string, string>>();
                var transforms = new List<string>();
                for (int i = 0; i < __0.Count; i++) {
                    string file = __0[i].Item1, transform = __1[i];
                    bool officialFile = officialModules.Any(id => InsideModule(file, id));
                    bool officialTransform = officialModules.Any(id => InsideModule(transform, id));
                    if (files.Count == 0 && !officialFile) continue;
                    files.Add(Tuple.Create(officialFile ? file : "", __0[i].Item2));
                    transforms.Add(officialTransform ? transform : "");
                }
                _rbmXmlContext = __0.Any(f => InsideModule(f.Item1, "RBM") || InsideModule(f.Item1, "RBM_WS"))
                    || __1.Any(f => InsideModule(f, "RBM") || InsideModule(f, "RBM_WS"));
                if (files.Count != 0) {
                    bool previous = _nativeXml;
                    try { _nativeXml = true; Capture(NativeMergeFiles(files, transforms, __2)); }
                    finally { _nativeXml = previous; }
                }
                else throw new InvalidOperationException("No verified official base XML for " + collection + "; refusing to classify every item as a mod-only exception.");
                if (_rbmXmlContext) {
                    foreach (var file in __0.Where(f => InsideModule(f.Item1, "RBM") || InsideModule(f.Item1, "RBM_WS"))) {
                        if (string.IsNullOrEmpty(file.Item1)) continue;
                        var document = new XmlDocument { XmlResolver = null }; document.Load(file.Item1);
                        foreach (XmlElement item in document.SelectNodes("/Items/Item | /Items/CraftedItem")) {
                            string id = item.GetAttribute("id"); if (id.Length != 0) RbmItemIds.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex) { Gap("xml-baseline", ex); }
        }

        private static Exception RestoreMergeContext(bool __state, Exception __exception)
        {
            _rbmXmlContext = __state;
            return __exception;
        }

        private static void AfterMergeFiles(ref XmlDocument __result)
        {
            if (!_rbmXmlContext || _nativeXml || __result?.DocumentElement == null) return;
            try {
                __result = OverlayEconomicXml(__result);
                if (__result.DocumentElement.Name == "Items" && _itemsCaptured)
                    foreach (XmlElement item in __result.DocumentElement.ChildNodes.OfType<XmlElement>()) {
                        string id = item.GetAttribute("id"); if (id.Length != 0 && !Items.ContainsKey(id)) RbmItemIds.Add(id);
                    }
            }
            catch (Exception ex) { Gap("final-xml-economic-overlay", ex); }
        }

        internal static bool BeforeMergeDocuments(XmlDocument __0, ref XmlDocument __1, string __2, bool __3, ref XmlDocument __result)
        {
            if (_nativeXml) { __result = NativeMergeDocuments(__0, __1, __2, __3); return false; }
            if (!_rbmXmlContext || __1?.DocumentElement == null || !__1.OuterXml.Contains("RBM_XML_TAG")) return true;
            try {
                __1 = OverlayEconomicXml(__1);
            }
            catch (Exception ex) { Gap("xml-economic-overlay", ex); }
            return true;
        }

        private static XmlDocument OverlayEconomicXml(XmlDocument source)
        {
                var copy = (XmlDocument)source.CloneNode(true);
                foreach (XmlElement modifier in copy.SelectNodes("//ItemModifier[@id]")) {
                    if (!Modifiers.TryGetValue(modifier.GetAttribute("id"), out var native)) continue;
                    foreach (string attribute in EconomicModifierAttributes) CopyAttribute(native, modifier, attribute);
                }
                // Restore native crafting costs/classification, retaining blade physics.
                foreach (XmlElement piece in copy.SelectNodes("//CraftingPiece[@id]")) {
                    if (!Pieces.TryGetValue(piece.GetAttribute("id"), out var native)) continue;
                    foreach (string attribute in new[] { "tier", "appearance", "CraftingCost" }) CopyAttribute(native, piece, attribute);
                    ReplaceChild(native, piece, "Materials");
                }
                return copy;
        }

        private static void CopyAttribute(XmlElement source, XmlElement target, string name)
        {
            if (source.HasAttribute(name)) target.SetAttribute(name, source.GetAttribute(name)); else target.RemoveAttribute(name);
        }

        private static void ReplaceChild(XmlElement source, XmlElement target, string name)
        {
            foreach (XmlNode existing in target.SelectNodes(name).Cast<XmlNode>().ToArray()) target.RemoveChild(existing);
            foreach (XmlNode original in source.SelectNodes(name)) target.AppendChild(target.OwnerDocument.ImportNode(original, true));
        }

        internal static void Capture(XmlDocument document)
        {
            if (document?.DocumentElement == null) return;
            string root = document.DocumentElement.Name;
            if (!new[] { "Items", "CraftingPieces", "CraftingTemplates", "WeaponDescriptions", "ItemModifiers" }.Contains(root)) return;
            var rows = document.DocumentElement.ChildNodes.OfType<XmlElement>().Where(e => e.HasAttribute("id"))
                .GroupBy(e => e.GetAttribute("id"), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => (XmlElement)e.CloneNode(true)).ToArray(), StringComparer.Ordinal);
            var map = rows.ToDictionary(e => e.Key, e => e.Value.Last(), StringComparer.Ordinal);
            // Publish a complete collection atomically. Duplicate ID rows are replayed
            // in native order, rather than assuming the last row resets every field.
            lock (Sync) {
            DefinitionGroups[root] = rows;
            if (root == "Items") { Items = map; _itemsCaptured = true; }
            if (root == "CraftingPieces") { Pieces = map; _piecesCaptured = true; }
            if (root == "CraftingTemplates") { Templates = map; _templatesCaptured = true; }
            if (root == "WeaponDescriptions") { Descriptions = map; _descriptionsCaptured = true; }
            if (root == "ItemModifiers") Modifiers = map;
            // A new XML generation invalidates references computed from earlier data.
            ClearComputed();
            }
        }

        private static IEnumerable<XmlElement> Rows(XmlElement element)
        {
            string root = element.Name == "Item" || element.Name == "CraftedItem" ? "Items"
                : element.Name == "CraftingPiece" ? "CraftingPieces" : element.Name == "CraftingTemplate" ? "CraftingTemplates"
                : element.Name == "WeaponDescription" ? "WeaponDescriptions" : "ItemModifiers";
            return DefinitionGroups.TryGetValue(root, out var group) && group.TryGetValue(element.GetAttribute("id"), out var rows)
                ? rows : new[] { element };
        }

        private static bool BeforeItemEvent(ItemObject __0)
        {
            if (InShadow) return false; // No shadow object is published to game listeners.
            Apply(__0);
            return true;
        }
        private static void AfterCraft(ref ItemObject __4) { if (!InShadow) Apply(__4); }
        private static void AfterValue(ItemObject __instance) { if (!InShadow) Apply(__instance); }

        private static void Apply(ItemObject item)
        {
            try { if (TryGet(item, out var baseline)) baseline.Apply(item); }
            catch (Exception ex) { Gap(item?.StringId ?? "null-item", ex); }
        }

        internal static void ReconcileLoadedItems()
        {
            if (Game.Current == null || InShadow) return;
            // One finite pass at the normal load boundary, including saved crafts.
            // Never clear inventories, volunteers, rosters, IDs or campaign history.
            foreach (ItemObject item in Game.Current.ObjectManager.GetObjectTypeList<ItemObject>().ToArray()) Apply(item);
        }

        internal static bool TryGet(ItemObject item, out Baseline baseline)
        {
            lock (Sync) return TryGetCore(item, out baseline);
        }

        private static bool TryGetCore(ItemObject item, out Baseline baseline)
        {
            baseline = null;
            if (InShadow || item == null || Game.Current == null) return false;
            if (!ReferenceEquals(_game, Game.Current)) { ClearComputed(); _game = Game.Current; }
            string id = item.StringId ?? "";
            try {
                if (Values.TryGetValue(id, out baseline)) return true;
                if (Items.TryGetValue(id, out var definition)) {
                    baseline = Build(definition);
                    Values[id] = baseline;
                    Gaps.Remove(id);
                    return true;
                }
                if (item.IsCraftedWeapon) {
                    if (Crafted.TryGetValue(item, out var cached) && ReferenceEquals(cached.Design, item.WeaponDesign)) { baseline = cached.Baseline; return true; }
                    if (!_piecesCaptured || !_templatesCaptured || !_descriptionsCaptured)
                        throw new InvalidOperationException("The native crafting input collections have not been captured.");
                    if (!CanProject(item.WeaponDesign)) { Addition("crafted:" + item.WeaponDesign.Template?.StringId); return false; }
                    baseline = Build(item.WeaponDesign, item.Name, item.Culture, id);
                    Crafted.Remove(item); Crafted.Add(item, new CraftedCache { Design = item.WeaponDesign, Baseline = baseline });
                    Gaps.Remove(id);
                    return true;
                }
                if (RbmItemIds.Contains(id)) {
                    if (!_itemsCaptured) throw new InvalidOperationException("The official item baseline has not been captured; this is not a proven mod-only exception.");
                    Addition(id);
                }
                return false;
            }
            catch (Exception ex) { Gap(id, ex); baseline = null; return false; }
        }

        private static bool CanProject(WeaponDesign design)
        {
            return design?.Template != null && Templates.ContainsKey(design.Template.StringId)
                && design.UsedPieces != null && design.UsedPieces.All(p => p != null && (!p.IsValid || Pieces.ContainsKey(p.CraftingPiece.StringId)));
        }

        private static Baseline Build(XmlElement definition)
        {
            using (ShadowScope()) {
                ItemObject shadow = new ItemObject();
                foreach (XmlElement row in Rows(definition)) {
                if (row.Name == "CraftedItem") {
                    string template = row.GetAttribute("crafting_template");
                    var elements = new WeaponDesignElement[4];
                    foreach (XmlElement piece in row.SelectNodes("Pieces/Piece")) {
                        var kind = (CraftingPiece.PieceTypes)Enum.Parse(typeof(CraftingPiece.PieceTypes), piece.GetAttribute("Type"));
                        int scale = piece.HasAttribute("scale_factor") ? int.Parse(piece.GetAttribute("scale_factor")) : 100;
                        elements[(int)kind] = WeaponDesignElement.CreateUsablePiece(GetPiece(piece.GetAttribute("id")), scale);
                    }
                    FillInvalid(elements);
                    BasicCultureObject culture = ReadCulture(row);
                    var name = new TextObject(row.GetAttribute("name"));
                    CraftingTemplate nativeTemplate = GetTemplate(template);
                    ItemModifierGroup group = row.GetAttribute("has_modifier") == "false" ? null
                        : row.HasAttribute("modifier_group") ? Game.Current.ObjectManager.GetObject<ItemModifierGroup>(row.GetAttribute("modifier_group"))
                        : nativeTemplate.ItemModifierGroup;
                    if (row.HasAttribute("is_merchandise")) Set(shadow, "NotMerchandise", row.GetAttribute("is_merchandise") != "true");
                    if (row.HasAttribute("multiplayer_item")) Set(shadow, "MultiplayerItem", row.GetAttribute("multiplayer_item") == "true");
                    Generate(new WeaponDesign(nativeTemplate, name, elements, row.GetAttribute("id")), name, null, row.GetAttribute("id"), group, ref shadow);
                    if (row.HasAttribute("value")) Set(shadow, "Value", int.Parse(row.GetAttribute("value")));
                    if (row.HasAttribute("culture")) Set(shadow, "Culture", culture);
                }
                else {
                    PreflightReferences(row);
                    shadow.Deserialize(Game.Current.ObjectManager, row);
                }
                }
                return Describe(shadow);
            }
        }

        private static Baseline Build(WeaponDesign source, TextObject name, BasicCultureObject culture, string id)
        {
            using (ShadowScope()) {
                var elements = new WeaponDesignElement[source.UsedPieces.Length];
                for (int i = 0; i < elements.Length; i++) {
                    WeaponDesignElement piece = source.UsedPieces[i];
                    elements[i] = piece.IsValid ? WeaponDesignElement.CreateUsablePiece(GetPiece(piece.CraftingPiece.StringId), piece.ScalePercentage)
                        : WeaponDesignElement.GetInvalidPieceForType((CraftingPiece.PieceTypes)i);
                }
                var design = new WeaponDesign(GetTemplate(source.Template.StringId), name, elements, id);
                return Describe(Generate(design, name, culture, id, design.Template.ItemModifierGroup));
            }
        }

        private static void FillInvalid(WeaponDesignElement[] elements)
        {
            for (int i = 0; i < elements.Length; i++) if (elements[i] == null) elements[i] = WeaponDesignElement.GetInvalidPieceForType((CraftingPiece.PieceTypes)i);
        }

        private static ItemObject Generate(WeaponDesign design, TextObject name, BasicCultureObject culture, string id, ItemModifierGroup modifierGroup)
        {
            ItemObject result = new ItemObject();
            Generate(design, name, culture, id, modifierGroup, ref result);
            return result;
        }

        private static void Generate(WeaponDesign design, TextObject name, BasicCultureObject culture, string id, ItemModifierGroup modifierGroup, ref ItemObject result)
        {
            Crafting.GenerateItem(design, name, culture, modifierGroup, ref result, id);
            if (result?.WeaponComponent == null) throw new InvalidOperationException("Native design could not produce a corresponding weapon.");
        }

        private static Baseline Describe(ItemObject shadow)
        {
            return new Baseline { Value = shadow.Value, Tier = shadow.Tierf, Appearance = shadow.Appearance,
                Category = shadow.ItemCategory, Culture = shadow.Culture, NotMerchandise = shadow.NotMerchandise,
                Multiplayer = shadow.MultiplayerItem, Food = shadow.IsFood, ModifierGroup = shadow.ItemComponent?.ItemModifierGroup };
        }

        private static CraftingPiece GetPiece(string id)
        {
            if (ShadowPieces.TryGetValue(id, out var piece)) return piece;
            if (!Pieces.TryGetValue(id, out var definition)) throw new InvalidOperationException("Missing native crafting piece " + id);
            piece = new CraftingPiece();
            foreach (XmlElement row in Rows(definition)) {
                var copy = (XmlElement)row.CloneNode(true);
                // Native Deserialize otherwise registers templates and mutates their Pieces.
                foreach (XmlNode templates in copy.SelectNodes("CraftingTemplates").Cast<XmlNode>().ToArray()) copy.RemoveChild(templates);
                ReadCulture(copy);
                piece.Deserialize(Game.Current.ObjectManager, copy);
            }
            ShadowPieces.Add(id, piece);
            return piece;
        }

        private static WeaponDescription GetDescription(string id)
        {
            if (ShadowDescriptions.TryGetValue(id, out var description)) return description;
            if (!Descriptions.TryGetValue(id, out var definition)) throw new InvalidOperationException("Missing native weapon description " + id);
            description = new WeaponDescription();
            foreach (XmlElement row in Rows(definition)) {
                var copy = (XmlElement)row.CloneNode(true);
                foreach (XmlNode node in copy.SelectNodes("AvailablePieces").Cast<XmlNode>().ToArray()) copy.RemoveChild(node);
                description.Deserialize(Game.Current.ObjectManager, copy);
            }
            var available = new MBList<CraftingPiece>();
            XmlElement lastAvailable = Rows(definition).LastOrDefault(r => r.SelectSingleNode("AvailablePieces") != null);
            if (lastAvailable != null)
                foreach (XmlElement piece in lastAvailable.SelectNodes("AvailablePieces/*[@id]"))
                    if (Pieces.ContainsKey(piece.GetAttribute("id"))) available.Add(GetPiece(piece.GetAttribute("id")));
            AccessTools.Field(typeof(WeaponDescription), "_availablePieces").SetValue(description, available);
            ShadowDescriptions.Add(id, description);
            return description;
        }

        private static CraftingTemplate GetTemplate(string id)
        {
            if (ShadowTemplates.TryGetValue(id, out var template)) return template;
            if (!Templates.TryGetValue(id, out var definition)) throw new InvalidOperationException("Missing native crafting template " + id);
            template = new CraftingTemplate();
            foreach (XmlElement row in Rows(definition)) {
                var copy = (XmlElement)row.CloneNode(true);
                // Resolve the detached graph, not live RBM membership or UI stats.
                foreach (string child in new[] { "WeaponDescriptions", "UsablePieces", "StatsData" })
                    foreach (XmlNode node in copy.SelectNodes(child).Cast<XmlNode>().ToArray()) copy.RemoveChild(node);
                template.Deserialize(Game.Current.ObjectManager, copy);
            }
            template.Pieces.Clear();
            foreach (XmlElement row in Rows(definition))
                foreach (XmlElement piece in row.SelectNodes("UsablePieces/*[@piece_id]"))
                    if (Pieces.ContainsKey(piece.GetAttribute("piece_id"))) template.Pieces.Add(GetPiece(piece.GetAttribute("piece_id")));
            foreach (var entry in Pieces) {
                if (Rows(entry.Value).Any(row => row.SelectNodes("CraftingTemplates/*[@id]").Cast<XmlElement>().Any(t => t.GetAttribute("id") == id))) {
                    CraftingPiece piece = GetPiece(entry.Key);
                    if (!template.Pieces.Contains(piece)) template.Pieces.Add(piece);
                }
            }
            XmlElement lastDescriptions = Rows(definition).LastOrDefault(r => r.SelectSingleNode("WeaponDescriptions") != null);
            var descriptions = lastDescriptions == null ? new WeaponDescription[0]
                : lastDescriptions.SelectNodes("WeaponDescriptions/*[@id]").Cast<XmlElement>()
                    .Where(d => Descriptions.ContainsKey(d.GetAttribute("id"))).Select(d => GetDescription(d.GetAttribute("id"))).ToArray();
            Set(template, "WeaponDescriptions", descriptions);
            ShadowTemplates.Add(id, template);
            return template;
        }

        private static BasicCultureObject ReadCulture(XmlElement element)
        {
            string reference = element.GetAttribute("culture");
            if (reference.Length == 0) return null;
            string id = reference.StartsWith("Culture.", StringComparison.Ordinal) ? reference.Substring("Culture.".Length) : throw new InvalidOperationException("Unexpected native culture reference.");
            BasicCultureObject culture = Game.Current.ObjectManager.GetObject<BasicCultureObject>(id);
            if (culture == null) throw new InvalidOperationException("Native culture is not registered yet: " + id);
            return culture;
        }

        private static void PreflightReferences(XmlElement item)
        {
            ReadCulture(item);
            foreach (XmlElement horse in item.SelectNodes("ItemComponent/Horse[@monster]")) {
                string reference = horse.GetAttribute("monster");
                string id = reference.StartsWith("Monster.", StringComparison.Ordinal) ? reference.Substring("Monster.".Length) : "";
                if (id.Length == 0 || Game.Current.ObjectManager.GetObject<Monster>(id) == null)
                    throw new InvalidOperationException("Native horse monster is not registered yet: " + id);
            }
        }

        private sealed class Scope : IDisposable
        {
            internal Scope() { _shadowDepth++; }
            public void Dispose() { _shadowDepth--; }
        }

        private static IDisposable ShadowScope()
        {
            if (Game.Current?.BasicModels?.ItemValueModel?.GetType() != typeof(DefaultItemValueModel)
                || Game.Current.BasicModels.ItemCategorySelector?.GetType() != typeof(DefaultItemCategorySelector))
                throw new InvalidOperationException("Native economic projection requires the reviewed default basic models.");
            return new Scope();
        }

        private static void Set(object instance, string property, object value)
        {
            PropertyInfo member = AccessTools.Property(instance.GetType(), property);
            if (member?.GetSetMethod(true) == null) throw new MissingMemberException(instance.GetType().FullName, property);
            member.SetValue(instance, value, null);
        }

        private static void Gap(string id, Exception error)
        {
            lock (Sync) {
            Gaps.Add(id);
            if (Reported.Count < 256 && Reported.Add("gap:" + id)) Issue("Native RBM economic coverage is incomplete for " + id + ": " + error.Message);
            }
        }

        private static void Addition(string id)
        {
            lock (Sync) {
            if (Reported.Count < 256 && Reported.Add("addition:" + id)) Issue("RBM-only addition retains explicitly non-native pricing: " + id);
            }
        }
        private static void Issue(string message) { try { Report(message); } catch { } }

        internal static object Snapshot() => new { nativeDefinitions = Items.Count, pricedNativeItems = Values.Count,
            nativeCraftingPieces = Pieces.Count, nativeTemplates = Templates.Count, rbmDeclaredItems = RbmItemIds.Count,
            unresolved = Gaps.OrderBy(id => id, StringComparer.Ordinal).ToArray(), nativeAcceptancePending = true };
    }
}
