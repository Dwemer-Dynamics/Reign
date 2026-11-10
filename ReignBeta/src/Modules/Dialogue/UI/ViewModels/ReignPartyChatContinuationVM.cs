using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignPartyChatScreenVM
    {
        private readonly ReignPartyChatHistoryBehavior _partyHistory = ReignPartyChatHistoryBehavior.Instance;
        private readonly int _partyHistoryEpoch = ReignPartyChatHistoryBehavior.Instance?.Epoch ?? -1;
        private ReignPartyChatContinuation _partyOrigin;
        private bool _restoringPartyChat;
        private bool _continuedPartyChat;
        private float _continuationRefresh;
        [DataSourceProperty] public bool ShowPartyContinuation => !_castleMode && !IsHomesMode && !_targetedReviewMode
            && !_banterReadOnly && string.IsNullOrWhiteSpace(_session.ApproachRequestId) && !_banterHandoff;
        private bool _banterHandoff;
        [DataSourceProperty] public bool CanContinuePartyChat => ShowPartyContinuation && !_isFinalized && !IsBusy && InputEnabled
            && string.IsNullOrWhiteSpace(InputText) && ActionDraft == null && !_continuedPartyChat && !ChatLines.Any(l => l.IsPlayerLine || l.IsNpcLine)
            && _partyHistory?.Owns(_partyHistoryEpoch) == true && PartyContinuationReason(_partyHistory.Last).Length == 0;
        [DataSourceProperty] public bool PartyContinuationUnavailable => !CanContinuePartyChat;
        private void RefreshContinueAvailability()
        {
            OnPropertyChanged(nameof(CanContinuePartyChat));
            OnPropertyChanged(nameof(PartyContinuationUnavailable));
        }

        private ReignPartyChatContinuation CurrentPartyPlace()
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            Vec2 position = MobileParty.MainParty?.Position.ToVec2() ?? Vec2.Zero;
            return new ReignPartyChatContinuation {
                CampaignId = ReignServerClient.GetCampaignId(), PlayerId = Hero.MainHero?.StringId ?? "",
                LocationId = settlement?.StringId ?? "overland", RoomId = CampaignMission.Current?.Location?.StringId ?? "",
                MapX = position.X, MapY = position.Y
            };
        }

        private string PartyContinuationReason(ReignPartyChatContinuation snapshot)
        {
            if (snapshot == null) return "There is no saved party chat to continue.";
            if (Hero.MainHero?.IsAlive != true || Hero.MainHero.IsPrisoner || MobileParty.MainParty == null)
                return "Your party is unavailable.";
            var place = CurrentPartyPlace();
            return snapshot.UnavailableReason(place.CampaignId, place.PlayerId, place.LocationId, place.RoomId,
                place.MapX, place.MapY, ReignPartyChatSession.GetAvailableConversationHeroesQuietly().Select(h => h.StringId));
        }

        internal void RefreshPartyContinuation(float dt)
        {
            _continuationRefresh += dt;
            if (_continuationRefresh < .5f) return;
            _continuationRefresh = 0;
            RefreshContinueAvailability();
        }

        private bool CheckContinuedPartyPresence()
        {
            if (!_continuedPartyChat) return true;
            string reason = PartyContinuationReason(_partyOrigin);
            if (_partyHistory?.Owns(_partyHistoryEpoch) != true) reason = "This conversation belongs to a different loaded save.";
            if (reason.Length == 0) return true;
            AddSystemLine(reason, false);
            return false;
        }

        private void CapturePartyOrigin()
        {
            if (ShowPartyContinuation && _partyOrigin == null) _partyOrigin = CurrentPartyPlace();
        }

        internal void PersistPartyChat()
        {
            if (_restoringPartyChat || !ShowPartyContinuation || _partyHistory?.Owns(_partyHistoryEpoch) != true
                || _partyOrigin == null || !_session.RecentTranscriptEntries.Any(l => l.Role == "player" || l.Role == "npc")) return;
            var participants = _conversationHeroes.Where(h => h != null).Select(h => h.StringId)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (participants.Count == 0) return;
            // Preserve the original place; closing after moving must never bless a new location.
            var saved = new ReignPartyChatContinuation {
                CampaignId = _partyOrigin.CampaignId, PlayerId = _partyOrigin.PlayerId,
                LocationId = _partyOrigin.LocationId, RoomId = _partyOrigin.RoomId, MapX = _partyOrigin.MapX, MapY = _partyOrigin.MapY,
                SourceSessionId = _session.SessionId, PlayerInputCount = _session.PlayerInputCount,
                TranscriptSequence = _session.TranscriptSequence, ParticipantIds = participants,
                SelectedIds = GetSelectedHeroes().Select(h => h.StringId).Where(participants.Contains).ToList(),
                DisplayLines = ChatLines.Select(l => new ReignPartyChatSavedLine {
                    Speaker = l.Speaker, Text = l.OriginalText, Role = l.Role, ExchangeId = l.ExchangeId,
                    InlineImagePath = l.InlineScenePath, FullImagePath = l.FullScenePath
                }).ToList(),
                ContextLines = _session.RecentTranscriptEntries.Select(l => new ReignPartyChatSavedLine {
                    Sequence = l.Sequence, SessionId = l.SessionId, ExchangeId = l.ExchangeId, SpeakerHeroStringId = l.SpeakerHeroStringId,
                    Speaker = l.Speaker, Text = l.Text, Role = l.Role, AudienceHeroStringId = l.AudienceHeroStringId
                }).ToList()
            };
            _partyHistory.Store(saved, _partyHistoryEpoch);
            RefreshContinueAvailability();
        }

        public void ExecuteContinuePartyChat()
        {
            if (!CanContinuePartyChat) return; // Re-evaluates live presence at the actual click.
            var saved = _partyHistory.Last;
            _restoringPartyChat = true;
            try
            {
                RefreshPartyMembers();
                var selected = new HashSet<string>(saved.SelectedIds.Count > 0 ? saved.SelectedIds : saved.ParticipantIds,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var member in PartyMembers) member.IsActive = selected.Contains(member.Hero.StringId);
                _conversationHeroes.Clear();
                foreach (string id in saved.ParticipantIds)
                    _conversationHeroes.Add(PartyMembers.First(m => string.Equals(m.Hero.StringId, id, StringComparison.OrdinalIgnoreCase)).Hero);
                // The old transport is finalized. A new ID isolates retries, routes,
                // Save Sync forks and old in-flight completions; no history is replayed.
                _session.RestoreIdentity("party_chat_" + Guid.NewGuid().ToString("N"));
                _session.RestoreTranscript(saved.ContextLines.Select(l => new ReignPartyChatTranscriptLine {
                    Sequence = l.Sequence, SessionId = l.SessionId, ExchangeId = l.ExchangeId,
                    SpeakerHeroStringId = l.SpeakerHeroStringId, Speaker = l.Speaker, Text = l.Text, Role = l.Role,
                    AudienceHeroStringId = l.AudienceHeroStringId
                }));
                _session.RestoreTurnState(saved.PlayerInputCount, saved.TranscriptSequence);
                _conversationSessionId = ""; _conversationSessionTask = null; _conversationFinishRequested = false;
                _view?.Close(); _view = null;
                ChatLines.Clear();
                foreach (var line in saved.DisplayLines.Where(l => l != null))
                {
                    AddChatLine(line.Speaker, line.Text, line.Role, line.ExchangeId);
                    if (line.Role == "view") View.RestoreSavedScene(ChatLines.Last(), line.InlineImagePath, line.FullImagePath);
                }
                _partyOrigin = saved; _continuedPartyChat = true;
                _lifeApproachedKey = LifeAudienceKey(GetSelectedHeroes());
                RefreshLabels(); ChatScrollVersion++;
                OnPropertyChanged(nameof(View)); RefreshContinueAvailability();
            }
            finally { _restoringPartyChat = false; }
        }
    }
}
