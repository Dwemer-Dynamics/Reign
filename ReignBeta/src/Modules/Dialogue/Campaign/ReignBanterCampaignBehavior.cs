using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Shared.Dialogue;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed class ReignBanterCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignBanterCampaignBehavior Instance { get; private set; }
        private string _journal = "", _generation = "", _campaign = "", _timeline = "";
        private JObject _episode, _options = new JObject { ["enabled"] = true };
        private int _epoch, _shown;
        private bool _busy, _initialized, _takingOver, _moving, _positionObserved;
        private double _real, _nextDelivery, _nextPoll, _retryAfter, _endedDay = -1, _observedDay;
        private TaleWorlds.Library.Vec2 _position;
        private bool Current(NativeCampaign owner, int epoch) => Instance == this && ReferenceEquals(owner, NativeCampaign.Current) && epoch == _epoch
            && _campaign == ReignServerClient.GetCampaignId() && _timeline == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main");
        public override void RegisterEvents() { Instance = this; }
        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) _journal = JsonConvert.SerializeObject(new JObject { ["episode"] = _episode, ["endedDay"] = _endedDay, ["shown"] = _shown });
            store.SyncData("ReignPartyBanterV1", ref _journal);
            if (store.IsLoading)
            {
                _epoch++; _busy = _initialized = _takingOver = _positionObserved = false; _generation = "";
                try { var saved = JObject.Parse(string.IsNullOrWhiteSpace(_journal) ? "{}" : _journal); _episode = saved["episode"] as JObject; _endedDay = saved.Value<double?>("endedDay") ?? -1; _shown = saved.Value<int?>("shown") ?? 0; }
                catch { _episode = null; _endedDay = -1; _shown = 0; }
                ReignBanterPanel.Close();
            }
        }
        private static bool Ready() => NativeCampaign.Current != null && MobileParty.MainParty != null && Hero.MainHero?.IsAlive == true
            && !ReignCampaignInitializationGate.IsPending && Settings.ReignBetaSettings.Instance?.UseLocalServer != false;
        private static bool MapVisible() => ScreenManager.TopScreen?.GetType().Name.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) >= 0
            && ReignGovernmentScreenManager.CanPresentOnMap(ReignBanterPanel.InputLayer)
            && !ReignPartyChatScreenManager.IsOpenOrPending && !ReignIndividualChatScreenManager.IsOpen
            && !ReignSocialEventScreenManager.IsOpen && NativeCampaign.Current?.ConversationManager?.IsConversationInProgress != true;
        private bool Active => _episode != null && new[] { "generating", "ready", "delivering" }.Contains(_episode.Value<string>("state"));
        private JObject Capture(bool moving = false, bool roster = false) => ReignServerClient.CaptureBanterContext(_generation, _episode?.Value<string>("sessionId"), _episode?.Value<int>("revision") ?? 0, moving, roster,
            moving || _episode == null ? null : (_episode["heroIds"] as JArray ?? new JArray()).Values<string>().ToArray());
        public void ApplicationTick(float dt)
        {
            _real += Math.Max(0, dt);
            if (ReignBanterPanel.IsCalibrationActive) return;
            if (!Ready() || ReignLiveInteractionTestHost.OwnsAutomationSession) { ReignBanterPanel.Close(); return; }
            string campaign = ReignServerClient.GetCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            if (_campaign != campaign || _timeline != timeline) { _epoch++; _busy = _initialized = _positionObserved = false; _campaign = campaign; _timeline = timeline; _generation = ""; ReignBanterPanel.Close(); }
            double day = CampaignTime.Now.ToDays; var position = MobileParty.MainParty.Position.ToVec2();
            if (!_positionObserved) { _positionObserved = true; _observedDay = day; _position = position; _moving = false; }
            else if (day > _observedDay)
            {
                _moving = (position - _position).LengthSquared > .000001f && MobileParty.MainParty.CurrentSettlement == null && MobileParty.MainParty.MapEvent == null;
                _observedDay = day; _position = position;
            }
            else _moving = false;
            bool visible = MapVisible(); ReignBanterPanel.Update(_episode, visible, _takingOver, BeginTakeover);
            if (_busy || _real < _retryAfter || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return;
            if (!_initialized) { Request("context", Capture()); return; }
            if (Active && (MobileParty.MainParty.MapEvent != null || Hero.MainHero.IsPrisoner || _options.Value<bool?>("enabled") == false
                || (_episode["heroIds"] as JArray ?? new JArray()).Values<string>().Except(ReignPartyChatSession.GetMainPartyHeroes().Select(h => h.StringId)).Any()))
            { Finish("interrupted"); return; }
            if (_takingOver) { Request("handoff", Capture(roster: true)); return; }
            if (Active && visible)
            {
                if (_real >= _nextDelivery && (_episode.Value<int?>("draftCount") ?? 0) > (_episode.Value<int?>("accepted") ?? 0))
                { var payload = Capture(roster: true); payload["sequence"] = (_episode.Value<int?>("accepted") ?? 0) + 1; Request("accept", payload); return; }
                if (_episode.Value<bool?>("generationDone") == true && (_episode.Value<int?>("accepted") ?? 0) >= (_episode.Value<int?>("draftCount") ?? 0) && _real >= _nextDelivery)
                { Finish("complete"); return; }
            }
            if (_real >= _nextPoll) { _nextPoll = _real + 5; Request(Active ? "status" : "context", Capture()); return; }
            if (visible && PartyBanterPolicy.CanStart(_options.Value<bool?>("enabled") != false, _moving, Active, ReignPartyChatScreenManager.IsOpenOrPending,
                ReignPartyChatSession.GetMainPartyHeroes().Count, day, _endedDay, _real, _retryAfter))
            {
                var payload = Capture(true, true); payload["sessionId"] = "banter_" + Guid.NewGuid().ToString("N"); Request("start", payload);
            }
        }
        private void Finish(string reason) { var p = Capture(roster: true); p["reason"] = reason; Request("finish", p); }
        internal void CampaignEnded()
        {
            if (Active && _generation.Length > 0)
            {
                JObject payload = new JObject { ["campaignId"] = _campaign, ["timelineId"] = _timeline,
                    ["generation"] = _generation, ["sessionId"] = _episode.Value<string>("sessionId"),
                    ["worldDay"] = _observedDay, ["reason"] = "campaign_exit" };
                string generation = _generation; int epoch = _epoch;
                // Cleanup is scoped to the captured server generation. It cannot
                // publish into a newly restored campaign or apply native effects.
                _ = Cleanup();
                async Task Cleanup()
                {
                    try { await ReignServerClient.BanterRequestAsync("finish", payload,
                        () => Task.FromResult(Instance == this && _epoch == epoch && _generation == generation), false).ConfigureAwait(false); }
                    catch (Exception ex) { ReignLog.Warn("Party banter exit cleanup: " + ex.Message); }
                }
            }
            ReignBanterPanel.Close();
        }
        private void BeginTakeover() { if (_episode == null || _takingOver) return; _takingOver = true; if (!_busy) Request("handoff", Capture(roster: true)); }
        private void Request(string operation, JObject payload)
        {
            if (_busy) return; _busy = true;
            NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            _ = RunAsync();
            async Task RunAsync()
            {
                try
                {
                    JObject result = await ReignServerClient.BanterRequestAsync(operation, payload,
                        () => ReignMainThread.InvokeAsync(() => Current(owner, epoch))).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => {
                        if (!Current(owner, epoch)) return;
                        if (result?.Value<bool?>("ok") != true)
                        {
                            if (result?.Value<string>("errorCode") == "banter_stale_generation" || result?.Value<string>("errorCode") == "banter_missing_or_stale") _initialized = false;
                            if (result?["episode"] is JObject recovery) Receive(recovery);
                            _retryAfter = _real + 2; return;
                        }
                        if (operation == "context")
                        {
                            _generation = result.Value<string>("generation") ?? ""; _initialized = true;
                            _options = result["options"] as JObject ?? _options;
                            _endedDay = Math.Max(_endedDay, result.Value<double?>("endedDay") ?? -1);
                            if (result["episode"] is JObject restored) Receive(restored);
                            else { _episode = null; _shown = 0; }
                        }
                        else if (result.Value<string>("sessionId") != null) Receive(result);
                        if (operation == "handoff")
                        {
                            _takingOver = false;
                            ReignBanterPanel.Close(); ReignPartyChatScreenManager.OpenBanter(result);
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await ReignMainThread.InvokeAsync(() => { if (!Current(owner, epoch)) return; _retryAfter = _real + 60; ReignLog.Warn("Party banter: " + ex.Message); }).ConfigureAwait(false);
                }
                finally { await ReignMainThread.InvokeAsync(() => { if (Current(owner, epoch)) _busy = false; }).ConfigureAwait(false); }
            }
        }
        private void Receive(JObject episode)
        {
            _retryAfter = PartyBanterPolicy.FailureRetryAfter(_episode?.Value<string>("sessionId"), _episode?.Value<string>("state"),
                episode.Value<string>("sessionId"), episode.Value<string>("state"), _real, _retryAfter);
            if (_episode?.Value<string>("sessionId") != episode.Value<string>("sessionId")) _shown = 0;
            _episode = episode;
            int accepted = episode.Value<int?>("accepted") ?? 0;
            if (accepted > _shown)
            {
                _shown = accepted;
                string text = (episode["transcript"] as JArray)?.OfType<JObject>().LastOrDefault()?.Value<string>("text") ?? "";
                _nextDelivery = _real + PartyBanterPolicy.ReadingSeconds(text);
            }
            if (!Active)
            {
                _endedDay = Math.Max(_endedDay, episode.Value<double?>("endedDay") ?? -1);
            }
            _nextPoll = Math.Min(_nextPoll, _real + (Active ? .5 : 5));
        }
    }
}
