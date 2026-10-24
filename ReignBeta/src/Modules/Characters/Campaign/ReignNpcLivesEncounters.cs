using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private bool _startingStoryEncounter;
        private readonly Dictionary<string, MapEvent> _storyBattles = new Dictionary<string, MapEvent>();
        private async void StartStoryEncounter(string storyId, string objectiveId)
        {
            if (_startingStoryEncounter) return;
            _startingStoryEncounter = true;
            string encounter = "";
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current; int epoch = _epoch;
            try
            {
                await RefreshLifeSnapshotAsync().ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => {
                if (!Current(campaign, epoch)) throw new InvalidOperationException("The campaign changed.");
                var story = State.Stories.FirstOrDefault(s => s.Id == storyId);
                if (story == null || story.Status != "active" || !StoriesAtCurrentPlace().Contains(story) || Mission.Current != null)
                    throw new InvalidOperationException("The companion and agreed location must be ready for this encounter.");
                var objective = story.Definition.Phases[story.PhaseIndex].Objectives.Single(o => o.Id == objectiveId && (o.Kind == "duel" || o.Kind == "hideout"));
                Hero hero = ReignObjectResolver.FindHero(story.HeroId);
                if (Hero.MainHero.IsWounded || hero.IsWounded) throw new InvalidOperationException("Both of you must recover before beginning this encounter.");
                if (_storyEncounters.Values.Any(e => e.Value<string>("storyId") == storyId && e.Value<string>("status") != "resolved"))
                    throw new InvalidOperationException("This story already has an encounter in progress.");
                encounter = "npc_encounter_" + Guid.NewGuid().ToString("N");
                var p = ReignServerClient.CaptureNpcLivesContext(Generation);
                p["id"] = story.Id; p["revision"] = story.Revision; p["heroId"] = story.HeroId;
                p["kind"] = "encounter_started"; p["objectiveId"] = objective.Id;
                p["encounterId"] = encounter; p["nativeReceipt"] = encounter + ":started";
                _storyEncounters[encounter] = new JObject { ["storyId"] = story.Id, ["revision"] = story.Revision,
                    ["heroId"] = story.HeroId, ["objectiveId"] = objective.Id, ["kind"] = objective.Kind,
                    ["locationId"] = story.LocationId, ["requiredOutcome"] = objective.RequiredOutcome,
                    ["status"] = "preparing", ["generation"] = Generation, ["campaignId"] = _campaign, ["timelineId"] = _timeline };
                QueueLifeOperation("native", p, encounter + ":started");
                }).ConfigureAwait(false);
                // The game remains paused while transport confirms the exact phase.
                for (int i = 0; i < 150; i++)
                {
                    if (await ReignMainThread.InvokeAsync(() => !Current(campaign, epoch) || _acceptedOperations.Contains(encounter + ":started")).ConfigureAwait(false)) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }
                await ReignMainThread.InvokeAsync(() => {
                var row = _storyEncounters[encounter];
                Hero hero = ReignObjectResolver.FindHero(row.Value<string>("heroId"));
                if (!Current(campaign, epoch) || !_acceptedOperations.Contains(encounter + ":started")
                    || Mission.Current != null || !StoriesAtCurrentPlace().Any(st => st.Id == storyId && st.Revision == row.Value<int>("revision")))
                    throw new InvalidOperationException("The encounter could not be prepared. It remains available to retry.");
                _storyEncounters[encounter]["status"] = "active";
                if (row.Value<string>("kind") == "duel")
                {
                    if (!ReignDuelService.StartStoryPractice(encounter, hero, out string error)) throw new InvalidOperationException(error);
                }
                else StartStoryHideout(encounter, hero);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => {
                    if (!Current(campaign, epoch)) return;
                    if (encounter.Length > 0 && _storyEncounters.TryGetValue(encounter, out var failed)) failed["launchFailure"] = ex.GetBaseException().Message;
                    if (encounter.Length > 0) ResolveStoryEncounter(encounter, "withdrawn");
                    InformationManager.DisplayMessage(new InformationMessage(ex.Message));
                }).ConfigureAwait(false);
            }
            finally { await ReignMainThread.InvokeAsync(() => { if (Current(campaign, epoch)) _startingStoryEncounter = false; }).ConfigureAwait(false); }
        }
        private void StartStoryHideout(string encounter, Hero companion)
        {
            Settlement place = Settlement.CurrentSettlement;
            var native = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<HideoutCampaignBehavior>();
            if (place?.Hideout == null || native == null || !place.Parties.Any(p => p.IsBandit)
                || !place.Hideout.NextPossibleAttackTime.IsPast)
                throw new InvalidOperationException("This hideout is empty or cannot yet be attacked.");
            var nightMethod = AccessTools.Method(typeof(HideoutCampaignBehavior), "IsItNighttimeNow", Type.EmptyTypes);
            var startMethod = AccessTools.Method(typeof(HideoutCampaignBehavior), "OnTroopRosterManageDone", new[] { typeof(TroopRoster), typeof(bool) });
            if (nightMethod == null || startMethod == null) throw new InvalidOperationException("This game version does not provide the required hideout adapter.");
            bool assault = !(bool)nightMethod.Invoke(native, null);
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.BanditDensityModel;
            int maximum = model.GetMaximumTroopCountForHideoutMission(MobileParty.MainParty, assault);
            int minimum = model.GetMinimumTroopCountForHideoutMission(MobileParty.MainParty, assault);
            if (maximum < 2 || MobileParty.MainParty.MemberRoster.TotalHealthyCount < Math.Max(2, minimum))
                throw new InvalidOperationException("There are not enough healthy party members to attempt this hideout.");
            var troops = TroopRoster.CreateDummyTroopRoster();
            troops.AddToCounts(Hero.MainHero.CharacterObject, 1); troops.AddToCounts(companion.CharacterObject, 1);
            foreach (var troop in MobileParty.MainParty.MemberRoster.GetTroopRoster().Where(t => t.Character != Hero.MainHero.CharacterObject && t.Character != companion.CharacterObject)
                .OrderByDescending(t => t.Character.Level))
            {
                int count = Math.Min(maximum - troops.TotalManCount, troop.Number - troop.WoundedNumber);
                if (count > 0) troops.AddToCounts(troop.Character, count);
                if (troops.TotalManCount >= maximum) break;
            }
            PrepareStoryNativeBinding(encounter, place);
            startMethod.Invoke(native, new object[] { troops, assault });
            // Native mission opening is deferred until the game-state transition.
            // Its map event exists now; the witness initializes with the scene.
            if (MobileParty.MainParty.MapEvent?.IsHideoutBattle != true)
                throw new InvalidOperationException("The native hideout encounter did not start.");
            _storyBattles[encounter] = MobileParty.MainParty.MapEvent;
        }
        internal void WitnessStoryParticipants(Mission mission)
        {
            if (!ReferenceEquals(mission, Mission.Current) || MobileParty.MainParty?.MapEvent == null) return;
            foreach (var entry in _storyBattles)
                if (ReferenceEquals(entry.Value, MobileParty.MainParty.MapEvent)
                    && _storyEncounters.TryGetValue(entry.Key, out var row) && row.Value<string>("status") == "active"
                    && row.Value<bool?>("participantWitnessed") != true
                    && mission.AllAgents.Any(a => a.IsActive() && a.Character is CharacterObject c && c.HeroObject?.StringId == row.Value<string>("heroId")))
                    row["participantWitnessed"] = true;
        }
        private void OnStoryHideoutCompleted(BattleSideEnum winner, HideoutEventComponent component, HideoutEventComponent.HideoutBattleEndState end)
        {
            foreach (var entry in _storyBattles.Where(e => ReferenceEquals(e.Value, component?.MapEvent)).ToList())
            {
                if (!_storyEncounters.TryGetValue(entry.Key, out var row) || row.Value<string>("status") != "active") continue;
                bool won = end == HideoutEventComponent.HideoutBattleEndState.Victory && winner == component.MapEvent.PlayerSide
                    && row.Value<bool?>("participantWitnessed") == true;
                if (won)
                {
                    try { row["nativeOutcomeEvidence"] = CompleteStoryNativeBinding(row); }
                    catch (Exception ex) { won = false; row["failure"] = ex.Message;
                        InformationManager.DisplayMessage(new InformationMessage("The story outcome needs review: " + ex.Message)); }
                }
                ResolveStoryEncounter(entry.Key, won ? "won" : end == HideoutEventComponent.HideoutBattleEndState.Defeated ? "lost" : "withdrawn");
                _storyBattles.Remove(entry.Key);
            }
        }
        internal void ResolveStoryEncounter(string encounter, string outcome)
        {
            if (!_storyEncounters.TryGetValue(encounter, out var row) || row.Value<string>("status") == "resolved") return;
            if (row.Value<string>("campaignId") != _campaign || row.Value<string>("timelineId") != _timeline) return;
            if (outcome != "won" && outcome != "lost" && outcome != "withdrawn") throw new ArgumentException("A native encounter outcome is required.");
            row["status"] = "resolved"; row["outcome"] = outcome;
            var p = ReignServerClient.CaptureNpcLivesContext(Generation);
            foreach (string key in new[] { "heroId", "revision", "objectiveId", "kind", "locationId" }) p[key] = row[key].DeepClone();
            p["id"] = row["storyId"].DeepClone(); p["encounterId"] = encounter;
            p["outcome"] = outcome == "won" && row.Value<string>("requiredOutcome").Length > 0 ? row.Value<string>("requiredOutcome") : outcome;
            p["nativeReceipt"] = encounter + ":result";
            if (row["nativeOutcomeEvidence"] != null) p["nativeOutcomeEvidence"] = row["nativeOutcomeEvidence"].DeepClone();
            QueueLifeOperation("native", p, encounter + ":result");
        }
    }
    internal sealed class NpcStoryMissionWitness : MissionLogic
    {
        public override void OnMissionTick(float dt) => ReignNpcLivesBehavior.Instance?.WitnessStoryParticipants(Mission);
    }
}
