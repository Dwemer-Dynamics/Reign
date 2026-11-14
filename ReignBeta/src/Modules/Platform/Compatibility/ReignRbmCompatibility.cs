using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Xml;
using HarmonyLib;
using ReignBeta.Integration;

namespace ReignBeta.Compatibility
{
    // Optional, process-scoped policy. No compile-time reference to an RBM assembly.
    // Pricing is deliberately separate: native formulas on RBM stats are not native prices.
    internal static class ReignRbmCompatibility
    {
        internal const string Owner = "com.bannerlordreign.rbm-compatibility.v1";
        private const string EconomicsOwner = Owner + ".economics";
        private static readonly Harmony Hooks = new Harmony(Owner);
        private static Action<string> Report = message => ReignLog.Warn(message);
        private static readonly Dictionary<string, string> SupportedHashes = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "RBM", "456BA61917F3AD848DBD9FC3254A070D8ADD38FC0BF341FFCAE3855846B34E60" },
            { "RBMConfig", "8ED163892A06C78127CBE239581DE93226F496322A49FA33F1D4ED952001FAFF" },
            { "RBMCombat", "9906643FD78DDB171BFBC0A57A1CF09B96E12B8120DD65143081A891E83EBC88" },
            { "RBMAI", "BDE2439D9AFACC4E806906EFC280FF1B517769F248A54E6AE2708A3BEC807F0C" },
            { "RBMAutoResolve", "046E3D544555F18223741000C588555BBB279E123E6E8FD084D3BDC9B2F19E78" },
            { "TaleWorlds.Core", "CAE7BC303C46C3A9489C6C34F8DA2C05F72A430C81E218664E2C9771E7DB10EC" },
            { "TaleWorlds.MountAndBlade", "EAEC5C773AC9E936670790B16333BC765F949629B6D10ECD5CF5EA2346BB2406" }
        };

        internal static readonly string[][] DisabledSettings = {
            new[] { "developerMode", "/Config/DeveloperMode" },
            new[] { "rbmTournamentEnabled", "/Config/RBMTournament/Enabled" },
            new[] { "rbmCampaignEnabled", "/Config/RBMCampaign/Enabled" },
            // RBM4.8.1 split simulation out of the Campaign-module gate.
            new[] { "simulationEquipmentEnabled", "/Config/RBMCampaign/SimulationEquipmentEnabled" },
            new[] { "spoilsLoggingEnabled", "/Config/RBMCampaign/SpoilsLoggingEnabled" },
            new[] { "spoilsVerboseLoggingEnabled", "/Config/RBMCampaign/SpoilsVerboseLoggingEnabled" },
            new[] { "economyLoggingEnabled", "/Config/RBMCampaign/EconomyLoggingEnabled" },
            new[] { "caravanLoggingEnabled", "/Config/RBMCampaign/CaravanLoggingEnabled" },
            new[] { "strategicPowerLoggingEnabled", "/Config/RBMCampaign/StrategicPowerLogging" },
            new[] { "simulationRoutEnabled", "/Config/RBMCampaign/SimulationRoutEnabled" },
            new[] { "simulationLoggingEnabled", "/Config/RBMCampaign/SimulationLoggingEnabled" },
            new[] { "simulationLogHits", "/Config/RBMCampaign/SimulationLogHits" },
            new[] { "spectateBattlesEnabled", "/Config/RBMCampaign/SpectateBattlesEnabled" },
            new[] { "vanillaCombatAi", "/Config/RBMAI/VanillaCombatAi" },
            new[] { "keepBattleEnabled", "/Config/RBMAI/KeepBattleEnabled" },
            new[] { "aiBehaviorLogEnabled", "/Config/RBMAI/AiBehaviorLogEnabled" },
            new[] { "battleHitLoggingEnabled", "/Config/RBMCombat/Global/BattleHitLoggingEnabled" },
            new[] { "realisticArrowArc", "/Config/RBMCombat/Global/RealisticArrowArc" },
            new[] { "armorPenetrationMessage", "/Config/RBMCombat/Global/ArmorPenetrationMessage" },
            new[] { "passiveShoulderShields", "/Config/RBMCombat/Global/PassiveShoulderShields" },
            new[] { "troopOverhaulActive", "/Config/RBMCombat/Global/TroopOverhaulActive" },
            new[] { "sneakAttackInstaKill", "/Config/RBMCombat/Global/SneakAttackInstaKill" },
            new[] { "rangedReloadAffectsAi", "/Config/RBMCombat/Global/RangedReloadAffectsAi" },
            new[] { "rangedAimArcEnabled", "/Config/RBMCombat/Global/RangedAimArcEnabled" }
        };

        // The assembly hash and this exact patch-method/target pair must both match.
        // Economy hooks stay installed until independent native economic data is available.
        internal static readonly string[][] Suppressions = {
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+DefaultVolunteerModelPatch", "Prefix", "GetBasicVolunteer" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+OverrideDefaultPartyHealingModel", "PrefixGetAiWeight", "GetSurvivalChance" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+AddSkillXpPatch", "Prefix", "GetXpMultiplier" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+GetXpMultiplierFromShotDifficultyPatch", "Prefix", "GetXpMultiplierFromShotDifficulty" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+GetXpFromHitPatch", "PrefixGetXpFromHit", "GetXpFromHit" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+OverrideCharacterObject", "PrefixGetPowerImp", "GetPowerImp" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+OverrideCharacterObject", "PrefixGetPower", "GetPower" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+OverrideCharacterObject", "PrefixGetBattlePower", "GetBattlePower" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+GetRandomizedEquipmentPatch", "Prefix", "GetRandomizedEquipment" },
            new[] { "com.rbmcombat", "RBMCombat.CampaignChanges+InitializeSpawnEquipmentPatch", "Prefix", "InitializeSpawnEquipment" },
            new[] { "com.rbmai", "RBMAI.AiModule.SpawningPatches+SetRoutedPatch", "Prefix", "CheckIfBattleShouldContinueAfterBattleMission" }
        };

        private static bool _bootstrapRegistered, _earlyBootstrap, _binding, _installed, _unsupportedReported, _economicHooksInstalled, _preferenceSourceReady;
        [ThreadStatic] private static int _saveDepth;
        private static Type _config;
        private static Assembly _mainAssembly, _combatAssembly;
        private static FieldInfo[] _fields;
        private static Dictionary<string, bool> Preferences = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static Dictionary<string, string> PreferenceText = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Func<string> ConfigPath;
        private static readonly List<Tuple<MethodBase, HarmonyLib.Patch>> RemovedPrefixes = new List<Tuple<MethodBase, HarmonyLib.Patch>>();

        internal static void Bootstrap()
        {
            if (_bootstrapRegistered) return;
            try {
                Type module = typeof(TaleWorlds.MountAndBlade.Module);
                if (!HashMatches(module.Assembly, SupportedHashes["TaleWorlds.MountAndBlade"])) {
                    Issue("RBM compatibility is inactive: unreviewed native module loader.");
                    return;
                }
                MethodInfo startup = Required(module, "InitializeSubModuleBases");
                RequireShape(startup, false, typeof(void));
                Patch(startup, prefix: nameof(BeforeSubmoduleLoads));
                _bootstrapRegistered = true;
            }
            catch (Exception ex) { Issue("RBM early bootstrap could not be installed: " + ex.Message); }
        }

        private static void BeforeSubmoduleLoads()
        {
            // Reviewed native 1.5.7 constructs every submodule before this method invokes
            // ANY OnSubModuleLoad. Never patch from an AssemblyLoad callback.
            _earlyBootstrap = true;
            TryBind();
        }

        internal static void VerifyEarlyStartup()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "RBM") && !_installed)
                Issue("RBM combat-only compatibility is inactive: early binding did not complete. Check the supported versions and restart; no late settings rewrite was attempted.");
        }

        internal static void AfterGameLoad(TaleWorlds.Core.Game game)
        {
            if (!_installed || !ReferenceEquals(game, TaleWorlds.Core.Game.Current)) return;
            try {
                Reconcile();
                if (_economicHooksInstalled) ReignRbmNativeEconomics.ReconcileLoadedItems();
                else if ((_config?.GetField("rbmCombatEnabled")?.GetValue(null) as bool?) == true)
                    Issue("Native RBM economics is inactive after load; native price coverage is incomplete.");
            }
            catch (Exception ex) { Issue("RBM post-load verification is incomplete: " + ex.Message); }
        }

        private static void TryBind()
        {
            if (!_earlyBootstrap || _installed || _binding || _unsupportedReported) return;
            _binding = true;
            try {
                Assembly primary = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RBM");
                if (primary == null) return;
                // Load declared dependencies only; never invoke RBM callbacks here.
                foreach (string name in new[] { "RBMConfig", "RBMCombat", "RBMAI", "RBMAutoResolve" }) {
                    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name)) continue;
                    if (!primary.GetReferencedAssemblies().Any(a => a.Name == name)) throw new InvalidOperationException("Missing reviewed RBM dependency reference: " + name);
                    string dependencyPath = Path.Combine(Path.GetDirectoryName(primary.Location), name + ".dll");
                    if (!File.Exists(dependencyPath)) throw new FileNotFoundException("RBM dependency is unavailable before initialization: " + name);
                    Assembly.LoadFrom(dependencyPath);
                }
                var groups = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => SupportedHashes.ContainsKey(a.GetName().Name))
                    .GroupBy(a => a.GetName().Name).ToArray();
                if (groups.Any(g => g.Count() != 1)) {
                    _unsupportedReported = true;
                    Issue("RBM compatibility is inactive: duplicate assembly identities. Restart with one copy of each dependency.");
                    return;
                }
                var assemblies = groups.ToDictionary(g => g.Key, g => g.Single());
                if (assemblies.Count != SupportedHashes.Count) return;
                foreach (var entry in SupportedHashes) {
                    if (!HashMatches(assemblies[entry.Key], entry.Value)) {
                        Issue("RBM compatibility is inactive: unsupported " + entry.Key + " binary. No RBM patches or settings changed.");
                        _unsupportedReported = true;
                        return;
                    }
                }
                Type config = assemblies["RBMConfig"].GetType("RBMConfig.RBMConfig", true);
                ValidateConfig(config);
                Type module = assemblies["RBM"].GetType("RBM.SubModule", true);
                Type ai = assemblies["RBMAI"].GetType("RBMAI.RBMAiPatcher", true);
                Type utilities = assemblies["RBMConfig"].GetType("RBMConfig.Utilities", true);
                // Bind every required entry before installing any hook.
                MethodInfo parse = Required(config, "parseXmlConfig"), load = Required(config, "LoadConfig");
                MethodInfo save = Required(config, "saveXmlConfig"), write = Required(config, "TrySaveConfig");
                MethodInfo apply = Required(module, "ApplyHarmonyPatches"), fullAi = Required(ai, "DoPatching");
                MethodInfo combatApply = Required(assemblies["RBMCombat"].GetType("RBMCombat.RBMCombatPatcher", true), "DoPatching");
                MethodInfo defaults = Required(utilities, "createXmlConfig"), path = Required(utilities, "GetConfigFilePath");
                foreach (MethodInfo method in new[] { parse, load, save, apply, fullAi }) RequireShape(method, true, typeof(void));
                RequireShape(write, true, typeof(void), typeof(XmlDocument));
                RequireShape(defaults, true, typeof(void), typeof(XmlDocument).MakeByRefType());
                RequireShape(path, true, typeof(string));
                RequireShape(combatApply, true, typeof(void), typeof(Harmony).MakeByRefType());
                RequireImplicitPatchAll(apply);
                RequireImplicitPatchAll(combatApply);
                _mainAssembly = assemblies["RBM"];
                _combatAssembly = assemblies["RBMCombat"];
                ConfigPath = () => (string)path.Invoke(null, null);
                CapturePreferences();
                Patch(parse, postfix: nameof(AfterParse), finalizer: nameof(EnforceFinally));
                Patch(load, postfix: nameof(AfterLoad), finalizer: nameof(EnforceFinally));
                Patch(defaults, postfix: nameof(AfterDefaults), finalizer: nameof(EnforceFinally));
                Patch(save, prefix: nameof(BeforeSave), finalizer: nameof(AfterSave));
                Patch(write, prefix: nameof(PrepareSave));
                Patch(combatApply, transpiler: nameof(ExplicitCombatPatchAll));
                Patch(apply, prefix: nameof(EnforceSafely), finalizer: nameof(AfterPatching), transpiler: nameof(ExplicitMainPatchAll));
                Patch(fullAi, prefix: nameof(EnforceSafely), finalizer: nameof(AfterPatching));
                _installed = true;
                // Do not re-read the user's file or register/remove campaign behaviors.
                // Late binding must be reported; it cannot undo prior initialization.
                EnforceSafely();
                AfterPatching(null);
                Issue("RBM native-rule/settings hooks bound before submodule callbacks; native economics initializes after config load. Native game acceptance remains pending.");
            }
            catch (Exception ex) {
                try { Hooks.UnpatchAll(Owner); RestoreFields(); RestoreSuppressed(); }
                catch (Exception cleanup) { Issue("RBM compatibility rollback is incomplete: " + cleanup.Message); }
                _installed = false;
                Issue("RBM compatibility binding failed; no complete combat-only profile is established: " + ex.Message);
            }
            finally { _binding = false; }
        }

        private static bool HashMatches(Assembly assembly, string expected)
        {
            using (var stream = File.OpenRead(assembly.Location))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == expected;
        }

        private static void Issue(string message)
        {
            try { Report(message); } catch { /* Diagnostics must not break module loading. */ }
        }

        private static void RequireShape(MethodInfo method, bool isStatic, Type result, params Type[] arguments)
        {
            if (method.IsStatic != isStatic || method.ReturnType != result
                || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments))
                throw new InvalidOperationException("Unsupported RBM/native signature: " + method.DeclaringType?.FullName + "." + method.Name);
        }

        private static MethodInfo Required(Type type, string name)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static void Patch(MethodBase target, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
        {
            Hooks.Patch(target,
                prefix == null ? null : new HarmonyMethod(typeof(ReignRbmCompatibility), prefix) { priority = Priority.First },
                postfix == null ? null : new HarmonyMethod(typeof(ReignRbmCompatibility), postfix) { priority = Priority.Last },
                transpiler == null ? null : new HarmonyMethod(typeof(ReignRbmCompatibility), transpiler),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(ReignRbmCompatibility), finalizer) { priority = Priority.Last });
        }

        private static MethodInfo ImplicitPatchAll => AccessTools.DeclaredMethod(typeof(Harmony), nameof(Harmony.PatchAll), Type.EmptyTypes);
        private static void RequireImplicitPatchAll(MethodBase method)
        {
            if (PatchProcessor.GetOriginalInstructions(method).Count(instruction => instruction.Calls(ImplicitPatchAll)) != 1)
                throw new InvalidOperationException("Unsupported RBM implicit PatchAll call shape: " + method.DeclaringType?.FullName + "." + method.Name);
        }

        private static IEnumerable<CodeInstruction> ExplicitMainPatchAll(IEnumerable<CodeInstruction> instructions) =>
            ExplicitPatchAll(instructions, nameof(PatchMainAssembly));
        private static IEnumerable<CodeInstruction> ExplicitCombatPatchAll(IEnumerable<CodeInstruction> instructions) =>
            ExplicitPatchAll(instructions, nameof(PatchCombatAssembly));
        private static IEnumerable<CodeInstruction> ExplicitPatchAll(IEnumerable<CodeInstruction> instructions, string helper)
        {
            var code = instructions.ToList();
            if (code.Count(instruction => instruction.Calls(ImplicitPatchAll)) != 1)
                throw new InvalidOperationException("RBM implicit PatchAll rewrite is missing or ambiguous.");
            return code.Select(instruction => instruction.Calls(ImplicitPatchAll)
                ? new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = Required(typeof(ReignRbmCompatibility), helper) }
                : instruction);
        }

        // Harmony.PatchAll() inspects its caller's ReflectedType. A Harmony-generated
        // caller has no declaring type; inlining can expose the combat caller too.
        // Pass only the hash-reviewed assembly intended by each exact call site.
        private static void PatchMainAssembly(Harmony harmony) => PatchReviewedAssembly(harmony, _mainAssembly, "com.rbmmain");
        private static void PatchCombatAssembly(Harmony harmony) => PatchReviewedAssembly(harmony, _combatAssembly, "com.rbmcombat");
        private static void PatchReviewedAssembly(Harmony harmony, Assembly assembly, string owner)
        {
            if (harmony == null || harmony.Id != owner || assembly == null)
                throw new InvalidOperationException("RBM explicit PatchAll owner/assembly is unavailable: " + owner);
            harmony.PatchAll(assembly);
        }

        internal static void ValidateConfig(Type config)
        {
            var fields = DisabledSettings.Select(setting => config.GetField(setting[0], BindingFlags.Public | BindingFlags.Static)).ToArray();
            if (fields.Any(f => f == null || f.FieldType != typeof(bool))) throw new InvalidOperationException("RBM off-policy fields do not match the reviewed Boolean contract.");
            if (config.GetField("xmlConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.FieldType != typeof(XmlDocument))
                throw new InvalidOperationException("RBM preference XML contract is missing.");
            _config = config;
            _fields = fields;
        }

        private static XmlDocument ConfigXml => (XmlDocument)_config?.GetField("xmlConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        internal static void CapturePreferences()
        {
            if (_fields == null) return;
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FieldInfo field in _fields) values[field.Name] = (bool)field.GetValue(null);
            foreach (string[] setting in DisabledSettings) {
                string text = ConfigXml?.SelectSingleNode(setting[1])?.InnerText;
                if (text != null) texts[setting[0]] = text;
            }
            Preferences = values;
            PreferenceText = texts;
        }

        internal static void Enforce()
        {
            if (_fields == null) return;
            foreach (FieldInfo field in _fields) field.SetValue(null, false);
        }

        internal static void AfterParse()
        {
            try { if (_saveDepth == 0) { CapturePreferences(); _preferenceSourceReady = true; } }
            catch (Exception ex) { Issue("RBM preferences could not be captured: " + ex.Message); }
            finally { EnforceSafely(); }
        }

        internal static void AfterLoad()
        {
            EnforceSafely();
            EnsureEconomics();
        }

        private static void EnsureEconomics()
        {
            if (!_installed || _economicHooksInstalled || (_config?.GetField("rbmCombatEnabled")?.GetValue(null) as bool?) != true) return;
            if (TaleWorlds.Core.Game.Current != null) {
                Issue("Native RBM economics requires a cold start before item loading; late activation was refused.");
                return;
            }
            var economics = new Harmony(EconomicsOwner);
            try { ReignRbmNativeEconomics.Install(economics); _economicHooksInstalled = true; }
            catch (Exception ex) {
                try { economics.UnpatchAll(EconomicsOwner); ReignRbmNativeEconomics.Reset(); } catch { }
                Issue("Native RBM economics could not be installed: " + ex.Message);
            }
        }

        internal static Exception EnforceFinally(Exception __exception)
        {
            EnforceSafely();
            return __exception;
        }

        internal static void EnforceSafely()
        {
            try { Enforce(); } catch (Exception ex) { Issue("RBM off policy is incomplete: " + ex.Message); }
        }

        internal static void AfterDefaults(ref XmlDocument __0)
        {
            try {
                OverlayPreferences(__0);
                XmlDocument defaults = __0;
                _preferenceSourceReady = defaults != null && DisabledSettings.All(s => defaults.SelectSingleNode(s[1]) != null);
            }
            catch (Exception ex) { Issue("RBM default preference overlay failed: " + ex.Message); }
            finally { EnforceSafely(); }
        }

        internal static void BeforeSave()
        {
            _saveDepth++;
            EnforceSafely();
        }

        internal static void OverlayPreferences(XmlDocument document)
        {
            if (document == null) return;
            foreach (string[] setting in DisabledSettings) {
                if (!Preferences.TryGetValue(setting[0], out bool value)) continue;
                XmlNode node = document.SelectSingleNode(setting[1]);
                if (node != null) node.InnerText = PreferenceText.TryGetValue(setting[0], out string text) ? text : (value ? "1" : "0");
            }
        }

        internal static bool PrepareSave(ref XmlDocument __0)
        {
            try {
                if (__0 == null || ConfigPath == null || Preferences.Count != DisabledSettings.Length || !_preferenceSourceReady)
                    throw new InvalidOperationException("RBM save has no complete reviewed preference document/path.");
                var candidate = (XmlDocument)__0.CloneNode(true);
                // Compare to current DISK state, not a stale load-time snapshot:
                // save X', then revert X, must perform both writes.
                OverlayPreferences(candidate);
                string path = ConfigPath();
                if (File.Exists(path)) {
                    var current = new XmlDocument { XmlResolver = null };
                    try {
                        current.Load(path);
                        if (current.DocumentElement?.Name != "Config") throw new XmlException("Unexpected RBM config root.");
                        RememberDiskPreferences(current);
                        OverlayPreferences(candidate);
                        if (Equivalent(current.DocumentElement, candidate.DocumentElement)) return false;
                    }
                    catch (XmlException) {
                        // Native defaults can recover a malformed file, but retain
                        // its exact original bytes before allowing that normal save.
                        byte[] original = File.ReadAllBytes(path);
                        string hash;
                        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(original)).Replace("-", "");
                        string backup = path + ".reign-corrupt-" + hash + ".bak";
                        if (!File.Exists(backup)) File.WriteAllBytes(backup, original);
                        if (!File.ReadAllBytes(backup).SequenceEqual(original)) throw new IOException("Corrupt config backup could not be verified.");
                    }
                }
                __0 = candidate;
                return true;
            }
            catch (Exception ex) {
                Issue("RBM preference save was blocked to preserve the player's file: " + ex.Message);
                return false;
            }
        }

        private static void RememberDiskPreferences(XmlDocument document)
        {
            var values = new Dictionary<string, bool>(Preferences, StringComparer.Ordinal);
            var texts = new Dictionary<string, string>(PreferenceText, StringComparer.Ordinal);
            foreach (string[] setting in DisabledSettings) {
                XmlNode node = document.SelectSingleNode(setting[1]);
                if (node == null) continue;
                texts[setting[0]] = node.InnerText;
                string text = node.InnerText.Trim();
                values[setting[0]] = text == "1" || (setting[0] == "developerMode" && text.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            Preferences = values; PreferenceText = texts;
        }

        internal static Exception AfterSave(Exception __exception)
        {
            // saveXmlConfig mutates its static XML before calling TrySaveConfig.
            // Repair that document even on an exception, without touching live flags.
            try { OverlayPreferences(ConfigXml); }
            catch (Exception ex) { Issue("RBM preference document restoration failed: " + ex.Message); }
            finally { _saveDepth = Math.Max(0, _saveDepth - 1); EnforceSafely(); }
            return __exception;
        }

        private static bool Equivalent(XmlElement left, XmlElement right)
        {
            if (left == null || right == null || left.Name != right.Name || left.Attributes.Count != right.Attributes.Count) return false;
            foreach (XmlAttribute attribute in left.Attributes)
                if (right.GetAttribute(attribute.Name) != attribute.Value) return false;
            XmlElement[] a = left.ChildNodes.OfType<XmlElement>().ToArray(), b = right.ChildNodes.OfType<XmlElement>().ToArray();
            if (a.Length != b.Length) return false;
            if (a.Length != 0) return a.Zip(b, Equivalent).All(equal => equal);
            if (left.InnerText == right.InnerText) return true;
            // Native saving normalizes scalar numeric formatting. Preserve original
            // bytes for no-op saves, without normalizing version/string settings.
            FieldInfo field = _config?.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f => f.Name.Equals(left.Name, StringComparison.OrdinalIgnoreCase));
            Type type = field?.FieldType;
            if (type == null && _config != null) {
                type = _config.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .SelectMany(f => f.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    .FirstOrDefault(f => f.Name.Equals(left.Name, StringComparison.OrdinalIgnoreCase))?.FieldType;
            }
            if (type == typeof(float)) return float.TryParse(left.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out float firstFloat)
                && float.TryParse(right.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out float secondFloat) && firstFloat.Equals(secondFloat);
            if (type == typeof(double)) return double.TryParse(left.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out double firstDouble)
                && double.TryParse(right.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out double secondDouble) && firstDouble.Equals(secondDouble);
            return false;
        }

        internal static Exception AfterPatching(Exception __exception)
        {
            try { Reconcile(); if (_economicHooksInstalled) ReignRbmNativeCalculations.VerifyIsolation(); }
            catch (Exception ex) { Issue("RBM noncombat suppression is incomplete: " + ex.Message); }
            return __exception;
        }

        internal static bool MatchesSuppression(string owner, MethodInfo patch, MethodBase target)
        {
            return patch != null && target != null && Suppressions.Any(row =>
                row[0] == owner && row[1] == patch.DeclaringType?.FullName && row[2] == patch.Name && row[3] == target.Name
                && patch.DeclaringType.Assembly.GetName().Name == (owner == "com.rbmai" ? "RBMAI" : "RBMCombat"));
        }

        internal static void Reconcile()
        {
            // Lifecycle work only. There is no per-frame Harmony scan.
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray()) {
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                foreach (string owner in new[] { "com.rbmcombat", "com.rbmai" }) {
                    HarmonyLib.Patch[] owned = info.Prefixes.Where(p => p.owner == owner).ToArray();
                    if (!owned.Any(p => MatchesSuppression(owner, p.PatchMethod, target))) continue;
                    if (owned.Any(p => !MatchesSuppression(owner, p.PatchMethod, target))) {
                        Issue("RBM suppression is incomplete: an unexpected prefix shares owner " + owner + " on " + target.Name + ". All prefixes on that target were retained.");
                        continue;
                    }
                    // Method-only Unpatch also removes that method under OTHER owners.
                    // Owner+kind removal is exact after validating every owned prefix.
                    Hooks.Unpatch(target, HarmonyPatchType.Prefix, owner);
                    foreach (HarmonyLib.Patch patch in owned) {
                        if (!RemovedPrefixes.Any(p => p.Item1 == target && p.Item2.owner == owner && p.Item2.PatchMethod == patch.PatchMethod))
                            RemovedPrefixes.Add(Tuple.Create(target, patch));
                    }
                }
            }
        }

        private static void RestoreFields()
        {
            if (_fields == null) return;
            foreach (FieldInfo field in _fields) if (Preferences.TryGetValue(field.Name, out bool value)) field.SetValue(null, value);
        }

        private static void RestoreSuppressed()
        {
            foreach (var removed in RemovedPrefixes) {
                HarmonyLib.Patch patch = removed.Item2;
                string enabledField = patch.owner == "com.rbmai" ? "rbmAiEnabled" : "rbmCombatEnabled";
                var enabled = _config?.GetField(enabledField, BindingFlags.Public | BindingFlags.Static);
                if (enabled != null && enabled.GetValue(null) is bool choice && !choice) continue;
                if (!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(patch.owner) == true)) continue;
                Patches current = Harmony.GetPatchInfo(removed.Item1);
                if (current != null && current.Prefixes.Any(p => p.owner == patch.owner && p.PatchMethod == patch.PatchMethod)) continue;
                new Harmony(patch.owner).Patch(removed.Item1, prefix: new HarmonyMethod(patch.PatchMethod) {
                    priority = patch.priority, before = patch.before, after = patch.after
                });
            }
            RemovedPrefixes.Clear();
        }

        internal static void Unload()
        {
            Cleanup(RestoreFields, "preference fields");
            Cleanup(() => Hooks.UnpatchAll(Owner), "rule hooks");
            Cleanup(() => new Harmony(EconomicsOwner).UnpatchAll(EconomicsOwner), "economic hooks");
            Cleanup(ReignRbmNativeEconomics.Reset, "economic caches");
            _economicHooksInstalled = false;
            Cleanup(RestoreSuppressed, "suppressed RBM hooks");
            Preferences.Clear();
            PreferenceText.Clear();
            _fields = null;
            _config = null;
            _mainAssembly = null;
            _combatAssembly = null;
            ConfigPath = null;
            _saveDepth = 0;
            _bootstrapRegistered = false;
            _earlyBootstrap = false;
            _installed = false;
            _preferenceSourceReady = false;
        }

        private static void Cleanup(Action action, string description)
        {
            try { action(); } catch (Exception ex) { Issue("RBM compatibility cleanup is incomplete for " + description + ": " + ex.Message); }
        }
    }
}
