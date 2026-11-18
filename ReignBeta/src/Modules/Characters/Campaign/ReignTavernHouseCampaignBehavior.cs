using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Shared.Characters;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Integration;
using ReignBeta.Portraits.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Characters.Campaign
{
    /// <summary>Saved native identities own the house. The server negotiates; this behavior alone transfers gold and companions.</summary>
    public sealed partial class ReignTavernHouseCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignTavernHouseCampaignBehavior Instance => ReignServices.TavernHouse;
        private TavernHouseState _state = new TavernHouseState();
        private readonly PopulationIdentityIndex<TavernHousePerson> _identityIndex = new PopulationIdentityIndex<TavernHousePerson>(p => p.HeroId);
        private TavernHouseCastCatalog _catalog;
        private readonly RealTimeMaintenanceCadence _maintenanceCadence = new RealTimeMaintenanceCadence();
        private readonly PausedPopulationCatchup _pausedCatchup = new PausedPopulationCatchup();
        private TaleWorlds.CampaignSystem.Campaign _sessionCampaign;
        private DateTime _nextMaintenanceRetryUtc;
        private int _lastPausedDebt = -1;
        private bool _nativeProgressSinceObservation;
        private DateTime _nextPortraitUtc;
        private DateTime _nextRecruitmentRecoveryUtc;
        private int _recruitmentRecoveryIndex;
        private readonly Dictionary<string, DateTime> _portraitRetryUtc = new Dictionary<string, DateTime>();
        private string _authorizedRecruitId;
        private string _lastError = "";
        private bool _ready;
        private readonly TavernHouseHeroLookup<Hero> _passHeroes = new TavernHouseHeroLookup<Hero>(Hero.Find);
        private readonly Dictionary<string, List<TavernHousePerson>> _passPeople = new Dictionary<string, List<TavernHousePerson>>(StringComparer.Ordinal);
        private bool _populationPassActive;
        private long _lookupPassCount;
        private int _lastLookupCount, _lastLookupFallbackHits, _lastLookupDuplicateIds;

        private bool BeginPopulationPass()
        {
            if (_populationPassActive) return false;
            try
            {
                _passHeroes.Refresh(_state.People.Select(p => p.HeroId), Hero.DeadOrDisabledHeroes, Hero.AllAliveHeroes, h => h.StringId);
                foreach (var person in _state.People)
                {
                    if (!_passPeople.TryGetValue(person.TownId, out var people))
                        _passPeople.Add(person.TownId, people = new List<TavernHousePerson>());
                    people.Add(person);
                }
                _populationPassActive = true;
                _lookupPassCount++;
                return true;
            }
            catch { _passHeroes.Clear(); _passPeople.Clear(); throw; }
        }
        private void EndPopulationPass()
        {
            _lastLookupCount = _passHeroes.Count; _lastLookupFallbackHits = _passHeroes.FallbackHits;
            _lastLookupDuplicateIds = _passHeroes.DuplicateIds;
            _populationPassActive = false; _passHeroes.Clear(); _passPeople.Clear();
        }
        private Hero PopulationHero(string id) => _populationPassActive ? _passHeroes.Find(id) : Hero.Find(id);
        private IEnumerable<TavernHousePerson> PeopleInTown(string townId) => _populationPassActive
            ? (_passPeople.TryGetValue(townId, out var people) ? people : Enumerable.Empty<TavernHousePerson>())
            : _state.People.Where(p => p.TownId == townId);
        internal JObject RuntimeStatus() => new JObject { ["ready"] = _ready, ["lookupPassCount"] = _lookupPassCount,
            ["lookupCount"] = _lastLookupCount, ["lookupFallbackHits"] = _lastLookupFallbackHits,
            ["lookupDuplicateIds"] = _lastLookupDuplicateIds, ["maintenanceInFlight"] = _populationPassActive,
            ["pausedCatchupPending"] = _pausedCatchup.Pending, ["pausedCatchupBlocked"] = _pausedCatchup.Blocked, ["error"] = _lastError };

        private double Day => CampaignTime.Now.ToDays;
        private string Timeline => ReignServices.WorldHistory?.TimelineId ?? "main";
        public bool Ready => _ready;

        public override void RegisterEvents()
        {
            ReignServices.TavernHouse = this;
            ReignTavernHousePatches.Install();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, MaintainDaily);
        }

        public override void SyncData(IDataStore store)
        {
            List<string> chunks = store.IsSaving ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state)) : null;
            store.SyncData("_reign_tavern_house_v1", ref chunks);
            if (!store.IsLoading) return;
            _pausedCatchup.Request();
            _nextMaintenanceRetryUtc = DateTime.MinValue;
            _lastPausedDebt = -1;
            _identityIndex.Invalidate();
            _state = chunks == null || chunks.Count == 0 ? new TavernHouseState()
                : JsonConvert.DeserializeObject<TavernHouseState>(ReignSavePayloadCodec.Decode(chunks));
            TavernHouseRules.ValidateState(_state);
            foreach (var visit in _state.Visits.Where(v => v.PaymentState == "applying"))
            { visit.PaymentState = "recovery_required"; visit.Paid = false; }
            _ready = false; _authorizedRecruitId = null; _portraitRetryUtc.Clear();
            _populationPassActive = false; _passHeroes.Clear(); _passPeople.Clear();
            _nextRecruitmentRecoveryUtc = DateTime.MinValue; _recruitmentRecoveryIndex = 0;
        }

        private void OnSession(CampaignGameStarter starter)
        {
            _sessionCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
            _pausedCatchup.Request();
            // Native PlayerTownVisitCampaignBehavior owns this submenu; town_tavern is an option, not a menu.
            starter.AddGameMenuOption("town_backstreet", "reign_visit_madam", "Visit the Madam", args =>
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                return _ready && Settlement.CurrentSettlement?.IsTown == true && Hero.MainHero != null && !Hero.MainHero.IsChild
                    && Hero.MainHero.IsAlive && !Hero.MainHero.IsPrisoner && !ReignCampaignInitializationGate.IsPending;
            }, args =>
            {
                Settlement town = Settlement.CurrentSettlement;
                if (!EnsureTown(town)) return;
                args.MapState?.ExitMenuMode();
                ReignTavernHouseScreenManager.Open(town);
            }, false, 1);
            try
            {
                string path = Path.Combine(BasePath.Name, "Modules", "ReignBeta", "ModuleData", "reign_tavern_cast.json");
                _catalog = JsonConvert.DeserializeObject<TavernHouseCastCatalog>(File.ReadAllText(path));
                TavernHouseRules.ValidateCatalog(_catalog);
                var missing = Town.AllTowns.Where(t => !_catalog.Towns.Any(c => c.TownId == t.Settlement.StringId)).Select(t => t.Settlement.StringId).ToArray();
                if (missing.Length > 0) throw new InvalidOperationException("Tavern cast coverage is missing towns: " + string.Join(", ", missing));
                _ready = ReignTavernHousePatches.Ready;
                if (!_ready) throw new InvalidOperationException("Native tavern population protection hooks are unavailable.");
                _lastError = "";
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Tavern house initialization", ex); }
        }

        public TavernHousePerson GetPerson(Hero hero) => hero == null ? null : _identityIndex.Find(_state.People, hero.StringId);
        public bool IsActiveStaff(Hero hero) => TavernHouseRules.IsActive(GetPerson(hero));
        internal bool IsOwnedIdentity(Hero hero) => GetPerson(hero) != null;
        internal bool AllowsRecruitment(Hero hero) => !IsActiveStaff(hero) || _authorizedRecruitId == hero?.StringId;
        public IReadOnlyList<Hero> GetStaff(Settlement town) => town?.IsTown != true ? Array.Empty<Hero>()
            : PeopleInTown(town.StringId).Where(TavernHouseRules.IsActive).OrderByDescending(p => p.Madam)
                .ThenBy(p => p.Slot).Select(p => PopulationHero(p.HeroId)).Where(h => h != null && h.IsAlive).ToArray();
        public Hero GetMadam(Settlement town) => town?.IsTown != true ? null
            : _state.People.Where(p => p.TownId == town.StringId && p.Madam && TavernHouseRules.IsActive(p))
                .Select(p => PopulationHero(p.HeroId)).FirstOrDefault(h => h != null && h.IsAlive && !h.IsChild);
        public bool IsAvailable(Hero hero, Settlement town) => IsActiveStaff(hero) && town?.IsTown == true
            && GetPerson(hero).TownId == town.StringId && hero.IsAlive && !hero.IsChild && hero.Age < TavernHouseRules.RetirementAge
            && !hero.IsPrisoner && hero.CompanionOf == null && hero.Clan == null && hero.PartyBelongedTo == null
            && hero.CurrentSettlement == town && hero.HeroState == Hero.CharacterStates.Active;

        public bool EnsureTown(Settlement town)
        {
            if (!_ready || town?.IsTown != true || ReignCampaignInitializationGate.IsPending) return false;
            bool ownsPass = false;
            try
            {
                ownsPass = BeginPopulationPass();
                ResumeRecruitment();
                ReserveTown(town);
                MaintainTown(town, int.MaxValue);
                foreach (Hero hero in GetStaff(town)) ReignTavernHousePortraitSeed.TrySeed(hero, GetPerson(hero));
                return GetMadam(town) != null;
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Tavern house creation", ex); return false; }
            finally { if (ownsPass) EndPopulationPass(); }
        }

        private void ReserveTown(Settlement town)
        {
            if (_state.InitializedTownIds.Contains(town.StringId)) return;
            var cast = _catalog.Towns.Single(t => t.TownId == town.StringId);
            // Reserve the complete topology before allocating a Hero. Retry uses these same slots and identities.
            for (int i = 0; i < cast.People.Count; i++)
                _state.Slots.Add(new TavernHouseSlot { TownId = town.StringId, Index = i, Female = cast.People[i].Female, ReplacementDay = Day });
            _state.InitializedTownIds.Add(town.StringId);
        }

        private void OnTick(float dt)
        {
            if (DateTime.UtcNow < _nextMaintenanceRetryUtc) return;
            if (!_maintenanceCadence.TryTake(System.Diagnostics.Stopwatch.GetTimestamp(),
                    System.Diagnostics.Stopwatch.Frequency, dt > 0f)) return;
            using (ReignLog.MeasureWork("population.tavern", dt)) Maintain();
            ObservePausedCatchup(pausedPass: false);
        }

        internal void ApplicationTick()
        {
            if (Mission.Current != null) { _pausedCatchup.Request(); _lastPausedDebt = -1; return; }
            if (!_pausedCatchup.Pending || _pausedCatchup.Blocked || DateTime.UtcNow < _nextMaintenanceRetryUtc) return;
            bool current = _sessionCampaign != null && ReferenceEquals(_sessionCampaign, TaleWorlds.CampaignSystem.Campaign.Current);
            if (!current) return;
            if (_sessionCampaign.TimeControlMode != CampaignTimeControlMode.Stop) return;
            bool saving = ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || ReignSaveSyncCoordinator.IsSaveFinalizationPending;
            if (!_pausedCatchup.CanPoll(current, _ready && Hero.MainHero != null && !ReignCampaignInitializationGate.IsPending,
                    false, saving, ReignSaveSyncCoordinator.IsReadyForCampaign(ReignCampaignIdentity.CurrentCampaignId()))) return;
            if (!_maintenanceCadence.TryTake(System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency)) return;
            using (ReignLog.MeasureWork("population.tavern", 0f)) Maintain(queuePortraits: false);
            ObservePausedCatchup(pausedPass: true);
        }

        internal void CampaignEnded()
        {
            _sessionCampaign = null; _ready = false;
            _pausedCatchup.Request(); _lastPausedDebt = -1;
            _nativeProgressSinceObservation = false; _nextMaintenanceRetryUtc = default; _lastError = "";
            _passHeroes.Clear(); _passPeople.Clear(); _identityIndex.Invalidate();
        }

        private void ObservePausedCatchup(bool pausedPass)
        {
            if (!_ready || Hero.MainHero == null || ReignCampaignInitializationGate.IsPending || Mission.Current != null) return;
            if (!string.IsNullOrEmpty(_lastError)) return;
            try
            {
                // Recruitment may legitimately await a receipt or its five-second retry slot.
                // Only native population debt participates in stalled-creation detection.
                bool recruitmentPending = _state.People.Any(TavernHouseRules.HasPendingRecruitment);
                int pending = 0;
                var peopleById = _state.People.ToDictionary(p => p.Id, StringComparer.Ordinal);
                var slotsByTown = _state.Slots.GroupBy(s => s.TownId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
                foreach (var town in Town.AllTowns)
                {
                    if (town.Settlement.IsUnderSiege || town.Settlement.SiegeEvent != null) continue;
                    string townId = town.Settlement.StringId;
                    if (!_state.InitializedTownIds.Contains(townId)) { pending++; continue; }
                    if (!slotsByTown.TryGetValue(townId, out var slots))
                        throw new InvalidOperationException("Initialized tavern town has no saved slots; refusing repair.");
                    foreach (var slot in slots)
                    {
                        TavernHousePerson person = null;
                        if (!string.IsNullOrEmpty(slot.PersonId) && !peopleById.TryGetValue(slot.PersonId, out person))
                            throw new InvalidOperationException("Saved tavern slot references a missing person; refusing repair.");
                        if ((person != null && !person.Initialized) || TavernHouseRules.ReplacementDue(slot, Day)) pending++;
                    }
                }
                bool stalled = pausedPass && pending > 0 && pending == _lastPausedDebt && !_nativeProgressSinceObservation;
                if (_nativeProgressSinceObservation) _pausedCatchup.Progress();
                _nativeProgressSinceObservation = false;
                if (_lastPausedDebt >= 0 && pending < _lastPausedDebt) _pausedCatchup.Progress();
                _lastPausedDebt = pending;
                if (stalled) throw new InvalidOperationException("Pending tavern preparation made no progress; retry deferred.");
                _pausedCatchup.Observe(pending > 0 || recruitmentPending, false);
            }
            catch (Exception ex) { DeferMaintenance(ex); }
        }

        private void DeferMaintenance(Exception error)
        {
            _lastError = error.Message;
            _nextMaintenanceRetryUtc = DateTime.UtcNow.AddSeconds(30);
            _pausedCatchup.Observe(true, true);
            ReignLog.Exception("Tavern house maintenance", error);
        }

        private void Maintain(bool queuePortraits = true)
        {
            if (!_ready || ReignCampaignInitializationGate.IsPending || Hero.MainHero == null || Mission.Current != null) return;
            bool ownsPass = false;
            try
            {
                ownsPass = BeginPopulationPass();
                ResumeRecruitment();
                foreach (var visit in _state.Visits.Where(v => v.Paid && !v.Ended && v.ServerConfirmed))
                    if (Hero.MainHero.CurrentSettlement?.StringId != visit.TownId) visit.Ended = true;
                int remainingCreations = 4;
                foreach (var town in Town.AllTowns.OrderByDescending(t => t.Settlement == Hero.MainHero.CurrentSettlement))
                {
                    ReserveTown(town.Settlement);
                    // Initial preparation is bounded. Local entry finishes its own roster synchronously.
                    remainingCreations -= MaintainTown(town.Settlement, remainingCreations);
                }
                if (queuePortraits) QueuePortrait();
                _lastError = "";
            }
            catch (Exception ex) { DeferMaintenance(ex); }
            finally { if (ownsPass) EndPopulationPass(); }
        }

        private void MaintainDaily()
        {
            _pausedCatchup.Request();
            using (ReignLog.MeasureWork("population.tavern", 0f)) Maintain();
        }

        private void ResumeRecruitment()
        {
            if (DateTime.UtcNow < _nextRecruitmentRecoveryUtc || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null) return;
            var pending = _state.People.Where(TavernHouseRules.HasPendingRecruitment).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (pending.Length == 0) return;
            var person = pending[_recruitmentRecoveryIndex % pending.Length];
            _recruitmentRecoveryIndex = (_recruitmentRecoveryIndex + 1) % pending.Length;
            _nextRecruitmentRecoveryUtc = DateTime.UtcNow.AddSeconds(5);
            // The saved receipt was written only after explicit consent and price validation.
            // Resume one agreement at a time, even if native clan/party transfer already removed its roster card.
            TryRecruit(PopulationHero(person.HeroId), person.RecruitmentGold, person.RecruitmentReceipt, true, out string reason);
        }

        private int MaintainTown(Settlement town, int budget)
        {
            if (town.IsUnderSiege || town.SiegeEvent != null) return 0;
            int created = 0;
            foreach (var person in PeopleInTown(town.StringId).Where(TavernHouseRules.IsActive).ToArray())
            {
                Hero hero = PopulationHero(person.HeroId);
                if (hero == null) throw new InvalidOperationException("Saved tavern hero is missing; refusing to substitute: " + person.HeroId);
                if (hero.CompanionOf != null || hero.Clan != null || hero.PartyBelongedTo != null) Depart(person, true, "recruited");
                else if (!hero.IsAlive || hero.Age >= TavernHouseRules.RetirementAge)
                {
                    Depart(person, false, hero.IsAlive ? "retired_at_40" : "died");
                    if (hero.IsAlive) hero.SetNewOccupation(Occupation.Townsfolk);
                }
            }
            PromoteMadam(town);
            foreach (var slot in _state.Slots.Where(s => s.TownId == town.StringId).OrderBy(s => s.Index))
            {
                if (budget <= 0) break;
                var reserved = string.IsNullOrEmpty(slot.PersonId) ? null : _state.People.Single(p => p.Id == slot.PersonId);
                if (reserved != null && !reserved.Initialized) { CreatePerson(town, slot, reserved); created++; if (--budget == 0) break; }
                else if (TavernHouseRules.ReplacementDue(slot, Day)) { CreatePerson(town, slot, null); created++; if (--budget == 0) break; }
            }
            PromoteMadam(town);
            return created;
        }

        private void Depart(TavernHousePerson person, bool recruited, string reason)
        {
            _pausedCatchup.Request();
            if (!TavernHouseRules.IsActive(person)) return;
            person.Recruited = recruited; person.Retired = !recruited; person.Madam = false;
            person.LeftDay = Day; person.DepartureReason = reason;
            TavernHouseSlot slot = _state.Slots.Single(s => s.TownId == person.TownId && s.Index == person.Slot);
            slot.PersonId = ""; slot.Generation++;
            slot.ReplacementDay = TavernHouseRules.ReplacementDay(Day, recruited);
            PromoteMadam(Settlement.Find(person.TownId));
        }

        internal void CompleteTemporaryDeparture(Hero hero)
        {
            var person = GetPerson(hero);
            if (person != null) Depart(person, false, "temporary_guest");
        }

        private void PromoteMadam(Settlement town)
        {
            if (town == null) return;
            var eligible = PeopleInTown(town.StringId).Where(TavernHouseRules.IsActive)
                .Where(p => PopulationHero(p.HeroId)?.IsAlive == true && PopulationHero(p.HeroId)?.IsChild == false).ToArray();
            TavernHousePerson successor = TavernHouseRules.SelectMadam(eligible,
                p => PopulationHero(p.HeroId).GetSkillValue(DefaultSkills.Charm), p => PopulationHero(p.HeroId).GetSkillValue(DefaultSkills.Roguery));
            if (successor != null) successor.Madam = true;
        }

        private void CreatePerson(Settlement town, TavernHouseSlot slot, TavernHousePerson person)
        {
            TavernHouseCastTown cast = _catalog.Towns.Single(t => t.TownId == town.StringId);
            TavernHouseCastPerson authored = cast.People[slot.Index];
            CultureObject culture = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<CultureObject>(cast.CultureId)
                ?? throw new InvalidOperationException("Authored tavern culture is unavailable: " + cast.CultureId);
            CharacterObject donor = slot.Female ? culture.Townswoman : culture.Townsman;
            if (donor == null || donor.BodyPropertyRange == null) throw new InvalidOperationException("Native civilian donor unavailable for " + cast.CultureId);
            string identity = slot.Generation == 0 ? authored.Id : authored.Id + "_g" + slot.Generation;
            var random = new Random(WandererPopulationRules.Seed(identity));
            int age = slot.Generation == 0 ? authored.Age : 19 + random.Next(12);
            if (person == null)
            {
                string name = slot.Generation == 0 ? authored.Name : WandererNames.Choose(cast.CultureId, slot.Female, identity,
                    _state.People.Select(p => p.Name).Concat(Hero.AllAliveHeroes.Select(h => h.Name.ToString())));
                string biography = slot.Generation == 0 ? authored.Biography
                    : name + " settled in " + town.Name + " as an adult and chose work at its tavern house. " + authored.Personality;
                BodyProperties body;
                if (slot.Generation == 0)
                {
                    string definition = string.Format(CultureInfo.InvariantCulture,
                        "<BodyProperties version=\"4\" age=\"{0}\" weight=\"{1}\" build=\"{2}\" key=\"{3}\" />",
                        authored.Age, authored.BodyWeight, authored.BodyBuild, authored.BodyKey);
                    if (!BodyProperties.FromString(definition, out body)) throw new InvalidOperationException("Authored tavern native body is invalid: " + authored.Id);
                }
                else body = ReignWandererAppearance.Generate(donor, culture, age, identity, Array.Empty<WandererIdentity>(), Hero.AllAliveHeroes);
                person = new TavernHousePerson { Id = identity, CastId = authored.Id, TownId = town.StringId,
                    Slot = slot.Index, Generation = slot.Generation, CultureId = cast.CultureId, Name = name,
                    Female = slot.Female, Madam = slot.Generation == 0 && authored.Madam, Body = body.ToString(),
                    CivilianEquipment = authored.CivilianEquipment.Select(e => new TavernHouseEquipmentPart { Slot = e.Slot, ItemId = e.ItemId }).ToList(),
                    Biography = biography, Personality = authored.Personality, CreatedDay = Day };
                _state.People.Add(person); slot.PersonId = person.Id;
                if (_populationPassActive)
                {
                    if (!_passPeople.TryGetValue(person.TownId, out var people))
                        _passPeople.Add(person.TownId, people = new List<TavernHousePerson>());
                    people.Add(person);
                }
            }
            if (!BodyProperties.FromString(person.Body, out var savedBody)) throw new InvalidOperationException("Saved tavern appearance is invalid: " + person.Id);
            Hero hero = string.IsNullOrEmpty(person.HeroId) ? null : PopulationHero(person.HeroId);
            if (hero == null && !string.IsNullOrEmpty(person.HeroId)) throw new InvalidOperationException("Reserved tavern Hero disappeared: " + person.HeroId);
            if (hero == null)
            {
                hero = HeroCreator.CreateSpecialHero(donor, town, null, null, (int)savedBody.Age);
                person.HeroId = hero.StringId;
                _identityIndex.Invalidate();
                if (_populationPassActive) _passHeroes.Created(person.HeroId, hero);
            }
            if (person.Initialized) return;
            hero.Clan = null; hero.Father = null; hero.Mother = null; hero.Spouse = null;
            hero.Culture = culture; hero.IsFemale = person.Female; hero.SetNewOccupation(Occupation.Special);
            hero.SetName(new TextObject("{=!}" + person.Name), new TextObject("{=!}" + person.Name.Split(' ')[0]));
            hero.SetBirthDay(CampaignTime.YearsFromNow(-savedBody.Age));
            hero.StaticBodyProperties = savedBody.StaticProperties; hero.Weight = savedBody.Weight; hero.Build = savedBody.Build;
            var equipment = new Equipment();
            foreach (var part in person.CivilianEquipment)
            {
                if (!Enum.TryParse(part.Slot, out EquipmentIndex equipmentSlot)) throw new InvalidOperationException("Invalid tavern equipment slot: " + part.Slot);
                ItemObject item = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(part.ItemId)
                    ?? throw new InvalidOperationException("Authored tavern civilian item is unavailable: " + part.ItemId);
                equipment[equipmentSlot] = new EquipmentElement(item);
            }
            hero.CivilianEquipment.FillFrom(equipment); hero.BattleEquipment.FillFrom(equipment);
            hero.HeroDeveloper.ClearHero();
            foreach (SkillObject skill in TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObjectTypeList<SkillObject>())
                hero.SetSkillValue(skill, 30 + random.Next(51));
            hero.SetSkillValue(DefaultSkills.Charm, slot.Generation == 0 ? authored.Charm : 100 + random.Next(81));
            hero.SetSkillValue(DefaultSkills.Roguery, slot.Generation == 0 ? authored.Roguery : 100 + random.Next(81));
            hero.SetTraitLevel(DefaultTraits.Honor, authored.Honor);
            hero.SetTraitLevel(DefaultTraits.Calculating, authored.Calculating);
            hero.SetTraitLevel(DefaultTraits.Mercy, authored.Mercy);
            hero.SetTraitLevel(DefaultTraits.Generosity, authored.Generosity);
            hero.SetTraitLevel(DefaultTraits.Valor, authored.Valor);
            hero.Level = person.Madam ? 20 : 12; hero.HeroDeveloper.InitializeHeroDeveloper();
            hero.EncyclopediaText = new TextObject("{=!}" + person.Biography);
            hero.ChangeState(Hero.CharacterStates.Active);
            if (hero.CurrentSettlement != town) EnterSettlementAction.ApplyForCharacterOnly(hero, town);
            person.Initialized = true;
            _nativeProgressSinceObservation = true;
            _pausedCatchup.Progress();
            ReignLog.Info("Tavern native identity created cast=" + person.CastId + " identity=" + person.Id + " hero=" + hero.StringId + " town=" + town.StringId);
        }

        private void QueuePortrait()
        {
            if (DateTime.UtcNow < _nextPortraitUtc) return;
            _nextPortraitUtc = DateTime.UtcNow.AddSeconds(3);
            foreach (var person in _state.People.Where(p => TavernHouseRules.IsActive(p) && p.Generation > 0 && !p.PortraitComplete))
            {
                Hero hero = PopulationHero(person.HeroId);
                if (hero == null) continue;
                string key = CharacterCacheId.ForHero(hero);
                if (PortraitCache.ExistsOnDisk(key)) { person.PortraitComplete = true; continue; }
                if (PortraitCache.IsPending(key) || _portraitRetryUtc.TryGetValue(person.Id, out DateTime retry) && DateTime.UtcNow < retry) continue;
                _portraitRetryUtc[person.Id] = DateTime.UtcNow.AddMinutes(5);
                ReignPortraitBridge.TryRequestPortrait(hero, out string message, out uint color);
                break;
            }
        }

        public TavernHouseVisitReceipt GetOpenVisit(Settlement town) => town == null ? null
            : _state.Visits.FirstOrDefault(v => !v.Ended && v.TownId == town.StringId && v.CampaignId == ReignServerClient.GetCampaignId()
                && v.TimelineId == Timeline && (v.Paid || v.PaymentState == "applying" || v.PaymentState == "recovery_required"));
        public void MarkServerConfirmed(string visitId, string conversationId)
        {
            var visit = _state.Visits.SingleOrDefault(v => v.VisitId == visitId && v.CampaignId == ReignServerClient.GetCampaignId() && v.TimelineId == Timeline);
            if (visit == null || !visit.Paid) return;
            visit.ServerConfirmed = true; visit.ConversationId = conversationId ?? "";
            if (visit.ActionTermsHash.Length > 0) ReignServices.Agreement?.RecheckAgreement(visit.AgreementId);
        }
        public void EndVisit(string visitId)
        {
            var visit = _state.Visits.SingleOrDefault(v => v.VisitId == visitId && v.CampaignId == ReignServerClient.GetCampaignId() && v.TimelineId == Timeline);
            if (visit != null) visit.Ended = true;
        }

        internal bool ValidateRecruitment(Hero hero, int agreedGold, string agreementId, bool consentConfirmed, out string reason)
        {
            reason = "";
            var person = GetPerson(hero);
            if (person == null || hero == null || !hero.IsAlive || hero.IsChild || hero.IsPrisoner || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            { reason = "Recruitment requires a living, adult, free member of this house."; return false; }
            if (!consentConfirmed || agreedGold < 0 || string.IsNullOrWhiteSpace(agreementId))
            { reason = "Recruitment requires their explicit permanent agreement and exact nonnegative payment."; return false; }
            if (!string.IsNullOrEmpty(person.RecruitmentReceipt) && (person.RecruitmentReceipt != agreementId || person.RecruitmentGold != agreedGold))
            { reason = "This character already has a different recruitment agreement."; return false; }
            if (person.RecruitmentComplete) return true;
            bool resuming = person.RecruitmentReceipt == agreementId;
            if (!resuming && !IsAvailable(hero, Hero.MainHero.CurrentSettlement))
            { reason = "This character is no longer available here."; return false; }
            if (hero.CompanionOf != null && hero.CompanionOf != Clan.PlayerClan || hero.Clan != null && hero.Clan != Clan.PlayerClan)
            { reason = "This character already belongs to another clan."; return false; }
            if (hero.CompanionOf != Clan.PlayerClan && Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit)
            { reason = "Your clan has reached its native companion limit."; return false; }
            if (!person.RecruitmentGoldPaid && Hero.MainHero.Gold < agreedGold)
            { reason = "You do not have the agreed recruitment payment."; return false; }
            return true;
        }

        public bool TryRecruit(Hero hero, int agreedGold, string agreementId, bool consentConfirmed, out string reason)
        {
            if (!ValidateRecruitment(hero, agreedGold, agreementId, consentConfirmed, out reason)) return false;
            var person = GetPerson(hero);
            if (person.RecruitmentComplete) return true;
            person.RecruitmentReceipt = agreementId; person.RecruitmentGold = agreedGold;
            try
            {
                _authorizedRecruitId = hero.StringId;
                if (hero.CompanionOf != Clan.PlayerClan) AddCompanionAction.Apply(Clan.PlayerClan, hero);
                if (hero.PartyBelongedTo != MobileParty.MainParty) AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                if (hero.CompanionOf != Clan.PlayerClan || hero.PartyBelongedTo != MobileParty.MainParty)
                { reason = "The native companion transfer did not complete; the agreement is saved for recovery."; return false; }
                Depart(person, true, "recruited");
                hero.SetNewOccupation(Occupation.Wanderer);
                ReignServices.EncounteredResidents?.DetachDepartingRole(hero);
                if (!person.RecruitmentGoldPaid)
                {
                    int before = Hero.MainHero.Gold;
                    person.RecruitmentGoldPaid = true;
                    try { if (agreedGold > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, hero, agreedGold, true); }
                    catch { person.RecruitmentGoldPaid = before - Hero.MainHero.Gold >= agreedGold; throw; }
                }
                if (!TavernHouseRules.CompleteRecruitment(person, hero.CompanionOf == Clan.PlayerClan, hero.PartyBelongedTo == MobileParty.MainParty))
                { reason = "The native recruitment is incomplete; its saved agreement will resume automatically."; return false; }
                return true;
            }
            catch (Exception ex) { reason = "The recruitment agreement is saved for recovery: " + ex.Message; ReignLog.Exception("Tavern recruitment", ex); return false; }
            finally { _authorizedRecruitId = null; }
        }

        public JObject Metadata(Hero hero)
        {
            var person = GetPerson(hero);
            return person == null ? null : new JObject { ["schema"] = 1, ["identityId"] = person.Id, ["castId"] = person.CastId,
                ["heroStringId"] = person.HeroId, ["townId"] = person.TownId, ["name"] = person.Name, ["madam"] = person.Madam,
                ["active"] = TavernHouseRules.IsActive(person), ["biography"] = person.Biography, ["personality"] = person.Personality,
                ["generation"] = person.Generation, ["cultureId"] = person.CultureId };
        }
        public JObject RuntimeSnapshot() => new JObject { ["schema"] = 1, ["ready"] = _ready, ["error"] = _lastError,
            ["townCount"] = _state.InitializedTownIds.Count, ["people"] = JArray.FromObject(_state.People),
            ["slots"] = JArray.FromObject(_state.Slots), ["visits"] = JArray.FromObject(_state.Visits) };
    }
}
