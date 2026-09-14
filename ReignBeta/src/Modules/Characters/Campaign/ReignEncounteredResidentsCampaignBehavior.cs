using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using ReignBeta.Shared.Characters;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal sealed class EncounteredResident
    {
        public string Id, HeroId, HomeId, LocationId, SourceTemplateId, SkillsTemplateId, SlotKey;
        public string Name, Role, BodyProperties;
        public string NativeSpawnTag, NativeActionSet;
        public uint ClothingColor1, ClothingColor2;
        public bool Initialized, Military, ReleasedFromDuty, InitialPortraitCompleted, Recruited, Retired;
        public string SourcePartyId, ManpowerTemplateId, RecruitmentReceipt, DutyReleaseReceipt;
        public int RecruitmentGold;
        public bool RecruitmentGoldPaid;
        public double MetDay;
        public List<ResidentEquipmentPart> Equipment = new List<ResidentEquipmentPart>();
    }

    internal sealed class ResidentEquipmentPart
    {
        public int Slot;
        public string ItemId, ModifierId;
    }

    /// <summary>
    /// Native service agents remain role agents. Their saved personal identity is a distinct Hero,
    /// so no shared civilian/troop template is renamed and native service dialogs remain intact.
    /// </summary>
    internal sealed partial class ReignEncounteredResidentsCampaignBehavior : CampaignBehaviorBase
    {
        internal static ReignEncounteredResidentsCampaignBehavior Instance { get; private set; }
        private List<EncounteredResident> _residents = new List<EncounteredResident>();
        private readonly Dictionary<IAgentOriginBase, string> _originSlots = new Dictionary<IAgentOriginBase, string>();
        private readonly Dictionary<Agent, EncounteredResident> _agents = new Dictionary<Agent, EncounteredResident>();
        private readonly Dictionary<IAgentOriginBase, Tuple<uint, uint>> _colors = new Dictionary<IAgentOriginBase, Tuple<uint, uint>>();
        private readonly HashSet<Location> _preparedLocations = new HashSet<Location>();
        private readonly Dictionary<LocationCharacter, Tuple<Location, LocationCharacter>> _personalRoles
            = new Dictionary<LocationCharacter, Tuple<Location, LocationCharacter>>();
        private string _pendingVisitHeroId;
        private float _pendingVisitSeconds;

        public override void RegisterEvents()
        {
            Instance = this;
            ReignEncounteredResidentPatches.Install();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, _ => ClearScene());
        }

        public override void SyncData(IDataStore dataStore)
        {
            List<string> chunks = dataStore.IsSaving
                ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_residents)) : null;
            dataStore.SyncData("_reign_encountered_residents_v1", ref chunks);
            if (!dataStore.IsLoading) return;
            _residents = chunks == null || chunks.Count == 0 ? new List<EncounteredResident>()
                : JsonConvert.DeserializeObject<List<EncounteredResident>>(ReignSavePayloadCodec.Decode(chunks))
                    ?? throw new InvalidOperationException("Encountered resident save data is invalid.");
            ClearScene();
        }

        private void ClearScene()
        {
            // A Location can outlive its mission. Undo our body-bearing role objects
            // before dropping identity bindings so a cached role cannot become a ghost.
            foreach (var personal in _personalRoles.Keys.ToList()) RestoreRolePrototype(personal);
            _originSlots.Clear(); _agents.Clear(); _colors.Clear(); _preparedLocations.Clear();
        }

        private void RestoreRolePrototype(LocationCharacter personal)
        {
            if (!_personalRoles.TryGetValue(personal, out var prior)) return;
            _personalRoles.Remove(personal);
            if (!prior.Item1.GetCharacterList().Contains(personal)) return;
            prior.Item1.RemoveLocationCharacter(personal);
            if (prior.Item2 != null && !prior.Item1.GetCharacterList().Contains(prior.Item2))
                prior.Item1.AddCharacter(prior.Item2);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (var record in _residents.Where(r => r.Initialized))
                Resolve(record)?.SetHasMet();
            foreach (string menu in new[] { "town", "castle", "village" })
                starter.AddGameMenuOption(menu, "reign_visit_homes_" + menu, "Visit the homes",
                    args => {
                        args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                        Settlement settlement = Settlement.CurrentSettlement;
                        return settlement != null && (settlement.IsFortification || settlement.IsVillage)
                            && !ReignCampaignInitializationGate.IsPending;
                    }, args => {
                        Settlement settlement = Settlement.CurrentSettlement;
                        if (settlement == null) return;
                        args.MapState?.ExitMenuMode();
                        ReignPartyChatScreenManager.OpenHomes(settlement);
                    }, false, 5);
        }

        internal IEnumerable<EncounteredResident> Records => _residents;
        internal EncounteredResident Find(Hero hero) => hero == null ? null : _residents.FirstOrDefault(r => r.HeroId == hero.StringId);
        internal Hero Resolve(EncounteredResident record) => string.IsNullOrEmpty(record?.HeroId) ? null : Hero.Find(record.HeroId);
        private EncounteredResident SlotResident(string slot) => _residents.FirstOrDefault(r => r.SlotKey == slot && !r.Recruited && !r.Retired);
        internal Hero ResolveAgent(Agent agent) => agent != null && _agents.TryGetValue(agent, out var record)
            ? Resolve(record) : (agent?.Character as CharacterObject)?.HeroObject;
        internal Hero ConversationHero => ResolveAgent(TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager.OneToOneConversationAgent as Agent)
            ?? CharacterObject.OneToOneConversationCharacter?.HeroObject;

        internal Equipment PortraitEquipment(Hero hero)
        {
            Agent present = Mission.Current?.Agents.FirstOrDefault(a => a.IsActive() && ResolveAgent(a) == hero);
            return present?.SpawnEquipment ?? hero?.CivilianEquipment;
        }

        internal Tuple<uint, uint> PortraitColors(Hero hero)
        {
            var record = Find(hero);
            Agent present = Mission.Current?.Agents.FirstOrDefault(a => a.IsActive() && ResolveAgent(a) == hero);
            if (present?.Origin != null && _colors.TryGetValue(present.Origin, out var colors)) return colors;
            return Tuple.Create(hero.Clan?.Color ?? record.ClothingColor1, hero.Clan?.Color2 ?? record.ClothingColor2);
        }

        internal List<Hero> ResidentsAt(Settlement settlement)
        {
            if (settlement == null) return new List<Hero>();
            return _residents.Where(r => r.Initialized).Select(r => new { Record = r, Hero = Resolve(r) })
                .Where(x => x.Hero != null && !x.Record.Retired && !x.Record.Recruited && EncounteredResidentRules.IsAvailable(x.Record.HomeId,
                    x.Hero.CurrentSettlement?.StringId, settlement.StringId, true, x.Hero.IsAlive,
                    x.Hero.IsPrisoner, x.Hero.PartyBelongedTo != null,
                    x.Hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge))
                .Select(x => x.Hero).OrderBy(h => h.Name.ToString(), StringComparer.CurrentCulture).ToList();
        }

        internal bool CanMeet(Agent agent)
        {
            var source = agent?.Character as CharacterObject;
            if (agent == null || source == null || !agent.IsHuman || !agent.IsActive() || source.IsHero
                || agent == Agent.Main || Settlement.CurrentSettlement == null || agent.Age < 18
                || (Agent.Main != null && agent.IsEnemyOf(Agent.Main))) return false;
            if (source.Occupation == Occupation.Special) return false;
            // Only ordinary settlement spawns have a stable native service slot.
            return agent.Origin != null && _originSlots.ContainsKey(agent.Origin);
        }

        internal void PrepareLocationCharacter(LocationCharacter character, Location location)
        {
            if (character?.AgentOrigin == null || character.Character == null || character.Character.IsHero
                || location == null || Settlement.CurrentSettlement == null) return;
            if (_originSlots.ContainsKey(character.AgentOrigin)) return;
            string prefix = Settlement.CurrentSettlement.StringId + "|" + location.StringId + "|"
                + character.Character.StringId + "|" + (character.SpecialTargetTag ?? "");
            int ordinal = 0;
            foreach (var item in location.GetCharacterList())
            {
                if (ReferenceEquals(item, character)) break;
                if (item.Character?.StringId == character.Character.StringId && item.SpecialTargetTag == character.SpecialTargetTag) ordinal++;
            }
            _originSlots[character.AgentOrigin] = prefix + "|" + ordinal;
        }

        internal void RestoreNativePopulation()
        {
            Location location = CampaignMission.Current?.Location;
            Settlement settlement = Settlement.CurrentSettlement;
            if (location == null || settlement == null || !_preparedLocations.Add(location)) return;
            var original = location.GetCharacterList().Where(c => c.Character != null && !c.Character.IsHero).ToList();
            foreach (var character in original) PrepareLocationCharacter(character, location);
            var unused = original.ToList();
            var available = ResidentsAt(settlement);
            foreach (var record in _residents.Where(r => r.LocationId == location.StringId && r.HomeId == settlement.StringId
                && available.Contains(Resolve(r))).OrderBy(r => r.MetDay))
            {
                Hero hero = Resolve(record);
                CharacterObject source = CharacterObject.Find(record.SourceTemplateId);
                if (source == null) continue;
                LocationCharacter prototype = unused.FirstOrDefault(c => _originSlots.TryGetValue(c.AgentOrigin, out var key) && key == record.SlotKey)
                    ?? unused.FirstOrDefault(c => c.Character.StringId == source.StringId && c.SpecialTargetTag == record.NativeSpawnTag)
                    ?? unused.FirstOrDefault(c => c.Character.Occupation == source.Occupation && c.Character.IsFemale == source.IsFemale
                        && c.AgentData.AgentAge >= 18);
                if (prototype != null) { unused.Remove(prototype); location.RemoveLocationCharacter(prototype); }
                var data = new AgentData(new TaleWorlds.CampaignSystem.AgentOrigins.SimpleAgentOrigin(source))
                    .Monster(TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(source.Race, "_settlement")).Age((int)hero.Age)
                    .BodyProperties(hero.BodyProperties).NoHorses(true);
                LocationCharacter.AddBehaviorsDelegate behaviors = prototype?.AddBehaviors
                    ?? new LocationCharacter.AddBehaviorsDelegate(SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors);
                var personal = new LocationCharacter(data, behaviors,
                    prototype?.SpecialTargetTag ?? record.NativeSpawnTag ?? "npc_common", prototype?.FixedLocation ?? false,
                    prototype?.CharacterRelation ?? LocationCharacter.CharacterRelations.Neutral,
                    prototype?.ActionSetCode ?? record.NativeActionSet, prototype?.UseCivilianEquipment ?? true,
                    false, prototype?.SpecialItem, false, false, false, prototype?.AfterAgentCreated);
                if (prototype != null)
                    foreach (var bone in prototype.PrefabNamesForBones) personal.PrefabNamesForBones[bone.Key] = bone.Value;
                // Preserve identity independently of today's native template/count/ordinal choices.
                _originSlots[personal.AgentOrigin] = record.SlotKey;
                _personalRoles[personal] = Tuple.Create(location, prototype);
                location.AddCharacter(personal);
            }
            foreach (var character in unused)
            {
                string key = _originSlots[character.AgentOrigin];
                if (_residents.Any(r => r.SlotKey == key))
                    _originSlots[character.AgentOrigin] = key + "|replacement|" + Guid.NewGuid().ToString("N");
            }
        }

        internal void BeforeSpawn(AgentBuildData data)
        {
            if (data?.AgentOrigin == null) return;
            if (Find((data.AgentCharacter as CharacterObject)?.HeroObject) != null)
                _colors[data.AgentOrigin] = Tuple.Create(data.AgentClothingColor1, data.AgentClothingColor2);
            if (!_originSlots.TryGetValue(data.AgentOrigin, out var slot)) return;
            _colors[data.AgentOrigin] = Tuple.Create(data.AgentClothingColor1, data.AgentClothingColor2);
            var resident = SlotResident(slot);
            Hero hero = Resolve(resident);
            if (hero == null || !resident.Initialized || !ResidentsAt(Settlement.CurrentSettlement).Contains(hero)) return;
            data.BodyProperties(hero.BodyProperties).Age((int)hero.Age).Equipment(hero.CivilianEquipment)
                .FixedEquipment(true).ClothingColor1(resident.ClothingColor1).ClothingColor2(resident.ClothingColor2);
            _colors[data.AgentOrigin] = Tuple.Create(resident.ClothingColor1, resident.ClothingColor2);
        }

        internal void AfterSpawn(Agent agent)
        {
            if (agent?.Origin == null || !_originSlots.TryGetValue(agent.Origin, out var slot)) return;
            var record = SlotResident(slot);
            if (record != null && record.Initialized && !ResidentsAt(Settlement.CurrentSettlement).Contains(Resolve(record)))
            {
                slot += "|replacement|" + Guid.NewGuid().ToString("N");
                _originSlots[agent.Origin] = slot;
                record = null;
            }
            Hero hero = Resolve(record);
            if (hero != null && ResidentsAt(Settlement.CurrentSettlement).Contains(hero)) _agents[agent] = record;
        }

        internal Hero Meet(Agent agent)
        {
            Hero existing = ResolveAgent(agent);
            if (existing != null) return existing;
            if (!CanMeet(agent)) return null;
            CharacterObject source = (CharacterObject)agent.Character;
            Settlement settlement = Settlement.CurrentSettlement;
            string slot = _originSlots[agent.Origin];
            var record = SlotResident(slot);
            if (record != null && record.Initialized && !ResidentsAt(settlement).Contains(Resolve(record)))
            {
                slot += "|replacement|" + Guid.NewGuid().ToString("N");
                _originSlots[agent.Origin] = slot;
                record = null;
            }
            if (record == null)
            {
                CultureObject culture = source.Culture ?? settlement.Culture;
                CharacterObject skills = SelectSkillsTemplate(source, culture, agent.IsFemale);
                var nativeNames = (agent.IsFemale ? culture.FemaleNameList : culture.MaleNameList).Select(n => n.ToString());
                string name = EncounteredResidentRules.ChooseName(culture.StringId, agent.IsFemale, nativeNames,
                    Hero.AllAliveHeroes.Select(h => h.Name.ToString()).Concat(_residents.Select(r => r.Name)),
                    _residents.Where(r => r.HomeId == settlement.StringId || r.MetDay > CampaignTime.Now.ToDays - 7).Select(r => r.Name),
                    max => MBRandom.RandomInt(max));
                record = new EncounteredResident {
                    Id = Guid.NewGuid().ToString("N"), SlotKey = slot, HomeId = settlement.StringId,
                    LocationId = slot.Split('|')[1], SourceTemplateId = source.StringId, SkillsTemplateId = skills.StringId,
                    Name = name, Role = source.Occupation.ToString(), BodyProperties = agent.BodyPropertiesValue.ToString(),
                    Military = EncounteredResidentRules.IsMilitary(source.Occupation.ToString(), source.StringId),
                    SourcePartyId = (agent.Origin.BattleCombatant as PartyBase)?.Id,
                    MetDay = CampaignTime.Now.ToDays,
                    Equipment = Enumerable.Range(0, 12).Where(i => agent.SpawnEquipment[(EquipmentIndex)i].Item != null)
                        .Select(i => new ResidentEquipmentPart { Slot = i,
                            ItemId = agent.SpawnEquipment[(EquipmentIndex)i].Item.StringId,
                            ModifierId = agent.SpawnEquipment[(EquipmentIndex)i].ItemModifier?.StringId }).ToList()
                };
                var nativeRole = CampaignMission.Current?.Location?.GetLocationCharacter(agent.Origin);
                record.NativeSpawnTag = nativeRole?.SpecialTargetTag;
                record.NativeActionSet = nativeRole?.ActionSetCode;
                if (_colors.TryGetValue(agent.Origin, out var colors))
                { record.ClothingColor1 = colors.Item1; record.ClothingColor2 = colors.Item2; }
                CaptureDutySource(record, agent, settlement);
                _residents.Add(record); // An interrupted construction retries this exact name and slot.
            }
            Hero hero = Resolve(record);
            if (!BodyProperties.FromString(record.BodyProperties, out var capturedBody))
                throw new InvalidOperationException("The encountered body snapshot is invalid.");
            if (hero == null)
            {
                CharacterObject skills = CharacterObject.Find(record.SkillsTemplateId);
                hero = HeroCreator.CreateSpecialHero(skills, settlement, null, null, (int)capturedBody.Age);
                record.HeroId = hero.StringId; // Save identity before any fallible finishing work.
            }
            if (!record.Initialized)
            {
                hero.Clan = null;
                hero.Father = null; hero.Mother = null; hero.Spouse = null;
                hero.SetName(new TextObject("{=!}" + record.Name), new TextObject("{=!}" + record.Name.Split(' ')[0]));
                hero.Culture = source.Culture ?? settlement.Culture;
                hero.SetNewOccupation(source.Occupation);
                // Modded service templates can carry notable/wanderer occupations.
                // The role agent keeps that job; the persistent resident must not
                // join the native notable or travelling-wanderer populations.
                if (hero.IsNotable || hero.IsLord || hero.IsWanderer) hero.SetNewOccupation(Occupation.Special);
                hero.StaticBodyProperties = capturedBody.StaticProperties;
                hero.Weight = capturedBody.Weight; hero.Build = capturedBody.Build;
                hero.SetBirthDay(CampaignTime.YearsFromNow(-capturedBody.Age));
                Equipment capturedEquipment = new Equipment();
                foreach (var part in record.Equipment)
                    capturedEquipment[(EquipmentIndex)part.Slot] = new EquipmentElement(
                        TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(part.ItemId),
                        string.IsNullOrEmpty(part.ModifierId) ? null : TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemModifier>(part.ModifierId));
                hero.CivilianEquipment.FillFrom(capturedEquipment);
                hero.BattleEquipment.FillFrom(capturedEquipment);
                CharacterObject stats = CharacterObject.Find(record.SkillsTemplateId);
                foreach (SkillObject skill in TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObjectTypeList<SkillObject>())
                    hero.SetSkillValue(skill, stats.GetSkillValue(skill));
                hero.Level = Math.Max(1, stats.Level);
                hero.HeroDeveloper.InitializeHeroDeveloper();
                hero.ChangeState(Hero.CharacterStates.Active);
                EnterSettlementAction.ApplyForCharacterOnly(hero, settlement);
                record.Initialized = true;
                ReignLog.Info("Encountered resident created hero=" + hero.StringId + " source=" + record.SourceTemplateId
                    + " stats=" + record.SkillsTemplateId + " home=" + record.HomeId);
            }
            hero.SetHasMet();
            _agents[agent] = record;
            ReignEncounteredResidentPatches.RefreshKnownName();
            return hero;
        }

        private static CharacterObject SelectSkillsTemplate(CharacterObject source, CultureObject culture, bool female)
        {
            string role = EncounteredResidentRules.SkillRole(source.Occupation.ToString(), source.StringId);
            if (role == "military") return source;
            var candidates = CharacterObject.All.Where(c => c.IsTemplate && c.Occupation == Occupation.Wanderer
                && c.Culture == culture && c.IsFemale == female && c.Race == source.Race).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("No native wanderer mechanics template matches this person's culture, sex and race.");
            Func<CharacterObject, int> score = c => role == "smith" ? c.GetSkillValue(DefaultSkills.Crafting) * 2 + c.GetSkillValue(DefaultSkills.Engineering)
                : role == "engineer" ? c.GetSkillValue(DefaultSkills.Engineering) * 2 + c.GetSkillValue(DefaultSkills.Crafting)
                : role == "performer" ? c.GetSkillValue(DefaultSkills.Charm)
                : role == "trader" ? c.GetSkillValue(DefaultSkills.Trade) * 2 + c.GetSkillValue(DefaultSkills.Charm)
                : role == "fighter" ? c.GetSkillValue(DefaultSkills.Athletics) + c.GetSkillValue(DefaultSkills.OneHanded) : 0;
            var best = candidates.OrderByDescending(score).Take(role == "resident" ? candidates.Count : Math.Min(3, candidates.Count)).ToList();
            return best[MBRandom.RandomInt(best.Count)];
        }

        internal bool SuppressAutomaticLocation(Hero hero) => Find(hero) is EncounteredResident resident
            && !resident.Recruited && hero.PartyBelongedTo == null && !hero.IsPrisoner;

        internal void VisitInPerson(Hero hero)
        {
            EncounteredResident resident = Find(hero);
            Settlement settlement = Settlement.CurrentSettlement;
            if (resident == null || !ResidentsAt(settlement).Contains(hero) || PlayerEncounter.LocationEncounter == null) return;
            Location location = settlement.LocationComplex?.GetLocationWithId(resident.LocationId);
            if (location == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("That person's usual location is unavailable."));
                return;
            }
            _pendingVisitHeroId = hero.StringId;
            _pendingVisitSeconds = 0;
            // Native menu Talk uses this controller. We target the saved service slot after spawning,
            // not a shared troop CharacterObject that could select a different citizen.
            PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(location);
        }

        internal void TickNativeVisit(float dt)
        {
            if (string.IsNullOrEmpty(_pendingVisitHeroId) || Agent.Main == null || !Agent.Main.IsActive()) return;
            _pendingVisitSeconds += dt;
            Hero hero = Hero.Find(_pendingVisitHeroId);
            if (!ResidentsAt(Settlement.CurrentSettlement).Contains(hero)) { _pendingVisitHeroId = null; return; }
            Agent target = _agents.FirstOrDefault(x => x.Value.HeroId == _pendingVisitHeroId && x.Key.IsActive()).Key;
            if (target != null && !target.IsEnemyOf(Agent.Main))
            {
                _pendingVisitHeroId = null;
                Mission.Current.GetMissionBehavior<SandBox.Missions.MissionLogics.MissionAgentHandler>()
                    .TeleportTargetAgentNearReferenceAgent(target, Agent.Main, false, true);
                TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager.SetupAndStartMissionConversation(target, Agent.Main, true);
            }
            else if (_pendingVisitSeconds > 15f)
            {
                _pendingVisitHeroId = null;
                InformationManager.DisplayMessage(new InformationMessage("This resident is not at their usual place right now. You can still visit their home."));
            }
        }

        internal void AgentRemoved(Agent agent, Agent killer, AgentState state)
        {
            if (agent == null || !_agents.TryGetValue(agent, out var record)) return;
            Hero hero = Resolve(record);
            if (hero != null && hero.IsAlive && !record.Recruited)
            {
                if (state == AgentState.Killed)
                { record.Retired = true; KillCharacterAction.ApplyByBattle(hero, ResolveAgent(killer)); }
                else if (state == AgentState.Unconscious) hero.HitPoints = Math.Max(1, (int)agent.Health);
            }
            _agents.Remove(agent);
        }

        internal JObject RuntimeSnapshot()
        {
            // Read-only, bounded evidence through the existing live-test heartbeat.
            // Hashes compare the real role body/outfit with the saved hero without
            // copying unbounded equipment or character documents into telemetry.
            string Fingerprint(string value)
            {
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value ?? "")))
                        .Replace("-", "").ToLowerInvariant();
            }
            string Outfit(Equipment equipment) => equipment == null ? "" : string.Join("|",
                Enumerable.Range(0, 12).Select(i => i + ":" + equipment[(EquipmentIndex)i].Item?.StringId
                    + ":" + equipment[(EquipmentIndex)i].ItemModifier?.StringId));
            Settlement home = Settlement.CurrentSettlement;
            var available = ResidentsAt(home).ToList();
            var here = _residents.Where(r => r.HomeId == home?.StringId).Take(64).ToList();
            var agents = Mission.Current?.Agents.Where(a => a.IsActive() && a.IsHuman
                && (CanMeet(a) || _agents.ContainsKey(a))).Take(64).ToList() ?? new List<Agent>();
            return new JObject {
                ["schema"] = "reign-encountered-resident-runtime-v1",
                ["capturedUtc"] = DateTime.UtcNow.ToString("O"),
                ["registeredCount"] = _residents.Count, ["localAvailableCount"] = available.Count,
                ["homeSettlementId"] = home?.StringId ?? "", ["locationId"] = CampaignMission.Current?.Location?.StringId ?? "",
                ["pendingVisitHeroId"] = _pendingVisitHeroId ?? "",
                ["nativeConversationHeroId"] = ConversationHero?.StringId ?? "",
                ["homes"] = ReignPartyChatScreenManager.ActiveViewModel?.HomesSnapshot() ?? new JObject(),
                ["residents"] = new JArray(here.Select(record => {
                    Hero hero = Resolve(record);
                    return new JObject { ["heroId"] = record.HeroId, ["name"] = record.Name,
                        ["sourceTemplateId"] = record.SourceTemplateId, ["skillsTemplateId"] = record.SkillsTemplateId,
                        ["role"] = record.Role, ["slotKey"] = record.SlotKey, ["available"] = available.Contains(hero),
                        ["initialized"] = record.Initialized, ["recruited"] = record.Recruited,
                        ["releasedFromDuty"] = record.ReleasedFromDuty, ["recruitmentGoldPaid"] = record.RecruitmentGoldPaid,
                        ["bodySha256"] = Fingerprint(hero?.BodyProperties.ToString()),
                        ["outfitSha256"] = Fingerprint(Outfit(hero?.CivilianEquipment)) };
                })),
                ["agents"] = new JArray(agents.Select(agent => new JObject {
                    ["agentIndex"] = agent.Index, ["sourceTemplateId"] = agent.Character.StringId,
                    ["nativeName"] = agent.Name, ["heroId"] = ResolveAgent(agent)?.StringId ?? "",
                    ["canMeet"] = CanMeet(agent), ["bodySha256"] = Fingerprint(agent.BodyPropertiesValue.ToString()),
                    ["outfitSha256"] = Fingerprint(Outfit(agent.SpawnEquipment)) }))
            };
        }

        internal JObject Metadata(Hero hero)
        {
            var record = Find(hero); if (record == null) return null;
            var home = Settlement.Find(record.HomeId);
            return new JObject { ["schema"] = "reign-encountered-resident-v1", ["residentId"] = record.Id,
                ["homeSettlementId"] = record.HomeId, ["homeSettlementName"] = home?.Name?.ToString() ?? "",
                ["homeOwnerClanLeaderId"] = home?.OwnerClan?.Leader?.StringId ?? "",
                ["homeRulerId"] = home?.OwnerClan?.Kingdom?.Leader?.StringId ?? "",
                ["homeKingdomId"] = home?.OwnerClan?.Kingdom?.StringId ?? "",
                ["sourceTemplateId"] = record.SourceTemplateId, ["skillsTemplateId"] = record.SkillsTemplateId,
                ["occupation"] = record.Role, ["military"] = record.Military, ["releasedFromDuty"] = record.ReleasedFromDuty,
                ["initialPortraitCompleted"] = record.InitialPortraitCompleted, ["recruited"] = record.Recruited };
        }
    }

    internal sealed class ReignEncounteredResidentMissionBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => (MissionBehaviorType)1;
        public override void OnMissionTick(float dt) => ReignEncounteredResidentsCampaignBehavior.Instance?.TickNativeVisit(dt);
        public override void OnAgentRemoved(Agent agent, Agent killer, AgentState state, KillingBlow blow)
            => ReignEncounteredResidentsCampaignBehavior.Instance?.AgentRemoved(agent, killer, state);
    }
}
