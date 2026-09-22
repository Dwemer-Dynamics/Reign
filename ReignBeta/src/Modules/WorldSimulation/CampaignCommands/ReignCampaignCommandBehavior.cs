using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.Save;
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

namespace ReignBeta.World
{
    public sealed class ReignCampaignCommandBehavior : CampaignBehaviorBase
    {
        private const double Hour = 1d / 24d;
        private const double GuidanceWindow = 12d / 24d;
        private const float NearbyEnemyRadiusSquared = 25f * 25f;
        private List<ReignCampaignOrderRecord> _orders = new List<ReignCampaignOrderRecord>();
        private List<string> _stateChunks = new List<string>();
        private string _legacyState = string.Empty;

        public static ReignCampaignCommandBehavior Instance { get; private set; }
        public IReadOnlyList<ReignCampaignOrderRecord> Orders => _orders;

        public ReignCampaignOrderRecord ActiveOrderFor(string commanderHeroStringId)
        {
            return _orders.LastOrDefault(x => x != null && !x.IsTerminal
                && string.Equals(x.CommanderHeroStringId, commanderHeroStringId,
                    StringComparison.OrdinalIgnoreCase));
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeEventStarted);
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEventEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                foreach (ReignCampaignOrderRecord order in _orders.Where(x => x != null))
                {
                    EnsurePlan(order);
                    SyncCurrentStepFromOrder(order);
                }
                _stateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_orders));
                _legacyState = string.Empty;
            }
            dataStore.SyncData("_reign_campaignCommandState", ref _legacyState);
            dataStore.SyncData("_reign_campaignCommandStateChunks", ref _stateChunks);
            if (dataStore.IsLoading)
            {
                try
                {
                    string json = _stateChunks != null && _stateChunks.Count > 0
                        ? ReignSavePayloadCodec.Decode(_stateChunks)
                        : _legacyState;
                    _orders = string.IsNullOrWhiteSpace(json)
                        ? new List<ReignCampaignOrderRecord>()
                        : JsonConvert.DeserializeObject<List<ReignCampaignOrderRecord>>(json)
                            ?? new List<ReignCampaignOrderRecord>();
                    foreach (ReignCampaignOrderRecord order in _orders.Where(x => x != null))
                        EnsurePlan(order);
                }
                catch (Exception ex)
                {
                    _orders = new List<ReignCampaignOrderRecord>();
                    ReignLog.Warn("Campaign command save state could not be restored: " + ex.Message);
                }
            }
        }

        public static bool ValidateControlAction(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;
            if (action == null)
            {
                reason = "Campaign command action is missing.";
                return false;
            }
            if (ReignBetaSettings.Instance?.CampaignCommandEngineEnabled == false)
            {
                reason = "The Campaign Command Engine is disabled in Reign settings.";
                return false;
            }
            if (action.Type == ReignWorldActionType.RegularIssueCampaignOrder)
            {
                Hero commander = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                MobileParty party = commander?.PartyBelongedTo;
                if (commander == null || commander.IsDead || commander.IsPrisoner)
                {
                    reason = "A living, free NPC commander is required.";
                    return false;
                }
                if (commander == Hero.MainHero)
                {
                    reason = "The human main party cannot be controlled by Campaign Command.";
                    return false;
                }
                if (party != null && (!party.IsActive || party.IsMainParty || party.LeaderHero != commander))
                {
                    reason = "The commander belongs to a party they do not lead and cannot receive an independent order.";
                    return false;
                }
                if (party == null && commander.Clan == null)
                {
                    reason = "A partyless commander must belong to a clan before a lord party can be established.";
                    return false;
                }
                if (!HasAuthorityOrAcceptedRecommendation(action, commander, out reason)) return false;
                return ValidatePlanShape(action, out reason);
            }

            string orderId = ReadString(action.TermsJson, "orderId", string.Empty);
            if (string.IsNullOrWhiteSpace(orderId))
            {
                reason = "terms.orderId is required.";
                return false;
            }
            ReignCampaignOrderRecord existing = Instance?._orders.FirstOrDefault(x =>
                string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (existing == null || existing.IsTerminal)
            {
                reason = "The campaign order is missing or already terminal.";
                return false;
            }
            if (action.Type == ReignWorldActionType.RegularRespondToOrderReport
                && string.IsNullOrWhiteSpace(ReadString(action.TermsJson, "response", string.Empty)))
            {
                reason = "terms.response is required when responding to a report.";
                return false;
            }
            return true;
        }

        public ReignActionResult ExecuteControlAction(ReignWorldActionRecord action)
        {
            if (!ValidateControlAction(action, out string reason))
                return ReignActionResult.ValidationFailed(reason);
            switch (action.Type)
            {
                case ReignWorldActionType.RegularIssueCampaignOrder: return Issue(action);
                case ReignWorldActionType.RegularReviseCampaignOrder: return Revise(action);
                case ReignWorldActionType.RegularRespondToOrderReport: return Respond(action);
                case ReignWorldActionType.RegularCancelCampaignOrder: return Cancel(action);
                default: return ReignActionResult.FailTerminal("Unsupported campaign command control action.",
                    "unsupported_campaign_command", "unsupported_action");
            }
        }

        private ReignActionResult Issue(ReignWorldActionRecord action)
        {
            double now = CurrentDay();
            JObject terms = ParseTerms(action.TermsJson);
            string requestedId = terms.Value<string>("orderId");
            string orderId = string.IsNullOrWhiteSpace(requestedId) ? action.ActionId : requestedId.Trim();
            if (_orders.Any(x => string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase)))
                return ReignActionResult.NoOp("Campaign order " + orderId + " was already accepted.")
                    .WithResultCode("campaign_order_duplicate_suppressed");

            Hero commander = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            List<ReignCampaignOrderStepRecord> steps = BuildSteps(action, terms, commander);
            ReignCampaignOrderStepRecord first = steps[0];
            ReignCampaignOrderRecord order = new ReignCampaignOrderRecord
            {
                OrderId = orderId,
                CorrelationId = string.IsNullOrWhiteSpace(action.NegotiationId) ? action.ActionId : action.NegotiationId,
                IssuerHeroStringId = ReadString(action.TermsJson, "issuerHeroStringId", Hero.MainHero?.StringId ?? string.Empty),
                CommanderHeroStringId = action.ActorHeroStringId,
                TargetHeroStringId = first.TargetHeroStringId,
                TargetPartyStringId = first.TargetPartyStringId,
                TargetSettlementStringId = first.TargetSettlementStringId,
                Objective = first.Objective,
                Region = first.Region,
                AuthorityMode = ResolveAuthorityMode(action),
                TermsJson = first.TermsJson,
                CreatedDay = now,
                AcceptedDay = now,
                StartedDay = now,
                NextReviewDay = now,
                Stage = "preflight",
                Status = "accepted",
                Steps = steps,
                CurrentStepIndex = 0,
                PlanHash = string.IsNullOrWhiteSpace(terms.Value<string>("planHash"))
                    ? ComputePlanHash(steps) : terms.Value<string>("planHash").Trim()
            };
            ActivateCurrentStep(order, now);
            ResolveAndStoreAnchors(order);
            AppendHistory(order, "accepted", "Order accepted through " + order.AuthorityMode + ".");
            _orders.Add(order);
            ReviewOrder(order, true);
            Publish(order, "accepted");
            return ReignActionResult.Progress("Campaign order " + order.OrderId + " was accepted by "
                    + (ReignObjectResolver.FindHero(order.CommanderHeroStringId)?.Name?.ToString() ?? order.CommanderHeroStringId) + ".")
                .WithResultCode("campaign_order_accepted")
                .WithEffect("campaign_order", "order", order.OrderId, order.Objective,
                    "status=" + order.Status + ";authority=" + order.AuthorityMode)
                .WithChangedEntity("party", ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId)?.StringId ?? string.Empty,
                    order.CommanderHeroStringId, "campaign_order_supervised");
        }

        private ReignActionResult Revise(ReignWorldActionRecord action)
        {
            ReignCampaignOrderRecord order = FindOrder(action);
            JObject existing = ParseTerms(order.TermsJson);
            JObject update = ParseTerms(action.TermsJson);
            update.Remove("orderId");
            existing.Merge(update, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            string objective = ReignCampaignCommandCapabilityRegistry.NormalizeObjective(
                existing.Value<string>("objective") ?? order.Objective);
            if (!ReignCampaignCommandCapabilityRegistry.IsPlannerVisible(objective))
                return ReignActionResult.ValidationFailed("The revised objective is unsupported.");
            order.Objective = objective;
            order.TermsJson = existing.ToString(Formatting.None);
            order.TargetSettlementStringId = FirstNonEmpty(existing.Value<string>("targetSettlementStringId"),
                action.TargetSettlementStringId, order.TargetSettlementStringId);
            order.TargetPartyStringId = FirstNonEmpty(existing.Value<string>("targetPartyId"), order.TargetPartyStringId);
            order.Region = ReignCampaignCommandGeography.NormalizeRegion(
                FirstNonEmpty(existing.Value<string>("region"), order.Region));
            double duration = ReadDouble(existing, "durationHours", 0d);
            if (duration > 0d) order.DeadlineDay = CurrentDay() + duration / 24d;
            order.Revision++;
            order.AwaitingGuidance = false;
            order.PendingReportId = string.Empty;
            order.Stage = "preflight";
            order.Status = "active";
            SyncCurrentStepFromOrder(order);
            order.PlanHash = ComputePlanHash(order.Steps);
            ResolveAndStoreAnchors(order);
            AppendHistory(order, "revised", "Revision " + order.Revision + " accepted.");
            ReviewOrder(order, true);
            Publish(order, "revised");
            return ReignActionResult.Progress("Campaign order " + order.OrderId + " was revised and reissued.")
                .WithResultCode("campaign_order_revised");
        }

        private ReignActionResult Respond(ReignWorldActionRecord action)
        {
            ReignCampaignOrderRecord order = FindOrder(action);
            string response = NormalizeResponse(ReadString(action.TermsJson, "response", string.Empty));
            if (response == "cancel") return CancelOrder(order, "Cancelled by the sovereign in response to report.");
            order.AwaitingGuidance = false;
            order.PendingReportId = string.Empty;
            order.PendingReportText = string.Empty;
            order.GuidanceDeadlineDay = 0d;
            order.LlmOutcome = string.Empty;
            order.LlmReason = string.Empty;
            order.LlmJudgmentRequested = false;
            if (response == "withdraw")
            {
                order.Objective = "withdraw";
                order.TargetSettlementStringId = string.Empty;
                order.Stage = "preflight";
            }
            else if (response == "hold")
            {
                order.Objective = "timed_hold";
                order.HoldUntilDay = CurrentDay() + Math.Max(1d,
                    ReadDouble(ParseTerms(action.TermsJson), "durationHours", 12d)) / 24d;
                order.Stage = "preflight";
            }
            else if (response == "adapt")
            {
                order.Stage = "adapting";
            }
            else
            {
                order.Stage = "executing";
            }
            order.Status = "active";
            SyncCurrentStepFromOrder(order);
            AppendHistory(order, "guidance", "Sovereign response: " + response + ".");
            ReviewOrder(order, true);
            Publish(order, "guidance_received");
            return ReignActionResult.Progress("Guidance was delivered for campaign order " + order.OrderId + ".")
                .WithResultCode("campaign_order_guidance_received");
        }

        private ReignActionResult Cancel(ReignWorldActionRecord action)
        {
            return CancelOrder(FindOrder(action), "Cancelled by the issuing authority.");
        }

        private ReignActionResult CancelOrder(ReignCampaignOrderRecord order, string reason)
        {
            MobileParty party = ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId);
            if (party != null && party.IsActive && !party.IsMainParty) party.SetMoveModeHold();
            order.Status = "cancelled";
            order.Stage = "terminal";
            order.LastFailure = reason;
            order.AwaitingGuidance = false;
            order.UrgentReportPending = false;
            AppendHistory(order, "cancelled", reason);
            Publish(order, "cancelled");
            return ReignActionResult.Done("Campaign order " + order.OrderId + " was cancelled safely.")
                .WithResultCode("campaign_order_cancelled");
        }

        private void OnHourlyTick()
        {
            if (ReignBetaSettings.Instance?.CampaignCommandEngineEnabled == false) return;
            double now = CurrentDay();
            foreach (ReignCampaignOrderRecord order in _orders.Where(x => x != null && !x.IsTerminal).ToList())
            {
                if (order.AwaitingGuidance && order.GuidanceDeadlineDay > 0d && now >= order.GuidanceDeadlineDay)
                {
                    ResolveUnansweredReport(order);
                }
                if (!order.IsTerminal && !order.AwaitingGuidance && now + 0.00001d >= order.NextReviewDay)
                {
                    ReviewOrder(order, false);
                }
                DeliverUrgentReport(order);
            }
        }

        private void ReviewOrder(ReignCampaignOrderRecord order, bool force)
        {
            if (order == null || order.IsTerminal || order.AwaitingGuidance) return;
            double now = CurrentDay();
            if (!force && now < order.NextReviewDay) return;
            order.LastReviewDay = now;
            order.NextReviewDay = now + Hour;
            order.AttemptCount++;

            Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
            MobileParty party = commander?.PartyBelongedTo;
            if (commander == null || commander.IsDead || commander.IsPrisoner)
            {
                Fail(order, "commander_unavailable", "The commander is no longer living and free.");
                return;
            }
            EnsurePlan(order);
            if (party == null && order.Objective != "establish_party")
            {
                if (!InsertPartyRecoveryStep(order, commander))
                {
                    Fail(order, "commander_unavailable", "The commander no longer leads a party and is not eligible to establish another.");
                    return;
                }
                party = null;
            }
            if (party != null && (!party.IsActive || party.IsMainParty || party.LeaderHero != commander))
            {
                Fail(order, "commander_unavailable", "The commander no longer leads an eligible NPC party.");
                return;
            }

            if (order.Objective == "establish_party")
            {
                if (ObserveCompletion(order, party, null, null)) return;
                if (!ApplyNativeOrder(order, party, null, null, out string establishmentFailure))
                    RequestGuidance(order, establishmentFailure, false);
                else
                {
                    order.Status = "active";
                    order.Stage = "establishing_party";
                    AppendHistory(order, "review", "Party establishment remains in progress.");
                    Publish(order, "reviewed");
                }
                return;
            }

            if (party.Army != null && party.Army.LeaderParty != party
                && RequiresIndependentMovement(order.Objective)
                && !ReadBool(order.TermsJson, "armyDetachmentAccepted", false))
            {
                RequestGuidance(order, "The party is subordinate in " + party.Army.Name
                    + ". Explicit commander agreement is required before detaching it from the native army hierarchy.", false);
                return;
            }

            ResolveAndStoreAnchors(order);
            Settlement target = ResolveTargetSettlement(order);
            MobileParty targetParty = ResolveTargetParty(order);
            ReignCampaignCommandDecision decision = ReignCampaignCommandJudgment.Evaluate(
                BuildDecisionInput(order, commander, party, target, targetParty));
            order.LastDecision = decision.Outcome;
            if (decision.RequiresReport && (decision.Outcome == "request_guidance" || decision.Outcome == "refuse"))
            {
                if (decision.Outcome == "refuse" && !IsSovereignOrder(order))
                {
                    order.Status = "refused";
                    order.Stage = "terminal";
                    order.LastFailure = decision.Reason;
                    AppendHistory(order, "refused", decision.Reason);
                    QueueReport(order, decision.Reason, true);
                    Publish(order, "refused");
                    return;
                }
                RequestGuidance(order, decision.Reason, decision.Exceptional);
                return;
            }
            if (decision.Outcome == "withdraw")
            {
                order.Objective = "withdraw";
                order.TargetSettlementStringId = string.Empty;
                order.Stage = "adapting";
            }

            if (ObserveCompletion(order, party, target, targetParty)) return;
            string before = NativeSignature(party);
            if (!ApplyNativeOrder(order, party, target, targetParty, out string failure))
            {
                if (failure.StartsWith("obsolete:", StringComparison.OrdinalIgnoreCase))
                    Fail(order, "obsolete_target", failure.Substring("obsolete:".Length));
                else
                    RequestGuidance(order, failure, false);
                return;
            }
            string after = NativeSignature(party);
            if (!string.IsNullOrWhiteSpace(order.LastNativeSignature)
                && before != order.LastNativeSignature && after == order.LastNativeSignature)
                order.NativeReassertions++;
            order.LastNativeSignature = after;
            order.Status = "active";
            order.Stage = "executing";
            AppendHistory(order, "review", "Hourly review continued " + order.Objective + ".");
            Publish(order, "reviewed");
        }

        private bool ObserveCompletion(ReignCampaignOrderRecord order, MobileParty party,
            Settlement target, MobileParty targetParty)
        {
            double now = CurrentDay();
            if (order.DeadlineDay > 0d && now >= order.DeadlineDay
                && order.Objective != "besiege_capture" && order.Objective != "relieve_siege")
            {
                Complete(order, "The ordered duration elapsed.");
                return true;
            }
            switch (order.Objective)
            {
                case "establish_party":
                    if (party != null && party.IsActive && !party.IsMainParty
                        && party.LeaderHero?.StringId == order.CommanderHeroStringId)
                    { Complete(order, "The commander now leads an eligible lord party."); return true; }
                    break;
                case "hold_position":
                    break;
                case "timed_hold":
                    if (order.HoldUntilDay > 0d && now >= order.HoldUntilDay)
                    { Complete(order, "The timed hold completed."); return true; }
                    break;
                case "move":
                case "withdraw":
                case "return_home":
                    if (target != null && (party.CurrentSettlement == target
                        || party.GetPosition2D.DistanceSquared(target.GetPosition2D) <= 2.25f))
                    { Complete(order, "The party reached " + target.Name + "."); return true; }
                    break;
                case "form_army":
                    if (party.Army != null && party.Army.LeaderParty == party)
                    {
                        order.Stage = party.Army.IsWaitingForArmyMembers() ? "assembling_army" : "army_ready";
                        if (!party.Army.IsWaitingForArmyMembers())
                        { Complete(order, "The native army finished gathering and is ready for its next objective."); return true; }
                    }
                    break;
                case "join_army":
                    if (party.Army != null && targetParty?.Army == party.Army)
                    { Complete(order, "The party joined the native army."); return true; }
                    break;
                case "leave_army":
                    if (party.Army == null)
                    { Complete(order, "The party left the native army."); return true; }
                    break;
                case "disband_army":
                    if (party.Army == null)
                    { Complete(order, "The native army was disbanded."); return true; }
                    break;
                case "besiege_capture":
                    if (target != null && target.MapFaction == party.MapFaction)
                    { Complete(order, target.Name + " is now friendly to the commander's faction."); return true; }
                    break;
                case "relieve_siege":
                    if (target != null && !target.IsUnderSiege)
                    { Complete(order, "The siege of " + target.Name + " ended."); return true; }
                    break;
                case "engage_party":
                    if (targetParty == null || !targetParty.IsActive)
                    { Complete(order, "The enemy party is no longer an active target."); return true; }
                    break;
                case "recruit_resupply":
                    JObject terms = ParseTerms(order.TermsJson);
                    int troops = ResolveRecruitmentTarget(order, party, terms);
                    double food = ReadDouble(terms, "minimumFoodDays", 3d);
                    CountTroopRoles(party, out int regulars, out int infantry, out int archers, out int cavalry);
                    int requiredInfantry = (int)ReadDouble(terms, "minimumInfantry", 0d);
                    int requiredArchers = (int)ReadDouble(terms, "minimumArchers", 0d);
                    int requiredCavalry = (int)ReadDouble(terms, "minimumCavalry", 0d);
                    if (regulars >= troops && infantry >= requiredInfantry && archers >= requiredArchers
                        && cavalry >= requiredCavalry && party.GetNumDaysForFoodToLast() >= food)
                    { Complete(order, "Recruitment and resupply thresholds were met."); return true; }
                    break;
            }
            return false;
        }

        private bool ApplyNativeOrder(ReignCampaignOrderRecord order, MobileParty party,
            Settlement target, MobileParty targetParty, out string failure)
        {
            failure = string.Empty;
            switch (order.Objective)
            {
                case "establish_party":
                    Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
                    if (commander == null || commander.IsDead || commander.IsPrisoner || commander == Hero.MainHero)
                    { failure = "The named commander is not eligible to establish a party."; return false; }
                    if (commander.PartyBelongedTo != null) return commander.PartyBelongedTo.LeaderHero == commander;
                    Settlement spawn = ResolvePartySpawn(commander, order);
                    if (spawn == null) { failure = "No safe friendly settlement is available for party establishment."; return false; }
                    MobileParty created = LordPartyComponent.CreateLordParty(
                        commander.StringId + "_reign", commander, spawn.GatePosition, 2f, spawn, commander);
                    created.SetMoveModeHold();
                    return created.IsActive && created.LeaderHero == commander;
                case "hold_position":
                    if (!TryReadExactPoint(order, party, out CampaignVec2 holdPoint))
                    { failure = "The exact hold position is missing or outside the campaign map."; return false; }
                    if (party.Position.DistanceSquared(holdPoint) > 4f)
                        party.SetMoveGoToPoint(holdPoint, party.NavigationCapability);
                    else
                        party.SetMoveModeHold();
                    return true;
                case "move":
                case "withdraw":
                case "return_home":
                case "recruit_resupply":
                    if (!ValidateRecruitmentCapacity(order, party, out failure)) return false;
                    if (target == null) { failure = "No safe, navigable settlement anchor is available."; return false; }
                    SetPartyAiAction.GetActionForVisitingSettlement(party, target, party.NavigationCapability,
                        party.IsCurrentlyAtSea, target.HasPort);
                    return true;
                case "timed_hold":
                    party.SetMoveModeHold();
                    return true;
                case "patrol":
                case "scout_report":
                    if (order.Objective == "patrol" && TryReadExactPoint(order, party, out CampaignVec2 patrolCenter))
                    {
                        CampaignVec2 waypoint = PatrolWaypoint(patrolCenter, order.AnchorIndex++);
                        party.SetMoveGoToPoint(waypoint, party.NavigationCapability);
                        return true;
                    }
                    if (target == null) { failure = "No concrete patrol anchor could be resolved."; return false; }
                    SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target,
                        party.NavigationCapability, party.IsCurrentlyAtSea, target.HasPort);
                    AdvanceAnchor(order);
                    return true;
                case "escort":
                    if (!ValidOtherParty(party, targetParty)) { failure = "obsolete:The escort target is no longer active."; return false; }
                    SetPartyAiAction.GetActionForEscortingParty(party, targetParty, party.NavigationCapability,
                        party.IsCurrentlyAtSea, targetParty.IsCurrentlyAtSea);
                    return true;
                case "form_army":
                    if (target == null) { failure = "A concrete settlement objective is required to form an army."; return false; }
                    Kingdom kingdom = party.MapFaction as Kingdom;
                    if (kingdom == null) { failure = "The commander does not belong to a kingdom."; return false; }
                    if (party.Army == null)
                    {
                        bool eligible = TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(
                            party, out MBList<MobileParty> members);
                        if (!eligible)
                        {
                            failure = party.LeaderHero?.Clan != null && party.LeaderHero.Clan.Influence <= 100f
                                ? "The commander's clan has " + party.LeaderHero.Clan.Influence.ToString("0.0", CultureInfo.InvariantCulture)
                                    + " influence; native army creation requires more than 100 influence before variable member costs are charged."
                                : "Native army eligibility currently fails because of influence, party readiness, food, war, location, or eligible-member constraints.";
                            return false;
                        }
                        kingdom.CreateArmy(party.LeaderHero, target, Army.ArmyTypes.Besieger, members);
                    }
                    if (party.Army == null || party.Army.LeaderParty != party)
                    { failure = "Native army creation did not produce a leader army."; return false; }
                    order.Stage = party.Army.IsWaitingForArmyMembers() ? "assembling_army" : "army_ready";
                    return true;
                case "join_army":
                    if (!ValidOtherParty(party, targetParty) || targetParty.Army == null
                        || targetParty.Army.LeaderParty != targetParty)
                    { failure = "obsolete:The named army leader is unavailable."; return false; }
                    party.Army = targetParty.Army;
                    SetPartyAiAction.GetActionForEscortingParty(party, targetParty, party.NavigationCapability,
                        party.IsCurrentlyAtSea, targetParty.IsCurrentlyAtSea);
                    return true;
                case "leave_army":
                    if (!ReadBool(order.TermsJson, "armyDetachmentAccepted", false))
                    { failure = "Explicit commander agreement is required before detaching from an army."; return false; }
                    party.Army = null;
                    party.SetMoveModeHold();
                    return true;
                case "disband_army":
                    if (party.Army == null || party.Army.LeaderParty != party)
                    { failure = "obsolete:The commander no longer leads an army."; return false; }
                    DisbandArmyAction.ApplyByUnknownReason(party.Army);
                    return true;
                case "raid":
                    if (target == null || !target.IsVillage) { failure = "obsolete:The raid target is not a village."; return false; }
                    if (!IsHostile(party, target.MapFaction)) { failure = "obsolete:The village is no longer hostile."; return false; }
                    SetPartyAiAction.GetActionForRaidingSettlement(party, target, party.NavigationCapability,
                        party.IsCurrentlyAtSea, false);
                    return true;
                case "besiege_capture":
                    if (target == null || !target.IsFortification) { failure = "obsolete:The siege target is not a fortification."; return false; }
                    if (!IsHostile(party, target.MapFaction)) { failure = "obsolete:The settlement is no longer hostile."; return false; }
                    SetPartyAiAction.GetActionForBesiegingSettlement(party, target,
                        party.NavigationCapability, party.IsCurrentlyAtSea);
                    return true;
                case "defend":
                    if (target == null) { failure = "No friendly defense anchor is available."; return false; }
                    MobileParty besieger = target.SiegeEvent?.BesiegerCamp?.LeaderParty;
                    if (ValidOtherParty(party, besieger) && IsHostile(party, besieger.MapFaction))
                        SetPartyAiAction.GetActionForEngagingParty(party, besieger,
                            party.NavigationCapability, party.IsCurrentlyAtSea);
                    else
                        SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target,
                            party.NavigationCapability, party.IsCurrentlyAtSea, target.HasPort);
                    return true;
                case "relieve_siege":
                    if (target == null || !target.IsFortification) { failure = "obsolete:The relief target is unavailable."; return false; }
                    MobileParty siegeLeader = target.SiegeEvent?.BesiegerCamp?.LeaderParty;
                    if (!ValidOtherParty(party, siegeLeader)) { failure = "obsolete:The siege has ended or has no active besieger."; return false; }
                    SetPartyAiAction.GetActionForEngagingParty(party, siegeLeader,
                        party.NavigationCapability, party.IsCurrentlyAtSea);
                    return true;
                case "hunt_enemy_parties":
                    MobileParty enemy = FindEnemyParty(party, order);
                    if (enemy != null)
                        SetPartyAiAction.GetActionForEngagingParty(party, enemy,
                            party.NavigationCapability, party.IsCurrentlyAtSea);
                    else if (target != null)
                        SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target,
                            party.NavigationCapability, party.IsCurrentlyAtSea, target.HasPort);
                    else
                    { failure = "No hostile party or concrete regional anchor is currently available."; return false; }
                    return true;
                case "engage_party":
                    if (!ValidOtherParty(party, targetParty)) { failure = "obsolete:The enemy party is no longer active."; return false; }
                    if (!IsHostile(party, targetParty.MapFaction)) { failure = "obsolete:The party is no longer hostile."; return false; }
                    SetPartyAiAction.GetActionForEngagingParty(party, targetParty,
                        party.NavigationCapability, party.IsCurrentlyAtSea);
                    return true;
                default:
                    failure = "No native adapter is registered for " + order.Objective + ".";
                    return false;
            }
        }

        private void RequestGuidance(ReignCampaignOrderRecord order, string reason, bool exceptional)
        {
            if (order.AwaitingGuidance) return;
            order.AwaitingGuidance = true;
            order.Status = "awaiting_guidance";
            order.Stage = "reporting";
            order.GuidanceDeadlineDay = CurrentDay() + GuidanceWindow;
            QueueReport(order, reason, true);
            AppendHistory(order, "guidance_requested", reason);
            if (exceptional && !order.LlmJudgmentRequested)
            {
                order.LlmJudgmentRequested = true;
                _ = ReignServerClient.RequestCampaignOrderJudgmentAsync(order, BuildJudgmentPayload(order));
            }
            Publish(order, "guidance_required");
        }

        private void QueueReport(ReignCampaignOrderRecord order, string reason, bool urgent)
        {
            order.PendingReportId = "order_report_" + Guid.NewGuid().ToString("N");
            Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
            order.PendingReportText = (commander?.Name?.ToString() ?? "Your commander") + " reports on order "
                + order.OrderId + ":\n\n" + reason + "\n\nThe order is paused for guidance. If no answer arrives within 12 campaign hours, the commander will decide according to duty, loyalty, and conditions.";
            order.UrgentReportPending = urgent;
            order.LastReportDay = CurrentDay();
        }

        private void DeliverUrgentReport(ReignCampaignOrderRecord order)
        {
            if (order == null || !order.UrgentReportPending || string.IsNullOrWhiteSpace(order.PendingReportText)) return;
            order.UrgentReportPending = false;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign != null && !campaign.TimeControlModeLock)
                campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            _ = ReignServerClient.SendCampaignOrderReportLetterAsync(order);
            InformationManager.ShowInquiry(new InquiryData(
                "Urgent Campaign Report",
                order.PendingReportText + "\n\nReport ID: " + order.PendingReportId,
                true, false, "Acknowledge", string.Empty, () => { }, null));
            AppendHistory(order, "report_delivered", order.PendingReportId);
            Publish(order, "report_delivered");
        }

        private void ResolveUnansweredReport(ReignCampaignOrderRecord order)
        {
            string outcome = ReignCampaignCommandJudgment.IsAllowedOutcome(order.LlmOutcome)
                ? order.LlmOutcome : order.LastDecision;
            if (outcome == "request_guidance" || outcome == "refuse")
            {
                ReignCampaignCommandDecisionInput input = BuildDecisionInput(order,
                    ReignObjectResolver.FindHero(order.CommanderHeroStringId),
                    ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId),
                    ResolveTargetSettlement(order), ResolveTargetParty(order));
                outcome = input.CivilianRelief && input.TargetUrgent ? "continue"
                    : input.SovereignOrder ? "adapt" : "withdraw";
            }
            order.AwaitingGuidance = false;
            order.PendingReportId = string.Empty;
            order.GuidanceDeadlineDay = 0d;
            if (outcome == "withdraw")
            {
                order.Objective = "withdraw";
                order.TargetSettlementStringId = string.Empty;
                order.Stage = "adapting";
            }
            else if (outcome == "adapt" || outcome == "deviate")
            {
                order.Stage = "adapting";
                if (outcome == "deviate") ApplyBoundedDeviation(order);
            }
            else
            {
                order.Stage = "executing";
            }
            order.Status = "active";
            AppendHistory(order, "guidance_timeout", "No response in 12 hours; commander chose " + outcome + ".");
            Publish(order, "guidance_timeout_decision");
            ReviewOrder(order, true);
        }

        private void ApplyBoundedDeviation(ReignCampaignOrderRecord order)
        {
            if (order == null) return;
            string prior = order.Objective;
            if (prior == "raid" || prior == "besiege_capture" || prior == "engage_party"
                || prior == "hunt_enemy_parties")
            {
                order.Objective = "hunt_enemy_parties";
            }
            else
            {
                order.Objective = "defend";
            }
            order.TargetPartyStringId = string.Empty;
            order.TargetHeroStringId = string.Empty;
            order.TargetSettlementStringId = string.Empty;
            if (string.IsNullOrWhiteSpace(order.Region)) order.Region = "frontier";
            order.DeadlineDay = CurrentDay() + 12d / 24d;
            order.DeviationCount++;
            ResolveAndStoreAnchors(order);
            AppendHistory(order, "deviated", prior + " -> " + order.Objective
                + " within known geography after unanswered guidance.");
        }

        internal void ApplyLlmJudgment(string orderId, string outcome, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null || order.IsTerminal || !ReignCampaignCommandJudgment.IsAllowedOutcome(outcome)) return;
            order.LlmOutcome = outcome.ToLowerInvariant();
            order.LlmReason = reason ?? string.Empty;
            AppendHistory(order, "llm_advisory", order.LlmOutcome + ": " + order.LlmReason);
        }

        internal bool RequestGuidanceForCertification(string orderId, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            RequestGuidance(order, reason ?? "Certification guidance fixture.", false);
            return order.AwaitingGuidance && order.GuidanceDeadlineDay > CurrentDay();
        }

        internal bool ExpireGuidanceForCertification(string orderId)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null || !order.AwaitingGuidance) return false;
            order.GuidanceDeadlineDay = CurrentDay();
            ResolveUnansweredReport(order);
            return !order.AwaitingGuidance && !order.IsTerminal;
        }

        internal bool ReviewForCertification(string orderId)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            ReviewOrder(order, true);
            return !order.IsTerminal;
        }

        internal bool CompleteCurrentStepForCertification(string orderId, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            int before = order.CurrentStepIndex;
            Complete(order, string.IsNullOrWhiteSpace(reason) ? "Certification step transition." : reason);
            return order.IsTerminal || order.CurrentStepIndex == before + 1;
        }

        internal bool VerifySaveRoundTripForCertification()
        {
            try
            {
                string serialized = JsonConvert.SerializeObject(_orders);
                List<ReignCampaignOrderRecord> restored =
                    JsonConvert.DeserializeObject<List<ReignCampaignOrderRecord>>(serialized);
                return restored != null && restored.Count == _orders.Count
                    && restored.Select(x => x.OrderId).SequenceEqual(_orders.Select(x => x.OrderId))
                    && restored.Select(x => x.HistoryJson).SequenceEqual(_orders.Select(x => x.HistoryJson))
                    && restored.Select(x => x.PlanHash).SequenceEqual(_orders.Select(x => x.PlanHash))
                    && restored.Select(x => x.CurrentStepIndex).SequenceEqual(_orders.Select(x => x.CurrentStepIndex))
                    && restored.Select(x => x.Steps?.Count ?? 0).SequenceEqual(_orders.Select(x => x.Steps?.Count ?? 0));
            }
            catch { return false; }
        }

        private void Complete(ReignCampaignOrderRecord order, string reason)
        {
            EnsurePlan(order);
            ReignCampaignOrderStepRecord step = CurrentStep(order);
            if (step != null)
            {
                SyncCurrentStepFromOrder(order);
                step.Status = "completed";
                step.Stage = "terminal";
                step.CompletedDay = CurrentDay();
                order.CompletedStepCount = Math.Max(order.CompletedStepCount, order.CurrentStepIndex + 1);
                AppendHistory(order, "step_completed", "Step " + (order.CurrentStepIndex + 1) + "/"
                    + order.Steps.Count + " (" + step.Objective + "): " + reason);
                if (order.CurrentStepIndex + 1 < order.Steps.Count)
                {
                    order.CurrentStepIndex++;
                    ActivateCurrentStep(order, CurrentDay());
                    order.Status = "active";
                    order.Stage = "preflight";
                    order.NextReviewDay = CurrentDay();
                    order.LastNativeSignature = string.Empty;
                    order.AnchorSettlementIdsCsv = string.Empty;
                    order.AnchorIndex = 0;
                    Publish(order, "step_completed");
                    return;
                }
            }
            order.Status = "completed";
            order.Stage = "terminal";
            order.AwaitingGuidance = false;
            AppendHistory(order, "completed", reason);
            QueueReport(order, reason, false);
            Publish(order, "completed");
        }

        private void Fail(ReignCampaignOrderRecord order, string code, string reason)
        {
            ReignCampaignOrderStepRecord step = CurrentStep(order);
            if (step != null)
            {
                step.Status = "failed";
                step.Stage = "terminal";
                step.LastFailure = code + ":" + reason;
            }
            order.Status = "failed";
            order.Stage = "terminal";
            order.LastFailure = code + ":" + reason;
            order.AwaitingGuidance = false;
            QueueReport(order, reason, true);
            AppendHistory(order, "failed", order.LastFailure);
            Publish(order, "failed");
        }

        private void OnMapEventEnded(MapEvent mapEvent) => ReviewAllRelevant("map_event_ended");
        private void OnSiegeEventStarted(SiegeEvent siege) => ReviewAllRelevant("siege_started");
        private void OnSiegeEventEnded(SiegeEvent siege) => ReviewAllRelevant("siege_ended");
        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
            => ReviewAllRelevant("settlement_owner_changed");

        private void ReviewAllRelevant(string nativeEvent)
        {
            foreach (ReignCampaignOrderRecord order in _orders.Where(x => x != null && !x.IsTerminal
                && !x.AwaitingGuidance).ToList())
            {
                AppendHistory(order, "native_event", nativeEvent);
                ReviewOrder(order, true);
            }
        }

        private static ReignCampaignCommandDecisionInput BuildDecisionInput(ReignCampaignOrderRecord order,
            Hero commander, MobileParty party, Settlement target, MobileParty targetParty)
        {
            float own = PartyStrength(party);
            float enemy = NearbyEnemyStrength(party);
            if (targetParty != null) enemy = Math.Max(enemy, PartyStrength(targetParty));
            MobileParty siegeLeader = target?.SiegeEvent?.BesiegerCamp?.LeaderParty;
            if (siegeLeader != null) enemy = Math.Max(enemy, PartyStrength(siegeLeader));
            return new ReignCampaignCommandDecisionInput
            {
                OwnStrength = own,
                EnemyStrength = enemy,
                FoodDays = party?.GetNumDaysForFoodToLast() ?? 0f,
                Morale = party?.Morale ?? 0f,
                CasualtyRatio = party == null ? 1f : Math.Max(0f, 1f - party.PartySizeRatio),
                TravelDays = target == null || party == null ? 0f
                    : (float)Math.Sqrt(party.GetPosition2D.DistanceSquared(target.GetPosition2D)) / 96f,
                Mercy = commander?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                Valor = commander?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                Honor = commander?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                Calculating = commander?.GetTraitLevel(DefaultTraits.Calculating) ?? 0,
                Leadership = commander?.GetSkillValue(DefaultSkills.Leadership) ?? 0,
                Tactics = commander?.GetSkillValue(DefaultSkills.Tactics) ?? 0,
                RelationToSovereign = commander == null || Hero.MainHero == null ? 0 : commander.GetRelation(Hero.MainHero),
                SovereignOrder = IsSovereignOrder(order),
                CivilianRelief = order.Objective == "relieve_siege" || order.Objective == "defend",
                Conquest = order.Objective == "besiege_capture" || order.Objective == "raid",
                TargetUrgent = target?.IsUnderSiege == true,
                TargetValid = TargetValidForDecision(order, party, target, targetParty)
            };
        }

        private static JObject BuildJudgmentPayload(ReignCampaignOrderRecord order)
        {
            Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
            MobileParty party = commander?.PartyBelongedTo;
            Settlement target = ResolveTargetSettlement(order);
            MobileParty targetParty = ResolveTargetParty(order);
            ReignCampaignCommandDecisionInput input = BuildDecisionInput(order, commander, party, target, targetParty);
            return JObject.FromObject(new
            {
                orderId = order.OrderId, order.Objective, order.AuthorityMode,
                commanderHeroStringId = order.CommanderHeroStringId,
                commanderName = commander?.Name?.ToString() ?? string.Empty,
                targetSettlementStringId = order.TargetSettlementStringId,
                region = order.Region,
                state = input,
                allowedOutcomes = new[] { "continue", "adapt", "request_guidance", "withdraw", "refuse", "deviate" }
            });
        }

        private static bool TargetValidForDecision(ReignCampaignOrderRecord order, MobileParty party,
            Settlement target, MobileParty targetParty)
        {
            ReignCampaignCommandCapability capability = ReignCampaignCommandCapabilityRegistry.Find(order.Objective);
            if (capability == null) return false;
            if (capability.RequiresSettlement && target == null) return false;
            if (capability.RequiresParty && targetParty == null) return false;
            if (capability.RequiresWar && target != null && !IsHostile(party, target.MapFaction)) return false;
            if (capability.RequiresWar && targetParty != null && !IsHostile(party, targetParty.MapFaction)) return false;
            return true;
        }

        private static bool ValidateObjectiveShape(ReignWorldActionRecord action, string objective, out string reason)
        {
            reason = string.Empty;
            ReignCampaignCommandCapability capability = ReignCampaignCommandCapabilityRegistry.Find(objective);
            JObject terms = ParseTerms(action.TermsJson);
            bool hasSettlement = !string.IsNullOrWhiteSpace(action.TargetSettlementStringId)
                || !string.IsNullOrWhiteSpace(terms.Value<string>("targetSettlementStringId"));
            bool hasRegion = !string.IsNullOrWhiteSpace(terms.Value<string>("region"));
            bool hasParty = !string.IsNullOrWhiteSpace(action.TargetHeroStringId)
                || !string.IsNullOrWhiteSpace(terms.Value<string>("targetPartyId"));
            bool hasExactPoint = HasExactPoint(terms);
            if (capability.RequiresSettlement && !hasSettlement && !capability.SupportsRegion)
            { reason = objective + " requires a settlement target."; return false; }
            if (capability.RequiresSettlement && !hasSettlement && capability.SupportsRegion && !hasRegion)
            { reason = objective + " requires a settlement or region."; return false; }
            if (capability.RequiresParty && !hasParty)
            { reason = objective + " requires a target party or party leader."; return false; }
            if (objective == "timed_hold" && ReadDouble(terms, "durationHours", 0d) <= 0d)
            { reason = "timed_hold requires a positive durationHours."; return false; }
            if (objective == "hold_position" && !hasExactPoint)
            { reason = "hold_position requires finite positionX and positionY values inside the NavalDLC Main_map bounds."; return false; }
            if (objective == "patrol" && !hasSettlement && !hasRegion && !hasExactPoint)
            { reason = "patrol requires a settlement, region, or exact map position."; return false; }
            if ((objective == "leave_army") && !ReadBool(action.TermsJson, "armyDetachmentAccepted", false))
            { reason = "leave_army requires explicit armyDetachmentAccepted=true."; return false; }
            return true;
        }

        private static bool HasAuthorityOrAcceptedRecommendation(ReignWorldActionRecord action,
            Hero commander, out string reason)
        {
            reason = string.Empty;
            if (HasDirectAuthority(commander)) return true;
            bool explicitlyAccepted = !action.RequiresAcceptance
                && string.Equals(action.AuthorizationMode, "accepted_recommendation",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(action.AcceptedByHeroStringId,
                    commander.StringId, StringComparison.OrdinalIgnoreCase);
            if (explicitlyAccepted) return true;
            reason = "The player lacks direct clan/kingdom authority and the current commander did not explicitly accept the recommendation.";
            return false;
        }

        private static bool HasDirectAuthority(Hero commander)
        {
            if (commander == null || Hero.MainHero == null || Clan.PlayerClan == null) return false;
            if (Clan.PlayerClan.Leader == Hero.MainHero && commander.Clan == Clan.PlayerClan) return true;
            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            return playerKingdom != null && playerKingdom.Leader == Hero.MainHero
                && commander.Clan?.Kingdom == playerKingdom;
        }

        private static string ResolveAuthorityMode(ReignWorldActionRecord action)
        {
            Hero commander = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            return HasDirectAuthority(commander) ? "direct_authority" : "accepted_recommendation";
        }

        private static bool IsSovereignOrder(ReignCampaignOrderRecord order)
        {
            return string.Equals(order?.AuthorityMode, "direct_authority", StringComparison.OrdinalIgnoreCase)
                && string.Equals(order?.IssuerHeroStringId, Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresIndependentMovement(string objective)
        {
            return objective != "join_army" && objective != "leave_army" && objective != "disband_army"
                && objective != "timed_hold";
        }

        private void ResolveAndStoreAnchors(ReignCampaignOrderRecord order)
        {
            MobileParty party = ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId);
            Settlement explicitTarget = ReignObjectResolver.FindSettlement(order.TargetSettlementStringId);
            IReadOnlyList<Settlement> anchors = ReignCampaignCommandGeography.ResolveAnchors(
                party, order.Region, explicitTarget);
            order.AnchorSettlementIdsCsv = string.Join(",", anchors.Where(x => x != null).Select(x => x.StringId));
            if (order.AnchorIndex >= anchors.Count) order.AnchorIndex = 0;
        }

        private static Settlement ResolveTargetSettlement(ReignCampaignOrderRecord order)
        {
            Settlement explicitTarget = ReignObjectResolver.FindSettlement(order.TargetSettlementStringId);
            if (order.Objective == "return_home")
            {
                Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
                explicitTarget = explicitTarget ?? commander?.HomeSettlement ?? commander?.Clan?.InitialHomeSettlement;
            }
            if (order.Objective == "withdraw" || order.Objective == "recruit_resupply")
            {
                MobileParty party = ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId);
                Settlement safe = Settlement.All.Where(x => x != null && !x.IsHideout && !x.IsUnderSiege
                    && party?.MapFaction != null && x.MapFaction == party.MapFaction
                    && (x.IsTown || x.IsCastle || x.IsVillage))
                    .OrderBy(x => party == null ? 0f : party.GetPosition2D.DistanceSquared(x.GetPosition2D))
                    .FirstOrDefault();
                explicitTarget = safe ?? explicitTarget;
            }
            if (explicitTarget != null) return explicitTarget;
            string[] ids = (order.AnchorSettlementIdsCsv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length == 0) return null;
            int index = Math.Max(0, Math.Min(order.AnchorIndex, ids.Length - 1));
            return ReignObjectResolver.FindSettlement(ids[index]);
        }

        private static MobileParty ResolveTargetParty(ReignCampaignOrderRecord order)
        {
            return ReignObjectResolver.FindParty(order.TargetPartyStringId)
                ?? ReignObjectResolver.FindHero(order.TargetHeroStringId)?.PartyBelongedTo;
        }

        private static MobileParty FindEnemyParty(MobileParty party, ReignCampaignOrderRecord order)
        {
            if (party?.MapFaction == null) return null;
            HashSet<string> anchorIds = new HashSet<string>((order.AnchorSettlementIdsCsv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            List<Settlement> anchors = anchorIds.Select(ReignObjectResolver.FindSettlement).Where(x => x != null).ToList();
            return MobileParty.All.Where(x => ValidOtherParty(party, x) && IsHostile(party, x.MapFaction)
                    && (anchors.Count == 0 || anchors.Min(a => x.GetPosition2D.DistanceSquared(a.GetPosition2D)) <= 900f))
                .OrderBy(x => party.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
        }

        private static float NearbyEnemyStrength(MobileParty party)
        {
            if (party?.MapFaction == null) return 0f;
            return MobileParty.All.Where(x => ValidOtherParty(party, x) && IsHostile(party, x.MapFaction)
                    && party.GetPosition2D.DistanceSquared(x.GetPosition2D) <= NearbyEnemyRadiusSquared)
                .Sum(PartyStrength);
        }

        private static float PartyStrength(MobileParty party)
        {
            return party == null ? 0f : Math.Max(0, party.MemberRoster.TotalManCount)
                * Math.Max(0.25f, party.Morale / 50f);
        }

        private static bool ValidOtherParty(MobileParty own, MobileParty other)
        {
            return other != null && other != own && other.IsActive && !other.IsMainParty;
        }

        private static bool IsHostile(MobileParty party, IFaction faction)
        {
            return party?.MapFaction != null && faction != null && party.MapFaction.IsAtWarWith(faction);
        }

        private static string NativeSignature(MobileParty party)
        {
            if (party == null) return "missing";
            return party.DefaultBehavior + "|" + (party.TargetSettlement?.StringId ?? string.Empty)
                + "|" + (party.TargetParty?.StringId ?? string.Empty) + "|"
                + (party.Army?.LeaderParty?.StringId ?? string.Empty);
        }

        private static void AdvanceAnchor(ReignCampaignOrderRecord order)
        {
            int count = (order.AnchorSettlementIdsCsv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (count > 1) order.AnchorIndex = (order.AnchorIndex + 1) % count;
        }

        private static void AppendHistory(ReignCampaignOrderRecord order, string kind, string text)
        {
            JArray history;
            try { history = JArray.Parse(string.IsNullOrWhiteSpace(order.HistoryJson) ? "[]" : order.HistoryJson); }
            catch { history = new JArray(); }
            history.Add(new JObject { ["day"] = CurrentDay(), ["kind"] = kind, ["text"] = text ?? string.Empty });
            while (history.Count > 200) history.RemoveAt(0);
            order.HistoryJson = history.ToString(Formatting.None);
        }

        private static void Publish(ReignCampaignOrderRecord order, string eventType)
        {
            _ = ReignServerClient.ReportCampaignOrderStateAsync(order, eventType);
        }

        private ReignCampaignOrderRecord FindOrder(ReignWorldActionRecord action)
        {
            string id = ReadString(action.TermsJson, "orderId", string.Empty);
            return _orders.First(x => string.Equals(x.OrderId, id, StringComparison.OrdinalIgnoreCase));
        }

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
            step.DeadlineDay = duration > 0d ? now + duration / 24d : 0d;
            step.HoldUntilDay = step.Objective == "timed_hold" ? now + Math.Max(1d, duration) / 24d : 0d;
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
