using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Rebellion.Campaign
{
    public sealed partial class ReignRebellionCampaignBehavior
    {
        /// <summary>
        /// Explicit player declaration. NPC outbreaks are intentionally rejected here and
        /// can originate only from the saved weekly relationship roll.
        /// </summary>
        public bool TryStartDirectedMovement(Kingdom kingdom, Clan leaderClan, IEnumerable<Clan> supporters, out string result)
            => TryStartDirectedMovement(kingdom, leaderClan, supporters, out result, null);

        private bool TryStartDirectedMovement(Kingdom kingdom, Clan leaderClan, IEnumerable<Clan> supporters,
            out string result, Action<ReignRebellionMovementRecord, Kingdom> verifiedFounderFixture)
        {
            result = string.Empty;
            result = DeclarationPreflightFailure(kingdom, leaderClan, kingdom?.Leader);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return false;
            }

            ExpireChangedPreparationAuthority(CurrentDay());
            ReignRebellionPlotRecord preparedPlot = FindActivePlayerPlot();
            ReignRebellionMovementRecord movement = CreateMovement(kingdom, leaderClan, true, WeekIndex(CurrentDay()));
            AddOrUpdateMember(movement, leaderClan, "rebel", "player_declaration",
                leaderClan.Leader.GetRelation(leaderClan.Leader), leaderClan.Leader.GetRelation(kingdom.Leader));
            foreach (Clan clan in kingdom.Clans.Where(x => x != null && x != leaderClan).ToList())
                AddOrUpdateMember(movement, clan, "loyalist", "declaration_loyalist",
                    clan.Leader?.GetRelation(leaderClan.Leader) ?? 0, clan.Leader?.GetRelation(kingdom.Leader) ?? 0);
            ReignRebellionPlotRecord priorTransition = _preparationInNativeTransition;
            _preparationInNativeTransition = preparedPlot;
            try
            {
                if (verifiedFounderFixture == null) StartCivilWar(movement);
                else StartCivilWarCore(movement, verifiedFounderFixture);
            }
            finally { _preparationInNativeTransition = priorTransition; }
            if (!IsCivilWar(movement))
            {
                result = Same(movement.ExecutionPhase, "outbreak_recovery_pending")
                    ? "The declaration stopped after native campaign changes may have begun. This rebellion is held for review."
                    : "The declaration could not create a valid rebel kingdom.";
                return false;
            }
            if (preparedPlot != null && !Same(preparedPlot.Status, "active_rebellion"))
            {
                preparedPlot.Status = "active_rebellion";
                preparedPlot.Resolution = "player_declared";
                preparedPlot.MovementId = movement.MovementId;
                preparedPlot.UpdatedDay = CurrentDay();
                ActivatePreparedPledges(preparedPlot, movement);
            }
            result = "You challenged " + (kingdom.Leader?.Name?.ToString() ?? "the ruler") + " and began a rebellion for the throne.";
            return true;
        }

        public string DeclarationPreflightFailure(Kingdom kingdom, Clan leaderClan, Hero addressedRuler)
        {
            if (!PreparationRealmReady(kingdom))
                return "The current realm and ruler are not ready for a declaration.";
            bool playerLeader = leaderClan == Clan.PlayerClan && leaderClan?.Leader == Hero.MainHero;
            bool validVassal = kingdom != null && leaderClan?.Kingdom == kingdom
                && leaderClan != kingdom.RulingClan && leaderClan.Leader != null
                && !leaderClan.Leader.IsDead && !leaderClan.Leader.IsPrisoner;
            string failure = DirectedMovementPreflightFailure(playerLeader, validVassal,
                kingdom != null && HasActiveMovement(kingdom), kingdom != null && IsRealmOnCooldown(kingdom, CurrentDay()));
            if (failure.Length > 0) return failure;
            if (addressedRuler == null || addressedRuler != kingdom.Leader)
                return "The declaration must address the current ruler of the challenged realm.";
            var plot = FindActivePlayerPlot();
            if (plot != null && (!Same(plot.ParentKingdomStringId, kingdom.StringId) || !Same(plot.RulerHeroStringId, addressedRuler.StringId)))
                return "The saved rebellion preparation belongs to a different ruler or realm.";
            return "";
        }

        private static string DirectedMovementPreflightFailure(bool playerLeader,
            bool validVassal, bool activeMovement, bool realmOnCooldown)
        {
            if (!playerLeader)
                return "NPC rebellions can begin only from their authoritative weekly relationship roll.";
            if (!validVassal)
                return "You must be the living, free leader of a non-ruling vassal clan to challenge the ruler.";
            if (activeMovement) return "This kingdom already has an active rebellion.";
            if (realmOnCooldown) return "This realm is still within its 63-day post-rebellion cooldown.";
            return string.Empty;
        }

        public bool TryRecruitLord(Hero recruiter, Hero targetLord, out string result)
        {
            result = string.Empty;
            if (recruiter != Hero.MainHero || Clan.PlayerClan?.Leader != recruiter)
            {
                result = "Only the player clan leader can recruit lords to a player-led rebellion.";
                return false;
            }
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && x.IsPlayerLed
                && Same(x.LeaderHeroStringId, recruiter.StringId));
            Kingdom rebels = FindKingdom(movement?.RebelKingdomStringId);
            Clan targetClan = targetLord?.Clan;
            if (movement == null || rebels == null)
            {
                result = "You must declare and lead an active rebellion before recruiting lords.";
                return false;
            }
            if (!CanMutateLiveCivilWar(movement))
            {
                result = "Lords can join only while the exact civil war is active and no recognition owns it.";
                return false;
            }
            if (targetLord == null || !targetLord.IsLord || targetLord.IsDead || targetLord.IsPrisoner
                || targetClan == null || targetClan.IsEliminated || targetClan == Clan.PlayerClan)
            {
                result = "The accepting lord must be a living, free member of an active noble clan.";
                return false;
            }
            if (Same(targetClan.StringId, FindHero(movement.OriginalRulerHeroStringId)?.Clan?.StringId))
            {
                result = "The ruler being challenged cannot be recruited into the rebellion against their own rule.";
                return false;
            }
            ReignRebellionMembershipRecord existing = MembersOf(movement).FirstOrDefault(x => Same(x.ClanStringId, targetClan.StringId));
            if (existing != null && Same(existing.Side, "rebel"))
            {
                result = targetClan.Name + " is already committed to the rebellion.";
                return false;
            }

            string originalKingdom = targetClan.Kingdom?.StringId ?? string.Empty;
            ReignRebellionMembershipRecord member = AddOrUpdateMember(movement, targetClan, "rebel", "player_recruitment",
                targetLord.GetRelation(recruiter), targetLord.GetRelation(FindHero(movement.OriginalRulerHeroStringId)));
            if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId)) member.OriginalKingdomStringId = originalKingdom;
            RememberOriginalKingdom(movement, targetClan.StringId, originalKingdom);
            MoveClanToKingdom(targetClan, rebels);
            member.IsCommitted = true;
            member.PlayerChoiceRequired = false;
            member.LastSwitchDay = CurrentDay();
            EmitHistory(movement, "civil_war_recruitment", "completed", targetLord.StringId,
                targetLord.Name + " committed " + targetClan.Name + " to the player-led rebellion.", false);
            result = targetLord.Name + " and " + targetClan.Name + " joined the rebellion until it ends.";
            return true;
        }

        public bool TryJoinNpcRebellion(Hero player, Hero rebelLeader, out string result)
        {
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && !x.IsPlayerLed
                && Same(x.LeaderHeroStringId, rebelLeader?.StringId));
            return TryJoinNpcRebellion(player, rebelLeader, movement, out result);
        }

        private bool TryJoinNpcRebellion(Hero player, Hero rebelLeader,
            ReignRebellionMovementRecord movement, out string result)
        {
            result = string.Empty;
            if (player != Hero.MainHero || Clan.PlayerClan?.Leader != player)
            {
                result = "Only the player clan leader can join a rebellion this way.";
                return false;
            }
            Kingdom parent = FindKingdom(movement?.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement?.RebelKingdomStringId);
            if (movement == null || movement.IsPlayerLed || rebelLeader == null
                || !Same(movement.LeaderHeroStringId, rebelLeader.StringId)
                || parent == null || rebels == null || Clan.PlayerClan?.Kingdom != parent)
            {
                result = "You can join only the active rebel leader opposing your current kingdom.";
                return false;
            }
            if (!CanMutateLiveCivilWar(movement))
            {
                result = "You can join only while the exact civil war is active and no recognition owns it.";
                return false;
            }
            if (player.IsDead || player.IsPrisoner)
            {
                result = "A dead or imprisoned clan leader cannot join the rebellion.";
                return false;
            }

            ReignRebellionMembershipRecord member = AddOrUpdateMember(movement, Clan.PlayerClan, "rebel", "player_joined_npc_rebellion",
                player.GetRelation(rebelLeader), player.GetRelation(FindHero(movement.OriginalRulerHeroStringId)));
            member.PlayerChoiceRequired = false;
            member.IsCommitted = true;
            MoveClanToKingdom(Clan.PlayerClan, rebels);
            EmitHistory(movement, "civil_war_allegiance", "completed", player.StringId,
                "The player clan joined " + rebelLeader.Name + "'s rebellion.", false);
            result = "Your clan joined " + rebelLeader.Name + " until the rebellion ends.";
            return true;
        }

        public bool TrySurrender(Hero surrenderingLeader, string movementId, out string result)
        {
            result = string.Empty;
            ReignRebellionMovementRecord movement = string.IsNullOrWhiteSpace(movementId)
                ? FindActiveMovementForHero(surrenderingLeader)
                : _movements.FirstOrDefault(x => IsCivilWar(x) && Same(x.MovementId, movementId));
            if (movement == null)
            {
                result = "No active rebellion matches this surrender.";
                return false;
            }
            if (HasRecognitionAttempt(movement))
            {
                result = "An earlier negotiated recognition owns this civil war and must be reconciled.";
                return false;
            }
            bool rebelLeaderSurrendered = Same(movement.LeaderHeroStringId, surrenderingLeader?.StringId);
            bool challengedRulerSurrendered = !rebelLeaderSurrendered
                && Same(movement.OriginalRulerHeroStringId, surrenderingLeader?.StringId);
            if (!rebelLeaderSurrendered && !challengedRulerSurrendered)
            {
                result = "Only the named rebel leader or challenged ruler can surrender the rebellion.";
                return false;
            }

            string cause = challengedRulerSurrendered ? "challenged_ruler_surrendered" : "rebel_leader_surrendered";
            if (!ResolveCivilWar(movement, challengedRulerSurrendered, cause))
            {
                result = OrdinaryReintegrationUnverified;
                return false;
            }

            result = challengedRulerSurrendered ? "The challenged ruler surrendered; the rebels won the throne."
                : "The rebel leader surrendered; the loyalists won the rebellion.";
            return true;
        }

        public bool SetPlayerClanSide(string movementId, bool joinRebels, out string result)
        {
            if (!joinRebels)
            {
                result = "The player remains a loyalist by staying in the parent kingdom; no action is required.";
                return true;
            }
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && Same(x.MovementId, movementId));
            return TryJoinNpcRebellion(Hero.MainHero, FindHero(movement?.LeaderHeroStringId), movement, out result);
        }

        private ReignRebellionMovementRecord CreateMovement(Kingdom kingdom, Clan leaderClan, bool playerLed, int weekIndex)
        {
            ReignRebellionMovementRecord movement = new ReignRebellionMovementRecord
            {
                ParentKingdomStringId = kingdom.StringId,
                LeaderClanStringId = leaderClan.StringId,
                LeaderHeroStringId = leaderClan.Leader.StringId,
                OriginalRulerHeroStringId = kingdom.Leader?.StringId ?? string.Empty,
                Objective = "claimant_takeover",
                Stage = "declared",
                CreatedDay = CurrentDay(),
                UpdatedDay = CurrentDay(),
                IsPlayerKingdom = kingdom == Clan.PlayerClan?.Kingdom,
                IsPlayerLed = playerLed,
                UltimatumIssued = true,
                RelationshipRulesMigrated = true,
                OutbreakWeekIndex = weekIndex,
                DemandJson = new JObject
                {
                    ["politicalResult"] = "claimant_takeover",
                    ["newRulingClanId"] = leaderClan.StringId,
                    ["trigger"] = playerLed ? "player_declaration"
                        : "weekly_effective_attitude_roll"
                }.ToString(Newtonsoft.Json.Formatting.None)
            };
            movement.CorrelationId = movement.MovementId;
            _movements.Add(movement);
            return movement;
        }

        // The cohort fixture reflects and patches this exact stable entry point.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartCivilWar(ReignRebellionMovementRecord movement) => StartCivilWarCore(movement, null);

        private void StartCivilWarCore(ReignRebellionMovementRecord movement,
            Action<ReignRebellionMovementRecord, Kingdom> verifiedFounderFixture)
        {
            if (movement == null || movement.ResolutionApplied || !Same(movement.Stage, "declared")
                || !Same(movement.ExecutionPhase, "idle") || !string.IsNullOrWhiteSpace(movement.RebelKingdomStringId)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Clan leaderClan = FindClan(movement.LeaderClanStringId);
            Hero founder = leaderClan?.Leader;
            Hero challengedRuler = parent?.Leader;
            if (!string.Equals(founder?.StringId, movement.LeaderHeroStringId, StringComparison.Ordinal)
                || !string.Equals(challengedRuler?.StringId, movement.OriginalRulerHeroStringId, StringComparison.Ordinal)
                || !HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, parent))
            {
                ResolveWithoutMutation(movement, "failed_preflight");
                return;
            }

            foreach (ReignRebellionMembershipRecord member in MembersOf(movement).ToList())
            {
                Clan clan = FindClan(member.ClanStringId);
                if (clan == null) continue;
                if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId))
                    member.OriginalKingdomStringId = clan.Kingdom?.StringId ?? string.Empty;
                RememberOriginalKingdom(movement, clan.StringId, member.OriginalKingdomStringId);
            }
            movement.ExecutionSnapshotJson = BuildExecutionSnapshot(movement, parent).ToString(Newtonsoft.Json.Formatting.None);
            movement.OriginalParentStrongholdIdsCsv = string.Join(",", Settlement.All
                .Where(x => x?.IsFortification == true && x.MapFaction == parent).Select(x => x.StringId));
            // Keep unverified effects attached to the same movement across saves.
            // Re-entry is allowed only from the untouched declaration above.
            movement.ExecutionPhase = "outbreak_applying";
            string step = "resident_escape";
            Kingdom rebels;
            try
            {
                if (!EvacuateResidentsBeforeOutbreak(movement, parent, out bool nativeEscapeAttempted))
                {
                    if (nativeEscapeAttempted) HoldOutbreak(movement, "resident_escape_unverified");
                    else ResolveWithoutMutation(movement, "failed_resident_escape");
                    return;
                }

                step = "realm_creation";
                if (!HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, parent))
                {
                    HoldOutbreak(movement, "outbreak_authority_changed");
                    return;
                }

                rebels = CreateRebelKingdom(movement, parent, challengedRuler, leaderClan, founder);
                if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels))
                {
                    HoldOutbreak(movement, "rebel_realm_unverified");
                    return;
                }

                // Only the guarded disposable campaign fixture supplies a callback.
                if (verifiedFounderFixture != null)
                {
                    verifiedFounderFixture(movement, rebels);
                    HoldOutbreak(movement, "fixture_verified_founder_interruption");
                    return;
                }

                var rebelMembers = MembersOf(movement).Where(x => Same(x.Side, "rebel")).ToList();
                var verifiedClans = new List<Clan>();
                foreach (ReignRebellionMembershipRecord member in rebelMembers)
                {
                    step = "clan_transfer:" + member.ClanStringId;
                    Clan clan = FindClan(member.ClanStringId);
                    if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels))
                    {
                        HoldOutbreak(movement, "outbreak_authority_changed");
                        return;
                    }
                    if (clan == null || clan.IsEliminated || (clan.Kingdom != parent && clan.Kingdom != rebels))
                    {
                        HoldOutbreak(movement, "outbreak_clan_authority_changed:" + member.ClanStringId);
                        return;
                    }
                    if (clan.Kingdom == parent) MoveClanToKingdom(clan, rebels);
                    // Native clan changes can notify listeners synchronously. Recheck the
                    // exact authority and every verified clan before moving another clan.
                    if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels))
                    {
                        HoldOutbreak(movement, "outbreak_authority_changed");
                        return;
                    }
                    verifiedClans.Add(clan);
                    if (!HasOutbreakAllegiance(verifiedClans, rebels))
                    {
                        HoldOutbreak(movement, step);
                        return;
                    }
                    member.IsCommitted = true;
                    member.LastSwitchDay = CurrentDay();
                }

                step = "war_declaration";
                if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels))
                {
                    HoldOutbreak(movement, "outbreak_authority_changed");
                    return;
                }
                movement.OriginalRebelStrongholdIdsCsv = string.Join(",", Settlement.All
                    .Where(x => x?.IsFortification == true && x.MapFaction == rebels).Select(x => x.StringId));
                if (!HasOutbreakAllegiance(verifiedClans, rebels))
                {
                    HoldOutbreak(movement, "outbreak_allegiance_unverified");
                    return;
                }
                if (!rebels.IsAtWarWith(parent))
                {
                    if (ReignServices.AI != null)
                        ReignServices.AI.DeclareWarWithOrigin(rebels, parent, "rebellion",
                            "Civil war began.", movement.MovementId, string.Empty, true);
                    else DeclareWarAction.ApplyByRebellion(rebels, parent);
                }
                step = "war_stance_verification";
                if (rebels.IsEliminated || parent.IsEliminated || !rebels.IsAtWarWith(parent))
                {
                    HoldOutbreak(movement, "war_stance_unverified");
                    return;
                }
                if (parent.Leader != challengedRuler || rebels.RulingClan != leaderClan
                    || leaderClan.Leader != founder || leaderClan.Kingdom != rebels
                    || !HasOutbreakAllegiance(verifiedClans, rebels))
                {
                    HoldOutbreak(movement, "outbreak_allegiance_unverified");
                    return;
                }
            }
            catch (Exception ex)
            {
                HoldOutbreak(movement, step);
                ReignLog.Warn("Rebellion outbreak native transition failed: " + ex.Message);
                return;
            }

            movement.Stage = "civil_war";
            movement.CivilWarStartedDay = CurrentDay();
            movement.UpdatedDay = CurrentDay();
            movement.ExecutionPhase = "verified";
            EmitHistory(movement, "civil_war_started", "completed", movement.LeaderHeroStringId,
                rebels.InformalName + " rose against " + parent.InformalName + " for the throne.", false);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] "
                + rebels.InformalName + " has begun a rebellion against " + parent.InformalName + "."));
        }

        private void HoldOutbreak(ReignRebellionMovementRecord movement, string reason)
        {
            movement.ExecutionPhase = "outbreak_recovery_pending";
            movement.ResolutionCause = reason;
            movement.UpdatedDay = CurrentDay();
            ReignLog.Warn("Rebellion " + movement.MovementId + " outbreak held at " + reason + ".");
        }

        private bool EvacuateResidentsBeforeOutbreak(ReignRebellionMovementRecord movement, Kingdom parent,
            out bool nativeEscapeAttempted)
        {
            nativeEscapeAttempted = false;
            var rebelClans = new HashSet<string>(MembersOf(movement)
                .Where(x => Same(x.Side, "rebel")).Select(x => x.ClanStringId), StringComparer.OrdinalIgnoreCase);
            List<Settlement> holdings = Settlement.All.Where(x => x?.IsFortification == true
                && x.MapFaction == parent).ToList();
            List<Hero> residents = holdings.SelectMany(x => x.HeroesWithoutParty).Where(x => x != null).Distinct().ToList();
            List<ReignRebellionResidentEscape.Escape> plan = ReignRebellionResidentEscape.Plan(movement.MovementId,
                residents.Select(hero => new ReignRebellionResidentEscape.Resident
                {
                    HeroId = hero.StringId,
                    ClanId = hero.Clan?.StringId,
                    SettlementId = hero.CurrentSettlement?.StringId,
                    HomeSettlementId = hero.HomeSettlement?.StringId,
                    IsRebel = rebelClans.Contains(hero.Clan?.StringId ?? string.Empty),
                    IsParticipant = hero.Clan?.Kingdom == parent,
                    IsNoble = hero.IsLord && !hero.IsWanderer && !hero.IsNotable,
                    IsAlive = hero.IsAlive,
                    IsPrisoner = hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null,
                    HasParty = hero.PartyBelongedTo != null,
                    IsPlayer = hero == Hero.MainHero
                }), holdings.Select(settlement => new ReignRebellionResidentEscape.Stronghold
                {
                    SettlementId = settlement.StringId,
                    OwnerClanId = settlement.OwnerClan?.StringId,
                    IsRebel = rebelClans.Contains(settlement.OwnerClan?.StringId ?? string.Empty),
                    IsSafe = settlement.SiegeEvent == null && settlement.Party.MapEvent == null
                }));
            var outcomes = new JArray();
            bool success = true;
            foreach (ReignRebellionResidentEscape.Escape escape in plan)
            {
                var outcome = new JObject
                {
                    ["heroId"] = escape.HeroId,
                    ["originSettlementId"] = escape.OriginSettlementId,
                    ["destinationSettlementId"] = escape.DestinationSettlementId,
                    ["side"] = escape.IsRebel ? "rebel" : "loyalist",
                    ["roll"] = escape.Roll,
                    ["escaped"] = false,
                    ["nativeAttempted"] = false
                };
                outcomes.Add(outcome);
                if (!escape.Escapes) continue; // The native declaration owns capture.
                Hero hero = FindHero(escape.HeroId);
                if (hero == null || !hero.IsAlive || hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null
                    || hero.PartyBelongedTo != null || hero == Hero.MainHero
                    || !Same(hero.CurrentSettlement?.StringId, escape.OriginSettlementId))
                {
                    ReignLog.Warn("Rebellion resident escape preflight changed for " + escape.HeroId + ".");
                    outcome["error"] = "resident_preflight_changed";
                    success = false;
                    break;
                }
                try
                {
                    Hero.CharacterStates previousState = hero.HeroState;
                    bool preserveDormantState = hero.IsNotSpawned || hero.IsDisabled;
                    outcome["previousHeroState"] = previousState.ToString();
                    outcome["originalGovernorshipId"] = hero.GovernorOf?.StringId ?? string.Empty;
                    outcome["nativeAttempted"] = true;
                    nativeEscapeAttempted = true;
                    if (hero.GovernorOf != null)
                    {
                        SaveResidentEscapeProgress(movement, outcomes, false);
                        ChangeGovernorAction.RemoveGovernorOf(hero);
                    }
                    Settlement destination = holdings.FirstOrDefault(x => Same(x.StringId, escape.DestinationSettlementId)
                        && x.MapFaction == parent && x.SiegeEvent == null && x.Party.MapEvent == null
                        && rebelClans.Contains(x.OwnerClan?.StringId ?? string.Empty) == escape.IsRebel);
                    outcome["destinationSettlementId"] = destination?.StringId ?? string.Empty;
                    outcome["wentIntoHiding"] = destination == null;
                    SaveResidentEscapeProgress(movement, outcomes, false);
                    if (destination != null && preserveDormantState)
                    {
                        // Native TeleportHeroAction activates dormant heroes.
                        // Move resident children without changing their lifecycle.
                        LeaveSettlementAction.ApplyForCharacterOnly(hero);
                        EnterSettlementAction.ApplyForCharacterOnly(hero, destination);
                    }
                    else if (destination != null) TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, destination);
                    else MakeHeroFugitiveAction.Apply(hero);
                    bool escaped = hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedToAsPrisoner == null
                        && hero.PartyBelongedTo == null && hero.GovernorOf == null
                        && (destination == null ? hero.CurrentSettlement == null && hero.HeroState == Hero.CharacterStates.Fugitive
                            : hero.CurrentSettlement == destination && (!preserveDormantState || hero.HeroState == previousState));
                    if (!escaped) throw new InvalidOperationException("Native resident relocation was not applied.");
                    outcome["escaped"] = true;
                    outcome["heroState"] = hero.HeroState.ToString();
                }
                catch (Exception ex)
                {
                    // Never knowingly declare war over a failed planned escape.
                    ReignLog.Warn("Rebellion resident escape failed for " + escape.HeroId + ": " + ex.Message);
                    outcome["error"] = ex.Message;
                    success = false;
                    break;
                }
            }
            SaveResidentEscapeProgress(movement, outcomes, success);
            ReignLog.Info("Rebellion " + movement.MovementId + " evacuated "
                + outcomes.Count(x => x.Value<bool>("escaped")) + " of " + plan.Count
                + " threatened residents before declaration.");
            return success;
        }

        private static void SaveResidentEscapeProgress(ReignRebellionMovementRecord movement, JArray outcomes, bool complete)
        {
            JObject snapshot = ParseObject(movement.ExecutionSnapshotJson);
            snapshot["residentEscape"] = new JObject
            {
                ["loyalistEscapePercent"] = ReignRebellionResidentEscape.LoyalistEscapePercent,
                ["completed"] = complete,
                ["outcomes"] = outcomes
            };
            movement.ExecutionSnapshotJson = snapshot.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool HasOutbreakAuthority(Kingdom parent, Hero challengedRuler,
            Clan leaderClan, Hero founder, Kingdom founderKingdom)
        {
            return parent != null && !parent.IsEliminated && challengedRuler != null && !challengedRuler.IsDead
                && parent.Leader == challengedRuler && leaderClan != null && !leaderClan.IsEliminated
                && leaderClan.Kingdom == founderKingdom && founder != null && !founder.IsDead
                && !founder.IsPrisoner && leaderClan.Leader == founder;
        }

        private static bool HasEstablishedOutbreakAuthority(Kingdom parent, Hero challengedRuler,
            Clan leaderClan, Hero founder, Kingdom rebels)
            => rebels != null && !rebels.IsEliminated && rebels.RulingClan == leaderClan
                && HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels);

        private static bool HasOutbreakAllegiance(IEnumerable<Clan> clans, Kingdom rebels)
            => clans.All(clan => !clan.IsEliminated && clan.Kingdom == rebels
                && FindClan(clan.StringId) == clan);

        private Kingdom CreateRebelKingdom(ReignRebellionMovementRecord movement, Kingdom parent,
            Hero challengedRuler, Clan leaderClan, Hero founder)
        {
            string id = TaleWorlds.CampaignSystem.Campaign.Current.CampaignObjectManager
                .FindNextUniqueStringId<Kingdom>("reign_rebels_" + SafeId(parent.StringId));
            if (!HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, parent)) return null;
            movement.RebelKingdomStringId = id;
            Kingdom rebels = Kingdom.CreateKingdom(id);
            if (rebels == null || rebels.IsEliminated
                || !HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, parent)) return null;
            TextObject name = new TextObject("{=ReignRebelRealm}{CLAN} Rebellion");
            name.SetTextVariable("CLAN", leaderClan.Name);
            TextObject informal = new TextObject(name.ToString());
            rebels.InitializeKingdom(name, informal, parent.Culture, leaderClan.Banner ?? parent.Banner,
                parent.Color2, parent.Color, leaderClan.HomeSettlement ?? parent.InitialHomeSettlement,
                parent.EncyclopediaText, parent.FormalName, parent.EncyclopediaRulerTitle);
            if (rebels.IsEliminated || !HasOutbreakAuthority(parent, challengedRuler, leaderClan, founder, parent)) return null;
            // Native creation installs the founder before notifying clan-change listeners.
            ChangeKingdomAction.ApplyByCreateKingdom(leaderClan, rebels, true);
            if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels)) return null;
            foreach (PolicyObject policy in parent.ActivePolicies.ToList())
            {
                if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels)) return null;
                if (!rebels.HasPolicy(policy)) rebels.AddPolicy(policy);
            }
            return rebels;
        }

        private void ReconcileCivilWar(ReignRebellionMovementRecord movement)
        {
            if (!IsCivilWar(movement) || HasRecognitionAttempt(movement)) return;
            // A native heir transition may complete death before the judgment's
            // political resolution resumes on the application tick.
            if (ReignRulerCampaignBehavior.HoldsCivilWar(movement.MovementId)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            Hero rebelLeader = FindHero(movement.LeaderHeroStringId);
            Hero challengedRuler = FindHero(movement.OriginalRulerHeroStringId);

            if (rebelLeader == null || rebelLeader.IsDead)
            {
                ResolveCivilWar(movement, false, "rebel_leader_killed");
                return;
            }
            if (challengedRuler == null || challengedRuler.IsDead)
            {
                ResolveCivilWar(movement, true, "challenged_ruler_killed");
                return;
            }
            if (parent != null && parent.Leader != challengedRuler)
            {
                ResolveCivilWar(movement, true, "challenged_ruler_deposed");
                return;
            }
            float now = CurrentDay();
            bool rebelLeaderHeld = IsHeldByFaction(rebelLeader, parent);
            bool challengedRulerHeld = IsHeldByFaction(challengedRuler, rebels);
            UpdateLeadershipCaptureState(movement, rebelLeaderHeld,
                challengedRulerHeld, now);
            if (!ReignRulerCampaignBehavior.Enabled(RulerOptions.CivilWars) && rebelLeaderHeld && LeadershipCaptureExpired(
                movement.RebelLeaderCapturedDay, now))
            {
                ResolveCivilWar(movement, false, "rebel_leader_captured");
                return;
            }
            if (!ReignRulerCampaignBehavior.Enabled(RulerOptions.CivilWars) && challengedRulerHeld && LeadershipCaptureExpired(
                movement.ChallengedRulerCapturedDay, now))
            {
                ResolveCivilWar(movement, true, "challenged_ruler_captured");
                return;
            }
            if (rebelLeaderHeld || challengedRulerHeld) return;

            // Generic/native peace never bypasses the leadership objective.
            if (parent != null && rebels != null && !parent.IsEliminated && !rebels.IsEliminated
                && !parent.IsAtWarWith(rebels) && !movement.ExecutionPhase.StartsWith("resolving", StringComparison.OrdinalIgnoreCase))
            {
                if (ReignServices.AI != null)
                    ReignServices.AI.DeclareWarWithOrigin(rebels, parent, "rebellion",
                        "Civil war resumed after external peace.", movement.MovementId, string.Empty, true);
                else DeclareWarAction.ApplyByRebellion(rebels, parent);
            }
        }

        internal void DescribeRulerJudgment(RulerJudgment judgment)
        {
            var movement = _movements.FirstOrDefault(m => IsCivilWar(m)
                && ((m.LeaderHeroStringId == judgment.PrisonerId && m.ParentKingdomStringId == judgment.VictorKingdomId)
                || (m.OriginalRulerHeroStringId == judgment.PrisonerId && m.RebelKingdomStringId == judgment.VictorKingdomId)));
            if (movement == null) return;
            judgment.CivilWar = true; judgment.MovementId = movement.MovementId;
            judgment.RebelVictory = movement.OriginalRulerHeroStringId == judgment.PrisonerId;
        }

        internal IEnumerable<(string ClanId, string LeaderId)> RebelClanAppealTargets(RulerJudgment judgment)
        {
            if (!judgment.CivilWar || judgment.RebelVictory || judgment.DefeatedLord) yield break;
            var movement = _movements.FirstOrDefault(m => IsCivilWar(m) && m.MovementId == judgment.MovementId
                && m.LeaderHeroStringId == judgment.PrisonerId && m.ParentKingdomStringId == judgment.VictorKingdomId
                && m.RebelKingdomStringId == judgment.DefeatedKingdomId);
            if (movement == null || movement.ResolutionApplied || HasRecognitionAttempt(movement)) yield break;

            foreach (var member in MembersOf(movement))
            {
                if (!Same(member.Side, "rebel") || member.FateApplied || Same(member.ClanStringId, movement.LeaderClanStringId)) continue;
                var clan = FindClan(member.ClanStringId);
                if (clan?.IsEliminated == false && Same(clan.Kingdom?.StringId, movement.RebelKingdomStringId)
                    && clan.Leader?.IsAlive == true && clan.Leader.StringId != judgment.PrisonerId)
                    yield return (clan.StringId, clan.Leader.StringId);
            }
        }

        internal void CompleteRulerJudgment(RulerJudgment judgment)
        {
            var movement = _movements.FirstOrDefault(m => m.MovementId == judgment.MovementId);
            if (movement == null || movement.ResolutionApplied) return;
            if (HasRecognitionAttempt(movement))
                throw new InvalidOperationException("An earlier negotiated recognition owns this civil war.");
            var parent = FindKingdom(movement.ParentKingdomStringId);
            var rebels = FindKingdom(movement.RebelKingdomStringId);
            var oldRuler = ReignRulerCampaignBehavior.HeroById(movement.OriginalRulerHeroStringId);
            if (!judgment.RebelVictory)
            {
                if (OrdinaryResolutionInProgress(movement)) throw new InvalidOperationException(OrdinaryReintegrationUnverified);
                foreach (var member in MembersOf(movement).Where(m => m.ClanStringId == ReignRulerCampaignBehavior.HeroById(judgment.PrisonerId)?.Clan?.StringId))
                { member.FateApplied = true; member.FateOutcome = judgment.Verdict.ToString(); }
                if (!ResolveCivilWar(movement, false, "ruler_judgment", judgment))
                    throw new InvalidOperationException(OrdinaryReintegrationUnverified);
                return;
            }
            movement.ExecutionPhase = "resolving_ruler_judgment";
            if (judgment.Verdict == RulerVerdict.Reunification)
            {
                if (oldRuler?.IsAlive == true)
                {
                    if (oldRuler.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(oldRuler);
                    var heir = oldRuler.Clan.AliveLords.Where(h => h != oldRuler && !h.IsChild).OrderByDescending(h => h.Age).ThenBy(h => h.StringId).FirstOrDefault();
                    if (heir != null) ChangeClanLeaderAction.ApplyWithSelectedNewLeader(oldRuler.Clan, heir);
                }
                if (parent.IsAtWarWith(rebels)) MakePeaceAction.Apply(parent, rebels);
                foreach (var clan in rebels.Clans.ToList()) MoveClanToKingdom(clan, parent);
                var claimant = FindClan(movement.LeaderClanStringId);
                ChangeRulingClanAction.Apply(parent, claimant);
                if (rebels.Clans.Count == 0) DestroyKingdomAction.Apply(rebels);
                movement.Resolution = "reunification";
            }
            else
            {
                if (judgment.Verdict == RulerVerdict.Independence)
                {
                    if (oldRuler?.IsPrisoner == true) EndCaptivityAction.ApplyByReleasedAfterBattle(oldRuler);
                    if (parent.IsAtWarWith(rebels)) MakePeaceAction.Apply(parent, rebels);
                }
                movement.Resolution = judgment.Verdict == RulerVerdict.RebelExecution ? "independence_execution" : "recognized_independence";
            }
            movement.WinningSide = "rebel"; movement.WinningLeaderHeroStringId = movement.LeaderHeroStringId;
            movement.ResolutionCause = "ruler_judgment"; FinalizeResolution(movement);
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            Kingdom captorKingdom = captor?.MapFaction as Kingdom;
            if (prisoner == null || !ReignRulerCampaignBehavior.IsSovereignCustody(captor, captorKingdom)) return;
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
            {
                if (Same(movement.LeaderHeroStringId, prisoner.StringId)
                    && Same(movement.ParentKingdomStringId, captorKingdom.StringId))
                {
                    MarkLeadershipCaptured(movement, true, CurrentDay());
                    continue;
                }
                if (Same(movement.OriginalRulerHeroStringId, prisoner.StringId)
                    && Same(movement.RebelKingdomStringId, captorKingdom.StringId))
                {
                    MarkLeadershipCaptured(movement, false, CurrentDay());
                }
            }
        }

        private void UpdateLeadershipCaptureState(
            ReignRebellionMovementRecord movement, bool rebelLeaderHeld,
            bool challengedRulerHeld, float now)
        {
            if (rebelLeaderHeld)
                MarkLeadershipCaptured(movement, true, now);
            else if (movement.RebelLeaderCapturedDay > 0f)
            {
                movement.RebelLeaderCapturedDay = NextLeadershipCaptureDay(
                    movement.RebelLeaderCapturedDay, false, now);
                movement.UpdatedDay = now;
                EmitHistory(movement, "civil_war_leader_released", "civil_war",
                    movement.LeaderHeroStringId,
                    "The rebel leader escaped or was released before captivity ended the civil war.", false);
            }

            if (challengedRulerHeld)
                MarkLeadershipCaptured(movement, false, now);
            else if (movement.ChallengedRulerCapturedDay > 0f)
            {
                movement.ChallengedRulerCapturedDay = NextLeadershipCaptureDay(
                    movement.ChallengedRulerCapturedDay, false, now);
                movement.UpdatedDay = now;
                EmitHistory(movement, "civil_war_ruler_released", "civil_war",
                    movement.OriginalRulerHeroStringId,
                    "The challenged ruler escaped or was released before captivity ended the civil war.", false);
            }
        }

        private static float NextLeadershipCaptureDay(float recordedDay, bool held, float now)
        {
            if (!held) return -1f;
            return recordedDay > 0f ? recordedDay : now;
        }

        private void MarkLeadershipCaptured(ReignRebellionMovementRecord movement,
            bool rebelLeader, float now)
        {
            if (movement == null) return;
            float recorded = rebelLeader ? movement.RebelLeaderCapturedDay
                : movement.ChallengedRulerCapturedDay;
            // Zero is also treated as unset for records loaded from saves made
            // before the capture-grace fields existed.
            float next = NextLeadershipCaptureDay(recorded, true, now);
            if (recorded > 0f && Math.Abs(next - recorded) < 0.001f) return;
            if (rebelLeader) movement.RebelLeaderCapturedDay = next;
            else movement.ChallengedRulerCapturedDay = next;
            movement.UpdatedDay = now;
            Hero capturedLeader = FindHero(rebelLeader ? movement.LeaderHeroStringId : movement.OriginalRulerHeroStringId);
            ReignServices.News?.Capture(capturedLeader, capturedLeader?.PartyBelongedToAsPrisoner, rebelLeader);
            EmitHistory(movement,
                rebelLeader ? "civil_war_leader_captured" : "civil_war_ruler_captured",
                "civil_war",
                rebelLeader ? movement.LeaderHeroStringId
                    : movement.OriginalRulerHeroStringId,
                ReignRulerCampaignBehavior.Enabled(RulerOptions.CivilWars)
                    ? (rebelLeader
                        ? "The rebel leader was captured; an escape or rescue may still change the outcome of the civil war."
                        : "The challenged ruler was captured; an escape or rescue may still change the outcome of the civil war.")
                    : (rebelLeader
                        ? "The rebel leader was captured; the rebellion has three campaign days to secure a rescue or escape."
                        : "The challenged ruler was captured; the loyalists have three campaign days to secure a rescue or escape."),
                false);
        }

        private static bool LeadershipCaptureExpired(float capturedDay,
            float currentDay)
        {
            return capturedDay > 0f
                && currentDay - capturedDay >= LeadershipCaptureGraceDays;
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            if (ReignServices.Ruler?.ApplyingNative == true) return;
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
            {
                if (Same(movement.LeaderHeroStringId, victim.StringId))
                {
                    ResolveCivilWar(movement, false, "rebel_leader_killed");
                    return;
                }
                if (Same(movement.OriginalRulerHeroStringId, victim.StringId))
                {
                    ResolveCivilWar(movement, true, "challenged_ruler_killed");
                    return;
                }
            }
        }

        private void OnPeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom a = first as Kingdom;
            Kingdom b = second as Kingdom;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && IsPair(x, a, b));
            if (movement == null || movement.ExecutionPhase.StartsWith("resolving", StringComparison.OrdinalIgnoreCase)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            if (parent != null && rebels != null && !parent.IsEliminated && !rebels.IsEliminated && !parent.IsAtWarWith(rebels))
            {
                if (ReignServices.AI != null)
                    ReignServices.AI.DeclareWarWithOrigin(rebels, parent, "rebellion",
                        "Civil war resumed during resolution reconciliation.", movement.MovementId, string.Empty, true);
                else DeclareWarAction.ApplyByRebellion(rebels, parent);
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.HasWinner) return;
            Kingdom winner = mapEvent.GetLeaderParty(mapEvent.WinningSide)?.MapFaction as Kingdom;
            Kingdom loser = mapEvent.GetLeaderParty(mapEvent.DefeatedSide)?.MapFaction as Kingdom;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && IsPair(x, winner, loser));
            if (movement == null) return;
            EmitHistory(movement, "civil_war_battle", "completed", winner?.Leader?.StringId,
                (winner?.InformalName?.ToString() ?? "A side") + " won a battle, but the rebellion remains leadership-bound.", false);
        }

        private const string OrdinaryReintegrationUnverified = "The civil-war reintegration is held until its native outcome can be verified.";

        private static bool OrdinaryResolutionInProgress(ReignRebellionMovementRecord movement)
            => movement.ExecutionPhase?.StartsWith("resolving", StringComparison.OrdinalIgnoreCase) == true;

        private bool ResolveCivilWar(ReignRebellionMovementRecord movement, bool rebelVictory, string cause,
            RulerJudgment rulerJudgment = null)
        {
            if (movement == null || movement.ResolutionApplied || OrdinaryResolutionInProgress(movement)) return false;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            movement.ExecutionPhase = "resolving_reunification";
            if (parent != null && rebels != null && parent.IsAtWarWith(rebels)) MakePeaceAction.Apply(parent, rebels);

            List<ReignRebellionMembershipRecord> losingMembers = MembersOf(movement)
                .Where(x => Same(x.Side, rebelVictory ? "loyalist" : "rebel"))
                .ToList();
            if (!RestoreCommittedClans(movement, parent, rebels)) return false;
            foreach (ReignRebellionMembershipRecord member in losingMembers) member.IsDefeated = true;
            Clan claimant = FindClan(movement.LeaderClanStringId);
            if (rebelVictory && parent != null && claimant != null && claimant.Kingdom == parent && !claimant.IsEliminated)
                ChangeRulingClanAction.Apply(parent, claimant);

            if (rebels != null && !rebels.IsEliminated && rebels.Clans.Count == 0)
                DestroyKingdomAction.Apply(rebels);

            movement.WinningSide = rebelVictory ? "rebel" : "loyalist";
            movement.Resolution = rebelVictory ? "rebel_victory" : "loyalist_victory";
            movement.ResolutionCause = cause ?? string.Empty;
            Hero victor = rebelVictory ? FindHero(movement.LeaderHeroStringId) : FindHero(movement.OriginalRulerHeroStringId);
            if (rulerJudgment != null && parent?.Leader?.IsAlive == true) victor = parent.Leader;
            movement.WinningLeaderHeroStringId = victor?.StringId ?? string.Empty;
            movement.Stage = "judgment";
            movement.UpdatedDay = CurrentDay();
            movement.CooldownUntilDay = CurrentDay() + RealmCooldownDays;

            if (victor == Hero.MainHero)
            {
                movement.JudgmentsPending = losingMembers.Any(x => !x.FateApplied && IsJudgmentTarget(x));
                movement.ExecutionPhase = movement.JudgmentsPending ? "awaiting_player_judgments" : "resolving_complete";
                if (movement.JudgmentsPending)
                {
                    EmitHistory(movement, "civil_war_judgments", "pending", victor.StringId,
                        "The victorious player must decide each defeated clan leader's fate.", false);
                    TryShowNextPlayerJudgment();
                    return true;
                }
            }
            else
            {
                foreach (ReignRebellionMembershipRecord member in losingMembers.Where(x => !x.FateApplied).ToList())
                    ApplyNpcMercyJudgment(movement, victor, member, rulerJudgment);
            }
            FinalizeResolution(movement);
            return true;
        }

        private bool RestoreCommittedClans(ReignRebellionMovementRecord movement, Kingdom parent, Kingdom rebels)
        {
            JObject origins = ParseObject(movement.OriginalClanKingdomsJson);
            foreach (ReignRebellionMembershipRecord member in MembersOf(movement).ToList())
            {
                Clan clan = FindClan(member.ClanStringId);
                if (clan == null || clan.IsEliminated) continue;
                string originId = string.IsNullOrWhiteSpace(member.OriginalKingdomStringId)
                    ? origins.Value<string>(clan.StringId) ?? movement.ParentKingdomStringId
                    : member.OriginalKingdomStringId;
                Kingdom destination = FindKingdom(originId);
                if (destination == null || destination.IsEliminated || Same(originId, movement.RebelKingdomStringId))
                    destination = parent;
                bool leaveRebels = destination == null && rebels != null && clan.Kingdom == rebels;
                if (destination != null)
                    MoveClanToKingdom(clan, destination);
                else if (leaveRebels)
                    ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
                else
                    continue;

                if (clan.Kingdom != destination)
                {
                    movement.ResolutionCause = "clan_reintegration_unverified:" + member.ClanStringId;
                    movement.UpdatedDay = CurrentDay();
                    ReignLog.Warn("Rebellion " + movement.MovementId + " reintegration held at " + movement.ResolutionCause + ".");
                    return false;
                }
            }

            return true;
        }

        private void ApplyNpcMercyJudgment(ReignRebellionMovementRecord movement, Hero victor, ReignRebellionMembershipRecord member,
            RulerJudgment rulerJudgment)
        {
            Hero defeated = CurrentLeader(member);
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                return;
            }
            var appealed = ReignRulerPolicy.InfluencedClanVerdict(rulerJudgment, member.ClanStringId, defeated.StringId,
                victor?.StringId, !CampaignOptions.IsLifeDeathCycleDisabled);
            if (appealed.HasValue)
            {
                ApplyFate(movement, victor, member, appealed == RulerVerdict.Freedom ? "freedom"
                    : appealed == RulerVerdict.Imprisonment ? "imprisonment" : "execution");
                return;
            }
            if (member.FateRoll <= 0) member.FateRoll = MBRandom.RandomInt(1, 101);
            int relation = victor?.GetRelation(defeated) ?? 0;
            int mercy = victor?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            member.FateScore = member.FateRoll + 15 * mercy + (int)Math.Round(relation / 5d, MidpointRounding.AwayFromZero);
            string outcome = member.FateScore >= 67 ? "freedom" : member.FateScore >= 34 ? "imprisonment" : "execution";
            ApplyFate(movement, victor, member, outcome);
        }

        private void TryShowNextPlayerJudgment()
        {
            if (_judgmentInquiryOpen || Hero.MainHero == null) return;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => x != null && x.JudgmentsPending
                && Same(x.WinningLeaderHeroStringId, Hero.MainHero.StringId) && !x.ResolutionApplied);
            if (movement == null) return;
            ReignRebellionMembershipRecord member = MembersOf(movement)
                .FirstOrDefault(x => x.IsDefeated && !x.FateApplied && IsJudgmentTarget(x));
            if (member == null)
            {
                movement.JudgmentsPending = false;
                FinalizeResolution(movement);
                return;
            }
            Hero defeated = CurrentLeader(member);
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                TryShowNextPlayerJudgment();
                return;
            }

            _judgmentInquiryOpen = true;
            List<InquiryElement> choices = new List<InquiryElement>
            {
                new InquiryElement("freedom", "Grant freedom (+30 relation)", null),
                new InquiryElement("imprisonment", "Order imprisonment (+10 relation)", null),
                new InquiryElement("execution", "Order execution", null)
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Judgment of " + defeated.Name,
                defeated.Name + ", leader of " + (defeated.Clan?.Name?.ToString() ?? "a defeated clan")
                    + ", awaits judgment after the rebellion.\n\nThis choice is final and uses native captivity or death rules.",
                choices, true, 1, 1, "Confirm Judgment", "Decide Later",
                selected =>
                {
                    _judgmentInquiryOpen = false;
                    string outcome = selected?.FirstOrDefault()?.Identifier as string;
                    if (!string.IsNullOrWhiteSpace(outcome)) ApplyFate(movement, Hero.MainHero, member, outcome);
                    TryShowNextPlayerJudgment();
                },
                selected => { _judgmentInquiryOpen = false; },
                string.Empty, false), true, false);
        }

        private void ApplyFate(ReignRebellionMovementRecord movement, Hero victor,
            ReignRebellionMembershipRecord member, string requestedOutcome)
        {
            Hero defeated = CurrentLeader(member);
            if (member == null || member.FateApplied) return;
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                return;
            }

            string outcome = requestedOutcome;
            if (Same(outcome, "freedom"))
            {
                if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
                ApplyFateRelationBonus(victor, defeated, member, 30);
            }
            else if (Same(outcome, "imprisonment"))
            {
                ImprisonByVictor(victor, defeated);
                ApplyFateRelationBonus(victor, defeated, member, 10);
            }
            else
            {
                if (ReignRulerCampaignBehavior.Enabled(RulerOptions.CivilWars) && !CampaignOptions.IsLifeDeathCycleDisabled)
                {
                    ImprisonByVictor(victor, defeated);
                    ReignServices.Ruler.ScheduleDefeatedLord(defeated, victor, movement.MovementId);
                    outcome = "execution_scheduled";
                }
                else if (!CampaignOptions.IsLifeDeathCycleDisabled)
                {
                    if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
                    KillCharacterAction.ApplyByExecution(defeated, victor, true, defeated != Hero.MainHero);
                    outcome = defeated.IsDead ? "execution" : "imprisonment";
                    if (!defeated.IsDead)
                    {
                        ImprisonByVictor(victor, defeated);
                        ApplyFateRelationBonus(victor, defeated, member, 10);
                    }
                }
                else
                {
                    outcome = "imprisonment";
                    ImprisonByVictor(victor, defeated);
                    ApplyFateRelationBonus(victor, defeated, member, 10);
                }
            }

            member.FateOutcome = outcome;
            member.FateApplied = true;
            EmitHistory(movement, "civil_war_judgment", "completed", victor?.StringId,
                (victor?.Name?.ToString() ?? "The victor") + " imposed " + outcome + " on " + defeated.Name + ".", false);
        }

        private static void ImprisonByVictor(Hero victor, Hero defeated)
        {
            if (victor == null || defeated == null || defeated.IsDead) return;
            PartyBase holder = victor == Hero.MainHero ? PartyBase.MainParty : null;
            holder = holder ?? victor.PartyBelongedTo?.Party
                ?? victor.Clan?.Leader?.PartyBelongedTo?.Party
                ?? victor.Clan?.Fiefs.FirstOrDefault()?.Settlement?.Party;
            if (defeated.IsPrisoner && defeated.PartyBelongedToAsPrisoner == holder) return;
            if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
            if (holder != null) TakePrisonerAction.Apply(holder, defeated);
        }

        private static void ApplyFateRelationBonus(Hero victor, Hero defeated,
            ReignRebellionMembershipRecord member, int delta)
        {
            if (victor == null || defeated == null || member.RelationBonusApplied || delta == 0) return;
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(defeated, victor, delta, true);
            member.RelationBonusApplied = true;
        }

        private void FinalizeResolution(ReignRebellionMovementRecord movement, float? observedDay = null)
        {
            if (movement == null) return;
            if (!string.IsNullOrWhiteSpace(movement.BackingSponsorKingdomStringId)
                && !string.IsNullOrWhiteSpace(movement.RebelKingdomStringId))
            {
                ReignServices.AI?.EndAgreementsBetween(
                    movement.BackingSponsorKingdomStringId, movement.RebelKingdomStringId,
                    "rebellion_backing", "rebellion_resolved", string.Empty, string.Empty);
                movement.BackingStatus = "expired";
                movement.BackingTerminalReason = "rebellion_resolved";
            }
            movement.JudgmentsPending = false;
            movement.Stage = "resolved";
            movement.ResolutionApplied = true;
            movement.ExecutionPhase = "verified";
            movement.UpdatedDay = observedDay ?? CurrentDay();
            movement.CooldownUntilDay = Math.Max(movement.CooldownUntilDay, movement.UpdatedDay + RealmCooldownDays);
            CloseResolvedPreparationPlots(_plots, _movements);
            EmitHistory(movement, "civil_war_resolved", "completed", movement.WinningLeaderHeroStringId,
                "The rebellion ended in " + movement.Resolution.Replace('_', ' ')
                    + " because " + (movement.ResolutionCause ?? string.Empty).Replace('_', ' ') + ".", false);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] The rebellion has ended: "
                + movement.Resolution.Replace('_', ' ') + "."));
        }

        private void ResolveWithoutMutation(ReignRebellionMovementRecord movement, string reason)
        {
            if (movement == null) return;
            movement.Stage = "resolved";
            movement.Resolution = reason;
            movement.ResolutionCause = reason;
            movement.ResolutionApplied = true;
            movement.ExecutionPhase = "failed";
            movement.CooldownUntilDay = CurrentDay() + RealmCooldownDays;
            CloseResolvedPreparationPlots(_plots, _movements);
        }

        private void MigrateLegacyMovements()
        {
            foreach (ReignRebellionMovementRecord movement in _movements.Where(x => x != null && !x.RelationshipRulesMigrated).ToList())
            {
                if (HasRecognitionAttempt(movement)) continue;
                movement.RelationshipRulesMigrated = true;
                if (IsCivilWar(movement))
                {
                    movement.Objective = "claimant_takeover";
                    movement.IsPlayerLed = Same(movement.LeaderClanStringId, Clan.PlayerClan?.StringId);
                    foreach (ReignRebellionMembershipRecord member in MembersOf(movement))
                    {
                        Clan clan = FindClan(member.ClanStringId);
                        if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId))
                            member.OriginalKingdomStringId = Same(member.Side, "rebel")
                                ? movement.ParentKingdomStringId
                                : clan?.Kingdom?.StringId ?? movement.ParentKingdomStringId;
                        member.IsCommitted = true;
                        RememberOriginalKingdom(movement, member.ClanStringId, member.OriginalKingdomStringId);
                    }
                    EmitHistory(movement, "rebellion_rules_migrated", "completed", movement.LeaderHeroStringId,
                        "The active civil war migrated to relationship sides and leadership-bound victory.", false);
                }
                else if (!movement.ResolutionApplied && !Same(movement.Stage, "resolved"))
                {
                    ResolveWithoutMutation(movement, "legacy_prewar_movement_retired");
                }
            }
        }

        private void ReconcileAllMovements()
        {
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
                ReconcileCivilWar(movement);
        }

        private bool HasActiveMovement(Kingdom kingdom)
        {
            return kingdom != null && _movements.Any(x => x != null && !x.ResolutionApplied
                && (Same(x.ParentKingdomStringId, kingdom.StringId) || Same(x.RebelKingdomStringId, kingdom.StringId)));
        }

        private bool IsRealmOnCooldown(Kingdom kingdom, float now)
        {
            return kingdom != null && _movements.Any(x => Same(x.ParentKingdomStringId, kingdom.StringId)
                && x.ResolutionApplied && x.CooldownUntilDay > now);
        }

        private static bool IsEligibleNpcVassalLeader(Clan clan, Kingdom kingdom)
        {
            Hero leader = clan?.Leader;
            return clan != null && kingdom != null && clan.Kingdom == kingdom && clan != kingdom.RulingClan
                && clan != Clan.PlayerClan && !clan.IsEliminated && !clan.IsUnderMercenaryService
                && !clan.IsMinorFaction && !clan.IsClanTypeMercenary
                && leader != null && leader.IsLord && !leader.IsDead && !leader.IsPrisoner;
        }

        private ReignRebellionMembershipRecord AddOrUpdateMember(ReignRebellionMovementRecord movement, Clan clan,
            string side, string joinedBy, int rebelRelation, int rulerRelation)
        {
            ReignRebellionMembershipRecord member = _memberships.FirstOrDefault(x =>
                Same(x.MovementId, movement.MovementId) && Same(x.ClanStringId, clan.StringId));
            if (member == null)
            {
                member = new ReignRebellionMembershipRecord
                {
                    MovementId = movement.MovementId,
                    ClanStringId = clan.StringId,
                    OriginalKingdomStringId = clan.Kingdom?.StringId ?? string.Empty,
                    JoinedDay = CurrentDay()
                };
                _memberships.Add(member);
            }
            member.LeaderHeroStringId = clan.Leader?.StringId ?? member.LeaderHeroStringId;
            member.Side = side;
            member.JoinedBy = joinedBy ?? string.Empty;
            member.RelationToRebelAtJoin = rebelRelation;
            member.RelationToRulerAtJoin = rulerRelation;
            member.SupportScore = Same(side, "rebel") ? 100f : 0f;
            member.IsCommitted = true;
            member.PlayerChoiceRequired = false;
            RememberOriginalKingdom(movement, clan.StringId, member.OriginalKingdomStringId);
            return member;
        }

        private static void MoveClanToKingdom(Clan clan, Kingdom destination)
        {
            if (clan == null || destination == null || clan.Kingdom == destination) return;
            Kingdom old = clan.Kingdom;
            if (old != null)
                ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, destination,
                    CampaignTime.Now + CampaignTime.Days(RebellionCommitmentDays), true);
            else
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, destination,
                    CampaignTime.Now + CampaignTime.Days(RebellionCommitmentDays), true);
        }

        private static bool IsHeldByFaction(Hero hero, Kingdom faction)
        {
            return hero?.IsPrisoner == true && ReignRulerCampaignBehavior.IsSovereignCustody(hero.PartyBelongedToAsPrisoner, faction);
        }

        private static Hero CurrentLeader(ReignRebellionMembershipRecord member)
        {
            return FindClan(member?.ClanStringId)?.Leader ?? FindHero(member?.LeaderHeroStringId);
        }

        private static bool IsJudgmentTarget(ReignRebellionMembershipRecord member)
        {
            Hero hero = CurrentLeader(member);
            return hero != null && !hero.IsDead;
        }

        private void RememberOriginalKingdom(ReignRebellionMovementRecord movement, string clanId, string kingdomId)
        {
            if (movement == null || string.IsNullOrWhiteSpace(clanId)) return;
            JObject origins = ParseObject(movement.OriginalClanKingdomsJson);
            if (origins[clanId] == null) origins[clanId] = kingdomId ?? string.Empty;
            movement.OriginalClanKingdomsJson = origins.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
