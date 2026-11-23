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
            ReignSocialBalanceHarnessCampaignBehavior harness = ReignServices.SocialBalanceHarness;
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
            ReignServices.SocialBalanceHarness?.RecordOverride(
                "social_reputation_profile_preflight", evidence);
            return blockers.Count > 0
                ? LiveCommandResult.Failed(string.Join(" ", blockers))
                : LiveCommandResult.Completed("Validated native prerequisites for the Social Reputation profile.", evidence);
        }
    }
}
