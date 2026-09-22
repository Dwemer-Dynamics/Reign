using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Integration;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.Campaign
{
    internal static class ReignDuelLaunchPromptService
    {
        private static bool _promptOpen;
        private static string _promptActionId;
        private static readonly Dictionary<string, string> TemporaryArenaSettlementContexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PendingArenaSettlementContextCleanup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Tick()
        {
            ProcessPendingArenaSettlementContextCleanup();

            if (_promptOpen || !ReignDuelService.HasPendingDuel)
            {
                return;
            }

            if (!CanPromptOnCampaignMap())
            {
                return;
            }

            if (!ReignDuelService.TryGetFirstPendingDuel(out ReignDuelIntent intent))
            {
                return;
            }

            ShowStartInquiry(intent);
        }

        private static bool CanPromptOnCampaignMap()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null)
            {
                return false;
            }

            if (ReignIndividualChatScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpen || ReignSocialEventScreenManager.IsOpen)
            {
                return false;
            }

            if (Mission.Current != null)
            {
                return false;
            }

            if (CharacterObject.OneToOneConversationCharacter != null)
            {
                return false;
            }

            try
            {
                TaleWorlds.CampaignSystem.Conversation.ConversationManager manager = TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager;
                if (manager != null && manager.IsConversationInProgress)
                {
                    return false;
                }
            }
            catch
            {
            }

            ScreenBase top = ScreenManager.TopScreen;
            string topName = top?.GetType().Name ?? string.Empty;
            return topName.IndexOf("Map", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ShowStartInquiry(ReignDuelIntent intent)
        {
            _promptOpen = true;
            _promptActionId = intent.ActionId;

            string mode = intent.Lethal ? "duel of honor" : "training duel";
            string text = intent.OpponentName + " has accepted a " + mode + ". Start the duel now?";
            ReignLog.Info("Showing duel launch prompt action=" + intent.ActionId + " opponent=" + intent.OpponentHeroStringId + " mode=" + intent.Mode);

            InformationManager.ShowInquiry(new InquiryData(
                "Bannerlord Reign Duel",
                text,
                true,
                true,
                "Start Duel",
                "Cancel",
                () => StartPromptedDuel(intent.ActionId),
                () => CancelPromptedDuel(intent.ActionId),
                string.Empty,
                0f,
                null,
                null), true);
        }

        private static void StartPromptedDuel(string actionId)
        {
            _promptOpen = false;
            _promptActionId = string.Empty;

            if (!ReignDuelService.TryGetPendingDuel(actionId, out ReignDuelIntent intent))
            {
                ReignLog.Warn("Duel prompt start ignored because pending action was missing: " + actionId);
                return;
            }

            if (!TryLaunchNativeArenaDuel(intent, out string error))
            {
                ReignDuelService.FailPendingDuel(intent, "Could not start the duel: " + error, "duel_native_arena_launch_failed");
            }
        }

        private static void CancelPromptedDuel(string actionId)
        {
            _promptOpen = false;
            _promptActionId = string.Empty;
            ReignDuelService.CancelPendingDuel(actionId);
        }

        private static bool TryLaunchNativeArenaDuel(ReignDuelIntent intent, out string error)
        {
            error = string.Empty;
            Hero opponent = intent.OpponentHero;
            if (opponent?.CharacterObject == null)
            {
                error = "the opponent hero was no longer available";
                return false;
            }

            Settlement arenaSettlement = FindArenaSettlement();
            Location arena = arenaSettlement?.LocationComplex?.GetLocationWithId("arena");
            if (arena == null)
            {
                error = "no usable arena location could be found";
                return false;
            }

            try
            {
                bool temporarySettlementContext = EnsureArenaSettlementContext(intent, arenaSettlement, out string contextError);
                if (!string.IsNullOrWhiteSpace(contextError))
                {
                    error = contextError;
                    return false;
                }

                int wallLevel = arenaSettlement.Town?.GetWallLevel() ?? 1;
                string scene = arena.GetSceneName(wallLevel);
                if (string.IsNullOrWhiteSpace(scene))
                {
                    error = "the arena scene was empty for " + arenaSettlement.Name;
                    CleanupTemporaryArenaSettlementContext(intent.ActionId);
                    return false;
                }

                ReignDuelService.MarkActive(intent.ActionId);
                float health = Math.Max(intent.PlayerHealth, intent.OpponentHealth);
                CampaignMission.OpenArenaDuelMission(
                    scene,
                    arena,
                    opponent.CharacterObject,
                    false,
                    false,
                    character => OnNativeArenaDuelEnded(intent, character),
                    health);

                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] The duel begins in the arena at " + arenaSettlement.Name + ".", Color.FromUint(0xFFFFD36A)));
                ReignLog.Info("Native arena duel launched action=" + intent.ActionId + " opponent=" + opponent.StringId + " settlement=" + arenaSettlement.StringId + " scene=" + scene + " temporarySettlementContext=" + temporarySettlementContext);
                return true;
            }
            catch (Exception ex)
            {
                ReignDuelService.MarkInactive(intent.ActionId);
                CleanupTemporaryArenaSettlementContext(intent.ActionId);
                error = ex.Message;
                ReignLog.Warn("Native arena duel launch failed action=" + intent.ActionId + ": " + ex);
                return false;
            }
        }

        private static void OnNativeArenaDuelEnded(ReignDuelIntent intent, CharacterObject callbackCharacter)
        {
            try
            {
                Hero callbackHero = callbackCharacter?.HeroObject;
                ReignLog.Info("Native arena duel callback action=" + intent.ActionId + " callbackHero=" + (callbackHero?.StringId ?? "null"));
                ReignDuelService.CompleteDuelFromMapMission(intent, callbackHero, "native_arena_duel_callback");
                ScheduleTemporaryArenaSettlementContextCleanup(intent.ActionId);
            }
            catch (Exception ex)
            {
                ReignDuelService.MarkInactive(intent?.ActionId);
                ScheduleTemporaryArenaSettlementContextCleanup(intent?.ActionId);
                ReignLog.Warn("Native arena duel callback failed: " + ex);
            }
        }

        private static bool EnsureArenaSettlementContext(ReignDuelIntent intent, Settlement arenaSettlement, out string error)
        {
            error = string.Empty;
            if (Settlement.CurrentSettlement != null)
            {
                return false;
            }

            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty == null)
            {
                error = "main party was unavailable while preparing the arena settlement context";
                return false;
            }

            if (arenaSettlement == null)
            {
                error = "arena settlement was unavailable while preparing the duel";
                return false;
            }

            try
            {
                EncounterManager.StartSettlementEncounter(mainParty, arenaSettlement);
                if (Settlement.CurrentSettlement == null || PlayerEncounter.LocationEncounter == null)
                {
                    error = "nearest arena town could not become a full settlement encounter";
                    return false;
                }

                TemporaryArenaSettlementContexts[intent.ActionId] = arenaSettlement.StringId;
                ReignLog.Info("Duel arena settlement encounter set action=" + intent.ActionId + " settlement=" + arenaSettlement.StringId);
                return true;
            }
            catch (Exception ex)
            {
                error = "nearest arena town encounter failed: " + ex.Message;
                ReignLog.Warn("Duel arena settlement encounter failed action=" + intent.ActionId + " settlement=" + arenaSettlement.StringId + ": " + ex);
                return false;
            }
        }

        private static void ScheduleTemporaryArenaSettlementContextCleanup(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            if (TemporaryArenaSettlementContexts.TryGetValue(actionId, out string settlementId))
            {
                PendingArenaSettlementContextCleanup[actionId] = settlementId;
                ReignLog.Info("Duel arena settlement encounter cleanup scheduled action=" + actionId + " settlement=" + settlementId);
            }
        }

        private static void ProcessPendingArenaSettlementContextCleanup()
        {
            if (PendingArenaSettlementContextCleanup.Count == 0 || Mission.Current != null)
            {
                return;
            }

            foreach (string actionId in PendingArenaSettlementContextCleanup.Keys.ToList())
            {
                CleanupTemporaryArenaSettlementContext(actionId);
            }
        }

        private static void CleanupTemporaryArenaSettlementContext(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) || !TemporaryArenaSettlementContexts.TryGetValue(actionId, out string settlementId))
            {
                return;
            }

            try
            {
                Settlement activeSettlement = PlayerEncounter.LocationEncounter?.Settlement ?? MobileParty.MainParty?.CurrentSettlement;
                if (activeSettlement != null
                    && !string.Equals(activeSettlement.StringId, settlementId, StringComparison.OrdinalIgnoreCase))
                {
                    ReignLog.Warn("Duel arena settlement encounter cleanup skipped action=" + actionId + " expected=" + settlementId + " active=" + activeSettlement.StringId);
                    TemporaryArenaSettlementContexts.Remove(actionId);
                    PendingArenaSettlementContextCleanup.Remove(actionId);
                    return;
                }

                MobileParty mainParty = MobileParty.MainParty;
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.Finish();
                    ReignLog.Info("Duel arena settlement encounter finished action=" + actionId + " settlement=" + settlementId);
                }
                else
                {
                    if (mainParty?.CurrentSettlement != null)
                    {
                        LeaveSettlementAction.ApplyForParty(mainParty);
                    }

                    PlayerEncounter.LocationEncounter = null;
                    ReignLog.Info("Duel arena settlement context cleared action=" + actionId + " settlement=" + settlementId);
                }

                TemporaryArenaSettlementContexts.Remove(actionId);
                PendingArenaSettlementContextCleanup.Remove(actionId);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Duel arena settlement context cleanup failed action=" + actionId + ": " + ex);
            }
        }

        private static Settlement FindArenaSettlement()
        {
            Settlement current = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? Hero.MainHero?.CurrentSettlement;

            if (HasArena(current))
            {
                return current;
            }

            Vec2 playerPosition = MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero;
            return Settlement.All
                .Where(HasArena)
                .OrderBy(x => DistanceSquared(playerPosition, x.GetPosition2D))
                .FirstOrDefault();
        }

        private static bool HasArena(Settlement settlement)
        {
            return settlement != null
                && settlement.IsTown
                && settlement.LocationComplex?.GetLocationWithId("arena") != null;
        }

        private static float DistanceSquared(Vec2 a, Vec2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return dx * dx + dy * dy;
        }
    }
}
