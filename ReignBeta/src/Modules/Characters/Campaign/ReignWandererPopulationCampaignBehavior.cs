using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Campaign
{
    public sealed class ReignWandererPopulationCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignWandererPopulationCampaignBehavior Instance { get; private set; }
        private WandererPopulationState _state = new WandererPopulationState();
        private readonly PopulationIdentityIndex<WandererIdentity> _identityIndex = new PopulationIdentityIndex<WandererIdentity>(p => p.HeroId);
        private CharacterObject[] _templates = Array.Empty<CharacterObject>();
        private CultureObject[] _cultures = Array.Empty<CultureObject>();
        private readonly Dictionary<string, int> _inFlight = new Dictionary<string, int>();
        private bool _ready, _historyBusy;
        private DateTime _nextHistoryUtc, _nextMaintenanceUtc;
        private int _historyCursor, _historyRequestId;
        private readonly RealTimeMaintenanceCadence _maintenanceCadence = new RealTimeMaintenanceCadence();
        private readonly PausedPopulationCatchup _pausedCatchup = new PausedPopulationCatchup();
        private TaleWorlds.CampaignSystem.Campaign _sessionCampaign;
        private bool _populationWorkRemaining = true;
        private bool _lastFillMadeProgress;
        private string _lastError = "", _historyError = "";
        private int _creationFailures;
        private long _creationAttemptCount;
        private double _lastAppearanceMs, _lastNativeAllocationMs, _lastInitializationMs;
        internal bool OwnsPopulation => _ready && ReignWandererPatches.Ready;
        private double Day => CampaignTime.Now.ToDays;

        public override void RegisterEvents()
        {
            Instance = this;
            ReignWandererPatches.Install();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDaily);
        }

        public override void SyncData(IDataStore store)
        {
            List<string> chunks = store.IsSaving ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state)) : null;
            store.SyncData("_reign_wanderer_population_v1", ref chunks);
            if (!store.IsLoading) return;
            _pausedCatchup.Request(); _populationWorkRemaining = true;
            _identityIndex.Invalidate();
            ReignWandererAppearance.ClearSchemaCache();
            _creationAttemptCount = 0;
            _lastAppearanceMs = _lastNativeAllocationMs = _lastInitializationMs = 0;
            _state = chunks == null || chunks.Count == 0 ? new WandererPopulationState()
                : JsonConvert.DeserializeObject<WandererPopulationState>(ReignSavePayloadCodec.Decode(chunks));
            if (_state == null || _state.Version != WandererPopulationRules.Version || _state.People == null
                || _state.People.Any(p => p == null || string.IsNullOrEmpty(p.Id))
                || _state.People.Select(p => p.Id).Distinct().Count() != _state.People.Count
                || _state.People.Where(p => !string.IsNullOrEmpty(p.HeroId)).GroupBy(p => p.HeroId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Wanderer save identity ledger is invalid; refusing regeneration.");
            _inFlight.Clear(); _ready = false; _historyBusy = false;
            System.Threading.Interlocked.Increment(ref _historyRequestId);
        }

        private void OnSession(CampaignGameStarter starter)
        {
            _sessionCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
            _pausedCatchup.Request(); _populationWorkRemaining = true;
            ReignWandererAppearance.ClearSchemaCache();
            try
            {
                _templates = CharacterObject.All.Where(c => c.IsTemplate && c.Occupation == Occupation.Wanderer)
                    .OrderBy(c => c.StringId, StringComparer.Ordinal).ToArray();
                _cultures = Town.AllTowns.Select(t => t.Culture).Concat(Kingdom.All.Select(k => k.Culture))
                    .Where(c => c != null).Distinct().OrderBy(c => c.StringId, StringComparer.Ordinal).ToArray();
                if (_templates.Length == 0 || _cultures.Length == 0) throw new InvalidOperationException("Native wanderer/culture catalog is empty.");
                foreach (var culture in _cultures)
                    if (!WandererNames.Supports(culture.StringId) || culture.Townsman?.BodyPropertyRange == null || culture.Townswoman?.BodyPropertyRange == null)
                        throw new InvalidOperationException("Wanderer cultural coverage is incomplete: " + culture.StringId);
                AdoptExisting();
                // Loaded native heroes are authoritative. Never reapply generated bodies/names on load.
                foreach (var person in _state.People.Where(p => p.Initialized && !p.Retired))
                    if (Hero.Find(person.HeroId)?.IsAlive != true) person.Retired = true;
                _ready = ReignWandererPatches.Ready;
                if (!_ready) throw new InvalidOperationException("Required Native population hooks unavailable.");
                _lastError = "";
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Wanderer initialization", ex); }
        }

        private WandererIdentity Adopt(Hero hero)
        {
            var found = Find(hero);
            if (found != null) return found;
            var record = new WandererIdentity { Id = "native:" + hero.StringId, HeroId = hero.StringId,
                TemplateId = hero.Template?.StringId ?? "", CultureId = hero.Culture?.StringId ?? "",
                Name = hero.Name.ToString(), Body = hero.BodyProperties.ToString(), FaceKey = ReignWandererAppearance.Key(hero.StaticBodyProperties),
                FaceShape = ReignWandererAppearance.Shape(hero.BodyProperties, hero.CharacterObject.Race, hero.IsFemale),
                Race = hero.CharacterObject.Race, Female = hero.IsFemale, Biography = hero.EncyclopediaText?.ToString() ?? "",
                Initialized = true, CreatedDay = Day, LastMoveDay = Day, LastTownId = hero.CurrentSettlement?.StringId ?? "" };
            _state.People.Add(record); return record;
        }

        private void AdoptExisting()
        {
            foreach (var hero in Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && h.IsWanderer
                && ReignTavernHouseCampaignBehavior.Instance?.IsActiveStaff(h) != true).ToList()) Adopt(hero);
        }

        internal WandererIdentity Find(Hero hero) => hero == null ? null : _identityIndex.Find(_state.People, hero.StringId);
        internal bool IsProtected(Hero hero) => Find(hero)?.Protected == true;
        internal bool BlocksRemoval(Hero hero) => ReignTavernHouseCampaignBehavior.Instance?.IsActiveStaff(hero) == true
            || Find(hero) is WandererIdentity p && !p.Retired
                && (p.Protected || !p.HistoryChecked || _inFlight.ContainsKey(p.HeroId));

        internal void BeginContact(Hero hero, string playerText)
        {
            if (hero == null || (!hero.IsWanderer && Find(hero) == null) || string.IsNullOrWhiteSpace(playerText)) return;
            var p = Adopt(hero); p.HistoryChecked = false;
            _pausedCatchup.Request();
            _inFlight[p.HeroId] = _inFlight.TryGetValue(p.HeroId, out int count) ? count + 1 : 1;
        }

        internal void EndContact(Hero hero, bool success, string receipt, string playerText)
        {
            var p = Find(hero);
            if (p == null || string.IsNullOrWhiteSpace(playerText)) return;
            _pausedCatchup.Request();
            if (_inFlight.TryGetValue(p.HeroId, out int count)) { if (count <= 1) _inFlight.Remove(p.HeroId); else _inFlight[p.HeroId] = count - 1; }
            if (success) WandererPopulationRules.Protect(p, receipt, Day);
            // Failure is uncertain: the server may have committed a turn before the connection broke.
        }

        private bool SafeTown(Settlement town) => WandererPopulationRules.CanMaintainTown(
            town?.IsTown == true, town?.IsUnderSiege == true, town?.SiegeEvent != null, Mission.Current != null);

        private bool QuestSafe(Hero hero) => hero.Issue == null
            && !TaleWorlds.CampaignSystem.Campaign.Current.QuestManager.TrackedObjects.ContainsKey(hero)
            && hero.CanDie(KillCharacterAction.KillCharacterActionDetail.Lost);

        private WandererPresence Presence(Hero hero)
        {
            var p = Find(hero);
            return new WandererPresence { HeroId = hero.StringId, TownId = hero.CurrentSettlement?.StringId ?? "",
                Alive = hero.IsAlive, Adult = !hero.IsChild, Free = !hero.IsPrisoner,
                Independent = hero.IsWanderer && hero.CompanionOf == null && hero.Clan == null && hero.PartyBelongedTo == null
                    && ReignTavernHouseCampaignBehavior.Instance?.IsActiveStaff(hero) != true,
                Available = hero.HeroState == Hero.CharacterStates.Active && hero.GovernorOf == null,
                SafeToMove = SafeTown(hero.CurrentSettlement) && hero.Issue == null
                    && ReignTavernHouseCampaignBehavior.Instance?.IsActiveStaff(hero) != true
                    && !TaleWorlds.CampaignSystem.Campaign.Current.QuestManager.TrackedObjects.ContainsKey(hero)
                    && !_inFlight.ContainsKey(hero.StringId) && (p == null || Day - p.LastMoveDay >= WandererPopulationRules.MinimumResidenceDays) };
        }

        private void OnTick(float dt)
        {
            if (!_maintenanceCadence.TryTake(System.Diagnostics.Stopwatch.GetTimestamp(),
                    System.Diagnostics.Stopwatch.Frequency, dt > 0f)) return;
            MaintainRealTime(dt);
        }

        internal void ApplicationTick()
        {
            if (Mission.Current != null) { _pausedCatchup.Request(); return; }
            if (!_pausedCatchup.Pending || _pausedCatchup.Blocked || DateTime.UtcNow < _nextMaintenanceUtc) return;
            bool current = _sessionCampaign != null && ReferenceEquals(_sessionCampaign, TaleWorlds.CampaignSystem.Campaign.Current);
            if (!current) return;
            if (_sessionCampaign.TimeControlMode != CampaignTimeControlMode.Stop) return;
            bool saving = ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || ReignSaveSyncCoordinator.IsSaveFinalizationPending;
            if (!_pausedCatchup.CanPoll(current, OwnsPopulation && Hero.MainHero != null && !ReignCampaignInitializationGate.IsPending,
                    false, saving, ReignSaveSyncCoordinator.IsReadyForCampaign(ReignCampaignIdentity.CurrentCampaignId()))) return;
            if (!_maintenanceCadence.TryTake(System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency)) return;
            MaintainRealTime(0f, pausedPass: true);
        }

        private void MaintainRealTime(float dt, bool pausedPass = false)
        {
            if (!_ready || ReignCampaignInitializationGate.IsPending || Mission.Current != null || Hero.MainHero == null) return;
            using var timing = ReignLog.MeasureWork("population.wanderer", dt);
            bool stalledPreparation = false;
            try
            {
                AdoptExisting();
                bool attemptedFill = false;
                if (DateTime.UtcNow >= _nextMaintenanceUtc)
                {
                    attemptedFill = true;
                    _populationWorkRemaining = Fill(1); _lastError = "";
                    if (_lastFillMadeProgress) _pausedCatchup.Progress();
                }
                if (!_historyBusy && DateTime.UtcNow >= _nextHistoryUtc) ReconcileHistory();
                if (!_populationWorkRemaining && _pausedCatchup.Pending)
                    _populationWorkRemaining = PopulationDeficitRemains();
                if (pausedPass && attemptedFill && _populationWorkRemaining && !_lastFillMadeProgress)
                {
                    stalledPreparation = true;
                    throw new InvalidOperationException("Pending wanderer population made no native progress; retry deferred.");
                }
                bool historyPending = _historyBusy || _state.People.Any(p => !p.Retired && !p.Protected && !p.HistoryChecked);
                _pausedCatchup.Observe(_populationWorkRemaining || historyPending, false);
            }
            catch (Exception ex) { _pausedCatchup.Observe(true, true); _populationWorkRemaining = true; _lastError = ex.Message; if (!stalledPreparation) _creationFailures++; _nextMaintenanceUtc = DateTime.UtcNow.AddSeconds(30); ReignLog.Warn("Wanderer maintenance deferred: " + ex.Message); }
        }

        internal void CampaignEnded()
        {
            _sessionCampaign = null; _ready = false;
            System.Threading.Interlocked.Increment(ref _historyRequestId);
            _historyBusy = false; _pausedCatchup.Request(); _populationWorkRemaining = true;
            _nextMaintenanceUtc = default; _nextHistoryUtc = default; _lastError = ""; _historyError = "";
            _identityIndex.Invalidate(); ReignWandererAppearance.ClearSchemaCache();
        }

        private bool PopulationDeficitRemains() => PlanPopulation(0).Item1;

        private Tuple<bool, WandererPopulationPlan> PlanPopulation(int budget)
        {
            var heroes = Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && h.IsWanderer).ToArray();
            var towns = Town.AllTowns.Select(t => new WandererTown { Id = t.Settlement.StringId, Safe = SafeTown(t.Settlement) }).ToArray();
            string preferredTownId = Hero.MainHero?.CurrentSettlement?.IsTown == true
                ? Hero.MainHero.CurrentSettlement.StringId : "";
            var plan = WandererPopulationRules.Plan(
                towns,
                heroes.Select(Presence), budget, preferredTownId);
            bool remaining = towns.Any(t => t.Safe && plan.Counts[t.Id] < WandererPopulationRules.Target);
            return Tuple.Create(remaining, plan);
        }

        private bool Fill(int budget)
        {
            var pending = PlanPopulation(budget); var plan = pending.Item2;
            _lastFillMadeProgress = false;
            foreach (var move in plan.Moves) _lastFillMadeProgress = Move(Hero.Find(move.HeroId), Settlement.Find(move.TownId)) || _lastFillMadeProgress;
            foreach (string town in plan.SpawnTowns)
            {
                long attempts = _creationAttemptCount;
                Create(Settlement.Find(town));
                _lastFillMadeProgress |= _creationAttemptCount > attempts;
            }
            return pending.Item1;
        }

        private bool Move(Hero hero, Settlement destination)
        {
            if (hero == null || !SafeTown(destination) || !Presence(hero).SafeToMove) return false;
            LeaveSettlementAction.ApplyForCharacterOnly(hero);
            EnterSettlementAction.ApplyForCharacterOnly(hero, destination);
            var p = Adopt(hero); p.LastMoveDay = Day; p.LastTownId = destination.StringId;
            return hero.CurrentSettlement == destination;
        }

        private void OnDaily()
        {
            if (!OwnsPopulation || ReignCampaignInitializationGate.IsPending || Mission.Current != null) return;
            _pausedCatchup.Request();
            double today = Math.Floor(Day);
            if (_state.LastDailyDay >= today) return;
            _state.LastDailyDay = today;
            using var timing = ReignLog.MeasureWork("population.wanderer", 0f);
            try
            {
                AdoptExisting();
                var eligible = Hero.AllAliveHeroes.Where(h => h.IsWanderer && h != Hero.MainHero).ToArray();
                // Preserve Native's global 10% daily turnover opportunity, not a per-person purge.
                if (MBRandom.RandomFloat < .1f)
                {
                    var candidates = eligible.Where(h => WandererPopulationRules.CanRotate(Find(h), Presence(h), QuestSafe(h), Day)).ToArray();
                    if (candidates.Length > 0)
                    {
                        Hero victim = candidates[MBRandom.RandomInt(candidates.Length)];
                        KillCharacterAction.ApplyByRemove(victim, false, false);
                        if (!victim.IsAlive) Find(victim).Retired = true;
                    }
                }
                // Swap identities between towns without changing either tavern's population.
                var movers = eligible.Where(h => h.IsAlive && WandererPopulationRules.Counts(Presence(h)) && Presence(h).SafeToMove).ToArray();
                if (movers.Length > 1)
                {
                    Hero a = movers[MBRandom.RandomInt(movers.Length)];
                    Hero b = movers.FirstOrDefault(h => h.CurrentSettlement != a.CurrentSettlement);
                    if (b != null) { Settlement origin = a.CurrentSettlement; Move(a, b.CurrentSettlement); Move(b, origin); }
                }
                _populationWorkRemaining = Fill(WandererPopulationRules.BatchSize);
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Wanderer daily maintenance", ex); }
        }

        private void Create(Settlement town)
        {
            if (!SafeTown(town)) return;
            _creationAttemptCount++;
            _lastAppearanceMs = _lastNativeAllocationMs = _lastInitializationMs = 0;
            long cursor = _state.Cursor++;
            var pair = WandererPopulationRules.Combination(cursor, _templates.Length, _cultures.Length);
            var template = _templates[pair.Item1]; var culture = _cultures[pair.Item2];
            string id = Guid.NewGuid().ToString("N");
            var random = new Random(WandererPopulationRules.Seed(id));
            bool female = template.IsFemale;
            CharacterObject appearanceDonor = female ? culture.Townswoman : culture.Townsman;
            CharacterObject heroDonor = _templates.Where(t => t.Culture == culture && t.IsFemale == female && t.Race == appearanceDonor.Race)
                .OrderBy(t => Skills.All.Sum(s => Math.Abs(t.GetSkillValue(s) - template.GetSkillValue(s))))
                .ThenBy(t => t.StringId, StringComparer.Ordinal).FirstOrDefault() ?? appearanceDonor;
            var living = Hero.AllAliveHeroes.ToArray();
            string name = WandererNames.Choose(culture.StringId, female, id, living.Select(h => h.FirstName?.ToString() ?? h.Name.ToString()).Concat(_state.People.Select(p => p.Name)));
            int age = TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge + 3 + random.Next(27);
            BodyProperties body;
            long appearanceStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try { body = ReignWandererAppearance.Generate(appearanceDonor, culture, age, id, _state.People, living); }
            finally { _lastAppearanceMs = ElapsedCreationMs(appearanceStart); }
            var birthplaces = Town.AllTowns.Where(t => t.Culture == culture).ToArray();
            Settlement birthplace = birthplaces.Length == 0 ? town : birthplaces[random.Next(birthplaces.Length)].Settlement;
            SkillObject specialty = Skills.All.OrderByDescending(s => template.GetSkillValue(s)).First();
            var p = new WandererIdentity { Id = id, TemplateId = template.StringId, CultureId = culture.StringId,
                Name = name, Female = female, Race = appearanceDonor.Race, Body = body.ToString(), FaceKey = ReignWandererAppearance.Key(body.StaticProperties),
                FaceShape = ReignWandererAppearance.Shape(body, appearanceDonor.Race, female), BirthplaceId = birthplace.StringId,
                Biography = WandererPopulationRules.Biography(name, culture.Name.ToString(), birthplace.Name.ToString(), specialty.Name.ToString().ToLowerInvariant(), random.Next()),
                CreatedDay = Day, LastMoveDay = Day, LastTownId = town.StringId, Generated = true, HistoryChecked = true };
            _state.People.Add(p); // Reserve first, before native allocation; exceptions may never reuse this identity.
            Hero hero;
            long allocationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try { hero = HeroCreator.CreateSpecialHero(heroDonor, birthplace, null, null, age); }
            finally { _lastNativeAllocationMs = ElapsedCreationMs(allocationStart); }
            p.HeroId = hero.StringId;
            _identityIndex.Invalidate();
            long initializationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                hero.Clan = null; hero.Father = null; hero.Mother = null; hero.Spouse = null;
                hero.IsFemale = female; hero.Culture = culture; hero.SetNewOccupation(Occupation.Wanderer);
                hero.SetName(new TextObject("{=!}" + name), new TextObject("{=!}" + name));
                hero.SetBirthDay(CampaignTime.YearsFromNow(-age));
                hero.StaticBodyProperties = body.StaticProperties; hero.Weight = body.Weight; hero.Build = body.Build;
                // Allocation initialized the cultural donor's development. Rebuild it from the chosen profession.
                hero.HeroDeveloper.ClearHero();
                foreach (var skill in Skills.All) hero.SetSkillValue(skill, template.GetSkillValue(skill));
                foreach (var trait in MBObjectManager.Instance.GetObjectTypeList<TraitObject>()) hero.SetTraitLevel(trait, template.GetTraitLevel(trait));
                // Keep the profession's mechanics, but allow actual personality differences between people.
                foreach (var trait in new[] { DefaultTraits.Honor, DefaultTraits.Mercy, DefaultTraits.Valor, DefaultTraits.Generosity, DefaultTraits.Calculating })
                    hero.SetTraitLevel(trait, Math.Max(-2, Math.Min(2, template.GetTraitLevel(trait) + random.Next(-1, 2))));
                hero.Level = template.Level; hero.HeroDeveloper.InitializeHeroDeveloper();
                hero.EncyclopediaText = new TextObject("{=!}" + p.Biography);
                hero.ChangeState(Hero.CharacterStates.Active);
                EnterSettlementAction.ApplyForCharacterOnly(hero, town);
                p.Initialized = true;
            }
            catch
            {
                // Keep a failed allocation out of scenes; preserve its record for diagnosis, never respawn by this id.
                hero.ChangeState(Hero.CharacterStates.Disabled); p.Retired = true; throw;
            }
            finally { _lastInitializationMs = ElapsedCreationMs(initializationStart); }
        }

        private static double ElapsedCreationMs(long started) =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;

        private async void ReconcileHistory()
        {
            var pending = _state.People.Where(x => !x.Retired && !x.Protected && !x.HistoryChecked && !_inFlight.ContainsKey(x.HeroId)).ToArray();
            // A failed history lookup must not starve every later wanderer's recovery.
            var p = pending.Length == 0 ? null : pending[(_historyCursor++ & int.MaxValue) % pending.Length];
            if (p == null) return;
            _historyBusy = true; _nextHistoryUtc = DateTime.UtcNow.AddSeconds(5);
            int requestId = System.Threading.Interlocked.Increment(ref _historyRequestId);
            string campaign = ReignServerClient.GetCampaignId();
            string timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            int saveGeneration = ReignSaveSyncCoordinator.CampaignGeneration;
            try
            {
                JObject response = await ReignServerClient.ReadWandererContactAsync(campaign, timeline, p.HeroId, Hero.MainHero.StringId);
                await ReignMainThread.InvokeAsync(() => {
                    if (System.Threading.Volatile.Read(ref _historyRequestId) != requestId) return;
                    if (_sessionCampaign == null || !ReferenceEquals(_sessionCampaign, TaleWorlds.CampaignSystem.Campaign.Current)) return;
                    if (Instance != this || ReignServerClient.GetCampaignId() != campaign || (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") != timeline) return;
                    if (ReignSaveSyncCoordinator.CampaignGeneration != saveGeneration
                        || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                        || ReignSaveSyncCoordinator.IsSaveFinalizationPending
                        || !ReignSaveSyncCoordinator.IsReadyForCampaign(campaign))
                    { _historyError = "Contact history recovery deferred until save alignment is idle."; return; }
                    if (response.Value<bool?>("ok") != true)
                    { _historyError = response.Value<string>("error") ?? "History unavailable"; _nextHistoryUtc = DateTime.UtcNow.AddSeconds(30); _pausedCatchup.Observe(true, true); return; }
                    if (response.Value<string>("heroStringId") != p.HeroId || response.Value<string>("campaignId") != campaign || response.Value<string>("timelineId") != timeline)
                    { _historyError = "Contact history identity mismatch; recovery deferred."; _nextHistoryUtc = DateTime.UtcNow.AddSeconds(30); _pausedCatchup.Observe(true, true); return; }
                    if (_inFlight.ContainsKey(p.HeroId)) return;
                    if (response.Value<bool?>("hasContact") == true) WandererPopulationRules.Protect(p, response.Value<string>("receipt"), Day);
                    else p.HistoryChecked = true;
                    _historyError = "";
                    _pausedCatchup.Progress();
                });
            }
            catch (Exception ex)
            {
                if (Instance == this && System.Threading.Volatile.Read(ref _historyRequestId) == requestId && ReignSaveSyncCoordinator.CampaignGeneration == saveGeneration)
                { _historyError = ex.Message; _nextHistoryUtc = DateTime.UtcNow.AddSeconds(30); _pausedCatchup.Observe(true, true); }
            }
            finally
            {
                if (System.Threading.Volatile.Read(ref _historyRequestId) == requestId) _historyBusy = false;
            }
        }

        internal JObject Metadata(Hero hero)
        {
            var p = Find(hero);
            if (p == null) return null;
            return new JObject { ["schema"] = WandererPopulationRules.Version, ["identityId"] = p.Id,
                ["heroStringId"] = p.HeroId, ["templateId"] = p.TemplateId, ["cultureId"] = p.CultureId,
                ["generated"] = p.Generated, ["name"] = p.Name, ["biography"] = p.Biography,
                ["protected"] = p.Protected, ["contactReceipt"] = p.ContactReceipt ?? "", ["contactDay"] = p.ContactDay };
        }

        internal JObject RuntimeSnapshot()
        {
            var presence = Hero.AllAliveHeroes.Where(h => h.IsWanderer && h != Hero.MainHero).Select(Presence).ToArray();
            return new JObject { ["schema"] = WandererPopulationRules.Version, ["ready"] = OwnsPopulation,
                ["templates"] = _templates.Length, ["cultures"] = new JArray(_cultures.Select(c => c.StringId)),
                ["combinationCount"] = _templates.Length * _cultures.Length, ["cursor"] = _state.Cursor,
                ["protected"] = _state.People.Count(p => p.Protected && !p.Retired), ["retired"] = _state.People.Count(p => p.Retired),
                ["historyPending"] = _state.People.Count(p => !p.HistoryChecked && !p.Retired && !p.Protected),
                ["error"] = _lastError, ["historyError"] = _historyError, ["creationFailures"] = _creationFailures,
                ["creationAttemptCount"] = _creationAttemptCount,
                ["pausedCatchupPending"] = _pausedCatchup.Pending, ["pausedCatchupBlocked"] = _pausedCatchup.Blocked, ["populationWorkRemaining"] = _populationWorkRemaining,
                ["lastAppearanceMs"] = _lastAppearanceMs, ["lastNativeAllocationMs"] = _lastNativeAllocationMs,
                ["lastInitializationMs"] = _lastInitializationMs,
                ["faceSchemaCacheHits"] = ReignWandererAppearance.SchemaDiagnostics.Hits,
                ["faceSchemaCacheMisses"] = ReignWandererAppearance.SchemaDiagnostics.Misses,
                ["faceSchemaCacheVerificationReads"] = ReignWandererAppearance.SchemaDiagnostics.VerificationReads,
                ["faceSchemaCacheMismatches"] = ReignWandererAppearance.SchemaDiagnostics.Mismatches,
                ["faceSchemaCacheEntries"] = ReignWandererAppearance.SchemaDiagnostics.Count,
                ["faceSchemaCachePayloadBytes"] = ReignWandererAppearance.SchemaDiagnostics.PayloadBytes,
                ["faceSchemaCacheDisabled"] = ReignWandererAppearance.SchemaDiagnostics.Disabled,
                ["towns"] = new JArray(Town.AllTowns.Select(t => new JObject { ["townId"] = t.Settlement.StringId,
                    ["count"] = presence.Count(p => WandererPopulationRules.Counts(p) && p.TownId == t.Settlement.StringId), ["safe"] = SafeTown(t.Settlement) })),
                ["localPeople"] = new JArray(Hero.AllAliveHeroes.Where(h => h.CurrentSettlement != null && h.CurrentSettlement == Hero.MainHero?.CurrentSettlement && Find(h) != null)
                    .Take(32).Select(h => { var row = Metadata(h); row["faceKey"] = ReignWandererAppearance.Key(h.StaticBodyProperties); return row; })) };
        }
    }
}
