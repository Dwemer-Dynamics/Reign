using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<JObject> RunCampaignCommandSoakAsync(JObject command, int seed)
        {
            int requestedDays = Math.Max(1, Math.Min(30, command.Value<int?>("soakDays") ?? 30));
            int pass = command.Value<int?>("pass") ?? 1;
            string runScope = command.Value<string>("certificationRunId")
                ?? command.Value<string>("runId") ?? "campaign_command_soak";
            string disposableSaveName = command.Value<string>("disposableSaveName") ?? string.Empty;
            LiveCommandResult checkpoint = await SaveCheckpointAsync(new JObject
            {
                ["saveName"] = disposableSaveName,
                ["timeoutSeconds"] = 300
            }).ConfigureAwait(false);
            bool checkpointPassed = string.Equals(checkpoint.Status, "completed",
                StringComparison.OrdinalIgnoreCase);
            command["checkpointPrepared"] = checkpointPassed;
            command["checkpointEvidence"] = checkpoint.Data ?? new JObject();
            if (!checkpointPassed)
            {
                CampaignCommandSoakFixture failedFixture = new CampaignCommandSoakFixture
                {
                    Error = string.IsNullOrWhiteSpace(checkpoint.Error)
                        ? "The isolated native checkpoint could not be created." : checkpoint.Error
                };
                failedFixture.Checks.Add(SoakCheck("case_checkpoint_observed", false));
                return BuildCampaignCommandSoakResult(false, requestedDays, 0d, 0d,
                    new JArray(), failedFixture.Checks, failedFixture, command, failedFixture.Error);
            }
            CampaignCommandSoakFixture fixture = await ReignMainThread.InvokeAsync(() =>
                PrepareCampaignCommandSoakFixture(runScope, seed, pass)).ConfigureAwait(false);
            JArray checks = fixture.Checks;
            checks.Add(SoakCheck("case_checkpoint_observed", true));
            if (!fixture.Prepared)
            {
                JObject cleanup = await ReignMainThread.InvokeAsync(() =>
                    CleanupCampaignCommandSoakFixture(fixture)).ConfigureAwait(false);
                foreach (JObject check in cleanup.Value<JArray>("checks")?.OfType<JObject>()
                    ?? Enumerable.Empty<JObject>()) checks.Add(check);
                return BuildCampaignCommandSoakResult(false, requestedDays, 0d, 0d,
                    new JArray(), checks, fixture, command,
                    fixture.Error.Length > 0 ? fixture.Error : "The native soak fixture could not be prepared.");
            }

            JArray advances = new JArray();
            double startDay = 0d;
            double completedDay = 0d;
            int completedHours = 0;
            string advanceError = string.Empty;
            bool advancesPassed = true;
            try
            {
                int firstSegmentDays = Math.Min(1, requestedDays);
                int remainingDays = requestedDays - firstSegmentDays;
                foreach (int segmentDays in new[] { firstSegmentDays, remainingDays }.Where(x => x > 0))
                {
                    JObject advanceCommand = new JObject(command)
                    {
                        ["days"] = segmentDays,
                        ["timeoutSeconds"] = Math.Max(30,
                            Math.Min(21600, command.Value<int?>("timeoutSeconds") ?? 5400))
                    };
                    LiveCommandResult advance = await PassiveWorldAdvanceAsync(advanceCommand)
                        .ConfigureAwait(false);
                    JObject data = advance.Data ?? new JObject();
                    double segmentStart = data.Value<double?>("startWorldDay") ?? completedDay;
                    double segmentEnd = data.Value<double?>("worldDay") ?? segmentStart;
                    if (advances.Count == 0) startDay = segmentStart;
                    completedDay = segmentEnd;
                    int segmentHours = (int)Math.Max(0d,
                        Math.Min(segmentDays * 24d, (segmentEnd - segmentStart) * 24d));
                    completedHours += segmentHours;
                    bool segmentPassed = string.Equals(advance.Status, "completed",
                            StringComparison.OrdinalIgnoreCase)
                        && segmentEnd - segmentStart >= segmentDays;
                    advancesPassed &= segmentPassed;
                    if (!segmentPassed && string.IsNullOrWhiteSpace(advanceError))
                        advanceError = string.IsNullOrWhiteSpace(advance.Error)
                            ? "A native campaign-time segment did not reach its checkpoint." : advance.Error;
                    data["requestedSegmentDays"] = segmentDays;
                    data["status"] = advance.Status ?? string.Empty;
                    advances.Add(data);

                    if (advances.Count == 1)
                    {
                        JObject recovery = await ReignMainThread.InvokeAsync(() =>
                            ObserveAndRestoreCampaignCommandSoakChanges(fixture)).ConfigureAwait(false);
                        foreach (JObject check in recovery.Value<JArray>("checks")?.OfType<JObject>()
                            ?? Enumerable.Empty<JObject>()) checks.Add(check);
                    }
                    if (!segmentPassed) break;
                }
            }
            finally
            {
                JObject cleanup = await ReignMainThread.InvokeAsync(() =>
                    CleanupCampaignCommandSoakFixture(fixture)).ConfigureAwait(false);
                foreach (JObject check in cleanup.Value<JArray>("checks")?.OfType<JObject>()
                    ?? Enumerable.Empty<JObject>()) checks.Add(check);
            }

            bool checksPassed = checks.OfType<JObject>().All(x => x.Value<bool>("passed"));
            bool passed = advancesPassed && completedHours >= requestedDays * 24 && checksPassed;
            return BuildCampaignCommandSoakResult(passed, requestedDays, startDay, completedDay,
                advances, checks, fixture, command, passed ? string.Empty : advanceError);
        }

        private sealed class CampaignCommandSoakFixture
        {
            public readonly JArray Checks = new JArray();
            public readonly List<ReignCampaignOrderRecord> Orders = new List<ReignCampaignOrderRecord>();
            public bool Prepared;
            public string Error = string.Empty;
            public Kingdom PlayerKingdom;
            public Kingdom TemporaryWarTarget;
            public bool TemporaryWarCreated;
            public Settlement OwnershipSettlement;
            public Hero OriginalSettlementOwner;
            public Hero TemporarySettlementOwner;
            public bool OwnershipChanged;
            public TaleWorlds.CampaignSystem.Siege.SiegeEvent Siege;
            public bool SiegeStarted;
            public MobileParty LossParty;
            public CharacterObject LossTroop;
            public int LossCount;
            public int InitialConcurrentOrders;
        }

        private static CampaignCommandSoakFixture PrepareCampaignCommandSoakFixture(
            string runScope, int seed, int pass)
        {
            CampaignCommandSoakFixture fixture = new CampaignCommandSoakFixture();
            try
            {
                List<MobileParty> parties = MobileParty.All.Where(x => x != null && x.IsActive
                        && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                        && x.MapEvent == null && x.BesiegedSettlement == null
                        && x.MapFaction is Kingdom
                        && (x.Army == null || x.Army.LeaderParty == x))
                    .OrderByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
                Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
                fixture.PlayerKingdom = parties.Select(x => x.MapFaction as Kingdom).Where(x => x != null)
                    .Distinct().OrderByDescending(x => x == playerKingdom ? 10000 : 0)
                    .ThenByDescending(x => parties.Count(p => p.MapFaction == x))
                    .FirstOrDefault(x => x.Clans.Count(c => !c.IsEliminated && c.Leader != null) >= 2
                        && Settlement.All.Any(s => s != null && s.IsFortification
                            && s.MapFaction == x && !s.IsUnderSiege
                            && s != MobileParty.MainParty?.CurrentSettlement));
                if (fixture.PlayerKingdom != null)
                    parties = parties.OrderByDescending(x => x.MapFaction == fixture.PlayerKingdom)
                        .ThenByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
                List<Settlement> friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                        && x.MapFaction == fixture.PlayerKingdom && !x.IsUnderSiege)
                    .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
                if (fixture.PlayerKingdom == null || parties.Count < 3 || friendly.Count == 0)
                {
                    fixture.Error = "The disposable campaign needs three eligible NPC-led parties and one reversible kingdom fixture.";
                    return fixture;
                }

                fixture.TemporaryWarTarget = Kingdom.All.FirstOrDefault(x => x != null
                    && x != fixture.PlayerKingdom && !x.IsEliminated
                    && !FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, x));
                if (fixture.TemporaryWarTarget != null)
                {
                    DeclareWarAction.ApplyByDefault(fixture.PlayerKingdom, fixture.TemporaryWarTarget);
                    fixture.TemporaryWarCreated = FactionManager.IsAtWarAgainstFaction(
                        fixture.PlayerKingdom, fixture.TemporaryWarTarget);
                }
                fixture.Checks.Add(SoakCheck("war_change_observed", fixture.TemporaryWarCreated));

                fixture.OwnershipSettlement = friendly.FirstOrDefault(x => x.IsFortification
                    && x != MobileParty.MainParty?.CurrentSettlement && x.OwnerClan?.Leader != null
                    && fixture.PlayerKingdom.Clans.Any(c => c != x.OwnerClan && !c.IsEliminated && c.Leader != null));
                if (fixture.OwnershipSettlement != null)
                {
                    fixture.OriginalSettlementOwner = fixture.OwnershipSettlement.OwnerClan.Leader;
                    fixture.TemporarySettlementOwner = fixture.PlayerKingdom.Clans
                        .Where(c => c != fixture.OwnershipSettlement.OwnerClan && !c.IsEliminated && c.Leader != null)
                        .Select(c => c.Leader).FirstOrDefault();
                    ChangeOwnerOfSettlementAction.ApplyByGift(fixture.OwnershipSettlement,
                        fixture.TemporarySettlementOwner);
                    fixture.OwnershipChanged = fixture.OwnershipSettlement.OwnerClan
                        == fixture.TemporarySettlementOwner?.Clan;
                }
                fixture.Checks.Add(SoakCheck("ownership_change_observed", fixture.OwnershipChanged));

                Settlement siegeTarget = friendly.FirstOrDefault(x => x.IsFortification
                    && x != MobileParty.MainParty?.CurrentSettlement && !x.IsUnderSiege);
                MobileParty enemyParty = MobileParty.All.Where(x => x != null && x.IsActive
                        && x.LeaderHero != null && x.MapEvent == null && x.BesiegedSettlement == null
                        && FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, x.MapFaction))
                    .OrderBy(x => siegeTarget == null ? 0f
                        : x.GetPosition2D.DistanceSquared(siegeTarget.GetPosition2D)).FirstOrDefault();
                if (siegeTarget != null && enemyParty != null)
                {
                    try
                    {
                        fixture.Siege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager
                            .StartSiegeEvent(siegeTarget, enemyParty);
                        fixture.SiegeStarted = siegeTarget.IsUnderSiege && fixture.Siege != null;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command soak siege fixture could not be created: " + ex.Message);
                    }
                }
                fixture.Checks.Add(SoakCheck("siege_observed", fixture.SiegeStarted));

                string[] preferredObjectives = { "patrol", "defend", "timed_hold" };
                for (int index = 0; index < preferredObjectives.Length; index++)
                {
                    MobileParty actor = parties[index];
                    Settlement actorSettlement = Settlement.All.FirstOrDefault(x => x != null
                        && !x.IsHideout && x.MapFaction == actor.MapFaction && !x.IsUnderSiege);
                    string objective = preferredObjectives[index];
                    if (objective != "timed_hold" && actorSettlement == null) objective = "timed_hold";
                    JObject terms = new JObject
                    {
                        ["objective"] = objective,
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["durationHours"] = 744
                    };
                    Settlement settlement = objective == "timed_hold" ? null : actorSettlement;
                    string actionId = "campaign_command_soak_" + runScope + "_p" + pass + "_s" + seed
                        + "_" + objective + "_" + index;
                    ReignActionResult issued = ReignServices.CampaignCommand.ExecuteControlAction(
                        new ReignWorldActionRecord
                        {
                            ActionId = actionId,
                            Type = ReignWorldActionType.RegularIssueCampaignOrder,
                            Source = "campaign_command_certification_soak",
                            ActorHeroStringId = actor.LeaderHero.StringId,
                            TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                            TermsJson = terms.ToString(Formatting.None),
                            AuthorizationMode = "accepted_recommendation",
                            AcceptedByHeroStringId = actor.LeaderHero.StringId,
                            RequiresAcceptance = false,
                            Reason = "Disposable simultaneous-order native soak."
                        });
                    ReignCampaignOrderRecord order = ReignServices.CampaignCommand.Orders
                        .FirstOrDefault(x => x.OrderId == actionId);
                    if (issued.Success && order != null && !order.IsTerminal
                        && !string.IsNullOrWhiteSpace(order.LastNativeSignature)) fixture.Orders.Add(order);
                }
                fixture.InitialConcurrentOrders = fixture.Orders.Count;
                fixture.Checks.Add(SoakCheck("simultaneous_orders", fixture.InitialConcurrentOrders >= 3,
                    fixture.InitialConcurrentOrders));

                fixture.LossParty = parties.FirstOrDefault(x => x.MemberRoster?.TotalManCount > 2);
                var lossElement = fixture.LossParty?.MemberRoster?.GetTroopRoster()
                    .FirstOrDefault(x => x.Character != null && !x.Character.IsHero && x.Number > 0);
                fixture.LossTroop = lossElement.HasValue ? lossElement.Value.Character : null;
                if (fixture.LossParty != null && fixture.LossTroop != null)
                {
                    int before = fixture.LossParty.MemberRoster.TotalManCount;
                    fixture.LossParty.MemberRoster.AddToCounts(fixture.LossTroop, -1);
                    fixture.LossCount = before - fixture.LossParty.MemberRoster.TotalManCount;
                }
                fixture.Checks.Add(SoakCheck("party_loss_observed", fixture.LossCount == 1,
                    fixture.LossCount));
                fixture.Checks.Add(SoakCheck("main_party_untouched",
                    !ReignServices.CampaignCommand.Orders.Any(x => x != null && !x.IsTerminal
                        && string.Equals(x.CommanderHeroStringId, Hero.MainHero?.StringId,
                            StringComparison.OrdinalIgnoreCase))));
                fixture.Prepared = fixture.Checks.OfType<JObject>().All(x => x.Value<bool>("passed"));
            }
            catch (Exception ex)
            {
                fixture.Error = ex.Message;
                ReignLog.Warn("Campaign Command soak fixture preparation failed: " + ex);
            }
            return fixture;
        }

        private static JObject ObserveAndRestoreCampaignCommandSoakChanges(CampaignCommandSoakFixture fixture)
        {
            JArray checks = new JArray();
            int observed = fixture.Orders.Count(x => x != null && (x.IsTerminal
                || (!string.IsNullOrWhiteSpace(x.LastNativeSignature) && x.LastReviewDay > x.AcceptedDay)));
            checks.Add(SoakCheck("hourly_order_review_observed", observed == fixture.Orders.Count, observed));
            bool restored = RestoreCampaignCommandSoakWorld(fixture);
            checks.Add(SoakCheck("changed_world_restored", restored));
            return new JObject { ["checks"] = checks };
        }

        private static JObject CleanupCampaignCommandSoakFixture(CampaignCommandSoakFixture fixture)
        {
            JArray checks = new JArray();
            bool worldRestored = RestoreCampaignCommandSoakWorld(fixture);
            int cancellations = 0;
            foreach (ReignCampaignOrderRecord order in fixture.Orders.Where(x => x != null && !x.IsTerminal))
            {
                ReignActionResult result = ReignServices.CampaignCommand.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        Source = "campaign_command_certification_soak_cleanup",
                        ActorHeroStringId = order.CommanderHeroStringId,
                        Reason = "Native soak cleanup.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                    });
                if (result.Success && order.IsTerminal) cancellations++;
            }
            int duplicates = ReignServices.CampaignCommand?.Orders
                .GroupBy(x => x.OrderId, StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1) ?? 0;
            int active = ReignServices.CampaignCommand?.Orders.Count(x => x != null && !x.IsTerminal) ?? 0;
            bool terminal = fixture.Orders.All(x => x != null && x.IsTerminal);
            checks.Add(SoakCheck("cleanup_world_restored", worldRestored));
            checks.Add(SoakCheck("terminal_orders_bounded", terminal, cancellations));
            checks.Add(SoakCheck("no_duplicate_effects", duplicates == 0, duplicates));
            checks.Add(SoakCheck("bounded_queue", active < 100, active));
            return new JObject { ["checks"] = checks };
        }

        private static bool RestoreCampaignCommandSoakWorld(CampaignCommandSoakFixture fixture)
        {
            bool restored = true;
            if (fixture.Siege != null)
            {
                try { fixture.Siege.FinalizeSiegeEvent(); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak siege cleanup failed: " + ex.Message);
                }
                fixture.Siege = null;
            }
            if (fixture.OwnershipChanged && fixture.OwnershipSettlement != null
                && fixture.OriginalSettlementOwner != null
                && fixture.OwnershipSettlement.OwnerClan != fixture.OriginalSettlementOwner.Clan)
            {
                try { ChangeOwnerOfSettlementAction.ApplyByGift(fixture.OwnershipSettlement,
                    fixture.OriginalSettlementOwner); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak owner cleanup failed: " + ex.Message);
                }
            }
            if (fixture.TemporaryWarCreated && fixture.PlayerKingdom != null
                && fixture.TemporaryWarTarget != null
                && FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, fixture.TemporaryWarTarget))
            {
                try { MakePeaceAction.ApplyByKingdomDecision(fixture.PlayerKingdom,
                    fixture.TemporaryWarTarget, 0, 0); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak war cleanup failed: " + ex.Message);
                }
            }
            if (fixture.LossCount > 0 && fixture.LossParty != null && fixture.LossParty.IsActive
                && fixture.LossTroop != null)
            {
                try { fixture.LossParty.MemberRoster.AddToCounts(fixture.LossTroop, fixture.LossCount); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak roster cleanup failed: " + ex.Message);
                }
                fixture.LossCount = 0;
            }
            return restored
                && (fixture.OwnershipSettlement == null || fixture.OriginalSettlementOwner == null
                    || fixture.OwnershipSettlement.OwnerClan == fixture.OriginalSettlementOwner.Clan)
                && (fixture.PlayerKingdom == null || fixture.TemporaryWarTarget == null
                    || !FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, fixture.TemporaryWarTarget));
        }

        private static JObject BuildCampaignCommandSoakResult(bool passed, int requestedDays,
            double startDay, double completedDay, JArray advances, JArray checks,
            CampaignCommandSoakFixture fixture, JObject command, string error)
        {
            int passedChecks = checks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
            int hourlyCases = requestedDays * 24;
            int completedHours = (int)Math.Max(0d, Math.Min(hourlyCases,
                (completedDay - startDay) * 24d));
            JObject metrics = new JObject
            {
                ["caseCheckpointObserved"] = command.Value<bool?>("checkpointPrepared") == true,
                ["simultaneousOrders"] = CheckPassed(checks, "simultaneous_orders"),
                ["simultaneousOrderCount"] = fixture.InitialConcurrentOrders,
                ["ownershipChangeObserved"] = CheckPassed(checks, "ownership_change_observed"),
                ["warChangeObserved"] = CheckPassed(checks, "war_change_observed"),
                ["siegeObserved"] = CheckPassed(checks, "siege_observed"),
                ["partyLossObserved"] = CheckPassed(checks, "party_loss_observed"),
                ["hourlyOrderReviewObserved"] = CheckPassed(checks, "hourly_order_review_observed"),
                ["worldRestored"] = CheckPassed(checks, "changed_world_restored")
                    && CheckPassed(checks, "cleanup_world_restored"),
                ["mainPartyUntouched"] = CheckPassed(checks, "main_party_untouched"),
                ["noDuplicateEffects"] = CheckPassed(checks, "no_duplicate_effects"),
                ["boundedQueue"] = CheckPassed(checks, "bounded_queue"),
                ["terminalOrdersBounded"] = CheckPassed(checks, "terminal_orders_bounded")
            };
            bool metricGate = metrics.Properties().Where(x => x.Name != "simultaneousOrderCount")
                .All(x => x.Value.Type == JTokenType.Boolean && x.Value.Value<bool>());
            passed &= metricGate && fixture.InitialConcurrentOrders >= 3;
            return new JObject
            {
                ["ok"] = passed,
                ["caseCount"] = hourlyCases + checks.Count,
                ["passedCaseCount"] = completedHours + passedChecks,
                ["startDay"] = startDay,
                ["completedDay"] = completedDay,
                ["requestedDays"] = requestedDays,
                ["checks"] = checks,
                ["advances"] = advances,
                ["checkpointEvidence"] = command["checkpointEvidence"] ?? new JObject(),
                ["metrics"] = metrics,
                ["error"] = passed ? string.Empty : string.IsNullOrWhiteSpace(error)
                    ? "The native soak failed its hourly, world-change, recovery, or safety evidence gate." : error
            };
        }

        private static JObject SoakCheck(string id, bool passed, int observed = 0) => new JObject
        {
            ["id"] = id, ["passed"] = passed, ["observed"] = observed
        };

        private static bool CheckPassed(JArray checks, string id) => checks.OfType<JObject>()
            .Any(x => string.Equals(x.Value<string>("id"), id, StringComparison.Ordinal)
                && x.Value<bool>("passed"));
    }
}
