using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.World
{
    internal sealed class ReignFollowAgreement
    {
        public string ActorHeroStringId = string.Empty;
        public string TargetHeroStringId = string.Empty;
        public string ManagedPartyStringId = string.Empty;
        public string ReturnSettlementStringId = string.Empty;
        public string SourceActionId = string.Empty;
        public bool Active = true;
    }

    /// <summary>
    /// Owns the durable agreement behind one Follow request. Mission, settlement and
    /// campaign-map movement are representations of this record, rather than separate
    /// player-facing actions.
    /// </summary>
    internal sealed class ReignFollowCampaignBehavior : CampaignBehaviorBase
    {
        private const string SaveKey = "_reign_follow_agreements_v1";
        private readonly List<ReignFollowAgreement> _agreements = new List<ReignFollowAgreement>();
        private List<string> _stateChunks = new List<string>();
        private float _representationTickIn;

        internal static ReignFollowCampaignBehavior Instance { get; private set; }

        internal IEnumerable<Tuple<Hero, Hero>> ActiveSceneFollowers
        {
            get
            {
                foreach (ReignFollowAgreement agreement in _agreements.Where(item => item?.Active == true).ToList())
                {
                    Hero actor = FindHero(agreement.ActorHeroStringId);
                    Hero target = FindHero(agreement.TargetHeroStringId) ?? Hero.MainHero;
                    if (actor != null && target != null && actor.IsAlive && !actor.IsPrisoner)
                        yield return Tuple.Create(actor, target);
                }
            }
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, ReconcileAfterLoad);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnCampaignTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, _ => ApplicationTick(0f));
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
                _stateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_agreements));
            dataStore.SyncData(SaveKey, ref _stateChunks);
            if (!dataStore.IsLoading) return;
            _agreements.Clear();
            if (_stateChunks != null && _stateChunks.Count > 0)
                _agreements.AddRange(JsonConvert.DeserializeObject<List<ReignFollowAgreement>>(
                    ReignSavePayloadCodec.Decode(_stateChunks)) ?? new List<ReignFollowAgreement>());
            Normalize();
        }

        internal ReignActionResult Start(ReignWorldActionRecord action, Hero actor, Hero target,
            ReignSceneActionMissionBehavior scene)
        {
            target = target ?? Hero.MainHero;
            if (actor == null || target == null || actor == target || !actor.IsAlive || actor.IsPrisoner)
                return ReignActionResult.ValidationFailed("Follow requires a living, available NPC and a different living target.");

            ReignFollowAgreement agreement = _agreements.FirstOrDefault(item => item.Active
                && Same(item.ActorHeroStringId, actor.StringId));
            if (agreement == null)
            {
                agreement = new ReignFollowAgreement
                {
                    ActorHeroStringId = actor.StringId,
                    ReturnSettlementStringId = (actor.CurrentSettlement ?? actor.HomeSettlement)?.StringId ?? string.Empty
                };
                _agreements.Add(agreement);
            }
            agreement.Active = true;
            agreement.TargetHeroStringId = target.StringId;
            agreement.SourceActionId = action?.ActionId ?? agreement.SourceActionId;
            EnsureAccompanyingCharacter(agreement, actor, target);

            if (scene?.CanFollow(actor, target) == true)
                return scene.Begin(action, actor, target);

            if (!EnsureMapFollow(agreement, actor, target, out MobileParty actorParty, out string failure))
            {
                agreement.Active = false;
                CleanupAgreement(agreement, actor);
                return ReignActionResult.ValidationFailed(failure);
            }

            return ReignActionResult.Done(actor.Name + " is following " + target.Name + " across the campaign map and in person.")
                .WithEffect("follow_agreement_started", "hero", actor.StringId, actor.Name.ToString(),
                    "scope=campaign_map_and_scene;targetHeroId=" + target.StringId)
                .WithEffect("movement_order", "party", actorParty.StringId,
                    actorParty.Name?.ToString() ?? actorParty.StringId, "order=follow_on_map")
                .WithChangedEntity("party", actorParty.StringId,
                    actorParty.Name?.ToString() ?? actorParty.StringId, "movement_order_changed");
        }

        internal ReignActionResult Stop(Hero actor, ReignSceneActionMissionBehavior scene)
        {
            if (actor == null) return ReignActionResult.ValidationFailed("Stop following requires an NPC.");
            bool sceneStopped = scene?.IsFollowing(actor) == true;
            if (sceneStopped) scene.Stop(actor, "The NPC stopped following.");
            ReignFollowAgreement agreement = _agreements.LastOrDefault(item => item.Active
                && Same(item.ActorHeroStringId, actor.StringId));
            if (agreement == null)
            {
                MobileParty party = actor.PartyBelongedTo;
                if (!sceneStopped && party == null)
                    return ReignActionResult.NoOp(actor.Name + " was not following.");
                if (party?.DefaultBehavior == AiBehavior.EscortParty) party.SetMoveModeHold();
                return ReignActionResult.Done(actor.Name + " stopped following.")
                    .WithEffect("follow_agreement_stopped", "hero", actor.StringId,
                        actor.Name.ToString(), "scope=campaign_map_and_scene");
            }

            agreement.Active = false;
            CleanupAgreement(agreement, actor);
            return ReignActionResult.Done(actor.Name + " stopped following on the campaign map and in person.")
                .WithEffect("follow_agreement_stopped", "hero", actor.StringId,
                    actor.Name.ToString(), "scope=campaign_map_and_scene");
        }

        internal void ApplicationTick(float dt)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null) return;
            if ((_representationTickIn -= dt) > 0f) return;
            _representationTickIn = 0.5f;
            foreach (ReignFollowAgreement agreement in _agreements.ToList())
            {
                Hero actor = FindHero(agreement.ActorHeroStringId);
                if (!agreement.Active)
                {
                    if (CleanupAgreement(agreement, actor)) _agreements.Remove(agreement);
                    continue;
                }
                Hero target = FindHero(agreement.TargetHeroStringId) ?? Hero.MainHero;
                if (actor == null || target == null || !actor.IsAlive || actor.IsPrisoner)
                {
                    agreement.Active = false;
                    continue;
                }
                EnsureAccompanyingCharacter(agreement, actor, target);
                if (Mission.Current == null && MobileParty.MainParty?.CurrentSettlement == null)
                    EnsureMapFollow(agreement, actor, target, out _, out _);
            }
        }

        internal static JArray RuntimeSnapshot()
        {
            var result = new JArray();
            if (Instance == null) return result;
            foreach (ReignFollowAgreement agreement in Instance._agreements.Where(item => item?.Active == true))
            {
                Hero actor = FindHero(agreement.ActorHeroStringId);
                MobileParty party = actor?.PartyBelongedTo;
                result.Add(new JObject
                {
                    ["actorHeroId"] = agreement.ActorHeroStringId,
                    ["targetHeroId"] = agreement.TargetHeroStringId,
                    ["sourceActionId"] = agreement.SourceActionId,
                    ["managedPartyId"] = agreement.ManagedPartyStringId,
                    ["partyId"] = party?.StringId ?? string.Empty,
                    ["mapBehavior"] = party?.DefaultBehavior.ToString() ?? string.Empty,
                    ["targetPartyId"] = party?.TargetParty?.StringId ?? string.Empty,
                    ["sceneBound"] = ReignSceneActionMissionBehavior.Current?.IsFollowing(actor) == true
                });
            }
            return result;
        }

        private void OnCampaignTick(float dt) => ApplicationTick(dt);

        private void ReconcileAfterLoad()
        {
            Normalize();
            ApplicationTick(0f);
        }

        private void Normalize()
        {
            for (int index = _agreements.Count - 1; index >= 0; index--)
            {
                ReignFollowAgreement item = _agreements[index];
                if (item == null || string.IsNullOrWhiteSpace(item.ActorHeroStringId))
                    _agreements.RemoveAt(index);
                else if (string.IsNullOrWhiteSpace(item.TargetHeroStringId))
                    item.TargetHeroStringId = Hero.MainHero?.StringId ?? "main_hero";
            }
            foreach (IGrouping<string, ReignFollowAgreement> duplicate in _agreements
                .Where(item => item.Active).GroupBy(item => item.ActorHeroStringId, StringComparer.OrdinalIgnoreCase))
                foreach (ReignFollowAgreement stale in duplicate.Take(Math.Max(0, duplicate.Count() - 1))) stale.Active = false;
        }

        private static void EnsureAccompanyingCharacter(ReignFollowAgreement agreement, Hero actor, Hero target)
        {
            if (actor == null || target != Hero.MainHero || PlayerEncounter.LocationEncounter == null) return;
            LocationEncounter encounter = PlayerEncounter.LocationEncounter;
            if (encounter.GetAccompanyingCharacter(actor.CharacterObject) != null) return;
            MobileParty party = actor.PartyBelongedTo ?? MobileParty.MainParty;
            if (party == null) return;
            LocationCharacter character = LocationCharacter.CreateBodyguardHero(actor, party,
                new LocationCharacter.AddBehaviorsDelegate(SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors));
            encounter.AddAccompanyingCharacter(character, true);
            ReignLog.Info("Follow accompanying character added actor=" + actor.StringId
                + " target=" + target.StringId + " action=" + agreement.SourceActionId);
        }

        private static bool EnsureMapFollow(ReignFollowAgreement agreement, Hero actor, Hero target,
            out MobileParty actorParty, out string failure)
        {
            actorParty = actor?.PartyBelongedTo;
            failure = string.Empty;
            MobileParty targetParty = target == Hero.MainHero ? MobileParty.MainParty : target?.PartyBelongedTo;
            if (targetParty == null || !targetParty.IsActive)
            {
                failure = "The selected follow target has no active campaign party.";
                return false;
            }
            if (actorParty == null)
            {
                Settlement home = Settlement.Find(agreement.ReturnSettlementStringId)
                    ?? actor.CurrentSettlement ?? actor.HomeSettlement ?? targetParty.CurrentSettlement;
                // A clanless resident still needs a faction for Bannerlord's map
                // track and party AI. This assigns the temporary party its home
                // settlement's faction without changing the hero's clan.
                Clan mapClan = actor.Clan ?? home?.OwnerClan;
                if (mapClan == null)
                {
                    failure = "The follower has no safe campaign faction at their home settlement.";
                    return false;
                }
                CampaignVec2 position = actor.CurrentSettlement?.GatePosition ?? targetParty.Position;
                if (actor.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(actor);
                TroopRoster members = TroopRoster.CreateDummyTroopRoster();
                members.AddToCounts(actor.CharacterObject, 1);
                actorParty = CustomPartyComponent.CreateCustomPartyWithTroopRoster(position, 1f, home,
                    new TextObject("{=!}" + actor.Name + " (following)"), mapClan, members,
                    TroopRoster.CreateDummyTroopRoster(), actor, null, null, 2f, true);
                agreement.ManagedPartyStringId = actorParty.StringId;
                ReignLog.Info("Follow campaign party created actor=" + actor.StringId
                    + " party=" + actorParty.StringId + " mapClan=" + mapClan.StringId
                    + " action=" + agreement.SourceActionId);
            }
            if (!actorParty.IsActive || actorParty.LeaderHero != actor || actorParty == targetParty)
            {
                failure = "The follower does not control a distinct active campaign party.";
                return false;
            }
            if (actorParty.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(actorParty);
            SetPartyAiAction.GetActionForEscortingParty(actorParty, targetParty,
                actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, targetParty.IsCurrentlyAtSea);
            if (actorParty.DefaultBehavior != AiBehavior.EscortParty || actorParty.TargetParty != targetParty)
            {
                failure = "Bannerlord did not apply the campaign escort order.";
                return false;
            }
            return true;
        }

        private static bool CleanupAgreement(ReignFollowAgreement agreement, Hero actor)
        {
            PlayerEncounter.LocationEncounter?.RemoveAccompanyingCharacter(actor);
            MobileParty party = FindParty(agreement.ManagedPartyStringId);
            if (party == null)
            {
                MobileParty existing = actor?.PartyBelongedTo;
                if (existing?.DefaultBehavior == AiBehavior.EscortParty) existing.SetMoveModeHold();
                return true;
            }
            if (party.MapEvent != null || party.SiegeEvent != null) return false;
            Settlement destination = Settlement.Find(agreement.ReturnSettlementStringId) ?? actor?.HomeSettlement;
            if (actor?.PartyBelongedTo == party)
                party.MemberRoster.AddToCounts(actor.CharacterObject, -1);
            DestroyPartyAction.Apply(null, party);
            if (actor != null && actor.IsAlive && !actor.IsPrisoner && actor.PartyBelongedTo == null && destination != null)
                EnterSettlementAction.ApplyForCharacterOnly(actor, destination);
            ReignLog.Info("Follow campaign party removed actor=" + (actor?.StringId ?? agreement.ActorHeroStringId)
                + " party=" + agreement.ManagedPartyStringId);
            return true;
        }

        private static MobileParty FindParty(string id) => string.IsNullOrWhiteSpace(id) ? null
            : MobileParty.All.FirstOrDefault(item => item != null && Same(item.StringId, id));
        private static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id) ? null : Hero.Find(id);
        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
