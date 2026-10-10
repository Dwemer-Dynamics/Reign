using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Actions
{
    // Operator-only native primitives. The list is checked-in public API metadata;
    // neither chat, model output, nor console input can select another type/method.
    public static class ReignNativeActionConsole
    {
        public static string Help(NativeActionDefinition d) => d.Id + "\nNative operator primitive: " + d.Type + "." + d.Method
            + "\n" + string.Join("\n", d.Parameters.Where(p => p.Direction != "out").Select(p => "--" + p.Name + "=<" + p.Type + ">"
                + (p.Default.Length > 0 ? " default=" + p.Default : " required")))
            + "\nUse exact IDs. PartyBase/IMapPoint: party:<id> or settlement:<id>. IFaction: kingdom:<id> or clan:<id>."
            + "\nArmy: leader party ID. Alley: settlementId:index. ItemRosterElement: itemId,count[,modifierId]."
            + "\nTroop rosters: JSON [{\"id\":\"troop_id\",\"count\":1,\"wounded\":0}]. CampaignVec2: x,y,land|sea."
            + "\nCampaignTime: absolute day or now. Optional null entities: null. Enum values are exact native names."
            + "\n--operation=<stableId> makes a repeated run return its retained receipt without invoking again."
            + "\nPreview resolves inputs without mutation. Run records before/after observations; semantic acceptance requires a separate effect assertion.";

        public static JObject Execute(string id, Dictionary<string, string> supplied, bool mutate)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || ReignAgreementBehavior.Instance == null)
                throw new InvalidOperationException("Load a campaign first.");
            var d = ReignNativeActionCatalog.Find(id) ?? throw new InvalidOperationException("Unknown native action ID.");
            if (supplied.Count > 32 || supplied.Any(kv => kv.Key.Length > 128 || kv.Value.Length > 8192))
                throw new InvalidOperationException("Native arguments exceed the bounded command size.");
            var method = ResolveMethod(d);
            var parameters = method.GetParameters();
            foreach (string key in supplied.Keys)
                if (key != "operation" && !d.Parameters.Any(p => p.Name == key && p.Direction != "out"))
                    throw new InvalidOperationException("Unknown or output-only parameter: " + key);
            string hash = Hash(id + "\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(supplied.Where(p => p.Key != "operation").ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)));
            string operation = supplied.TryGetValue("operation", out var named) ? named : "native_" + Guid.NewGuid().ToString("N");
            if (operation.Length < 1 || operation.Length > 80 || operation.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-'))
                throw new InvalidOperationException("Operation IDs use 1–80 letters, numbers, underscores or hyphens.");
            var behavior = ReignAgreementBehavior.Instance;
            var existing = behavior.NativeOperation(operation);
            if (mutate && existing != null)
            {
                if (existing.Value<string>("requestHash") != hash) throw new InvalidOperationException("That operation ID already belongs to different native arguments.");
                return existing;
            }
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i]; var spec = d.Parameters[i];
                Type type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                if (p.IsOut) { values[i] = type.IsValueType ? Activator.CreateInstance(type) : null; continue; }
                if (!supplied.TryGetValue(spec.Name, out string value))
                {
                    if (!p.HasDefaultValue) throw new InvalidOperationException("Supply --" + spec.Name + ".");
                    values[i] = p.DefaultValue == null && type.IsValueType ? Activator.CreateInstance(type) : p.DefaultValue;
                }
                else values[i] = ResolveValue(type, value, p.HasDefaultValue && p.DefaultValue == null);
            }
            ValidateContext(d, values);
            var receipt = new JObject { ["schema"] = "reign-native-action-result-v1", ["operationId"] = operation,
                ["actionId"] = id, ["requestHash"] = hash, ["campaignDay"] = CampaignTime.Now.ToDays,
                ["origin"] = "operator", ["status"] = mutate ? "started" : "preview", ["invocations"] = 0,
                ["parameters"] = JObject.FromObject(supplied), ["before"] = Observe(parameters, values),
                ["semanticVerification"] = "pending" };
            if (!mutate) return receipt;
            // Persist the uncertain state before invoking. A throw or interrupted native
            // operation must never turn a retry into a second mutation.
            receipt["invocations"] = 1; behavior.SaveNativeOperation(operation, receipt);
            try
            {
                object returned = method.Invoke(null, values);
                receipt["status"] = d.ReturnType == "void" ? "returned" : "queried";
                if (returned != null) receipt["returnValue"] = JToken.FromObject(returned);
            }
            catch (Exception ex)
            {
                receipt["status"] = "uncertain";
                foreach (var fault in ObserveExceptionFault(ex).Properties()) receipt[fault.Name] = fault.Value;
            }
            try
            {
                receipt["after"] = Observe(parameters, values);
                receipt["observedChange"] = !JToken.DeepEquals(receipt["before"], receipt["after"]);
            }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["observationError"] = ex.Message; }
            behavior.SaveNativeOperation(operation, receipt);
            return receipt;
        }

        internal static JObject ObserveExceptionFault(Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            string stack = error.StackTrace ?? "";
            const int maximumStackCharacters = 4096;
            return new JObject { ["error"] = error.Message, ["errorType"] = error.GetType().FullName,
                ["errorStack"] = stack.Substring(0, Math.Min(stack.Length, maximumStackCharacters)),
                ["errorStackTruncated"] = stack.Length > maximumStackCharacters };
        }

        public static MethodInfo ResolveMethod(NativeActionDefinition d)
        {
            Type type = typeof(Hero).Assembly.GetType(d.Type, false);
            if (type == null || type.Namespace != "TaleWorlds.CampaignSystem.Actions") throw new InvalidOperationException("The installed game lacks this registered native action.");
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == d.Method && TypeName(m.ReturnType) == d.ReturnType && m.GetParameters().Length == d.Parameters.Count)
                .Where(m => m.GetParameters().Select((p, i) => Matches(p, d.Parameters[i])).All(match => match)).ToList();
            if (methods.Count != 1) throw new InvalidOperationException("The installed native signature differs from the reviewed catalog; update its classification before invoking it.");
            return methods[0];
        }
        private static bool Matches(ParameterInfo actual, NativeActionParameter expected) => actual.Name == expected.Name
            && TypeName(actual.ParameterType) == expected.Type && actual.IsOut == (expected.Direction == "out")
            && actual.ParameterType.IsByRef == (expected.Direction.Length > 0)
            && actual.HasDefaultValue == (expected.Default.Length > 0) && DefaultMatches(actual, expected);
        private static bool DefaultMatches(ParameterInfo actual, NativeActionParameter expected)
        {
            if (expected.Default.Length == 0) return true;
            if (actual.DefaultValue == null) return expected.Default == "null" || expected.Default.StartsWith("default(", StringComparison.Ordinal);
            return Convert.ToString(actual.DefaultValue, CultureInfo.InvariantCulture).ToLowerInvariant() == expected.Default.ToLowerInvariant();
        }
        private static string TypeName(Type type)
        {
            if (type.IsByRef) return TypeName(type.GetElementType());
            if (type == typeof(void)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(float)) return "float";
            if (type.IsGenericType) return type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
            return type.IsNested ? type.DeclaringType.Name + "." + type.Name : type.Name;
        }

        private static object ResolveValue(Type type, string value, bool nullable)
        {
            if (value == "null") return nullable ? null : throw new InvalidOperationException("This entity parameter cannot be null.");
            if (type == typeof(bool)) return value == "true" ? true : value == "false" ? false : throw new InvalidOperationException("Use true or false.");
            if (type == typeof(int)) return int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (type == typeof(float)) { float n = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture); if (float.IsNaN(n) || float.IsInfinity(n)) throw new InvalidOperationException("Numbers must be finite."); return n; }
            if (type.IsEnum)
            {
                if (!Enum.GetNames(type).Contains(value, StringComparer.Ordinal)) throw new InvalidOperationException("Choose " + string.Join(", ", Enum.GetNames(type)) + ".");
                return Enum.Parse(type, value, false);
            }
            object found = null;
            if (type == typeof(Hero)) found = ReignObjectResolver.FindHero(value);
            else if (type == typeof(CharacterObject)) found = MBObjectManager.Instance.GetObject<CharacterObject>(value);
            else if (type == typeof(Clan)) found = Clan.All.FirstOrDefault(c => c.StringId == value);
            else if (type == typeof(Kingdom)) found = Kingdom.All.FirstOrDefault(k => k.StringId == value);
            else if (type == typeof(Settlement)) found = Settlement.Find(value);
            else if (type == typeof(Town)) found = Settlement.Find(value)?.Town;
            else if (type == typeof(MobileParty)) found = MobileParty.All.FirstOrDefault(p => p.StringId == value);
            else if (type == typeof(PartyBase)) found = ResolveParty(value);
            else if (type == typeof(IFaction)) found = value.StartsWith("kingdom:", StringComparison.Ordinal) ? (IFaction)Kingdom.All.FirstOrDefault(k => k.StringId == value.Substring(8))
                : value.StartsWith("clan:", StringComparison.Ordinal) ? Clan.All.FirstOrDefault(c => c.StringId == value.Substring(5)) : null;
            else if (type == typeof(IMapPoint)) found = value.StartsWith("settlement:", StringComparison.Ordinal) ? (IMapPoint)Settlement.Find(value.Substring(11))
                : value.StartsWith("party:", StringComparison.Ordinal) ? MobileParty.All.FirstOrDefault(p => p.StringId == value.Substring(6)) : null;
            else if (type == typeof(Workshop)) found = ReignObjectResolver.FindWorkshop(value);
            else if (type == typeof(WorkshopType)) found = MBObjectManager.Instance.GetObject<WorkshopType>(value);
            else if (type == typeof(Ship)) found = MobileParty.All.SelectMany(p => p.Ships).FirstOrDefault(s => ReignAssetTransfer.ShipId(s) == value);
            else if (type == typeof(Army)) found = MobileParty.All.FirstOrDefault(p => p.StringId == value && p.Army?.LeaderParty == p)?.Army;
            else if (type == typeof(Alley)) { int split = value.LastIndexOf(':'); var alleys = split < 0 ? null : Settlement.Find(value.Substring(0, split))?.Alleys; int index; if (alleys != null && int.TryParse(value.Substring(split + 1), out index) && index >= 0 && index < alleys.Count()) found = alleys[index]; }
            else if (type == typeof(CampaignTime)) { double day = value == "now" ? CampaignTime.Now.ToDays : double.Parse(value, CultureInfo.InvariantCulture); if (double.IsNaN(day) || double.IsInfinity(day) || day < 0) throw new InvalidOperationException("Use a finite campaign day."); return CampaignTime.Days((float)day); }
            else if (type == typeof(CampaignVec2))
            {
                var parts = value.Split(','); if (parts.Length != 3 || parts[2] != "land" && parts[2] != "sea") throw new InvalidOperationException("Use x,y,land or x,y,sea.");
                return new CampaignVec2(new Vec2((float)ResolveValue(typeof(float), parts[0], false), (float)ResolveValue(typeof(float), parts[1], false)), parts[2] == "land");
            }
            else if (type == typeof(ItemRosterElement))
            {
                var parts = value.Split(','); if (parts.Length < 2 || parts.Length > 3) throw new InvalidOperationException("Use itemId,count[,modifierId].");
                var item = MBObjectManager.Instance.GetObject<ItemObject>(parts[0]) ?? throw new InvalidOperationException("Unknown item ID.");
                int count = BoundedCount(parts[1]);
                var modifier = parts.Length == 3 ? MBObjectManager.Instance.GetObject<ItemModifier>(parts[2]) ?? throw new InvalidOperationException("Unknown modifier ID.") : null;
                return new ItemRosterElement(new EquipmentElement(item, modifier), count);
            }
            else if (type == typeof(TroopRoster) || type == typeof(FlattenedTroopRoster))
            {
                var rows = JArray.Parse(value); if (rows.Count == 0 || rows.Count > 256) throw new InvalidOperationException("Select 1–256 troop rows.");
                var roster = TroopRoster.CreateDummyTroopRoster();
                foreach (JObject row in rows)
                {
                    var troop = MBObjectManager.Instance.GetObject<CharacterObject>(row.Value<string>("id")) ?? throw new InvalidOperationException("Unknown troop ID.");
                    int count = BoundedCount(row.Value<string>("count")); int wounded = row.Value<int?>("wounded") ?? 0;
                    if (wounded < 0 || wounded > count) throw new InvalidOperationException("Wounded count must be within the selected count.");
                    roster.AddToCounts(troop, count, false, wounded);
                }
                return type == typeof(TroopRoster) ? (object)roster : new FlattenedTroopRoster(roster);
            }
            else if (type == typeof(Dictionary<MobileParty, float>))
            {
                var rows = JObject.Parse(value); if (rows.Count > 128) throw new InvalidOperationException("At most 128 contributing parties are supported.");
                var contributions = new Dictionary<MobileParty, float>();
                foreach (var row in rows) { var party = (MobileParty)ResolveValue(typeof(MobileParty), row.Key, false); float amount = (float)ResolveValue(typeof(float), row.Value.ToString(), false); if (amount < 0) throw new InvalidOperationException("Contributions cannot be negative."); contributions.Add(party, amount); }
                return contributions;
            }
            return found ?? throw new InvalidOperationException("No exact " + TypeName(type) + " entity matches '" + value + "'.");
        }
        private static int BoundedCount(string value) { int n = int.Parse(value, CultureInfo.InvariantCulture); if (n < 1 || n > 10000) throw new InvalidOperationException("Counts must be 1–10,000."); return n; }
        private static PartyBase ResolveParty(string value) => value.StartsWith("party:", StringComparison.Ordinal)
            ? MobileParty.All.FirstOrDefault(p => p.StringId == value.Substring(6))?.Party
            : value.StartsWith("settlement:", StringComparison.Ordinal) ? Settlement.Find(value.Substring(11))?.Party : null;

        private static void ValidateContext(NativeActionDefinition d, object[] values)
        {
            string assemblyHash = "";
            if (d.Id == "native.GiveItem.ApplyForHeroes")
                using (var file = System.IO.File.OpenRead(typeof(Hero).Assembly.Location))
                using (var sha = SHA256.Create())
                    assemblyHash = string.Concat(sha.ComputeHash(file).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            bool inSettlement = Hero.MainHero?.CurrentSettlement != null;
            bool conversationAgent = !inSettlement || d.Id != "native.RemoveCompanion.ApplyByFire"
                || (TaleWorlds.MountAndBlade.Mission.Current != null
                    && SandBox.Conversation.ConversationMission.OneToOneConversationAgent != null);
            string limitation = KnownInvocationLimitation(d.Id, inSettlement, conversationAgent, assemblyHash);
            if (limitation.Length > 0) throw new InvalidOperationException(limitation);
            if (d.Type.EndsWith("BreakInOutBesiegedSettlementAction", StringComparison.Ordinal)
                && Settlement.CurrentSettlement?.SiegeEvent == null)
                throw new InvalidOperationException("A current besieged settlement is required for this encounter operation.");
            if (d.Type.EndsWith("SiegeAftermathAction", StringComparison.Ordinal)
                && !values.OfType<Settlement>().Any(s => s.SiegeEvent != null))
                throw new InvalidOperationException("A live siege is required for siege aftermath.");
            if (values.OfType<Hero>().Any(h => h.IsChild) && (d.Type.EndsWith("MarriageAction", StringComparison.Ordinal)
                || d.Type.EndsWith("MakePregnantAction", StringComparison.Ordinal)))
                throw new InvalidOperationException("This native relationship operation requires adults.");
        }

        internal static string KnownInvocationLimitation(string id, bool playerInSettlement,
            bool hasConversationAgent, string nativeAssemblySha256)
        {
            if (id == "native.RemoveCompanion.ApplyByFire" && playerInSettlement && !hasConversationAgent)
                return "Native settlement dismissal requires its live one-to-one conversation agent. Use a valid native dismissal scene or an open-map test fixture.";
            if (id == "native.GiveItem.ApplyForHeroes" && string.Equals(nativeAssemblySha256,
                "1f8e33e2ed73e6ec653d7629180afb70649ddc6e5bd1657a802a264efda1c3ae", StringComparison.OrdinalIgnoreCase))
                return "This installed native hero-gift implementation subtracts the item from both inventories. Invocation is disabled for this exact assembly; use protected give.item or the separately verified native party-gift primitive.";
            return "";
        }

        private static readonly string[] ObservedProperties = { "StringId", "Name", "IsAlive", "IsDead", "IsDisabled", "IsPrisoner", "IsPregnant", "IsFugitive", "IsTraveling", "IsReleased", "DeathMark", "HeroState",
            "Gold", "PartyTradeGold", "HitPoints", "Clan", "CompanionOf", "Spouse", "Father", "Mother", "PartyBelongedTo", "CurrentSettlement", "StayingInSettlement",
            "Leader", "LeaderHero", "LeaderParty", "Kingdom", "RulingClan", "OwnerClan", "Owner", "Governor", "GovernorOf", "IsUnderMercenaryService", "Influence", "Renown",
            "IsEliminated", "IsActive", "IsDisbanding", "Army", "Position", "DefaultBehavior", "TargetSettlement", "TargetParty", "WorkshopType",
            "Town", "Village", "State", "VillageState", "Loyalty", "Prosperity", "Security", "Hearth", "Militia", "IsInRaftState", "Health", "MaxHitPoints", "HitPointsRatio", "SettlementHitPoints", "CrimeRating", "MainHeroCrimeRating", "Type",
            "TargetPosition", "DesiredAiNavigationType", "IsTargetingPort", "IsTransitionInProgress", "StartTransitionNextFrameToExitFromPort", "BribePaid" };
        private static JObject Observe(ParameterInfo[] parameters, object[] values)
        {
            var result = new JObject { ["mainHero"] = State(Hero.MainHero), ["mainParty"] = State(MobileParty.MainParty),
                ["aliveHeroes"] = Hero.AllAliveHeroes.Count(), ["kingdoms"] = Kingdom.All.Count(), ["mobileParties"] = MobileParty.All.Count() };
            for (int i = 0; i < parameters.Length; i++) result[parameters[i].Name] = State(values[i]);
            var heroes = values.OfType<Hero>().Concat(new[] { Hero.MainHero }).Where(h => h != null).Distinct().ToList();
            result["relations"] = new JArray(heroes.SelectMany((first, i) => heroes.Skip(i + 1).Select(second =>
                new JObject { ["first"] = first.StringId, ["second"] = second.StringId, ["value"] = first.GetRelation(second) })));
            var factions = values.OfType<IFaction>().Distinct().ToList();
            result["wars"] = new JArray(factions.SelectMany((first, i) => factions.Skip(i + 1).Select(second =>
                ObserveFactionPair(first, second))));
            try { result["romanticStates"] = ObserveRomanticStates(Romance.RomanticStateList, heroes); }
            catch { result["romanticStates"] = ObserveRomanticStates(null, heroes); }
            return result;
        }
        internal static bool CanObservePersistentTribute(bool distinctLivingKingdoms, bool hasDiplomacyModel,
            bool constantWar, bool shallowStance) => distinctLivingKingdoms && hasDiplomacyModel && !constantWar && !shallowStance;

        private static JObject ObserveFactionPair(IFaction first, IFaction second)
        {
            // Preserve the native model-aware war query. For persistent pairs it
            // already resolves the stance; querying a new pair independently can
            // create a default stance in the licensed engine.
            var result = new JObject { ["first"] = first.StringId, ["second"] = second.StringId,
                ["atWar"] = FactionManager.IsAtWarAgainstFaction(first, second) };
            result["tribute"] = new JObject { ["available"] = false, ["reason"] = "persistent kingdom stance unavailable" };
            try
            {
                var a = first as Kingdom; var b = second as Kingdom;
                var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.DiplomacyModel;
                bool kingdoms = a != null && b != null && a != b && !a.IsEliminated && !b.IsEliminated;
                if (!kingdoms || model == null || !CanObservePersistentTribute(kingdoms, model != null,
                    model.IsAtConstantWar(first, second), model.GetShallowDiplomaticStance(first, second).HasValue)) return result;
                StanceLink stance = a.GetStanceWith(b);
                result["tribute"] = new JObject { ["available"] = true,
                    ["firstToSecondDaily"] = stance.GetDailyTributeToPay(first),
                    ["secondToFirstDaily"] = stance.GetDailyTributeToPay(second),
                    ["installments"] = stance.DailyTributeInstallments,
                    ["remainingPayments"] = stance.GetRemainingTributePaymentCount(),
                    ["totalFirstToSecond"] = stance.Faction1 == first ? stance.TotalTributePaidFrom1To2 : stance.TotalTributePaidFrom2To1,
                    ["totalSecondToFirst"] = stance.Faction1 == first ? stance.TotalTributePaidFrom2To1 : stance.TotalTributePaidFrom1To2 };
            }
            catch { result["tribute"] = new JObject { ["available"] = false, ["reason"] = "native tribute read failed" }; }
            return result;
        }

        internal static JObject ObserveRomanticStates(IEnumerable<Romance.RomanticState> states, IEnumerable<Hero> participants)
        {
            const int limit = 32;
            var result = new JObject { ["schema"] = "reign-native-romantic-observation-v1", ["available"] = states != null && participants != null,
                ["total"] = 0, ["truncated"] = false, ["rows"] = new JArray() };
            if (states == null || participants == null) return result;
            var scoped = new HashSet<Hero>(participants.Where(h => h != null && !string.IsNullOrEmpty(h.StringId)));
            var rows = states.Select((state, index) => new { state, index })
                .Where(row => row.state != null && scoped.Contains(row.state.Person1) && scoped.Contains(row.state.Person2))
                .OrderBy(row => row.state.Person1.StringId, StringComparer.Ordinal)
                .ThenBy(row => row.state.Person2.StringId, StringComparer.Ordinal).ThenBy(row => row.index).ToList();
            result["total"] = rows.Count; result["truncated"] = rows.Count > limit;
            // Retain duplicate native records and their positions; collapsing them
            // could hide a corrupt state or falsely prove a relationship change.
            result["rows"] = new JArray(rows.Take(limit).Select(row => new JObject {
                ["nativeIndex"] = row.index, ["person1Id"] = row.state.Person1.StringId, ["person2Id"] = row.state.Person2.StringId,
                ["level"] = row.state.Level.ToString(), ["levelValue"] = (int)row.state.Level }));
            return result;
        }
        private static JToken State(object value)
        {
            if (value == null) return JValue.CreateNull();
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string) return new JValue(value.ToString());
            var state = new JObject { ["type"] = type.Name };
            foreach (string name in ObservedProperties)
            {
                JToken observed = ObservePublicMember(value, name);
                if (observed != null) state[name] = observed;
            }
            if (value is Hero hero) state["clanState"] = State(hero.CompanionOf ?? hero.Clan);
            if (value is MobileParty party)
            {
                state["members"] = Roster(party.MemberRoster); state["prisoners"] = Roster(party.PrisonRoster); state["items"] = Items(party.ItemRoster); state["clanState"] = State(party.ActualClan);
                try
                {
                    var disband = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IDisbandPartyCampaignBehavior>();
                    state["IsWaitingForDisband"] = disband == null ? (JToken)new JValue("unavailable") : new JValue(disband.IsPartyWaitingForDisband(party));
                }
                catch { state["IsWaitingForDisband"] = "unavailable"; }
            }
            if (value is PartyBase partyBase) { state["members"] = Roster(partyBase.MemberRoster); state["prisoners"] = Roster(partyBase.PrisonRoster); state["items"] = Items(partyBase.ItemRoster); state["goldHolder"] = State((object)partyBase.MobileParty ?? partyBase.Settlement?.SettlementComponent); }
            if (value is Settlement settlement) { state["componentState"] = State(settlement.SettlementComponent); }
            if (value is Kingdom kingdom) state["activePolicyIds"] = new JArray(kingdom.ActivePolicies.Select(p => p.StringId));
            if (value is TroopRoster roster) state["troops"] = Roster(roster);
            if (value is ItemRosterElement element) { state["item"] = element.EquipmentElement.Item?.StringId; state["modifier"] = element.EquipmentElement.ItemModifier?.StringId; state["count"] = element.Amount; }
            return state;
        }
        private static JToken ObservePublicMember(object value, string name)
        {
            // Only the fixed observation allowlist can expose native fields or getters.
            if (value == null || !ObservedProperties.Contains(name)) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var property = value.GetType().GetProperty(name, flags);
            var field = property == null ? value.GetType().GetField(name, flags) : null;
            if (property == null && field == null || property != null && property.GetIndexParameters().Length > 0) return null;
            try { return Reference(property != null ? property.GetValue(value, null) : field.GetValue(value)); }
            catch { return new JValue("unavailable"); }
        }
        private static JToken Reference(object value)
        {
            if (value == null) return JValue.CreateNull();
            var id = value.GetType().GetProperty("StringId");
            return new JValue(id != null ? id.GetValue(value, null)?.ToString() : value.ToString());
        }
        private static JToken Roster(TroopRoster roster) => roster == null ? JValue.CreateNull() : (JToken)new JObject
        { ["totalRows"] = roster.Count, ["truncated"] = roster.Count > 512, ["rows"] = new JArray(roster.GetTroopRoster().Take(512).Select(r => new JObject { ["id"] = r.Character.StringId, ["count"] = r.Number, ["wounded"] = r.WoundedNumber, ["xp"] = r.Xp })) };
        private static JToken Items(ItemRoster roster) => roster == null ? JValue.CreateNull() : (JToken)new JObject
        { ["totalRows"] = roster.Count, ["truncated"] = roster.Count > 512, ["rows"] = new JArray(roster.Take(512).Select(r => new JObject { ["id"] = r.EquipmentElement.Item?.StringId, ["modifier"] = r.EquipmentElement.ItemModifier?.StringId, ["count"] = r.Amount })) };
        private static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    }
}
