using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public ReignGovernmentMeetingRecord HoldPlayerMeeting(Kingdom kingdom)
        {
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            if (state == null) return null;
            if (state.NextMeetingDay >= 0f && CurrentDay() + 0.01f < state.NextMeetingDay)
            {
                return _meetings.Where(x => Same(x.KingdomStringId, kingdom.StringId))
                    .OrderByDescending(x => x.MeetingDay).FirstOrDefault();
            }
            return RunSeasonalMeeting(kingdom, state, true);
        }

        public async Task<int> GeneratePlayerMeetingSpeakerStatementsAsync(string meetingId)
        {
            int completed = 0;
            while (true)
            {
                SpeakerStatementRequest request = await ReignMainThread.InvokeAsync(() =>
                    ReserveNextSpeakerStatement(meetingId)).ConfigureAwait(false);
                if (request == null) break;
                JObject response;
                try
                {
                    Task<JObject> call = await ReignMainThread.InvokeAsync(() =>
                        ReignServerClient.RequestGovernmentSpeakerStatementAsync(
                            request.Kingdom, request.Party, request.Resolutions)).ConfigureAwait(false);
                    response = await call.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    response = new JObject { ["ok"] = false, ["error"] = ex.Message };
                }
                bool applied = await ReignMainThread.InvokeAsync(() =>
                    ApplySpeakerStatementResponse(request.MeetingId, request.Party.PartyId, response))
                    .ConfigureAwait(false);
                if (applied) completed++;
            }
            return completed;
        }

        public bool AcceptResolution(string resolutionId, int routeIndex, Hero actor, out string result)
        {
            result = string.Empty;
            ImportSeasonalBusiness();
            var hearing = _business.FirstOrDefault(x => x.Kind == "seasonal" && Same(x.TargetId, resolutionId));
            if (hearing != null && !Same(_executingSeasonalBusinessId, hearing.BusinessId))
                return ResolveBusiness(hearing.BusinessId, "route:" + routeIndex, actor, false, out result);
            ReignGovernmentResolutionRecord record = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, resolutionId));
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record?.TemplateId);
            Kingdom kingdom = FindKingdom(record?.KingdomStringId);
            if (record == null || template == null || kingdom == null || !Same(record.Status, "proposed"))
            {
                result = "That government resolution is unavailable.";
                return false;
            }
            if (actor == null || actor != kingdom.Leader)
            {
                result = "Only the current ruler may commit the realm to a resolution.";
                return false;
            }
            if (routeIndex != 0 && routeIndex != 1)
            {
                result = "Choose one of the resolution's two routes.";
                return false;
            }

            ReignGovernmentResolutionRoute route = routeIndex == 0 ? template.FirstRoute : template.SecondRoute;
            if (UsesImmediateGold(route.Action) && actor.Gold < route.Target)
            {
                result = "The ruler needs " + route.Target + " denars for that route.";
                return false;
            }

            record.SelectedRoute = routeIndex;
            record.RouteActionValue = (int)route.Action;
            record.RequiredAmount = route.Target;
            record.BaselineValue = MeasureResolution(record, route.Action);
            record.CurrentValue = record.BaselineValue;
            record.AcceptedDay = CurrentDay();
            record.DueDay = CurrentDay() + route.DeadlineDays;
            record.Status = "active";
            record.Outcome = string.Empty;
            if (route.Action == ReignGovernmentResolutionAction.ChangeConstruction)
            {
                JObject evidence;
                try { evidence = JObject.Parse(string.IsNullOrWhiteSpace(record.EvidenceJson) ? "{}" : record.EvidenceJson); }
                catch { evidence = new JObject(); }
                Building current = Settlement.Find(record.TargetSettlementStringId)?.Town?.CurrentBuilding;
                evidence["acceptedBuildingId"] = current?.BuildingType?.StringId ?? string.Empty;
                record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
            }
            record.Revision++;

            if (UsesImmediateGold(route.Action))
            {
                GiveGoldAction.ApplyBetweenCharacters(actor, null, route.Target, true);
                record.CurrentValue = route.Target;
                CompleteResolution(kingdom, record, template, "Treasury commitment paid in full.");
            }
            result = "Accepted " + template.Title + " through: " + route.Description + ".";
            return true;
        }

        public bool DeclineResolution(string resolutionId, Hero actor, out string result)
        {
            result = string.Empty;
            ImportSeasonalBusiness();
            var hearing = _business.FirstOrDefault(x => x.Kind == "seasonal" && Same(x.TargetId, resolutionId));
            if (hearing != null && !Same(_executingSeasonalBusinessId, hearing.BusinessId))
                return ResolveBusiness(hearing.BusinessId, "reject", actor, false, out result);
            ReignGovernmentResolutionRecord record = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, resolutionId));
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record?.TemplateId);
            Kingdom kingdom = FindKingdom(record?.KingdomStringId);
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            if (record == null || template == null || kingdom == null || state == null || !Same(record.Status, "proposed"))
            {
                result = "That government resolution is unavailable.";
                return false;
            }
            if (actor == null || actor != kingdom.Leader)
            {
                result = "Only the current ruler may refuse the realm's resolution.";
                return false;
            }
            record.Status = "refused";
            record.Outcome = "The ruler refused both offered routes.";
            record.ResolvedDay = CurrentDay();
            record.ConsequenceLevel = state.Level;
            record.Revision++;
            ApplyResolutionFailure(kingdom, state, record, template, true);
            result = "The resolution was refused. Its fixed consequences for the government's current degree of control were applied.";
            return true;
        }

        public bool ContributeGoods(string resolutionId, int amount, out string result)
        {
            result = string.Empty;
            ReignGovernmentResolutionRecord record = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, resolutionId));
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record?.TemplateId);
            Kingdom kingdom = FindKingdom(record?.KingdomStringId);
            if (record == null || template == null || kingdom == null || !Same(record.Status, "active") || amount <= 0)
            {
                result = "That material contribution cannot be made.";
                return false;
            }
            ReignGovernmentResolutionAction action = (ReignGovernmentResolutionAction)record.RouteActionValue;
            if (action != ReignGovernmentResolutionAction.FoodDelivery
                && action != ReignGovernmentResolutionAction.GoodsDelivery
                && action != ReignGovernmentResolutionAction.SupplyArmy)
            {
                result = "The selected route does not accept material contributions.";
                return false;
            }
            ItemRoster roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null)
            {
                result = "The ruler's party inventory is unavailable.";
                return false;
            }

            int remaining = Math.Min(amount, record.RequiredAmount - (int)record.CurrentValue);
            int available = 0;
            foreach (ItemRosterElement element in roster)
            {
                if (element.EquipmentElement.Item == null) continue;
                bool matches = action == ReignGovernmentResolutionAction.FoodDelivery
                    || action == ReignGovernmentResolutionAction.SupplyArmy
                    ? element.EquipmentElement.Item.IsFood
                    : !element.EquipmentElement.Item.IsFood && element.EquipmentElement.Item.IsTradeGood;
                if (matches) available += element.Amount;
            }
            if (available < remaining)
            {
                result = "The party carries only " + available + " eligible goods; " + remaining + " are needed.";
                return false;
            }

            int toRemove = remaining;
            foreach (ItemRosterElement element in roster.ToList())
            {
                ItemObject item = element.EquipmentElement.Item;
                if (item == null) continue;
                bool matches = action == ReignGovernmentResolutionAction.FoodDelivery
                    || action == ReignGovernmentResolutionAction.SupplyArmy
                    ? item.IsFood
                    : !item.IsFood && item.IsTradeGood;
                if (!matches) continue;
                int remove = Math.Min(toRemove, element.Amount);
                roster.AddToCounts(item, -remove);
                toRemove -= remove;
                if (toRemove <= 0) break;
            }
            record.CurrentValue += remaining;
            record.Revision++;
            if (record.CurrentValue >= record.RequiredAmount)
                CompleteResolution(kingdom, record, template, "The required material contribution was delivered.");
            result = "Delivered " + remaining + " goods; " + Math.Max(0, record.RequiredAmount - (int)record.CurrentValue) + " remain.";
            return true;
        }

        public int RecordResolutionProgress(
            string kingdomStringId,
            ReignGovernmentResolutionAction action,
            string targetId,
            int amount,
            string evidence)
        {
            if (amount <= 0) return 0;
            int updated = 0;
            foreach (ReignGovernmentResolutionRecord record in _resolutions.Where(x => Same(x.KingdomStringId, kingdomStringId)
                && Same(x.Status, "active") && x.RouteActionValue == (int)action).ToList())
            {
                if (!string.IsNullOrWhiteSpace(targetId) && !ResolutionTargets(record, targetId)) continue;
                record.CurrentValue += amount;
                record.EvidenceJson = MergeEvidence(record.EvidenceJson, evidence);
                record.Revision++;
                updated++;
                if (record.CurrentValue >= record.RequiredAmount)
                {
                    Kingdom kingdom = FindKingdom(record.KingdomStringId);
                    ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record.TemplateId);
                    if (kingdom != null && template != null)
                        CompleteResolution(kingdom, record, template, evidence);
                }
            }
            return updated;
        }

        private ReignGovernmentMeetingRecord RunSeasonalMeeting(
            Kingdom kingdom,
            ReignGovernmentStateRecord state,
            bool playerAttended)
        {
            ReconcileMembership(kingdom, state, false);
            float day = CurrentDay();
            HashSet<string> evidenceTags = CollectEvidenceTags(kingdom);
            var proposed = new List<ReignGovernmentResolutionRecord>();
            HashSet<string> usedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReignGovernmentPartyRecord party in GetParties(kingdom.StringId).Where(x => x.SeatCount > 0))
            {
                List<ReignGovernmentPlank> planks = ParsePlanks(party.PlanksCsv);
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Templates
                    .Where(x => !usedTemplates.Contains(x.Id)
                        && ReignGovernmentResolutionCatalog.IsEligible(x, evidenceTags, planks))
                    .OrderBy(x => ReignGovernmentRules.StableHash("resolution|" + kingdom.StringId + "|" + party.PartyId
                        + "|" + (int)Math.Floor(day / ReignGovernmentRules.SeasonalMeetingDays) + "|" + x.Id))
                    .FirstOrDefault();
                if (template == null) continue;
                usedTemplates.Add(template.Id);
                ReignGovernmentResolutionRecord record = CreateResolution(kingdom, state, party, template, day);
                _resolutions.Add(record);
                proposed.Add(record);
            }

            var meeting = new ReignGovernmentMeetingRecord
            {
                KingdomStringId = kingdom.StringId,
                MeetingDay = day,
                PartyIdsCsv = string.Join(",", GetParties(kingdom.StringId).Where(x => x.SeatCount > 0).Select(x => x.PartyId)),
                SpeakerHeroIdsCsv = string.Join(",", GetParties(kingdom.StringId).Where(x => x.SeatCount > 0).Select(x => x.SpeakerHeroStringId)),
                ResolutionIdsCsv = string.Join(",", proposed.Select(x => x.ResolutionId)),
                ProviderCallCount = 0,
                PlayerAttended = playerAttended,
                Summary = proposed.Count == 0
                    ? state.InstitutionName + " found no evidence-backed demand to advance this season."
                    : state.InstitutionName + " advanced " + proposed.Count + " evidence-backed resolutions through party speakers.",
                Revision = 1
            };
            var statements = new JObject();
            foreach (ReignGovernmentResolutionRecord record in proposed)
            {
                ReignGovernmentPartyRecord party = FindParty(kingdom.StringId, record.PartyId);
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record.TemplateId);
                string fallback = (party?.Name ?? "A party") + " calls for "
                    + (template?.Title ?? "measurable relief") + ".";
                statements[party?.PartyId ?? record.PartyId] = fallback;
                if (party != null)
                {
                    party.LastStatement = fallback;
                    party.LastStatementDay = day;
                    party.Revision++;
                }
            }
            meeting.SpeakerStatementsJson = statements.ToString(Newtonsoft.Json.Formatting.None);
            _meetings.Add(meeting);
            state.LastMeetingDay = day;
            state.NextMeetingDay = day + ReignGovernmentRules.SeasonalMeetingDays;
            state.LastMeetingSummary = meeting.Summary;
            state.Revision++;
            if (!playerAttended && kingdom.Leader != Hero.MainHero)
                ApplyNpcMeetingDisposition(kingdom, state, proposed);
            return meeting;
        }

        private SpeakerStatementRequest ReserveNextSpeakerStatement(string meetingId)
        {
            ReignGovernmentMeetingRecord meeting = _meetings.FirstOrDefault(x => Same(x.MeetingId, meetingId));
            Kingdom kingdom = FindKingdom(meeting?.KingdomStringId);
            if (meeting == null || kingdom == null || !meeting.PlayerAttended || kingdom.Leader != Hero.MainHero)
                return null;
            JObject statements;
            try { statements = JObject.Parse(string.IsNullOrWhiteSpace(meeting.SpeakerStatementsJson) ? "{}" : meeting.SpeakerStatementsJson); }
            catch { statements = new JObject(); }
            JArray attempted = statements["__attemptedPartyIds"] as JArray ?? new JArray();
            HashSet<string> attemptedIds = new HashSet<string>(attempted.Values<string>(),
                StringComparer.OrdinalIgnoreCase);
            List<ReignGovernmentPartyRecord> parties = GetParties(kingdom.StringId)
                .Where(x => x.SeatCount > 0 && !string.IsNullOrWhiteSpace(x.SpeakerHeroStringId)).ToList();
            ReignGovernmentPartyRecord party = parties.FirstOrDefault(x => !attemptedIds.Contains(x.PartyId));
            if (party == null) return null;
            attempted.Add(party.PartyId);
            statements["__attemptedPartyIds"] = attempted;
            meeting.SpeakerStatementsJson = statements.ToString(Newtonsoft.Json.Formatting.None);
            meeting.ProviderCallCount = Math.Min(parties.Count, meeting.ProviderCallCount + 1);
            meeting.Revision++;
            return new SpeakerStatementRequest
            {
                MeetingId = meeting.MeetingId,
                Kingdom = kingdom,
                Party = party,
                Resolutions = _resolutions.Where(x => Same(x.KingdomStringId, kingdom.StringId)
                    && Same(x.PartyId, party.PartyId)
                    && meeting.ResolutionIdsCsv.Split(',').Any(id => Same(id, x.ResolutionId))).ToList()
            };
        }

        private bool ApplySpeakerStatementResponse(string meetingId, string partyId, JObject response)
        {
            ReignGovernmentMeetingRecord meeting = _meetings.FirstOrDefault(x => Same(x.MeetingId, meetingId));
            if (meeting == null || response?.Value<bool?>("ok") != true) return false;
            string reply = response.Value<string>("reply")?.Trim();
            if (string.IsNullOrWhiteSpace(reply)) return false;
            JObject statements;
            try { statements = JObject.Parse(string.IsNullOrWhiteSpace(meeting.SpeakerStatementsJson) ? "{}" : meeting.SpeakerStatementsJson); }
            catch { statements = new JObject(); }
            statements[partyId] = reply;
            meeting.SpeakerStatementsJson = statements.ToString(Newtonsoft.Json.Formatting.None);
            meeting.Revision++;
            ReignGovernmentPartyRecord party = FindParty(meeting.KingdomStringId, partyId);
            if (party != null)
            {
                party.LastStatement = reply;
                party.LastStatementDay = meeting.MeetingDay;
                party.Revision++;
            }
            return true;
        }

        private ReignGovernmentResolutionRecord CreateResolution(
            Kingdom kingdom,
            ReignGovernmentStateRecord state,
            ReignGovernmentPartyRecord party,
            ReignGovernmentResolutionTemplate template,
            float day)
        {
            Settlement target = SelectTargetSettlement(kingdom, template.EvidenceTag);
            Kingdom foreign = SelectTargetKingdom(kingdom);
            Clan clan = SelectTargetClan(kingdom);
            Hero hero = SelectTargetHero(kingdom, template.EvidenceTag, target, clan);
            PolicyObject policy = SelectTargetPolicy(kingdom, template);
            return new ReignGovernmentResolutionRecord
            {
                KingdomStringId = kingdom.StringId,
                TemplateId = template.Id,
                PartyId = party.PartyId,
                SpeakerHeroStringId = party.SpeakerHeroStringId,
                TargetSettlementStringId = target?.StringId ?? string.Empty,
                TargetKingdomStringId = foreign?.StringId ?? string.Empty,
                TargetClanStringId = clan?.StringId ?? string.Empty,
                TargetHeroStringId = hero?.StringId ?? string.Empty,
                TargetPolicyStringId = policy?.StringId ?? string.Empty,
                ProposedDay = day,
                Status = "debate",
                ConsequenceLevel = state.Level,
                EvidenceJson = new JObject
                {
                    ["tag"] = template.EvidenceTag,
                    ["settlementId"] = target?.StringId ?? string.Empty,
                    ["kingdomId"] = foreign?.StringId ?? string.Empty,
                    ["clanId"] = clan?.StringId ?? string.Empty,
                    ["heroId"] = hero?.StringId ?? string.Empty,
                    ["policyId"] = policy?.StringId ?? string.Empty,
                    ["observedDay"] = day
                }.ToString(Newtonsoft.Json.Formatting.None),
                Revision = 1
            };
        }

        private void ApplyNpcMeetingDisposition(Kingdom kingdom, ReignGovernmentStateRecord state,
            IReadOnlyList<ReignGovernmentResolutionRecord> proposed)
        {
            ImportSeasonalBusiness();
            TickGovernmentBusiness();
        }

        private static int SelectNpcRoute(Kingdom kingdom, ReignGovernmentResolutionTemplate template)
        {
            bool firstGold = UsesImmediateGold(template.FirstRoute.Action);
            bool secondGold = UsesImmediateGold(template.SecondRoute.Action);
            int gold = kingdom?.Leader?.Gold ?? 0;
            if (firstGold && gold < template.FirstRoute.Target && !secondGold) return 1;
            if (secondGold && gold < template.SecondRoute.Target && !firstGold) return 0;
            return ReignGovernmentRules.StableHash("npc-route|" + (kingdom?.StringId ?? string.Empty) + "|" + template.Id) % 2u == 0u ? 0 : 1;
        }

        private void EvaluateResolutions(Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            foreach (ReignGovernmentResolutionRecord record in _resolutions.Where(x => Same(x.KingdomStringId, kingdom.StringId)
                && Same(x.Status, "active")).ToList())
            {
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record.TemplateId);
                if (template == null)
                {
                    record.Status = "invalidated";
                    record.Outcome = "The resolution template is unavailable.";
                    record.ResolvedDay = CurrentDay();
                    record.Revision++;
                    continue;
                }
                ReignGovernmentResolutionAction action = (ReignGovernmentResolutionAction)record.RouteActionValue;
#if !REIGN_EXCLUDE_COURT
                ApplySpymasterResolutionProgress(kingdom, record, action);
#endif
                ApplyPassiveResolutionProgress(record, action);
                ApplyDailyResolutionCommitment(kingdom, record, template, action);
                float measured = MeasureResolution(record, action);
                if (UsesWorldMeasurement(action)) record.CurrentValue = measured;
                if (ResolutionSatisfied(record, action))
                {
                    CompleteResolution(kingdom, record, template, "The tracked target was reached and validated.");
                    continue;
                }
                if (record.DueDay >= 0f && CurrentDay() > record.DueDay)
                {
                    record.Status = "failed";
                    record.Outcome = "The measurable target was not completed before its deadline.";
                    record.ResolvedDay = CurrentDay();
                    record.Revision++;
                    ApplyResolutionFailure(kingdom, state, record, template, false);
                }
            }
        }

        private void CompleteResolution(Kingdom kingdom, ReignGovernmentResolutionRecord record,
            ReignGovernmentResolutionTemplate template, string evidence)
        {
            if (!Same(record.Status, "active")) return;
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            if (state == null) return;
            ReignGovernmentResolutionReward reward = ReignGovernmentResolutionCatalog.CompletionReward(template.Scale);
            Settlement settlement = Settlement.Find(record.TargetSettlementStringId);
            if (settlement?.Town != null)
            {
                settlement.Town.Loyalty = MBMath.ClampFloat(settlement.Town.Loyalty + reward.Loyalty, 0f, 100f);
                settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity + reward.Prosperity);
                settlement.Town.Security = MBMath.ClampFloat(settlement.Town.Security + reward.HearthOrSecurity, 0f, 100f);
            }
            else if (settlement?.Village != null)
            {
                settlement.Village.Hearth = MathF.Max(0f, settlement.Village.Hearth + reward.HearthOrSecurity);
            }
            ApplyRelation(kingdom.Leader, FindHero(record.SpeakerHeroStringId), reward.SpeakerRelation);
            state.Revision++;
            record.Status = "completed";
            record.Outcome = evidence ?? "Completed.";
            record.ResolvedDay = CurrentDay();
            record.EvidenceJson = MergeEvidence(record.EvidenceJson, evidence);
            record.Revision++;
        }

        private void ApplyResolutionFailure(Kingdom kingdom, ReignGovernmentStateRecord state,
            ReignGovernmentResolutionRecord record, ReignGovernmentResolutionTemplate template, bool refused)
        {
            ReignGovernmentResolutionReward reward = ReignGovernmentResolutionCatalog.CompletionReward(template.Scale);
            double multiplier = ReignGovernmentRules.ResolutionFailureMultiplier(state.Level);
            int loyalty = -(int)Math.Round(reward.Loyalty * multiplier, MidpointRounding.AwayFromZero);
            int prosperity = -(int)Math.Round(reward.Prosperity * multiplier, MidpointRounding.AwayFromZero);
            int local = -(int)Math.Round(reward.HearthOrSecurity * multiplier, MidpointRounding.AwayFromZero);
            int relation = -(int)Math.Round(reward.SpeakerRelation * multiplier, MidpointRounding.AwayFromZero);
            int trust = -(int)Math.Round(reward.GovernmentTrust * multiplier, MidpointRounding.AwayFromZero);
            Settlement settlement = Settlement.Find(record.TargetSettlementStringId);
            if (settlement?.Town != null)
            {
                settlement.Town.Loyalty = MBMath.ClampFloat(settlement.Town.Loyalty + loyalty, 0f, 100f);
                settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity + prosperity);
                settlement.Town.Security = MBMath.ClampFloat(settlement.Town.Security + local, 0f, 100f);
            }
            else if (settlement?.Village != null)
            {
                settlement.Village.Hearth = MathF.Max(0f, settlement.Village.Hearth + local);
            }
            ApplyRelation(kingdom.Leader, FindHero(record.SpeakerHeroStringId), relation);
            state.Revision++;
            if (refused)
                record.Outcome += " Refusal consequences reflected the government's current degree of control.";
        }

        private static bool ResolutionSatisfied(ReignGovernmentResolutionRecord record, ReignGovernmentResolutionAction action)
        {
            switch (action)
            {
                case ReignGovernmentResolutionAction.LoyaltyTarget:
                case ReignGovernmentResolutionAction.ProsperityTarget:
                case ReignGovernmentResolutionAction.HearthTarget:
                case ReignGovernmentResolutionAction.SecurityTarget:
                case ReignGovernmentResolutionAction.ImproveForeignRelation:
                case ReignGovernmentResolutionAction.ImproveClanRelation:
                case ReignGovernmentResolutionAction.CollectTaxMinimum:
                    return record.CurrentValue - record.BaselineValue >= record.RequiredAmount;
                case ReignGovernmentResolutionAction.GarrisonMinimum:
                case ReignGovernmentResolutionAction.FieldArmyMinimum:
                case ReignGovernmentResolutionAction.CreateMilitia:
                    return record.CurrentValue >= record.RequiredAmount;
                case ReignGovernmentResolutionAction.GarrisonMaximum:
                    return record.CurrentValue <= record.RequiredAmount;
                default:
                    return record.CurrentValue >= record.RequiredAmount;
            }
        }

        private void ApplyDailyResolutionCommitment(Kingdom kingdom,
            ReignGovernmentResolutionRecord record,
            ReignGovernmentResolutionTemplate template,
            ReignGovernmentResolutionAction action)
        {
            if (!UsesDailyCommitment(action)) return;
            int day = (int)Math.Floor(CurrentDay());
            JObject evidence;
            try { evidence = JObject.Parse(string.IsNullOrWhiteSpace(record.EvidenceJson) ? "{}" : record.EvidenceJson); }
            catch { evidence = new JObject(); }
            if (evidence.Value<int?>("lastCommitmentDay") == day) return;
            evidence["lastCommitmentDay"] = day;

            Settlement settlement = Settlement.Find(record.TargetSettlementStringId);
            if (action == ReignGovernmentResolutionAction.EndVillageRaidsDays
                && settlement?.Village?.VillageState == Village.VillageStates.Looted)
            {
                evidence["lastCommitmentResult"] = "blocked_by_looted_village";
                record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
                record.Revision++;
                return;
            }
            if (action == ReignGovernmentResolutionAction.HaltConstructionDays
                && settlement?.Town?.CurrentBuilding != null)
            {
                evidence["lastCommitmentResult"] = "blocked_by_active_construction";
                evidence["activeBuildingId"] = settlement.Town.CurrentBuilding.BuildingType?.StringId ?? string.Empty;
                record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
                record.Revision++;
                return;
            }

            int dailyCost = DailyCommitmentGoldCost(action, template.Scale);
            Hero ruler = kingdom?.Leader;
            if (dailyCost > 0 && (ruler == null || ruler.Gold < dailyCost))
            {
                evidence["lastCommitmentResult"] = "treasury_shortfall";
                evidence["requiredDailyGold"] = dailyCost;
                record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
                record.Revision++;
                return;
            }
            if (dailyCost > 0) GiveGoldAction.ApplyBetweenCharacters(ruler, null, dailyCost, true);

            if (settlement?.Town != null)
            {
                if (action == ReignGovernmentResolutionAction.PatrolDays)
                    settlement.Town.Security = MBMath.ClampFloat(settlement.Town.Security + 0.25f, 0f, 100f);
                else if (action == ReignGovernmentResolutionAction.GuardCaravansDays)
                    settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity + 0.5f);
                else if (action == ReignGovernmentResolutionAction.TaxReliefDays)
                {
                    settlement.Town.Loyalty = MBMath.ClampFloat(settlement.Town.Loyalty + 0.15f, 0f, 100f);
                    settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity + 0.25f);
                }
                else if (action == ReignGovernmentResolutionAction.ReduceTariffsDays)
                    settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity + 0.75f);
                else if (action == ReignGovernmentResolutionAction.WaiveObligationDays)
                    settlement.Town.Loyalty = MBMath.ClampFloat(settlement.Town.Loyalty + 0.1f, 0f, 100f);
            }
            if (settlement?.Village != null && action == ReignGovernmentResolutionAction.WaiveObligationDays)
                settlement.Village.Hearth = MathF.Max(0f, settlement.Village.Hearth + 0.5f);

            record.CurrentValue += 1f;
            evidence["lastCommitmentResult"] = "honored";
            evidence["lastCommitmentCostGold"] = dailyCost;
            evidence["commitmentDaysHonored"] = record.CurrentValue;
            record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
            record.Revision++;
        }

        private static bool UsesDailyCommitment(ReignGovernmentResolutionAction action)
        {
            return action == ReignGovernmentResolutionAction.PatrolDays
                || action == ReignGovernmentResolutionAction.GuardCaravansDays
                || action == ReignGovernmentResolutionAction.EndVillageRaidsDays
                || action == ReignGovernmentResolutionAction.HaltConstructionDays
                || action == ReignGovernmentResolutionAction.TaxReliefDays
                || action == ReignGovernmentResolutionAction.ReduceTariffsDays
                || action == ReignGovernmentResolutionAction.WaiveObligationDays;
        }

        private static void ApplyPassiveResolutionProgress(ReignGovernmentResolutionRecord record,
            ReignGovernmentResolutionAction action)
        {
            if (record == null || action != ReignGovernmentResolutionAction.ChangeConstruction) return;
            Settlement settlement = Settlement.Find(record.TargetSettlementStringId);
            if (settlement?.Town == null) return;
            JObject evidence;
            try { evidence = JObject.Parse(string.IsNullOrWhiteSpace(record.EvidenceJson) ? "{}" : record.EvidenceJson); }
            catch { evidence = new JObject(); }
            string accepted = evidence.Value<string>("acceptedBuildingId") ?? string.Empty;
            string current = settlement.Town.CurrentBuilding?.BuildingType?.StringId ?? string.Empty;
            if (Same(accepted, current) || string.IsNullOrWhiteSpace(current)) return;
            record.CurrentValue = Math.Max(record.CurrentValue, record.RequiredAmount);
            record.EvidenceJson = MergeEvidence(record.EvidenceJson,
                "Native construction priority changed from " + (accepted.Length == 0 ? "none" : accepted)
                + " to " + current + ".");
            record.Revision++;
        }

#if !REIGN_EXCLUDE_COURT
        private static void ApplySpymasterResolutionProgress(Kingdom kingdom,
            ReignGovernmentResolutionRecord record,
            ReignGovernmentResolutionAction action)
        {
            if (kingdom?.Leader != Hero.MainHero
                || (action != ReignGovernmentResolutionAction.SpymasterInvestigation
                    && action != ReignGovernmentResolutionAction.SpymasterCounterintelligence
                    && action != ReignGovernmentResolutionAction.IdentifyCulprit))
                return;
            IEnumerable<ReignSpymasterMission> missions = ReignCourtCampaignBehavior.Instance?.SpymasterState?.Missions
                ?? Enumerable.Empty<ReignSpymasterMission>();
            ReignSpymasterMission completed = missions.Where(x => x != null
                    && x.State == ReignSpymasterMissionState.Succeeded
                    && x.ResolvedDay + 0.01f >= record.AcceptedDay
                    && (string.IsNullOrWhiteSpace(x.SponsorKingdomStringId)
                        || Same(x.SponsorKingdomStringId, kingdom.StringId))
                    && (string.IsNullOrWhiteSpace(record.TargetSettlementStringId)
                        && string.IsNullOrWhiteSpace(record.TargetHeroStringId)
                        || ResolutionTargets(record, x.TargetStringId)))
                .Where(x => action == ReignGovernmentResolutionAction.SpymasterCounterintelligence
                    ? Same(x.MissionType, "counterintelligence")
                    : action == ReignGovernmentResolutionAction.IdentifyCulprit
                        ? Same(x.MissionType, "person_rumors") || Same(x.MissionType, "counterintelligence")
                        : Same(x.MissionType, "land_intelligence")
                            || x.MissionType.StartsWith("person_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ResolvedDay).FirstOrDefault();
            if (completed == null) return;
            record.CurrentValue = Math.Max(record.CurrentValue, record.RequiredAmount);
            record.EvidenceJson = MergeEvidence(record.EvidenceJson,
                "Successful Spymaster mission " + completed.MissionId + ": " + completed.ResultSummary);
            record.Revision++;
        }
#endif

        private static int DailyCommitmentGoldCost(ReignGovernmentResolutionAction action,
            ReignGovernmentResolutionScale scale)
        {
            int weight = scale == ReignGovernmentResolutionScale.Major ? 3
                : scale == ReignGovernmentResolutionScale.Standard ? 2 : 1;
            switch (action)
            {
                case ReignGovernmentResolutionAction.PatrolDays: return 300 * weight;
                case ReignGovernmentResolutionAction.GuardCaravansDays: return 250 * weight;
                case ReignGovernmentResolutionAction.TaxReliefDays: return 500 * weight;
                case ReignGovernmentResolutionAction.ReduceTariffsDays: return 350 * weight;
                case ReignGovernmentResolutionAction.WaiveObligationDays: return 250 * weight;
                default: return 0;
            }
        }

        private float MeasureResolution(ReignGovernmentResolutionRecord record, ReignGovernmentResolutionAction action)
        {
            Settlement settlement = Settlement.Find(record.TargetSettlementStringId);
            switch (action)
            {
                case ReignGovernmentResolutionAction.LoyaltyTarget: return settlement?.Town?.Loyalty ?? record.CurrentValue;
                case ReignGovernmentResolutionAction.ProsperityTarget: return settlement?.Town?.Prosperity ?? record.CurrentValue;
                case ReignGovernmentResolutionAction.HearthTarget: return settlement?.Village?.Hearth ?? record.CurrentValue;
                case ReignGovernmentResolutionAction.SecurityTarget: return settlement?.Town?.Security ?? record.CurrentValue;
                case ReignGovernmentResolutionAction.GarrisonMinimum:
                case ReignGovernmentResolutionAction.GarrisonMaximum: return settlement?.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                case ReignGovernmentResolutionAction.FieldArmyMinimum:
                    return FindKingdom(record.KingdomStringId)?.Armies.Sum(x => x?.LeaderParty?.Army?.Parties.Sum(p => p.MemberRoster.TotalManCount) ?? 0) ?? 0;
                case ReignGovernmentResolutionAction.CreateMilitia: return settlement?.Town?.Militia ?? 0f;
                case ReignGovernmentResolutionAction.ImproveForeignRelation:
                {
                    Kingdom kingdom = FindKingdom(record.KingdomStringId);
                    Kingdom target = FindKingdom(record.TargetKingdomStringId);
                    return kingdom?.Leader == null || target?.Leader == null ? record.CurrentValue : kingdom.Leader.GetRelation(target.Leader);
                }
                case ReignGovernmentResolutionAction.ImproveClanRelation:
                {
                    Kingdom kingdom = FindKingdom(record.KingdomStringId);
                    Clan clan = Clan.FindFirst(x => Same(x.StringId, record.TargetClanStringId));
                    return kingdom?.Leader == null || clan?.Leader == null ? record.CurrentValue : kingdom.Leader.GetRelation(clan.Leader);
                }
                case ReignGovernmentResolutionAction.CollectTaxMinimum: return FindKingdom(record.KingdomStringId)?.Leader?.Gold ?? record.CurrentValue;
                default: return record.CurrentValue;
            }
        }

        private static bool UsesWorldMeasurement(ReignGovernmentResolutionAction action)
        {
            return action == ReignGovernmentResolutionAction.LoyaltyTarget
                || action == ReignGovernmentResolutionAction.ProsperityTarget
                || action == ReignGovernmentResolutionAction.HearthTarget
                || action == ReignGovernmentResolutionAction.SecurityTarget
                || action == ReignGovernmentResolutionAction.GarrisonMinimum
                || action == ReignGovernmentResolutionAction.GarrisonMaximum
                || action == ReignGovernmentResolutionAction.FieldArmyMinimum
                || action == ReignGovernmentResolutionAction.CreateMilitia
                || action == ReignGovernmentResolutionAction.ImproveForeignRelation
                || action == ReignGovernmentResolutionAction.ImproveClanRelation
                || action == ReignGovernmentResolutionAction.CollectTaxMinimum;
        }

        private static bool UsesImmediateGold(ReignGovernmentResolutionAction action)
        {
            return action == ReignGovernmentResolutionAction.TreasuryPayment
                || action == ReignGovernmentResolutionAction.CompensateClan
                || action == ReignGovernmentResolutionAction.HoldFeast;
        }

        private static HashSet<string> CollectEvidenceTags(Kingdom kingdom)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Town> fiefs = kingdom.Fiefs.Where(x => x?.Settlement?.IsFortification == true).ToList();
            List<Village> villages = Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                .Select(x => x.Village).Where(x => x != null).ToList();
            bool atWar = Kingdom.All.Any(x => x != kingdom && !x.IsEliminated && kingdom.GetStanceWith(x).IsAtWar);
            if (fiefs.Any(x => x.Loyalty < 40f)) Add(tags, "low_loyalty", "low_castle_loyalty", "realmwide_low_loyalty", "low_loyalty_tax", "rebellious_state");
            if (fiefs.Any(x => x.Prosperity < 3000f)) Add(tags, "low_prosperity", "realm_stagnation", "trade_growth");
            if (fiefs.Any(x => x.Security < 40f)) Add(tags, "low_security", "criminal_activity", "extortion_history");
            if (fiefs.Any(x => x.FoodChange < 0f || x.FoodStocks < 20f)) Add(tags, "low_food", "crop_failure", "high_food_price", "food_shortage_project");
            if (fiefs.Any(x => (x.GarrisonParty?.MemberRoster?.TotalManCount ?? 0) < 75)) Add(tags, "weak_garrison", "enemy_border", "siege_risk", "border_hardship");
            if (fiefs.Any(x => (x.GarrisonParty?.MemberRoster?.TotalManCount ?? 0) > 300)) Add(tags, "garrison_excess", "peace_garrison_cost");
            if (villages.Any(x => x.VillageState == Village.VillageStates.Looted))
                Add(tags, "village_raided", "repeated_raids", "raid_hearth_loss", "raid_trade_loss", "raid_tax_burden", "captured_defenders", "displaced_households");
            if (villages.Any(x => x.Hearth < 200f)) Add(tags, "low_hearth", "isolated_village", "livestock_loss", "village_food_burden");
            if (atWar)
                Add(tags, "war_exhaustion", "enemy_attack", "stalled_war", "wartime_trade_loss", "war_food_risk", "army_supply_shortage",
                    "wartime_village_risk", "recent_defeat", "siege_risk", "captured_clan_leader", "foreign_prisoners", "battle_casualties");
            else
                Add(tags, "missing_trade_agreement", "postwar_burden", "postwar_levies", "postwar_prisoners", "closed_border");
            if (Kingdom.All.Count(x => x != kingdom && !x.IsEliminated && kingdom.GetStanceWith(x).IsAtWar) > 1) tags.Add("multiple_wars");
            if (kingdom.Leader?.Gold < 60000) Add(tags, "low_treasury_war", "low_treasury_defense", "emergency_levy", "household_debt");
            if (kingdom.Clans.Any(x => x != kingdom.RulingClan && x.Leader?.IsPrisoner == true)) Add(tags, "captured_clan_leader", "foreign_prisoners");
            if (kingdom.Clans.Any(x => x != kingdom.RulingClan && x.Fiefs.Count == 0)) tags.Add("landless_loyal_clan");
            if (kingdom.Clans.Any(x => x != kingdom.RulingClan && x.Leader != null && x.Leader.GetRelation(kingdom.Leader) < -10))
                Add(tags, "clan_grievance", "low_clan_relation", "clan_conflict", "privilege_threat", "household_debt");
            Add(tags, "open_issues", "merchant_issue", "justice_grievance", "unpopular_policy", "stalled_project", "bad_project",
                "poor_trade_route", "merchant_burden", "broken_tax_promise", "ambassador_warning", "border_tension");
#if !REIGN_EXCLUDE_COURT
            if (fiefs.Any(x => ReignCourtCampaignBehavior.Instance?.GetSpymasterLoyaltyDelta(x) != 0f
                || ReignCourtCampaignBehavior.Instance?.GetSpymasterSecurityDelta(x) != 0f))
                Add(tags, "subterfuge_village", "subterfuge_food", "capital_spy_risk", "known_agent", "corruption_evidence",
                    "war_plan_leak", "hostile_rumors", "noble_intrigue", "suspected_proxy_raid");
#endif
            return tags;
        }

        private static Settlement SelectTargetSettlement(Kingdom kingdom, string tag)
        {
            IEnumerable<Settlement> owned = Settlement.All.Where(x => x != null && x.MapFaction == kingdom && (x.IsTown || x.IsCastle || x.IsVillage));
            if ((tag ?? string.Empty).IndexOf("village", StringComparison.OrdinalIgnoreCase) >= 0
                || (tag ?? string.Empty).IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0
                || (tag ?? string.Empty).IndexOf("hearth", StringComparison.OrdinalIgnoreCase) >= 0
                || (tag ?? string.Empty).IndexOf("farmer", StringComparison.OrdinalIgnoreCase) >= 0)
                return owned.Where(x => x.IsVillage).OrderBy(x => x.Village?.Hearth ?? float.MaxValue).FirstOrDefault();
            if ((tag ?? string.Empty).IndexOf("prosper", StringComparison.OrdinalIgnoreCase) >= 0
                || (tag ?? string.Empty).IndexOf("trade", StringComparison.OrdinalIgnoreCase) >= 0)
                return owned.Where(x => x.IsTown).OrderBy(x => x.Town?.Prosperity ?? float.MaxValue).FirstOrDefault();
            return owned.Where(x => x.IsFortification).OrderBy(x => x.Town?.Loyalty ?? float.MaxValue).FirstOrDefault()
                ?? owned.FirstOrDefault();
        }

        private static Kingdom SelectTargetKingdom(Kingdom kingdom)
        {
            return Kingdom.All.Where(x => x != null && x != kingdom && !x.IsEliminated)
                .OrderByDescending(x => kingdom.GetStanceWith(x).IsAtWar)
                .ThenBy(x => kingdom.Leader == null || x.Leader == null ? 0 : kingdom.Leader.GetRelation(x.Leader))
                .ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private static Clan SelectTargetClan(Kingdom kingdom)
        {
            return kingdom.Clans.Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated)
                .OrderBy(x => kingdom.Leader == null || x.Leader == null ? 0 : kingdom.Leader.GetRelation(x.Leader))
                .ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private static Hero SelectTargetHero(Kingdom kingdom, string tag, Settlement settlement, Clan clan)
        {
            if ((tag ?? string.Empty).IndexOf("notable", StringComparison.OrdinalIgnoreCase) >= 0)
                return settlement?.Notables?.OrderByDescending(x => x.Power).FirstOrDefault();
            if ((tag ?? string.Empty).IndexOf("prison", StringComparison.OrdinalIgnoreCase) >= 0
                || (tag ?? string.Empty).IndexOf("captured", StringComparison.OrdinalIgnoreCase) >= 0)
                return kingdom.Clans.SelectMany(x => x.Heroes).FirstOrDefault(x => x?.IsPrisoner == true);
            return clan?.Leader;
        }

        private static PolicyObject SelectTargetPolicy(Kingdom kingdom,
            ReignGovernmentResolutionTemplate template)
        {
            if (kingdom == null || template == null) return null;
            bool repeal = template.FirstRoute.Action == ReignGovernmentResolutionAction.RepealPolicy
                || template.SecondRoute.Action == ReignGovernmentResolutionAction.RepealPolicy;
            bool enact = template.FirstRoute.Action == ReignGovernmentResolutionAction.EnactPolicy
                || template.SecondRoute.Action == ReignGovernmentResolutionAction.EnactPolicy;
            IEnumerable<PolicyObject> candidates = repeal
                ? kingdom.ActivePolicies
                : enact
                    ? MBObjectManager.Instance.GetObjectTypeList<PolicyObject>().Where(x => x != null && !kingdom.HasPolicy(x))
                    : Enumerable.Empty<PolicyObject>();
            return candidates.Where(x => x != null)
                .OrderBy(x => ReignGovernmentRules.StableHash("resolution-policy|" + template.Id + "|" + x.StringId))
                .FirstOrDefault();
        }

        private static bool ResolutionTargets(ReignGovernmentResolutionRecord record, string targetId)
        {
            return Same(record.TargetSettlementStringId, targetId) || Same(record.TargetKingdomStringId, targetId)
                || Same(record.TargetClanStringId, targetId) || Same(record.TargetHeroStringId, targetId)
                || Same(record.TargetPolicyStringId, targetId);
        }

        private static string MergeEvidence(string json, string evidence)
        {
            JObject value;
            try { value = JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
            catch { value = new JObject(); }
            JArray history = value["progress"] as JArray ?? new JArray();
            if (!string.IsNullOrWhiteSpace(evidence)) history.Add(evidence);
            while (history.Count > 24) history.RemoveAt(0);
            value["progress"] = history;
            value["updatedDay"] = CurrentDay();
            return value.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static Kingdom FindKingdom(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Kingdom.All.FirstOrDefault(x => Same(x.StringId, id));
        }

        private static void Add(HashSet<string> tags, params string[] values)
        {
            foreach (string value in values) tags.Add(value);
        }

        private sealed class SpeakerStatementRequest
        {
            internal string MeetingId;
            internal Kingdom Kingdom;
            internal ReignGovernmentPartyRecord Party;
            internal List<ReignGovernmentResolutionRecord> Resolutions;
        }
    }
}
