using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> SavePrepareProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                ReignSocialBalanceHarnessCampaignBehavior harness =
                    ReignServices.SocialBalanceHarness;
                ReignCourtPersonalityReputationCampaignBehavior court =
                    ReignServices.CourtPersonalityReputation;
                if (harness == null || court == null)
                    return LiveCommandResult.Failed(
                        "The save-backed Social Reputation harness and court state are required.",
                        prerequisites.Data);
                JObject courtState = court.SocialBalancePersistenceSnapshot();
                JObject marker = harness.StagePersistenceMarker(command, courtState);
                JObject evidence = prerequisites.Data?.DeepClone() as JObject ?? new JObject();
                evidence["persistenceMarker"] = marker;
                evidence["courtState"] = courtState;
                evidence["requiresNativeSaveAndDifferentGameInstance"] = true;
                evidence["authoritativePassRequired"] = true;
                return LiveCommandResult.Completed(
                    "Staged the exact save-backed Social Reputation persistence marker; save and reload are still required.",
                    evidence);
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> SaveVerifyProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                ReignSocialBalanceHarnessCampaignBehavior harness =
                    ReignServices.SocialBalanceHarness;
                ReignCourtPersonalityReputationCampaignBehavior court =
                    ReignServices.CourtPersonalityReputation;
                if (harness == null || court == null)
                    return LiveCommandResult.Failed(
                        "The reloaded save-backed Social Reputation harness and court state are required.",
                        prerequisites.Data);
                JObject currentCourtState = court.SocialBalancePersistenceSnapshot();
                JObject verification = harness.VerifyPersistenceMarker(command, currentCourtState);
                JObject evidence = prerequisites.Data?.DeepClone() as JObject ?? new JObject();
                evidence["persistenceVerification"] = verification;
                evidence["authoritativePassRequired"] = true;
                return LiveCommandResult.Completed(
                    verification.Value<bool?>("verified") == true
                        ? "The native save-backed marker and court state survived a different game instance; server verification is pending."
                        : "The native save-backed persistence marker did not satisfy every reload assertion.",
                    evidence);
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> CleanupMarkerProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                string runId = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
                bool markerCleared = ReignServices.SocialBalanceHarness?
                    .ClearPersistenceMarker(runId) == true;
                int rebellionOverrides = ReignServices.Rebellion?
                    .ClearSocialBalanceRollOverrides(runId) ?? 0;
                JObject evidence = prerequisites.Data?.DeepClone() as JObject ?? new JObject();
                evidence["persistenceMarkerCleared"] = markerCleared;
                evidence["rebellionRollOverridesCleared"] = rebellionOverrides;
                evidence["promptOverrideActive"] = false;
                return LiveCommandResult.Completed(
                    "Cleared the exact run-owned Social Reputation persistence marker and pending native overrides.",
                    evidence);
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> UnchasteProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            JObject production = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                List<Hero> regularLords = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player && hero.IsLord
                        && hero.Clan?.Leader != hero && hero.Clan?.Kingdom?.Leader != hero)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Hero mother = regularLords.FirstOrDefault(hero => hero.IsFemale);
                Hero biologicalFather = regularLords.FirstOrDefault(hero => !hero.IsFemale);
                Hero legalFather = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && !hero.IsFemale
                        && hero != biologicalFather && hero != player)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (mother == null || biologicalFather == null || legalFather == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The Unchaste profile requires female and male ordinary lords plus a distinct living adult legal-father fixture."
                    };

                ReignFamilyCampaignBehavior family = ReignServices.Family;
                Hero child = null;
                string childError = string.Empty;
                if (family == null || !family.TryCreateSocialBalanceChild(
                    mother, biologicalFather, legalFather, true, false,
                    out child, out childError))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(childError)
                            ? "The Reign family behavior could not create controlled public parentage evidence."
                            : childError
                    };

                ReignCourtPersonalityReputationCampaignBehavior producer =
                    ReignServices.CourtPersonalityReputation;
                int seasonIndex = Math.Max(0, (int)Math.Floor(
                    Math.Max(0d, CampaignTime.Now.ToDays) / ReignCalendarService.DaysPerSeason));
                string enrolledRunId = (command["enrollment"] as JObject)
                    ?.Value<string>("runId") ?? string.Empty;
                JObject motherEvidence = new JObject();
                string motherError = string.Empty;
                if (producer == null || !producer.RecordUnchasteForSocialBalance(
                    mother, seasonIndex, enrolledRunId, out motherEvidence,
                    out motherError))
                    return new JObject { ["ok"] = false, ["error"] = motherError };
                JObject fatherEvidence = new JObject();
                string fatherError = string.Empty;
                if (!producer.RecordUnchasteForSocialBalance(
                    biologicalFather, seasonIndex, enrolledRunId, out fatherEvidence,
                    out fatherError))
                    return new JObject { ["ok"] = false, ["error"] = fatherError };

                JObject result = new JObject
                {
                    ["ok"] = true,
                    ["productionProducerUsed"] = true,
                    ["forcedRareBranch"] = true,
                    ["child"] = new JObject
                    {
                        ["childId"] = child.StringId,
                        ["motherId"] = mother.StringId,
                        ["biologicalFatherId"] = biologicalFather.StringId,
                        ["legalFatherId"] = legalFather.StringId,
                        ["publiclyKnown"] = true,
                        ["illegitimate"] = true
                    },
                    ["femaleParent"] = new JObject
                    {
                        ["heroId"] = mother.StringId,
                        ["heroName"] = mother.Name?.ToString() ?? mother.StringId,
                        ["isFemale"] = true,
                        ["isRegularLord"] = true,
                        ["producerEvidence"] = motherEvidence
                    },
                    ["maleParent"] = new JObject
                    {
                        ["heroId"] = biologicalFather.StringId,
                        ["heroName"] = biologicalFather.Name?.ToString() ?? biologicalFather.StringId,
                        ["isFemale"] = false,
                        ["isRegularLord"] = true,
                        ["producerEvidence"] = fatherEvidence
                    },
                    ["worldHistorySequence"] = ReignServices.WorldHistory?.Sequence ?? 0L
                };
                ReignServices.SocialBalanceHarness?.RecordOverride(
                    "unchaste_forced_public_parentage", new JObject(result));
                return result;
            }).ConfigureAwait(false);

            if (production.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(
                    production.Value<string>("error") ?? "The native Unchaste producer fixture failed.",
                    production);
            long sequence = production.Value<long?>("worldHistorySequence") ?? 0L;
            bool drained = sequence > 0L && ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                sequence, TimeSpan.FromSeconds(120));
            production["worldHistoryDrained"] = drained;
            production["authoritativePassRequired"] = true;
            production["message"] = "The server must independently join both production occurrences to the public parentage evidence and exact male/female established-reputation values.";
            if (!drained)
                return LiveCommandResult.Failed(
                    "World History did not ingest the forced Unchaste production evidence before the profile deadline.",
                    production);
            return LiveCommandResult.Completed(
                "Produced public illegitimacy evidence for ordinary male and female lords through the native court-personality path; authoritative evaluation is pending.",
                production);
        }

        private static async Task<LiveCommandResult> PlayerParityProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            JObject production = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                List<Hero> ordinaryLords = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player && hero.IsLord
                        && hero.Clan?.Leader != hero && hero.Clan?.Kingdom?.Leader != hero)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Hero mother = player?.IsFemale == true
                    ? player : ordinaryLords.FirstOrDefault(hero => hero.IsFemale);
                Hero biologicalFather = player?.IsFemale == false
                    ? player : ordinaryLords.FirstOrDefault(hero => !hero.IsFemale);
                Hero legalFather = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && !hero.IsFemale
                        && hero != biologicalFather && hero != player)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (player == null || mother == null || biologicalFather == null || legalFather == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "Player parity requires the living adult player plus opposite-sex biological and distinct legal-parent fixtures."
                    };

                ReignFamilyCampaignBehavior family = ReignServices.Family;
                Hero child = null;
                string childError = string.Empty;
                if (family == null || !family.TryCreateSocialBalanceChild(
                    mother, biologicalFather, legalFather, true, false,
                    out child, out childError))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(childError)
                            ? "The Reign family behavior could not create player public-parentage evidence."
                            : childError
                    };

                ReignCourtPersonalityReputationCampaignBehavior producer =
                    ReignServices.CourtPersonalityReputation;
                int seasonIndex = Math.Max(0, (int)Math.Floor(
                    Math.Max(0d, CampaignTime.Now.ToDays) / ReignCalendarService.DaysPerSeason));
                string enrolledRunId = (command["enrollment"] as JObject)
                    ?.Value<string>("runId") ?? string.Empty;
                JObject playerEvidence = new JObject();
                string playerError = string.Empty;
                if (producer == null || !producer.RecordUnchasteForSocialBalance(
                    player, seasonIndex, enrolledRunId, out playerEvidence, out playerError))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(playerError)
                            ? "The native player Unchaste producer fixture failed."
                            : playerError
                    };

                JObject result = new JObject
                {
                    ["ok"] = true,
                    ["productionProducerUsed"] = true,
                    ["forcedRareBranch"] = true,
                    ["player"] = new JObject
                    {
                        ["heroId"] = player.StringId,
                        ["heroName"] = player.Name?.ToString() ?? player.StringId,
                        ["isFemale"] = player.IsFemale,
                        ["isPlayer"] = true,
                        ["isRuler"] = player.Clan?.Kingdom?.Leader == player,
                        ["producerEvidence"] = playerEvidence
                    },
                    ["child"] = new JObject
                    {
                        ["childId"] = child.StringId,
                        ["motherId"] = mother.StringId,
                        ["biologicalFatherId"] = biologicalFather.StringId,
                        ["legalFatherId"] = legalFather.StringId,
                        ["publiclyKnown"] = true,
                        ["illegitimate"] = true
                    },
                    ["worldHistorySequence"] = ReignServices.WorldHistory?.Sequence ?? 0L
                };
                ReignServices.SocialBalanceHarness?.RecordOverride(
                    "player_unchaste_parity_forced_public_parentage", new JObject(result));
                return result;
            }).ConfigureAwait(false);

            if (production.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(
                    production.Value<string>("error") ?? "The native player parity fixture failed.",
                    production);
            long sequence = production.Value<long?>("worldHistorySequence") ?? 0L;
            bool drained = sequence > 0L && ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                sequence, TimeSpan.FromSeconds(120));
            production["worldHistoryDrained"] = drained;
            production["authoritativePassRequired"] = true;
            production["message"] = "The server must independently join the player's production Unchaste occurrence, prove exact gender parity with the passed NPC matrix, and require compatible player Flirt and Affair evidence.";
            if (!drained)
                return LiveCommandResult.Failed(
                    "World History did not ingest the forced player-parity evidence before the profile deadline.",
                    production);
            return LiveCommandResult.Completed(
                "Produced player public-parentage evidence through the native court-personality path; authoritative parity evaluation is pending.",
                production);
        }

        private static async Task<LiveCommandResult> NpcFavoringPresenceProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            JObject production = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                Hero ruler = Kingdom.All
                    .Where(kingdom => kingdom != null && !kingdom.IsEliminated)
                    .Select(kingdom => kingdom.Leader)
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                Hero favorite = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player && hero != ruler)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                Settlement fixtureSettlement = ruler?.CurrentSettlement
                    ?? ruler?.PartyBelongedTo?.CurrentSettlement
                    ?? Settlement.All.Where(settlement => settlement != null && settlement.IsTown)
                        .OrderBy(settlement => settlement.StringId, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                if (ruler == null || favorite == null || fixtureSettlement == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The NPC favoring profile requires a non-player ruler, a distinct adult favorite, and a native town fixture."
                    };

                string rulerBefore = ruler.CurrentSettlement?.StringId
                    ?? ruler.PartyBelongedTo?.CurrentSettlement?.StringId ?? string.Empty;
                string favoriteBefore = favorite.CurrentSettlement?.StringId
                    ?? favorite.PartyBelongedTo?.CurrentSettlement?.StringId ?? string.Empty;
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(ruler, fixtureSettlement);
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(favorite, fixtureSettlement);
                string enrolledRunId = (command["enrollment"] as JObject)
                    ?.Value<string>("runId") ?? string.Empty;
                ReignCourtPersonalityReputationCampaignBehavior producer =
                    ReignServices.CourtPersonalityReputation;
                JObject evidence = new JObject();
                string error = string.Empty;
                if (producer == null || !producer.RunNpcRulerFavoringPresenceForSocialBalance(
                    ruler, favorite, enrolledRunId, out evidence, out error))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(error)
                            ? "The native NPC ruler-favoring presence fixture failed."
                            : error,
                        ["producerEvidence"] = evidence
                    };
                evidence["rulerBeforeSettlementId"] = rulerBefore;
                evidence["favoriteBeforeSettlementId"] = favoriteBefore;
                evidence["fixtureSettlementId"] = fixtureSettlement.StringId;
                evidence["authoritativePassRequired"] = true;
                return evidence;
            }).ConfigureAwait(false);

            if (production.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(
                    production.Value<string>("error")
                        ?? "The native NPC ruler-favoring presence fixture failed.",
                    production);
            long sequence = production.Value<long?>("worldHistorySequence") ?? 0L;
            bool drained = sequence > 0L && ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                sequence, TimeSpan.FromSeconds(120));
            production["worldHistoryDrained"] = drained;
            production["message"] = "The server must independently bind the exact day-eleven event to a completed social-outcome receipt, the parameterized Ruler Favoring occurrence, and its clan-tier-scaled five-percent base chance.";
            if (!drained)
                return LiveCommandResult.Failed(
                    "World History did not ingest the exact NPC ruler-favoring event before the profile deadline.",
                    production);
            return LiveCommandResult.Completed(
                "Exercised the native NPC ruler-favoring presence state machine, separation reset, day-ten gate, and exact forced day-eleven rare branch; authoritative evaluation is pending.",
                production);
        }

        private static async Task<LiveCommandResult> FavoringProjectionProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            JObject production = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                Hero ruler = Kingdom.All
                    .Where(kingdom => kingdom != null && !kingdom.IsEliminated)
                    .Select(kingdom => kingdom.Leader)
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(hero => hero?.Clan?.Kingdom != null
                        && Hero.AllAliveHeroes.Count(candidate => IsSocialBalanceAdult(candidate)
                            && candidate != hero && candidate.Clan?.Kingdom == hero.Clan.Kingdom) >= 3);
                Kingdom kingdom = ruler?.Clan?.Kingdom;
                List<Hero> sameKingdomCandidates = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != ruler
                        && hero.Clan?.Kingdom == kingdom)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Hero foreignObserver = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player
                        && hero != ruler && hero.Clan?.Kingdom != null
                        && hero.Clan.Kingdom != kingdom)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                Settlement fixtureSettlement = ruler?.CurrentSettlement
                    ?? ruler?.PartyBelongedTo?.CurrentSettlement
                    ?? Settlement.All.Where(settlement => settlement != null && settlement.IsTown)
                        .OrderBy(settlement => settlement.StringId, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                if (ruler == null || sameKingdomCandidates.Count < 3
                    || foreignObserver == null || fixtureSettlement == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The favoring projection profile requires a non-player ruler, two same-kingdom favorites, a distinct same-kingdom observer, a foreign observer, and a native town fixture."
                    };

                TeleportHeroAction.ApplyImmediateTeleportToSettlement(ruler, fixtureSettlement);
                var colocatedFavorites = new List<Hero>();
                var relocationAttempts = new JArray();
                foreach (Hero candidate in sameKingdomCandidates)
                {
                    string beforeSettlementId = candidate.CurrentSettlement?.StringId
                        ?? candidate.PartyBelongedTo?.CurrentSettlement?.StringId
                        ?? candidate.PartyBelongedTo?.AttachedTo?.CurrentSettlement?.StringId
                        ?? string.Empty;
                    TeleportHeroAction.ApplyImmediateTeleportToSettlement(candidate, fixtureSettlement);
                    Settlement effectiveSettlement = candidate.CurrentSettlement
                        ?? candidate.PartyBelongedTo?.CurrentSettlement
                        ?? candidate.PartyBelongedTo?.AttachedTo?.CurrentSettlement;
                    bool colocated = effectiveSettlement == fixtureSettlement;
                    relocationAttempts.Add(new JObject
                    {
                        ["heroId"] = candidate.StringId,
                        ["beforeSettlementId"] = beforeSettlementId,
                        ["afterSettlementId"] = effectiveSettlement?.StringId ?? string.Empty,
                        ["colocated"] = colocated
                    });
                    if (colocated) colocatedFavorites.Add(candidate);
                    if (colocatedFavorites.Count >= 2) break;
                }
                Hero favoriteOne = colocatedFavorites.ElementAtOrDefault(0);
                Hero favoriteTwo = colocatedFavorites.ElementAtOrDefault(1);
                Hero nonFavorite = sameKingdomCandidates.FirstOrDefault(hero =>
                    hero != favoriteOne && hero != favoriteTwo);
                if (favoriteOne == null || favoriteTwo == null || nonFavorite == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The favoring projection profile could not co-locate two eligible same-kingdom favorites in the native town fixture.",
                        ["rulerId"] = ruler.StringId,
                        ["fixtureSettlementId"] = fixtureSettlement.StringId,
                        ["relocationAttempts"] = relocationAttempts
                    };
                string enrolledRunId = (command["enrollment"] as JObject)
                    ?.Value<string>("runId") ?? string.Empty;
                ReignCourtPersonalityReputationCampaignBehavior producer =
                    ReignServices.CourtPersonalityReputation;
                JObject first = new JObject();
                JObject second = new JObject();
                string firstError = string.Empty;
                if (producer == null || !producer.RunNpcRulerFavoringPresenceForSocialBalance(
                    ruler, favoriteOne, enrolledRunId, out first, out firstError))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(firstError)
                            ? "The first controlled favoring producer failed." : firstError,
                        ["firstProducerEvidence"] = first
                    };
                string secondError = string.Empty;
                if (!producer.RunNpcRulerFavoringPresenceForSocialBalance(
                    ruler, favoriteTwo, enrolledRunId, out second, out secondError))
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = string.IsNullOrWhiteSpace(secondError)
                            ? "The second controlled favoring producer failed." : secondError,
                        ["firstProducerEvidence"] = first,
                        ["secondProducerEvidence"] = second
                    };
                return new JObject
                {
                    ["ok"] = true,
                    ["productionStateMachineUsed"] = true,
                    ["rulerId"] = ruler.StringId,
                    ["rulerName"] = ruler.Name?.ToString() ?? ruler.StringId,
                    ["rulerClanTier"] = ruler.Clan?.Tier ?? 0,
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["favoriteOneId"] = favoriteOne.StringId,
                    ["favoriteTwoId"] = favoriteTwo.StringId,
                    ["nonFavoriteObserverId"] = nonFavorite.StringId,
                    ["foreignObserverId"] = foreignObserver.StringId,
                    ["fixtureSettlementId"] = fixtureSettlement.StringId,
                    ["relocationAttempts"] = relocationAttempts,
                    ["firstProducerEvidence"] = first,
                    ["secondProducerEvidence"] = second,
                    ["worldHistorySequence"] = Math.Max(
                        first.Value<long?>("worldHistorySequence") ?? 0L,
                        second.Value<long?>("worldHistorySequence") ?? 0L),
                    ["authoritativePassRequired"] = true
                };
            }).ConfigureAwait(false);

            if (production.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(
                    production.Value<string>("error")
                        ?? "The native favoring projection fixture failed.", production);
            long sequence = production.Value<long?>("worldHistorySequence") ?? 0L;
            bool drained = sequence > 0L && ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                sequence, TimeSpan.FromSeconds(120));
            production["worldHistoryDrained"] = drained;
            production["message"] = "The server must prove two parameterized favorites collapse to one global cost, resolve favorite/non-favorite/foreign observers, and avoid relationship-pair creation.";
            if (!drained)
                return LiveCommandResult.Failed(
                    "World History did not ingest both controlled favoring events before the profile deadline.",
                    production);
            return LiveCommandResult.Completed(
                "Produced two independent native Ruler Favoring reputations for observer-projection proof; authoritative evaluation is pending.",
                production);
        }

        private static async Task<LiveCommandResult> FavoringRebellionProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                ReignRebellionCampaignBehavior rebellion = ReignServices.Rebellion;
                if (rebellion == null)
                    return LiveCommandResult.Failed("The native rebellion behavior is unavailable.",
                        prerequisites.Data);
                Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
                Kingdom kingdom = Kingdom.All
                    .Where(value => value != null && !value.IsEliminated && value != playerKingdom
                        && value.RulingClan != null
                        && value.Clans.Count(clan => clan != null && !clan.IsEliminated
                            && IsSocialBalanceAdult(clan.Leader)) >= 2)
                    .OrderBy(value => value.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                Clan original = kingdom?.RulingClan;
                Clan successor = kingdom?.Clans
                    .Where(clan => clan != null && !clan.IsEliminated && clan != original
                        && IsSocialBalanceAdult(clan.Leader))
                    .OrderBy(clan => clan.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (kingdom == null || original == null || successor == null)
                    return LiveCommandResult.Failed(
                        "No non-player kingdom had an original ruler and eligible successor clan.",
                        prerequisites.Data);

                string enrolledRunId = (command["enrollment"] as JObject)
                    ?.Value<string>("runId") ?? string.Empty;
                string fixtureRunId = enrolledRunId + "-social-favoring-rebellion";
                string activeSave = ReignServerClient.ActiveNativeSaveName();
                JObject declaration = rebellion.RunPreparationHarnessProfile(
                    "declaration", fixtureRunId, activeSave);
                JObject resolution = rebellion.RunPreparationHarnessProfile(
                    "resolution", fixtureRunId, activeSave);
                string afterClanId = string.Empty;
                bool transitionVerified = false;
                bool restored = false;
                string transitionError = string.Empty;
                try
                {
                    ChangeRulingClanAction.Apply(kingdom, successor);
                    afterClanId = kingdom.RulingClan?.StringId ?? string.Empty;
                    transitionVerified = string.Equals(afterClanId, successor.StringId,
                        StringComparison.OrdinalIgnoreCase)
                        && kingdom.Leader == successor.Leader;
                }
                catch (Exception ex)
                {
                    transitionError = ex.Message;
                }
                finally
                {
                    try
                    {
                        if (kingdom.RulingClan != original)
                            ChangeRulingClanAction.Apply(kingdom, original);
                        restored = kingdom.RulingClan == original && kingdom.Leader == original.Leader;
                    }
                    catch (Exception ex)
                    {
                        transitionError = string.IsNullOrWhiteSpace(transitionError)
                            ? ex.Message : transitionError + " | restore: " + ex.Message;
                    }
                }
                JObject cleanup = rebellion.RunPreparationHarnessProfile(
                    "cleanup", fixtureRunId, activeSave);
                JObject evidence = prerequisites.Data?.DeepClone() as JObject ?? new JObject();
                evidence["kingdomId"] = kingdom.StringId;
                evidence["originalRulingClanId"] = original.StringId;
                evidence["originalRulerHeroId"] = original.Leader?.StringId ?? string.Empty;
                evidence["successorClanId"] = successor.StringId;
                evidence["successorHeroId"] = successor.Leader?.StringId ?? string.Empty;
                evidence["transitionedRulingClanId"] = afterClanId;
                evidence["nativeRulerTransitionVerified"] = transitionVerified;
                evidence["originalRulerRestored"] = restored;
                evidence["transitionError"] = transitionError;
                evidence["declarationHarness"] = declaration;
                evidence["resolutionHarness"] = resolution;
                evidence["cleanupHarness"] = cleanup;
                evidence["authoritativePassRequired"] = true;
                ReignServices.SocialBalanceHarness?.RecordOverride(
                    "favoring_rebellion_transition", new JObject
                    {
                        ["kingdomId"] = kingdom.StringId,
                        ["beforeClanId"] = original.StringId,
                        ["successorClanId"] = successor.StringId,
                        ["restored"] = restored,
                        ["fixtureRunId"] = fixtureRunId
                    });
                bool completed = transitionVerified && restored
                    && declaration.Value<bool?>("passed") == true
                    && resolution.Value<bool?>("passed") == true
                    && cleanup.Value<bool?>("passed") == true;
                return completed
                    ? LiveCommandResult.Completed(
                        "Exercised and restored one native ruler transition plus exact run-owned rebellion declaration and resolution fixtures; authoritative evaluation is pending.",
                        evidence)
                    : LiveCommandResult.Failed(
                        "The favoring/rebellion leadership-transition fixture did not satisfy every native assertion.",
                        evidence);
            }).ConfigureAwait(false);
        }
    }
}
