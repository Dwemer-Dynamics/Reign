using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Government.Campaign;
using ReignBeta.Government.Domain;
using ReignBeta.Persistence.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Government.Testing
{
    internal static partial class ReignGovernmentTestControls
    {
        private const string ConstructionTestTemplate = "R083";

        private static JObject GovernmentConstructionTest(ReignGovernmentCampaignBehavior government,
            string runId, JObject options, string gameInstanceId)
        {
            if (runId.Length > 80 || runId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("A bounded construction fixture ID is required.");
            string step = options.Value<string>("constructionStep");
            var player = Hero.MainHero;
            var kingdom = Clan.PlayerClan?.Kingdom;
            var settlement = MobileParty.MainParty?.CurrentSettlement;
            var town = settlement?.Town;
            if (step != "prepare" && step != "observe" && step != "manual" && step != "automatic"
                || player == null || !player.IsAlive || player.IsPrisoner || kingdom?.Leader != player
                || town == null || settlement.OwnerClan != Clan.PlayerClan || settlement.MapFaction != kingdom
                || settlement.IsUnderSiege || settlement.Party?.MapEvent != null || Mission.Current != null
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || !ReignGovernmentConstructionNativeObservation.VerifyReady()
                || !ReignGovernmentConstructionNativeObservation.TryCaptureOwner(out var owner)
                || !ReferenceEquals(owner.Behavior, government)
                || owner.TimelineId != options.Value<string>("expectedConstructionTimeline"))
                throw new InvalidOperationException("Construction needs the paused, aligned ruler in a safe player-owned town and the exact loaded stock hooks.");

            string resolutionId = "government_construction_test_" + runId;
            var records = government._resolutions.Where(row => row.ResolutionId == resolutionId).Take(2).ToList();
            if (records.Count > 1 || records.Any(row => row.TemplateId != ConstructionTestTemplate
                || row.KingdomStringId != kingdom.StringId || row.TargetSettlementStringId != settlement.StringId))
                throw new InvalidOperationException("The construction fixture identity is ambiguous or its scope changed.");
            var record = records.SingleOrDefault();
            var queue = town.BuildingsInProgress;
            if (step == "prepare" && record == null)
            {
                if (government._resolutions.Any(row => row.TemplateId == ConstructionTestTemplate
                    && row.TargetSettlementStringId == settlement.StringId && row.Status == "active"))
                    throw new InvalidOperationException("The town already has an active construction resolution.");
                var project = town.Buildings.Select(item => item.BuildingType.StringId).Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => ReignGovernmentConstructionNativeObservation.AvailableProject(
                        town, kingdom, settlement.StringId, id, true)).FirstOrDefault(item => item != null);
                if (project == null)
                    throw new InvalidOperationException("The town needs an unfinished native queue head and a distinct unfinished project.");

                var route = ReignGovernmentResolutionCatalog.Find(ConstructionTestTemplate).FirstRoute;
                double day = CampaignTime.Now.ToDays;
                var terms = new ReignGovernmentConstructionTerms(resolutionId, owner.CampaignId, owner.TimelineId,
                    kingdom.StringId, settlement.StringId, queue.Peek().BuildingType.StringId, project.BuildingType.StringId,
                    project.CurrentLevel, BuildingType.MaxLevel, day, day + route.DeadlineDays);
                if (!terms.IsValid)
                    throw new InvalidOperationException("The construction fixture could not freeze valid terms.");
                record = new ReignGovernmentResolutionRecord
                {
                    ResolutionId = resolutionId, TemplateId = ConstructionTestTemplate, KingdomStringId = kingdom.StringId,
                    TargetSettlementStringId = settlement.StringId, SelectedRoute = 0, RouteActionValue = (int)route.Action,
                    RequiredAmount = route.Target, Status = "active", Revision = 1,
                    ProposedDay = (float)day, AcceptedDay = (float)day, DueDay = (float)terms.DueDay,
                    EvidenceJson = new JObject { ["construction"] = new JObject
                    {
                        ["schemaVersion"] = 1, ["settlementId"] = settlement.StringId, ["projectId"] = terms.ProjectId,
                        ["projectName"] = project.Name.ToString(), ["acceptedTerms"] = terms.ToJson()
                    } }.ToString(Formatting.None)
                };
                if (ReignGovernmentCampaignBehavior.GetAcceptedConstructionTerms(record)?.Matches(terms) != true)
                    throw new InvalidOperationException("Production rejected the fixed construction terms.");
                government._resolutions.Add(record);
            }

            if (record == null)
                throw new InvalidOperationException("Prepare the named construction fixture first.");
            var offer = (JObject)JObject.Parse(record.EvidenceJson)["construction"];
            var accepted = ReignGovernmentCampaignBehavior.GetAcceptedConstructionTerms(record);
            if (accepted == null)
                throw new InvalidOperationException("The loaded fixture has no valid original construction terms.");
            var target = town.Buildings.Single(item => item.BuildingType.StringId == accepted.ProjectId);
            bool applied = false;
            if (step == "manual" || step == "automatic")
            {
                var active = government.GetActiveConstructionObservationTerms(town, kingdom, owner);
                if (active == null || !active.IsWithinWindow(CampaignTime.Now.ToDays)
                    || target.CurrentLevel != active.LevelAtAcceptance || queue.Count == 0
                    || queue.Peek().BuildingType.StringId != active.PreviousProjectId)
                    throw new InvalidOperationException("The original pending terms or queue head changed. Restore the owned checkpoint before retrying.");
                if (step == "manual")
                {
                    BuildingHelper.ChangeCurrentBuildingQueue(new List<Building> { target }, town);
                }
                else
                {
                    int cost = target.GetConstructionCost();
                    if (cost <= 0)
                        throw new InvalidOperationException("The native building has no positive construction cost.");
                    // Staging the queue directly must not count as a production observation.
                    queue.Clear();
                    queue.Enqueue(target);
                    target.BuildingProgress = cost + 1f;
                    if (government.GetActiveConstructionObservationTerms(town, kingdom, owner)?.Matches(active) != true)
                        throw new InvalidOperationException("The staged queue changed the pending resolution before the stock completion call.");
                    BuildingHelper.CheckIfBuildingIsComplete(target);
                }
                offer = (JObject)JObject.Parse(record.EvidenceJson)["construction"];
                string required = step == "manual" ? ReignGovernmentConstructionObservation.PriorityChanged
                    : ReignGovernmentConstructionObservation.LevelCompleted;
                applied = ReignGovernmentConstructionObservation.Read(offer["observation"], active)?.Transition == required;
            }

            return new JObject
            {
                ["ok"] = step == "prepare" || step == "observe" || applied,
                ["schema"] = "reign-government-construction-native-v1", ["step"] = step,
                ["fixtureId"] = runId, ["gameInstanceId"] = gameInstanceId, ["campaignId"] = owner.CampaignId,
                ["timelineId"] = owner.TimelineId, ["day"] = CampaignTime.Now.ToDays, ["status"] = record.Status,
                ["revision"] = record.Revision, ["construction"] = offer,
                ["projectLevel"] = target.CurrentLevel, ["queueHead"] = queue.FirstOrDefault()?.BuildingType?.StringId,
                ["loyalty"] = town.Loyalty, ["prosperity"] = town.Prosperity, ["security"] = town.Security
            };
        }
    }
}
