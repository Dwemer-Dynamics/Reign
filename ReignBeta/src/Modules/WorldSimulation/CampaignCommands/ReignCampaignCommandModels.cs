using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ReignBeta.World
{
    public sealed class ReignCampaignCommandCapability
    {
        public string Id;
        public string Schema;
        public string Resolver;
        public string AuthorityCheck;
        public string Preflight;
        public string NativeAdapter;
        public string Observer;
        public string CompletionCriteria;
        public string FailureClassification;
        public string ReportEvents;
        public string TestProfile;
        public bool SupportsRegion;
        public bool SupportsDuration;
        public bool RequiresSettlement;
        public bool RequiresParty;
        public bool RequiresWar;

        public bool IsComplete
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Id)
                    && !string.IsNullOrWhiteSpace(Schema)
                    && !string.IsNullOrWhiteSpace(Resolver)
                    && !string.IsNullOrWhiteSpace(AuthorityCheck)
                    && !string.IsNullOrWhiteSpace(Preflight)
                    && !string.IsNullOrWhiteSpace(NativeAdapter)
                    && !string.IsNullOrWhiteSpace(Observer)
                    && !string.IsNullOrWhiteSpace(CompletionCriteria)
                    && !string.IsNullOrWhiteSpace(FailureClassification)
                    && !string.IsNullOrWhiteSpace(ReportEvents)
                    && !string.IsNullOrWhiteSpace(TestProfile);
            }
        }
    }

    /// <summary>
    /// The planner-facing allowlist for supervised campaign orders. A capability is
    /// deliberately invisible unless its complete execution and evidence contract is
    /// registered here.
    /// </summary>
    public static class ReignCampaignCommandCapabilityRegistry
    {
        private static readonly IReadOnlyList<ReignCampaignCommandCapability> Registered =
            new List<ReignCampaignCommandCapability>
            {
                Capability("establish_party", "settlement?", "safe-friendly-spawn", "create-lord-party", "party-led", false, false, false, false),
                Capability("move", "settlement|region", "settlement-or-region", "visit-settlement", "arrival", false, true, false, false),
                Capability("hold_position", "positionX;positionY", "exact-map-position", "move-then-hold", "cancel-or-supersede", false, false, false, false),
                Capability("timed_hold", "durationHours;settlement?", "current-position-or-settlement", "hold", "duration-elapsed", true, false, false, false),
                Capability("patrol", "settlement|region|positionX+positionY;durationHours?", "ordered-region-or-exact-position", "patrol-anchor", "duration-or-cancel", true, false, false, false),
                Capability("scout_report", "settlement|region;durationHours", "ordered-region-anchors", "patrol-and-observe", "report-delivered", true, true, false, false),
                Capability("escort", "targetPartyId|targetHeroStringId;durationHours?", "active-party", "escort-party", "duration-or-target-invalid", true, false, true, false),
                Capability("recruit_resupply", "settlement?;minimumTroops?;minimumFoodDays?", "safe-friendly-settlement", "visit-settlement", "readiness-threshold", false, false, false, false),
                Capability("form_army", "settlement", "friendly-or-hostile-objective", "native-create-and-gather-army", "army-gathered", false, true, false, false),
                Capability("join_army", "targetPartyId|targetHeroStringId", "army-leader-party", "join-and-escort-army", "army-membership", false, false, true, false),
                Capability("leave_army", "agreementRequired", "current-army", "detach-and-hold", "not-in-army", false, false, false, false),
                Capability("disband_army", "reason?", "led-army", "disband-army", "army-disbanded", false, false, false, false),
                Capability("raid", "settlement", "hostile-village", "raid-settlement", "village-raided-or-obsolete", false, true, false, true),
                Capability("besiege_capture", "settlement", "hostile-fortification", "besiege-settlement", "settlement-friendly", false, true, false, true),
                Capability("defend", "settlement|region;durationHours?", "friendly-threatened-anchor", "patrol-or-engage-besieger", "safe-window-or-duration", true, true, false, false),
                Capability("relieve_siege", "settlement", "friendly-besieged-fortification", "engage-besieger", "siege-ended", false, true, false, false),
                Capability("hunt_enemy_parties", "region?;durationHours?", "hostile-party-in-region", "engage-or-patrol", "duration-or-cancel", true, false, false, true),
                Capability("engage_party", "targetPartyId|targetHeroStringId", "active-hostile-party", "engage-party", "map-event-or-target-invalid", false, false, true, true),
                Capability("withdraw", "settlement?", "nearest-safe-friendly-settlement", "visit-settlement", "arrival", false, false, false, false),
                Capability("return_home", "settlement?", "commander-home-settlement", "visit-settlement", "arrival", false, false, false, false)
            };

        public static IReadOnlyList<ReignCampaignCommandCapability> All => Registered;

        public static ReignCampaignCommandCapability Find(string objective)
        {
            string id = NormalizeObjective(objective);
            return Registered.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsPlannerVisible(string objective)
        {
            ReignCampaignCommandCapability capability = Find(objective);
            return capability != null && capability.IsComplete;
        }

        public static string NormalizeObjective(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace('-', '_').Replace(' ', '_');
            switch (normalized)
            {
                case "go":
                case "travel": return "move";
                case "hold_position": return "hold_position";
                case "wait":
                case "hold": return "timed_hold";
                case "scout": return "scout_report";
                case "resupply":
                case "recruit": return "recruit_resupply";
                case "besiege":
                case "capture":
                case "siege": return "besiege_capture";
                case "hunt":
                case "search_and_destroy": return "hunt_enemy_parties";
                case "attack_party": return "engage_party";
                case "home": return "return_home";
                default: return normalized;
            }
        }

        private static ReignCampaignCommandCapability Capability(string id, string schema,
            string resolver, string adapter, string completion, bool duration, bool settlement,
            bool party, bool war)
        {
            return new ReignCampaignCommandCapability
            {
                Id = id,
                Schema = schema,
                Resolver = resolver,
                AuthorityCheck = "sovereign-authority-or-explicit-leader-acceptance",
                Preflight = id == "establish_party"
                    ? "living-free-npc;not-main-hero;party-eligibility;spawn-validity"
                    : "living-free-npc;establish-party-if-planned;not-main-party;army-hierarchy;target-validity;navigation",
                NativeAdapter = adapter,
                Observer = "hourly-and-native-event-supervisor",
                CompletionCriteria = completion,
                FailureClassification = "validation|authority|obsolete|navigation|native|safety|cancelled",
                ReportEvents = "accepted|adapted|guidance|required|refused|deviated|completed|failed|cancelled",
                TestProfile = "native_primitives|composite_orders|fault_recovery",
                SupportsDuration = duration,
                SupportsRegion = !string.IsNullOrWhiteSpace(resolver) && resolver.IndexOf("region", StringComparison.OrdinalIgnoreCase) >= 0,
                RequiresSettlement = settlement,
                RequiresParty = party,
                RequiresWar = war
            };
        }
    }

    public sealed class ReignCampaignOrderStepRecord
    {
        public string StepId = Guid.NewGuid().ToString("N");
        public string Objective = string.Empty;
        public string TargetHeroStringId = string.Empty;
        public string TargetPartyStringId = string.Empty;
        public string TargetSettlementStringId = string.Empty;
        public string Region = string.Empty;
        public string TermsJson = "{}";
        public string Status = "pending";
        public string Stage = "preflight";
        public string LastFailure = string.Empty;
        public double StartedDay;
        public double CompletedDay;
        public double DeadlineDay;
        public double HoldUntilDay;
        public int AttemptCount;

        public JObject Terms
        {
            get
            {
                try { return string.IsNullOrWhiteSpace(TermsJson) ? new JObject() : JObject.Parse(TermsJson); }
                catch { return new JObject(); }
            }
        }
    }

    public sealed class ReignCampaignOrderRecord
    {
        public int Version = 2;
        public int PlanVersion = 2;
        public string OrderId = Guid.NewGuid().ToString("N");
        public string CorrelationId = Guid.NewGuid().ToString("N");
        public string IssuerHeroStringId = string.Empty;
        public string CommanderHeroStringId = string.Empty;
        public string TargetHeroStringId = string.Empty;
        public string TargetPartyStringId = string.Empty;
        public string TargetSettlementStringId = string.Empty;
        public string Objective = string.Empty;
        public string Region = string.Empty;
        public string AuthorityMode = string.Empty;
        public string Status = "accepted";
        public string Stage = "preflight";
        public string TermsJson = "{}";
        public string AnchorSettlementIdsCsv = string.Empty;
        public string PendingReportId = string.Empty;
        public string PendingReportText = string.Empty;
        public string LastDecision = "continue";
        public string LastFailure = string.Empty;
        public string LastNativeSignature = string.Empty;
        public string LlmOutcome = string.Empty;
        public string LlmReason = string.Empty;
        public string HistoryJson = "[]";
        public string PlanHash = string.Empty;
        public List<ReignCampaignOrderStepRecord> Steps = new List<ReignCampaignOrderStepRecord>();
        public int CurrentStepIndex;
        public int CompletedStepCount;
        public double CreatedDay;
        public double AcceptedDay;
        public double StartedDay;
        public double DeadlineDay;
        public double HoldUntilDay;
        public double NextReviewDay;
        public double LastReviewDay;
        public double LastReportDay;
        public double GuidanceDeadlineDay;
        public int Revision;
        public int AttemptCount;
        public int AnchorIndex;
        public int NativeReassertions;
        public int DeviationCount;
        public bool AwaitingGuidance;
        public bool UrgentReportPending;
        public bool LlmJudgmentRequested;

        public bool IsTerminal
        {
            get
            {
                return Status == "completed" || Status == "failed" || Status == "refused"
                    || Status == "cancelled";
            }
        }
    }

    public sealed class ReignCampaignCommandDecisionInput
    {
        public float OwnStrength;
        public float EnemyStrength;
        public float FoodDays;
        public float Morale;
        public float CasualtyRatio;
        public float TravelDays;
        public int Mercy;
        public int Valor;
        public int Honor;
        public int Calculating;
        public int Leadership;
        public int Tactics;
        public int RelationToSovereign;
        public bool SovereignOrder;
        public bool CivilianRelief;
        public bool Conquest;
        public bool TargetUrgent;
        public bool TargetValid = true;
    }

    public sealed class ReignCampaignCommandDecision
    {
        public string Outcome = "continue";
        public string Reason = string.Empty;
        public bool RequiresReport;
        public bool Exceptional;
    }

    public static class ReignCampaignCommandJudgment
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(
            new[] { "continue", "adapt", "request_guidance", "withdraw", "refuse", "deviate" },
            StringComparer.OrdinalIgnoreCase);

        public static bool IsAllowedOutcome(string value) => Allowed.Contains(value ?? string.Empty);

        public static ReignCampaignCommandDecision Evaluate(ReignCampaignCommandDecisionInput input)
        {
            if (input == null || !input.TargetValid)
                return Decision("adapt", "The named objective is no longer valid.", true, false);

            float own = Math.Max(1f, input.OwnStrength);
            float ratio = Math.Max(0f, input.EnemyStrength) / own;
            bool exhausted = input.FoodDays < 1.25f || input.Morale < 25f || input.CasualtyRatio > 0.55f;
            bool suicidal = ratio >= 2.5f || (ratio >= 1.9f && exhausted);
            int obedience = (input.SovereignOrder ? 80 : 20) + input.Honor * 8
                + input.RelationToSovereign / 2 + input.Leadership / 25;
            int compassion = input.Mercy * 18;
            int courage = input.Valor * 15 + input.Tactics / 30;

            if (input.CivilianRelief && input.TargetUrgent)
            {
                if (ratio < 4.5f || courage + compassion + obedience >= 105)
                    return Decision(ratio > 2.2f ? "adapt" : "continue",
                        "The threatened civilians make intervention urgent despite the danger.", ratio > 2.2f, ratio > 2.2f);
                return Decision("request_guidance",
                    "Relief is urgent, but the force is unlikely to reach the civilians intact.", true, true);
            }

            if (suicidal && input.Conquest && compassion >= 18)
            {
                return obedience >= 110
                    ? Decision("request_guidance", "The sovereign command is binding, but the assault would waste the army.", true, true)
                    : Decision("refuse", "The commander will not throw away the army in a hopeless conquest.", true, true);
            }

            if (exhausted && ratio > 1.25f)
                return Decision(input.SovereignOrder ? "request_guidance" : "withdraw",
                    "Supplies, morale, or losses make the current plan unsafe.", true, true);
            if (ratio > 1.65f && input.Calculating > 0)
                return Decision("adapt", "The commander is seeking a safer approach before committing.", true, false);
            return Decision("continue", "The order remains feasible.", false, false);
        }

        private static ReignCampaignCommandDecision Decision(string outcome, string reason,
            bool report, bool exceptional)
        {
            return new ReignCampaignCommandDecision
            {
                Outcome = outcome,
                Reason = reason,
                RequiresReport = report,
                Exceptional = exceptional
            };
        }
    }
}
