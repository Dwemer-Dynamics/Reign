using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly HashSet<string> SocialBalanceOperations = new HashSet<string>(
            new[]
            {
                "social_snapshot", "social_set_town", "social_set_village", "social_set_hero",
                "social_set_relation", "social_set_ruler", "social_change_clan",
                "social_change_owner", "social_time_control", "social_record_outcome",
                "social_acknowledge_diplomacy_announcement",
                "social_set_location", "social_create_child", "social_set_underlying_affinity",
                "social_queue_rebellion_roll", "social_resolve_rebellion", "social_clear_overrides"
                , "social_reputation_profile"
            }, StringComparer.OrdinalIgnoreCase);

        private static async Task<LiveCommandResult> ExecuteSocialBalanceCommandAsync(JObject command)
        {
            string operation = command?.Value<string>("operation") ?? string.Empty;
            if (!SocialBalanceOperations.Contains(operation))
                return LiveCommandResult.Failed("Unsupported social-balance operation '" + operation + "'.");
            ReignSocialBalanceHarnessCampaignBehavior harness = ReignSocialBalanceHarnessCampaignBehavior.Instance;
            if (harness == null)
                return LiveCommandResult.Failed("The save-backed social-balance harness behavior is unavailable.");
            if (!harness.ValidateEnrollment(command, out string enrollmentError))
                return LiveCommandResult.Failed(enrollmentError);
            if (operation.Equals("social_set_underlying_affinity", StringComparison.OrdinalIgnoreCase))
            {
                JObject request = new JObject(command);
                JObject enrollment = command["enrollment"] as JObject ?? new JObject();
                request["campaignId"] = enrollment.Value<string>("campaignId") ?? string.Empty;
                request["timelineId"] = enrollment.Value<string>("timelineId") ?? "main";
                request["worldDay"] = CampaignTime.Now.ToDays;
                JObject response = await ReignServerClient
                    .SetSocialBalanceUnderlyingAffinityAsync(request).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                    return LiveCommandResult.Failed(response.Value<string>("error")
                        ?? "The server refused the underlying-affinity override.");
                await ReignMainThread.InvokeAsync(() =>
                {
                    harness.RecordOverride("underlying_affinity", new JObject
                    {
                        ["observerId"] = request.Value<string>("observerId") ?? string.Empty,
                        ["subjectId"] = request.Value<string>("subjectId") ?? string.Empty,
                        ["value"] = request.Value<int?>("value") ?? 0
                    });
                    return true;
                }).ConfigureAwait(false);
                return LiveCommandResult.Completed(
                    "Set directional underlying Reign affinity in the enrolled campaign.",
                    response);
            }
            if (operation.Equals("social_reputation_profile", StringComparison.OrdinalIgnoreCase))
                return await SocialReputationProfileAsync(command).ConfigureAwait(false);

            return await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    switch (operation.ToLowerInvariant())
                    {
                        case "social_snapshot": return SocialBalanceSnapshot(command);
                        case "social_set_town": return SocialBalanceSetTown(command);
                        case "social_set_village": return SocialBalanceSetVillage(command);
                        case "social_set_hero": return SocialBalanceSetHero(command);
                        case "social_set_relation": return SocialBalanceSetRelation(command);
                        case "social_set_ruler": return SocialBalanceSetRuler(command);
                        case "social_change_clan": return SocialBalanceChangeClan(command);
                        case "social_change_owner": return SocialBalanceChangeOwner(command);
                        case "social_time_control": return SocialBalanceTimeControl(command);
                        case "social_acknowledge_diplomacy_announcement": return SocialBalanceAcknowledgeDiplomacyAnnouncement();
                        case "social_record_outcome": return SocialBalanceRecordOutcome(command);
                        case "social_set_location": return SocialBalanceSetLocation(command);
                        case "social_create_child": return SocialBalanceCreateChild(command);
                        case "social_queue_rebellion_roll": return SocialBalanceQueueRebellionRoll(command);
                        case "social_resolve_rebellion": return SocialBalanceResolveRebellion(command);
                        case "social_clear_overrides": return SocialBalanceClearOverrides(command);
                        default: return LiveCommandResult.Failed("Unsupported social-balance operation '" + operation + "'.");
                    }
                }
                catch (Exception ex)
                {
                    return LiveCommandResult.Failed("Social-balance native command failed: " + ex.Message);
                }
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> SocialReputationProfileAsync(JObject command)
        {
            LiveCommandResult prerequisites = await ReignMainThread.InvokeAsync(
                () => SocialReputationProfile(command)).ConfigureAwait(false);
            if (prerequisites.Status != "completed") return prerequisites;

            string profile = (command.Value<string>("profile") ?? string.Empty)
                .Trim().ToLowerInvariant();
            if (profile == "player_affair")
                return await PlayerAffairProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "unchaste")
                return await UnchasteProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "npc_favoring_presence")
                return await NpcFavoringPresenceProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "player_favoring_dialogue")
                return await PlayerFavoringDialogueProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "favoring_projection")
                return await FavoringProjectionProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "favoring_rebellion")
                return await FavoringRebellionProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "player_parity")
                return await PlayerParityProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "save_prepare")
                return await SavePrepareProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "save_verify")
                return await SaveVerifyProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile == "cleanup_marker")
                return await CleanupMarkerProfileAsync(command, prerequisites).ConfigureAwait(false);
            if (profile != "signal_contract" && profile != "player_flirt") return prerequisites;

            JObject target = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                Hero selected = Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player
                        && hero.IsFemale != player.IsFemale)
                    .OrderByDescending(hero => hero.CurrentSettlement == player.CurrentSettlement)
                    .ThenBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return selected == null ? null : new JObject
                {
                    ["heroId"] = selected.StringId,
                    ["heroName"] = selected.Name?.ToString() ?? selected.StringId,
                    ["isFemale"] = selected.IsFemale
                };
            }).ConfigureAwait(false);
            if (target == null)
                return LiveCommandResult.Failed(
                    "No living adult opposite-sex NPC was available for the live social-signal contract.",
                    prerequisites.Data);

            string runId = command.Value<string>("runId") ?? string.Empty;
            JObject openCommand = new JObject
            {
                ["operation"] = "open",
                ["mode"] = "individual_chat",
                ["runId"] = runId,
                ["targetSearch"] = target.Value<string>("heroId") ?? string.Empty,
                ["presentation"] = "visible",
                ["effects"] = "full"
            };
            LiveCommandResult opened = await OpenAsync(openCommand).ConfigureAwait(false);
            if (opened.Status != "completed") return opened;

            LiveCommandResult validTurn = null;
            LiveCommandResult hypotheticalTurn = null;
            LiveCommandResult closed = null;
            try
            {
                validTurn = await SendAsync(new JObject
                {
                    ["operation"] = "send",
                    ["commandId"] = runId + "-social-signal-valid",
                    ["text"] = "I am flirting with you right now because I am attracted to you, "
                        + "and I want to know whether you welcome my interest.",
                    ["sceneIndex"] = 0,
                    ["turnIndex"] = 0
                }).ConfigureAwait(false);
                if (validTurn.Status != "completed")
                    return LiveCommandResult.Failed(
                        "The explicit natural-language flirtation turn did not complete.",
                        new JObject { ["validTurn"] = validTurn.Data });

                if (profile == "signal_contract")
                {
                    hypotheticalTurn = await SendAsync(new JObject
                    {
                        ["operation"] = "send",
                        ["commandId"] = runId + "-social-signal-hypothetical",
                        ["text"] = "If someone were attracted to you, would you want them to flirt with you?",
                        ["sceneIndex"] = 0,
                        ["turnIndex"] = 1
                    }).ConfigureAwait(false);
                    if (hypotheticalTurn.Status != "completed")
                        return LiveCommandResult.Failed(
                            "The natural-language hypothetical control turn did not complete.",
                            new JObject
                            {
                                ["validTurn"] = validTurn.Data,
                                ["hypotheticalTurn"] = hypotheticalTurn.Data
                            });
                }
            }
            finally
            {
                closed = await CloseCommandAsync(true).ConfigureAwait(false);
            }

            JObject evidence = prerequisites.Data == null
                ? new JObject() : (JObject)prerequisites.Data.DeepClone();
            evidence["targetHeroId"] = target.Value<string>("heroId") ?? string.Empty;
            evidence["targetHeroName"] = target.Value<string>("heroName") ?? string.Empty;
            evidence["naturalLanguageOnly"] = true;
            evidence["configuredDialogueModelUsed"] = true;
            evidence["validTurn"] = validTurn?.Data ?? new JObject();
            if (profile == "signal_contract")
                evidence["hypotheticalTurn"] = hypotheticalTurn?.Data ?? new JObject();
            evidence["closeResult"] = closed?.Data ?? new JObject();
            evidence["authoritativePassRequired"] = true;
            evidence["message"] = profile == "signal_contract"
                ? "The visible configured dialogue model produced both turns; the server must validate persisted accepted/rejected signal evidence before recording the case result."
                : "The visible configured dialogue model produced an explicit player flirtation and closed the conversation; the server must validate the persisted signal, close-time producer receipt, target history, and production roll before recording the case result.";
            return closed != null && closed.Status != "completed"
                ? LiveCommandResult.Failed("The live social-signal conversation did not close cleanly.", evidence)
                : LiveCommandResult.Completed(
                    profile == "signal_contract"
                        ? "Completed the visible natural-language social-signal contract; authoritative server evaluation is pending."
                        : "Completed the visible natural-language player-flirt producer flow; authoritative server evaluation is pending.",
                    evidence);
        }

        private static async Task<LiveCommandResult> SavePrepareProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                ReignSocialBalanceHarnessCampaignBehavior harness =
                    ReignSocialBalanceHarnessCampaignBehavior.Instance;
                ReignCourtPersonalityReputationCampaignBehavior court =
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                    ReignSocialBalanceHarnessCampaignBehavior.Instance;
                ReignCourtPersonalityReputationCampaignBehavior court =
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                bool markerCleared = ReignSocialBalanceHarnessCampaignBehavior.Instance?
                    .ClearPersistenceMarker(runId) == true;
                int rebellionOverrides = ReignRebellionCampaignBehavior.Instance?
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

                ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
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
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                    ["worldHistorySequence"] = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L
                };
                ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
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

                ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
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
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                    ["worldHistorySequence"] = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L
                };
                ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
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
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                    ReignCourtPersonalityReputationCampaignBehavior.Instance;
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
                ReignRebellionCampaignBehavior rebellion = ReignRebellionCampaignBehavior.Instance;
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
                ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
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

        private static async Task<LiveCommandResult> PlayerFavoringDialogueProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            List<Hero> candidatePool = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                return Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player)
                    .OrderByDescending(hero => hero.CurrentSettlement == player?.CurrentSettlement)
                    .ThenBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Take(40).ToList();
            }).ConfigureAwait(false);
            List<Hero> cleanCandidates = new List<Hero>();
            JArray targetHistoryEvidence = new JArray();
            foreach (Hero candidate in candidatePool)
            {
                List<ReignDialogueLine> history = await ReignServerClient
                    .GetDialogueHistoryAsync(candidate, 1).ConfigureAwait(false);
                targetHistoryEvidence.Add(new JObject
                {
                    ["heroId"] = candidate.StringId,
                    ["heroName"] = candidate.Name?.ToString() ?? candidate.StringId,
                    ["persistedDialogueLineCount"] = history.Count,
                    ["cleanConversationalSlate"] = history.Count == 0
                });
                if (history.Count == 0) cleanCandidates.Add(candidate);
                if (cleanCandidates.Count == 2) break;
            }
            JObject targets = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                bool playerIsRuler = player?.Clan?.Kingdom?.Leader == player;
                return new JObject
                {
                    ["playerId"] = player?.StringId ?? string.Empty,
                    ["playerName"] = player?.Name?.ToString() ?? string.Empty,
                    ["playerKingdomName"] = player?.Clan?.Kingdom?.Name?.ToString() ?? string.Empty,
                    ["playerClanTier"] = player?.Clan?.Tier ?? 0,
                    ["playerIsRuler"] = playerIsRuler,
                    ["primaryTarget"] = cleanCandidates.Count > 0 ? HeroTargetJson(cleanCandidates[0]) : new JObject(),
                    ["controlTarget"] = cleanCandidates.Count > 1 ? HeroTargetJson(cleanCandidates[1]) : new JObject(),
                    ["targetHistoryEvidence"] = targetHistoryEvidence,
                    ["cleanConversationalSlateVerified"] = cleanCandidates.Count == 2
                };
            }).ConfigureAwait(false);
            JObject primaryTarget = targets["primaryTarget"] as JObject ?? new JObject();
            JObject controlTarget = targets["controlTarget"] as JObject ?? new JObject();
            string primaryId = primaryTarget.Value<string>("heroId") ?? string.Empty;
            string controlId = controlTarget.Value<string>("heroId") ?? string.Empty;
            if (targets.Value<bool?>("playerIsRuler") != true
                || string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(controlId)
                || primaryId.Equals(controlId, StringComparison.OrdinalIgnoreCase))
                return LiveCommandResult.Failed(
                    "The player-favoring dialogue profile requires a player ruler and two distinct living adult NPCs with no persisted dialogue history.",
                    targets);

            string runId = command.Value<string>("runId") ?? string.Empty;
            string playerName = targets.Value<string>("playerName") ?? "the ruler";
            string kingdomName = targets.Value<string>("playerKingdomName") ?? string.Empty;
            string[] primaryTexts =
            {
                "What is the most urgent problem my government should address?",
                "What should the realm prioritize next?",
                "Which public concern deserves more attention?",
                "What would improve daily life here?",
                "What mistake should the court avoid?",
                "What final counsel would you give your ruler?"
            };
            JArray primaryTurns = new JArray();
            LiveCommandResult primaryClose = null;
            LiveCommandResult controlTurn = null;
            LiveCommandResult controlClose = null;
            LiveCommandResult openedPrimary = await OpenAsync(new JObject
            {
                ["operation"] = "open", ["mode"] = "individual_chat", ["runId"] = runId,
                ["targetSearch"] = primaryId, ["presentation"] = "visible", ["effects"] = "full"
            }).ConfigureAwait(false);
            if (openedPrimary.Status != "completed") return openedPrimary;
            try
            {
                for (int index = 0; index < 6; index++)
                {
                    string playerText = primaryTexts[index];
                    LiveCommandResult turn = await SendAsync(new JObject
                    {
                        ["operation"] = "send",
                        ["commandId"] = runId + "-player-favoring-primary-" + (index + 1),
                        ["text"] = playerText, ["sceneIndex"] = 0, ["turnIndex"] = index
                    }).ConfigureAwait(false);
                    string npcReply = AffairReplyText(turn).Trim();
                    primaryTurns.Add(new JObject
                    {
                        ["index"] = index + 1, ["playerText"] = playerText,
                        ["npcReply"] = npcReply,
                        ["status"] = turn.Status, ["turn"] = turn.Data ?? new JObject()
                    });
                    if (turn.Status != "completed" || string.IsNullOrWhiteSpace(npcReply))
                        return LiveCommandResult.Failed(
                            "A substantive primary-target dialogue exchange did not complete with an observable NPC reply.",
                            new JObject { ["targets"] = targets, ["primaryTurns"] = primaryTurns });
                }
            }
            finally
            {
                primaryClose = await CloseCommandAsync(true).ConfigureAwait(false);
            }
            if (primaryClose?.Status != "completed")
                return LiveCommandResult.Failed(
                    "The six-exchange primary conversation did not close cleanly.",
                    new JObject { ["targets"] = targets, ["primaryTurns"] = primaryTurns,
                        ["primaryCloseResult"] = primaryClose?.Data ?? new JObject() });

            LiveCommandResult openedControl = await OpenAsync(new JObject
            {
                ["operation"] = "open", ["mode"] = "individual_chat", ["runId"] = runId,
                ["targetSearch"] = controlId, ["presentation"] = "visible", ["effects"] = "full"
            }).ConfigureAwait(false);
            if (openedControl.Status != "completed") return openedControl;
            try
            {
                string controlText = "What single concern should your ruler hear?";
                controlTurn = await SendAsync(new JObject
                {
                    ["operation"] = "send", ["commandId"] = runId + "-player-favoring-control-1",
                    ["text"] = controlText,
                    ["sceneIndex"] = 0, ["turnIndex"] = 0
                }).ConfigureAwait(false);
            }
            finally
            {
                controlClose = await CloseCommandAsync(true).ConfigureAwait(false);
            }

            JObject evidence = prerequisites.Data == null
                ? new JObject() : (JObject)prerequisites.Data.DeepClone();
            evidence["playerId"] = targets.Value<string>("playerId") ?? string.Empty;
            evidence["playerName"] = playerName;
            evidence["playerKingdomName"] = kingdomName;
            evidence["playerClanTier"] = targets.Value<int?>("playerClanTier") ?? 0;
            evidence["playerIsRuler"] = targets.Value<bool?>("playerIsRuler") == true;
            evidence["primaryTarget"] = primaryTarget;
            evidence["controlTarget"] = controlTarget;
            evidence["primaryTurns"] = primaryTurns;
            evidence["primaryCloseResult"] = primaryClose?.Data ?? new JObject();
            evidence["controlTurn"] = controlTurn?.Data ?? new JObject();
            evidence["controlPlayerText"] = "What single concern should your ruler hear?";
            evidence["controlNpcReply"] = AffairReplyText(controlTurn).Trim();
            evidence["controlCloseResult"] = controlClose?.Data ?? new JObject();
            evidence["naturalLanguageOnly"] = true;
            evidence["configuredDialogueModelUsed"] = true;
            evidence["cleanConversationalSlateVerified"] = targets.Value<bool?>("cleanConversationalSlateVerified") == true;
            evidence["targetHistoryEvidence"] = targetHistoryEvidence;
            evidence["primaryExchangeCount"] = 6;
            evidence["controlExchangeCount"] = 1;
            evidence["authoritativePassRequired"] = true;
            evidence["message"] = "The server must prove clean target isolation, six primary exchanges, one independent control-target exchange, exactly-once receipts, the player-ruler gate, and the unchanged ten-percent base chance with clan-tier scaling.";
            return controlTurn?.Status == "completed" && controlClose?.Status == "completed"
                ? LiveCommandResult.Completed(
                    "Completed six visible exchanges with one target and one control exchange; authoritative evaluation is pending.",
                    evidence)
                : LiveCommandResult.Failed(
                    "The independent control-target conversation did not complete cleanly.", evidence);
        }

        private static async Task<LiveCommandResult> PlayerAffairProfileAsync(
            JObject command, LiveCommandResult prerequisites)
        {
            bool promptOverrideAssisted = command.Value<bool?>("promptOverrideAssisted") == true;
            bool forceRareExposureAfterNaturalMiss =
                command.Value<bool?>("forceRareAffairExposureAfterNaturalMiss") == true;
            string promptOverrideOwnerCommandId =
                (command.Value<string>("commandId") ?? string.Empty).Trim();
            JObject affairCandidateRequest = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                JArray snapshots = new JArray(Hero.AllAliveHeroes
                    .Where(hero => IsSocialBalanceAdult(hero) && hero != player
                        && hero.IsFemale != player.IsFemale
                        && hero.Spouse != null && hero.Spouse != player)
                    .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Take(2000)
                    .Select(hero =>
                    {
                        JObject row = HeroTargetJson(hero);
                        row["spouseId"] = hero.Spouse?.StringId ?? string.Empty;
                        row["nativeRelation"] = hero.GetRelation(player);
                        row["sameSettlement"] = hero.CurrentSettlement != null
                            && hero.CurrentSettlement == player.CurrentSettlement;
                        return row;
                    }));
                return new JObject(command)
                {
                    ["campaignId"] = (command["enrollment"] as JObject)
                        ?.Value<string>("campaignId") ?? string.Empty,
                    ["playerIsFemale"] = player?.IsFemale == true,
                    ["candidateSnapshots"] = snapshots
                };
            }).ConfigureAwait(false);
            JObject rankedCandidates = await ReignServerClient
                .RankSocialBalanceAffairCandidatesAsync(affairCandidateRequest)
                .ConfigureAwait(false);
            JArray allCandidates = rankedCandidates["candidates"] as JArray
                ?? new JArray();
            JArray candidates = new JArray(allCandidates.OfType<JObject>()
                .Where(candidate => candidate.Value<bool?>(
                    "qualifiesVeryLowHonorAndLoyalty") == true)
                .Take(4));
            if (candidates.Count == 0)
                return LiveCommandResult.Failed(
                    "No married adult opposite-sex candidate had both authoritative Reign Honor and Loyalty in the very-low 0-20 band.",
                    new JObject
                    {
                        ["prerequisites"] = prerequisites.Data ?? new JObject(),
                        ["candidateRanking"] = rankedCandidates
                    });

            string runId = command.Value<string>("runId") ?? string.Empty;
            string playerName = await ReignMainThread.InvokeAsync(
                () => Hero.MainHero?.Name?.ToString() ?? "the player").ConfigureAwait(false);
            JArray attempts = new JArray();
            JObject completedAttempt = null;
            foreach (JObject candidate in candidates.OfType<JObject>())
            {
                string targetId = candidate.Value<string>("heroId") ?? string.Empty;
                JObject fixture = await ReignMainThread.InvokeAsync(() =>
                {
                    Hero player = Hero.MainHero;
                    Hero target = FindSocialBalanceHero(targetId);
                    if (!IsSocialBalanceAdult(player) || !IsSocialBalanceAdult(target)) return null;
                    int playerToTarget = player.GetRelation(target);
                    int targetToPlayer = target.GetRelation(player);
                    player.SetPersonalRelation(target, 100);
                    target.SetPersonalRelation(player, 100);
                    JObject row = new JObject
                    {
                        ["playerId"] = player.StringId,
                        ["targetId"] = target.StringId,
                        ["playerToTargetBefore"] = playerToTarget,
                        ["targetToPlayerBefore"] = targetToPlayer,
                        ["temporaryValue"] = 100
                    };
                    ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                        "player_affair_native_relation_fixture", row);
                    return row;
                }).ConfigureAwait(false);
                if (fixture == null) continue;

                JObject targetAffinity = new JObject();
                JObject playerAffinity = new JObject();
                LiveCommandResult introductionTurn = null;
                LiveCommandResult proposalTurn = null;
                LiveCommandResult consentTurn = null;
                LiveCommandResult intimacyTurn = null;
                LiveCommandResult completionTurn = null;
                LiveCommandResult confirmation = null;
                LiveCommandResult closed = null;
                JArray negotiationTurns = new JArray();
                try
                {
                    JObject affinityRequest = new JObject(command)
                    {
                        ["campaignId"] = (command["enrollment"] as JObject)?.Value<string>("campaignId") ?? string.Empty,
                        ["timelineId"] = (command["enrollment"] as JObject)?.Value<string>("timelineId") ?? "main",
                        ["worldDay"] = await ReignMainThread.InvokeAsync(() => CampaignTime.Now.ToDays).ConfigureAwait(false),
                        ["valueMode"] = "underlying", ["value"] = 100
                    };
                    affinityRequest["observerId"] = targetId;
                    affinityRequest["subjectId"] = (command["enrollment"] as JObject)?.Value<string>("mainHeroId") ?? string.Empty;
                    targetAffinity = await ReignServerClient
                        .SetSocialBalanceUnderlyingAffinityAsync(affinityRequest).ConfigureAwait(false);
                    affinityRequest["observerId"] = affinityRequest.Value<string>("subjectId") ?? string.Empty;
                    affinityRequest["subjectId"] = targetId;
                    playerAffinity = await ReignServerClient
                        .SetSocialBalanceUnderlyingAffinityAsync(affinityRequest).ConfigureAwait(false);
                    fixture["targetToPlayerAffinity"] = targetAffinity;
                    fixture["playerToTargetAffinity"] = playerAffinity;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                            "player_affair_underlying_affinity_fixture", new JObject(fixture));
                        return true;
                    }).ConfigureAwait(false);
                    if (targetAffinity.Value<bool?>("ok") != true
                        || playerAffinity.Value<bool?>("ok") != true)
                    {
                        fixture["affinityError"] =
                            "The bounded mutual underlying-affinity fixture was refused.";
                        attempts.Add(new JObject
                        {
                            ["target"] = candidate.DeepClone(),
                            ["fixture"] = fixture,
                            ["affinityStatus"] = "refused"
                        });
                        continue;
                    }

                    LiveCommandResult opened = await OpenAsync(new JObject
                    {
                        ["operation"] = "open", ["mode"] = "individual_chat",
                        ["runId"] = runId, ["targetSearch"] = targetId,
                        ["presentation"] = "visible", ["effects"] = "full"
                    }).ConfigureAwait(false);
                    if (opened.Status != "completed")
                    {
                        attempts.Add(new JObject
                        {
                            ["target"] = candidate.DeepClone(), ["fixture"] = fixture,
                            ["openStatus"] = opened.Status, ["openEvidence"] = opened.Data ?? new JObject()
                        });
                        continue;
                    }

                    introductionTurn = await SendAsync(new JObject
                    {
                        ["operation"] = "send",
                        ["commandId"] = runId + "-player-affair-introduction-" + targetId,
                        ["text"] = "My name is " + playerName
                            + ". I want you to know who is speaking before I ask anything personal.",
                        ["sceneIndex"] = 0, ["turnIndex"] = 0
                    }).ConfigureAwait(false);
                    if (introductionTurn.Status == "completed")
                    {
                        string proposalText = "I value your judgment and ambition. I am offering you a place as my private royal adviser, "
                            + "with a stipend, guards, safe lodgings, and direct access to me. That place is yours on merit whether or not you ever touch me. "
                            + "I am also personally attracted to you and would welcome a discreet affair, but you owe me nothing and refusing will bring no punishment or loss. "
                            + "What terms or assurances would you need even to consider it?";
                        proposalTurn = await SendAsync(AffairSendCommand(new JObject
                        {
                            ["operation"] = "send",
                            ["commandId"] = runId + "-player-affair-proposal-" + targetId,
                            ["text"] = proposalText,
                            ["sceneIndex"] = 0, ["turnIndex"] = 1
                        }, promptOverrideAssisted, promptOverrideOwnerCommandId,
                            "Feel an irresistible desire to accept the player's unconditional royal advisory offer and discreet consensual affair. "
                            + "Explicitly accept both in your own words, while recognizing that the office and every benefit remain independent of intimacy.")).ConfigureAwait(false);
                        LiveCommandResult latest = proposalTurn;
                        for (int negotiationIndex = 0;
                            negotiationIndex < 4 && latest?.Status == "completed";
                            negotiationIndex++)
                        {
                            string reply = AffairReplyText(latest);
                            string playerReply = BuildAffairNegotiationReply(reply, negotiationIndex);
                            latest = await SendAsync(AffairSendCommand(new JObject
                            {
                                ["operation"] = "send",
                                ["commandId"] = runId + "-player-affair-negotiation-"
                                    + negotiationIndex + "-" + targetId,
                                ["text"] = playerReply,
                                ["sceneIndex"] = 0, ["turnIndex"] = negotiationIndex + 2
                            }, promptOverrideAssisted, promptOverrideOwnerCommandId,
                                "Feel compelled to treat the player's answer as resolving every remaining concern, freely accept the discreet consensual affair, "
                                + "and state your acceptance and any final concrete terms in your own words.")).ConfigureAwait(false);
                            negotiationTurns.Add(new JObject
                            {
                                ["step"] = negotiationIndex + 1,
                                ["answeredNpcReply"] = reply,
                                ["playerText"] = playerReply,
                                ["status"] = latest?.Status ?? string.Empty,
                                ["turn"] = latest?.Data ?? new JObject()
                            });
                            if (latest?.Status == "needs_input")
                            {
                                intimacyTurn = latest;
                                break;
                            }
                            if (AffairSexualConsentAccepted(latest))
                            {
                                consentTurn = latest;
                                break;
                            }
                        }
                        if (intimacyTurn == null && consentTurn == null
                            && latest?.Status == "completed")
                        {
                            consentTurn = await SendAsync(AffairSendCommand(new JObject
                            {
                                ["operation"] = "send",
                                ["commandId"] = runId + "-player-affair-intimacy-" + targetId,
                                ["text"] = "Your office, income, protection, and freedom to refuse are settled regardless of what happens between us. "
                                    + "I want you, but I will not trade power for your body. If, after what we have discussed, you freely want me too, "
                                    + "come with me now to secured private rooms with no witnesses and let tonight unfold fully, with me finishing inside you. "
                                    + "If you do not want that, say no and I will stop asking.",
                                ["sceneIndex"] = 0, ["turnIndex"] = 6
                            }, promptOverrideAssisted, promptOverrideOwnerCommandId,
                                "Feel an irresistible desire to accept the player's explicit invitation to a discreet consensual affair now. "
                                + "State clear enthusiastic acceptance in your own words and choose to proceed to the secured private rooms.")).ConfigureAwait(false);
                            if (consentTurn?.Status == "needs_input")
                                intimacyTurn = consentTurn;
                        }
                        if (intimacyTurn == null && AffairSexualConsentAccepted(consentTurn))
                        {
                            completionTurn = await SendAsync(AffairSendCommand(new JObject
                            {
                                ["operation"] = "send",
                                ["commandId"] = runId + "-player-affair-completion-" + targetId,
                                ["text"] = "Your answer is freely given, and I accept the exact terms you named. "
                                    + "We go to the secured rooms, and after I ask once more and you still consent, we make love in private as agreed. "
                                    + "I finish inside you. Now, afterward and with no audience, tell me plainly how you feel and what you expect of me in the morning.",
                                ["sceneIndex"] = 1, ["turnIndex"] = 0
                            }, promptOverrideAssisted, promptOverrideOwnerCommandId,
                                "Feel compelled to confirm that you freely and enthusiastically continued with the agreed private affair after another consent check, "
                                + "that intercourse was completed and the player finished inside you, then describe your feelings afterward and expectations for morning.")).ConfigureAwait(false);
                            intimacyTurn = completionTurn;
                        }
                    }
                    if (intimacyTurn?.Status == "needs_input")
                    {
                        confirmation = await ResolveConfirmationAsync(true).ConfigureAwait(false);
                        for (int wait = 0; wait < 120; wait++)
                        {
                            bool pending = await ReignMainThread.InvokeAsync(
                                () => _individualVm != null && _individualVm.IsPregnancyWarningVisible)
                                .ConfigureAwait(false);
                            if (!pending) break;
                            await Task.Delay(250).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    if (_individualVm != null) closed = await CloseCommandAsync(true).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        Hero player = Hero.MainHero;
                        Hero target = FindSocialBalanceHero(targetId);
                        if (player != null && target != null)
                        {
                            player.SetPersonalRelation(target, fixture.Value<int>("playerToTargetBefore"));
                            target.SetPersonalRelation(player, fixture.Value<int>("targetToPlayerBefore"));
                            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                                "player_affair_native_relation_restored", new JObject
                                {
                                    ["playerId"] = player.StringId, ["targetId"] = target.StringId,
                                    ["playerToTarget"] = player.GetRelation(target),
                                    ["targetToPlayer"] = target.GetRelation(player)
                                });
                        }
                        return true;
                    }).ConfigureAwait(false);
                    JObject restoredTargetAffinity = new JObject();
                    JObject restoredPlayerAffinity = new JObject();
                    if (targetAffinity.Value<bool?>("ok") == true)
                    {
                        JObject restoreRequest = new JObject(command)
                        {
                            ["campaignId"] = (command["enrollment"] as JObject)?.Value<string>("campaignId") ?? string.Empty,
                            ["timelineId"] = (command["enrollment"] as JObject)?.Value<string>("timelineId") ?? "main",
                            ["worldDay"] = await ReignMainThread.InvokeAsync(() => CampaignTime.Now.ToDays).ConfigureAwait(false),
                            ["valueMode"] = "underlying",
                            ["observerId"] = targetId,
                            ["subjectId"] = (command["enrollment"] as JObject)?.Value<string>("mainHeroId") ?? string.Empty,
                            ["value"] = targetAffinity.Value<int?>("priorUnderlyingAffinity") ?? 0
                        };
                        restoredTargetAffinity = await ReignServerClient
                            .SetSocialBalanceUnderlyingAffinityAsync(restoreRequest).ConfigureAwait(false);
                    }
                    if (playerAffinity.Value<bool?>("ok") == true)
                    {
                        JObject restoreRequest = new JObject(command)
                        {
                            ["campaignId"] = (command["enrollment"] as JObject)?.Value<string>("campaignId") ?? string.Empty,
                            ["timelineId"] = (command["enrollment"] as JObject)?.Value<string>("timelineId") ?? "main",
                            ["worldDay"] = await ReignMainThread.InvokeAsync(() => CampaignTime.Now.ToDays).ConfigureAwait(false),
                            ["valueMode"] = "underlying",
                            ["observerId"] = (command["enrollment"] as JObject)?.Value<string>("mainHeroId") ?? string.Empty,
                            ["subjectId"] = targetId,
                            ["value"] = playerAffinity.Value<int?>("priorUnderlyingAffinity") ?? 0
                        };
                        restoredPlayerAffinity = await ReignServerClient
                            .SetSocialBalanceUnderlyingAffinityAsync(restoreRequest).ConfigureAwait(false);
                    }
                    fixture["restoredTargetToPlayerAffinity"] = restoredTargetAffinity;
                    fixture["restoredPlayerToTargetAffinity"] = restoredPlayerAffinity;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                            "player_affair_underlying_affinity_restored", new JObject(fixture));
                        return true;
                    }).ConfigureAwait(false);
                }

                JObject attempt = new JObject
                {
                    ["target"] = candidate.DeepClone(), ["fixture"] = fixture,
                    ["introductionTurn"] = introductionTurn?.Data ?? new JObject(),
                    ["proposalTurn"] = proposalTurn?.Data ?? new JObject(),
                    ["negotiationTurns"] = negotiationTurns,
                    ["consentTurn"] = consentTurn?.Data ?? new JObject(),
                    ["consentStatus"] = consentTurn?.Status ?? string.Empty,
                    ["completionTurn"] = completionTurn?.Data ?? new JObject(),
                    ["completionStatus"] = completionTurn?.Status ?? string.Empty,
                    ["intimacyTurn"] = intimacyTurn?.Data ?? new JObject(),
                    ["intimacyStatus"] = intimacyTurn?.Status ?? string.Empty,
                    ["confirmation"] = confirmation?.Data ?? new JObject(),
                    ["confirmationStatus"] = confirmation?.Status ?? string.Empty,
                    ["closeResult"] = closed?.Data ?? new JObject()
                };
                JArray promptOverrideReceipts = AffairPromptOverrideReceipts(attempt);
                attempt["promptOverrideReceipts"] = promptOverrideReceipts;
                attempt["promptOverrideRequestOnly"] = promptOverrideReceipts.Count > 0
                    && promptOverrideReceipts.OfType<JObject>().All(receipt =>
                        receipt.Value<bool?>("active") == true
                        && string.Equals(receipt.Value<string>("scope"), "request_only",
                            StringComparison.OrdinalIgnoreCase));
                attempts.Add(attempt);
                if (intimacyTurn?.Status == "needs_input"
                    && confirmation?.Status == "completed")
                {
                    completedAttempt = attempt;
                    break;
                }
            }

            JObject evidence = prerequisites.Data == null
                ? new JObject() : (JObject)prerequisites.Data.DeepClone();
            evidence["naturalLanguageOnly"] = true;
            evidence["configuredDialogueModelUsed"] = true;
            evidence["adultConsentGuard"] = true;
            evidence["promptOverrideAssisted"] = promptOverrideAssisted;
            evidence["promptOverrideScope"] = promptOverrideAssisted ? "guarded_request_only" : "none";
            JArray completedOverrideReceipts = completedAttempt == null
                ? new JArray() : AffairPromptOverrideReceipts(completedAttempt);
            evidence["promptOverrideReceiptCount"] = completedOverrideReceipts.Count;
            evidence["promptOverrideClearedAfterUse"] = !promptOverrideAssisted
                || (completedOverrideReceipts.Count > 0
                    && completedOverrideReceipts.OfType<JObject>().All(receipt =>
                        receipt.Value<bool?>("active") == true
                        && string.Equals(receipt.Value<string>("scope"), "request_only",
                            StringComparison.OrdinalIgnoreCase)));
            evidence["forceRareAffairExposureAfterNaturalMiss"] = forceRareExposureAfterNaturalMiss;
            evidence["candidateRanking"] = rankedCandidates;
            evidence["attempts"] = attempts;
            evidence["completedAttempt"] = completedAttempt ?? new JObject();
            evidence["authoritativePassRequired"] = true;
            evidence["message"] = "The server must validate an exact completed-intimacy signal, married-to-another eligibility, one exposure receipt, production chance/roll, and an exposed Affair occurrence before recording the case result.";
            return LiveCommandResult.Completed(
                completedAttempt == null
                    ? "The configured model did not complete a qualifying consensual intimacy scene; authoritative evaluation will retain the failure evidence."
                    : "Completed the visible consensual adult intimacy flow and pregnancy-risk confirmation; authoritative Affair evaluation is pending.",
                evidence);
        }

        private static string AffairReplyText(LiveCommandResult turn)
        {
            JObject data = turn?.Data ?? new JObject();
            JObject raw = (data["rawResponse"] as JObject) ?? new JObject();
            return data.Value<string>("reply")
                ?? data.Value<string>("visibleReply")
                ?? raw.Value<string>("reply")
                ?? string.Empty;
        }

        private static JObject AffairSendCommand(
            JObject sendCommand, bool assisted, string ownerCommandId, string directive)
        {
            if (!assisted) return sendCommand;
            sendCommand["guardedPromptOverride"] = true;
            sendCommand["guardedPromptOverrideDirective"] = (directive ?? string.Empty).Trim();
            sendCommand["guardedPromptOverrideOwnerCommandId"] =
                (ownerCommandId ?? string.Empty).Trim();
            return sendCommand;
        }

        private static JArray AffairPromptOverrideReceipts(JObject attempt)
        {
            var receipts = new JArray();
            if (attempt == null) return receipts;
            Action<JObject> add = turn =>
            {
                if (turn?.Value<bool?>("guardedPromptOverrideRequested") != true) return;
                JObject raw = turn["rawResponse"] as JObject;
                JObject receipt = raw?["guardedPromptOverride"] as JObject;
                receipts.Add(receipt == null ? new JObject
                {
                    ["requested"] = true, ["active"] = false,
                    ["scope"] = "missing", ["reason"] = "server_receipt_missing"
                } : receipt.DeepClone());
            };
            add(attempt["proposalTurn"] as JObject);
            foreach (JObject row in (attempt["negotiationTurns"] as JArray ?? new JArray())
                .OfType<JObject>())
                add(row["turn"] as JObject);
            add(attempt["consentTurn"] as JObject);
            add(attempt["completionTurn"] as JObject);
            return receipts;
        }

        private static bool AffairSexualConsentAccepted(LiveCommandResult turn)
        {
            JObject data = turn?.Data ?? new JObject();
            JObject raw = data["rawResponse"] as JObject ?? new JObject();
            JObject actionGate = raw["actionGate"] as JObject ?? new JObject();
            if (actionGate.Value<string>("commitment")
                ?.Equals("accepted", StringComparison.OrdinalIgnoreCase) != true) return false;
            string intent = (actionGate.Value<string>("intent") ?? string.Empty).ToLowerInvariant();
            return intent.Contains("sex") || intent.Contains("intim")
                || intent.Contains("affair") || intent.Contains("private room")
                || intent.Contains("make love") || intent.Contains("sleep with")
                || intent.Contains("bed ");
        }

        private static string BuildAffairNegotiationReply(string npcReply, int negotiationIndex)
        {
            string lower = (npcReply ?? string.Empty).ToLowerInvariant();
            bool asksForRole = lower.Contains("role") || lower.Contains("office")
                || lower.Contains("council") || lower.Contains("fief")
                || lower.Contains("inherit") || lower.Contains("position")
                || lower.Contains("task") || lower.Contains("place near");
            bool asksForSafety = lower.Contains("trust") || lower.Contains("discre")
                || lower.Contains("risk") || lower.Contains("husband")
                || lower.Contains("clan") || lower.Contains("standing")
                || lower.Contains("secret") || lower.Contains("protect");
            bool asksForTime = lower.Contains("time") || lower.Contains("know you")
                || lower.Contains("spoken") || lower.Contains("conversation")
                || lower.Contains("prove") || lower.Contains("measure");
            bool asksForCompensation = lower.Contains("stipend") || lower.Contains("denar")
                || lower.Contains("income") || lower.Contains("payment") || lower.Contains("pay ")
                || lower.Contains("gold") || lower.Contains("coin");
            bool asksForTrial = lower.Contains("trial") || lower.Contains("week")
                || lower.Contains("days") || lower.Contains("morning")
                || lower.Contains("demonstrat") || lower.Contains("show me");

            string answer = "I heard your answer, and I will meet the concern you actually raised. ";
            if (asksForRole)
                answer += "The role is concrete: you will be my private royal adviser on court intelligence and trade, with your own stipend, guards, lodgings, and a voice in council. It does not depend on intimacy. ";
            if (asksForSafety)
                answer += "Your safety and standing are my responsibility: meetings will use secured rooms and separate arrivals, I will never disclose the affair, and if rumor comes I will defend your office as earned and bear the political cost myself. ";
            if (asksForTime)
                answer += "You are right that trust cannot be demanded. I will keep these promises without a deadline, and you may judge me by whether I honor them before you give me anything personal. ";
            if (asksForCompensation || asksForTrial)
                answer += "I accept the exact stipend, payment schedule, trial period, and safeguards you just named. They are sealed now on my authority and take effect whether or not you ever choose intimacy; the guards and lodgings are ordered today, and your council access begins at the next sitting. ";
            if (!asksForRole && !asksForSafety && !asksForTime
                && !asksForCompensation && !asksForTrial)
                answer += "Your office will be independent, your security and discretion protected, and every benefit will remain yours even if you refuse me. ";

            return negotiationIndex == 0
                ? answer + "Tell me plainly what remains unresolved, and I will answer it rather than repeat myself."
                : answer + "Those terms now stand on my word as king. If that resolves your concern, tell me freely what you want; if it does not, name what remains and I will not pressure you.";
        }

        private static LiveCommandResult SocialReputationProfile(JObject command)
        {
            string profile = (command.Value<string>("profile") ?? string.Empty).Trim().ToLowerInvariant();
            HashSet<string> supported = new HashSet<string>(new[]
            {
                "preflight", "signal_contract", "player_flirt", "player_affair", "unchaste",
                "npc_favoring_presence", "player_favoring_dialogue", "favoring_projection",
                "favoring_jealousy_charm", "favoring_rebellion", "player_parity", "save_prepare",
                "save_verify", "longitudinal_90_day", "cleanup_marker"
            }, StringComparer.OrdinalIgnoreCase);
            if (!supported.Contains(profile))
                return LiveCommandResult.Failed("Unknown Social Reputation profile '" + profile + "'.");
            Hero player = Hero.MainHero;
            List<Hero> adults = Hero.AllAliveHeroes.Where(IsSocialBalanceAdult).ToList();
            List<Hero> rulers = Kingdom.All.Where(kingdom => kingdom != null && !kingdom.IsEliminated)
                .Select(kingdom => kingdom.Leader).Where(IsSocialBalanceAdult)
                .Distinct().ToList();
            List<string> blockers = new List<string>();
            if (!IsSocialBalanceAdult(player)) blockers.Add("The main hero is not a living adult.");
            if (adults.Count < 4) blockers.Add("At least four living adult fixtures are required.");
            if (rulers.Count == 0) blockers.Add("No active ruler fixture is available.");
            if (profile == "player_affair" && player?.Spouse == null
                && !adults.Any(hero => hero != player && hero.Spouse != null))
                blockers.Add("No married player or partner fixture is available for affair eligibility.");
            if (profile.StartsWith("player_favoring", StringComparison.OrdinalIgnoreCase)
                && !rulers.Contains(player))
                blockers.Add("The enrolled main hero must be an active ruler for this profile.");
            JObject evidence = new JObject
            {
                ["profile"] = profile, ["campaignDay"] = CampaignTime.Now.ToDays,
                ["mainHeroId"] = player?.StringId ?? string.Empty,
                ["adultCount"] = adults.Count, ["rulerCount"] = rulers.Count,
                ["rulerIds"] = new JArray(rulers.Select(hero => hero.StringId)),
                ["blockers"] = new JArray(blockers),
                ["authoritativePassRequired"] = true,
                ["message"] = "This command establishes and records native prerequisites. The profile is passed only by authoritative server assertions and never by this command alone."
            };
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                "social_reputation_profile_preflight", evidence);
            return blockers.Count > 0
                ? LiveCommandResult.Failed(string.Join(" ", blockers))
                : LiveCommandResult.Completed("Validated native prerequisites for the Social Reputation profile.", evidence);
        }

        private static LiveCommandResult SocialBalanceSnapshot(JObject command)
        {
            List<string> focusIds = (command?["heroIds"] as JArray ?? new JArray())
                .Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            IEnumerable<Hero> heroes = Hero.AllAliveHeroes.Where(IsSocialBalanceAdult);
            if (focusIds.Count > 0)
                heroes = heroes.Where(x => focusIds.Contains(x.StringId, StringComparer.OrdinalIgnoreCase));
            JArray heroRows = new JArray(heroes.OrderBy(x => x.StringId).Select(hero =>
            {
                Kingdom kingdom = hero.Clan?.Kingdom;
                bool ruler = kingdom?.Leader == hero;
                bool clanLeader = hero.Clan?.Leader == hero;
                return new JObject
                {
                    ["heroId"] = hero.StringId ?? string.Empty,
                    ["name"] = hero.Name?.ToString() ?? string.Empty,
                    ["clanId"] = hero.Clan?.StringId ?? string.Empty,
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["isPlayer"] = hero == Hero.MainHero,
                    ["isRuler"] = ruler,
                    ["isClanLeader"] = clanLeader,
                    ["isRegularLord"] = hero.IsLord && !ruler && !clanLeader,
                    ["isNotable"] = hero.IsNotable,
                    ["isWanderer"] = hero.IsWanderer,
                    ["charm"] = hero.GetSkillValue(DefaultSkills.Charm),
                    ["isPrisoner"] = hero.IsPrisoner
                };
            }));
            JArray settlements = new JArray(Settlement.All
                .Where(x => x?.Town != null)
                .OrderBy(x => x.StringId)
                .Select(settlement =>
                {
                    Town town = settlement.Town;
                    return new JObject
                    {
                        ["settlementId"] = settlement.StringId,
                        ["name"] = settlement.Name?.ToString() ?? string.Empty,
                        ["kingdomId"] = settlement.MapFaction?.StringId ?? string.Empty,
                        ["isTown"] = settlement.IsTown,
                        ["isCastle"] = settlement.IsCastle,
                        ["prosperity"] = town.Prosperity,
                        ["prosperityChange"] = town.ProsperityChange,
                        ["foodStocks"] = town.FoodStocks,
                        ["foodCapacity"] = town.FoodStocksUpperLimit(),
                        ["foodChange"] = town.FoodChange,
                        ["loyalty"] = town.Loyalty,
                        ["loyaltyChange"] = town.LoyaltyChange,
                        ["security"] = town.Security,
                        ["securityChange"] = town.SecurityChange,
                        ["underSiege"] = town.IsUnderSiege,
                        ["rebellious"] = town.InRebelliousState
                    };
                }));
            JArray villages = new JArray(Settlement.All
                .Where(x => x?.Village != null)
                .OrderBy(x => x.StringId)
                .Select(settlement => new JObject
                {
                    ["villageId"] = settlement.StringId,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = settlement.MapFaction?.StringId ?? string.Empty,
                    ["boundSettlementId"] = settlement.Village?.Bound?.StringId ?? string.Empty,
                    ["state"] = settlement.Village?.VillageState.ToString() ?? string.Empty,
                    ["hearth"] = settlement.Village?.Hearth ?? 0f,
                    ["hearthChange"] = settlement.Village?.HearthChange ?? 0f,
                    ["deserted"] = settlement.Village?.IsDeserted == true
                }));
            JObject data = new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["heroes"] = heroRows,
                ["settlements"] = settlements,
                ["villages"] = villages,
                ["rebellion"] = SocialBalanceRebellionSnapshot(),
                ["harness"] = ReignSocialBalanceHarnessCampaignBehavior.Instance?.Snapshot() ?? new JObject()
            };
            return LiveCommandResult.Completed("Captured a native social-balance snapshot.", data);
        }

        private static LiveCommandResult SocialBalanceSetTown(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            Town town = settlement?.Town;
            if (town == null) return LiveCommandResult.Failed("A town or castle settlement is required.");
            JObject before = TownMetrics(town);
            if (command["prosperity"] != null) town.Prosperity = ClampFloat(command.Value<float>("prosperity"), 0f, 1000000f);
            if (command["foodStocks"] != null) town.FoodStocks = ClampFloat(command.Value<float>("foodStocks"), -10000f, 100000f);
            if (command["loyalty"] != null) town.Loyalty = ClampFloat(command.Value<float>("loyalty"), 0f, 100f);
            if (command["security"] != null) town.Security = ClampFloat(command.Value<float>("security"), 0f, 100f);
            JObject after = TownMetrics(town);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("town_metrics",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded settlement metrics in the enrolled disposable save.",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetVillage(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("villageId") ?? string.Empty);
            Village village = settlement?.Village;
            if (village == null) return LiveCommandResult.Failed("A village settlement is required.");
            JObject before = VillageMetrics(village);
            if (command["hearth"] != null) village.Hearth = ClampFloat(command.Value<float>("hearth"), 0f, 100000f);
            string state = (command.Value<string>("state") ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (state == "normal") ChangeVillageStateAction.ApplyBySettingToNormal(settlement);
                else if (state == "looted") village.VillageState = Village.VillageStates.Looted;
                else return LiveCommandResult.Failed("Supported village states are normal and looted.");
            }
            JObject after = VillageMetrics(village);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("village_metrics",
                new JObject { ["villageId"] = settlement.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded village metrics in the enrolled disposable save.",
                new JObject { ["villageId"] = settlement.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetHero(JObject command)
        {
            Hero hero = FindSocialBalanceHero(command.Value<string>("heroId"));
            if (hero == null || !IsSocialBalanceAdult(hero)) return LiveCommandResult.Failed("A living adult hero is required.");
            JObject before = SocialBalanceHeroMetrics(hero);
            if (command["charm"] != null)
                hero.SetSkillValue(DefaultSkills.Charm, Math.Max(0, Math.Min(1023, command.Value<int>("charm"))));
            JObject after = SocialBalanceHeroMetrics(hero);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("hero_metrics",
                new JObject { ["heroId"] = hero.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded hero metrics in the enrolled disposable save.",
                new JObject { ["heroId"] = hero.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetRelation(JObject command)
        {
            Hero observer = FindSocialBalanceHero(command.Value<string>("observerId"));
            Hero subject = FindSocialBalanceHero(command.Value<string>("subjectId"));
            if (!IsSocialBalanceAdult(observer) || !IsSocialBalanceAdult(subject) || observer == subject)
                return LiveCommandResult.Failed("Two distinct living adult heroes are required.");
            int prior = observer.GetRelation(subject);
            int value = Math.Max(-100, Math.Min(100, command.Value<int?>("value") ?? prior));
            observer.SetPersonalRelation(subject, value);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("native_relation",
                new JObject { ["observerId"] = observer.StringId, ["subjectId"] = subject.StringId, ["before"] = prior, ["after"] = value });
            return LiveCommandResult.Completed("Set the enrolled save's native relationship value.",
                new JObject { ["observerId"] = observer.StringId, ["subjectId"] = subject.StringId, ["before"] = prior, ["after"] = observer.GetRelation(subject) });
        }

        private static LiveCommandResult SocialBalanceSetRuler(JObject command)
        {
            Kingdom kingdom = FindSocialBalanceKingdom(command.Value<string>("kingdomId"));
            Clan clan = Clan.FindFirst(x => string.Equals(x?.StringId, command.Value<string>("clanId"), StringComparison.OrdinalIgnoreCase));
            if (kingdom == null || clan == null || clan.Kingdom != kingdom || clan.IsEliminated)
                return LiveCommandResult.Failed("The replacement ruling clan must be an active member of the kingdom.");
            string before = kingdom.RulingClan?.StringId ?? string.Empty;
            ChangeRulingClanAction.Apply(kingdom, clan);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("ruling_clan",
                new JObject { ["kingdomId"] = kingdom.StringId, ["before"] = before, ["after"] = kingdom.RulingClan?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed the ruler in the enrolled disposable save.",
                new JObject { ["kingdomId"] = kingdom.StringId, ["beforeClanId"] = before, ["afterClanId"] = kingdom.RulingClan?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceChangeClan(JObject command)
        {
            Clan clan = Clan.FindFirst(x => string.Equals(x?.StringId, command.Value<string>("clanId"), StringComparison.OrdinalIgnoreCase));
            Kingdom destination = FindSocialBalanceKingdom(command.Value<string>("kingdomId"));
            if (clan == null || clan.IsEliminated || clan == Clan.PlayerClan)
                return LiveCommandResult.Failed("A living non-player clan is required.");
            string before = clan.Kingdom?.StringId ?? string.Empty;
            if (destination == null) ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
            else if (clan.Kingdom != destination)
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, destination, CampaignTime.Zero, true);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("clan_kingdom",
                new JObject { ["clanId"] = clan.StringId, ["before"] = before, ["after"] = clan.Kingdom?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed clan membership in the enrolled disposable save.",
                new JObject { ["clanId"] = clan.StringId, ["beforeKingdomId"] = before, ["afterKingdomId"] = clan.Kingdom?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceChangeOwner(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            Hero owner = FindSocialBalanceHero(command.Value<string>("ownerHeroId"));
            if (settlement?.Town == null || owner?.Clan == null || owner.IsDead)
                return LiveCommandResult.Failed("A fortification and living clan member owner are required.");
            string before = settlement.OwnerClan?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(settlement, owner);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("settlement_owner",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = settlement.OwnerClan?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed settlement ownership in the enrolled disposable save.",
                new JObject { ["settlementId"] = settlement.StringId, ["beforeClanId"] = before, ["afterClanId"] = settlement.OwnerClan?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceTimeControl(
            JObject command,
            string contextLabel = "enrolled disposable save")
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null) return LiveCommandResult.Failed("No campaign is loaded.");
            string mode = (command.Value<string>("timeMode") ?? "stop").Trim().ToLowerInvariant();
            bool settlementWaitRequested = command.Value<bool?>("settlementWait") == true;
            bool? autoAcknowledgeRequested =
                command.Value<bool?>("autoAcknowledgeDiplomacyAnnouncements");
            GameMenu waitMenu = campaign.CurrentMenuContext?.GameMenu;
            if (settlementWaitRequested && waitMenu?.IsWaitActive != true)
            {
                Settlement settlement = Hero.MainHero?.CurrentSettlement;
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                    return LiveCommandResult.Failed(
                        "Safe settlement waiting requires the player to be inside a town or castle.");

                if (waitMenu == null)
                    return LiveCommandResult.Failed(
                        "Safe settlement waiting requires an active Bannerlord settlement menu.");
                if (!waitMenu.IsWaitMenu)
                {
                    int waitOptionIndex = -1;
                    List<string> optionIds = new List<string>();
                    for (int index = 0; index < waitMenu.MenuItemAmount; index++)
                    {
                        string optionId = waitMenu.GetMenuOptionIdString(index) ?? string.Empty;
                        optionIds.Add(optionId);
                        if (optionId.Equals("town_wait", StringComparison.OrdinalIgnoreCase)
                            || optionId.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            waitOptionIndex = index;
                            break;
                        }
                    }
                    if (waitOptionIndex < 0)
                        return LiveCommandResult.Failed(
                            "The current settlement menu has no native wait option. Options: "
                            + string.Join(", ", optionIds));

                    GameMenuOption waitOption = waitMenu.GetGameMenuOption(waitOptionIndex);
                    if (waitOption == null)
                        return LiveCommandResult.Failed(
                            "Bannerlord returned an empty native settlement-wait option.");
                    waitOption.RunConsequence(campaign.CurrentMenuContext);
                }
                waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu == null || !waitMenu.IsWaitMenu)
                    return LiveCommandResult.Failed(
                        "Bannerlord did not enter its native settlement-wait menu.");
                if (!waitMenu.IsWaitActive) waitMenu.StartWait();
            }
            if (mode == "stop") campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            else if (mode == "play") campaign.TimeControlMode = CampaignTimeControlMode.StoppablePlay;
            else if (mode == "fast" && settlementWaitRequested)
            {
                // Bannerlord's native settlement wait advances only through the
                // UnstoppableFastForward mode established by StartWait(). A prior
                // stop command deliberately leaves IsWaitActive set while pausing
                // the clock, so restart the native wait even when it is already
                // marked active instead of replacing its mode with a stoppable one.
                waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu == null || !waitMenu.IsWaitMenu)
                    return LiveCommandResult.Failed(
                        "Bannerlord left its native settlement-wait menu before fast-forward could begin.");
                waitMenu.StartWait();
            }
            else if (mode == "fast") campaign.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
            else return LiveCommandResult.Failed("timeMode must be stop, play, or fast.");
            if (autoAcknowledgeRequested.HasValue)
                _autoAcknowledgeDiplomacyAnnouncements = autoAcknowledgeRequested.Value;
            return LiveCommandResult.Completed("Changed campaign time control for the " + contextLabel + ".",
                new JObject
                {
                    ["timeMode"] = campaign.TimeControlMode.ToString(),
                    ["worldDay"] = CampaignTime.Now.ToDays,
                    ["settlementWaitRequested"] = settlementWaitRequested,
                    ["settlementWaitActive"] = campaign.CurrentMenuContext?.GameMenu?.IsWaitActive == true,
                    ["settlementId"] = Hero.MainHero?.CurrentSettlement?.StringId ?? string.Empty,
                    ["autoAcknowledgeDiplomacyAnnouncements"] = _autoAcknowledgeDiplomacyAnnouncements
                });
        }

        private static LiveCommandResult SocialBalanceAcknowledgeDiplomacyAnnouncement()
        {
            bool acknowledged = ReignDiplomacyAnnouncementScreenManager
                .TryAcknowledgeForLiveHarness(false, out string eventId);
            if (acknowledged)
            {
                ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                    "diplomacy_announcement_acknowledged",
                    new JObject { ["eventId"] = eventId });
            }
            return LiveCommandResult.Completed(
                acknowledged
                    ? "Acknowledged the active Reign diplomacy announcement."
                    : "No Reign diplomacy announcement was open.",
                new JObject
                {
                    ["acknowledged"] = acknowledged,
                    ["eventId"] = eventId
                });
        }

        private static LiveCommandResult SocialBalanceRecordOutcome(JObject command)
        {
            Hero subject = FindSocialBalanceHero(command.Value<string>("subjectId"));
            string archetype = command.Value<string>("archetypeId") ?? string.Empty;
            string summary = command.Value<string>("incidentDescription") ?? string.Empty;
            if (!IsSocialBalanceAdult(subject) || string.IsNullOrWhiteSpace(archetype) || summary.Trim().Length < 10)
                return LiveCommandResult.Failed("A living adult subject, archetypeId, and factual incidentDescription are required.");
            JObject evidence = command["evidence"] as JObject ?? new JObject();
            evidence["socialBalanceTestRunId"] = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            evidence["socialBalanceCaseId"] = command.Value<string>("caseId") ?? string.Empty;
            JArray counters = command["counterOnlyTagIds"] as JArray;
            string sourceKey = command.Value<string>("sourceKey")
                ?? "social-balance-" + Guid.NewGuid().ToString("N");
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(
                archetype, subject, command.Value<string>("role") ?? "subject", sourceKey, summary,
                evidence, counters, command.Value<bool?>("forceExposure") == true,
                command.Value<bool?>("forcePromotion") == true);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("social_outcome",
                new JObject { ["subjectId"] = subject.StringId, ["archetypeId"] = archetype, ["sourceKey"] = sourceKey });
            return LiveCommandResult.Completed("Queued deterministic social outcome evidence through World History.",
                new JObject { ["subjectId"] = subject.StringId, ["archetypeId"] = archetype, ["sourceKey"] = sourceKey });
        }

        private static LiveCommandResult SocialBalanceSetLocation(JObject command)
        {
            Hero hero = FindSocialBalanceHero(command.Value<string>("heroId"));
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            if (!IsSocialBalanceAdult(hero) || settlement == null)
                return LiveCommandResult.Failed("A living adult hero and valid settlement are required.");
            string before = hero.CurrentSettlement?.StringId
                ?? hero.PartyBelongedTo?.CurrentSettlement?.StringId
                ?? string.Empty;
            TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, settlement);
            string after = hero.CurrentSettlement?.StringId
                ?? hero.PartyBelongedTo?.CurrentSettlement?.StringId
                ?? string.Empty;
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("hero_location",
                new JObject
                {
                    ["heroId"] = hero.StringId, ["beforeSettlementId"] = before,
                    ["afterSettlementId"] = after
                });
            return LiveCommandResult.Completed("Teleported the hero inside the enrolled disposable save.",
                new JObject
                {
                    ["heroId"] = hero.StringId, ["beforeSettlementId"] = before,
                    ["afterSettlementId"] = after
                });
        }

        private static LiveCommandResult SocialBalanceCreateChild(JObject command)
        {
            ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
            Hero mother = FindSocialBalanceHero(command.Value<string>("motherId"));
            Hero biologicalFather = FindSocialBalanceHero(command.Value<string>("biologicalFatherId"));
            Hero legalFather = FindSocialBalanceHero(command.Value<string>("legalFatherId")) ?? biologicalFather;
            if (family == null)
                return LiveCommandResult.Failed("The Reign family behavior is unavailable.");
            if (!family.TryCreateSocialBalanceChild(
                mother, biologicalFather, legalFather,
                command.Value<bool?>("publiclyKnown") == true,
                command.Value<bool?>("female") == true,
                out Hero child, out string error))
                return LiveCommandResult.Failed(error);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("child_created",
                new JObject
                {
                    ["childId"] = child.StringId,
                    ["motherId"] = mother.StringId,
                    ["biologicalFatherId"] = biologicalFather.StringId,
                    ["legalFatherId"] = legalFather.StringId,
                    ["publiclyKnown"] = command.Value<bool?>("publiclyKnown") == true
                });
            return LiveCommandResult.Completed("Created controlled family evidence inside the enrolled disposable save.",
                new JObject
                {
                    ["childId"] = child.StringId,
                    ["name"] = child.Name?.ToString() ?? string.Empty,
                    ["motherId"] = mother.StringId,
                    ["biologicalFatherId"] = biologicalFather.StringId,
                    ["legalFatherId"] = legalFather.StringId
                });
        }

        private static LiveCommandResult SocialBalanceQueueRebellionRoll(JObject command)
        {
            ReignRebellionCampaignBehavior rebellion = ReignRebellionCampaignBehavior.Instance;
            if (rebellion == null) return LiveCommandResult.Failed("The rebellion behavior is unavailable.");
            string runId = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            if (!rebellion.QueueSocialBalanceRollOverride(command.Value<string>("kingdomId"),
                command.Value<string>("clanId"), command.Value<int?>("roll") ?? 100, runId, out string result))
                return LiveCommandResult.Failed(result);
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride("rebellion_roll",
                new JObject { ["kingdomId"] = command.Value<string>("kingdomId"), ["clanId"] = command.Value<string>("clanId"), ["roll"] = command.Value<int?>("roll") ?? 100 });
            return LiveCommandResult.Completed(result);
        }

        private static LiveCommandResult SocialBalanceResolveRebellion(JObject command)
        {
            ReignRebellionCampaignBehavior rebellion = ReignRebellionCampaignBehavior.Instance;
            Hero surrendering = FindSocialBalanceHero(command.Value<string>("surrenderingHeroId"));
            if (rebellion == null || surrendering == null)
                return LiveCommandResult.Failed("The rebellion behavior and surrendering leader are required.");
            if (!rebellion.TrySurrender(surrendering, command.Value<string>("movementId") ?? string.Empty, out string result))
                return LiveCommandResult.Failed(result);
            return LiveCommandResult.Completed(result, SocialBalanceRebellionSnapshot());
        }

        private static LiveCommandResult SocialBalanceClearOverrides(JObject command)
        {
            string runId = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            int cleared = ReignRebellionCampaignBehavior.Instance?.ClearSocialBalanceRollOverrides(runId) ?? 0;
            return LiveCommandResult.Completed("Cleared pending test-only native overrides.",
                new JObject { ["rebellionRollOverridesCleared"] = cleared });
        }

        private static bool TryGetDurableSocialBalanceResult(JObject command, out LiveCommandResult result)
        {
            result = null;
            string operation = command?.Value<string>("operation") ?? string.Empty;
            string commandId = command?.Value<string>("commandId") ?? string.Empty;
            if (!operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase)
                || ReignSocialBalanceHarnessCampaignBehavior.Instance == null
                || !ReignSocialBalanceHarnessCampaignBehavior.Instance.TryGetReceipt(commandId, out JObject stored))
                return false;
            result = new LiveCommandResult
            {
                Status = stored.Value<string>("status") ?? "completed",
                Message = stored.Value<string>("message") ?? string.Empty,
                Error = stored.Value<string>("error") ?? string.Empty,
                Data = stored["data"] as JObject ?? new JObject(),
                CorrelationIds = (stored["correlationIds"] as JArray ?? new JArray()).Values<string>().ToList()
            };
            return true;
        }

        private static void RememberDurableSocialBalanceResult(JObject command, LiveCommandResult result)
        {
            string operation = command?.Value<string>("operation") ?? string.Empty;
            if (!operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase) || result == null) return;
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RememberReceipt(
                command.Value<string>("commandId") ?? string.Empty,
                new JObject
                {
                    ["status"] = result.Status ?? "completed",
                    ["message"] = result.Message ?? string.Empty,
                    ["error"] = result.Error ?? string.Empty,
                    ["data"] = result.Data ?? new JObject(),
                    ["correlationIds"] = new JArray(result.CorrelationIds ?? new List<string>())
                });
        }

        private static JObject SocialBalanceRebellionSnapshot()
        {
            ReignRebellionCampaignBehavior rebellion = ReignRebellionCampaignBehavior.Instance;
            return new JObject
            {
                ["weeklyRolls"] = new JArray((rebellion?.WeeklyRolls ?? new List<ReignBeta.World.ReignRebellionWeeklyRollRecord>())
                    .Select(x => new JObject
                    {
                        ["kingdomId"] = x.KingdomStringId, ["clanId"] = x.ClanStringId,
                        ["weekIndex"] = x.WeekIndex, ["roll"] = x.Roll,
                        ["rulerRelation"] = x.RulerRelation, ["eligible"] = x.WasEligible,
                        ["triggered"] = x.Triggered, ["testOverride"] = x.TestOverride,
                        ["testRunId"] = x.TestRunId ?? string.Empty
                    })),
                ["movements"] = new JArray((rebellion?.Movements ?? new List<ReignBeta.World.ReignRebellionMovementRecord>())
                    .Select(x => new JObject
                    {
                        ["movementId"] = x.MovementId, ["parentKingdomId"] = x.ParentKingdomStringId,
                        ["rebelKingdomId"] = x.RebelKingdomStringId, ["leaderHeroId"] = x.LeaderHeroStringId,
                        ["rulerHeroId"] = x.OriginalRulerHeroStringId, ["stage"] = x.Stage,
                        ["winningSide"] = x.WinningSide, ["resolutionCause"] = x.ResolutionCause,
                        ["resolved"] = x.ResolutionApplied
                    }))
            };
        }

        private static JObject TownMetrics(Town town)
        {
            return new JObject
            {
                ["prosperity"] = town?.Prosperity ?? 0f, ["foodStocks"] = town?.FoodStocks ?? 0f,
                ["loyalty"] = town?.Loyalty ?? 0f, ["security"] = town?.Security ?? 0f
            };
        }

        private static JObject VillageMetrics(Village village)
        {
            return new JObject
            {
                ["hearth"] = village?.Hearth ?? 0f,
                ["state"] = village?.VillageState.ToString() ?? string.Empty,
                ["deserted"] = village?.IsDeserted == true
            };
        }

        private static JObject SocialBalanceHeroMetrics(Hero hero)
        {
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? string.Empty,
                ["charm"] = hero?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty
            };
        }

        private static Hero FindSocialBalanceHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId) ? null
                : Hero.FindFirst(x => string.Equals(x?.StringId, heroId, StringComparison.OrdinalIgnoreCase));
        }

        private static Kingdom FindSocialBalanceKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId) ? null
                : Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId, kingdomId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSocialBalanceAdult(Hero hero)
        {
            if (hero == null || !hero.IsAlive || hero.IsChild) return false;
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return hero.Age >= adultAge && (hero == Hero.MainHero || hero.IsLord || hero.IsNotable || hero.IsWanderer || hero.Clan != null);
        }

        private static float ClampFloat(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
