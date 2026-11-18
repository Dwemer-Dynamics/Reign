using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignPartyChatScreenVM
    {
        private string _lifeApproachedKey = "";
        private string LifeChatContext => IsHomesMode ? "home:" + (_homesSettlement?.StringId ?? "")
            : _castleMode ? "castle:" + _castleRoomName : "party";
        private string LifeAudienceKey(IEnumerable<Hero> heroes) => LifeChatContext + "|"
            + string.Join("|", heroes.Select(h => h.StringId).OrderBy(id => id, StringComparer.Ordinal));
        private async Task<ReignPartyChatBatchResult> RequestLifeOpening(List<Hero> heroes)
        {
            var result = new ReignPartyChatBatchResult();
            if (_isFinalized || heroes.Count == 0) return result;
            // Called on the game thread where the approach is accepted. This screen, the
            // original load fence and the original player own every later capture,
            // dispatch, transcript, persistence and busy-state change of this opening.
            var owner = new PartyTurnOwner(ReignServerClient.RequestFence.Capture(), Hero.MainHero);
            Func<bool> current = () => IsPartyTurnCurrent(owner);
            IsBusy = true; BusyText = "Approaching...";
            try
            {
                string key = LifeAudienceKey(heroes);
                if (_lifeApproachedKey.Length > 0 && key != _lifeApproachedKey)
                {
                    _conversationSessionId = ""; _conversationSessionTask = null; _conversationHeroes.Clear();
                    _session.RestoreIdentity("party_chat_" + Guid.NewGuid().ToString("N"));
                    _session.RestoreTranscript(Array.Empty<ReignPartyChatTranscriptLine>());
                    ChatLines.Clear();
                    _partyOrigin = null; _continuedPartyChat = false;
                }
                _session.NpcLifeContext = LifeChatContext;
                string session = await EnsureConversationSessionAsync(heroes).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(session)) throw new InvalidOperationException("This conversation could not be opened.");
                string campaignId = "", timelineId = "main";
                var context = await ReignMainThread.InvokeAsync(() => {
                    if (!current()) throw new ReignRequestOwnerAbandonedException("The party approach owner closed before its capture.");
                    campaignId = ReignServerClient.GetCampaignId();
                    timelineId = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                    return new JObject {
                        ["npcLifeContext"] = LifeChatContext,
                        ["npcLifeSceneContext"] = _session.BuildSceneContext(heroes),
                        ["npcLifeAudienceIds"] = new JArray(heroes.Select(h => h.StringId).Concat(new[] { Hero.MainHero.StringId }))
                    };
                }).ConfigureAwait(false);
                using var lane = new SemaphoreSlim(3);
                var openings = await Task.WhenAll(heroes.Select(async hero => {
                    await lane.WaitAsync().ConfigureAwait(false);
                    try {
                        var individualContext = (JObject)context.DeepClone();
                        // Life preparation reads native state in its synchronous prefix, so it
                        // starts inside a guarded game-thread callback for the same owner.
                        Task preparation = await ReignMainThread.InvokeAsync(() => !current() ? null
                            : ReignBeta.Campaign.ReignNpcLivesBehavior.Instance?.PrepareLifeConversationAsync(hero, session, individualContext) ?? Task.CompletedTask).ConfigureAwait(false);
                        if (preparation == null) throw new ReignRequestOwnerAbandonedException("The party approach owner closed before life preparation.");
                        await preparation.ConfigureAwait(false);
                        return await ReignNpcLivesClient.OpenNpcLifeApproachAsync(hero, session, individualContext, requestOwner: current).ConfigureAwait(false);
                    }
                    finally { lane.Release(); }
                })).ConfigureAwait(false);
                var restored = new List<ReignPartyChatTranscriptLine>();
                foreach (string historyId in openings.Select(o => o.Value<string>("sessionId") ?? session).Where(id => id.Length > 0).Distinct())
                {
                    var history = await ReignServerClient.PostOwnedJsonAsync("/party-chat/history", new JObject {
                        ["campaignId"] = campaignId, ["conversationSessionId"] = historyId,
                        ["playerHeroStringId"] = context["npcLifeAudienceIds"].Last,
                        ["timelineId"] = timelineId, ["limit"] = 36
                    }, current).ConfigureAwait(false);
                    if (history.Value<bool?>("ok") == true)
                        restored.AddRange((history["lines"] as JArray ?? new JArray()).OfType<JObject>().Select(r => r.ToObject<ReignPartyChatTranscriptLine>()).Where(r => r != null));
                }
                await ReignMainThread.InvokeAsync(() => {
                    if (!current()) return;
                    if (restored.Count > 0)
                    {
                        var distinct = restored.GroupBy(r => r.SessionId + "|" + r.Sequence).Select(g => g.First()).ToList();
                        _session.RestoreTranscript(distinct); ChatLines.Clear();
                        foreach (var line in distinct) AddChatLine(line.Speaker, line.Text, line.Role);
                    }
                    foreach (var failure in openings.Where(o => o.Value<bool?>("ok") != true))
                        AddSystemLine(failure.Value<string>("error") ?? "This approach is still waiting for a response.", false);
                    _lifeApproachedKey = key; result.Ok = openings.All(o => o.Value<bool?>("ok") == true);
                    result.SessionId = session; PersistCastleTranscript();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                if (ex is ReignRequestOwnerAbandonedException) ReignLog.Info("Party approach opening abandoned: " + ex.Message);
                await ReignMainThread.InvokeAsync(() => { if (current()) AddSystemLine(ex.Message, false); }).ConfigureAwait(false);
            }
            finally { await ReignMainThread.InvokeAsync(() => { if (current()) { IsBusy = false; BusyText = ""; RefreshLabels(); } }).ConfigureAwait(false); }
            return result;
        }
    }
}
