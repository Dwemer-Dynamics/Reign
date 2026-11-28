using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Diplomacy.World;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Shared.Dialogue;
using ReignBeta.World;
using ReignBeta.WorldSimulation.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static LiveCommandResult ExerciseOrdinaryRansomFixture(JObject command, JObject test, JObject scope, string operation)
        {
            JObject args = test["arguments"] as JObject;
            string stage = args?["stage"]?.Type == JTokenType.String ? args.Value<string>("stage") : "";
            string scenario = args?["scenario"]?.Type == JTokenType.String ? args.Value<string>("scenario") : "";
            bool bulk = scenario == "bulk_paid";
            bool pruneHistory = scenario == "named_pruned";
            bool interruptRelease = scenario == "named_hold_release";
            bool interruptPayment = scenario == "named_hold_payment";
            bool interrupted = interruptRelease || interruptPayment;
            if (test.Count != 6 || test.Value<string>("phase") != "fixture" || test.Value<string>("kind") != "fixture"
                || args == null || stage is not ("discover" or "run" or "prune" or "replay")
                || (stage == "prune" && !pruneHistory)
                || (stage == "discover" ? args.Count != 1 : args.Count != 5
                    || scenario is not ("named_paid" or "bulk_paid" or "named_free" or "named_pruned" or "named_hold_release" or "named_hold_payment")
                    || !OrdinaryRansomFixtureId(args["actorKingdomId"]) || !OrdinaryRansomFixtureId(args["targetKingdomId"])
                    || !(args["captiveIds"] is JArray ids) || ids.Count != (bulk ? 3 : 1)
                    || !ids.All(OrdinaryRansomFixtureId) || ids.Values<string>().Distinct(StringComparer.Ordinal).Count() != ids.Count))
                return LiveCommandResult.Failed("Supply one fixed ordinary ransom fixture and its exact scenario identities.");

            var native = new ReignNativeRansom();
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string save = ActiveSaveName(), point = scope.Value<string>("checkpointSavePointId");
            var owner = ObservePassiveWorldCurrentLoad(command, campaign, native.CampaignId, native.TimelineId, save, point);
            if (!native.OwnsLoadedWorld() || scope.Value<bool?>("recoveryCheckpointVerified") != true
                || owner == null || !JToken.DeepEquals(owner, scope["checkpointLoadOwner"])
                || !NativeSaveHasPoint(save, native.CampaignId, point)
                || !string.Equals(NativeCheckpointHash(save), scope.Value<string>("checkpointSha256"), StringComparison.OrdinalIgnoreCase))
                return LiveCommandResult.Failed("The fixture requires the exact aligned, reloaded recovery point on the enrolled Current.");

            bool EligibleRuler(Kingdom kingdom) => kingdom != null && !kingdom.IsEliminated
                && kingdom != Clan.PlayerClan?.Kingdom && kingdom.Leader?.IsAlive == true && !kingdom.Leader.IsPrisoner;
            bool EligibleHolder(Kingdom kingdom) => EligibleRuler(kingdom) && kingdom.Leader.PartyBelongedTo is { } party
                && party.LeaderHero == kingdom.Leader && party.Party.MapFaction == kingdom && party.MapEvent == null && party.SiegeEvent == null;
            bool EligibleCaptive(Hero hero, Kingdom kingdom) => hero?.IsAlive == true && !hero.IsChild && hero.IsLord
                && hero.Clan?.Kingdom == kingdom && hero != kingdom.Leader && hero != hero.Clan.Leader
                && hero.PartyBelongedTo == null && !hero.IsPrisoner && hero.GovernorOf == null;
            bool HeldFrom(Hero hero, Kingdom origin, Kingdom holder) => hero.IsPrisoner && hero.Clan?.Kingdom == origin
                && hero.PartyBelongedToAsPrisoner?.MapFaction == holder;
            if (stage == "discover")
            {
                var kingdoms = Kingdom.All.Where(EligibleRuler).OrderBy(k => k.StringId, StringComparer.Ordinal).Take(16).ToArray();
                return LiveCommandResult.Completed("Read eligible independent fixture participants.", new JObject
                {
                    ["kingdoms"] = new JArray(kingdoms.Select(k => new JObject
                    {
                        ["kingdomId"] = k.StringId, ["canHold"] = EligibleHolder(k), ["rulerGold"] = k.Leader.Gold,
                        ["captiveIds"] = new JArray(Hero.AllAliveHeroes.Where(h => EligibleCaptive(h, k))
                            .OrderBy(h => h.StringId, StringComparer.Ordinal).Take(12).Select(h => h.StringId))
                    })),
                    ["pairsWithExistingCustody"] = new JArray(kingdoms.Where(EligibleHolder)
                        .SelectMany(holder => kingdoms.Where(origin => origin != holder
                            && Hero.AllAliveHeroes.Any(h => HeldFrom(h, origin, holder)))
                            .Select(origin => new JObject { ["actorKingdomId"] = holder.StringId, ["targetKingdomId"] = origin.StringId })))
                });
            }

            var actor = Kingdom.All.SingleOrDefault(k => k.StringId == args.Value<string>("actorKingdomId"));
            var target = Kingdom.All.SingleOrDefault(k => k.StringId == args.Value<string>("targetKingdomId"));
            if (!EligibleHolder(actor) || !EligibleRuler(target) || actor == target)
                return LiveCommandResult.Failed("The exact two non-player rulers and safe original captor party must be available.");
            var holder = actor.Leader.PartyBelongedTo;
            var holderParty = holder.Party;
            var testCaptives = ((JArray)args["captiveIds"]).Values<string>()
                .Select(id => Hero.FindFirst(h => h.StringId == id)).ToArray();
            if (testCaptives.Any(h => h?.IsAlive != true || h.Clan?.Kingdom != target))
                return LiveCommandResult.Failed("Every requested captive must still be the exact living noble of the target kingdom.");

            var behavior = ReignServices.AI;
            var operations = ReignServices.Agreement;
            string actionId = "d44_" + ReignNativeRansom.Key("ordinary-ransom-fixture", native.CampaignId, native.TimelineId,
                scope.Value<string>("campaignTestRunId") + ":" + operation);
            string binding = new JArray(scenario, actor.StringId, target.StringId, actor.Leader.StringId, target.Leader.StringId,
                holder.StringId, args["captiveIds"].DeepClone()).ToString(Formatting.None);
            string receipt = "ransom_" + NativeRansomWorldAction.Key(native.CampaignId, native.TimelineId, actionId);
            int gold = scenario == "named_free" ? 0 : 11;
            var terms = new JObject { ["gold"] = gold, ["maxPrisoners"] = bulk ? 2 : 1 };
            if (!bulk) terms["prisonerHeroStringId"] = testCaptives[0].StringId;
            var action = new ReignWorldActionRecord
            {
                ActionId = actionId, Type = ReignWorldActionType.DiplomacyRansomPackage, Source = "gauntlet:" + actionId,
                ActorKingdomStringId = actor.StringId, TargetKingdomStringId = target.StringId,
                ActorHeroStringId = "", TargetHeroStringId = bulk ? "" : testCaptives[0].StringId,
                ActorClanStringId = "", TargetClanStringId = "", TargetSettlementStringId = "",
                TermsJson = terms.ToString(Formatting.None), MaxAttempts = 1
            };
            JObject Observe() => new JObject
            {
                ["payerGold"] = target.Leader.Gold, ["recipientGold"] = actor.Leader.Gold,
                ["payerRelation"] = target.Leader.GetRelation(actor.Leader),
                ["recipientRelation"] = actor.Leader.GetRelation(target.Leader),
                ["agreementCount"] = behavior.Agreements.Count(a => a.AgreementId == receipt),
                ["actionCount"] = behavior.Actions.Count(a => a.ActionId == actionId),
                ["captives"] = new JArray(testCaptives.Select(h => new JObject
                {
                    ["heroId"] = h.StringId, ["alive"] = h.IsAlive, ["isPrisoner"] = h.IsPrisoner,
                    ["hasCaptor"] = h.PartyBelongedToAsPrisoner != null,
                    ["sameCaptor"] = h.PartyBelongedToAsPrisoner == holderParty,
                    ["originalRosterCount"] = holderParty.PrisonRoster.GetTroopCount(h.CharacterObject)
                }))
            };
            if (behavior == null || operations == null)
                return LiveCommandResult.Failed("The production action and native operation ledgers must both be available.");
            var history = behavior.Actions as IList<ReignWorldActionRecord>;
            if (pruneHistory && (history == null || history.IsReadOnly))
                return LiveCommandResult.Failed("The test cannot isolate one owned row in the native action history.");
            var saved = operations.NativeOperation(actionId);
            if (stage == "prune")
            {
                var ownRow = history.SingleOrDefault(row => row?.ActionId == actionId);
                if (saved?.Value<string>("binding") != binding || saved.Value<string>("status") != "completed"
                    || saved.Value<string>("gameInstanceId") == _gameInstanceId || saved.Value<string>("savePointId") == point
                    || ownRow?.Status != ReignWorldActionStatus.Completed || ownRow.Type != action.Type
                    || ownRow.Source != action.Source || ownRow.TermsJson != action.TermsJson
                    || ownRow.ExecutionSnapshotJson != saved.Value<string>("executionSnapshot")
                    || !native.Journal.TryFindWorldAction(native.CampaignId, native.TimelineId, actionId, out var retained)
                    || !retained.IsVerified() || retained.PublicationId != receipt)
                    return LiveCommandResult.Failed("Pruning requires the exact completed fixture row and retained receipt on a freshly reloaded checkpoint.");

                JObject beforePrune = Observe();
                ReignActionResult withRow = behavior.GetLastActionResult(actionId);
                if (!JToken.DeepEquals(saved["after"], beforePrune) || withRow?.ResultCode != "completed"
                    || !JToken.DeepEquals(saved["receipt"], JObject.FromObject(withRow)))
                    return LiveCommandResult.Failed("The saved fixture row or its original receipt changed before pruning.");
                var neighbors = history.Where(row => !ReferenceEquals(row, ownRow)).ToArray();
                var journalBefore = JObject.FromObject(retained);

                // Model the loss of this one terminal row on the disposable save. Never run a global prune from a fixture.
                history.Remove(ownRow);
                ReignActionResult withoutRow = behavior.GetLastActionResult(actionId);
                JObject afterPrune = Observe();
                var expected = (JObject)beforePrune.DeepClone();
                expected["actionCount"] = 0;
                if (!history.SequenceEqual(neighbors) || !JToken.DeepEquals(expected, afterPrune)
                    || behavior.GetLastActionResult(actionId + "_missing") != null
                    || withoutRow?.ResultCode != "completed" || !JToken.DeepEquals(saved["receipt"], JObject.FromObject(withoutRow))
                    || !native.Journal.TryFindWorldAction(native.CampaignId, native.TimelineId, actionId, out var afterRetained)
                    || !JToken.DeepEquals(journalBefore, JObject.FromObject(afterRetained)))
                    return LiveCommandResult.Failed("The exact prune or readback changed other evidence. Restore the previous checkpoint.");

                saved["status"] = "pruned";
                saved["prunedGameInstanceId"] = _gameInstanceId;
                saved["prunedSavePointId"] = point;
                saved["after"] = afterPrune;
                operations.SaveNativeOperation(actionId, saved);
                return LiveCommandResult.Completed("Verified one terminal history removal and recovered the original receipt.", new JObject
                {
                    ["schema"] = "reign-native-ordinary-ransom-fixture-v1", ["stage"] = stage, ["scenario"] = scenario,
                    ["gameInstanceId"] = _gameInstanceId, ["savePointId"] = point, ["actionId"] = actionId,
                    ["receiptId"] = receipt, ["before"] = beforePrune, ["after"] = afterPrune, ["verified"] = true,
                    ["unrelatedRowsPreserved"] = neighbors.Length, ["wrongIdRejected"] = true, ["journal"] = journalBefore,
                    ["receipt"] = saved["receipt"].DeepClone()
                });
            }
            bool replay = stage == "replay";
            if (replay)
            {
                string gameField = pruneHistory ? "prunedGameInstanceId" : "gameInstanceId";
                string pointField = pruneHistory ? "prunedSavePointId" : "savePointId";
                string savedStatus = interrupted ? "held" : pruneHistory ? "pruned" : "completed";
                if (saved?.Value<string>("binding") != binding || saved.Value<string>("status") != savedStatus
                    || saved.Value<string>(gameField) == _gameInstanceId || saved.Value<string>(pointField) == point
                    || behavior.Actions.Any(row => row?.ActionId == actionId)
                    || (!pruneHistory && !interrupted && ReignDiplomacyExecutor.ReadOrdinaryRansom(action, publish: false)?.ResultCode != "completed"))
                    return LiveCommandResult.Failed("Replay requires the exact fixture state on a freshly reloaded checkpoint containing its original ransom attempt.");
            }
            else
            {
                if (saved != null || behavior.Actions.Any(a => a.ActionId == actionId)
                    || testCaptives.Any(h => !EligibleCaptive(h, target)) || target.Leader.Gold < gold
                    || actor.Leader.Gold > int.MaxValue - gold
                    || (bulk && Hero.AllAliveHeroes.Any(h => HeldFrom(h, target, actor))))
                    return LiveCommandResult.Failed("Use an unused operation, eligible free captives, sufficient gold and an empty matching bulk custody pool.");
                saved = new JObject { ["status"] = "started", ["binding"] = binding, ["gameInstanceId"] = _gameInstanceId, ["savePointId"] = point };
                operations.SaveNativeOperation(actionId, saved);
                foreach (Hero captive in testCaptives) TakePrisonerAction.Apply(holderParty, captive);
                if (testCaptives.Any(h => !h.IsPrisoner || h.PartyBelongedToAsPrisoner != holderParty
                    || holderParty.PrisonRoster.GetTroopCount(h.CharacterObject) != 1))
                    return LiveCommandResult.Failed("Native capture was incomplete. Restore the retained checkpoint before retrying.");
            }

            JObject before = Observe();
            int payerBefore = before.Value<int>("payerGold"), recipientBefore = before.Value<int>("recipientGold");
            Func<bool> fixtureOwns = null;
            if (interruptRelease) fixtureOwns = () => testCaptives[0].IsPrisoner;
            if (interruptPayment) fixtureOwns = () => target.Leader.Gold == payerBefore && actor.Leader.Gold == recipientBefore;
            // The scenario verifies the ordinary native settlement step after approval.
            // Government deliberation remains outside this fixture. A pruned replay uses the production history lookup by ID.
            var result = replay ? (pruneHistory ? behavior.GetLastActionResult(actionId) : ReignDiplomacyExecutor.Execute(action))
                : ReignActionValidator.Validate(action, out string invalid)
                    ? ReignDiplomacyExecutor.RansomPackage(actor, target, action, fixtureOwns)
                    : ReignActionResult.ValidationFailed(invalid);
            JObject after = Observe();
            bool hasAttempt = native.Journal.TryFindWorldAction(native.CampaignId, native.TimelineId, actionId, out var attempt);
            var captivesAfter = (JArray)after["captives"];
            string[] released = captivesAfter.Where(row => row.Value<bool>("alive") && !row.Value<bool>("isPrisoner")
                && !row.Value<bool>("hasCaptor") && row.Value<int>("originalRosterCount") == 0)
                .Select(row => row.Value<string>("heroId")).ToArray();
            bool correctCustody = released.Length == (bulk ? 2 : 1) && captivesAfter.Where(row => !released.Contains(row.Value<string>("heroId")))
                .All(row => row.Value<bool>("alive") && row.Value<bool>("isPrisoner")
                    && row.Value<bool>("sameCaptor") && row.Value<int>("originalRosterCount") == 1);
            if (interrupted)
            {
                string holdReason = interruptPayment ? "post_payment_authority_or_custody_changed" : "release_or_authority_changed";
                int movedGold = interruptPayment ? gold : 0;
                var holdResult = behavior.GetLastActionResult(actionId);
                var heldJournal = hasAttempt ? JObject.FromObject(attempt) : null;
                var heldReceipt = holdResult != null ? JObject.FromObject(holdResult) : null;
                bool held = hasAttempt && attempt.State == NativeRansomState.Held && attempt.HoldReason == holdReason
                    && attempt.WorldAction?.ActionId == actionId && string.IsNullOrEmpty(attempt.PublicationId)
                    && attempt.Releases.Count == 1 && attempt.Releases[0].Returned && attempt.Releases[0].Observed?.ProvesRelease() == true
                    && attempt.Payment?.Amount == gold && attempt.Payment.Invoked == interruptPayment
                    && (interruptPayment ? attempt.Payment.ProvesPayment() : attempt.Payment.Before == null && attempt.Payment.After == null)
                    && result?.ResultCode == "ordinary_ransom_unverified" && holdResult?.ResultCode == result.ResultCode
                    && correctCustody && after.Value<int>("agreementCount") == 0 && after.Value<int>("actionCount") == 0
                    && JToken.DeepEquals(before["payerRelation"], after["payerRelation"])
                    && JToken.DeepEquals(before["recipientRelation"], after["recipientRelation"])
                    && (replay ? JToken.DeepEquals(before, after) && JToken.DeepEquals(saved["after"], after)
                        && JToken.DeepEquals(saved["journal"], heldJournal) && JToken.DeepEquals(saved["receipt"], heldReceipt)
                        : before.Value<int>("agreementCount") == 0 && payerBefore - after.Value<int>("payerGold") == movedGold
                            && after.Value<int>("recipientGold") - recipientBefore == movedGold);
                if (held && !replay)
                {
                    saved["status"] = "held";
                    saved["after"] = after.DeepClone();
                    saved["journal"] = heldJournal.DeepClone();
                    saved["receipt"] = heldReceipt.DeepClone();
                    operations.SaveNativeOperation(actionId, saved);
                }
                var holdEvidence = new JObject
                {
                    ["schema"] = "reign-native-ordinary-ransom-fixture-v1", ["stage"] = stage, ["scenario"] = scenario,
                    ["gameInstanceId"] = _gameInstanceId, ["savePointId"] = point, ["actionId"] = actionId,
                    ["holdReason"] = attempt?.HoldReason, ["before"] = before, ["after"] = after,
                    ["verified"] = held, ["journal"] = heldJournal, ["receipt"] = heldReceipt
                };
                return held ? LiveCommandResult.Completed("Verified the held native ransom without another effect or publication.", holdEvidence)
                    : LiveCommandResult.Failed("The expected native hold was not proved. Restore the previous checkpoint.", holdEvidence);
            }
            bool verified = result?.Success == true && result.Completed && result.ResultCode == "completed"
                && hasAttempt && attempt.IsVerified() && attempt.WorldAction?.ActionId == actionId
                && attempt.PublicationId == receipt && after.Value<int>("agreementCount") == 1
                && attempt.Releases.Select(r => r.HeroId).OrderBy(id => id).SequenceEqual(released.OrderBy(id => id))
                && attempt.Payment?.Amount == gold && attempt.Payment.Invoked == (gold > 0) && correctCustody
                && after.Value<int>("actionCount") == 0
                && (replay ? JToken.DeepEquals(before, after) && JToken.DeepEquals(saved["after"], after)
                    && (pruneHistory ? JToken.DeepEquals(saved["receipt"], JObject.FromObject(result))
                        && behavior.GetLastActionResult(actionId + "_missing") == null
                        : saved.Value<string>("executionSnapshot") == action.ExecutionSnapshotJson)
                    : before.Value<int>("agreementCount") == 0
                        && before.Value<int>("payerGold") - after.Value<int>("payerGold") == gold
                        && after.Value<int>("recipientGold") - before.Value<int>("recipientGold") == gold);
            if (verified && !replay)
            {
                if (pruneHistory)
                {
                    // Save an exact terminal row before removing it. Enqueue and mark it terminal in the same native call.
                    behavior.EnqueueAction(action);
                    action.LastAttemptDay = (float)CampaignTime.Now.ToDays;
                    action.Status = ReignWorldActionStatus.Completed;
                    after = Observe();
                    if (after.Value<int>("actionCount") != 1)
                        return LiveCommandResult.Failed("The fixture's terminal row was not recorded exactly once. Restore the previous checkpoint.");
                    saved["receipt"] = JObject.FromObject(result);
                }
                saved["status"] = "completed";
                saved["after"] = after.DeepClone();
                saved["executionSnapshot"] = action.ExecutionSnapshotJson;
                operations.SaveNativeOperation(actionId, saved);
            }
            var evidence = new JObject
            {
                ["schema"] = "reign-native-ordinary-ransom-fixture-v1", ["stage"] = stage, ["scenario"] = scenario,
                ["gameInstanceId"] = _gameInstanceId, ["savePointId"] = point, ["actionId"] = actionId,
                ["receiptId"] = receipt, ["expectedGold"] = gold, ["releasedHeroIds"] = new JArray(released),
                ["before"] = before, ["after"] = after, ["verified"] = verified, ["result"] = result?.ResultCode,
                ["journal"] = hasAttempt ? JObject.FromObject(attempt) : null,
                ["receipt"] = pruneHistory && verified ? JObject.FromObject(result) : null
            };
            return verified ? LiveCommandResult.Completed("Verified the ordinary ransom's exact native effects.", evidence)
                : LiveCommandResult.Failed("The exact custody, gold or publication did not verify. Restore the retained checkpoint before retrying.", evidence);
        }

        private static bool OrdinaryRansomFixtureId(JToken token) => token?.Type == JTokenType.String
            && token.Value<string>() is { Length: >= 1 and <= 128 } id
            && id.All(ch => char.IsLetterOrDigit(ch) && ch <= 127 || ch is '_' or '-');
    }
}
