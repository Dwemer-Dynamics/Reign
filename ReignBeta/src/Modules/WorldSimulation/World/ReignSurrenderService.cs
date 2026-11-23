using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Campaign;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using ReignBeta.Dialogue.Runtime;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;
using ReignBeta.Dialogue.UI;
using ReignBeta.Platform.Campaign;
using ReignBeta.World;
using ReignBeta.WorldSimulation.UI;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.World
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

        internal static bool Validate(ReignWorldActionRecord action, Hero actor, MobileParty party,
            bool proposal, out string reason)
        {
            reason = string.Empty;
            Hero player = Hero.MainHero;
            if (!ReignConversationEligibility.IsAdultLivingNpc(actor) || actor.IsPrisoner
                || player == null || player.IsPrisoner || PartyBase.MainParty == null)
            {
                reason = "Surrender requires a free, living adult NPC and an available player party.";
                return false;
            }
            if (IsEncounterSurrender(actor, party)) return true;
            if (!CanSurrenderPersonally(actor, party, player))
            {
                reason = "The surrendering hero must be present with you or lead the opposing party in your active encounter.";
                return false;
            }
            if (!proposal && (action.RequiresAcceptance
                || !string.Equals(action.AcceptedByHeroStringId, actor.StringId, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Personal surrender requires the surrendering hero's acceptance.";
                return false;
            }
            return true;
        }

        private static bool IsEncounterSurrender(Hero actor, MobileParty party)
        {
            PartyBase player = PartyBase.MainParty;
            return party?.IsActive == true && party.LeaderHero == actor && !party.IsMainParty
                && party.MapEvent?.IsPlayerMapEvent == true && player?.MapEvent == party.MapEvent
                && party.Party.MapEventSide != null && party.Party.Side == player.OpponentSide;
        }

        private static bool CanSurrenderPersonally(Hero actor, MobileParty party, Hero player)
            => PartyBase.MainParty.MapEvent == null && party?.MapEvent == null && party?.BesiegedSettlement == null
                && ((actor.CurrentSettlement != null && actor.CurrentSettlement == player.CurrentSettlement)
                    || (party != null && party == MobileParty.MainParty));

        private static ReignActionResult CapturePersonally(Hero actor)
        {
            PartyBase player = PartyBase.MainParty;
            TakePrisonerAction.Apply(player, actor);
            if (!IsInPlayerCustody(actor))
                return ReignActionResult.FailTerminal("Personal surrender did not place the hero in your custody.",
                    "surrender_verification_failed", "state_verification");

            return ReignActionResult.Done(actor.Name + " surrendered and is now your prisoner.")
                .WithResultCode("personal_surrender_completed")
                .WithEffect("hero_captured", "hero", actor.StringId, actor.Name.ToString(),
                    "captorPartyId=" + MobileParty.MainParty.StringId)
                .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "captured");
        }

        private static bool IsInPlayerCustody(Hero actor)
        {
            PartyBase player = PartyBase.MainParty;
            return actor.IsPrisoner && player != null && actor.PartyBelongedToAsPrisoner == player
                && player.PrisonRoster.GetTroopRoster().Any(t => t.Character == actor.CharacterObject && t.Number == 1);
        }

        internal static ReignActionResult Queue(ReignWorldActionRecord action, Hero actor, MobileParty party)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
                return ReignActionResult.ValidationFailed("Surrender requires an identified action.");
            if (Pending.ContainsKey(action.ActionId)) return Waiting(actor);
            if (!Validate(action, actor, party, false, out string reason))
                return ReignActionResult.ValidationFailed(reason);
            if (!IsEncounterSurrender(actor, party)) return CapturePersonally(actor);

            MapEvent battle = party.MapEvent;
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
                    bool captive = IsInPlayerCustody(pending.Actor);
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
            ReignServices.AI?.CompleteActionFromMissionHook(pending.ActionId, result);
        }
    }
}
