using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using ReignBeta.Shared.Characters;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior : CampaignBehaviorBase
    {
        public static ReignNpcLivesBehavior Instance { get; private set; }
        internal NpcLivesState State { get; private set; } = new NpcLivesState();
        internal string Generation { get; private set; } = "";
        private string _campaign = "", _timeline = "";
        private List<string> _chunks = new List<string>();
        private List<JObject> _outbox = new List<JObject>();
        private HashSet<string> _nativeEffects = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _acceptedOperations = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, string> _failedOperations = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, double> _shownUntil = new Dictionary<string, double>(StringComparer.Ordinal);
        private int _epoch;
        private bool _busy, _initialized;
        private double _real, _nextPoll, _nextEvaluation;
        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddLifeMenus);
            CampaignEvents.OnHideoutBattleCompletedEvent.AddNonSerializedListener(this, OnStoryHideoutCompleted);
            CampaignEvents.CanMoveToSettlementEvent.AddNonSerializedListener(this, OnMeetingCanMoveToSettlement);
            CampaignEvents.OnHeroGetsBusyEvent.AddNonSerializedListener(this, OnMeetingHeroGetsBusy);
            CampaignEvents.OnHeroTeleportationRequestedEvent.AddNonSerializedListener(this, OnMeetingTeleportRequested);
        }
        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) _chunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(new {
                version = 1, state = State, outbox = _outbox, nativeEffects = _nativeEffects, acceptedOperations = _acceptedOperations, failedOperations = _failedOperations,
                shownUntil = _shownUntil, travel = _personalTravel, meetings = _meetingTravel, duties = _lifeDuties, encounters = _storyEncounters, bindings = _storyBindings,
                communicationHosts = _communicationHosts
            }));
            store.SyncData("ReignNpcLivesV1", ref _chunks);
            if (store.IsLoading)
            {
                _meetingMovement.Clear();
                _epoch++; _initialized = _busy = false; Generation = ""; _nextPoll = _nextEvaluation = 0;
                if (_chunks == null || _chunks.Count == 0) return;
                var saved = JObject.Parse(ReignSavePayloadCodec.Decode(_chunks));
                if (saved.Value<int>("version") != 1) throw new InvalidOperationException("Unsupported NPC Lives save format.");
                State = saved["state"]?.ToObject<NpcLivesState>() ?? new NpcLivesState();
                _outbox = saved["outbox"]?.ToObject<List<JObject>>() ?? new List<JObject>();
                _nativeEffects = saved["nativeEffects"]?.ToObject<HashSet<string>>() ?? new HashSet<string>(StringComparer.Ordinal);
                _acceptedOperations = saved["acceptedOperations"]?.ToObject<HashSet<string>>() ?? new HashSet<string>(StringComparer.Ordinal);
                _failedOperations = saved["failedOperations"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>(StringComparer.Ordinal);
                _shownUntil = saved["shownUntil"]?.ToObject<Dictionary<string, double>>() ?? new Dictionary<string, double>(StringComparer.Ordinal);
                RestoreLifeTravel(saved);
            }
        }
        private bool Current(NativeCampaign owner, int epoch) => Instance == this && ReferenceEquals(owner, NativeCampaign.Current) && epoch == _epoch
            && _campaign == ReignServerClient.GetCampaignId() && _timeline == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main");
        private static bool Ready() => NativeCampaign.Current != null && MobileParty.MainParty != null && Hero.MainHero?.IsAlive == true
            && !ReignCampaignInitializationGate.IsPending && Settings.ReignBetaSettings.Instance?.UseLocalServer != false;
        private static bool QuietMap() => ReignPlayerApproachPopup.CanPresentOnMap() && !ReignPlayerApproachPopup.IsOpen
            && !ReignPartyChatScreenManager.IsOpenOrPending && !ReignIndividualChatScreenManager.IsOpen && !ReignSocialEventScreenManager.IsOpen
            && Mission.Current == null && NativeCampaign.Current?.ConversationManager?.IsConversationInProgress != true
            && MobileParty.MainParty?.MapEvent == null && Hero.MainHero?.IsPrisoner == false;
        public void ApplicationTick(float dt)
        {
            _real += Math.Max(0, dt);
            if (!Ready() || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return;
            string campaign = ReignServerClient.GetCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            if (_campaign != campaign || _timeline != timeline)
            { _epoch++; _busy = _initialized = false; _campaign = campaign; _timeline = timeline; Generation = ""; _nextPoll = _nextEvaluation = 0; }
            if (ReignLiveInteractionTestHost.OwnsAutomationSession && !NpcLivesTestOwnsSession
                && !ReignLiveInteractionTestHost.OwnsPassiveWorldSession) return;
            if (_initialized) { AdvanceCommunicationHosts(); AdvanceLifeTravel(); }
            if (_busy || _real < _nextPoll) return;
            _nextPoll = _real + 5;
            if (!_initialized) { Request("state", ReignServerClient.CaptureNpcLivesContext("")); return; }
            ApplyLifeRewards();
            if (_outbox.Count > 0)
            {
                var row = _outbox[0];
                if (HoldAnnouncementDeliveryForNativeTest(row)) return;
                var p = (JObject)row["payload"].DeepClone();
                p["generation"] = Generation;
                Request(row.Value<string>("route"), p, row.Value<string>("operationId")); return;
            }
            double day = CampaignTime.Now.ToDays;
            if (QuietMap() && ReignServerClient.NpcLivesOptionsConfirmed && day >= _nextEvaluation)
            {
                _nextEvaluation = day + 1d / 24;
                var p = ReignServerClient.CaptureNpcLivesContext(Generation, true, leaveDestinations: true); p["locations"] = ReignServerClient.CaptureNpcStoryLocations();
                Request("evaluate", p); return;
            }
            Request("state", ReignServerClient.CaptureNpcLivesContext(Generation));
        }
        private void Request(string route, JObject payload, string operation = "")
        {
            _busy = true; NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            _ = Run();
            async Task Run()
            {
                try
                {
                    var response = await ReignServerClient.NpcLivesRequestAsync(route, payload).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => {
                        if (!Current(owner, epoch)) return;
                        if (response.Value<bool?>("ok") != true)
                        {
                            if (operation.Length > 0 && response.Value<string>("status") == "rejected" && response.Value<string>("operationId") == operation)
                            {
                                _failedOperations[operation] = response.Value<string>("error") ?? "This request requires review.";
                                _outbox.RemoveAll(r => r.Value<string>("operationId") == operation);
                                InformationManager.DisplayMessage(new InformationMessage("Personal matter needs review: " + _failedOperations[operation]));
                            }
                            if (response.Value<string>("status") == "stale_generation") { _initialized = false; Generation = ""; }
                            throw new InvalidOperationException(response.Value<string>("error") ?? "NPC Lives request was not accepted.");
                        }
                        string generation = response.Value<string>("generation") ?? "";
                        var state = response["state"]?.ToObject<NpcLivesState>();
                        if (state == null || state.CampaignId != _campaign || state.TimelineId != _timeline || generation.Length == 0)
                            throw new InvalidOperationException("NPC Lives returned a different campaign.");
                        bool hadStoryLocations = StoryLocationsAvailable();
                        if (generation != Generation || state.Revision >= State.Revision) State = state;
                        Generation = generation; _initialized = true;
                        RefreshStoryLocationMenu(hadStoryLocations);
                        if (operation.Length > 0) { _acceptedOperations.Add(operation); _outbox.RemoveAll(r => r.Value<string>("operationId") == operation); }
                        if (route == "announcement")
                        {
                            payload["contributionId"] = operation; payload["playerHeroStringId"] = payload["playerId"]?.DeepClone();
                            ReignConversationInputJournal.Acknowledge(payload, response);
                        }
                        ReignAgreementBehavior.Instance?.RecheckPendingAgreements();
                        if (QuietMap()) PresentLifeInvitation(response.Value<string>("invitationId"));
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) { ReignLog.Warn("NPC Lives request awaiting retry: " + ex.Message); }
                finally { await ReignMainThread.InvokeAsync(() => { if (Current(owner, epoch)) _busy = false; }).ConfigureAwait(false); }
            }
        }
        internal void QueueLifeOperation(string route, JObject p, string operation)
        {
            if (_acceptedOperations.Contains(operation) || _failedOperations.ContainsKey(operation) || _outbox.Any(r => r.Value<string>("operationId") == operation)) return;
            p["operationId"] = operation;
            _outbox.Add(new JObject { ["route"] = route, ["operationId"] = operation, ["payload"] = p.DeepClone() });
            _nextPoll = 0;
        }
        internal async Task RefreshLifeSnapshotAsync()
        {
            NativeCampaign owner = null; int epoch = 0;
            var p = await ReignMainThread.InvokeAsync(() => { owner = NativeCampaign.Current; epoch = _epoch; return ReignServerClient.CaptureNpcLivesContext(Generation); }).ConfigureAwait(false);
            var response = await ReignServerClient.NpcLivesRequestAsync("state", p).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => {
                if (!Current(owner, epoch)) throw new InvalidOperationException("The campaign changed while loading personal lives.");
                if (response.Value<bool?>("ok") != true) throw new InvalidOperationException(response.Value<string>("error") ?? "Personal lives are unavailable.");
                var state = response["state"]?.ToObject<NpcLivesState>(); string generation = response.Value<string>("generation") ?? "";
                if (state == null || state.CampaignId != _campaign || state.TimelineId != _timeline || generation.Length == 0)
                    throw new InvalidOperationException("Personal lives returned a different campaign.");
                bool hadStoryLocations = StoryLocationsAvailable();
                if (generation != Generation || state.Revision >= State.Revision) State = state;
                Generation = generation; _initialized = true;
                RefreshStoryLocationMenu(hadStoryLocations);
            }).ConfigureAwait(false);
        }
        private void ApplyLifeRewards()
        {
            var receipts = new JArray();
            foreach (var effect in State.Effects.Where(e => e.Status == "pending" && e.Kind == "skill_xp"))
            {
                Hero hero = ReignActionService.HeroById(effect.HeroId);
                if (hero == null || !hero.IsAlive) continue;
                if (effect.Amount == 0) _nativeEffects.Add(effect.Id);
                if (!_nativeEffects.Contains(effect.Id))
                {
                    var skill = typeof(DefaultSkills).GetProperty(effect.Skill, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null) as SkillObject;
                    if (skill == null || effect.Amount <= 0) { ReignLog.Warn("Unsupported personal-story reward: " + effect.Skill); continue; }
                    hero.AddSkillXp(skill, effect.Amount);
                    _nativeEffects.Add(effect.Id);
                    ReignLog.Info("NPC Lives reward receipt=" + effect.Id + " hero=" + hero.StringId + " skill=" + effect.Skill + " xp=" + effect.Amount);
                }
                receipts.Add(new JObject { ["id"] = effect.Id, ["heroId"] = effect.HeroId, ["nativeReceipt"] = effect.Id });
            }
            if (receipts.Count == 0) return;
            var p = ReignServerClient.CaptureNpcLivesContext(Generation); p["receipts"] = receipts;
            QueueLifeOperation("acknowledge", p, "xp_ack:" + string.Join("|", receipts.OfType<JObject>().Select(r => r.Value<string>("id"))));
        }
        private void PresentLifeInvitation(string requested)
        {
            if (!QuietMap()) return;
            var story = State.Stories.FirstOrDefault(s => s.Id == requested) ?? State.Stories.FirstOrDefault(s => s.Status == "invited"
                && ReignServerClient.NpcLivesOptions.Allows(s.Definition.Category));
            var leave = State.Leaves.FirstOrDefault(l => l.Id == requested) ?? State.Leaves.FirstOrDefault(l => l.Status == "discussing"
                && ReignServerClient.NpcLivesOptions.Allows("leave"));
            if (story != null && (story.Status != "invited" || !ReignServerClient.NpcLivesOptions.Allows(story.Definition.Category))) story = null;
            if (leave != null && (leave.Status != "discussing" || !ReignServerClient.NpcLivesOptions.Allows("leave"))) leave = null;
            string id = story?.Id ?? leave?.Id; if (id == null) return;
            int revision = story?.Revision ?? leave.Revision; string displayKey = id + ":" + revision;
            if (_shownUntil.TryGetValue(displayKey, out var until) && CampaignTime.Now.ToDays < until) return;
            Hero hero = ReignActionService.HeroById(story?.HeroId ?? leave.HeroId);
            if (hero == null || hero.IsPrisoner || !MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject)) return;
            var prior = NativeCampaign.Current.TimeControlMode; var owner = NativeCampaign.Current; int epoch = _epoch;
            void Release() { if (Current(owner, epoch) && !owner.TimeControlModeLock) owner.TimeControlMode = prior; }
            ReignPlayerApproachPopup.TryPresentOnMap(() => {
                owner.TimeControlMode = CampaignTimeControlMode.Stop;
                var popup = new ReignPlayerApproachPopup("A PERSONAL MATTER", hero.Name + " would like a moment with you.",
                    "Approach", "Another time", accept => {
                        Release(); if (!Current(owner, epoch)) return;
                        _shownUntil[displayKey] = CampaignTime.Now.ToDays + NpcLivesPolicy.ReaskDelay(ReignServerClient.NpcLivesOptions);
                        if (accept) ReignIndividualChatScreenManager.OpenForHero(hero,
                            new JObject { ["npcLifeRequestId"] = id, ["npcStoryId"] = story?.Id ?? "", ["npcLivesRevision"] = revision,
                                ["npcLifeInvitationEntry"] = true }, null);
                    }, () => { Release(); if (Current(owner, epoch)) _shownUntil[displayKey] = CampaignTime.Now.ToDays + NpcLivesPolicy.ReaskDelay(ReignServerClient.NpcLivesOptions); });
                ScreenManager.PushScreen(popup);
                if (!popup.Initialized) { ScreenManager.PopScreen(); Release(); }
            });
        }
        internal async Task<JObject> AnnounceAsync(string text, string operationId)
        {
            if (!ReignServerClient.NpcLivesOptions.Allows("announcement")) throw new InvalidOperationException("Party announcements are disabled.");
            if (!_initialized) throw new InvalidOperationException("Party information is still loading. Try again in a moment.");
            // The exact roster and words are saved in the outbox before transport.
            var p = ReignServerClient.CaptureNpcLivesContext(Generation, true); p["text"] = text;
            QueueLifeOperation("announcement", p, operationId);
            return await Task.FromResult(new JObject { ["ok"] = true, ["status"] = "queued", ["operationId"] = operationId });
        }
        internal bool NpcLivesTestOwnsSession => ReignLiveInteractionTestHost.OwnsNpcLivesSession;
    }
}
