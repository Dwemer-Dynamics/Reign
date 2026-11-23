using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using ReignBeta.Court;
using ReignBeta.CastleChat;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Portraits.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignPartyChatScreenVM
    {
        private async Task TrackTargetedOpeningAsync(Task<ReignPartyChatBatchResult> opening)
        {
            ReignPartyChatBatchResult result = null;
            try { result = await opening.ConfigureAwait(false); }
            catch { }
            // Every targeted turn updates the tracker; the opening cannot overwrite a later result.
            if (ReferenceEquals(_activeBatchTask, opening)) _activeBatchTask = null;
        }

        private void RefreshCastleMembers()
        {
            PartyMembers.Clear();
            foreach (string id in ReignCourtCampaignBehavior.SplitIds(_castleSession?.OccupantHeroIdsCsv))
            {
                Hero hero = Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
                if (hero != null) PartyMembers.Add(new ReignPartyChatMemberVM(hero, true, _ => { }, PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait));
            }
        }

        private void RememberConversationHeroes(IEnumerable<Hero> heroes)
        {
            CapturePartyOrigin();
            foreach (Hero hero in heroes ?? Enumerable.Empty<Hero>())
            {
                if (!_banterReadOnly) ReignHeroKnowledge.MarkMet(hero);
                if (hero != null && !_conversationHeroes.Any(existing => existing?.StringId == hero.StringId))
                {
                    _conversationHeroes.Add(hero);
                }
            }
        }

        internal JObject HomesSnapshot() => new JObject {
            ["isHomesMode"] = IsHomesMode, ["isDirectory"] = IsHomeDirectory,
            ["cardHeroIds"] = new JArray(PartyMembers.Select(member => member.Hero.StringId)),
            ["selectedHeroIds"] = new JArray(GetSelectedHeroes().Select(hero => hero.StringId)),
            ["sessionHeroIds"] = new JArray(_conversationHeroes.Select(hero => hero.StringId)),
            ["sessionId"] = _conversationSessionId ?? ""
        };

        private async Task<string> EnsureConversationSessionAsync(IEnumerable<Hero> activeHeroes)
        {
            if (!string.IsNullOrWhiteSpace(_conversationSessionId))
            {
                return _conversationSessionId;
            }

            List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null)
                .Distinct()
                .ToList();
            RememberConversationHeroes(heroes);
            Task<string> pending = _conversationSessionTask
                ?? ReignServerClient.StartPartyChatConversationSessionAsync(_session, heroes);
            _conversationSessionTask = pending;
            _conversationSessionId = await pending ?? string.Empty;
            return _conversationSessionId;
        }

        private void RequestConversationFinish(string reason)
        {
            if (_conversationFinishRequested
                || (string.IsNullOrWhiteSpace(_conversationSessionId) && _conversationSessionTask == null))
            {
                return;
            }

            _conversationFinishRequested = true;
            // Detached same-load cleanup: owned by the load and player only, never by
            // the (possibly closed) screen. Captured here on the game thread.
            _ = FinishConversationAsync(reason, ReignServerClient.RequestFence.Capture(), Hero.MainHero);
        }

        private async Task FinishConversationAsync(string reason, ReignServerClient.RequestFence loadFence, Hero loadPlayer)
        {
            Task batch = _activeBatchTask;
            if (batch != null)
            {
                try
                {
                    await batch;
                }
                catch
                {
                    // The server finish remains useful after a failed response batch.
                }
            }

            // The session start's synchronous prefix reads native state; start it on the
            // game thread only while the original load is current.
            Task<string> sessionTask = await ReignMainThread.InvokeAsync(() =>
                loadFence.IsCurrent() && ReferenceEquals(loadPlayer, Hero.MainHero)
                    ? EnsureConversationSessionAsync(_conversationHeroes) : null);
            string sessionId = sessionTask == null ? null : await sessionTask;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }
            Func<bool> sameLoad = () => loadFence.IsCurrent() && ReferenceEquals(loadPlayer, Hero.MainHero);
            // The finish payload's native reads run in this guarded callback; its close work
            // applies only while the same load is current, even after the screen closed.
            Task<ReignConversationFinishResult> finish = await ReignMainThread.InvokeAsync(() => sameLoad()
                ? ReignServerClient.FinishPartyChatConversationSessionAsync(
                    _session,
                    _conversationHeroes,
                    reason ?? (_isFinalized ? "screen_finalized" : "closed_by_player"), sameLoad)
                : null);
            if (finish != null) await finish;
        }

        private static string FriendlyServerError(string error)
        {
            string text = (error ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "No response was generated.";
            }

            if (ContainsIgnoreCase(text, "context_length_exceeded") || ContainsIgnoreCase(text, "prompt exceeds") || ContainsIgnoreCase(text, "too much context"))
            {
                return "The AI request contained too much context. The full diagnostic was saved to the server audit log.";
            }

            if (ContainsIgnoreCase(text, "missing_api_key") || ContainsIgnoreCase(text, "Invalid Authentication")
                || ContainsIgnoreCase(text, "invalid_api_key") || ContainsIgnoreCase(text, "unauthorized")
                || ContainsIgnoreCase(text, "api key"))
            {
                return "API key missing or rejected. Open Server Diagnostics, save the key, then run Test LLM Key.";
            }

            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }
            return text.Length <= 220 ? text : text.Substring(0, 220).TrimEnd() + "...";
        }

        private static bool IsBatchFatalError(string error)
        {
            return ContainsIgnoreCase(error, "context_length_exceeded")
                || ContainsIgnoreCase(error, "prompt exceeds")
                || ContainsIgnoreCase(error, "too much context")
                || ContainsIgnoreCase(error, "missing_api_key")
                || ContainsIgnoreCase(error, "Invalid Authentication")
                || ContainsIgnoreCase(error, "invalid_api_key")
                || ContainsIgnoreCase(error, "unauthorized");
        }
    }
}
