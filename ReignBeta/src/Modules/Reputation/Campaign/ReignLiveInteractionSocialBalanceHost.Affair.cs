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
                    ReignServices.SocialBalanceHarness?.RecordOverride(
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
                        ReignServices.SocialBalanceHarness?.RecordOverride(
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
                            ReignServices.SocialBalanceHarness?.RecordOverride(
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
                        ReignServices.SocialBalanceHarness?.RecordOverride(
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
    }
}
