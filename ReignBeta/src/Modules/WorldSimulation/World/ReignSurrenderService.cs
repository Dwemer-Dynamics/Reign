using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Campaign;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.World
{
    internal static class ReignSurrenderService
    {
        private sealed class PendingSurrender
        {
            internal string ActionId;
            internal NativeCampaign Campaign;
            internal Hero Actor;
            internal MobileParty Party;
            internal MapEvent Battle;
            internal PlayerEncounter Encounter;
            internal readonly ReignSurrenderHandoff Handoff = new ReignSurrenderHandoff();
        }

        private static readonly Dictionary<string, PendingSurrender> Pending =
            new Dictionary<string, PendingSurrender>(StringComparer.Ordinal);

        internal static ReignActionResult Queue(ReignWorldActionRecord action, Hero actor, MobileParty party)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
                return ReignActionResult.ValidationFailed("Surrender requires an identified action.");
            if (Pending.ContainsKey(action.ActionId)) return Waiting(actor);
            MapEvent battle = party?.MapEvent;
            PartyBase player = PartyBase.MainParty;
            if (actor == null || battle == null || !battle.IsPlayerMapEvent
                || player?.MapEvent != battle || party.Party.MapEventSide == null
                || party.Party.Side != player.OpponentSide)
                return ReignActionResult.ValidationFailed("Surrender requires the opposing party in your active encounter.");
            if (Pending.Values.Any(p => ReferenceEquals(p.Campaign, NativeCampaign.Current)
                && ReferenceEquals(p.Battle, battle)))
                return ReignActionResult.ValidationFailed("This encounter already has a surrender awaiting capture.");
            if (PlayerEncounter.Current != null && PlayerEncounter.Battle != null
                && PlayerEncounter.Battle != battle)
                return ReignActionResult.ValidationFailed("A different native encounter is active.");

            // A native StartBattle action can create a MapEvent before its
            // PlayerEncounter has been initialized. Bind the exact battle first.
            if (PlayerEncounter.Battle != battle) PlayerEncounter.Init();
            if (PlayerEncounter.Current == null || PlayerEncounter.Battle != battle)
                return ReignActionResult.ValidationFailed("The native surrender encounter could not be initialized.");
            BattleState expected = party.Party.Side == BattleSideEnum.Defender
                ? BattleState.AttackerVictory : BattleState.DefenderVictory;
            PlayerEncounter.EnemySurrender = true;
            if (battle.BattleState != expected)
                return ReignActionResult.FailTerminal("Native surrender did not establish your victory.",
                    "surrender_verification_failed", "state_verification");

            Pending.Add(action.ActionId, new PendingSurrender { ActionId = action.ActionId,
                Campaign = NativeCampaign.Current, Actor = actor, Party = party, Battle = battle,
                Encounter = PlayerEncounter.Current });
            return Waiting(actor);
        }

        private static ReignActionResult Waiting(Hero actor) =>
            ReignActionResult.Progress(actor.Name + " surrendered; the native capture is awaiting completion.")
                .WithResultCode("encounter_surrender_pending_capture");

        internal static void Tick()
        {
            foreach (PendingSurrender pending in Pending.Values.ToArray())
            {
                if (!ReferenceEquals(pending.Campaign, NativeCampaign.Current))
                {
                    Pending.Remove(pending.ActionId);
                    continue;
                }
                try
                {
                    bool captive = pending.Actor.IsPrisoner
                        && pending.Actor.PartyBelongedToAsPrisoner == PartyBase.MainParty
                        && PartyBase.MainParty.PrisonRoster.GetTroopRoster().Any(t =>
                            t.Character == pending.Actor.CharacterObject && t.Number == 1);
                    bool same = ReferenceEquals(PlayerEncounter.Current, pending.Encounter)
                        && ReferenceEquals(PartyBase.MainParty?.MapEvent, pending.Battle);
                    var state = pending.Handoff.Advance(same, InteractionOpen(), captive,
                        () => PlayerEncounter.Update());
                    if (state == ReignSurrenderHandoffState.Captured)
                        Complete(pending, ReignActionResult.Done(pending.Actor.Name + " surrendered and is now your prisoner.")
                            .WithResultCode("encounter_surrender_completed")
                            .WithEffect("party_surrendered", "party", pending.Party.StringId,
                                pending.Party.Name.ToString(), "captorPartyId=" + MobileParty.MainParty.StringId)
                            .WithEffect("hero_captured", "hero", pending.Actor.StringId,
                                pending.Actor.Name.ToString(), "captorPartyId=" + MobileParty.MainParty.StringId)
                            .WithChangedEntity("hero", pending.Actor.StringId,
                                pending.Actor.Name.ToString(), "captured"));
                    else if (state == ReignSurrenderHandoffState.EncounterLost)
                        Complete(pending, ReignActionResult.FailTerminal(
                            "The surrender encounter ended without the agreed captive in your party.",
                            "surrender_capture_not_completed", "state_verification"));
                }
                catch (Exception ex)
                {
                    Complete(pending, ReignActionResult.FailTerminal(
                        "The native surrender capture could not complete: " + ex.Message,
                        "surrender_capture_failed", "native_execution"));
                }
            }
        }

        private static bool InteractionOpen()
        {
            if (Mission.Current != null || ReignIndividualChatScreenManager.IsOpen
                || ReignPartyChatScreenManager.IsOpen || ReignSocialEventScreenManager.IsOpen
                || CharacterObject.OneToOneConversationCharacter != null
                || NativeCampaign.Current?.ConversationManager?.IsConversationInProgress == true)
                return true;
            return (ScreenManager.TopScreen?.GetType().Name ?? string.Empty)
                .IndexOf("Map", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static void Complete(PendingSurrender pending, ReignActionResult result)
        {
            Pending.Remove(pending.ActionId);
            ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(pending.ActionId, result);
        }
    }
}
