using System.Linq;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.World
{
    public static class ReignStrategyExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.ValidationFailed(validationFailure);
            }
            if (!ReignGovernmentActionGate.TryAuthorize(action, out ReignActionResult governmentResult))
                return governmentResult;

            MobileParty party = ReignObjectResolver.FindHeroParty(action.ActorHeroStringId);
            Settlement target = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.StrategyRecruitAndRecover:
                    return ExecuteRecruitAndRecover(action, party, target);

                case ReignWorldActionType.StrategyFormArmy:
                    return ExecuteFormArmy(action, party, target);

                case ReignWorldActionType.StrategyAttackSettlement:
                    return ExecuteAttackSettlement(action, party, target);

                case ReignWorldActionType.StrategyCaptureSettlement:
                    return ExecuteCaptureSettlement(action, party, target);

                default:
                    return ReignActionResult.FailTerminal("Unsupported strategy action type: " + action.Type, "unsupported_action", "unsupported_action");
            }
        }

        private static ReignActionResult ExecuteRecruitAndRecover(ReignWorldActionRecord action, MobileParty party, Settlement target)
        {
            if (HasEnoughTroops(action, party) && HasEnoughFood(party))
            {
                return ReignActionResult.NoOp(party.LeaderHero.Name + " is ready for orders.")
                    .WithChangedEntity("hero", party.LeaderHero.StringId, party.LeaderHero.Name.ToString(), "ready_for_orders")
                    .WithDiagnostic("troops", party.MemberRoster.TotalManCount.ToString())
                    .WithDiagnostic("foodDays", party.GetNumDaysForFoodToLast().ToString("0.0"));
            }

            Settlement recovery = FindFriendlyRecoverySettlement(party, target);
            if (recovery == null)
            {
                return ReignActionResult.Retry("No friendly recovery settlement found.", "no_recovery_settlement", 0.5f)
                    .WithDiagnostic("troops", party.MemberRoster.TotalManCount.ToString())
                    .WithDiagnostic("foodDays", party.GetNumDaysForFoodToLast().ToString("0.0"));
            }

            party.SetMoveGoToSettlement(recovery, party.NavigationCapability, false);
            return ReignActionResult.Progress(party.LeaderHero.Name + " is recruiting and recovering at " + recovery.Name + ".")
                .WithEffect("movement_order", "settlement", recovery.StringId, recovery.Name.ToString(), "order=recruit_and_recover")
                .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "movement_order_changed")
                .WithDiagnostic("troops", party.MemberRoster.TotalManCount.ToString())
                .WithDiagnostic("foodDays", party.GetNumDaysForFoodToLast().ToString("0.0"));
        }

        private static ReignActionResult ExecuteFormArmy(ReignWorldActionRecord action, MobileParty party, Settlement target)
        {
            Kingdom kingdom = party.MapFaction as Kingdom;
            if (kingdom == null)
            {
                return ReignActionResult.ValidationFailed("Actor party is not in a kingdom.");
            }

            if (party.Army != null && party.Army.LeaderParty == party)
            {
                if (party.Army.IsWaitingForArmyMembers())
                {
                    return ReignActionResult.Progress(party.LeaderHero.Name + " is waiting at the native gathering point for the army to assemble.")
                        .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "army_gathering")
                        .WithDiagnostic("armyParties", party.Army.Parties.Count.ToString());
                }

                return ReignActionResult.NoOp(party.LeaderHero.Name + " leads an assembled army.")
                    .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "army_assembled")
                    .WithDiagnostic("armyParties", party.Army.Parties.Count.ToString());
            }

            bool naturallyEligible = TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(party, out MBList<MobileParty> possibleMembers);
            if (!naturallyEligible)
            {
                float influence = party.LeaderHero?.Clan?.Influence ?? 0f;
                string message = influence <= 100f
                    ? party.LeaderHero.Name + " cannot form a native army: the clan has " + influence.ToString("0.0") + " influence, and Bannerlord requires more than 100 before member invitation costs are considered."
                    : party.LeaderHero.Name + " cannot form a native army under the current Bannerlord eligibility rules (leadership, food, party strength, war, eligible members, and their variable influence costs all apply).";
                return ReignActionResult.ValidationFailed(message)
                    .WithDiagnostic("clanInfluence", influence.ToString("0.0"))
                    .WithDiagnostic("possibleMemberCount", possibleMembers == null ? "0" : possibleMembers.Count.ToString())
                    .WithDiagnostic("nativeCanLordCreateArmy", "False");
            }

            kingdom.CreateArmy(party.LeaderHero, target, Army.ArmyTypes.Besieger, possibleMembers);
            if (party.Army == null || party.Army.LeaderParty != party)
            {
                return ReignActionResult.FailTerminal("Bannerlord did not create a native army for the commanded lord.", "army_creation_failed", "state_verification")
                    .WithDiagnostic("naturallyEligible", naturallyEligible.ToString())
                    .WithDiagnostic("possibleMemberCount", possibleMembers == null ? "0" : possibleMembers.Count.ToString());
            }

            if (party.Army.IsWaitingForArmyMembers())
            {
                return ReignActionResult.Progress(party.LeaderHero.Name + " created an army and is waiting at the native gathering point for invited parties.")
                    .WithEffect("army_created", "settlement", target.StringId, target.Name.ToString(), "leaderHeroId=" + party.LeaderHero.StringId)
                    .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "army_gathering")
                    .WithDiagnostic("naturallyEligible", naturallyEligible.ToString())
                    .WithDiagnostic("possibleMemberCount", possibleMembers == null ? "0" : possibleMembers.Count.ToString());
            }

            return ReignActionResult.Done(party.LeaderHero.Name + " created and assembled an army for " + target.Name + ".")
                .WithEffect("army_created", "settlement", target.StringId, target.Name.ToString(), "leaderHeroId=" + party.LeaderHero.StringId)
                .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "army_assembled")
                .WithDiagnostic("naturallyEligible", naturallyEligible.ToString())
                .WithDiagnostic("possibleMemberCount", possibleMembers == null ? "0" : possibleMembers.Count.ToString());
        }

        private static ReignActionResult ExecuteAttackSettlement(ReignWorldActionRecord action, MobileParty party, Settlement target)
        {
            if (target.MapFaction == party.MapFaction)
            {
                return ReignActionResult.NoOp(target.Name + " is no longer hostile.")
                    .WithChangedEntity("settlement", target.StringId, target.Name.ToString(), "already_friendly");
            }

            if (target.IsFortification)
            {
                party.SetMoveBesiegeSettlement(target, party.NavigationCapability);
                return ReignActionResult.Progress(party.LeaderHero.Name + " is moving to besiege " + target.Name + ".")
                    .WithEffect("movement_order", "settlement", target.StringId, target.Name.ToString(), "order=besiege")
                    .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "movement_order_changed");
            }

            party.SetMoveRaidSettlement(target, party.NavigationCapability, false);
            return ReignActionResult.Progress(party.LeaderHero.Name + " is moving to raid " + target.Name + ".")
                .WithEffect("movement_order", "settlement", target.StringId, target.Name.ToString(), "order=raid")
                .WithChangedEntity("party", party.StringId, party.Name?.ToString() ?? string.Empty, "movement_order_changed");
        }

        private static ReignActionResult ExecuteCaptureSettlement(ReignWorldActionRecord action, MobileParty party, Settlement target)
        {
            if (target.MapFaction == party.MapFaction)
            {
                action.PlanStage = ReignStrategicPlanStage.Completed;
                return ReignActionResult.Done(target.Name + " has joined " + party.MapFaction.Name + ".")
                    .WithChangedEntity("settlement", target.StringId, target.Name.ToString(), "captured")
                    .WithEffect("settlement_captured", "settlement", target.StringId, target.Name.ToString(), "ownerFactionId=" + (party.MapFaction?.StringId ?? string.Empty));
            }

            if (action.PlanStage == ReignStrategicPlanStage.None)
            {
                action.PlanStage = ReignStrategicPlanStage.ValidateWar;
            }

            switch (action.PlanStage)
            {
                case ReignStrategicPlanStage.ValidateWar:
                    if (target.MapFaction == null || party.MapFaction == null || !target.MapFaction.IsAtWarWith(party.MapFaction))
                    {
                        action.PlanStage = ReignStrategicPlanStage.Failed;
                        return ReignActionResult.ValidationFailed("Target is not hostile.");
                    }

                    action.PlanStage = ReignStrategicPlanStage.RecruitAndRecover;
                    return ReignActionResult.Progress("War validated; recruiting and recovery begins.")
                        .WithDiagnostic("planStage", action.PlanStage.ToString());

                case ReignStrategicPlanStage.RecruitAndRecover:
                    if (!HasEnoughTroops(action, party) || !HasEnoughFood(party))
                    {
                        return ExecuteRecruitAndRecover(action, party, target);
                    }

                    action.PlanStage = ReignStrategicPlanStage.FormArmy;
                    return ReignActionResult.Progress("Party is ready; attempting army formation.")
                        .WithDiagnostic("planStage", action.PlanStage.ToString());

                case ReignStrategicPlanStage.FormArmy:
                    ReignActionResult armyResult = ExecuteFormArmy(action, party, target);
                    if (!armyResult.Success || !armyResult.Completed)
                    {
                        return armyResult.WithDiagnostic("planStage", action.PlanStage.ToString());
                    }
                    action.PlanStage = ReignStrategicPlanStage.MoveToTarget;
                    return ReignActionResult.Progress(armyResult.Message)
                        .WithDiagnostic("planStage", action.PlanStage.ToString());

                case ReignStrategicPlanStage.MoveToTarget:
                    action.PlanStage = ReignStrategicPlanStage.SiegeOrAssault;
                    return ExecuteAttackSettlement(action, party, target);

                case ReignStrategicPlanStage.SiegeOrAssault:
                    if (target.MapFaction == party.MapFaction)
                    {
                        action.PlanStage = ReignStrategicPlanStage.Completed;
                        return ReignActionResult.Done(target.Name + " has been captured.")
                            .WithChangedEntity("settlement", target.StringId, target.Name.ToString(), "captured")
                            .WithEffect("settlement_captured", "settlement", target.StringId, target.Name.ToString(), "ownerFactionId=" + (party.MapFaction?.StringId ?? string.Empty));
                    }

                    if (party.DefaultBehavior != AiBehavior.BesiegeSettlement || party.TargetSettlement != target)
                    {
                        party.SetMoveBesiegeSettlement(target, party.NavigationCapability);
                    }

                    return ReignActionResult.Progress(party.LeaderHero.Name + " is maintaining pressure on " + target.Name + ".")
                        .WithEffect("movement_order", "settlement", target.StringId, target.Name.ToString(), "order=maintain_siege_pressure")
                        .WithDiagnostic("planStage", action.PlanStage.ToString());

                default:
                    return ReignActionResult.FailTerminal("Capture plan is in an invalid stage.", "invalid_plan_stage", "invalid_state");
            }
        }

        private static bool HasEnoughTroops(ReignWorldActionRecord action, MobileParty party)
        {
            int minimum = action.MinimumTroops <= 0 ? 40 : action.MinimumTroops;
            return party.MemberRoster.TotalManCount >= minimum && party.PartySizeRatio >= 0.5f;
        }

        private static bool HasEnoughFood(MobileParty party)
        {
            return party.Food > 0f && party.GetNumDaysForFoodToLast() >= 3;
        }

        private static Settlement FindFriendlyRecoverySettlement(MobileParty party, Settlement target)
        {
            IFaction faction = party.MapFaction;
            if (faction == null)
            {
                return null;
            }

            return Settlement.All
                .Where(x => x != null
                    && !x.IsHideout
                    && !x.IsUnderSiege
                    && x.MapFaction == faction
                    && (x.IsTown || x.IsCastle || x.IsVillage))
                .OrderBy(x => target == null ? party.GetPosition2D.DistanceSquared(x.GetPosition2D) : target.GetPosition2D.DistanceSquared(x.GetPosition2D))
                .FirstOrDefault();
        }
    }
}
