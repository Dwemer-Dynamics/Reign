using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
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

namespace ReignBeta.Campaign
{
    /// <summary>Saved native identities own the house. The server negotiates; this behavior alone transfers gold and companions.</summary>
    public sealed class ReignTavernHouseCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignTavernHouseCampaignBehavior Instance { get; private set; }
        private TavernHouseState _state = new TavernHouseState();
        private TavernHouseCastCatalog _catalog;
        private float _seconds;
        private DateTime _nextPortraitUtc;
        private DateTime _nextRecruitmentRecoveryUtc;
        private int _recruitmentRecoveryIndex;
        private readonly Dictionary<string, DateTime> _portraitRetryUtc = new Dictionary<string, DateTime>();
        private string _authorizedRecruitId;
        private string _lastError = "";
        private bool _ready;
        private double Day => CampaignTime.Now.ToDays;
        private string Timeline => ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
        public bool Ready => _ready;

        public override void RegisterEvents()
        {
            Instance = this;
            ReignTavernHousePatches.Install();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Maintain);
        }

        public override void SyncData(IDataStore store)
        {
            List<string> chunks = store.IsSaving ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state)) : null;
            store.SyncData("_reign_tavern_house_v1", ref chunks);
            if (!store.IsLoading) return;
            _state = chunks == null || chunks.Count == 0 ? new TavernHouseState()
                : JsonConvert.DeserializeObject<TavernHouseState>(ReignSavePayloadCodec.Decode(chunks));
            TavernHouseRules.ValidateState(_state);
            _ready = false; _authorizedRecruitId = null; _portraitRetryUtc.Clear();
            _nextRecruitmentRecoveryUtc = DateTime.MinValue; _recruitmentRecoveryIndex = 0;
        }

        private void OnSession(CampaignGameStarter starter)
        {
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

        public TavernHousePerson GetPerson(Hero hero) => hero == null ? null : _state.People.FirstOrDefault(p => p.HeroId == hero.StringId);
        public bool IsActiveStaff(Hero hero) => TavernHouseRules.IsActive(GetPerson(hero));
        internal bool IsOwnedIdentity(Hero hero) => GetPerson(hero) != null;
        internal bool AllowsRecruitment(Hero hero) => !IsActiveStaff(hero) || _authorizedRecruitId == hero?.StringId;
        public IReadOnlyList<Hero> GetStaff(Settlement town) => town?.IsTown != true ? Array.Empty<Hero>()
            : _state.People.Where(p => p.TownId == town.StringId && TavernHouseRules.IsActive(p)).OrderByDescending(p => p.Madam)
                .ThenBy(p => p.Slot).Select(p => Hero.Find(p.HeroId)).Where(h => h != null && h.IsAlive).ToArray();
        public Hero GetMadam(Settlement town) => town?.IsTown != true ? null
            : _state.People.Where(p => p.TownId == town.StringId && p.Madam && TavernHouseRules.IsActive(p))
                .Select(p => Hero.Find(p.HeroId)).FirstOrDefault(h => h != null && h.IsAlive && !h.IsChild);
        public bool IsAvailable(Hero hero, Settlement town) => IsActiveStaff(hero) && town?.IsTown == true
            && GetPerson(hero).TownId == town.StringId && hero.IsAlive && !hero.IsChild && hero.Age < TavernHouseRules.RetirementAge
            && !hero.IsPrisoner && hero.CompanionOf == null && hero.Clan == null && hero.PartyBelongedTo == null
            && hero.CurrentSettlement == town && hero.HeroState == Hero.CharacterStates.Active;

        public bool EnsureTown(Settlement town)
        {
            if (!_ready || town?.IsTown != true || ReignCampaignInitializationGate.IsPending) return false;
            try
            {
                ResumeRecruitment();
                ReserveTown(town);
                MaintainTown(town, int.MaxValue);
                foreach (Hero hero in GetStaff(town)) ReignTavernHousePortraitSeed.TrySeed(hero, GetPerson(hero));
                return GetMadam(town) != null;
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Tavern house creation", ex); return false; }
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
            _seconds += dt;
            if (_seconds < 1f) return;
            _seconds = 0;
            Maintain();
        }

        private void Maintain()
        {
            if (!_ready || ReignCampaignInitializationGate.IsPending || Hero.MainHero == null || Mission.Current != null) return;
            try
            {
                ResumeRecruitment();
                foreach (var visit in _state.Visits.Where(v => v.Paid && !v.Ended && v.ServerConfirmed))
                    if (Hero.MainHero.CurrentSettlement?.StringId != visit.TownId) visit.Ended = true;
                int remainingCreations = 4;
                foreach (var town in Town.AllTowns.OrderByDescending(t => t.Settlement == Hero.MainHero.CurrentSettlement))
                {
                    ReserveTown(town.Settlement);
                    // Initial preparation is bounded. Local entry finishes its own roster synchronously.
                    int before = _state.People.Count(p => p.Initialized);
                    MaintainTown(town.Settlement, remainingCreations);
                    remainingCreations -= _state.People.Count(p => p.Initialized) - before;
                }
                QueuePortrait();
                _lastError = "";
            }
            catch (Exception ex) { _lastError = ex.Message; ReignLog.Exception("Tavern house maintenance", ex); }
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
            TryRecruit(Hero.Find(person.HeroId), person.RecruitmentGold, person.RecruitmentReceipt, true, out string reason);
        }

        private void MaintainTown(Settlement town, int budget)
        {
            if (town.IsUnderSiege || town.SiegeEvent != null) return;
            foreach (var person in _state.People.Where(p => p.TownId == town.StringId && TavernHouseRules.IsActive(p)).ToArray())
            {
                Hero hero = Hero.Find(person.HeroId);
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
                if (reserved != null && !reserved.Initialized) { CreatePerson(town, slot, reserved); if (--budget == 0) break; }
                else if (TavernHouseRules.ReplacementDue(slot, Day)) { CreatePerson(town, slot, null); if (--budget == 0) break; }
            }
            PromoteMadam(town);
        }

        private void Depart(TavernHousePerson person, bool recruited, string reason)
        {
            if (!TavernHouseRules.IsActive(person)) return;
            person.Recruited = recruited; person.Retired = !recruited; person.Madam = false;
            person.LeftDay = Day; person.DepartureReason = reason;
            TavernHouseSlot slot = _state.Slots.Single(s => s.TownId == person.TownId && s.Index == person.Slot);
            slot.PersonId = ""; slot.Generation++;
            slot.ReplacementDay = TavernHouseRules.ReplacementDay(Day, recruited);
            PromoteMadam(Settlement.Find(person.TownId));
        }

        private void PromoteMadam(Settlement town)
        {
            if (town == null) return;
            var eligible = _state.People.Where(p => p.TownId == town.StringId && TavernHouseRules.IsActive(p))
                .Where(p => Hero.Find(p.HeroId)?.IsAlive == true && Hero.Find(p.HeroId)?.IsChild == false).ToArray();
            TavernHousePerson successor = TavernHouseRules.SelectMadam(eligible,
                p => Hero.Find(p.HeroId).GetSkillValue(DefaultSkills.Charm), p => Hero.Find(p.HeroId).GetSkillValue(DefaultSkills.Roguery));
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
            }
            if (!BodyProperties.FromString(person.Body, out var savedBody)) throw new InvalidOperationException("Saved tavern appearance is invalid: " + person.Id);
            Hero hero = string.IsNullOrEmpty(person.HeroId) ? null : Hero.Find(person.HeroId);
            if (hero == null && !string.IsNullOrEmpty(person.HeroId)) throw new InvalidOperationException("Reserved tavern Hero disappeared: " + person.HeroId);
            if (hero == null)
            {
                hero = HeroCreator.CreateSpecialHero(donor, town, null, null, (int)savedBody.Age);
                person.HeroId = hero.StringId;
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
            ReignLog.Info("Tavern native identity created cast=" + person.CastId + " identity=" + person.Id + " hero=" + hero.StringId + " town=" + town.StringId);
        }

        private void QueuePortrait()
        {
            if (DateTime.UtcNow < _nextPortraitUtc) return;
            _nextPortraitUtc = DateTime.UtcNow.AddSeconds(3);
            foreach (var person in _state.People.Where(p => TavernHouseRules.IsActive(p) && p.Generation > 0 && !p.PortraitComplete))
            {
                Hero hero = Hero.Find(person.HeroId);
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
            : TavernHouseRules.GetOpenVisit(_state.Visits, town.StringId, ReignServerClient.GetCampaignId(), Timeline);
        public void MarkServerConfirmed(string visitId, string conversationId)
        {
            var visit = _state.Visits.SingleOrDefault(v => v.VisitId == visitId && v.CampaignId == ReignServerClient.GetCampaignId() && v.TimelineId == Timeline);
            if (visit == null || !visit.Paid) return;
            visit.ServerConfirmed = true; visit.ConversationId = conversationId ?? "";
        }
        public void EndVisit(string visitId)
        {
            var visit = _state.Visits.SingleOrDefault(v => v.VisitId == visitId && v.CampaignId == ReignServerClient.GetCampaignId() && v.TimelineId == Timeline);
            if (visit != null) visit.Ended = true;
        }

        public bool ConfirmVisit(TavernHouseVisitQuote quote, out TavernHouseVisitReceipt receipt, out string reason)
        {
            receipt = null;
            if (!TavernHouseRules.ValidateQuote(quote, out reason)) return false;
            if (!_ready || Hero.MainHero == null || Hero.MainHero.IsChild || !Hero.MainHero.IsAlive || Hero.MainHero.IsPrisoner
                || quote.PlayerHeroId != Hero.MainHero.StringId || quote.CampaignId != ReignServerClient.GetCampaignId() || quote.TimelineId != Timeline)
            { reason = "This agreement does not belong to the active adult player and campaign."; return false; }
            receipt = _state.Visits.SingleOrDefault(v => v.CampaignId == quote.CampaignId && v.TimelineId == quote.TimelineId && v.AgreementId == quote.AgreementId);
            if (receipt != null && !TavernHouseRules.Matches(receipt, quote)) { reason = "The agreement was already used with different terms."; return false; }
            if (receipt?.Paid == true) return true;
            Settlement town = Settlement.Find(quote.TownId);
            if (town?.IsTown != true || Hero.MainHero.CurrentSettlement != town || !EnsureTown(town))
            { reason = "You must be at this town's tavern house."; return false; }
            if (GetOpenVisit(town) is TavernHouseVisitReceipt active && active != receipt)
            { reason = "Finish the current visit before accepting another agreement."; return false; }
            if (quote.Charges.Any(c => !IsAvailable(Hero.Find(c.HeroId), town)))
            { reason = "An agreed participant is no longer available. Negotiate a new agreement."; return false; }
            Hero madam = GetMadam(town);
            if (madam == null || !IsAvailable(madam, town)) { reason = "The madam is unavailable."; return false; }
            if (Hero.MainHero.Gold < quote.TotalGold) { reason = "You do not have the agreed payment."; return false; }
            if (receipt == null)
            {
                receipt = new TavernHouseVisitReceipt { VisitId = Guid.NewGuid().ToString("N"), AgreementId = quote.AgreementId,
                    CampaignId = quote.CampaignId, TimelineId = quote.TimelineId, TownId = quote.TownId, PlayerHeroId = quote.PlayerHeroId,
                    Charges = quote.Charges.Select(c => new TavernHouseVisitCharge { HeroId = c.HeroId, DisplayName = Hero.Find(c.HeroId).Name.ToString(), Gold = c.Gold }).ToList(),
                    ParticipantHeroIds = quote.Charges.Select(c => c.HeroId).ToList(), QuoteRevision = quote.QuoteRevision,
                    PaidGold = quote.TotalGold, StartedDay = Day };
                _state.Visits.Add(receipt);
            }
            int goldBefore = Hero.MainHero.Gold;
            // Mark before native event dispatch; if a downstream listener throws after the transfer, it still must not repeat.
            receipt.Paid = true;
            try { if (quote.TotalGold > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, madam, quote.TotalGold, true); }
            catch (Exception ex)
            {
                receipt.Paid = goldBefore - Hero.MainHero.Gold >= quote.TotalGold;
                ReignLog.Exception("Tavern visit payment", ex);
                if (!receipt.Paid) { reason = "The native payment did not complete. The saved agreement can be retried."; return false; }
            }
            reason = "";
            return true;
        }

        public bool TryRecruit(Hero hero, int agreedGold, string agreementId, bool consentConfirmed, out string reason)
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
