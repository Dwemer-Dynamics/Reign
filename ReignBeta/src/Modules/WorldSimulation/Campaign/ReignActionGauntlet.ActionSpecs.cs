using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Platform;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignActionGauntlet
    {
        private static ActionSpec FollowOnMap(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null)
            {
                return Skip("Need two active non-player lord parties.");
            }

            return Spec(Action(ReignWorldActionType.RegularFollowOnMap, c, "FollowOnMap", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["targetPartyId"] = c.TargetHero.PartyBelongedTo.StringId
            }));
        }

        private static ActionSpec FollowInScene(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need an actor hero.") : Spec(Action(ReignWorldActionType.RegularFollowInScene, c, "FollowInScene", c.ActorHero));
        }

        private static ActionSpec StopFollowing(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need an actor party.") : Spec(Action(ReignWorldActionType.RegularStopFollowing, c, "StopFollowing", c.ActorHero));
        }

        private static ActionSpec GoToSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularGoToSettlement, c, "GoToSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec PatrolAroundSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularPatrolAroundSettlement, c, "PatrolAroundSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec WaitNearSettlement(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null || c.ArenaSettlement == null ? Skip("Need actor party and arena settlement.") : Spec(Action(ReignWorldActionType.RegularWaitNearSettlement, c, "WaitNearSettlement", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec RaidVillage(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileVillage == null)
            {
                return Skip("Need actor party and hostile village.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileVillage.MapFaction, null);
            return Spec(Action(ReignWorldActionType.RegularRaidVillage, c, "RaidVillage", c.ActorHero, null, c.HostileVillage));
        }

        private static ActionSpec BesiegeSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(Action(ReignWorldActionType.RegularBesiegeSettlement, c, "BesiegeSettlement", c.ActorHero, null, c.HostileFortification));
        }

        private static ActionSpec CreateParty(GauntletContext c)
        {
            Hero hero = c.PartylessHero ?? c.ActorHero;
            return hero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularCreateParty, c, "CreateParty", hero, null, c.ArenaSettlement));
        }

        private static ActionSpec ShowTheWay(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularShowTheWay, c, "ShowTheWay", c.ActorHero, null, c.ArenaSettlement));
        }

        private static ActionSpec AttackParty(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null)
            {
                return Skip("Need two active non-player lord parties.");
            }

            return Spec(Action(ReignWorldActionType.RegularAttackParty, c, "AttackParty", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["targetPartyId"] = c.TargetHero.PartyBelongedTo.StringId
            }));
        }

        private static ActionSpec AttackPlayerParty(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need actor party.") : Spec(Action(ReignWorldActionType.RegularAttackPlayerParty, c, "AttackPlayerParty", c.ActorHero));
        }

        private static ActionSpec SurrenderToPlayer(GauntletContext c)
        {
            Hero surrenderHero = c.SurrenderHero;
            MobileParty surrenderParty = surrenderHero?.PartyBelongedTo;
            if (surrenderParty == null || MobileParty.MainParty == null)
            {
                return Skip("Need a distinct hostile lord party for a disposable player encounter.");
            }

            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.Finish();
            }

            EnsureWar(MobileParty.MainParty.MapFaction, surrenderParty.MapFaction, null);
            StartBattleAction.ApplyStartBattle(MobileParty.MainParty, surrenderParty);
            if (surrenderParty.MapEvent != null && PartyBase.MainParty.MapEvent == surrenderParty.MapEvent)
            {
                PlayerEncounter.Start();
                PlayerEncounter.Init();
            }

            if (PlayerEncounter.Current == null || surrenderParty.MapEvent == null || PartyBase.MainParty.MapEvent != surrenderParty.MapEvent)
            {
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.Finish();
                }
                return Skip("Could not establish the native player encounter required for surrender.");
            }

            return Spec(
                Action(ReignWorldActionType.RegularSurrenderToPlayer, c, "SurrenderToPlayer", surrenderHero),
                () =>
                {
                    if (PlayerEncounter.Current != null)
                    {
                        PlayerEncounter.Finish();
                    }
                });
        }

        private static ActionSpec LeavePlayerAlone(GauntletContext c)
        {
            return c.ActorHero?.PartyBelongedTo == null ? Skip("Need actor party.") : Spec(Action(ReignWorldActionType.RegularLeavePlayerAlone, c, "LeavePlayerAlone", c.ActorHero));
        }

        private static ActionSpec KillCharacter(GauntletContext c)
        {
            return c.KillTarget == null || c.ActorHero == null ? Skip("Need a non-critical kill target and actor hero.") : Spec(Action(ReignWorldActionType.RegularKillCharacter, c, "KillCharacter", c.ActorHero, c.KillTarget));
        }

        private static ActionSpec DuelPlayer(GauntletContext c)
        {
            return c.ActorHero == null ? Skip("Need actor hero.") : Spec(Action(ReignWorldActionType.RegularDuelPlayer, c, "DuelPlayer", c.ActorHero));
        }

        private static ActionSpec GiveGoldToPlayer(GauntletContext c)
        {
            if (c.ActorHero == null)
            {
                return Skip("Need actor hero.");
            }

            EnsureHeroGold(c.ActorHero, 5000);
            return Spec(Action(ReignWorldActionType.RegularGiveGoldToPlayer, c, "GiveGoldToPlayer", c.ActorHero, Hero.MainHero, null, new JObject { ["gold"] = 250 }));
        }

        private static ActionSpec TransferGold(GauntletContext c)
        {
            if (c.ActorHero == null || c.TargetHero == null)
            {
                return Skip("Need actor and target heroes.");
            }

            EnsureHeroGold(c.ActorHero, 5000);
            return Spec(Action(ReignWorldActionType.RegularTransferGold, c, "TransferGold", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["gold"] = 300,
                ["fromHeroStringId"] = c.ActorHero.StringId,
                ["toHeroStringId"] = c.TargetHero.StringId
            }));
        }

        private static ActionSpec TransferItem(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.TargetHero?.PartyBelongedTo == null || DefaultItems.Grain == null)
            {
                return Skip("Need two active parties and grain.");
            }

            c.ActorHero.PartyBelongedTo.ItemRoster.AddToCounts(DefaultItems.Grain, 15);
            return Spec(Action(ReignWorldActionType.RegularTransferItem, c, "TransferItem", c.ActorHero, c.TargetHero, null, new JObject
            {
                ["itemId"] = DefaultItems.Grain.StringId,
                ["amount"] = 5,
                ["fromHeroStringId"] = c.ActorHero.StringId,
                ["toHeroStringId"] = c.TargetHero.StringId
            }));
        }

        private static ActionSpec TransferWorkshop(GauntletContext c)
        {
            if (c.Workshop == null)
            {
                return Skip("Need a workshop with an owner.");
            }

            return Spec(Action(ReignWorldActionType.RegularTransferWorkshop, c, "TransferWorkshop", c.ActorHero, Hero.MainHero, null, new JObject
            {
                ["workshopId"] = ReignObjectResolver.WorkshopId(c.Workshop),
                ["toHeroStringId"] = Hero.MainHero.StringId
            }));
        }

        private static ActionSpec TransferPrisoner(GauntletContext c)
        {
            if (c.PrisonerHero == null)
            {
                return Skip("Need a prisoner hero.");
            }

            if (!c.PrisonerHero.IsPrisoner)
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, c.PrisonerHero);
            }

            return Spec(Action(ReignWorldActionType.RegularTransferPrisoner, c, "TransferPrisoner", c.ActorHero, c.PrisonerHero, null, new JObject
            {
                ["prisonerHeroStringId"] = c.PrisonerHero.StringId,
                ["toHeroStringId"] = Hero.MainHero.StringId
            }));
        }

        private static ActionSpec HirePlayerAsMercenary(GauntletContext c)
        {
            EnsurePlayerClanIndependentForMembershipTest();
            return c.TargetKingdom == null ? Skip("Need target kingdom.") : Spec(Action(ReignWorldActionType.RegularHirePlayerAsMercenary, c, "HirePlayerAsMercenary", null, null, null, new JObject { ["awardMultiplier"] = 35 }, null, null, c.TargetKingdom));
        }

        private static ActionSpec DismissPlayerMercenary(GauntletContext c)
        {
            return Clan.PlayerClan == null || !Clan.PlayerClan.IsUnderMercenaryService ? Skip("Player clan is not in mercenary service.") : Spec(Action(ReignWorldActionType.RegularDismissPlayerMercenary, c, "DismissPlayerMercenary"));
        }

        private static ActionSpec OfferPlayerVassalage(GauntletContext c)
        {
            EnsurePlayerClanIndependentForMembershipTest();
            return c.TargetKingdom == null ? Skip("Need target kingdom.") : Spec(Action(ReignWorldActionType.RegularOfferPlayerVassalage, c, "OfferPlayerVassalage", null, null, null, null, null, null, c.TargetKingdom));
        }

        private static ActionSpec DismissPlayerVassal(GauntletContext c)
        {
            return Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.IsUnderMercenaryService ? Skip("Player clan is not a normal vassal.") : Spec(Action(ReignWorldActionType.RegularDismissPlayerVassal, c, "DismissPlayerVassal"));
        }

        private static ActionSpec JoinClan(GauntletContext c)
        {
            Hero hero = c.ClanTransferHero;
            if (hero == null || c.TargetClan == null)
            {
                return Skip("Need a living non-leader clan member and a different target clan.");
            }

            c.ClanTransferOriginalClan = hero.Clan;
            return Spec(Action(ReignWorldActionType.RegularJoinClan, c, "JoinClan", hero, null, null, null, null, c.TargetClan));
        }

        private static ActionSpec LeaveClan(GauntletContext c)
        {
            Hero hero = c.ClanTransferHero;
            if (hero?.Clan == null)
            {
                return Skip("Need the joined non-leader hero to be in a clan.");
            }

            Clan originalClan = c.ClanTransferOriginalClan;
            return Spec(
                Action(ReignWorldActionType.RegularLeaveClan, c, "LeaveClan", hero),
                () =>
                {
                    if (hero != null && hero.CompanionOf == null && hero.Clan != originalClan)
                    {
                        hero.Clan = originalClan;
                    }
                });
        }

        private static ActionSpec JoinKingdom(GauntletContext c)
        {
            if (c.JoinClan == null || c.TargetKingdom == null)
            {
                return Skip("Need a non-player clan and target kingdom.");
            }

            if (c.JoinClan.Kingdom == c.TargetKingdom)
            {
                return Skip("Selected clan already belongs to target kingdom.");
            }

            return Spec(Action(ReignWorldActionType.RegularJoinKingdom, c, "JoinKingdom", null, null, null, null, c.JoinClan, null, c.TargetKingdom));
        }

        private static ActionSpec LeaveKingdom(GauntletContext c)
        {
            Clan clan = c.LastJoinedClan ?? c.JoinClan;
            return clan == null || clan.Kingdom == null ? Skip("Need a clan currently in a kingdom.") : Spec(Action(ReignWorldActionType.RegularLeaveKingdom, c, "LeaveKingdom", null, null, null, null, clan));
        }

        private static ActionSpec HireMercenaryClan(GauntletContext c)
        {
            Clan clan = c.MercenaryClan ?? c.JoinClan;
            return clan == null || c.TargetKingdom == null ? Skip("Need mercenary/target clan and target kingdom.") : Spec(Action(ReignWorldActionType.RegularHireMercenaryClan, c, "HireMercenaryClan", null, null, null, new JObject { ["awardMultiplier"] = 45 }, null, clan, c.TargetKingdom));
        }

        private static ActionSpec DiplomacyDeclareWar(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDeclareWar, c, "DeclareWar", actor, target);
        }

        private static ActionSpec DiplomacyMakePeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyMakePeace, c, "MakePeace", actor, target);
        }

        private static ActionSpec DiplomacyOfferTributePeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyOfferTributePeace, c, "OfferTributePeace", actor, target, null, new JObject { ["dailyTribute"] = 25, ["durationDays"] = 10 });
        }

        private static ActionSpec DiplomacyRecordPromise(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return DiplomacySpec(ReignWorldActionType.DiplomacyRecordPromise, c, "RecordPromise", actor, target, null, new JObject { ["durationDays"] = 15, ["isPublic"] = false, ["promiseText"] = "gauntlet promise" });
        }

        private static ActionSpec DiplomacyDemandReparationsPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureWar(actor, target, null);
            EnsureLeaderGold(target, 1000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandReparationsPeace, c, "DemandReparationsPeace", actor, target, null, new JObject { ["reparationsGold"] = 100, ["dailyTribute"] = 10, ["durationDays"] = 10 });
        }

        private static ActionSpec DiplomacyDemandSettlementPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            Settlement settlement = FindFortificationOwnedBy(target);
            if (settlement == null)
            {
                return Skip("Need a target-owned town or castle.");
            }

            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandSettlementPeace, c, "DemandSettlementPeace", actor, target, settlement, new JObject
            {
                ["settlementIds"] = new JArray(settlement.StringId),
                ["reparationsGold"] = 50
            });
        }

        private static ActionSpec DiplomacyDemandSurrenderPeace(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            if (target == null || !Settlement.All.Any(x => x != null && x.IsFortification && x.MapFaction == target))
            {
                return Skip("Need a target kingdom with towns or castles.");
            }

            EnsureWar(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyDemandSurrenderPeace, c, "DemandSurrenderPeace", actor, target, null, new JObject
            {
                ["allTargetFortifications"] = true,
                ["reparationsGold"] = 50
            });
        }

        private static ActionSpec DiplomacySignTradeAgreement(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignTradeAgreement, c, "SignTradeAgreement", "trade_agreement", 120);
        }

        private static ActionSpec DiplomacySignNonAggressionPact(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignNonAggressionPact, c, "SignNonAggressionPact", "non_aggression_pact", 90);
        }

        private static ActionSpec DiplomacySignAlliance(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignAlliance, c, "SignAlliance", "alliance", 180);
        }

        private static ActionSpec DiplomacySignDefensivePact(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacySignDefensivePact, c, "SignDefensivePact", "defensive_pact", 120);
        }

        private static ActionSpec DiplomacyBreakTreaty(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            if (actor == null || target == null)
            {
                return Skip("Need two live kingdoms.");
            }

            EnsurePeace(actor, target, null);
            ReignWorldActionRecord seed = WorldAction(ReignWorldActionType.DiplomacyRecordPromise, c, "BreakTreatySeed", actor, target, null, null, null, null, null, null, new JObject { ["kind"] = "trade_agreement" });
            ReignServices.AI?.RecordAgreementFromAction(seed, "trade_agreement", 30f, true);
            return DiplomacySpec(ReignWorldActionType.DiplomacyBreakTreaty, c, "BreakTreaty", actor, target, null, new JObject { ["agreementKind"] = "trade_agreement" });
        }

        private static ActionSpec DiplomacyExchangePrisoners(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Hero prisoner = EnsurePrisonerFrom(target, actor, c.ActorHero, c.TargetHero);
            if (prisoner == null)
            {
                return Skip("Need a target-kingdom prisoner held by the actor kingdom.");
            }

            return DiplomacySpec(ReignWorldActionType.DiplomacyExchangePrisoners, c, "ExchangePrisoners", actor, target, null, new JObject { ["maxEachSide"] = 2 }, prisoner);
        }

        private static ActionSpec DiplomacyRansomPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Hero prisoner = EnsurePrisonerFrom(target, actor, c.ActorHero, c.TargetHero);
            if (prisoner == null)
            {
                return Skip("Need a target-kingdom prisoner held by the actor kingdom.");
            }

            EnsureLeaderGold(target, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyRansomPackage, c, "RansomPackage", actor, target, null, new JObject { ["maxPrisoners"] = 1, ["ransomGold"] = 500 }, prisoner);
        }

        private static ActionSpec DiplomacyHostageGuarantee(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyHostageGuarantee, c, "HostageGuarantee", new JObject { ["durationDays"] = 60, ["isPublic"] = false });
        }

        private static ActionSpec DiplomacyWarIndemnity(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureLeaderGold(target, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyWarIndemnity, c, "WarIndemnity", actor, target, null, new JObject { ["indemnityGold"] = 400 });
        }

        private static ActionSpec DiplomacyRecognizeConquest(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyRecognizeConquest, c, "RecognizeConquest", new JObject { ["durationDays"] = 0 });
        }

        private static ActionSpec DiplomacyReturnOccupiedSettlement(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Settlement settlement = FindFortificationOwnedBy(actor);
            if (settlement == null)
            {
                return Skip("Need an actor-owned town or castle to return.");
            }

            EnsurePeace(actor, target, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyReturnOccupiedSettlement, c, "ReturnOccupiedSettlement", actor, target, settlement);
        }

        private static ActionSpec DiplomacyDemilitarizedBorder(GauntletContext c)
        {
            return AgreementSpec(ReignWorldActionType.DiplomacyDemilitarizedBorder, c, "DemilitarizedBorder", new JObject { ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacySupplyAgreement(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacySupplyAgreement, c, "SupplyAgreement", actor, target, null, new JObject { ["gold"] = 200, ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacyLoanOrSubsidy(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyLoanOrSubsidy, c, "LoanOrSubsidy", actor, target, null, new JObject { ["loanGold"] = 300, ["durationDays"] = 120 });
        }

        private static ActionSpec DiplomacyPayToStayNeutral(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsureLeaderGold(actor, 10000);
            return DiplomacySpec(ReignWorldActionType.DiplomacyPayToStayNeutral, c, "PayToStayNeutral", actor, target, null, new JObject { ["gold"] = 250, ["durationDays"] = 60 });
        }

        private static ActionSpec DiplomacyPayToJoinWar(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Kingdom enemy = FindOtherLiveKingdom(actor, target);
            if (enemy == null)
            {
                return Skip("Need a third live kingdom.");
            }

            EnsureLeaderGold(actor, 10000);
            EnsurePeace(target, enemy, null);
            return DiplomacySpec(ReignWorldActionType.DiplomacyPayToJoinWar, c, "PayToJoinWar", actor, target, null, new JObject
            {
                ["gold"] = 250,
                ["enemyKingdomId"] = enemy.StringId,
                ["durationDays"] = 30
            });
        }

        private static ActionSpec DiplomacyGuaranteeIndependence(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacyGuaranteeIndependence, c, "GuaranteeIndependence", "guarantee_independence", 180);
        }

        private static ActionSpec DiplomacyProtectorateOrVassalage(GauntletContext c)
        {
            return PeaceAgreementSpec(ReignWorldActionType.DiplomacyProtectorateOrVassalage, c, "ProtectorateOrVassalage", "protectorate_or_vassalage", 180);
        }

        private static ActionSpec DiplomacyPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindKingdomWithFortificationNot(actor);
            Settlement settlement = FindFortificationOwnedBy(target);
            if (actor == null || target == null || settlement == null)
            {
                return Skip("Need actor/target kingdoms and a target-owned fortification.");
            }

            EnsureLeaderGold(actor, 10000);
            var spec = DiplomacySpec(ReignWorldActionType.DiplomacyPackage, c, "DiplomaticPackage", actor, target, settlement, new JObject
            {
                ["gold"] = 100,
                ["fromHeroStringId"] = actor.Leader?.StringId ?? string.Empty,
                ["toHeroStringId"] = target.Leader?.StringId ?? string.Empty,
                ["settlementIds"] = new JArray(settlement.StringId),
                ["packageText"] = "gauntlet compound package"
            });
            return ExpectedFailure(spec.Action);
        }

        private static ActionSpec PeaceAgreementSpec(ReignWorldActionType type, GauntletContext c, string label, string kind, int days)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            EnsurePeace(actor, target, null);
            return DiplomacySpec(type, c, label, actor, target, null, new JObject { ["durationDays"] = days, ["kind"] = kind });
        }

        private static ActionSpec AgreementSpec(ReignWorldActionType type, GauntletContext c, string label, JObject terms)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return DiplomacySpec(type, c, label, actor, target, null, terms);
        }

        private static ActionSpec StrategyRecruitAndRecover(GauntletContext c)
        {
            MobileParty party = c.ActorHero?.PartyBelongedTo;
            if (party == null || c.ArenaSettlement == null)
            {
                return Skip("Need actor party and recovery settlement.");
            }

            // Prepare a real recovery need without destructively removing troops from the
            // disposable save. The action's requested threshold is one troop above the
            // current roster, so the native executor must issue a recovery movement order.
            int requiredTroops = Math.Min(10000, Math.Max(1, party.MemberRoster.TotalManCount + 1));
            return Spec(WorldAction(ReignWorldActionType.StrategyRecruitAndRecover, c, "RecruitAndRecover", null, null, c.ActorHero, null, c.ArenaSettlement, null, null, null, new JObject(), minimumTroops: requiredTroops));
        }

        private static ActionSpec StrategyFormArmy(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyFormArmy, c, "FormArmy", null, null, c.ActorHero, null, c.HostileFortification, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec StrategyAttackSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileVillage == null)
            {
                return Skip("Need actor party and hostile village.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileVillage.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyAttackSettlement, c, "AttackSettlement", null, null, c.ActorHero, null, c.HostileVillage, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec StrategyCaptureSettlement(GauntletContext c)
        {
            if (c.ActorHero?.PartyBelongedTo == null || c.HostileFortification == null)
            {
                return Skip("Need actor party and hostile fortification.");
            }

            EnsureWar(c.ActorHero.PartyBelongedTo.MapFaction, c.HostileFortification.MapFaction, null);
            return Spec(WorldAction(ReignWorldActionType.StrategyCaptureSettlement, c, "CaptureSettlement", null, null, c.ActorHero, null, c.HostileFortification, null, null, null, new JObject(), minimumTroops: 20));
        }

        private static ActionSpec PoliticsMarriageAlliance(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            Clan target = FindSecondClanInKingdom(kingdom, claimant);
            return PoliticsSpec(ReignWorldActionType.PoliticsMarriageAlliance, c, "MarriageAlliance", kingdom, claimant, target, new JObject { ["durationDays"] = 365 });
        }

        private static ActionSpec PoliticsSupportClaimant(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            return PoliticsSpec(ReignWorldActionType.PoliticsSupportClaimant, c, "SupportClaimant", kingdom, claimant, null, new JObject { ["durationDays"] = 120, ["isPublic"] = false });
        }

        private static ActionSpec PoliticsMediateClanDispute(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            Clan target = FindSecondClanInKingdom(kingdom, claimant);
            return PoliticsSpec(ReignWorldActionType.PoliticsMediateClanDispute, c, "MediateClanDispute", kingdom, claimant, target, new JObject { ["durationDays"] = 30 });
        }

        private static ActionSpec PoliticsEncourageClanDefection(GauntletContext c)
        {
            Kingdom destination = FindActorKingdom();
            Clan clan = Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && !x.IsClanTypeMercenary && x.Kingdom != null && x.Kingdom != destination && x.Leader != null);
            return PoliticsSpec(ReignWorldActionType.PoliticsEncourageClanDefection, c, "EncourageClanDefection", destination, clan, null, new JObject { ["durationDays"] = 120, ["minimumStayDays"] = 10 });
        }

        private static ActionSpec PoliticsExileClan(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan clan = FindClaimantClan(kingdom);
            c.LastExiledClan = clan;
            return PoliticsSpec(ReignWorldActionType.PoliticsExileClan, c, "ExileClan", kingdom, clan, null, new JObject { ["durationDays"] = 180 });
        }

        private static ActionSpec PoliticsRestoreExiledClan(GauntletContext c)
        {
            Kingdom kingdom = FindActorKingdom() ?? FindPoliticsKingdom();
            Clan clan = c.LastExiledClan ?? Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Kingdom == null && x.Leader != null);
            return PoliticsSpec(ReignWorldActionType.PoliticsRestoreExiledClan, c, "RestoreExiledClan", kingdom, clan, null, new JObject { ["durationDays"] = 120, ["minimumStayDays"] = 10 });
        }

        private static ActionSpec PoliticsInstallRulingClan(GauntletContext c)
        {
            Kingdom kingdom = FindPoliticsKingdom();
            Clan claimant = FindClaimantClan(kingdom);
            return PoliticsSpec(ReignWorldActionType.PoliticsInstallRulingClan, c, "InstallRulingClan", kingdom, claimant, null, new JObject());
        }

        private static ActionSpec PoliticsStartRulingClanRebellion(GauntletContext c)
        {
            return Skip("NPC rebellion starts are now tested through the saved weekly native-relationship roll; the explicit action is reserved for an in-person or mailed player declaration to their current ruler.");
        }

        private static ActionSpec FailureRetiredTemporaryTruce(GauntletContext c)
        {
            return ExpectedFailure(DiplomacySpec(ReignWorldActionType.DiplomacySignTemporaryTruce, c, "RetiredTemporaryTruce", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()))?.Action);
        }

        private static ActionSpec FailureRetiredTradeEmbargo(GauntletContext c)
        {
            return ExpectedFailure(DiplomacySpec(ReignWorldActionType.DiplomacyTradeEmbargo, c, "RetiredTradeEmbargo", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()))?.Action);
        }

        private static ActionSpec DiplomacyCaravanProtection(GauntletContext c)
        {
            return DiplomacySpec(ReignWorldActionType.DiplomacyCaravanProtectionAgreement, c, "CaravanProtection", FindActorKingdom(), FindOtherLiveKingdom(FindActorKingdom()), null, new JObject { ["durationDays"] = 60 });
        }

        private static ActionSpec FailureIllegalVillageSurrender(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            Settlement village = Settlement.All.FirstOrDefault(x => x != null && x.IsVillage && x.MapFaction == target);
            if (village == null)
            {
                return Skip("Need target-owned village.");
            }

            EnsureWar(actor, target, null);
            return ExpectedFailure(WorldAction(ReignWorldActionType.DiplomacyDemandSettlementPeace, c, "IllegalVillageSurrender", actor, target, null, null, village, null, null, null, new JObject { ["settlementIds"] = new JArray(village.StringId) }));
        }

        private static ActionSpec FailureMissingPrisonerPackage(GauntletContext c)
        {
            Kingdom actor = FindActorKingdom();
            Kingdom target = FindOtherLiveKingdom(actor);
            return ExpectedFailure(WorldAction(ReignWorldActionType.DiplomacyPackage, c, "MissingPrisonerPackage", actor, target, null, null, null, null, null, "missing_prisoner_hero", new JObject { ["prisonerHeroStringId"] = "missing_prisoner_hero" }));
        }

        private static ActionSpec FailureUnaffordableGoldTransfer(GauntletContext c)
        {
            if (c.ActorHero == null)
            {
                return Skip("Need actor hero.");
            }

            return ExpectedFailure(Action(ReignWorldActionType.RegularGiveGoldToPlayer, c, "UnaffordableGoldTransfer", c.ActorHero, Hero.MainHero, null, new JObject { ["gold"] = 2000000000 }));
        }

        private static ActionSpec FailureUnauthorizedSettlementTrade(GauntletContext c)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification && x.OwnerClan?.Leader != null
                && x.OwnerClan != Clan.PlayerClan);
            Hero unrelated = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero && x != settlement?.OwnerClan?.Leader
                && x.Clan != null);
            if (settlement == null || unrelated == null || Hero.MainHero == null)
            {
                return Skip("Need a non-player fief owner and an unrelated living NPC.");
            }

            JObject terms = SettlementTradeTerms(settlement, unrelated);
            ReignWorldActionRecord action = Action(ReignWorldActionType.RegularTradePackage,
                c, "UnauthorizedSettlementTrade", unrelated, Hero.MainHero, settlement,
                terms);
            BindDialogueSettlementTestAuthorization(action, unrelated, terms);
            return ExpectedFailure(action);
        }

        private static ActionSpec FailureTamperedSettlementConsent(GauntletContext c)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification && x.OwnerClan?.Leader != null
                && x.OwnerClan != Clan.PlayerClan);
            Hero owner = settlement?.OwnerClan?.Leader;
            if (settlement == null || owner == null || Hero.MainHero == null)
            {
                return Skip("Need a transferable non-player fief with a living clan leader.");
            }

            JObject terms = SettlementTradeTerms(settlement, owner);
            ReignWorldActionRecord action = Action(ReignWorldActionType.RegularTradePackage,
                c, "TamperedSettlementConsent", owner, Hero.MainHero, settlement, terms);
            BindDialogueSettlementTestAuthorization(action, owner, terms);
            terms["gold"] = 2;
            action.TermsJson = terms.ToString(Formatting.None);
            return ExpectedFailure(action);
        }

        private static JObject SettlementTradeTerms(Settlement settlement, Hero source)
        {
            return new JObject
            {
                ["targetSettlementStringId"] = settlement.StringId,
                ["settlementToHeroStringId"] = Hero.MainHero.StringId,
                ["settlementFromHeroStringId"] = source.StringId,
                ["settlementFromClanStringId"] = settlement.OwnerClan.StringId,
                ["settlementAuthorizingHeroStringId"] = source.StringId,
                ["settlementAuthorityKind"] = "owner_clan_leader",
                ["gold"] = 1,
                ["goldFromHeroStringId"] = Hero.MainHero.StringId,
                ["goldToHeroStringId"] = source.StringId,
                ["bypassFairness"] = true
            };
        }

        private static void BindDialogueSettlementTestAuthorization(
            ReignWorldActionRecord action, Hero acceptedBy, JObject terms)
        {
            action.Source = "test_lab_action_gate";
            action.AuthorizationMode = "dialogue_acceptance";
            action.AcceptedByHeroStringId = acceptedBy.StringId;
            action.NegotiationId = "gauntlet-settlement-authority-" + Guid.NewGuid().ToString("N");
            action.NegotiatedCommand = "trade_package";
            action.RequiresAcceptance = false;
            action.TermsJson = terms.ToString(Formatting.None);
            action.TermsHash = ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand,
                action.TermsJson);
        }

        private static ActionSpec FailureUnknownAction(GauntletContext c)
        {
            return ExpectedFailure(WorldAction(ReignWorldActionType.Unknown, c, "UnknownAction", null, null, c.ActorHero, null, null, null, null, null, new JObject()));
        }

        private static ActionSpec DiplomacySpec(ReignWorldActionType type, GauntletContext c, string label, Kingdom actor, Kingdom target, Settlement settlement = null, JObject terms = null, Hero targetHero = null)
        {
            if (actor == null || target == null)
            {
                return Skip("Need two live kingdoms.");
            }

            return Spec(WorldAction(type, c, label, actor, target, null, targetHero, settlement, null, null, null, terms ?? new JObject()));
        }

        private static ActionSpec PoliticsSpec(ReignWorldActionType type, GauntletContext c, string label, Kingdom kingdom, Clan actorClan, Clan targetClan, JObject terms)
        {
            if (kingdom == null || actorClan == null)
            {
                return Skip("Need kingdom and actor clan.");
            }

            if ((type == ReignWorldActionType.PoliticsMarriageAlliance || type == ReignWorldActionType.PoliticsMediateClanDispute) && targetClan == null)
            {
                return Skip("Need target clan.");
            }

            return Spec(WorldAction(type, c, label, kingdom, null, null, null, null, actorClan, targetClan, null, terms ?? new JObject()));
        }

        private static ActionSpec ExpectedFailure(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return Skip("No invalid action could be constructed.");
            }

            return new ActionSpec { Action = action, ExpectValidationFailure = true };
        }

        private static ReignWorldActionRecord WorldAction(
            ReignWorldActionType type,
            GauntletContext context,
            string label,
            Kingdom actorKingdom,
            Kingdom targetKingdom,
            Hero actor,
            Hero target,
            Settlement settlement,
            Clan actorClan,
            Clan targetClan,
            string targetHeroId,
            JObject terms,
            int minimumTroops = 0)
        {
            return new ReignWorldActionRecord
            {
                Type = type,
                Source = "gauntlet:" + context.CorrelationId,
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                TargetHeroStringId = target?.StringId ?? targetHeroId ?? string.Empty,
                ActorClanStringId = actorClan?.StringId ?? actor?.Clan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? target?.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = actorKingdom?.StringId ?? actor?.Clan?.Kingdom?.StringId ?? actorClan?.Kingdom?.StringId ?? string.Empty,
                TargetKingdomStringId = targetKingdom?.StringId ?? target?.Clan?.Kingdom?.StringId ?? targetClan?.Kingdom?.StringId ?? string.Empty,
                TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = "ReignBeta Action Gauntlet: " + label,
                RequiresAcceptance = false,
                MaxAttempts = 1,
                MinimumTroops = minimumTroops
            };
        }

        private static Kingdom FindActorKingdom()
        {
            return Clan.PlayerClan?.Kingdom
                ?? Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.Leader != null);
        }

        private static Kingdom FindOtherLiveKingdom(Kingdom actor, Kingdom exclude = null)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.Leader != null && x != actor && x != exclude);
        }

        private static Kingdom FindKingdomWithFortificationNot(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null
                && !x.IsEliminated
                && x.Leader != null
                && x != actor
                && Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x));
        }

        private static Settlement FindFortificationOwnedBy(Kingdom kingdom)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsFortification && x.MapFaction == kingdom && !x.IsUnderSiege);
        }

        private static void EnsurePeace(Kingdom left, Kingdom right, List<string> notes)
        {
            if (left == null || right == null || left == right || !left.IsAtWarWith(right))
            {
                return;
            }

            MakePeaceAction.ApplyByKingdomDecision(left, right, 0, 0);
            notes?.Add("peace made between " + left.InformalName + " and " + right.InformalName);
        }

        private static void EnsureLeaderGold(Kingdom kingdom, int minimumGold)
        {
            if (kingdom?.Leader != null)
            {
                EnsureHeroGold(kingdom.Leader, minimumGold);
            }
        }

        private static Hero EnsurePrisonerFrom(Kingdom prisonerKingdom, Kingdom holderKingdom, params Hero[] exclude)
        {
            if (prisonerKingdom == null || holderKingdom == null)
            {
                return null;
            }

            Hero prisoner = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !exclude.Contains(x)
                && !x.IsDead
                && x.Clan?.Kingdom == prisonerKingdom
                && x != prisonerKingdom.Leader)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && !exclude.Contains(x)
                    && !x.IsDead
                    && x.IsLord
                    && x != prisonerKingdom.Leader);

            if (prisoner == null)
            {
                return null;
            }

            if (!prisoner.IsPrisoner)
            {
                PartyBase holder = holderKingdom == Clan.PlayerClan?.Kingdom && PartyBase.MainParty != null
                    ? PartyBase.MainParty
                    : holderKingdom.Leader?.PartyBelongedTo?.Party;
                if (holder == null)
                {
                    return null;
                }

                TakePrisonerAction.Apply(holder, prisoner);
            }

            return prisoner;
        }

        private static Kingdom FindPoliticsKingdom()
        {
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x.RulingClan != null && FindClaimantClan(x) != null)
                ?? FindActorKingdom();
        }

        private static Clan FindClaimantClan(Kingdom kingdom)
        {
            return Clan.All.FirstOrDefault(x => x != null
                && x.Kingdom == kingdom
                && x != kingdom?.RulingClan
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Leader != null);
        }

        private static Clan FindSecondClanInKingdom(Kingdom kingdom, Clan except)
        {
            return Clan.All.FirstOrDefault(x => x != null
                && x.Kingdom == kingdom
                && x != except
                && x != kingdom?.RulingClan
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Leader != null);
        }

        private static void EnsurePlayerKingdomState(List<string> notes)
        {
            Hero player = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            if (player == null || playerClan == null || MobileParty.MainParty == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                return;
            }

            EnsureHeroGold(player, 1000000);
            notes?.Add("player kingdom gold ensured");

            CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
            if (cataphract != null && MobileParty.MainParty.MemberRoster.TotalManCount < 150)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 150);
                notes?.Add("player troops ensured");
            }

            if (DefaultItems.Grain != null && MobileParty.MainParty.ItemRoster.GetItemNumber(DefaultItems.Grain) < 300)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                notes?.Add("player grain ensured");
            }

            int requiredRenown = TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(4);
            if (playerClan.Renown < requiredRenown)
            {
                GainRenownAction.Apply(player, requiredRenown - playerClan.Renown + 5f, true);
                notes?.Add("player clan tier ensured");
            }

            Settlement zeonica = FindArenaSettlement();
            if (zeonica != null && zeonica.IsTown && zeonica.OwnerClan != playerClan)
            {
                ChangeOwnerOfSettlementAction.ApplyByGift(zeonica, player);
                notes?.Add("arena town granted to player");
            }

            if (playerClan.Leader != player)
            {
                playerClan.SetLeader(player);
                notes?.Add("player set as clan leader");
            }

            if (playerClan.Kingdom == null)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.KingdomManager.CreateKingdom(playerClan.Name, playerClan.InformalName, playerClan.Culture, playerClan);
                notes?.Add("player kingdom created");
            }

            if (playerClan.Kingdom != null && playerClan.Kingdom.RulingClan != playerClan)
            {
                ChangeRulingClanAction.Apply(playerClan.Kingdom, playerClan);
                notes?.Add("player clan made ruling clan");
            }
        }

        private static void EnsurePlayerClanIndependentForMembershipTest()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || playerClan.Kingdom == null)
            {
                return;
            }

            if (playerClan.IsUnderMercenaryService)
            {
                ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(playerClan, true);
                return;
            }

            ChangeKingdomAction.ApplyByLeaveKingdom(playerClan, true);
        }
    }
}
