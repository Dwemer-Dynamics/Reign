using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Government;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ReignBeta.World
{
    public static class ReignRegularActionExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.ValidationFailed(validationFailure);
            }
            if (!ReignGovernmentActionGate.TryAuthorize(action, out ReignActionResult governmentResult))
                return governmentResult;

            Hero actor = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero target = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            MobileParty actorParty = actor?.PartyBelongedTo;
            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.RegularFollowOnMap:
                    return FollowOnMap(action, actor, actorParty, target);
                case ReignWorldActionType.RegularFollowInScene:
                    return SceneProgress(action, "follow_in_scene", "Follow-in-scene intent recorded; mission hooks will execute this when the actor is in the same scene.");
                case ReignWorldActionType.RegularStopFollowing:
                    return StopFollowing(actor, actorParty);
                case ReignWorldActionType.RegularGoToSettlement:
                    return GoToSettlement(actor, actorParty, settlement);
                case ReignWorldActionType.RegularPatrolAroundSettlement:
                    return PatrolAroundSettlement(actor, actorParty, settlement);
                case ReignWorldActionType.RegularWaitNearSettlement:
                    return WaitNearSettlement(actor, actorParty, settlement);
                case ReignWorldActionType.RegularRaidVillage:
                    return RaidVillage(actor, actorParty, settlement);
                case ReignWorldActionType.RegularBesiegeSettlement:
                    return BesiegeSettlement(actor, actorParty, settlement);
                case ReignWorldActionType.RegularCreateParty:
                    return CreateParty(action, actor, settlement);
                case ReignWorldActionType.RegularShowTheWay:
                    return SceneProgress(action, "show_the_way", "Show-the-way intent recorded; mission/menu hooks will guide the player when that UI path exists.");
                case ReignWorldActionType.RegularAttackParty:
                    return AttackParty(action, actor, actorParty, target);
                case ReignWorldActionType.RegularAttackPlayerParty:
                    return AttackPlayerParty(actor, actorParty);
                case ReignWorldActionType.RegularSurrenderToPlayer:
                    return SurrenderToPlayer(actor, actorParty);
                case ReignWorldActionType.RegularLeavePlayerAlone:
                    return LeavePlayerAlone(actor, actorParty);
                case ReignWorldActionType.RegularKillCharacter:
                    return KillCharacter(action, actor, target);
                case ReignWorldActionType.RegularDuelPlayer:
                    return ReignDuelService.QueueConversationDuel(action, actor, target);
                case ReignWorldActionType.RegularGiveGoldToPlayer:
                    return GiveGoldToPlayer(action, actor);
                case ReignWorldActionType.RegularTransferGold:
                    return TransferGold(action, actor, target);
                case ReignWorldActionType.RegularTransferItem:
                    return TransferItem(action, actor, target);
                case ReignWorldActionType.RegularTransferWorkshop:
                    return TransferWorkshop(action, target);
                case ReignWorldActionType.RegularTransferPrisoner:
                    return TransferPrisoner(action, actor);
                case ReignWorldActionType.RegularHirePlayerAsMercenary:
                    return HirePlayerAsMercenary(action);
                case ReignWorldActionType.RegularDismissPlayerMercenary:
                    return DismissPlayerMercenary();
                case ReignWorldActionType.RegularOfferPlayerVassalage:
                    return OfferPlayerVassalage(action);
                case ReignWorldActionType.RegularDismissPlayerVassal:
                    return DismissPlayerVassal();
                case ReignWorldActionType.RegularJoinClan:
                    return JoinClan(action, actor);
                case ReignWorldActionType.RegularLeaveClan:
                    return LeaveClan(actor);
                case ReignWorldActionType.RegularJoinKingdom:
                    return JoinKingdom(action);
                case ReignWorldActionType.RegularLeaveKingdom:
                    return LeaveKingdom(action);
                case ReignWorldActionType.RegularHireMercenaryClan:
                    return HireMercenaryClan(action);
                case ReignWorldActionType.RegularTradePackage:
                    return ReignTradePackageExecutor.Execute(action);
                case ReignWorldActionType.RegularPrepareArrest:
                    return ReignArrestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The arrest campaign service is unavailable.")
                        : ReignArrestCampaignBehavior.Instance.PrepareArrest(action, target);
                case ReignWorldActionType.RegularConfirmArrest:
                    return ReignArrestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The arrest campaign service is unavailable.")
                        : ReignArrestCampaignBehavior.Instance.ConfirmArrest(action, target);
                case ReignWorldActionType.RegularReleaseArrestedCharacter:
                    return ReignArrestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The arrest campaign service is unavailable.")
                        : ReignArrestCampaignBehavior.Instance.ReleaseArrested(action, target, false);
                case ReignWorldActionType.RegularRescindArrestAccusation:
                    return ReignArrestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The arrest campaign service is unavailable.")
                        : ReignArrestCampaignBehavior.Instance.ReleaseArrested(action, target, true);
                case ReignWorldActionType.RegularPlayerAttackParty:
                    return ReignArrestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The arrest campaign service is unavailable.")
                        : ReignArrestCampaignBehavior.Instance.AttackTargetParty(action, target);
                case ReignWorldActionType.RegularIssueCampaignOrder:
                case ReignWorldActionType.RegularReviseCampaignOrder:
                case ReignWorldActionType.RegularRespondToOrderReport:
                case ReignWorldActionType.RegularCancelCampaignOrder:
                    return ReignCampaignCommandBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The campaign command service is unavailable.")
                        : ReignCampaignCommandBehavior.Instance.ExecuteControlAction(action);
                case ReignWorldActionType.RegularCreateClanAccord:
                case ReignWorldActionType.RegularCancelClanAccord:
                    return ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The Clan Accords service is unavailable.")
                        : ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance.Execute(action);
                case ReignWorldActionType.RegularAcceptTemporaryPartyGuest:
                    return ReignTemporaryPartyGuestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The temporary noble guest service is unavailable.")
                        : ReignTemporaryPartyGuestCampaignBehavior.Instance.AcceptGuest(action, actor);
                case ReignWorldActionType.RegularRecruitEncounteredResident:
                case ReignWorldActionType.RegularReleaseResidentFromDuty:
                    return ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The encountered resident service is unavailable.")
                        : ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance.ExecuteResidentAction(action);
                case ReignWorldActionType.RegularRenewTemporaryPartyGuest:
                    return ReignTemporaryPartyGuestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The temporary noble guest service is unavailable.")
                        : ReignTemporaryPartyGuestCampaignBehavior.Instance.RenewGuest(action, actor);
                case ReignWorldActionType.RegularEndTemporaryPartyGuest:
                    return ReignTemporaryPartyGuestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The temporary noble guest service is unavailable.")
                        : ReignTemporaryPartyGuestCampaignBehavior.Instance.EndGuest(action, actor);
                case ReignWorldActionType.RegularAcknowledgeOwnFactionCombatRisk:
                    return ReignTemporaryPartyGuestCampaignBehavior.Instance == null
                        ? ReignActionResult.ValidationFailed("The temporary noble guest service is unavailable.")
                        : ReignTemporaryPartyGuestCampaignBehavior.Instance.AcknowledgeOwnFactionRisk(action, actor);
                default:
                    return ReignActionResult.FailTerminal("Unsupported regular action type: " + action.Type, "unsupported_action", "unsupported_action");
            }
        }

        private static ReignActionResult FollowOnMap(ReignWorldActionRecord action, Hero actor, MobileParty actorParty, Hero target)
        {
            MobileParty targetParty = ResolveTargetParty(action) ?? target?.PartyBelongedTo;
            if (targetParty == null)
            {
                return ReignActionResult.ValidationFailed("FollowOnMap requires a target hero or target party with an active party.");
            }

            SetPartyAiAction.GetActionForEscortingParty(actorParty, targetParty, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, targetParty.IsCurrentlyAtSea);
            return ReignActionResult.Progress(actor.Name + " is following " + PartyName(targetParty) + ".")
                .WithEffect("movement_order", "party", targetParty.StringId, PartyName(targetParty), "order=follow_on_map")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult StopFollowing(Hero actor, MobileParty actorParty)
        {
            actorParty.SetMoveModeHold();
            return ReignActionResult.Done(actor.Name + " stopped following and is holding position.")
                .WithEffect("movement_order", "party", actorParty.StringId, PartyName(actorParty), "order=hold")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult GoToSettlement(Hero actor, MobileParty actorParty, Settlement settlement)
        {
            SetPartyAiAction.GetActionForVisitingSettlement(actorParty, settlement, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, settlement.HasPort);
            return ReignActionResult.Progress(actor.Name + " is moving to " + settlement.Name + ".")
                .WithEffect("movement_order", "settlement", settlement.StringId, settlement.Name.ToString(), "order=go_to_settlement")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult PatrolAroundSettlement(Hero actor, MobileParty actorParty, Settlement settlement)
        {
            SetPartyAiAction.GetActionForPatrollingAroundSettlement(actorParty, settlement, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, settlement.HasPort);
            return ReignActionResult.Progress(actor.Name + " is patrolling around " + settlement.Name + ".")
                .WithEffect("movement_order", "settlement", settlement.StringId, settlement.Name.ToString(), "order=patrol")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult WaitNearSettlement(Hero actor, MobileParty actorParty, Settlement settlement)
        {
            SetPartyAiAction.GetActionForPatrollingAroundSettlement(actorParty, settlement, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, settlement.HasPort);
            return ReignActionResult.Progress(actor.Name + " is waiting near " + settlement.Name + ".")
                .WithEffect("movement_order", "settlement", settlement.StringId, settlement.Name.ToString(), "order=wait_near")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult RaidVillage(Hero actor, MobileParty actorParty, Settlement settlement)
        {
            SetPartyAiAction.GetActionForRaidingSettlement(actorParty, settlement, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea, false);
            return ReignActionResult.Progress(actor.Name + " is moving to raid " + settlement.Name + ".")
                .WithEffect("movement_order", "settlement", settlement.StringId, settlement.Name.ToString(), "order=raid_village")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult BesiegeSettlement(Hero actor, MobileParty actorParty, Settlement settlement)
        {
            SetPartyAiAction.GetActionForBesiegingSettlement(actorParty, settlement, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea);
            return ReignActionResult.Progress(actor.Name + " is moving to besiege " + settlement.Name + ".")
                .WithEffect("movement_order", "settlement", settlement.StringId, settlement.Name.ToString(), "order=besiege")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult CreateParty(ReignWorldActionRecord action, Hero actor, Settlement settlement)
        {
            if (actor.PartyBelongedTo != null)
            {
                return ReignActionResult.NoOp(actor.Name + " already has a party.")
                    .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "already_has_party");
            }

            Settlement spawn = settlement ?? actor.HomeSettlement ?? actor.Clan?.InitialHomeSettlement ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown);
            if (spawn == null)
            {
                return ReignActionResult.ValidationFailed("CreateParty requires a spawn settlement.");
            }

            MobileParty party = LordPartyComponent.CreateLordParty(actor.StringId + "_reign", actor, spawn.GatePosition, 2f, spawn, actor);
            party.SetMoveModeHold();
            return ReignActionResult.Done(actor.Name + " created a party at " + spawn.Name + ".")
                .WithEffect("party_created", "party", party.StringId, PartyName(party), "spawnSettlementId=" + spawn.StringId)
                .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "party_created");
        }

        private static ReignActionResult AttackParty(ReignWorldActionRecord action, Hero actor, MobileParty actorParty, Hero target)
        {
            MobileParty targetParty = ResolveTargetParty(action) ?? target?.PartyBelongedTo;
            if (targetParty == null)
            {
                return ReignActionResult.ValidationFailed("AttackParty requires a target party or a target hero with an active party.");
            }

            SetPartyAiAction.GetActionForEngagingParty(actorParty, targetParty, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea);
            return ReignActionResult.Progress(actor.Name + " is moving to attack " + PartyName(targetParty) + ".")
                .WithEffect("movement_order", "party", targetParty.StringId, PartyName(targetParty), "order=attack_party")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult AttackPlayerParty(Hero actor, MobileParty actorParty)
        {
            MobileParty playerParty = MobileParty.MainParty;
            SetPartyAiAction.GetActionForEngagingParty(actorParty, playerParty, actorParty.NavigationCapability, actorParty.IsCurrentlyAtSea);
            return ReignActionResult.Progress(actor.Name + " is moving to attack the player party.")
                .WithEffect("movement_order", "party", playerParty.StringId, PartyName(playerParty), "order=attack_player_party")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult SurrenderToPlayer(Hero actor, MobileParty actorParty)
        {
            MapEvent mapEvent = actorParty?.MapEvent;
            MapEventSide actorSide = actorParty?.Party?.MapEventSide;
            if (mapEvent == null || actorSide == null || !mapEvent.IsPlayerMapEvent || PartyBase.MainParty?.MapEvent != mapEvent)
            {
                return ReignActionResult.ValidationFailed("SurrenderToPlayer requires an active player encounter with the surrendering party.");
            }

            if (PlayerEncounter.Current != null && actorParty.Party.Side == PartyBase.MainParty.OpponentSide)
            {
                PlayerEncounter.EnemySurrender = true;
            }
            else
            {
                mapEvent.DoSurrender(actorParty.Party.Side);
            }

            BattleState expectedState = actorParty.Party.Side == BattleSideEnum.Defender
                ? BattleState.AttackerVictory
                : BattleState.DefenderVictory;
            if (mapEvent.BattleState != expectedState)
            {
                return ReignActionResult.FailTerminal("Native encounter surrender did not mark the actor side as surrendered.", "surrender_verification_failed", "state_verification");
            }

            return ReignActionResult.Done(actor.Name + " surrendered to the player in the active encounter.")
                .WithResultCode("encounter_surrender_completed")
                .WithEffect("party_surrendered", "party", actorParty.StringId, PartyName(actorParty), "captorPartyId=" + (MobileParty.MainParty?.StringId ?? string.Empty))
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "party_surrendered");
        }

        private static ReignActionResult LeavePlayerAlone(Hero actor, MobileParty actorParty)
        {
            actorParty.SetMoveModeHold();
            return ReignActionResult.Done(actor.Name + " stopped pursuing the player.")
                .WithEffect("movement_order", "party", actorParty.StringId, PartyName(actorParty), "order=leave_player_alone")
                .WithChangedEntity("party", actorParty.StringId, PartyName(actorParty), "movement_order_changed");
        }

        private static ReignActionResult KillCharacter(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            Hero killer = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "killerHeroStringId", string.Empty)) ?? actor;
            KillCharacterAction.ApplyByExecution(target, killer, true, true);
            if (target.IsAlive)
                return ReignActionResult.FailTerminal("Campaign protections prevented the character's death.",
                    "kill_verification_failed", "state_verification");
            return ReignActionResult.Done(target.Name + " was killed by " + killer.Name + ".")
                .WithEffect("hero_killed", "hero", target.StringId, target.Name.ToString(), "killerHeroId=" + killer.StringId)
                .WithChangedEntity("hero", target.StringId, target.Name.ToString(), "killed");
        }

        private static ReignActionResult GiveGoldToPlayer(ReignWorldActionRecord action, Hero actor)
        {
            int amount = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", ReadIntTerm(action.TermsJson, "goldAmount", 0)));
            int actorBefore = actor.Gold;
            int playerBefore = Hero.MainHero.Gold;
            GiveGoldAction.ApplyBetweenCharacters(actor, Hero.MainHero, amount, false);
            if (actor.Gold != actorBefore - amount || Hero.MainHero.Gold != playerBefore + amount)
            {
                return ReignActionResult.FailTerminal("Gold transfer verification failed; no success was recorded.", "gold_verification_failed", "state_verification");
            }

            ReignActionResult result = ReignActionResult.Done(actor.Name + " gave " + amount + " denars to the player.")
                .WithEffect("gold_transfer", "hero", Hero.MainHero.StringId, Hero.MainHero.Name.ToString(), "from=" + actor.StringId + ";amount=" + amount)
                .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "gold_changed")
                .WithChangedEntity("hero", Hero.MainHero.StringId, Hero.MainHero.Name.ToString(), "gold_changed");
            return WithVerifiedReceipt(result);
        }

        private static ReignActionResult TransferGold(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            int amount = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", ReadIntTerm(action.TermsJson, "goldAmount", 0)));
            Hero from = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "fromHeroStringId", string.Empty)) ?? actor;
            Hero to = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? target ?? Hero.MainHero;
            int fromBefore = from.Gold;
            int toBefore = to.Gold;
            GiveGoldAction.ApplyBetweenCharacters(from, to, amount, false);
            if (from.Gold != fromBefore - amount || to.Gold != toBefore + amount)
            {
                return ReignActionResult.FailTerminal("Gold transfer verification failed; no success was recorded.", "gold_verification_failed", "state_verification");
            }

            ReignActionResult result = ReignActionResult.Done(from.Name + " transferred " + amount + " denars to " + to.Name + ".")
                .WithEffect("gold_transfer", "hero", to.StringId, to.Name.ToString(), "from=" + from.StringId + ";amount=" + amount)
                .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "gold_changed")
                .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "gold_changed");
            return WithVerifiedReceipt(result);
        }

        private static ReignActionResult TransferItem(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            Hero fromHero = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "fromHeroStringId", string.Empty)) ?? actor;
            Hero toHero = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? target ?? Hero.MainHero;
            MobileParty fromParty = PartyForInventory(fromHero);
            MobileParty toParty = PartyForInventory(toHero);
            string itemId = ReadStringTerm(action.TermsJson, "itemId", ReadStringTerm(action.TermsJson, "item", string.Empty));
            ItemObject item = ReignObjectResolver.FindItem(itemId);
            int amount = ReadIntTerm(action.TermsJson, "amount", ReadIntTerm(action.TermsJson, "Amount", 1));
            int available = fromParty?.ItemRoster?.GetItemNumber(item) ?? 0;
            if (available >= amount)
            {
                int toBefore = toParty.ItemRoster.GetItemNumber(item);
                fromParty.ItemRoster.AddToCounts(item, -amount);
                toParty.ItemRoster.AddToCounts(item, amount);
                if (fromParty.ItemRoster.GetItemNumber(item) != available - amount
                    || toParty.ItemRoster.GetItemNumber(item) != toBefore + amount)
                {
                    return ReignActionResult.FailTerminal("Item transfer verification failed; no success was recorded.", "item_verification_failed", "state_verification");
                }

                ReignActionResult result = ReignActionResult.Done(fromHero.Name + " transferred " + amount + " " + item.Name + " to " + toHero.Name + ".")
                    .WithEffect("item_transfer", "item", item.StringId, item.Name.ToString(), "from=" + fromHero.StringId + ";to=" + toHero.StringId + ";amount=" + amount + ";source=inventory")
                    .WithChangedEntity("party", fromParty.StringId, PartyName(fromParty), "items_changed")
                    .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
                return WithVerifiedReceipt(result);
            }

            if (amount == 1 && ReignObjectResolver.TryFindEquippedItem(
                fromHero,
                itemId,
                ReadStringTerm(action.TermsJson, "sourceEquipmentSlot", string.Empty),
                ReadStringTerm(action.TermsJson, "sourceEquipmentSet", string.Empty),
                out Equipment equipment,
                out EquipmentIndex slot,
                out EquipmentElement equippedElement,
                out string sourceSet))
            {
                int toBefore = toParty.ItemRoster.GetItemNumber(equippedElement.Item);
                toParty.ItemRoster.AddToCounts(equippedElement, 1);
                equipment[slot].Clear();
                if (!equipment[slot].IsEmpty || toParty.ItemRoster.GetItemNumber(equippedElement.Item) != toBefore + 1)
                {
                    return ReignActionResult.FailTerminal("Equipped item transfer verification failed; no success was recorded.", "item_verification_failed", "state_verification");
                }

                ReignActionResult result = ReignActionResult.Done(fromHero.Name + " removed " + equippedElement.Item.Name + " and transferred it to " + toHero.Name + ".")
                    .WithEffect("equipped_item_transfer", "item", equippedElement.Item.StringId, equippedElement.Item.Name.ToString(), "from=" + fromHero.StringId + ";to=" + toHero.StringId + ";amount=1;source=" + sourceSet + ";slot=" + slot)
                    .WithChangedEntity("hero", fromHero.StringId, fromHero.Name.ToString(), "equipment_changed")
                    .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
                return WithVerifiedReceipt(result);
            }

            return ReignActionResult.ValidationFailed(fromHero.Name + " does not have enough " + (item?.Name?.ToString() ?? itemId) + " in inventory or equipped slots.");
        }

        private static ReignActionResult WithVerifiedReceipt(ReignActionResult result)
        {
            string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
            return string.IsNullOrWhiteSpace(receipt) ? result : result.WithMessage("Verified: " + receipt + ".");
        }

        private static ReignActionResult TransferWorkshop(ReignWorldActionRecord action, Hero target)
        {
            Workshop workshop = ReignObjectResolver.FindWorkshop(ReadStringTerm(action.TermsJson, "workshopId", ReadStringTerm(action.TermsJson, "workshop", string.Empty)));
            Hero to = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? target ?? Hero.MainHero;
            Hero oldOwner = workshop.Owner;
            ChangeOwnerOfWorkshopAction.ApplyByWar(workshop, to, workshop.WorkshopType);
            return ReignActionResult.Done((oldOwner?.Name?.ToString() ?? "A workshop owner") + " transferred " + workshop.Name + " to " + to.Name + ".")
                .WithEffect("workshop_transfer", "workshop", ReignObjectResolver.WorkshopId(workshop), workshop.Name.ToString(), "from=" + (oldOwner?.StringId ?? string.Empty) + ";to=" + to.StringId)
                .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "workshop_received");
        }

        private static ReignActionResult TransferPrisoner(ReignWorldActionRecord action, Hero actor)
        {
            Hero prisoner = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "prisonerHeroStringId", action.TargetHeroStringId));
            Hero facilitator = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? actor ?? Hero.MainHero;
            EndCaptivityAction.ApplyByReleasedByChoice(prisoner, facilitator);
            return ReignActionResult.Done(prisoner.Name + " was released or transferred by agreement.")
                .WithEffect("prisoner_release", "hero", prisoner.StringId, prisoner.Name.ToString(), "facilitator=" + facilitator.StringId)
                .WithChangedEntity("hero", prisoner.StringId, prisoner.Name.ToString(), "released_from_captivity");
        }

        private static ReignActionResult HirePlayerAsMercenary(ReignWorldActionRecord action)
        {
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            int awardMultiplier = ReadIntTerm(action.TermsJson, "awardMultiplier", 50);
            ChangeKingdomAction.ApplyByJoinFactionAsMercenary(Clan.PlayerClan, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)), awardMultiplier, true);
            return ReignActionResult.Done("The player clan entered mercenary service for " + kingdom.InformalName + ".")
                .WithEffect("mercenary_service_started", "kingdom", kingdom.StringId, kingdom.InformalName.ToString(), "clanId=" + Clan.PlayerClan.StringId)
                .WithChangedEntity("clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult DismissPlayerMercenary()
        {
            ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(Clan.PlayerClan, true);
            return ReignActionResult.Done("The player clan left mercenary service.")
                .WithEffect("mercenary_service_ended", "clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "dismissed=true")
                .WithChangedEntity("clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult OfferPlayerVassalage(ReignWorldActionRecord action)
        {
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)), true);
            return ReignActionResult.Done("The player clan became a vassal of " + kingdom.InformalName + ".")
                .WithEffect("vassalage_started", "kingdom", kingdom.StringId, kingdom.InformalName.ToString(), "clanId=" + Clan.PlayerClan.StringId)
                .WithChangedEntity("clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult DismissPlayerVassal()
        {
            ChangeKingdomAction.ApplyByLeaveKingdom(Clan.PlayerClan, true);
            return ReignActionResult.Done("The player clan left its kingdom.")
                .WithEffect("vassalage_ended", "clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "dismissed=true")
                .WithChangedEntity("clan", Clan.PlayerClan.StringId, Clan.PlayerClan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult JoinKingdom(ReignWorldActionRecord action)
        {
            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId) ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan;
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Kingdom old = clan.Kingdom;
            if (old != null && old != kingdom)
            {
                ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)), true);
            }
            else
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)), true);
            }

            return ReignActionResult.Done(clan.Name + " joined " + kingdom.InformalName + ".")
                .WithEffect("clan_joined_kingdom", "clan", clan.StringId, clan.Name.ToString(), "to=" + kingdom.StringId)
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult LeaveKingdom(ReignWorldActionRecord action)
        {
            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId) ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan;
            Kingdom old = clan.Kingdom;
            if (clan.IsUnderMercenaryService)
            {
                ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, true);
            }
            else
            {
                ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
            }

            return ReignActionResult.Done(clan.Name + " left " + (old?.InformalName?.ToString() ?? "its kingdom") + ".")
                .WithEffect("clan_left_kingdom", "clan", clan.StringId, clan.Name.ToString(), "from=" + (old?.StringId ?? string.Empty))
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult HireMercenaryClan(ReignWorldActionRecord action)
        {
            Clan clan = ReignObjectResolver.FindClan(action.TargetClanStringId) ?? ReignObjectResolver.FindClan(action.ActorClanStringId);
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            int awardMultiplier = ReadIntTerm(action.TermsJson, "awardMultiplier", 50);
            ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)), awardMultiplier, true);
            return ReignActionResult.Done(clan.Name + " entered mercenary service for " + kingdom.InformalName + ".")
                .WithEffect("mercenary_clan_hired", "clan", clan.StringId, clan.Name.ToString(), "kingdomId=" + kingdom.StringId)
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "kingdom_changed");
        }

        private static ReignActionResult JoinClan(ReignWorldActionRecord action, Hero actor)
        {
            Clan targetClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
            Clan previousClan = actor.Clan;
            actor.Clan = targetClan;
            if (actor.Clan != targetClan)
            {
                return ReignActionResult.FailTerminal("Native hero clan transfer did not place the hero in the target clan.", "join_clan_verification_failed", "state_verification");
            }

            return ReignActionResult.Done(actor.Name + " joined " + targetClan.Name + ".")
                .WithEffect("hero_joined_clan", "clan", targetClan.StringId, targetClan.Name.ToString(), "heroId=" + actor.StringId + ";fromClanId=" + (previousClan?.StringId ?? string.Empty))
                .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "hero_clan_changed");
        }

        private static ReignActionResult LeaveClan(Hero actor)
        {
            Clan previousClan = actor.Clan;
            actor.Clan = null;
            if (actor.Clan != null)
            {
                return ReignActionResult.FailTerminal("Native hero clan transfer did not remove the hero from the clan.", "leave_clan_verification_failed", "state_verification");
            }

            return ReignActionResult.Done(actor.Name + " left " + previousClan.Name + ".")
                .WithEffect("hero_left_clan", "clan", previousClan.StringId, previousClan.Name.ToString(), "heroId=" + actor.StringId)
                .WithChangedEntity("hero", actor.StringId, actor.Name.ToString(), "hero_clan_changed");
        }

        private static ReignActionResult SceneProgress(ReignWorldActionRecord action, string code, string message)
        {
            return ReignActionResult.Progress(message)
                .WithResultCode(code + "_pending_mission_hook")
                .WithEffect("scene_intent", "action", action.ActionId, action.Type.ToString(), "hook=" + code);
        }

        private static MobileParty ResolveTargetParty(ReignWorldActionRecord action)
        {
            string partyId = ReadStringTerm(action.TermsJson, "targetPartyId", string.Empty);
            return ReignObjectResolver.FindParty(partyId);
        }

        private static MobileParty PartyForInventory(Hero hero)
        {
            if (hero == Hero.MainHero)
            {
                return MobileParty.MainParty;
            }

            return hero?.PartyBelongedTo;
        }

        private static string PartyName(MobileParty party)
        {
            return party?.Name?.ToString() ?? party?.StringId ?? string.Empty;
        }

        private static string ReadStringTerm(string json, string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JToken token = JObject.Parse(json)[key];
                return token == null ? fallback : token.ToString();
            }
            catch
            {
                return fallback;
            }
        }

        private static int ReadIntTerm(string json, string key, int fallback)
        {
            string value = ReadStringTerm(json, key, string.Empty);
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static float ReadFloatTerm(string json, string key, float fallback)
        {
            string value = ReadStringTerm(json, key, string.Empty);
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
