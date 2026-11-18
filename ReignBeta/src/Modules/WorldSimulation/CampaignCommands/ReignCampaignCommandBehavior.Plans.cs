using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using ReignBeta.Shared.WarCouncil;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.World;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.CampaignCommands
{
    public sealed partial class ReignCampaignCommandBehavior
    {
        private static bool ValidatePlanShape(ReignWorldActionRecord action, out string reason)
        {
            JObject root = ParseTerms(action.TermsJson);
            JArray requestedSteps = root["steps"] as JArray;
            if (requestedSteps == null || requestedSteps.Count == 0)
            {
                string objective = ReignCampaignCommandCapabilityRegistry.NormalizeObjective(
                    root.Value<string>("objective"));
                if (!ReignCampaignCommandCapabilityRegistry.IsPlannerVisible(objective))
                {
                    reason = "The requested campaign objective is unsupported or incompletely registered: " + objective;
                    return false;
                }
                return ValidateObjectiveShape(action, objective, out reason);
            }
            if (requestedSteps.Count > 20)
            {
                reason = "A campaign plan may contain at most 20 ordered steps.";
                return false;
            }
            for (int i = 0; i < requestedSteps.Count; i++)
            {
                JObject step = requestedSteps[i] as JObject;
                if (step == null)
                {
                    reason = "Campaign plan step " + (i + 1) + " is not an object.";
                    return false;
                }
                JObject terms = step["terms"] as JObject ?? step;
                string objective = ReignCampaignCommandCapabilityRegistry.NormalizeObjective(
                    step.Value<string>("objective") ?? terms.Value<string>("objective"));
                if (!ReignCampaignCommandCapabilityRegistry.IsPlannerVisible(objective))
                {
                    reason = "Campaign plan step " + (i + 1) + " has an unsupported objective: " + objective;
                    return false;
                }
                if (!ValidateStepShape(terms, objective, out reason))
                {
                    reason = "Campaign plan step " + (i + 1) + ": " + reason;
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        private static bool ValidateStepShape(JObject terms, string objective, out string reason)
        {
            reason = string.Empty;
            ReignCampaignCommandCapability capability = ReignCampaignCommandCapabilityRegistry.Find(objective);
            bool hasSettlement = !string.IsNullOrWhiteSpace(terms?.Value<string>("targetSettlementStringId"));
            bool hasRegion = !string.IsNullOrWhiteSpace(terms?.Value<string>("region"));
            bool hasParty = !string.IsNullOrWhiteSpace(terms?.Value<string>("targetHeroStringId"))
                || !string.IsNullOrWhiteSpace(terms?.Value<string>("targetPartyId"));
            bool hasExactPoint = HasExactPoint(terms);
            if (capability.RequiresSettlement && !hasSettlement && !capability.SupportsRegion)
            { reason = objective + " requires a settlement target."; return false; }
            if (capability.RequiresSettlement && !hasSettlement && capability.SupportsRegion && !hasRegion)
            { reason = objective + " requires a settlement or region."; return false; }
            if (capability.RequiresParty && !hasParty)
            { reason = objective + " requires a target party or party leader."; return false; }
            if (objective == "timed_hold" && ReadDouble(terms, "durationHours", 0d) <= 0d)
            { reason = "timed_hold requires a positive durationHours."; return false; }
            if (objective == "scout_report" && ReadDouble(terms, "durationHours", 0d) <= 0d)
            { reason = "scout_report requires a positive durationHours."; return false; }
            if (objective == "hold_position" && !hasExactPoint)
            { reason = "hold_position requires a valid exact map position."; return false; }
            if (objective == "patrol" && !hasSettlement && !hasRegion && !hasExactPoint)
            { reason = "patrol requires a settlement, region, or exact map position."; return false; }
            if (objective == "leave_army" && terms?.Value<bool?>("armyDetachmentAccepted") != true)
            { reason = "leave_army requires explicit armyDetachmentAccepted=true."; return false; }
            return true;
        }

        private static List<ReignCampaignOrderStepRecord> BuildSteps(ReignWorldActionRecord action,
            JObject root, Hero commander)
        {
            List<ReignCampaignOrderStepRecord> steps = new List<ReignCampaignOrderStepRecord>();
            JArray requested = root["steps"] as JArray;
            if (requested != null && requested.Count > 0)
            {
                foreach (JObject source in requested.OfType<JObject>())
                {
                    JObject terms = (source["terms"] as JObject)?.DeepClone() as JObject
                        ?? source.DeepClone() as JObject ?? new JObject();
                    string objective = ReignCampaignCommandCapabilityRegistry.NormalizeObjective(
                        source.Value<string>("objective") ?? terms.Value<string>("objective"));
                    terms["objective"] = objective;
                    steps.Add(CreateStep(objective, terms,
                        source.Value<string>("targetHeroStringId") ?? terms.Value<string>("targetHeroStringId"),
                        source.Value<string>("targetPartyId") ?? terms.Value<string>("targetPartyId"),
                        source.Value<string>("targetSettlementStringId") ?? terms.Value<string>("targetSettlementStringId"),
                        source.Value<string>("region") ?? terms.Value<string>("region")));
                }
            }
            else
            {
                JObject terms = root.DeepClone() as JObject ?? new JObject();
                string objective = ReignCampaignCommandCapabilityRegistry.NormalizeObjective(terms.Value<string>("objective"));
                terms["objective"] = objective;
                steps.Add(CreateStep(objective, terms, action.TargetHeroStringId,
                    terms.Value<string>("targetPartyId"),
                    FirstNonEmpty(action.TargetSettlementStringId, terms.Value<string>("targetSettlementStringId")),
                    terms.Value<string>("region")));
            }
            if (commander?.PartyBelongedTo == null && steps[0].Objective != "establish_party")
            {
                JObject establishTerms = new JObject { ["objective"] = "establish_party" };
                string requestedSpawn = steps.Select(x => x.TargetSettlementStringId)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrWhiteSpace(requestedSpawn))
                    establishTerms["targetSettlementStringId"] = requestedSpawn;
                steps.Insert(0, CreateStep("establish_party", establishTerms, null, null, requestedSpawn, null));
            }
            return steps;
        }

        private static ReignCampaignOrderStepRecord CreateStep(string objective, JObject terms,
            string targetHeroId, string targetPartyId, string targetSettlementId, string region)
        {
            terms = terms ?? new JObject();
            terms.Remove("steps");
            terms["objective"] = objective;
            if (!string.IsNullOrWhiteSpace(targetHeroId)) terms["targetHeroStringId"] = targetHeroId;
            if (!string.IsNullOrWhiteSpace(targetPartyId)) terms["targetPartyId"] = targetPartyId;
            if (!string.IsNullOrWhiteSpace(targetSettlementId)) terms["targetSettlementStringId"] = targetSettlementId;
            if (!string.IsNullOrWhiteSpace(region)) terms["region"] = region;
            return new ReignCampaignOrderStepRecord
            {
                Objective = objective,
                TargetHeroStringId = targetHeroId ?? string.Empty,
                TargetPartyStringId = targetPartyId ?? string.Empty,
                TargetSettlementStringId = targetSettlementId ?? string.Empty,
                Region = ReignCampaignCommandGeography.NormalizeRegion(region),
                TermsJson = terms.ToString(Formatting.None)
            };
        }

        private static void EnsurePlan(ReignCampaignOrderRecord order)
        {
            if (order == null) return;
            if (order.Steps == null) order.Steps = new List<ReignCampaignOrderStepRecord>();
            if (order.Steps.Count == 0)
            {
                order.Steps.Add(new ReignCampaignOrderStepRecord
                {
                    Objective = order.Objective,
                    TargetHeroStringId = order.TargetHeroStringId,
                    TargetPartyStringId = order.TargetPartyStringId,
                    TargetSettlementStringId = order.TargetSettlementStringId,
                    Region = order.Region,
                    TermsJson = string.IsNullOrWhiteSpace(order.TermsJson) ? "{}" : order.TermsJson,
                    Status = order.IsTerminal ? order.Status : "active",
                    Stage = order.Stage,
                    StartedDay = order.StartedDay,
                    DeadlineDay = order.DeadlineDay,
                    HoldUntilDay = order.HoldUntilDay,
                    AttemptCount = order.AttemptCount
                });
            }
            order.Version = 2;
            order.PlanVersion = 2;
            order.CurrentStepIndex = Math.Max(0, Math.Min(order.CurrentStepIndex, order.Steps.Count - 1));
            if (string.IsNullOrWhiteSpace(order.PlanHash)) order.PlanHash = ComputePlanHash(order.Steps);
            ApplyCurrentStepToOrder(order);
        }

        private static ReignCampaignOrderStepRecord CurrentStep(ReignCampaignOrderRecord order)
        {
            if (order?.Steps == null || order.Steps.Count == 0) return null;
            int index = Math.Max(0, Math.Min(order.CurrentStepIndex, order.Steps.Count - 1));
            return order.Steps[index];
        }

        private static void ApplyCurrentStepToOrder(ReignCampaignOrderRecord order)
        {
            ReignCampaignOrderStepRecord step = CurrentStep(order);
            if (step == null) return;
            order.Objective = step.Objective;
            order.TargetHeroStringId = step.TargetHeroStringId;
            order.TargetPartyStringId = step.TargetPartyStringId;
            order.TargetSettlementStringId = step.TargetSettlementStringId;
            order.Region = step.Region;
            order.TermsJson = step.TermsJson;
            order.DeadlineDay = step.DeadlineDay;
            order.HoldUntilDay = step.HoldUntilDay;
        }

        private static void SyncCurrentStepFromOrder(ReignCampaignOrderRecord order)
        {
            ReignCampaignOrderStepRecord step = CurrentStep(order);
            if (step == null) return;
            step.Objective = order.Objective;
            step.TargetHeroStringId = order.TargetHeroStringId;
            step.TargetPartyStringId = order.TargetPartyStringId;
            step.TargetSettlementStringId = order.TargetSettlementStringId;
            step.Region = order.Region;
            step.TermsJson = order.TermsJson;
            step.DeadlineDay = order.DeadlineDay;
            step.HoldUntilDay = order.HoldUntilDay;
            step.Stage = order.Stage;
            step.AttemptCount = order.AttemptCount;
            if (!order.IsTerminal) step.Status = order.Status == "accepted" ? "active" : order.Status;
        }

        private static void ActivateCurrentStep(ReignCampaignOrderRecord order, double now)
        {
            ApplyCurrentStepToOrder(order);
            ReignCampaignOrderStepRecord step = CurrentStep(order);
            if (step == null) return;
            if (step.StartedDay <= 0d) step.StartedDay = now;
            step.Status = "active";
            step.Stage = "preflight";
            double duration = ReadDouble(ParseTerms(step.TermsJson), "durationHours", DefaultDurationHours(step.Objective));
            bool awaitingHoldArrival = step.Objective == "timed_hold"
                && !string.IsNullOrWhiteSpace(step.TargetSettlementStringId);
            step.DeadlineDay = duration > 0d && !awaitingHoldArrival ? now + duration / 24d : 0d;
            step.HoldUntilDay = step.Objective == "timed_hold" && !awaitingHoldArrival
                ? ReignTimedHoldPolicy.Deadline(now, duration, false, true, 0d) : 0d;
            ApplyCurrentStepToOrder(order);
        }

        private static bool InsertPartyRecoveryStep(ReignCampaignOrderRecord order, Hero commander)
        {
            if (order == null || commander?.Clan == null || commander.IsDead || commander.IsPrisoner) return false;
            JObject terms = new JObject { ["objective"] = "establish_party", ["recovery"] = true };
            ReignCampaignOrderStepRecord recovery = CreateStep("establish_party", terms, null, null,
                commander.HomeSettlement?.StringId ?? commander.Clan.InitialHomeSettlement?.StringId, null);
            order.Steps.Insert(order.CurrentStepIndex, recovery);
            order.PlanHash = ComputePlanHash(order.Steps);
            ActivateCurrentStep(order, CurrentDay());
            AppendHistory(order, "party_recovery", "The commander lost their party; a bounded party-establishment step was inserted before resuming the plan.");
            return true;
        }

        private static string ComputePlanHash(IEnumerable<ReignCampaignOrderStepRecord> steps)
        {
            string canonical = string.Join("\n", (steps ?? Enumerable.Empty<ReignCampaignOrderStepRecord>()).Select(x =>
                (x?.Objective ?? string.Empty) + "|" + (x?.TargetHeroStringId ?? string.Empty) + "|"
                + (x?.TargetPartyStringId ?? string.Empty) + "|" + (x?.TargetSettlementStringId ?? string.Empty)
                + "|" + (x?.Region ?? string.Empty) + "|" + (x?.TermsJson ?? "{}")));
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)).Select(x => x.ToString("x2")));
        }

        private static Settlement ResolvePartySpawn(Hero commander, ReignCampaignOrderRecord order)
        {
            Settlement requested = ReignObjectResolver.FindSettlement(order?.TargetSettlementStringId);
            IFaction faction = commander?.MapFaction;
            if (requested != null && !requested.IsUnderSiege && requested.MapFaction == faction) return requested;
            return new[] { commander?.CurrentSettlement, commander?.HomeSettlement, commander?.Clan?.InitialHomeSettlement }
                .FirstOrDefault(x => x != null && !x.IsUnderSiege && (faction == null || x.MapFaction == faction))
                ?? Settlement.All.Where(x => x != null && !x.IsHideout && !x.IsUnderSiege
                    && (x.IsTown || x.IsCastle) && (faction == null || x.MapFaction == faction))
                    .OrderBy(x => commander?.HomeSettlement == null ? 0f
                        : x.GetPosition2D.DistanceSquared(commander.HomeSettlement.GetPosition2D)).FirstOrDefault();
        }

        private static int ResolveRecruitmentTarget(ReignCampaignOrderRecord order, MobileParty party, JObject terms)
        {
            int explicitTarget = (int)ReadDouble(terms, "minimumTroops", 0d);
            if (explicitTarget > 0) return explicitTarget;
            int target = Math.Max(1, (int)Math.Ceiling((party?.Party?.PartySizeLimit ?? 1) * 0.8d));
            terms["minimumTroops"] = target;
            terms["minimumFoodDays"] = ReadDouble(terms, "minimumFoodDays", 3d);
            order.TermsJson = terms.ToString(Formatting.None);
            return target;
        }

        private static bool ValidateRecruitmentCapacity(ReignCampaignOrderRecord order, MobileParty party, out string failure)
        {
            JObject terms = ParseTerms(order.TermsJson);
            int target = ResolveRecruitmentTarget(order, party, terms);
            int infantry = Math.Max(0, (int)ReadDouble(terms, "minimumInfantry", 0d));
            int archers = Math.Max(0, (int)ReadDouble(terms, "minimumArchers", 0d));
            int cavalry = Math.Max(0, (int)ReadDouble(terms, "minimumCavalry", 0d));
            int requested = Math.Max(target, infantry + archers + cavalry);
            int limit = party?.Party?.PartySizeLimit ?? 0;
            if (requested > limit)
            {
                failure = "The requested force requires " + requested + " regular troops, but the commander's current native party limit is "
                    + limit + ". Revise the composition or total before the plan can continue.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private bool ReassessRecruitmentResources(ReignCampaignOrderRecord order, MobileParty party, ref Settlement target)
        {
            var terms = ParseTerms(order.TermsJson);
            double now = CurrentDay();
            if (ReadDouble(terms, "economyReviewDay", -1) == Math.Floor(now))
            {
                string anchor = terms.Value<string>("economyRecruitAnchor");
                target = string.IsNullOrEmpty(anchor) ? null : ReignObjectResolver.FindSettlement(anchor);
                if (target != null) return true;
                order.NextReviewDay = Math.Floor(now) + 1;
                return false;
            }
            CountTroopRoles(party, out int regulars, out int infantry, out int archers, out int cavalry);
            bool needTroops = regulars < ResolveRecruitmentTarget(order, party, terms)
                || infantry < ReadDouble(terms, "minimumInfantry", 0) || archers < ReadDouble(terms, "minimumArchers", 0)
                || cavalry < ReadDouble(terms, "minimumCavalry", 0);
            bool needFood = party.GetNumDaysForFoodToLast() < ReadDouble(terms, "minimumFoodDays", 3);
            var payer = party.LeaderHero;
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models;
            var economy = ReignServices.Economy;
            Func<Settlement, bool> eligible = settlement => {
                if (settlement.MapFaction != party.MapFaction || settlement.IsUnderSiege || settlement.IsUnderRaid || settlement.IsRaided
                    || (!settlement.IsTown && !settlement.IsVillage)) return false;
                if (needFood && !settlement.ItemRoster.Any(x => x.Amount > 0 && x.EquipmentElement.Item?.IsFood == true
                    && settlement.SettlementComponent.GetItemPrice(x.EquipmentElement, party) <= payer.Gold)) return false;
                if (!needTroops) return payer.Gold > 0;
                if ((economy?.GetRecruitCapacity(settlement) ?? 0) <= 0) return false;
                return settlement.Notables.Where(x => x.IsAlive).Any(notable => notable.VolunteerTypes
                    .Take(model.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(payer, notable))
                    .Where(troop => troop != null).Any(troop => ReignBeta.WorldSimulation.Economy.ReignWarEconomyTransactions.Allow(
                        ReignBeta.WorldSimulation.Economy.ReignWarEconomyTransactions.Ai, settlement, party, payer, troop, 1, true, recordBlocked: false) > 0));
            };
            target = Settlement.All.Where(eligible).OrderBy(x => x.GetPosition2D.DistanceSquared(party.GetPosition2D))
                .ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            terms["economyReviewDay"] = Math.Floor(now);
            terms["economyRecruitAnchor"] = target?.StringId ?? "";
            order.TermsJson = terms.ToString(Formatting.None);
            if (target != null)
            {
                order.TargetSettlementStringId = target.StringId;
                order.AnchorSettlementIdsCsv = target.StringId;
                order.LastFailure = "";
                return true;
            }
            const string reason = "Waiting for accessible volunteers of the requested types, party space, recovered village households, affordable food, or seven days of military funding. The requested force is unchanged; resources will be reassessed daily.";
            bool firstWait = order.Stage != "waiting_for_resources";
            order.Status = "active";
            order.Stage = "waiting_for_resources";
            order.LastFailure = reason;
            order.NextReviewDay = Math.Floor(now) + 1;
            party.SetMoveModeHold();
            if (firstWait) { AppendHistory(order, "waiting_for_resources", reason); Publish(order, "waiting_for_resources"); }
            return false;
        }

        internal static bool RecruitmentFitsActiveOrder(MobileParty party, CharacterObject troop)
        {
            var order = Instance?.ActiveOrderFor(party?.LeaderHero?.StringId);
            if (order == null || order.Objective != "recruit_resupply" || order.AwaitingGuidance
                || order.Status != "active") return true;
            var terms = ParseTerms(order.TermsJson);
            CountTroopRoles(party, out int regulars, out int infantry, out int archers, out int cavalry);
            bool needInfantry = infantry < ReadDouble(terms, "minimumInfantry", 0);
            bool needArchers = archers < ReadDouble(terms, "minimumArchers", 0);
            bool needCavalry = cavalry < ReadDouble(terms, "minimumCavalry", 0);
            if (needInfantry || needArchers || needCavalry)
                return troop.IsMounted ? needCavalry : troop.IsRanged ? needArchers : needInfantry;
            return regulars < ResolveRecruitmentTarget(order, party, terms);
        }

        private static void CountTroopRoles(MobileParty party, out int regulars, out int infantry,
            out int archers, out int cavalry)
        {
            regulars = infantry = archers = cavalry = 0;
            if (party?.MemberRoster == null) return;
            foreach (var element in party.MemberRoster.GetTroopRoster())
            {
                if (element.Character == null || element.Character.IsHero || element.Number <= 0) continue;
                regulars += element.Number;
                if (element.Character.IsMounted) cavalry += element.Number;
                else if (element.Character.IsRanged) archers += element.Number;
                else infantry += element.Number;
            }
        }

        private static JObject ParseTerms(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json); }
            catch { return new JObject(); }
        }

        private static string ReadString(string json, string key, string fallback)
        {
            JToken value = ParseTerms(json)[key];
            return value == null || value.Type == JTokenType.Null ? fallback : value.ToString();
        }

        private static bool ReadBool(string json, string key, bool fallback)
        {
            JToken value = ParseTerms(json)[key];
            if (value == null) return fallback;
            if (value.Type == JTokenType.Boolean) return value.Value<bool>();
            return bool.TryParse(value.ToString(), out bool parsed) ? parsed : fallback;
        }

        private static double ReadDouble(JObject obj, string key, double fallback)
        {
            JToken value = obj?[key];
            if (value == null) return fallback;
            return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double parsed) ? parsed : fallback;
        }

        private static bool HasExactPoint(JObject terms)
        {
            if (terms == null || terms["positionX"] == null || terms["positionY"] == null) return false;
            double x = ReadDouble(terms, "positionX", double.NaN);
            double y = ReadDouble(terms, "positionY", double.NaN);
            return ReignWarCouncilRules.IsWorldPointValid(x, y);
        }

        private static bool TryReadExactPoint(ReignCampaignOrderRecord order, MobileParty party,
            out CampaignVec2 point)
        {
            JObject terms = ParseTerms(order?.TermsJson);
            double x = ReadDouble(terms, "positionX", double.NaN);
            double y = ReadDouble(terms, "positionY", double.NaN);
            if (!ReignWarCouncilRules.IsWorldPointValid(x, y))
            {
                point = CampaignVec2.Invalid;
                return false;
            }
            point = new CampaignVec2(new Vec2((float)x, (float)y), party?.Position.IsOnLand ?? true);
            return true;
        }

        private static CampaignVec2 PatrolWaypoint(CampaignVec2 center, int index)
        {
            double radians = (Math.Abs(index) % 8) * Math.PI / 4d;
            Vec2 offset = new Vec2((float)(Math.Cos(radians) * 6d), (float)(Math.Sin(radians) * 6d));
            return new CampaignVec2(center.ToVec2() + offset, center.IsOnLand);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private static string NormalizeResponse(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace('-', '_').Replace(' ', '_');
            if (normalized.Contains("cancel")) return "cancel";
            if (normalized.Contains("withdraw") || normalized.Contains("retreat")) return "withdraw";
            if (normalized.Contains("hold") || normalized.Contains("wait")) return "hold";
            if (normalized.Contains("adapt") || normalized.Contains("judgment")) return "adapt";
            return "continue";
        }

        private static double DefaultDurationHours(string objective)
        {
            return 0d;
        }

        private static double CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
        }
    }
}
