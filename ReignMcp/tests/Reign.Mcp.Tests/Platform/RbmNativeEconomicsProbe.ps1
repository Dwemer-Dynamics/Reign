param(
    [Parameter(Mandatory=$true)][string]$ClientAssembly,
    [Parameter(Mandatory=$true)][string]$BannerlordPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateSet('Campaign','CampaignStoryMode')][string]$GameType = 'Campaign',
    [ValidateSet('Reference','Patched')][string]$Mode = 'Reference'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$working = Join-Path $EvidenceDirectory ($Mode + '\sandbox\bin\Win64_Shipping_Client')
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$workshop = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
$script:economicRoots = @([IO.Path]::GetDirectoryName($ClientAssembly), $gameBin)
# Match the game's explicit Harmony dependency before optional RBM payloads.
$script:economicRoots += Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'
$script:economicRoots += Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'
foreach ($module in @('Native','SandBoxCore','SandBox','StoryMode','CustomBattle','NavalDLC','RBM','Bannerlord.Harmony','Bannerlord.UIExtenderEx')) {
    $script:economicRoots += Join-Path $BannerlordPath ('Modules\' + $module + '\bin\Win64_Shipping_Client')
}
$script:economicRoots += Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:economicRoots) {
        $path = Join-Path $root $file
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$harmonyPath = @($script:economicRoots | ForEach-Object {Join-Path $_ '0Harmony.dll'} | Where-Object {Test-Path -LiteralPath $_})[0]
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll',$harmonyPath)
$references += [Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location
foreach ($assembly in @('TaleWorlds.Library','TaleWorlds.ObjectSystem','TaleWorlds.Localization','TaleWorlds.SaveSystem','TaleWorlds.DotNet','TaleWorlds.Core')) {
    $path = Join-Path $gameBin ($assembly + '.dll')
    [Reflection.Assembly]::LoadFrom($path) | Out-Null
    $references += $path
}
[Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
$harness = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Xml;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

public static class NativeEconomicFixture {
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] ModuleIds = { "Native", "SandBoxCore", "CustomBattle", "SandBox", "StoryMode", "NavalDLC" };
    private static readonly string[] Groups = { "ItemModifiers", "ItemModifierGroups", "CraftingPieces", "WeaponDescriptions", "CraftingTemplates", "Items" };
    private static readonly List<string> Warnings = new List<string>();
    private static readonly List<ItemObject> Published = new List<ItemObject>();
    private static int checks;
    private static string gameRoot, evidenceRoot, gameType, syntheticRoot;
    private static Type economics;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Set(object target, string property, object value) { AccessTools.Property(target.GetType(), property).SetValue(target, value, null); }
    private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
    private static string Id(MBObjectBase value) { return value == null ? "" : value.StringId; }
    public static void Warn(string message) { Warnings.Add(message); }
    public static string ModuleRoot(string id) {
        if (id == "RBM") return syntheticRoot;
        string path = Path.Combine(gameRoot, "Modules", id);
        return Directory.Exists(path) ? path : null;
    }
    public static string[] OfficialModules() { return ModuleIds; }

    private sealed class Inputs {
        public readonly List<Tuple<string,string>> Files = new List<Tuple<string,string>>();
        public readonly List<string> Transforms = new List<string>();
        public void Add(string path, string transform) { Files.Add(Tuple.Create(path, "")); Transforms.Add(transform); }
    }

    private static Inputs Matrix(string group, bool patched) {
        var result = new Inputs();
        foreach (string id in ModuleIds) {
            string module = Path.Combine(gameRoot, "Modules", id), manifest = Path.Combine(module, "SubModule.xml");
            if (!File.Exists(manifest)) continue;
            var document = new XmlDocument { XmlResolver = null }; document.Load(manifest);
            foreach (XmlElement entry in document.SelectNodes("/Module/Xmls/XmlNode")) {
                XmlElement name = (XmlElement)entry.SelectSingleNode("XmlName");
                if (name == null || name.GetAttribute("id") != group) continue;
                var included = entry.SelectNodes("IncludedGameTypes/GameType").Cast<XmlElement>().Select(e => e.GetAttribute("value")).ToArray();
                if (included.Length != 0 && !included.Contains(gameType)) continue;
                string stem = Path.Combine(module, "ModuleData", name.GetAttribute("path").Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(stem + ".xml")) result.Add(stem + ".xml", Transform(stem));
                else if (Directory.Exists(stem)) {
                    foreach (FileInfo file in new DirectoryInfo(stem).GetFiles("*.xml")) result.Add(file.FullName, Transform(Path.ChangeExtension(file.FullName, null)));
                }
                else result.Add("", Transform(stem));
            }
        }
        if (patched) {
            string fixture = Path.Combine(syntheticRoot, "ModuleData", group + ".xml");
            if (File.Exists(fixture)) {
                if(group=="CraftingPieces" && result.Files.Count>1) { result.Files.Insert(1,Tuple.Create(fixture,"")); result.Transforms.Insert(1,""); }
                else result.Add(fixture, "");
            }
            string transform=Path.Combine(syntheticRoot,"ModuleData",group+".xsl");
            if(File.Exists(transform)) result.Add("",transform);
        }
        return result;
    }
    private static string Transform(string stem) { return File.Exists(stem + ".xsl") ? stem + ".xsl" : File.Exists(stem + ".xslt") ? stem + ".xslt" : ""; }

    private static Dictionary<string,XmlDocument> Documents(bool patched) {
        var result = new Dictionary<string,XmlDocument>();
        foreach (string group in Groups) {
            Inputs matrix = Matrix(group, patched);
            Check(matrix.Files.Count != 0 && matrix.Files[0].Item1.Length != 0, "No native base XML for " + group);
            result[group] = MBObjectManager.CreateMergedXmlFile(matrix.Files, matrix.Transforms, true);
        }
        return result;
    }

    private static Game Context() {
        MBObjectManager manager = MBObjectManager.Init();
        Game.RegisterTypes(null, manager, null);
        manager.RegisterType<BasicCultureObject>("Culture", "Cultures", 1u);
        var game = (Game)FormatterServices.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("_current", Flags).SetValue(null, game);
        Set(game, "ObjectManager", manager);
        Set(game, "BasicModels", new BasicGameModels(new GameModel[] { new DefaultItemValueModel(), new DefaultItemCategorySelector() }));
        var text = new GameTextManager(); Set(game, "GameTextManager", text);
        AccessTools.Method(typeof(GameTexts), "Initialize").Invoke(null, new object[] { text });
        Set(game, "RandomGenerator", new MBFastRandom(123u));
        Set(game, "DefaultCharacterAttributes", Activator.CreateInstance(typeof(DefaultCharacterAttributes),true));
        Set(game, "DefaultSkills", Activator.CreateInstance(typeof(DefaultSkills),true));
        Set(game, "DefaultBannerEffects", Activator.CreateInstance(typeof(DefaultBannerEffects),true));
        Set(game, "DefaultItemCategories", Activator.CreateInstance(typeof(DefaultItemCategories),true));
        game.OnItemDeserializedEvent += item => Published.Add(item);
        return game;
    }

    private static void SeedReferences(MBObjectManager manager, IEnumerable<XmlDocument> documents) {
        foreach (var document in documents) {
            foreach (XmlAttribute reference in document.SelectNodes("//@culture")) {
                string id = reference.Value.StartsWith("Culture.") ? reference.Value.Substring(8) : reference.Value;
                if (manager.GetObject<BasicCultureObject>(id) == null) manager.CreateObject<BasicCultureObject>(id);
            }
            foreach (XmlAttribute reference in document.SelectNodes("//@monster")) {
                string id = reference.Value.StartsWith("Monster.") ? reference.Value.Substring(8) : reference.Value;
                if (manager.GetObject<Monster>(id) == null) manager.CreateObject<Monster>(id);
            }
        }
    }

    private static string Membership(MBObjectManager manager) {
        return string.Join("|", manager.GetObjectTypeList<CraftingTemplate>().OrderBy(t => t.StringId)
            .Select(t => t.StringId + ":" + string.Join(",", t.Pieces.Select(p => p.StringId))));
    }

    private static Dictionary<string,object> Describe(ItemObject item) {
        return new Dictionary<string,object> {
            {"value", item.Value}, {"tier", Bits(item.Tierf)}, {"category", Id(item.ItemCategory)},
            {"culture", Id(item.Culture)}, {"appearance", Bits(item.Appearance)},
            {"notMerchandise", item.NotMerchandise}, {"multiplayer", item.MultiplayerItem}, {"food", item.IsFood},
            {"modifierGroup", item.ItemComponent == null ? "" : Id(item.ItemComponent.ItemModifierGroup)},
            {"weight", Bits(item.Weight)}, {"arm", item.ArmorComponent == null ? -1 : item.ArmorComponent.ArmArmor}
        };
    }

    private static Dictionary<string,object> Modifier(ItemModifier modifier) {
        return new Dictionary<string,object> { {"price", Bits(modifier.PriceMultiplier)}, {"loot", Bits(modifier.LootDropScore)},
            {"production", Bits(modifier.ProductionDropScore)}, {"damage", modifier.Damage} };
    }

    private static List<object> Crafts(MBObjectManager manager) {
        var result = new List<object>();
        foreach (CraftingTemplate template in manager.GetObjectTypeList<CraftingTemplate>().Where(t => t.IsInitialized).OrderBy(t => t.StringId)) {
            WeaponDescription description = template.WeaponDescriptions.FirstOrDefault(d => d.AvailablePieces.Any(p => p.PieceType == CraftingPiece.PieceTypes.Blade && template.Pieces.Contains(p)));
            if (description == null) continue;
            foreach (int scale in new[] {90,100,110}) {
                var used = new WeaponDesignElement[4]; var pieces = new string[4];
                for (int i=0; i<4; i++) {
                    int kind=i;
                    CraftingPiece piece=description.AvailablePieces.FirstOrDefault(p => (int)p.PieceType==kind && template.Pieces.Contains(p));
                    used[i]=piece==null ? WeaponDesignElement.GetInvalidPieceForType((CraftingPiece.PieceTypes)i) : WeaponDesignElement.CreateUsablePiece(piece, scale);
                    pieces[i]=piece==null ? "" : piece.StringId;
                }
                var name=new TextObject("{=!}Native economics probe");
                var design=new WeaponDesign(template,name,used,"reign_economic_fixture_"+template.StringId+"_"+scale);
                ItemObject item=new ItemObject(); Crafting.GenerateItem(design,name,null,template.ItemModifierGroup,ref item,design.HashedCode);
                Check(item!=null && item.WeaponComponent!=null,"Native reference craft failed for "+template.StringId);
                result.Add(new Dictionary<string,object> { {"template",template.StringId},{"scale",scale},{"pieces",pieces},{"item",Describe(item)} });
            }
        }
        return result;
    }

    private static void WriteFixture(Dictionary<string,XmlDocument> reference) {
        syntheticRoot=Path.Combine(evidenceRoot,"synthetic-RBM"); Directory.CreateDirectory(Path.Combine(syntheticRoot,"ModuleData"));
        var items=new XmlDocument(); items.LoadXml("<Items><!--RBM_XML_TAG--><!--RBM_COMBAT_XML_TAG--></Items>");
        foreach(string id in new[]{"leather_gloves","sumpter_horse"}) {
            XmlElement original=reference["Items"].SelectNodes("/*/*[@id]").Cast<XmlElement>().LastOrDefault(e=>e.GetAttribute("id")==id);
            Check(original!=null,"Native control missing: "+id);
            var copy=(XmlElement)items.ImportNode(original,true); copy.SetAttribute("value","999999"); copy.SetAttribute("appearance","5"); copy.SetAttribute("is_merchandise","false");
            if(id=="leather_gloves") ((XmlElement)copy.SelectSingleNode("ItemComponent/Armor")).SetAttribute("arm_armor","24");
            else ((XmlElement)copy.SelectSingleNode("ItemComponent/Horse")).SetAttribute("speed","90");
            items.DocumentElement.AppendChild(copy);
        }
        var addition=(XmlElement)items.ImportNode(items.DocumentElement.SelectSingleNode("Item[@id='leather_gloves']"),true);
        addition.SetAttribute("id","reign_rbm_economics_added_gloves"); addition.RemoveAttribute("value"); addition.SetAttribute("is_merchandise","true"); items.DocumentElement.AppendChild(addition);
        items.Save(Path.Combine(syntheticRoot,"ModuleData","Items.xml"));
        var modifiers=new XmlDocument(); modifiers.LoadXml("<ItemModifiers/>");
        XmlElement nativeModifier=reference["ItemModifiers"].SelectNodes("/*/*[@id]").Cast<XmlElement>().Last(e=>e.GetAttribute("id")=="legendary_sword");
        var changed=(XmlElement)modifiers.ImportNode(nativeModifier,true); changed.SetAttribute("price_factor","666"); changed.SetAttribute("loot_drop_score","777"); changed.SetAttribute("production_drop_score","888"); changed.SetAttribute("damage","50"); modifiers.DocumentElement.AppendChild(changed);
        modifiers.Save(Path.Combine(syntheticRoot,"ModuleData","ItemModifiers.xml"));
        File.WriteAllText(Path.Combine(syntheticRoot,"ModuleData","ItemModifiers.xsl"),
            "<xsl:stylesheet version='1.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:template match='@*|node()'><xsl:copy><xsl:apply-templates select='@*|node()'/></xsl:copy></xsl:template><xsl:template match=\"ItemModifier[@id='legendary_sword']/@price_factor\"><xsl:attribute name='price_factor'>555</xsl:attribute></xsl:template></xsl:stylesheet>");
        var pieces=new XmlDocument(); pieces.LoadXml("<CraftingPieces><!--RBM_XML_TAG--><!--RBM_COMBAT_XML_TAG--></CraftingPieces>");
        XmlElement blade=reference["CraftingPieces"].SelectNodes("/*/*[@id]").Cast<XmlElement>().First(e=>e.GetAttribute("piece_type")=="Blade" && e.HasAttribute("weight"));
        var altered=(XmlElement)pieces.ImportNode(blade,true); altered.SetAttribute("weight","10"); altered.SetAttribute("tier","6"); altered.SetAttribute("appearance","5"); altered.SetAttribute("CraftingCost","999");
        pieces.DocumentElement.AppendChild(altered); pieces.Save(Path.Combine(syntheticRoot,"ModuleData","CraftingPieces.xml"));
    }

    public static bool Price(ItemObject __0,ref int __result) { __result=999999; return false; }
    public static bool ArmorTier(ArmorComponent __0,ref float __result) { __result=19; return false; }
    public static bool Thrust(float __0,float __1,float __2,bool __3,ref float __result) { __result=999; return false; }
    public static bool Merge(ref XmlDocument __0,XmlDocument __1,ref XmlDocument __result) {
        if(!__1.OuterXml.Contains("RBM_XML_TAG")) return true;
        if(__1.DocumentElement.Name!="Items" && __1.DocumentElement.Name!="ItemModifiers") return true;
        var ids=new HashSet<string>(__1.DocumentElement.ChildNodes.OfType<XmlElement>().Select(e=>e.GetAttribute("id")));
        var copy=(XmlDocument)__0.CloneNode(true);
        foreach(XmlElement element in copy.DocumentElement.ChildNodes.OfType<XmlElement>().ToArray()) if(ids.Contains(element.GetAttribute("id"))) copy.DocumentElement.RemoveChild(element);
        __0=copy; return true;
    }

    private static void Install(Assembly client) {
        economics=client.GetType("ReignBeta.Compatibility.ReignRbmNativeEconomics",true);
        economics.GetField("Report",Flags).SetValue(null,new Action<string>(Warn));
        economics.GetField("ModuleRoot",Flags).SetValue(null,new Func<string,string>(ModuleRoot));
        economics.GetField("OfficialModules",Flags).SetValue(null,new Func<string[]>(OfficialModules));
        economics.GetMethod("Install",Flags).Invoke(null,new object[]{new Harmony("reign.test.native-economics")});
        var rbm=new Harmony("com.rbmcombat");
        rbm.Patch(AccessTools.Method(typeof(DefaultItemValueModel),"CalculateValue"),new HarmonyMethod(typeof(NativeEconomicFixture),"Price"));
        rbm.Patch(AccessTools.Method(typeof(DefaultItemValueModel),"CalculateArmorTier"),new HarmonyMethod(typeof(NativeEconomicFixture),"ArmorTier"));
        rbm.Patch(AccessTools.Method(typeof(CombatStatCalculator),"CalculateStrikeMagnitudeForThrust"),new HarmonyMethod(typeof(NativeEconomicFixture),"Thrust"));
        new Harmony("com.rbmmain").Patch(AccessTools.Method(typeof(MBObjectManager),"MergeTwoXmls"),new HarmonyMethod(typeof(NativeEconomicFixture),"Merge"));
    }

    private static void Compare(Dictionary<string,object> expected,ItemObject item,string label) {
        Dictionary<string,object> actual=Describe(item);
        foreach(string key in new[]{"value","tier","category","culture","appearance","notMerchandise","multiplayer","food","modifierGroup"})
            Check(Convert.ToString(actual[key],CultureInfo.InvariantCulture)==Convert.ToString(expected[key],CultureInfo.InvariantCulture),label+" differs on "+key+": "+actual[key]+" / "+expected[key]);
    }

    public static Dictionary<string,object> Run(string root,string evidence,string requestedGameType,string mode,string clientPath) {
        gameRoot=root; evidenceRoot=evidence; gameType=requestedGameType;
        string referencePath=Path.Combine(evidenceRoot,"native-reference.json");
        bool patched=mode=="Patched";
        if(patched) { syntheticRoot=Path.Combine(evidenceRoot,"synthetic-RBM"); Install(Assembly.LoadFrom(clientPath)); }
        Game game=Context(); MBObjectManager manager=game.ObjectManager;
        Dictionary<string,XmlDocument> docs=Documents(patched); SeedReferences(manager,docs.Values);
        foreach(string group in Groups.Where(g=>g!="Items")) manager.LoadXml(docs[group]);
        string membership=Membership(manager); object model=game.BasicModels.ItemValueModel;
        Published.Clear(); manager.LoadXml(docs["Items"]);
        Check(Membership(manager)==membership,"Economic projection mutated the live crafting-template membership.");
        Check(Published.Count==docs["Items"].DocumentElement.ChildNodes.OfType<XmlElement>().Count(),"Shadow items leaked into native deserialization callbacks.");
        var nativeItems=manager.GetObjectTypeList<ItemObject>().Where(i=>i.IsInitialized && i.IsReady).ToArray();
        Check(nativeItems.Length>=1000,"Native item reference coverage is unexpectedly small.");
        if(!patched) {
            var itemValues=nativeItems.ToDictionary(i=>i.StringId,i=>(object)Describe(i));
            var modifierValues=manager.GetObjectTypeList<ItemModifier>().Where(m=>m.IsInitialized).ToDictionary(m=>m.StringId,m=>(object)Modifier(m));
            List<object> crafts=Crafts(manager);
            File.WriteAllText(referencePath,Json.Serialize(new Dictionary<string,object>{{"items",itemValues},{"modifiers",modifierValues},{"crafts",crafts},{"gameType",gameType}}));
            WriteFixture(docs);
            return new Dictionary<string,object>{{"ok",true},{"mode",mode},{"checks",checks},{"nativeItemCount",nativeItems.Length},{"nativeCraftChecks",crafts.Count},{"scope","separate native-only managed reference; no game engine/campaign started"}};
        }
        var reference=(Dictionary<string,object>)Json.DeserializeObject(File.ReadAllText(referencePath));
        var items=(Dictionary<string,object>)reference["items"];
        int before=manager.GetObjectTypeList<ItemObject>().Count;
        foreach(var entry in items) Compare((Dictionary<string,object>)entry.Value,manager.GetObject<ItemObject>(entry.Key),entry.Key);
        int craftsChecked=0;
        foreach(Dictionary<string,object> spec in (object[])reference["crafts"]) {
            string templateId=(string)spec["template"]; int scale=Convert.ToInt32(spec["scale"]);
            var ids=(object[])spec["pieces"]; var used=new WeaponDesignElement[4];
            for(int i=0;i<4;i++) used[i]=((string)ids[i]).Length==0 ? WeaponDesignElement.GetInvalidPieceForType((CraftingPiece.PieceTypes)i)
                : WeaponDesignElement.CreateUsablePiece(manager.GetObject<CraftingPiece>((string)ids[i]),scale);
            CraftingTemplate template=manager.GetObject<CraftingTemplate>(templateId); var name=new TextObject("{=!}Native economics probe");
            var design=new WeaponDesign(template,name,used,"reign_economic_fixture_"+templateId+"_"+scale);
            ItemObject item=new ItemObject(); Crafting.GenerateItem(design,name,null,template.ItemModifierGroup,ref item,design.HashedCode);
            Compare((Dictionary<string,object>)spec["item"],item,"runtime craft "+templateId+"/"+scale); craftsChecked++;
        }
        Check(craftsChecked>=12,"Too few native runtime-craft cases.");
        ItemObject gloves=manager.GetObject<ItemObject>("leather_gloves"), horse=manager.GetObject<ItemObject>("sumpter_horse");
        Check(gloves.ArmorComponent.ArmArmor==24,"Native pricing reverted RBM glove armor.");
        Check(horse.HorseComponent.Speed==90,"Native pricing reverted RBM horse speed.");
        var addition=manager.GetObject<ItemObject>("reign_rbm_economics_added_gloves");
        Check(addition.Value==999999 && !addition.NotMerchandise,"A genuinely added item lost its non-native pricing/availability.");
        var modifiers=(Dictionary<string,object>)reference["modifiers"];
        var expectedModifier=(Dictionary<string,object>)modifiers["legendary_sword"];
        var actualModifier=Modifier(manager.GetObject<ItemModifier>("legendary_sword"));
        foreach(string key in new[]{"price","loot","production"}) Check(Convert.ToInt32(expectedModifier[key])==Convert.ToInt32(actualModifier[key]),"Native modifier field differs: "+key);
        Check(manager.GetObject<ItemModifier>("legendary_sword").Damage==50,"Native economics reverted modifier combat bonuses.");
        bool registry=before==manager.GetObjectTypeList<ItemObject>().Count && membership==Membership(manager)
            && ReferenceEquals(model,game.BasicModels.ItemValueModel) && ReferenceEquals(manager,MBObjectManager.Instance);
        Check(registry,"Economic projection changed live registries/models.");
        object snapshot=economics.GetMethod("Snapshot",Flags).Invoke(null,null);
        string state=Json.Serialize(snapshot); File.WriteAllText(Path.Combine(evidenceRoot,"economic-snapshot.json"),state);
        Check(!Warnings.Any(w=>w.Contains("coverage is incomplete")),"Native economic coverage gaps: "+string.Join("; ",Warnings.Take(8)));
        return new Dictionary<string,object>{{"ok",true},{"mode",mode},{"checks",checks},{"nativeItemCount",items.Count},{"nativeCraftChecks",craftsChecked},
            {"physicalChecks",3},{"registryPreserved",registry},{"warnings",Warnings.ToArray()},
            {"scope","native-only reference compared in a separate process; official XML/XSLT with empty-schema offline matrices; no native campaign/market/cold-start acceptance"}};
    }
}
'@
$resultPath = Join-Path $EvidenceDirectory ($Mode.ToLowerInvariant() + '-report.json')
try {
    Add-Type -TypeDefinition $harness -ReferencedAssemblies $references
    $result = [NativeEconomicFixture]::Run($BannerlordPath,$EvidenceDirectory,$GameType,$Mode,$ClientAssembly)
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($Mode -eq 'Patched') { Copy-Item -LiteralPath $resultPath -Destination (Join-Path $EvidenceDirectory 'report.json') -Force }
}
catch {
    @{ok=$false; mode=$Mode; failure=$_.Exception.ToString()} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    throw
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
