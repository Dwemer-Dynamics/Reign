using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Family;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    internal sealed class ReignStartingChildrenCampaignBehavior : CampaignBehaviorBase
    {
        private StartingChildrenState _state = new StartingChildrenState();
        private bool _loadedSave;
        private string _reportPath;
        internal static ReignStartingChildrenCampaignBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            // Registered AFTER court households: their migration, faces and homes are ready first.
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGame);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Keep the legacy key readable, but never write the unbounded JSON again.
            string json = null;
            List<string> chunks = dataStore.IsSaving
                ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state)) : null;
            dataStore.SyncData("_reign_startingChildren_v1", ref json);
            dataStore.SyncData("_reign_startingChildren_chunks_v2", ref chunks);
            dataStore.SyncData("_reign_startingChildren_reportPath_v1", ref _reportPath);
            if (!dataStore.IsLoading) return;
            _loadedSave = true; // Also protects old saves with no version marker or receipts.
            if (chunks != null && chunks.Count > 0) json = ReignSavePayloadCodec.Decode(chunks);
            _state = string.IsNullOrEmpty(json) ? new StartingChildrenState()
                : JsonConvert.DeserializeObject<StartingChildrenState>(json)
                    ?? throw new InvalidOperationException("Starting-child save evidence is invalid.");
        }

        internal JObject Snapshot()
        {
            return new JObject
            {
                ["schema"] = "reign-starting-children-runtime-v1",
                ["status"] = _state.Completed ? "completed" : _loadedSave ? "existing_save_not_seeded"
                    : _state.Error != null ? "failed" : "awaiting_new_campaign",
                ["planned"] = _state.Plan?.Added ?? 0,
                ["created"] = _state.Receipts.Count,
                ["finalized"] = _state.Receipts.Count(r => r.Finalized),
                ["minorsBefore"] = _state.Plan?.MinorsBefore ?? 0,
                ["projectedMinorsAfter"] = _state.Plan?.ProjectedMinors ?? 0,
                ["shortfallFamilies"] = _state.Plan?.Families.Count(f => f.Reason == "chronology_shortfall") ?? 0,
                ["error"] = _state.Error ?? "", ["reportPath"] = _reportPath ?? ""
            };
        }

        private void OnNewGame(CampaignGameStarter starter)
        {
            if (_loadedSave || _state.Completed) return;
            var heroes = AllHeroes();
            if (_state.Plan == null)
                _state.Plan = ReignStartingChildrenPlanner.Plan(Census(heroes),
                    comesOfAge: TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge);
            // Resolve every template and parent before allocating anything; DLC absence is harmless
            // because the census consists only of the objects in this campaign.
            try
            {
                foreach (var spec in _state.Plan.Families.SelectMany(f => f.Children))
                    ResolveParents(spec, heroes, out _, out _, out _);
                ReignStartingChildrenPlanner.Apply(_state, !_loadedSave,
                    spec => Create(spec, heroes), (spec, id) => FinalizeChild(spec, id, heroes));
                WriteEvidence(heroes);
                ReignLog.Info("Starting children initialized: " + Snapshot().ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                _state.Error = ex.Message;
                WriteEvidence(heroes);
                ReignLog.Exception("New-campaign starting children failed; initialization is incomplete", ex);
                throw;
            }
        }

        private static Dictionary<string, Hero> AllHeroes() => Hero.AllAliveHeroes
            .Concat(Hero.DeadOrDisabledHeroes).Where(h => h != null && !h.IsTemplate)
            .GroupBy(h => h.StringId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        private static IEnumerable<StartingFamilyHero> Census(Dictionary<string, Hero> heroes)
        {
            var rulers = new HashSet<Hero>(Kingdom.All.Where(k => k.Leader != null).Select(k => k.Leader));
            return heroes.Values.Select(h => new StartingFamilyHero
            {
                Id = h.StringId, SpouseId = h.Spouse?.StringId, MotherId = h.Mother?.StringId,
                FatherId = h.Father?.StringId, ClanId = h.Clan?.StringId,
                Age = h.IsAlive ? h.Age : h.BirthDay.ElapsedYearsUntilNow,
                Female = h.IsFemale, Alive = h.IsAlive,
                EligibleParent = h.IsLord && !h.IsDisabled,
                Ruler = rulers.Contains(h), PlayerHousehold = h.Clan == Clan.PlayerClan || h == Hero.MainHero,
                Source = h.StringId.StartsWith("reign_court_", StringComparison.Ordinal) ? "reign"
                    : h.StringId.StartsWith("lord_7_", StringComparison.Ordinal) ? "war_sails" : "native_or_other"
            }).ToList();
        }

        private static void ResolveParents(StartingChildSpec spec, Dictionary<string, Hero> heroes,
            out Hero mother, out Hero father, out CharacterObject template)
        {
            if (!heroes.TryGetValue(spec.MotherId, out mother) || !heroes.TryGetValue(spec.FatherId, out father))
                throw new InvalidOperationException("Starting-child parent is missing: " + spec.SlotId);
            if (!mother.IsAlive || !father.IsAlive || mother.Spouse != father || father.Spouse != mother
                || mother.Clan == null || mother.Clan != father.Clan || mother.Clan == Clan.PlayerClan
                || Kingdom.All.Select(k => k.Leader).Contains(mother) || Kingdom.All.Select(k => k.Leader).Contains(father)
                || mother.CharacterObject.Race != father.CharacterObject.Race)
                throw new InvalidOperationException("Starting-child household is no longer eligible: " + spec.SlotId);
            template = TaleWorlds.CampaignSystem.Campaign.Current.Models.HeroCreationModel
                .GetCharacterTemplateForOffspring(mother, father, spec.Female);
            if (template == null || template.IsFemale != spec.Female || Home(mother, father) == null)
                throw new InvalidOperationException("Starting-child template or home is unavailable: " + spec.SlotId);
        }

        private static Settlement Home(Hero mother, Hero father) => mother.HomeSettlement
            ?? father.HomeSettlement ?? mother.BornSettlement ?? father.BornSettlement;

        private static string Create(StartingChildSpec spec, Dictionary<string, Hero> heroes)
        {
            ResolveParents(spec, heroes, out var mother, out var father, out var template);
            // Unlike newborn delivery followed by backdating, HeroCreated observes the final age.
            Hero child = HeroCreator.CreateChild(template, Home(mother, father), father.Clan, spec.Age);
            if (child == null) throw new InvalidOperationException("Native CreateChild returned null.");
            heroes.Add(child.StringId, child);
            return child.StringId;
        }

        private static void FinalizeChild(StartingChildSpec spec, string id, Dictionary<string, Hero> heroes)
        {
            ResolveParents(spec, heroes, out var mother, out var father, out _);
            Hero child = heroes[id];
            if ((child.Mother != null && child.Mother != mother) || (child.Father != null && child.Father != father))
                throw new InvalidOperationException("Created child has conflicting parentage: " + id);
            // Native setters append to Children; never assign a parent twice during recovery.
            if (child.Mother == null) child.Mother = mother;
            if (child.Father == null) child.Father = father;
            // CreateChild randomizes the birthday within this integer age. Normalize within the SAME
            // native age bucket so exact parental chronology and birth spacing match the plan.
            child.SetBirthDay(CampaignTime.YearsFromNow(-(float)spec.AgeYears));
            child.Culture = spec.Female ? mother.Culture : father.Culture;
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.HeroCreationModel;
            child.StaticBodyProperties = model.GetStaticBodyProperties(child, true);
            var names = model.GenerateFirstAndFullName(child);
            child.SetName(names.Item2, names.Item1);
            child.ClearTraits();
            EquipmentHelper.AssignHeroEquipmentFromEquipment(child, model.GetCivilianEquipment(child));
            EquipmentHelper.AssignHeroEquipmentFromEquipment(child, model.GetBattleEquipment(child));
            // Keep native NotSpawned child state. Activating a minor bypasses coming-of-age training.
            if (!child.IsAlive || !child.IsChild || !child.IsNotSpawned || child.Clan != father.Clan
                || child.IsFemale != spec.Female || Math.Abs(child.Age - spec.AgeYears) > 0.02f
                || child.PartyBelongedTo != null || child.Spouse != null || child.BornSettlement == null
                || mother.Children.Count(h => h == child) != 1 || father.Children.Count(h => h == child) != 1)
                throw new InvalidOperationException("Native starting-child postconditions failed: " + id);
            // No pregnancy clearing, OnGivenBirth, invented conception record, or adult court placement.
        }

        private void WriteEvidence(Dictionary<string, Hero> heroes)
        {
            try
            {
                if (string.IsNullOrEmpty(_reportPath))
                    _reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Bannerlord Reign", "tests", "starting-children", Guid.NewGuid().ToString("N") + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, JsonConvert.SerializeObject(new
                {
                    schema = "reign-starting-children-evidence-v1",
                    campaignId = TaleWorlds.CampaignSystem.Campaign.Current.UniqueGameId,
                    state = _state, livingAfter = heroes.Values.Count(h => h.IsAlive),
                    minorsAfter = heroes.Values.Count(h => h.IsAlive && h.Age >= 1 && h.Age < 18)
                }, Formatting.Indented));
            }
            catch (Exception ex) { ReignLog.Warn("Starting-child evidence file unavailable; save retains receipts: " + ex.Message); }
        }
    }
}
