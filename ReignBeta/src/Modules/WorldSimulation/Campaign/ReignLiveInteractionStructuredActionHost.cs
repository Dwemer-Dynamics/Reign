using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static Task<LiveCommandResult> StructuredActionTestAsync(JObject command) => ReignMainThread.InvokeAsync(() =>
        {
            JObject enrollment = command["enrollment"] as JObject;
            string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
            string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
            if (!_armed || enrollment == null || enrollment.Value<string>("source") != "reign_mcp_structured_actions"
                || string.IsNullOrWhiteSpace(enrollment.Value<string>("campaignTestRunId")) || expected.Length == 0 || baseline.Length == 0
                || expected == baseline || expected != ActiveSaveName()
                || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || enrollment.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                || command.Value<string>("gameInstanceId") != _gameInstanceId
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                return LiveCommandResult.Failed("Structured action tests require the exact paused, armed, verified run-owned Current campaign and timeline. A save-name prefix is not authorization.");
            try
            {
                var c = command["testCase"] as JObject ?? throw new InvalidOperationException("Missing structured test case.");
                if (c.ToString(Formatting.None).Length > 65536) throw new InvalidOperationException("Structured case exceeds the input bound.");
                string phase = c.Value<string>("phase") ?? "", kind = c.Value<string>("kind") ?? "";
                string id = c.Value<string>("operationId") ?? "";
                if (new[] { "run", "preview", "status", "fixture" }.Contains(phase)
                    && (id.Length < 1 || id.Length > 80 || id.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')))
                    throw new InvalidOperationException("Supply a stable operation ID.");
                foreach (string field in new[] { "heroIds", "itemIds" })
                    if (c[field] != null && (!(c[field] is JArray list) || list.Count > 16 || list.Any(x => x.Type != JTokenType.String)))
                        throw new InvalidOperationException("Snapshot IDs must be bounded exact string lists.");
                var evidence = new JObject { ["schema"] = "reign-structured-action-evidence-v1", ["caseId"] = c.Value<string>("caseId"),
                    ["phase"] = phase, ["operationId"] = id, ["origin"] = "operator", ["enrollment"] = enrollment.DeepClone(),
                    ["semanticVerification"] = "pending", ["before"] = BuildStructuredActionNativeSnapshot(c) };
                JToken result;
                switch (phase)
                {
                    case "catalog":
                        result = new JObject { ["structured"] = JArray.FromObject(ReignActionCatalog.All), ["native"] = JArray.FromObject(ReignNativeActionCatalog.All) }; break;
                    case "entities":
                        int offset = c.Value<int?>("offset") ?? 0;
                        if (offset < 0 || offset > 100000 || (c.Value<string>("query") ?? "").Length > 128) throw new InvalidOperationException("Invalid search bounds.");
                        var rows = ReignActionEntities.Find(c.Value<string>("entityKind"), c.Value<string>("ownerHeroId"), c.Value<string>("query") ?? "");
                        result = new JObject { ["offset"] = offset, ["total"] = rows.Count, ["rows"] = JArray.FromObject(rows.Skip(offset).Take(64)) }; break;
                    case "status":
                        if (kind == "native" || kind == "fixture") result = ReignAgreementBehavior.Instance.NativeOperation(id) ?? throw new InvalidOperationException("No retained native operation has that identity.");
                        else if (kind == "agreement") result = JObject.FromObject(ReignAgreementBehavior.Instance.Status(id));
                        else throw new InvalidOperationException("Choose native or agreement.");
                        break;
                    case "fixture":
                        if (kind != "fixture" || c.Value<string>("actionId") != "fixture.ship_damage")
                            throw new InvalidOperationException("Only the fixed ship damage fixture is available.");
                        result = ApplyStructuredShipDamageFixture(c, id);
                        break;
                    case "preview": case "run":
                        if (kind == "native")
                        {
                            var args = (c["arguments"] as JObject ?? throw new InvalidOperationException("Missing native arguments."))
                                .Properties().ToDictionary(p => p.Name, p => p.Value.Type == JTokenType.String ? (string)p.Value : throw new InvalidOperationException("Native arguments are strings."), StringComparer.Ordinal);
                            if (args.ContainsKey("operation")) throw new InvalidOperationException("The case owns operation identity.");
                            args["operation"] = id;
                            result = ReignNativeActionConsole.Execute(c.Value<string>("actionId"), args, phase == "run");
                        }
                        else if (kind == "agreement")
                        {
                            var a = (c["agreement"] as JObject)?.ToObject<ActionAgreement>() ?? throw new InvalidOperationException("Missing typed agreement.");
                            a.Id = id; a.Origin = ActionOrigin.Console; a.State = AgreementState.Draft;
                            a.PlayerAcceptedHash = ""; a.NpcAcceptedHash = "";
                            result = phase == "run" ? JObject.FromObject(ReignActionService.RunOperator(a))
                                : new JObject { ["errors"] = JArray.FromObject(ReignActionService.Preview(a)), ["termsHash"] = a.TermsHash() };
                        }
                        else throw new InvalidOperationException("Choose native or agreement.");
                        break;
                    default: throw new InvalidOperationException("Unsupported structured test phase.");
                }
                evidence["result"] = result; evidence["after"] = BuildStructuredActionNativeSnapshot(c);
                if (kind == "agreement" && id.Length > 0)
                {
                    var journal = ReignAgreementBehavior.Instance.Agreements.FirstOrDefault(j => j.Agreement.Id == id);
                    if (journal != null) evidence["journal"] = JObject.FromObject(journal);
                }
                return LiveCommandResult.Completed("Production adapter evidence captured. Apply a case-specific native effect assertion before recording a pass.", evidence);
            }
            catch (Exception ex) { return LiveCommandResult.Failed("Structured action case failed: " + ex.Message); }
        });

        // This fixture is separate from the action catalog and never represents NPC consent.
        private static JObject ApplyStructuredShipDamageFixture(JObject c, string id)
        {
            var args = c["arguments"] as JObject ?? throw new InvalidOperationException("Missing fixture arguments.");
            string[] fields = { "shipId", "ownerPartyId", "expectedHitPoints", "remainingRatio" };
            if (args.Count != fields.Length || args.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || ((string)p.Value).Length < 1 || ((string)p.Value).Length > 128))
                throw new InvalidOperationException("Supply exactly four bounded ship fixture arguments.");
            float expected = float.Parse(args.Value<string>("expectedHitPoints"), NumberStyles.Float, CultureInfo.InvariantCulture);
            float ratio = float.Parse(args.Value<string>("remainingRatio"), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (float.IsNaN(expected) || float.IsInfinity(expected) || expected <= 0f
                || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0.05f || ratio > 0.75f)
                throw new InvalidOperationException("Fixture hull values must be finite; remainingRatio is 0.05 through 0.75.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                "fixture.ship_damage\n" + JsonConvert.SerializeObject(new SortedDictionary<string, string>(
                    args.Properties().ToDictionary(p => p.Name, p => (string)p.Value), StringComparer.Ordinal)))))
                .Replace("-", "").ToLowerInvariant();
            var behavior = ReignAgreementBehavior.Instance;
            var retained = behavior.NativeOperation(id);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash) throw new InvalidOperationException("Fixture operation identity already has different arguments.");
                return retained;
            }
            var owners = MobileParty.All.Where(p => p.Ships.Any(s => ReignAssetTransfer.ShipId(s) == args.Value<string>("shipId"))).ToList();
            if (owners.Count != 1 || owners[0].StringId != args.Value<string>("ownerPartyId"))
                throw new InvalidOperationException("The exact ship must have one verified owner party.");
            var party = owners[0];
            var ship = party.Ships.Single(s => ReignAssetTransfer.ShipId(s) == args.Value<string>("shipId"));
            if (!party.IsActive || party.MapEvent != null || party.SiegeEvent != null || party.IsTransitionInProgress || ship.IsUsedByQuest)
                throw new InvalidOperationException("Damage fixtures require an active, safe party and a ship free of quest duties.");
            float before = ship.HitPoints, maximum = ship.MaxHitPoints, damaged = maximum * ratio;
            if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0f || before != expected || damaged <= 0f || damaged >= before)
                throw new InvalidOperationException("The observed hull must match exactly and the fixture must cause genuine positive damage.");
            var receipt = new JObject { ["schema"] = "reign-ship-damage-fixture-v1", ["operationId"] = id,
                ["actionId"] = "fixture.ship_damage", ["requestHash"] = hash, ["origin"] = "operator_fixture",
                ["campaignDay"] = CampaignTime.Now.ToDays, ["shipId"] = args["shipId"].DeepClone(),
                ["ownerPartyId"] = party.StringId, ["maxHitPoints"] = maximum, ["beforeHitPoints"] = before,
                ["requestedHitPoints"] = damaged, ["status"] = "started", ["invocations"] = 1,
                ["semanticVerification"] = "fixture_only" };
            behavior.SaveNativeOperation(id, receipt);
            try { ship.HitPoints = damaged; receipt["afterHitPoints"] = ship.HitPoints; receipt["status"] = "returned"; }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["error"] = ex.Message; }
            behavior.SaveNativeOperation(id, receipt);
            return receipt;
        }

        private static JObject BuildStructuredActionNativeSnapshot(JObject testCase)
        {
            var snapshot = BuildGauntletNativeSnapshot(testCase);
            var rows = new JArray();
            foreach (string heroId in (testCase["heroIds"] as JArray ?? new JArray()).Values<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(16))
            {
                var context = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.BuildConversationContext(ReignActionService.HeroById(heroId));
                if (context?.Value<bool?>("enabled") != true) continue;
                var row = new JObject();
                foreach (string key in new[] { "schema", "heroId", "agreementId", "agreementRevision", "phase", "termKind",
                    "startedDay", "reviewDueDay", "returnDueDay", "returnSettlementId", "inMainParty", "observedWorldDay",
                    "wageGold", "wagePeriodDays", "wageArrearsGold", "reviewActionStatus" })
                    if (context[key] != null) row[key] = context[key].DeepClone();
                rows.Add(row);
            }
            snapshot["temporaryPartyGuests"] = new JObject { ["schema"] = "reign-structured-guest-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 16, ["rows"] = rows };
            var ids = new HashSet<string>((testCase["heroIds"] as JArray ?? new JArray()).Values<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(16), StringComparer.Ordinal);
            snapshot["nativeMission"] = BuildStructuredMissionObservation(ids);
            snapshot["rebellionState"] = BuildStructuredRebellionObservation(ids);
            var orders = (ReignCampaignCommandBehavior.Instance?.Orders ?? new ReignCampaignOrderRecord[0])
                .Where(o => o != null && (ids.Contains(o.CommanderHeroStringId) || ids.Contains(o.IssuerHeroStringId)))
                .OrderBy(o => o.OrderId, StringComparer.Ordinal).ToList();
            var orderRows = new JArray();
            foreach (var o in orders.Take(64))
            {
                var party = ReignActionService.HeroById(o.CommanderHeroStringId)?.PartyBelongedTo;
                orderRows.Add(new JObject { ["orderId"] = o.OrderId, ["commanderHeroId"] = o.CommanderHeroStringId,
                    ["issuerHeroId"] = o.IssuerHeroStringId, ["objective"] = o.Objective, ["status"] = o.Status,
                    ["stage"] = o.Stage, ["authorityMode"] = o.AuthorityMode, ["targetSettlementId"] = o.TargetSettlementStringId,
                    ["targetPartyId"] = o.TargetPartyStringId, ["currentStepIndex"] = o.CurrentStepIndex,
                    ["completedStepCount"] = o.CompletedStepCount, ["stepCount"] = o.Steps?.Count ?? 0,
                    ["deadlineDay"] = o.DeadlineDay, ["nextReviewDay"] = o.NextReviewDay, ["lastReviewDay"] = o.LastReviewDay,
                    ["nativeReassertions"] = o.NativeReassertions, ["awaitingGuidance"] = o.AwaitingGuidance,
                    ["actualPartyId"] = party?.StringId, ["actualLeaderHeroId"] = party?.LeaderHero?.StringId,
                    ["actualSettlementId"] = party?.CurrentSettlement?.StringId, ["actualBehavior"] = party?.DefaultBehavior.ToString(),
                    ["actualTargetSettlementId"] = party?.TargetSettlement?.StringId, ["actualTargetPartyId"] = party?.TargetParty?.StringId,
                    ["actualPosition"] = party?.Position.ToString() });
            }
            snapshot["campaignOrders"] = new JObject { ["schema"] = "reign-structured-order-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = orders.Count,
                ["truncated"] = orders.Count > 64, ["rows"] = orderRows };
            var kingdoms = new HashSet<string>(ids.Select(id => ReignActionService.HeroById(id)?.Clan?.Kingdom?.StringId)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
            var treaties = (ReignAICampaignBehavior.Instance?.Agreements ?? new ReignDiplomaticAgreementRecord[0])
                .Where(a => a != null && kingdoms.Contains(a.ActorKingdomStringId) && kingdoms.Contains(a.TargetKingdomStringId))
                .OrderBy(a => a.AgreementId, StringComparer.Ordinal).ToList();
            var treatyRows = new JArray();
            foreach (var a in treaties.Take(64)) treatyRows.Add(new JObject { ["agreementId"] = a.AgreementId,
                ["kind"] = a.Kind, ["actorKingdomId"] = a.ActorKingdomStringId, ["targetKingdomId"] = a.TargetKingdomStringId,
                ["actorClanId"] = a.ActorClanStringId, ["targetClanId"] = a.TargetClanStringId,
                ["targetHeroId"] = a.TargetHeroStringId, ["targetSettlementId"] = a.TargetSettlementStringId,
                ["createdDay"] = a.CreatedDay, ["expireDay"] = a.ExpireDay, ["isActive"] = a.IsActive,
                ["isPublic"] = a.IsPublic, ["endedDay"] = a.EndedDay, ["breakerKingdomId"] = a.BreakerKingdomStringId });
            snapshot["diplomaticAgreements"] = new JObject { ["schema"] = "reign-structured-treaty-observation-v1",
                ["requestedKingdomPairsOnly"] = true, ["maximumRows"] = 64, ["total"] = treaties.Count,
                ["truncated"] = treaties.Count > 64, ["rows"] = treatyRows };
            var nativeAlliance = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var nativeTrade = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var requestedRealms = Kingdom.All.Where(k => k != null && kingdoms.Contains(k.StringId))
                .OrderBy(k => k.StringId, StringComparer.Ordinal).ToList();
            var nativePairs = new JArray();
            int nativePairTotal = requestedRealms.Count * (requestedRealms.Count - 1) / 2;
            for (int i = 0; i < requestedRealms.Count; i++)
                for (int n = i + 1; n < requestedRealms.Count && nativePairs.Count < 64; n++)
                {
                    var first = requestedRealms[i]; var second = requestedRealms[n];
                    bool allied = nativeAlliance?.IsAllyWithKingdom(first, second) == true;
                    TradeAgreementsCampaignBehavior.TradeAgreement existingTrade;
                    bool trading = nativeTrade != null && nativeTrade.HasTradeAgreement(first, second, out existingTrade);
                    nativePairs.Add(new JObject { ["firstKingdomId"] = first.StringId, ["secondKingdomId"] = second.StringId,
                        ["allianceAvailable"] = nativeAlliance != null, ["tradeAvailable"] = nativeTrade != null,
                        ["allied"] = allied, ["trading"] = trading,
                        ["allianceEndDay"] = allied ? (JToken)new JValue(nativeAlliance.GetAllianceEndDate(first, second).ToDays) : JValue.CreateNull(),
                        ["tradeEndDay"] = trading ? (JToken)new JValue(nativeTrade.GetTradeAgreementEndDate(first, second).ToDays) : JValue.CreateNull() });
                }
            snapshot["nativeDiplomaticRelations"] = new JObject { ["schema"] = "reign-structured-native-diplomacy-observation-v1",
                ["requestedKingdomPairsOnly"] = true, ["maximumRows"] = 64, ["total"] = nativePairTotal,
                ["truncated"] = nativePairTotal > 64, ["rows"] = nativePairs };
#if !REIGN_EXCLUDE_COURT
            var court = ReignBeta.Court.ReignCourtCampaignBehavior.Instance;
            var officeRows = new JArray();
            var offices = (court?.Offices ?? new ReignBeta.Court.CourtOfficeAssignment[0])
                .Where(o => o != null && ids.Contains(o.HeroStringId)).OrderBy(o => o.AssignmentId, StringComparer.Ordinal).ToList();
            foreach (var o in offices.Take(64)) officeRows.Add(new JObject { ["assignmentId"] = o.AssignmentId,
                ["office"] = o.Office.ToString(), ["heroId"] = o.HeroStringId, ["isActive"] = o.IsActive,
                ["assignedDay"] = o.AssignedDay, ["dismissedDay"] = o.DismissedDay,
                ["revision"] = o.Revision, ["lastCommandId"] = o.LastCommandId });
            var regents = (court?.Regents ?? new ReignBeta.Court.CourtRegentAssignment[0])
                .Where(r => r != null && ids.Contains(r.HeroStringId)).OrderBy(r => r.AssignmentId, StringComparer.Ordinal).ToList();
            var regentRows = new JArray();
            foreach (var r in regents.Take(64)) regentRows.Add(new JObject { ["assignmentId"] = r.AssignmentId,
                ["heroId"] = r.HeroStringId, ["isActive"] = r.IsActive, ["assignedDay"] = r.AssignedDay,
                ["dismissedDay"] = r.DismissedDay, ["revision"] = r.Revision, ["lastCommandId"] = r.LastCommandId });
            var ambassadors = (court?.Ambassadors ?? new ReignBeta.Court.AmbassadorPosting[0])
                .Where(a => a != null && ids.Contains(a.HeroStringId)).OrderBy(a => a.PostingId, StringComparer.Ordinal).ToList();
            var ambassadorRows = new JArray();
            foreach (var a in ambassadors.Take(64)) ambassadorRows.Add(new JObject { ["postingId"] = a.PostingId,
                ["heroId"] = a.HeroStringId, ["targetKingdomId"] = a.TargetKingdomStringId, ["missionType"] = a.MissionType,
                ["status"] = a.Status, ["assignedDay"] = a.AssignedDay, ["arrivalDay"] = a.ArrivalDay,
                ["lastReportDay"] = a.LastReportDay, ["recalledDay"] = a.RecalledDay,
                ["revision"] = a.Revision, ["lastCommandId"] = a.LastCommandId });
            var chancellor = court?.Chancellor;
            bool includeChancellor = chancellor != null && ids.Contains(chancellor.HeroId);
            var chancellorRow = includeChancellor ? new JObject { ["termId"] = chancellor.TermId,
                ["heroId"] = chancellor.HeroId, ["state"] = chancellor.State.ToString(), ["salary"] = chancellor.Salary,
                ["desiredActive"] = chancellor.DesiredActive, ["effectiveDay"] = chancellor.DesiredActiveEffectiveDay,
                ["appointedDay"] = chancellor.AppointedDay, ["lastPaidDay"] = chancellor.LastPaidDay,
                ["totalPaidGold"] = chancellor.TotalPaidGold, ["paidActiveDays"] = chancellor.PaidActiveDays,
                ["failedPaymentDays"] = chancellor.FailedPaymentDays, ["emergency"] = chancellor.Emergency,
                ["suppressesPetitions"] = chancellor.SuppressesPetitions } : null;
            var serviceHistory = (court?.RulerDocketState?.History ?? new List<ReignBeta.Court.ReignDocketHistoryRecord>())
                .Where(h => h != null && h.Type == "chancellor_service" && ids.Contains(h.ChancellorHeroId))
                .OrderByDescending(h => h.Day).ThenBy(h => h.RecordId, StringComparer.Ordinal).ToList();
            var historyRows = new JArray();
            foreach (var h in serviceHistory.Take(64)) historyRows.Add(new JObject { ["recordId"] = h.RecordId,
                ["heroId"] = h.ChancellorHeroId, ["type"] = h.Type, ["outcome"] = h.Outcome, ["day"] = h.Day });
            snapshot["courtState"] = new JObject { ["schema"] = "reign-structured-court-observation-v1",
                ["requestedHeroIdsOnly"] = true, ["maximumRowsPerCollection"] = 64,
                ["available"] = court != null, ["offices"] = officeRows, ["officeTotal"] = offices.Count,
                ["officesTruncated"] = offices.Count > 64, ["regents"] = regentRows, ["regentTotal"] = regents.Count,
                ["regentsTruncated"] = regents.Count > 64, ["ambassadors"] = ambassadorRows,
                ["ambassadorTotal"] = ambassadors.Count, ["ambassadorsTruncated"] = ambassadors.Count > 64,
                ["chancellor"] = chancellorRow, ["chancellorHistory"] = historyRows,
                ["chancellorHistoryTotal"] = serviceHistory.Count, ["chancellorHistoryTruncated"] = serviceHistory.Count > 64 };
            var missions = (court?.SpymasterState?.Missions ?? new List<ReignBeta.Court.ReignSpymasterMission>())
                .Where(m => m != null && ids.Contains(m.SpymasterHeroStringId))
                .OrderBy(m => m.MissionId, StringComparer.Ordinal).ToList();
            var missionRows = new JArray();
            foreach (var m in missions.Take(64)) missionRows.Add(new JObject { ["missionId"] = m.MissionId,
                ["spymasterHeroId"] = m.SpymasterHeroStringId, ["missionType"] = m.MissionType,
                ["targetType"] = m.TargetType, ["targetId"] = m.TargetStringId,
                ["sponsorKingdomId"] = m.SponsorKingdomStringId, ["goldCost"] = m.GoldCost,
                ["startedDay"] = m.StartedDay, ["dueDay"] = m.DueDay, ["resolvedDay"] = m.ResolvedDay,
                ["state"] = m.State.ToString(), ["agreementEffectState"] = m.AgreementEffectState,
                ["harmful"] = m.Harmful, ["targetNoticed"] = m.TargetNoticed, ["playerIdentified"] = m.PlayerIdentified,
                ["attributionPending"] = m.AttributionPending });
            snapshot["spymasterMissions"] = new JObject { ["schema"] = "reign-structured-spymaster-observation-v1",
                ["requestedSpymasterIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = missions.Count,
                ["truncated"] = missions.Count > 64, ["rows"] = missionRows };
            var effects = (court?.SpymasterState?.Effects ?? new List<ReignBeta.Court.ReignSpymasterSettlementEffect>())
                .Where(e => e != null && ids.Contains(e.AgentHeroStringId))
                .OrderBy(e => e.EffectId, StringComparer.Ordinal).ToList();
            var effectRows = new JArray();
            foreach (var e in effects.Take(64))
            {
                var town = TaleWorlds.CampaignSystem.Settlements.Settlement.Find(e.SettlementStringId)?.Town;
                effectRows.Add(new JObject { ["effectId"] = e.EffectId, ["agentHeroId"] = e.AgentHeroStringId,
                    ["settlementId"] = e.SettlementStringId, ["effectType"] = e.EffectType,
                    ["sourceKingdomId"] = e.SourceKingdomStringId, ["startDay"] = e.StartDay,
                    ["endDay"] = e.EndDay, ["magnitude"] = e.Magnitude,
                    ["actualFoodStocks"] = town?.FoodStocks, ["actualSecurity"] = town?.Security,
                    ["actualLoyalty"] = town?.Loyalty, ["actualProsperity"] = town?.Prosperity });
            }
            snapshot["spymasterEffects"] = new JObject { ["schema"] = "reign-structured-spymaster-effects-v1",
                ["requestedAgentIdsOnly"] = true, ["maximumRows"] = 64, ["total"] = effects.Count,
                ["truncated"] = effects.Count > 64, ["rows"] = effectRows };
#endif
            return snapshot;
        }

        private static JObject BuildStructuredRebellionObservation(HashSet<string> ids)
        {
            var behavior = ReignRebellionCampaignBehavior.Instance;
            var plots = (behavior?.Plots ?? new ReignRebellionPlotRecord[0])
                .Where(p => p != null && (ids.Contains(p.PlayerHeroStringId) || ids.Contains(p.RulerHeroStringId)
                    || ids.Contains(p.InformerHeroStringId))).OrderBy(p => p.PlotId, StringComparer.Ordinal).ToList();
            var plotIds = new HashSet<string>(plots.Select(p => p.PlotId), StringComparer.Ordinal);
            var pledges = (behavior?.Pledges ?? new ReignRebellionPledgeRecord[0])
                .Where(p => p != null && plotIds.Contains(p.PlotId) && ids.Contains(p.LordHeroStringId))
                .OrderBy(p => p.PledgeId, StringComparer.Ordinal).ToList();
            var memberships = (behavior?.Memberships ?? new ReignRebellionMembershipRecord[0])
                .Where(m => m != null && ids.Contains(m.LeaderHeroStringId))
                .OrderBy(m => m.MovementId, StringComparer.Ordinal).ThenBy(m => m.ClanStringId, StringComparer.Ordinal).ToList();
            var linkedMovements = new HashSet<string>(plots.Select(p => p.MovementId)
                .Concat(memberships.Select(m => m.MovementId)), StringComparer.Ordinal);
            var movements = (behavior?.Movements ?? new ReignRebellionMovementRecord[0])
                .Where(m => m != null && (ids.Contains(m.LeaderHeroStringId) || ids.Contains(m.OriginalRulerHeroStringId)
                    || linkedMovements.Contains(m.MovementId)))
                .OrderBy(m => m.MovementId, StringComparer.Ordinal).ToList();
            var summonses = (behavior?.Summonses ?? new ReignRebellionSummonsRecord[0])
                .Where(s => s != null && plotIds.Contains(s.PlotId) && (ids.Contains(s.PlayerHeroStringId)
                    || ids.Contains(s.RulerHeroStringId) || ids.Contains(s.InformerHeroStringId)))
                .OrderBy(s => s.SummonsId, StringComparer.Ordinal).ToList();
            var plotRows = new JArray();
            foreach (var p in plots.Take(64)) plotRows.Add(new JObject {
                ["plotId"] = p.PlotId, ["playerHeroId"] = p.PlayerHeroStringId, ["playerClanId"] = p.PlayerClanStringId,
                ["parentKingdomId"] = p.ParentKingdomStringId, ["rulerHeroId"] = p.RulerHeroStringId,
                ["status"] = p.Status, ["revision"] = p.Revision, ["createdDay"] = p.CreatedDay,
                ["updatedDay"] = p.UpdatedDay, ["informerHeroId"] = p.InformerHeroStringId,
                ["reportDispatchDay"] = p.ReportDispatchDay, ["reportDeliveryDay"] = p.ReportDeliveryDay,
                ["summonsId"] = p.SummonsId, ["movementId"] = p.MovementId, ["pledgesActivated"] = p.PledgesActivated,
                ["resolution"] = p.Resolution, ["rulerVerdict"] = p.RulerVerdict });
            var pledgeRows = new JArray();
            foreach (var p in pledges.Take(64)) pledgeRows.Add(new JObject {
                ["pledgeId"] = p.PledgeId, ["plotId"] = p.PlotId, ["lordHeroId"] = p.LordHeroStringId,
                ["clanId"] = p.ClanStringId, ["originalKingdomId"] = p.OriginalKingdomStringId,
                ["decision"] = p.Decision, ["requestChannel"] = p.RequestChannel, ["actionId"] = p.ActionId,
                ["requestedDay"] = p.RequestedDay, ["respondedDay"] = p.RespondedDay,
                ["reportedToRuler"] = p.ReportedToRuler, ["activated"] = p.Activated,
                ["activatedDay"] = p.ActivatedDay, ["movementId"] = p.MovementId });
            var membershipRows = new JArray();
            foreach (var m in memberships.Take(64))
            {
                var clan = ReignObjectResolver.FindClan(m.ClanStringId);
                membershipRows.Add(new JObject { ["movementId"] = m.MovementId, ["clanId"] = m.ClanStringId,
                    ["leaderHeroId"] = m.LeaderHeroStringId, ["side"] = m.Side, ["isCommitted"] = m.IsCommitted,
                    ["originalKingdomId"] = m.OriginalKingdomStringId, ["joinedDay"] = m.JoinedDay,
                    ["switchCount"] = m.SwitchCount, ["lastSwitchDay"] = m.LastSwitchDay,
                    ["fateApplied"] = m.FateApplied, ["fateOutcome"] = m.FateOutcome, ["isDefeated"] = m.IsDefeated,
                    ["playerChoiceRequired"] = m.PlayerChoiceRequired, ["actualClanAvailable"] = clan != null,
                    ["actualKingdomId"] = clan?.Kingdom?.StringId, ["actualLeaderHeroId"] = clan?.Leader?.StringId,
                    ["actualClanEliminated"] = clan == null ? JValue.CreateNull() : JToken.FromObject(clan.IsEliminated) });
            }
            var movementRows = new JArray();
            foreach (var m in movements.Take(64))
            {
                var parent = ReignObjectResolver.FindKingdom(m.ParentKingdomStringId);
                var rebel = ReignObjectResolver.FindKingdom(m.RebelKingdomStringId);
                movementRows.Add(new JObject { ["movementId"] = m.MovementId,
                    ["parentKingdomId"] = m.ParentKingdomStringId, ["rebelKingdomId"] = m.RebelKingdomStringId,
                    ["leaderClanId"] = m.LeaderClanStringId, ["leaderHeroId"] = m.LeaderHeroStringId,
                    ["originalRulerHeroId"] = m.OriginalRulerHeroStringId, ["stage"] = m.Stage,
                    ["isPlayerLed"] = m.IsPlayerLed, ["civilWarStartedDay"] = m.CivilWarStartedDay,
                    ["resolutionApplied"] = m.ResolutionApplied, ["resolution"] = m.Resolution,
                    ["winningSide"] = m.WinningSide, ["winningLeaderHeroId"] = m.WinningLeaderHeroStringId,
                    ["cooldownUntilDay"] = m.CooldownUntilDay, ["judgmentsPending"] = m.JudgmentsPending,
                    ["backingStatus"] = m.BackingStatus, ["backingSponsorKingdomId"] = m.BackingSponsorKingdomStringId,
                    ["backingActionId"] = m.BackingActionId, ["actualParentAvailable"] = parent != null,
                    ["actualRebelAvailable"] = rebel != null, ["actualParentLeaderHeroId"] = parent?.Leader?.StringId,
                    ["actualRebelLeaderHeroId"] = rebel?.Leader?.StringId, ["actualRebelRulingClanId"] = rebel?.RulingClan?.StringId,
                    ["actualRebelEliminated"] = rebel == null ? JValue.CreateNull() : JToken.FromObject(rebel.IsEliminated),
                    ["actualAtWar"] = parent == null || rebel == null ? JValue.CreateNull()
                        : JToken.FromObject(FactionManager.IsAtWarAgainstFaction(parent, rebel)) });
            }
            var summonsRows = new JArray();
            foreach (var s in summonses.Take(64)) summonsRows.Add(new JObject {
                ["summonsId"] = s.SummonsId, ["plotId"] = s.PlotId, ["playerHeroId"] = s.PlayerHeroStringId,
                ["rulerHeroId"] = s.RulerHeroStringId, ["informerHeroId"] = s.InformerHeroStringId,
                ["status"] = s.Status, ["issuedDay"] = s.IssuedDay, ["deliveryDay"] = s.DeliveryDay,
                ["deadlineDay"] = s.DeadlineDay, ["answeredDay"] = s.AnsweredDay, ["playerResponse"] = s.PlayerResponse,
                ["rulerVerdict"] = s.RulerVerdict, ["consequenceApplied"] = s.ConsequenceApplied, ["consequence"] = s.Consequence });
            return new JObject { ["schema"] = "reign-structured-rebellion-observation-v1",
                ["available"] = behavior != null, ["requestedHeroIdsOnly"] = true, ["maximumRowsPerCollection"] = 64,
                ["plots"] = new JObject { ["total"] = plots.Count, ["truncated"] = plots.Count > 64, ["rows"] = plotRows },
                ["pledges"] = new JObject { ["total"] = pledges.Count, ["truncated"] = pledges.Count > 64, ["rows"] = pledgeRows },
                ["memberships"] = new JObject { ["total"] = memberships.Count, ["truncated"] = memberships.Count > 64, ["rows"] = membershipRows },
                ["movements"] = new JObject { ["total"] = movements.Count, ["truncated"] = movements.Count > 64, ["rows"] = movementRows },
                ["summonses"] = new JObject { ["total"] = summonses.Count, ["truncated"] = summonses.Count > 64, ["rows"] = summonsRows } };
        }

        private static JObject BuildStructuredMissionObservation(HashSet<string> ids)
        {
            var mission = Mission.Current;
            var agents = mission == null ? new List<Agent>() : mission.AllAgents
                .Where(a => a?.Character != null && ids.Contains(a.Character.StringId))
                .OrderBy(a => a.Index).ToList();
            var agentRows = new JArray();
            foreach (var agent in agents.Take(32))
            {
                var weapons = new JArray();
                for (int slot = 0; slot < 4; slot++)
                {
                    string itemId = agent.Equipment?[(EquipmentIndex)slot].Item?.StringId;
                    if (!string.IsNullOrWhiteSpace(itemId))
                        weapons.Add(new JObject { ["slot"] = slot, ["itemId"] = itemId });
                }
                agentRows.Add(new JObject { ["heroId"] = agent.Character.StringId,
                    ["agentIndex"] = agent.Index, ["isMainAgent"] = agent == mission.MainAgent,
                    ["active"] = agent.IsActive(), ["state"] = agent.State.ToString(),
                    ["controller"] = agent.Controller.ToString(), ["aiControlled"] = agent.IsAIControlled,
                    ["watchState"] = agent.CurrentWatchState.ToString(), ["aiState"] = agent.AIStateFlags.ToString(),
                    ["mortality"] = agent.CurrentMortalityState.ToString(), ["health"] = agent.Health,
                    ["healthLimit"] = agent.HealthLimit, ["hasMeleeWeapon"] = agent.HasMeleeWeaponCached,
                    ["teamIndex"] = agent.Team?.TeamIndex, ["teamSide"] = agent.Team?.Side.ToString(),
                    ["enemyOfPlayer"] = agent.Team != null && mission.PlayerTeam != null
                        ? (JToken)new JValue(agent.Team.IsEnemyOf(mission.PlayerTeam)) : JValue.CreateNull(),
                    ["position"] = new JObject { ["x"] = agent.Position.x, ["y"] = agent.Position.y, ["z"] = agent.Position.z },
                    ["weapons"] = weapons });
            }
            var removals = mission?.GetMissionBehavior<ReignArenaDuelEquipmentBehavior>()?.Removals;
            var removalRows = new JArray();
            if (removals != null)
                foreach (var removal in removals.Where(r => ids.Contains(r.HeroId)).Take(2))
                    removalRows.Add(new JObject { ["heroId"] = removal.HeroId, ["agentIndex"] = removal.AgentIndex,
                        ["state"] = removal.State, ["health"] = removal.Health,
                        ["affectorHeroId"] = ids.Contains(removal.AffectorHeroId) ? removal.AffectorHeroId : string.Empty });
            return new JObject { ["schema"] = "reign-structured-mission-observation-v1",
                ["available"] = mission != null, ["mode"] = mission?.Mode.ToString(),
                ["mainAgentIndex"] = mission?.MainAgent?.Index, ["playerTeamIndex"] = mission?.PlayerTeam?.TeamIndex,
                ["nativeArenaDuelControllerPresent"] = mission?.MissionBehaviors.Any(b =>
                    b.GetType().FullName == "SandBox.Missions.MissionLogics.Arena.ArenaDuelMissionController") == true,
                ["duelAgentRemovals"] = removalRows,
                ["requestedHeroIdsOnly"] = true, ["maximumRows"] = 32, ["total"] = agents.Count,
                ["truncated"] = agents.Count > 32, ["rows"] = agentRows };
        }
    }
}
