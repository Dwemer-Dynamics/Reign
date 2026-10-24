using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.World;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using ReignBeta.Settings;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNewsCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignNewsCampaignBehavior Instance { get; private set; }
        private ReignNewsState _state = new ReignNewsState();
        private List<string> _chunks = new List<string>();
        private string _activeId = "";
        private int _epoch;
        private float _retryDelay;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnCaptured);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnReleased);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnKilled);
        }
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) _chunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state));
            dataStore.SyncData("ReignTownCrierV1", ref _chunks);
            if (!dataStore.IsLoading) return;
            _epoch++; _activeId = ""; _retryDelay = 0;
            _state = _chunks == null || _chunks.Count == 0 ? new ReignNewsState()
                : JsonConvert.DeserializeObject<ReignNewsState>(ReignSavePayloadCodec.Decode(_chunks)) ?? new ReignNewsState();
            _state.NormalizeAfterLoad();
        }
        internal void Publish(ReignNewsArticle article)
        {
            if (!_state.Publish(article, ReignServerClient.NewsOptions, ReignServerClient.NewsOptionsConfirmed)) return;
            InformationManager.DisplayMessage(new InformationMessage("[Reign Herald] " + article.Text, Color.FromUint(0xFFC5BDAF)));
        }
        internal void ApplyOptions()
        {
            if (!ReignServerClient.NewsOptionsConfirmed) return;
            _state.ApplyOptions(ReignServerClient.NewsOptions);
            if (!ReignServerClient.NewsOptions.Allows(ReignNewsCategories.Diplomacy))
                ReignDiplomacyAnnouncementScreenManager.TryAcknowledgeDisabledDiplomacy();
            if (_activeId.Length > 0 && !_state.Pending.Any(p => p.Id == _activeId))
                ReignDiplomacyAnnouncementScreenManager.TryCloseOwnedAnnouncement(_activeId);
        }
        internal void ApplicationTick(float dt)
        {
            _retryDelay = Math.Max(0, _retryDelay - dt);
            if (_retryDelay == 0) TryPresent();
        }
        private void TryPresent()
        {
            if (Instance != this || !ReignServerClient.NewsOptionsConfirmed || _state.Pending.Count == 0
                || ReignBetaSettings.Instance?.Enabled == false || ReignBetaSettings.Instance?.UseLocalServer == false
                || ReignDiplomacyAnnouncementScreenManager.IsOpen || !CanPresent()) return;
            ApplyOptions();
            if (_state.Pending.Count == 0) return;
            var news = _state.Pending[0];
            _activeId = news.Id;
            int epoch = _epoch;
            ReignDiplomacyAnnouncementScreenManager.Open(ToAnnouncement(news), () =>
            {
                if (Instance != this || epoch != _epoch) return;
                _state.Acknowledge(news.Id); _activeId = "";
                TryPresent();
            }, () =>
            {
                if (Instance != this || epoch != _epoch) return;
                _activeId = ""; _retryDelay = 1;
            });
        }
        private static bool CanPresent() => TaleWorlds.CampaignSystem.Campaign.Current != null
            && !TaleWorlds.CampaignSystem.Campaign.Current.TimeControlModeLock
            && !ReignCampaignInitializationGate.IsPending && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
            && Mission.Current == null && MobileParty.MainParty?.MapEvent == null
            && CharacterObject.OneToOneConversationCharacter == null && !InformationManager.IsAnyInquiryActive()
            && !ReignIndividualChatScreenManager.IsOpen && !ReignPartyChatScreenManager.IsOpen
            && !ReignSocialEventScreenManager.IsOpen && !ReignCorrespondenceScreenManager.IsOpen
            && ReignPlayerApproachPopup.CanPresentOnMap();

        private static ReignDiplomacyAnnouncement ToAnnouncement(ReignNewsArticle news) => new ReignDiplomacyAnnouncement
        {
            EventId = news.Id, IsNews = true, Title = news.Title, Outcome = news.Headline,
            GovernmentNews = news.GovernmentNews,
            Summary = news.Body, Terms = news.Consequences, WorldDay = (float)news.WorldDay, Accepted = news.Positive,
            ActorHeroStringId = news.ActorId, ActorName = news.ActorName, ActorKingdomStringId = news.ActorRealmId,
            ActorKingdomName = news.ActorRealmName, ActorPublicReason = news.ActorCaption,
            TargetHeroStringId = news.TargetId, TargetName = news.TargetName, TargetKingdomStringId = news.TargetRealmId,
            TargetKingdomName = news.TargetRealmName, TargetPublicReason = news.TargetCaption
        };
        internal JObject Diagnostics() => JObject.FromObject(new
        {
            schema = "reign-town-crier-v1", pending = _state.Pending.Take(64), pendingCount = _state.Pending.Count,
            pendingTruncated = _state.Pending.Count > 64, activeId = _activeId, retainedNoticeIds = _state.Seen.Count,
            optionsConfirmed = ReignServerClient.NewsOptionsConfirmed,
            options = ReignNewsCategories.Switches.Keys.ToDictionary(k => k, k => ReignServerClient.NewsOptions.Preference(k)),
            nativeTime = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode.ToString(),
            timeLocked = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlModeLock
        });
        private void OnCaptured(PartyBase captor, Hero prisoner)
        {
            if (prisoner == null) return;
            var movement = ReignRebellionCampaignBehavior.Instance?.FindActiveMovementForHero(prisoner);
            bool rebellionLeader = string.Equals(movement?.LeaderHeroStringId, prisoner.StringId, StringComparison.Ordinal);
            Capture(prisoner, captor, rebellionLeader);
        }
        internal void Capture(Hero prisoner, PartyBase captor, bool rebellionLeader)
        {
            if (prisoner == null || !prisoner.IsPrisoner || captor == null || !ReignNewsCategories.CaptureQualifies(prisoner.Clan?.Kingdom?.Leader == prisoner, rebellionLeader)
                || _state.CaptivityEpisodes.ContainsKey(prisoner.StringId)) return;
            string id = "crier:capture:" + Guid.NewGuid().ToString("N");
            _state.CaptivityEpisodes[prisoner.StringId] = id;
            string captorName = captor.LeaderHero?.Name?.ToString() ?? captor.MapFaction?.Name?.ToString() ?? "enemy forces";
            string role = rebellionLeader ? "the rebellion's leader" : "ruler of " + (prisoner.Clan?.Kingdom?.Name?.ToString() ?? "the realm");
            Publish(WithPeople(new ReignNewsArticle
            {
                Id = id, Category = ReignNewsCategories.Captures, Title = "A Leader Taken Captive", Headline = "News of a capture",
                Body = prisoner.Name + ", " + role + ", has been taken prisoner by " + captorName + ".",
                Consequences = "No sentence has yet been announced.", WorldDay = CampaignTime.Now.ToDays
            }, captor.LeaderHero ?? (captor.MapFaction as Kingdom)?.Leader, prisoner, "Capturing side", "Taken prisoner"));
        }
        private void OnReleased(Hero prisoner, PartyBase captor, IFaction faction, EndCaptivityDetail detail, bool notification)
        {
            if (prisoner == null || detail == EndCaptivityDetail.Death) return;
            _state.CaptivityEpisodes.Remove(prisoner.StringId);
        }
        private void OnKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool show)
        {
            if (victim == null || detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
            _state.CaptivityEpisodes.Remove(victim.StringId);
            Publish(WithPeople(new ReignNewsArticle
            {
                Id = "crier:execution:" + victim.StringId, Category = ReignNewsCategories.Executions,
                Title = "An Execution Announced", Headline = "The sentence is carried out",
                Body = killer == null ? victim.Name + " has been executed." : victim.Name + " has been executed on the orders of " + killer.Name + ".",
                WorldDay = CampaignTime.Now.ToDays
            }, killer, victim, "Ordered the execution", "Executed"));
        }
        private static ReignNewsArticle WithPeople(ReignNewsArticle news, Hero actor, Hero target, string actorCaption, string targetCaption)
        {
            news.ActorId = actor?.StringId ?? ""; news.ActorName = actor?.Name?.ToString() ?? "";
            news.ActorRealmId = actor?.Clan?.Kingdom?.StringId ?? ""; news.ActorRealmName = actor?.Clan?.Kingdom?.Name?.ToString() ?? "";
            news.TargetId = target?.StringId ?? ""; news.TargetName = target?.Name?.ToString() ?? "";
            news.TargetRealmId = target?.Clan?.Kingdom?.StringId ?? ""; news.TargetRealmName = target?.Clan?.Kingdom?.Name?.ToString() ?? "";
            news.ActorCaption = actorCaption; news.TargetCaption = targetCaption;
            return news;
        }
    }
}
