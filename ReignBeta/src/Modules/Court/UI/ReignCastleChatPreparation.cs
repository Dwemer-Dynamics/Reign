using System;
using System.Threading.Tasks;
using System.Linq;
using ReignBeta.Court;
using ReignBeta.UI.ViewModels;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.UI
{
    public static class ReignCastleChatPreparation
    {
        public static async void Begin(CastleRoomSessionRecord session, Action closeLayout, bool regenerateImage = false)
        {
            if (session == null) return;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            // The persisted session is created before any asynchronous image or dialogue work.
            // Closing the layout cannot reshuffle it; preparation can be resumed idempotently.
            closeLayout?.Invoke();
            string room = ReignCastleLayoutScreenVM.RoomName((ReignBeta.CastleChat.CastleRoom)session.Room);
            Settlement settlement = string.IsNullOrWhiteSpace(session.SettlementStringId)
                ? Settlement.CurrentSettlement
                : Settlement.Find(session.SettlementStringId);
            Kingdom settlementKingdom = settlement?.OwnerClan?.Kingdom;
            bool playerRulesSettlement = settlement?.OwnerClan == Clan.PlayerClan
                || (settlementKingdom != null
                    && settlementKingdom == Clan.PlayerClan?.Kingdom
                    && settlementKingdom.Leader == Hero.MainHero);
            string ownershipMode = playerRulesSettlement ? "player_ruled" : "foreign";
            const string foreignMarker = "[KEEP OWNERSHIP MODE: FOREIGN]";
            bool promptOwnershipIsStale = playerRulesSettlement
                ? (session.ImagePromptSnapshot ?? string.Empty).Contains(foreignMarker)
                    || (session.DialoguePromptSnapshot ?? string.Empty).Contains(foreignMarker)
                : !(session.ImagePromptSnapshot ?? string.Empty).Contains(foreignMarker)
                    || !(session.DialoguePromptSnapshot ?? string.Empty).Contains(foreignMarker);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Heading to " + room + "…"));
            if (string.IsNullOrWhiteSpace(session.ImagePromptSnapshot)
                || string.IsNullOrWhiteSpace(session.DialoguePromptSnapshot)
                || promptOwnershipIsStale || regenerateImage)
            {
                ReignCourtServerResponse prompts = await ReignCourtServerClient.ResolveCastleChatPromptsAsync(
                    session.CultureId, ReignCourtCampaignBehavior.CastleRoomPromptKey(
                        (ReignBeta.CastleChat.CastleRoom)session.Room),
                    ownershipMode,
                    settlement?.Name?.ToString() ?? "this settlement",
                    settlement?.OwnerClan?.Leader?.Name?.ToString() ?? "the settlement's ruling clan",
                    settlementKingdom?.Leader?.Name?.ToString()
                        ?? settlement?.OwnerClan?.Leader?.Name?.ToString()
                        ?? "the realm's ruler",
                    Hero.MainHero?.Name?.ToString() ?? "the visiting player").ConfigureAwait(false);
                if (prompts.Ok)
                {
                    session.ImagePromptSnapshot = prompts.Raw.Value<string>("imagePrompt") ?? string.Empty;
                    if (!regenerateImage || string.IsNullOrWhiteSpace(session.DialoguePromptSnapshot) || promptOwnershipIsStale)
                        session.DialoguePromptSnapshot = prompts.Raw.Value<string>("dialoguePrompt") ?? string.Empty;
                    session.PromptRevision = prompts.Raw.Value<string>("revision") ?? string.Empty;
                    if (regenerateImage)
                    {
                        // Explicit regeneration refreshes the image prompt and
                        // bypasses the image cache, never the conversation history.
                        session.PromptRevision += "|regenerate:" + Guid.NewGuid().ToString("N");
                        session.ImageStatus = "pending";
                    }
                    session.Revision++;
                }
                else
                {
                    session.ImageStatus = "failed";
                    session.OpeningStatus = "prompt_unavailable";
                    ReignLog.Warn("Castle Chat prompts could not be resolved for culture="
                        + session.CultureId + " room=" + room + ": " + prompts.Error);
                }
            }
            try
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (campaign == null || !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current))
                        return Task.CompletedTask;
                    var occupants = ReignCourtCampaignBehavior.SplitIds(session.OccupantHeroIdsCsv)
                        .Select(id => Hero.FindFirst(x => x.StringId == id)).Where(x => x != null).ToList();
                    return ReignScenePreparationCoordinator.RunAsync(session,
                        () => { ReignPartyChatScreenManager.OpenCastle(session); return Task.CompletedTask; },
                        () => string.Equals(session.OpeningStatus, "prompt_unavailable", StringComparison.OrdinalIgnoreCase)
                            ? Task.CompletedTask : ReignCastleSceneClient.PrepareAsync(session, occupants));
                }).Unwrap().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Castle scene generation failed; using crest fallback: " + ex.Message);
            }
        }
    }
}
