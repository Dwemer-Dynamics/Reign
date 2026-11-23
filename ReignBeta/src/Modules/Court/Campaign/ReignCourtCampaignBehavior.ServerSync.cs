using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private async Task SyncServerAgendaAsync()
        {
            if (!_serverSessionOpened || _session == null) return;
            JArray candidates = new JArray(_matters.Where(x => !x.IsTerminal).Select(ToServerMatter));
            ReignCourtServerResponse response = await ReignCourtServerClient.BuildAgendaAsync(_session, _serverRevision,
                "court_agenda_" + _session.SessionId + "_" + _session.LastAgendaDay, candidates).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Court server synchronized." : "Court server agenda unavailable: " + response.Error;
                if (response.Ok)
                {
                    _serverRevision = response.Revision;
                    foreach (JObject row in (response.Raw["agenda"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string id = row.Value<string>("matter_id");
                        if (!string.IsNullOrWhiteSpace(id)) _serverMatterRevisions[id] = row.Value<long?>("revision") ?? 0;
                    }
                }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            if (response.Ok) await SyncServerHomeAsync().ConfigureAwait(false);
        }

        private async Task TickServerSessionAsync()
        {
            if (_serverRequestInFlight || !_serverSessionOpened || _session == null) return;
            _serverRequestInFlight = true;
            try
            {
                ReignCourtServerResponse response = await ReignCourtServerClient.TickSessionAsync(_session, _serverRevision,
                    "court_tick_" + _session.SessionId + "_" + ((int)(CurrentDayFloat() * 24f)), BuildNativeSnapshot()).ConfigureAwait(false);
                NativeCourtExecution missionExecution = response.Ok && response.NativeActions.Count > 0
                    ? await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false)
                    : new NativeCourtExecution { Success = true };
                await ReignMainThread.InvokeAsync(() =>
                {
                    _serverAvailable = response.Ok;
                    _serverStatus = response.Ok
                        ? missionExecution.Success ? "Court server synchronized." : "Court synchronized, but an ambassador mission effect failed native validation: " + missionExecution.Error
                        : "Court server unavailable: " + response.Error;
                    if (response.Ok) _serverRevision = response.Revision;
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
                if (response.Ok) await SyncServerHomeAsync().ConfigureAwait(false);
            }
            finally { _serverRequestInFlight = false; }
        }

        private async Task CloseServerSessionAsync(string reason)
        {
            CourtSession session = _session;
            ReignCourtServerResponse response = await ReignCourtServerClient.CloseSessionAsync(session, _serverRevision,
                "court_close_" + session.SessionId, reason).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Court session closed on server." : "Court server unavailable while closing: " + response.Error;
                if (response.Ok) { _serverRevision = response.Revision; _serverSessionOpened = false; }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private async Task<string> StartServerMatterAsync(CourtMatter matter)
        {
            if (!_serverAvailable || !_serverSessionOpened || matter == null) return MutationUnavailable();
            long revision = _serverMatterRevisions.TryGetValue(matter.MatterId, out long known) ? known : matter.Revision;
            ReignCourtServerResponse response = await ReignCourtServerClient.StartMatterAsync(matter.MatterId, revision,
                "court_start_" + matter.MatterId + "_" + Guid.NewGuid().ToString("N")).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Matter opened with the court server." : "Matter remains queued: " + response.Error;
                if (response.Ok) _serverMatterRevisions[matter.MatterId] = response.Revision;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return response.Ok ? string.Empty : response.Error;
        }

        private async Task SyncServerHomeAsync()
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.GetHomeAsync().ConfigureAwait(false);
            if (!response.Ok) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                foreach (JObject row in (response.Raw["dailyAgenda"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    CourtMatter matter = UpsertServerMatter(row);
                    if (matter != null && !_agenda.Any(x => x.MatterId == matter.MatterId && x.AgendaDay == CurrentDay()))
                    {
                        _agenda.Add(new CourtAgendaItem
                        {
                            MatterId = matter.MatterId,
                            AgendaDay = CurrentDay(),
                            Slot = _agenda.Count(x => x.AgendaDay == CurrentDay()),
                            IsUrgentInterruption = matter.IsCritical,
                            Status = "scheduled"
                        });
                    }
                }
                foreach (JObject row in (response.Raw["peopleMatters"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerMatter(row);
                foreach (JObject row in (response.Raw["ambassadors"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerAmbassador(row);
                foreach (JObject row in (response.Raw["operations"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerOperation(row);
                foreach (JObject row in (response.Raw["offices"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerOffice(row);
                foreach (JObject row in (response.Raw["obligations"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerObligation(row);
                foreach (JObject row in (response.Raw["plots"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerPlot(row);
                foreach (JObject rumor in (response.Raw["rumors"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string rumorId = rumor.Value<string>("rumorId");
                    QueueMatter(new CourtMatter
                    {
                        SourceKey = "rumor:" + rumorId,
                        Kind = ReignCourtMatterKind.Rumor,
                        Title = rumor.Value<string>("claim") ?? "A new rumor reaches court",
                        Summary = "Source: " + (rumor.Value<string>("provenance") ?? "unknown") + ". Confidence " + ((rumor.Value<double?>("confidence") ?? 0.5d) * 100d).ToString("0") + "%.",
                        CreatedDay = (float)(rumor.Value<double?>("createdDay") ?? CurrentDayFloat()),
                        Priority = (int)Math.Round((rumor.Value<double?>("confidence") ?? 0.5d) * 50d),
                        RequiresServer = true,
                        DecisionOptionsJson = "[]"
                    });
                }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private CourtMatter UpsertServerMatter(JObject row)
        {
            string id = row.Value<string>("matter_id") ?? row.Value<string>("matterId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) return null;
            CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == id);
            if (matter == null)
            {
                matter = new CourtMatter { MatterId = id };
                _matters.Add(matter);
            }
            matter.SourceKey = row.Value<string>("source_key") ?? matter.SourceKey;
            matter.Kind = ParseMatterKind(row.Value<string>("kind"));
            matter.State = ParseMatterState(row.Value<string>("state"));
            matter.Title = row.Value<string>("title") ?? matter.Title;
            matter.Summary = row.Value<string>("summary") ?? matter.Summary;
            matter.Confidentiality = row.Value<string>("confidentiality") ?? matter.Confidentiality;
            matter.ParticipantHeroIdsCsv = JsonIds(row["participants_json"] ?? row["participants"]);
            matter.WitnessHeroIdsCsv = JsonIds(row["witnesses_json"] ?? row["witnesses"]);
            matter.SettlementStringId = row.Value<string>("settlement_id") ?? matter.SettlementStringId;
            matter.KingdomStringId = row.Value<string>("kingdom_id") ?? matter.KingdomStringId;
            matter.CreatedDay = row.Value<float?>("created_day") ?? matter.CreatedDay;
            matter.DueDay = row.Value<float?>("due_day") ?? matter.DueDay;
            matter.ScheduledDay = row.Value<float?>("scheduled_day") ?? matter.ScheduledDay;
            matter.Priority = row.Value<int?>("priority") ?? matter.Priority;
            matter.IsCritical = row.Value<int?>("is_critical") == 1 || row.Value<bool?>("isCritical") == true;
            matter.IsMajorRealmEvent = row.Value<int?>("is_major") == 1 || row.Value<bool?>("isMajorRealmEvent") == true;
            matter.IsAmbientRoleplay = row.Value<int?>("is_ambient") == 1 || row.Value<bool?>("isAmbientRoleplay") == true;
            matter.DecisionOptionsJson = JsonText(row["options_json"], matter.DecisionOptionsJson);
            matter.DefaultOutcomeJson = JsonText(row["default_outcome_json"], matter.DefaultOutcomeJson);
            matter.RequiresServer = row.Value<int?>("requires_server") != 0;
            matter.Revision = row.Value<long?>("revision") ?? matter.Revision;
            matter.CampaignId = _session?.CampaignId ?? matter.CampaignId;
            matter.TimelineId = _session?.TimelineId ?? matter.TimelineId;
            _serverMatterRevisions[id] = matter.Revision;
            return matter;
        }

        private void UpsertServerAmbassador(JObject row)
        {
            string id=row.Value<string>("posting_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            AmbassadorPosting posting=_ambassadors.FirstOrDefault(x=>x.PostingId==id);if(posting==null){posting=new AmbassadorPosting{PostingId=id};_ambassadors.Add(posting);}
            posting.HeroStringId=row.Value<string>("hero_id")??posting.HeroStringId;posting.TargetKingdomStringId=row.Value<string>("target_kingdom_id")??posting.TargetKingdomStringId;
            posting.MissionType=row.Value<string>("mission_type")??posting.MissionType;posting.MissionTermsJson=row.Value<string>("mission_terms_json")??posting.MissionTermsJson;
            posting.Status=row.Value<string>("status")??posting.Status;posting.AssignedDay=row.Value<float?>("assigned_day")??posting.AssignedDay;posting.ArrivalDay=row.Value<float?>("arrival_day")??posting.ArrivalDay;
            posting.LastReportDay=row.Value<float?>("last_report_day")??posting.LastReportDay;posting.RecalledDay=row.Value<float?>("recalled_day")??posting.RecalledDay;posting.Revision=row.Value<long?>("revision")??posting.Revision;
        }

        private void UpsertServerOffice(JObject row)
        {
            string id=row.Value<string>("assignment_id")??string.Empty,heroId=row.Value<string>("hero_id")??string.Empty,officeText=row.Value<string>("office")??string.Empty;if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(heroId))return;
            if(!Enum.TryParse(officeText,true,out ReignCourtOffice office))return;
            CourtOfficeAssignment assignment=_offices.FirstOrDefault(x=>x.AssignmentId==id);
            if(assignment==null)
            {
                foreach(CourtOfficeAssignment old in _offices.Where(x=>x.IsActive&&x.Office==office))old.IsActive=false;
                assignment=new CourtOfficeAssignment{AssignmentId=id,Office=office,HeroStringId=heroId};_offices.Add(assignment);
            }
            assignment.HeroStringId=heroId;assignment.AssignedDay=row.Value<float?>("assigned_day")??assignment.AssignedDay;assignment.DismissedDay=row.Value<float?>("dismissed_day")??assignment.DismissedDay;
            assignment.IsActive=string.Equals(row.Value<string>("status"),"active",StringComparison.OrdinalIgnoreCase);assignment.DismissalReason=row.Value<string>("dismissal_reason")??assignment.DismissalReason;assignment.Revision=row.Value<long?>("revision")??assignment.Revision;
        }

        private void UpsertServerOperation(JObject row)
        {
            string id=row.Value<string>("operation_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            IntelligenceOperation operation=_operations.FirstOrDefault(x=>x.OperationId==id);if(operation==null){operation=new IntelligenceOperation{OperationId=id};_operations.Add(operation);}
            operation.OperationType=row.Value<string>("operation_type")??operation.OperationType;operation.TargetType=row.Value<string>("target_type")??operation.TargetType;operation.TargetStringId=row.Value<string>("target_id")??operation.TargetStringId;
            operation.State=ParseOperationState(row.Value<string>("status"));operation.GoldCost=row.Value<int?>("gold_cost")??operation.GoldCost;operation.InfluenceCost=row.Value<float?>("influence_cost")??operation.InfluenceCost;
            operation.StartedDay=row.Value<float?>("started_day")??operation.StartedDay;operation.DueDay=row.Value<float?>("due_day")??operation.DueDay;operation.Risk=row.Value<float?>("risk")??operation.Risk;operation.Confidence=row.Value<float?>("confidence")??operation.Confidence;
            operation.SourceChainJson=row.Value<string>("source_chain_json")??operation.SourceChainJson;operation.ResultJson=row.Value<string>("result_json")??operation.ResultJson;operation.IsExposed=row.Value<int?>("is_exposed")==1;operation.Revision=row.Value<long?>("revision")??operation.Revision;
        }

        private void UpsertServerObligation(JObject row)
        {
            string id=row.Value<string>("obligation_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            CourtObligation obligation=_obligations.FirstOrDefault(x=>x.ObligationId==id);if(obligation==null){obligation=new CourtObligation{ObligationId=id};_obligations.Add(obligation);}
            obligation.ObligationType=row.Value<string>("obligation_type")??obligation.ObligationType;obligation.OwedByHeroStringId=row.Value<string>("owed_by")??obligation.OwedByHeroStringId;
            obligation.OwedToHeroStringId=row.Value<string>("owed_to")??obligation.OwedToHeroStringId;obligation.Description=row.Value<string>("description")??obligation.Description;
            obligation.CreatedDay=row.Value<float?>("created_day")??obligation.CreatedDay;obligation.DueDay=row.Value<float?>("due_day")??obligation.DueDay;
            obligation.TermsHash=row.Value<string>("terms_hash")??obligation.TermsHash;obligation.BreachRuleJson=row.Value<string>("breach_rule_json")??obligation.BreachRuleJson;
            obligation.Status=row.Value<string>("status")??obligation.Status;obligation.ResolutionJson=row.Value<string>("resolution_json")??obligation.ResolutionJson;obligation.Revision=row.Value<long?>("revision")??obligation.Revision;
            try{JObject payload=JObject.Parse(row.Value<string>("payload_json")??"{}");obligation.TermsJson=JsonText(payload["terms"],obligation.TermsJson);}catch{}
        }

        private void UpsertServerPlot(JObject row)
        {
            string id=row.Value<string>("plot_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            CourtPlot plot=_plots.FirstOrDefault(x=>x.PlotId==id);if(plot==null){plot=new CourtPlot{PlotId=id};_plots.Add(plot);}
            plot.PlotType=row.Value<string>("plot_type")??plot.PlotType;plot.DirectorHeroStringId=row.Value<string>("director_id")??plot.DirectorHeroStringId;plot.TargetStringId=row.Value<string>("target_id")??plot.TargetStringId;
            plot.ParticipantHeroIdsCsv=JsonIds(row["participants_json"]);plot.Status=row.Value<string>("status")??plot.Status;plot.CreatedDay=row.Value<float?>("created_day")??plot.CreatedDay;plot.DueDay=row.Value<float?>("due_day")??plot.DueDay;
            plot.Progress=row.Value<float?>("progress")??plot.Progress;plot.TermsHash=row.Value<string>("terms_hash")??plot.TermsHash;plot.SecretKnowledgeId=row.Value<string>("secret_knowledge_id")??plot.SecretKnowledgeId;plot.IsDiscoveredByPlayer=true;plot.Revision=row.Value<long?>("revision")??plot.Revision;
        }

        private static ReignIntelligenceOperationState ParseOperationState(string value)
        {
            foreach(ReignIntelligenceOperationState state in Enum.GetValues(typeof(ReignIntelligenceOperationState)))if(string.Equals(state.ToString(),value,StringComparison.OrdinalIgnoreCase))return state;
            return ReignIntelligenceOperationState.Planned;
        }

        private static string JsonText(JToken token, string fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback ?? "{}";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? fallback ?? "{}";
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string JsonIds(JToken token)
        {
            string text = JsonText(token, "[]");
            try { return string.Join(",", JArray.Parse(text).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x))); }
            catch { return string.Empty; }
        }

        private static ReignCourtMatterKind ParseMatterKind(string value)
        {
            string normalized = (value ?? string.Empty).Replace("_", string.Empty);
            foreach (ReignCourtMatterKind kind in Enum.GetValues(typeof(ReignCourtMatterKind)))
                if (string.Equals(kind.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) return kind;
            return ReignCourtMatterKind.Petition;
        }

        private static ReignCourtMatterState ParseMatterState(string value)
        {
            string normalized = (value ?? string.Empty).Replace("_", string.Empty);
            foreach (ReignCourtMatterState state in Enum.GetValues(typeof(ReignCourtMatterState)))
                if (string.Equals(state.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) return state;
            return ReignCourtMatterState.Queued;
        }

        private JObject BuildNativeSnapshot()
        {
            return new JObject
            {
                ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                ["playerInfluence"] = Clan.PlayerClan?.Influence ?? 0f,
                ["playerRenown"] = Clan.PlayerClan?.Renown ?? 0f,
                ["militaryStrength"] = _session?.Authority == ReignCourtAuthority.Royal ? Clan.PlayerClan?.Kingdom?.CurrentTotalStrength ?? 0f : Clan.PlayerClan?.CurrentTotalStrength ?? 0f,
                ["strategicSupply"] = _session == null ? 0 : ReignCourtSupplyService.GetStrategicSupplyUnits(_session.Authority),
                ["hostUnderSiege"] = Settlement.All.FirstOrDefault(x => x.StringId == _session?.HostSettlementStringId)?.IsUnderSiege == true
            };
        }

        private static JObject ToServerMatter(CourtMatter matter)
        {
            JArray options;
            try { options = JArray.Parse(matter.DecisionOptionsJson ?? "[]"); } catch { options = new JArray(); }
            JObject defaultOutcome;
            try { defaultOutcome = JObject.Parse(matter.DefaultOutcomeJson ?? "{}"); } catch { defaultOutcome = new JObject(); }
            return new JObject
            {
                ["matterId"] = matter.MatterId, ["sourceKey"] = matter.SourceKey, ["kind"] = matter.Kind.ToString().ToLowerInvariant(),
                ["title"] = matter.Title, ["summary"] = matter.Summary, ["confidentiality"] = matter.Confidentiality,
                ["participants"] = new JArray((matter.ParticipantHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)),
                ["witnesses"] = new JArray((matter.WitnessHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)),
                ["settlementStringId"] = matter.SettlementStringId, ["kingdomStringId"] = matter.KingdomStringId,
                ["createdDay"] = matter.CreatedDay, ["dueDay"] = matter.DueDay, ["priority"] = matter.Priority,
                ["isCritical"] = matter.IsCritical, ["isMajorRealmEvent"] = matter.IsMajorRealmEvent, ["isAmbientRoleplay"] = matter.IsAmbientRoleplay,
                ["options"] = options, ["defaultOutcome"] = defaultOutcome, ["requiresServer"] = matter.RequiresServer
            };
        }
    }
}
