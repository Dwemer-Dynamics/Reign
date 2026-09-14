using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Economy;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        // Heartbeats are cursor-based, but several autonomous systems can finish
        // more than one durable record while campaign time is paused. Those
        // records legitimately share the exact same campaign-day timestamp as
        // the preceding accepted heartbeat. Resend a very small same-day overlap;
        // the server's primary keys/upserts make the overlap idempotent.
        private const double WorldTestDeltaOverlapDays = 0.001d;

        private static bool IsVisibleAfterWorldTestDelta(double recordDay,
            double deltaAfterDay)
        {
            return deltaAfterDay < 0d
                || recordDay >= deltaAfterDay - WorldTestDeltaOverlapDays;
        }

        public static async Task<double?> SubmitWorldTestHeartbeatAsync(double deltaAfterDay)
        {
            try
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null) return null;
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer) return null;
                JObject payload = await ReignMainThread.InvokeAsync(
                    () => BuildWorldTestHeartbeat(deltaAfterDay)).ConfigureAwait(false);
                JObject response = await PostJsonAsync("/world-test/heartbeat", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true
                    ? response.Value<double?>("worldDay") ?? payload.Value<double?>("worldDay")
                    : null;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("World Test heartbeat failed: " + ex.Message);
                return null;
            }
        }

        private static JObject BuildWorldTestHeartbeat(double deltaAfterDay)
        {
            double worldDay = CampaignTime.Now.ToDays;
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            List<Hero> alive = Hero.AllAliveHeroes.Where(x => x != null && x.IsAlive).ToList();
            List<Hero> eligible = alive.Where(IsWorldTestPassiveNpc).ToList();
            List<Kingdom> kingdoms = Kingdom.All.Where(x => x != null && !x.IsEliminated).ToList();
            List<Clan> clans = Clan.All.Where(x => x != null && !x.IsEliminated && !x.IsBanditFaction).ToList();
            List<Hero> politicalLeaders = kingdoms
                .SelectMany(kingdom => kingdom.Clans
                    .Where(clan => clan != null && !clan.IsEliminated
                        && clan.Leader != null)
                    .Select(clan => clan.Leader)
                    .Concat(kingdom.Leader == null
                        ? Enumerable.Empty<Hero>()
                        : new[] { kingdom.Leader }))
                .Where(hero => hero != null && hero.IsAlive
                    && !string.IsNullOrWhiteSpace(hero.StringId))
                .GroupBy(hero => hero.StringId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(hero => hero.StringId,
                    StringComparer.OrdinalIgnoreCase).ToList();
            JArray politicalRoster = new JArray(politicalLeaders
                .Select(BuildIdentityRosterEntry));
            string politicalFingerprint = StableWorldTestHash(string.Join("|",
                politicalLeaders.Select(hero => string.Join(":",
                    hero.StringId ?? string.Empty,
                    hero.Clan?.StringId ?? string.Empty,
                    hero.Clan?.Kingdom?.StringId ?? string.Empty,
                    hero.Clan?.Leader == hero ? "1" : "0",
                    hero.Clan?.Kingdom?.Leader == hero ? "1" : "0"))));
            JArray wars = new JArray();
            for (int i = 0; i < kingdoms.Count; i++)
            {
                for (int j = i + 1; j < kingdoms.Count; j++)
                {
                    if (kingdoms[i].IsAtWarWith(kingdoms[j]))
                        wars.Add(new JObject { ["firstKingdomId"] = kingdoms[i].StringId, ["secondKingdomId"] = kingdoms[j].StringId });
                }
            }

            ReignAICampaignBehavior ai = ReignAICampaignBehavior.Instance;
            ReignRebellionCampaignBehavior rebellion = ReignRebellionCampaignBehavior.Instance;
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string featureConfiguration = string.Join("|",
                settings == null || settings.PassiveRelationshipDirectorEnabled,
                settings == null || settings.AmbientRelationshipDriftEnabled,
                settings == null || settings.ReignControlledNpcMarriageEnabled,
                settings == null || (settings.Enabled && settings.AllowAutonomousWorldTicks && settings.ExecuteDiplomacyActions),
                settings == null || settings.Enabled,
                settings == null || settings.UseLocalServer);
            Process process = Process.GetCurrentProcess();
            JArray agreements = new JArray((ai?.Agreements ?? new List<ReignDiplomaticAgreementRecord>())
                .Where(x => x != null && (x.IsActive
                    || IsVisibleAfterWorldTestDelta(x.CreatedDay, deltaAfterDay)))
                .Select(x => new JObject
                {
                    ["agreementId"] = x.AgreementId ?? string.Empty, ["kind"] = x.Kind ?? string.Empty,
                    ["actorKingdomId"] = x.ActorKingdomStringId ?? string.Empty, ["targetKingdomId"] = x.TargetKingdomStringId ?? string.Empty,
                    ["createdDay"] = x.CreatedDay, ["expireDay"] = x.ExpireDay, ["isActive"] = x.IsActive
                }));
            JArray actions = new JArray((ai?.Actions ?? new List<ReignWorldActionRecord>())
                .Where(x => x != null && (!x.IsTerminal
                    || IsVisibleAfterWorldTestDelta(x.CreatedDay, deltaAfterDay)
                    || IsVisibleAfterWorldTestDelta(x.LastAttemptDay, deltaAfterDay)))
                .Select(x => new JObject
                {
                    ["actionId"] = x.ActionId ?? string.Empty, ["type"] = x.Type.ToString(), ["status"] = x.Status.ToString(),
                    ["source"] = x.Source ?? string.Empty, ["createdDay"] = x.CreatedDay, ["lastAttemptDay"] = x.LastAttemptDay,
                    ["actorKingdomId"] = x.ActorKingdomStringId ?? string.Empty,
                    ["targetKingdomId"] = x.TargetKingdomStringId ?? string.Empty,
                    ["attemptCount"] = x.AttemptCount, ["failureReason"] = x.FailureReason ?? string.Empty
                }));

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["campaignLabel"] = Hero.MainHero.Name?.ToString() ?? GetCampaignId(),
                ["mainHeroStringId"] = Hero.MainHero.StringId ?? string.Empty,
                ["mainHeroName"] = Hero.MainHero.Name?.ToString() ?? string.Empty,
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerClanName"] = Clan.PlayerClan?.Name?.ToString() ?? string.Empty,
                ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerKingdomName"] = Clan.PlayerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["timelineId"] = timelineId,
                ["worldDay"] = worldDay,
                ["startupGrace"] = ReignCampaignPreparationCampaignBehavior
                    .AutonomousWorldStartupGraceSnapshot((float)worldDay),
                ["deltaAfterDay"] = deltaAfterDay,
                ["politicalRosterFingerprint"] = politicalFingerprint,
                ["politicalLeaders"] = politicalRoster,
                ["capturedUtc"] = DateTime.UtcNow.ToString("o"),
                ["manifest"] = new JObject
                {
                    ["clientAssemblyVersion"] = typeof(ReignServerClient).Assembly.GetName().Version?.ToString() ?? string.Empty,
                    ["timelineId"] = timelineId,
                    ["relationshipCompatibilityVersion"] = 5,
                    ["rumorSchema"] = "character_owned_social_reputation",
                    ["configurationHash"] = StableWorldTestHash(featureConfiguration),
                    ["relationshipWorker"] = new JObject
                    {
                        ["chunkSize"] = 500,
                        ["nativePullLimit"] = 1024,
                        ["applicationBudgetMilliseconds"] = 5
                    }
                },
                ["featureFlags"] = new JObject
                {
                    ["passiveRelationshipsEnabled"] = settings == null || settings.PassiveRelationshipDirectorEnabled,
                    ["ambientRelationshipsEnabled"] = settings == null || settings.AmbientRelationshipDriftEnabled,
                    ["politicalMarriageEnabled"] = settings == null || settings.ReignControlledNpcMarriageEnabled,
                    ["worldDiplomacyEnabled"] = settings == null || (settings.Enabled && settings.AllowAutonomousWorldTicks && settings.ExecuteDiplomacyActions),
                    ["rebellionsEnabled"] = settings == null || settings.Enabled,
                    ["rumorsEnabled"] = settings == null || settings.Enabled,
                    ["localServerEnabled"] = settings == null || settings.UseLocalServer
                },
                ["saveSync"] = new JObject
                {
                    ["alignmentPending"] = ReignSaveSyncCoordinator.IsAlignmentPending,
                    ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"
                },
                ["relationshipClient"] = BuildRelationshipOutboxDiagnostics(),
                ["producerClocks"] = new JObject
                {
                    ["heartbeatDay"] = worldDay,
                    ["relationshipOutbox"] = BuildRelationshipOutboxDiagnostics()
                },
                ["process"] = new JObject
                {
                    ["workingSetBytes"] = process.WorkingSet64,
                    ["privateMemoryBytes"] = process.PrivateMemorySize64,
                    ["totalProcessorMilliseconds"] = process.TotalProcessorTime.TotalMilliseconds
                },
                ["population"] = new JObject
                {
                    ["eligibleNpcCount"] = eligible.Count,
                    ["livingHeroCount"] = alive.Count,
                    ["livingAdultLordCount"] = alive.Count(x => x.IsLord && !x.IsChild && x.Age >= 18f),
                    ["kingdomCount"] = kingdoms.Count,
                    ["clanCount"] = clans.Count,
                    ["warCount"] = wars.Count,
                    ["marriageCount"] = alive.Count(x => x.Spouse != null) / 2,
                    ["pregnancyCount"] = alive.Count(x => x.IsPregnant),
                    ["livingChildCount"] = alive.Count(x => x.IsChild),
                    ["prisonerNpcCount"] = eligible.Count(x => x.IsPrisoner),
                    ["settlementCount"] = Settlement.All.Count(x => x != null),
                    ["fortificationCount"] = Settlement.All.Count(x => x?.IsFortification == true)
                },
                ["nativeOutcomes"] = new JObject
                {
                    ["wars"] = wars,
                    ["agreements"] = agreements,
                    ["actions"] = actions
                },
                ["warEconomy"] = BuildWorldTestWarEconomy(),
                ["clanAccords"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.Snapshot() ?? new JObject(),
                ["rebellions"] = new JObject
                {
                    ["weeklyRolls"] = BuildWorldTestRebellionRolls(rebellion, deltaAfterDay),
                    ["movements"] = BuildWorldTestRebellionMovements(rebellion, deltaAfterDay),
                    ["memberships"] = BuildWorldTestRebellionMemberships(rebellion, deltaAfterDay)
                }
            };
        }

        private static string StableWorldTestHash(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return string.Concat(digest.Select(x => x.ToString("x2")));
            }
        }

        private static bool IsWorldTestPassiveNpc(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.Age < 18f) return false;
            return hero.IsLord || hero.IsNotable || hero.IsWanderer || hero.Clan != null;
        }

        private static JArray BuildWorldTestRebellionRolls(ReignRebellionCampaignBehavior behavior,
            double deltaAfterDay)
        {
            int latestWeekIndex = (behavior?.WeeklyRolls
                ?? new List<ReignRebellionWeeklyRollRecord>())
                .Where(x => x != null).Select(x => x.WeekIndex)
                .DefaultIfEmpty(-1).Max();
            return new JArray((behavior?.WeeklyRolls ?? new List<ReignRebellionWeeklyRollRecord>())
                // Always resend the latest retained set. Rebellion timestamps are
                // saveable floats, whose precision near late-game campaign days
                // is coarser than the heartbeat's double cursor overlap. Server
                // upserts make this bounded resend idempotent and ensure an active
                // movement can never outlive its authoritative roll provenance.
                .Where(x => x != null && (x.WeekIndex == latestWeekIndex
                    || IsVisibleAfterWorldTestDelta(x.RolledDay, deltaAfterDay))).Select(x =>
                new JObject
                {
                    ["kingdomId"] = x.KingdomStringId ?? string.Empty, ["clanId"] = x.ClanStringId ?? string.Empty,
                    ["weekIndex"] = x.WeekIndex, ["roll"] = x.Roll, ["rulerRelation"] = x.RulerRelation,
                    ["personalAffinityToRuler"] = x.PersonalAffinityToRuler,
                    ["rulerPublicStanding"] = x.RulerPublicStanding,
                    ["rulerStandingRevision"] = x.RulerStandingRevision,
                    ["wasEligible"] = x.WasEligible, ["triggered"] = x.Triggered, ["rolledDay"] = x.RolledDay,
                    ["exclusionReason"] = x.ExclusionReason ?? string.Empty,
                    ["testOverride"] = x.TestOverride, ["testRunId"] = x.TestRunId ?? string.Empty,
                    ["isPlayerClan"] = string.Equals(x.ClanStringId, Clan.PlayerClan?.StringId, StringComparison.OrdinalIgnoreCase)
                }));
        }

        private static JObject BuildWorldTestWarEconomy()
        {
            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            List<Village> villages = Settlement.All.Where(x => x?.Village != null)
                .Select(x => x.Village).Where(x => x != null).ToList();
            JObject recoveryStages = new JObject();
            double foodPenalty = 0d;
            double securityPenalty = 0d;
            double mobilizationStrain = 0d;
            double mobilizationFoodPenalty = 0d;
            int mobilizationStrainedVillages = 0;
            int recruitmentRecoveryBlocks = 0;
            foreach (Village village in villages)
            {
                int step = behavior?.GetRecoveryStep(village)
                    ?? ReignEconomyCampaignBehavior.MaximumRecoveryStep;
                string key = step.ToString();
                recoveryStages[key] = (recoveryStages.Value<int?>(key) ?? 0) + 1;
                double modifier = step / (double)ReignEconomyCampaignBehavior.MaximumRecoveryStep;
                if (village.VillageState == Village.VillageStates.Normal && modifier < 1d)
                    foodPenalty += (village.GetHearthLevel() + 1) * 6d * (1d - modifier);
                if (modifier < 1d) securityPenalty += 2d * (1d - modifier);
                double strain = behavior?.GetMobilizationStrain(village) ?? 0d;
                mobilizationStrain += strain;
                double strainPenalty = behavior?.GetMobilizationFoodPenalty(village) ?? 0d;
                if (strainPenalty > 0d)
                {
                    mobilizationStrainedVillages++;
                    mobilizationFoodPenalty += Math.Min(strainPenalty,
                        (village.GetHearthLevel() + 1) * 6d * modifier);
                }
                if (village.VillageState == Village.VillageStates.Normal
                    && modifier < ReignEconomyCampaignBehavior.MinimumRecruitmentRecoveryModifier)
                    recruitmentRecoveryBlocks++;
            }

            JObject result = ReignEconomyPatches.BuildDiagnostics();
            result["villageCount"] = villages.Count;
            result["lootedVillages"] = villages.Count(x => x.VillageState == Village.VillageStates.Looted);
            result["recoveringVillages"] = behavior == null ? 0 : villages.Count(x => behavior.GetRecoveryStep(x) < ReignEconomyCampaignBehavior.MaximumRecoveryStep);
            result["recoveryStages"] = recoveryStages;
            result["foodPenalty"] = Math.Round(foodPenalty, 2);
            result["securityPenalty"] = Math.Round(securityPenalty, 2);
            result["mobilizationStrain"] = Math.Round(mobilizationStrain, 2);
            result["mobilizationStrainedVillages"] = mobilizationStrainedVillages;
            result["mobilizationFoodPenalty"] = Math.Round(mobilizationFoodPenalty, 2);
            result["recruitmentRecoveryBlocks"] = recruitmentRecoveryBlocks;
            result["recruitRequests"] = behavior?.RecruitRequests ?? 0;
            result["recruitsGranted"] = behavior?.RecruitsGranted ?? 0;
            result["recruitFloorBlocks"] = behavior?.RecruitFloorBlocks ?? 0;
            result["recruitHearthDebited"] = behavior?.RecruitHearthDebited ?? 0f;
            result["recoveryTransitions"] = behavior?.RecoveryTransitions ?? 0;
            result["siegeShockCount"] = behavior?.SiegeShockCount ?? 0;
            result["siegeSecurityLost"] = behavior?.SiegeSecurityLost ?? 0f;
            result["retinueCreations"] = behavior?.RetinueCreations ?? 0;
            result["retinueTroops"] = behavior?.RetinueTroops ?? 0;
            result["retinueDuplicateInitializationsBlocked"] = behavior?.RetinueDuplicateInitializationsBlocked ?? 0;
            result["retinueDuplicateCreations"] = 0;
            result["activePatrols"] = MobileParty.All.Count(x => x?.PatrolPartyComponent != null);
            result["patrolTroopsTransferred"] = behavior?.PatrolTroopsTransferred ?? 0;
            result["patrolTroopsReturned"] = behavior?.PatrolTroopsReturned ?? 0;
            result["patrolConservationMismatches"] = behavior?.PatrolConservationMismatches ?? 0;
            result["reliefShipmentCount"] = behavior?.ReliefShipmentCount ?? 0;
            result["reliefFoodShipped"] = behavior?.ReliefFoodShipped ?? 0f;
            result["reliefFoodDelivered"] = behavior?.ReliefFoodDelivered ?? 0f;
            result["reliefFoodLost"] = behavior?.ReliefFoodLost ?? 0f;
            result["reliefBlockedRequests"] = behavior?.ReliefBlockedRequests ?? 0;
            result["currentReliefDonors"] = behavior?.CurrentReliefDonors ?? 0;
            result["currentReliefRecipients"] = behavior?.CurrentReliefRecipients ?? 0;
            result["currentReliefUnmet"] = behavior?.CurrentReliefUnmet ?? 0;
            result["currentReliefFoodShipped"] = behavior?.CurrentReliefFoodShipped ?? 0f;
            result["currentReliefFoodDelivered"] = behavior?.CurrentReliefFoodDelivered ?? 0f;
            result["currentReliefFoodLost"] = behavior?.CurrentReliefFoodLost ?? 0f;
            return result;
        }

        private static JArray BuildWorldTestRebellionMovements(ReignRebellionCampaignBehavior behavior,
            double deltaAfterDay)
        {
            return new JArray((behavior?.Movements ?? new List<ReignRebellionMovementRecord>())
                .Where(x => x != null && IsVisibleAfterWorldTestDelta(
                    x.UpdatedDay, deltaAfterDay)).Select(x =>
                new JObject
                {
                    ["movementId"] = x.MovementId ?? string.Empty, ["parentKingdomId"] = x.ParentKingdomStringId ?? string.Empty,
                    ["rebelKingdomId"] = x.RebelKingdomStringId ?? string.Empty, ["leaderClanId"] = x.LeaderClanStringId ?? string.Empty,
                    ["leaderHeroId"] = x.LeaderHeroStringId ?? string.Empty, ["rulerHeroId"] = x.OriginalRulerHeroStringId ?? string.Empty,
                    ["stage"] = x.Stage ?? string.Empty, ["createdDay"] = x.CreatedDay, ["updatedDay"] = x.UpdatedDay,
                    ["civilWarStartedDay"] = x.CivilWarStartedDay, ["cooldownUntilDay"] = x.CooldownUntilDay,
                    ["isPlayerLed"] = x.IsPlayerLed, ["resolutionApplied"] = x.ResolutionApplied,
                    ["winningSide"] = x.WinningSide ?? string.Empty, ["resolutionCause"] = x.ResolutionCause ?? string.Empty,
                    ["outbreakWeekIndex"] = x.OutbreakWeekIndex,
                    ["rebelLeaderCapturedDay"] = x.RebelLeaderCapturedDay,
                    ["challengedRulerCapturedDay"] = x.ChallengedRulerCapturedDay,
                    ["captureGraceDays"] = 3
                }));
        }

        private static JArray BuildWorldTestRebellionMemberships(ReignRebellionCampaignBehavior behavior,
            double deltaAfterDay)
        {
            HashSet<string> changedMovements = new HashSet<string>(
                (behavior?.Movements ?? new List<ReignRebellionMovementRecord>())
                    .Where(x => x != null && IsVisibleAfterWorldTestDelta(
                        x.UpdatedDay, deltaAfterDay))
                    .Select(x => x.MovementId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            return new JArray((behavior?.Memberships ?? new List<ReignRebellionMembershipRecord>())
                .Where(x => x != null && (IsVisibleAfterWorldTestDelta(
                        x.JoinedDay, deltaAfterDay)
                    || changedMovements.Contains(x.MovementId ?? string.Empty))).Select(x =>
                new JObject
                {
                    ["movementId"] = x.MovementId ?? string.Empty, ["clanId"] = x.ClanStringId ?? string.Empty,
                    ["leaderHeroId"] = x.LeaderHeroStringId ?? string.Empty, ["side"] = x.Side ?? string.Empty,
                    ["relationToRebelAtJoin"] = x.RelationToRebelAtJoin, ["relationToRulerAtJoin"] = x.RelationToRulerAtJoin,
                    ["joinedDay"] = x.JoinedDay, ["joinedBy"] = x.JoinedBy ?? string.Empty,
                    ["automaticNpcSelection"] = string.Equals(x.JoinedBy, "npc_outbreak_poll", StringComparison.OrdinalIgnoreCase),
                    ["isPlayerClan"] = string.Equals(x.ClanStringId, Clan.PlayerClan?.StringId, StringComparison.OrdinalIgnoreCase),
                    ["fateRoll"] = x.FateRoll, ["fateScore"] = x.FateScore, ["fateOutcome"] = x.FateOutcome ?? string.Empty,
                    ["fateApplied"] = x.FateApplied, ["relationBonusApplied"] = x.RelationBonusApplied
                }));
        }
    }
}
