using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Rebellion.Integration
{
    public sealed class ReignRebellionEvaluationResult
    {
        public bool Ok;
        public string Error = string.Empty;
        public readonly List<JObject> Updates = new List<JObject>();
    }

    internal static class ReignRebellionServerClient
    {
        internal static Task<JObject> RequestEffectiveAttitudesAsync(JObject payload, RequestFence fence) =>
            PostJsonAsync("/relationships/effective/batch", payload, () => ReignMainThread.InvokeAsync(fence.IsCurrent));

        internal static async Task<ReignRebellionEvaluationResult> EvaluateRebellionsAsync(
            IReadOnlyList<ReignRebellionMovementRecord> movements,
            IReadOnlyList<ReignRebellionMembershipRecord> memberships, string generation, Func<bool> requestOwner)
        {
            ReignRebellionEvaluationResult result = new ReignRebellionEvaluationResult();
            try
            {
                JObject payload = BuildRebellionSnapshot(movements, memberships);
                payload["sharedActivityGeneration"] = generation;
                JObject response = await PostOwnedJsonAsync("/rebellions/evaluate", payload, requestOwner).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = ReadString(response, "error", string.Empty);
                if (result.Ok && response["updates"] is JArray updates && updates.Count > 0)
                {
                    var owner = response["ownership"] as JObject;
                    if (!MatchesRebellionEvaluationOwner(owner, payload))
                    {
                        result.Ok = false;
                        result.Error = "The rebellion evaluation did not confirm the requested save owner.";
                        return result;
                    }
                    result.Updates.AddRange(updates.OfType<JObject>());
                }
            }
            catch (ReignRequestOwnerAbandonedException) { }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Rebellion evaluation failed: " + ex.Message);
            }

            return result;
        }

        private static bool MatchesRebellionEvaluationOwner(JObject owner, JObject payload)
            => owner != null
                && JToken.DeepEquals(owner["campaignId"], payload["campaignId"])
                && JToken.DeepEquals(owner["timelineId"], payload["timelineId"])
                && JToken.DeepEquals(owner["sharedActivityGeneration"], payload["sharedActivityGeneration"]);

        public static Task<JObject> CreateNegotiationDraftAsync(JObject draft)
        {
            JObject payload = draft == null ? new JObject() : (JObject)draft.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main";
            payload["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
            return PostJsonAsync("/negotiations/draft", payload);
        }

        public static Task<JObject> RespondToNegotiationAsync(string negotiationId, Hero ruler, Kingdom kingdom, bool approve, string reason)
        {
            return PostJsonAsync("/negotiations/respond", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["negotiationId"] = negotiationId ?? string.Empty,
                ["rulerHeroId"] = ruler?.StringId ?? string.Empty,
                ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                ["response"] = approve ? "approve" : "reject",
                ["reason"] = reason ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
            });
        }

        public static Task<JObject> GetNegotiationsAsync(string negotiationId = "")
        {
            string route = "/negotiations/query?campaignId=" + Uri.EscapeDataString(GetCampaignId())
                + "&worldDay=" + Uri.EscapeDataString((TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(negotiationId)) route += "&negotiationId=" + Uri.EscapeDataString(negotiationId);
            return GetJsonAsync(route);
        }

        internal static async Task<ReignLetterSendResult> SendRebellionSystemLetterAsync(
            Hero sender, Hero recipient, string body, string reason,
            float deliveryDay, string plotId)
        {
            ReignLetterSendResult result = new ReignLetterSendResult();
            try
            {
                if (sender == null || recipient == null)
                {
                    result.Error = "A sender and recipient are required for a rebellion letter.";
                    return result;
                }
                double dispatchDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
                JObject response = await PostJsonAsync("/correspondence/send", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                    ["senderId"] = sender.StringId ?? string.Empty,
                    ["senderName"] = sender.Name?.ToString() ?? "Unknown sender",
                    ["recipientId"] = recipient.StringId ?? string.Empty,
                    ["recipientName"] = recipient.Name?.ToString() ?? "Unknown recipient",
                    ["body"] = body ?? string.Empty,
                    ["source"] = "rebellion_preparation",
                    ["reason"] = reason ?? string.Empty,
                    ["dispatchDay"] = dispatchDay,
                    ["deliveryDay"] = Math.Max(dispatchDay, deliveryDay),
                    ["originId"] = sender.CurrentSettlement?.StringId ?? string.Empty,
                    ["destinationId"] = recipient.CurrentSettlement?.StringId ?? string.Empty,
                    ["plotId"] = plotId ?? string.Empty
                }).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.LetterId = response.Value<string>("letterId") ?? string.Empty;
                result.ThreadId = response.Value<string>("threadId") ?? string.Empty;
                result.DispatchDay = response.Value<double?>("dispatchDay") ?? dispatchDay;
                result.DeliveryDay = response.Value<double?>("deliveryDay") ?? deliveryDay;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Rebellion system letter failed: " + ex.Message);
            }
            return result;
        }

        private static JObject BuildRebellionSnapshot(IReadOnlyList<ReignRebellionMovementRecord> movements,
            IReadOnlyList<ReignRebellionMembershipRecord> memberships)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            JArray kingdomRows = new JArray();
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated && x.RulingClan?.Leader != null))
            {
                JArray clans = new JArray();
                ReignRebellionMovementRecord civilMovement = (movements ?? Array.Empty<ReignRebellionMovementRecord>())
                    .FirstOrDefault(x => x != null && string.Equals(x.ParentKingdomStringId, kingdom.StringId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Stage, "civil_war", StringComparison.OrdinalIgnoreCase));
                Kingdom civilRebels = string.IsNullOrWhiteSpace(civilMovement?.RebelKingdomStringId)
                    ? null : Kingdom.All.FirstOrDefault(x => string.Equals(x.StringId, civilMovement.RebelKingdomStringId, StringComparison.OrdinalIgnoreCase));
                IEnumerable<Clan> snapshotClans = kingdom.Clans.Where(x => x?.Leader != null);
                if (civilRebels != null) snapshotClans = snapshotClans.Concat(civilRebels.Clans.Where(x => x?.Leader != null)).Distinct();
                List<Clan> snapshotClanList = snapshotClans.ToList();
                foreach (Clan clan in snapshotClanList)
                {
                    Hero leader = clan.Leader;
                    JObject relationsToLeaders = new JObject();
                    foreach (Clan other in snapshotClanList.Where(x => x?.Leader != null)) relationsToLeaders[other.Leader.StringId] = leader.GetRelation(other.Leader);
                    clans.Add(new JObject
                    {
                        ["clanId"] = clan.StringId ?? string.Empty,
                        ["leaderHeroId"] = leader.StringId ?? string.Empty,
                        ["leaderAlive"] = !leader.IsDead,
                        ["leaderAge"] = leader.Age,
                        ["leaderIsPrisoner"] = leader.IsPrisoner,
                        ["isPrisoner"] = leader.IsPrisoner,
                        ["unsafeForRebellion"] = leader.CurrentSettlement != null && leader.CurrentSettlement.MapFaction != kingdom,
                        ["isRulingClan"] = clan == kingdom.RulingClan,
                        ["isPlayerClan"] = clan == Clan.PlayerClan,
                        ["isMinorFaction"] = clan.IsMinorFaction,
                        ["isMercenary"] = clan.IsUnderMercenaryService || clan.IsClanTypeMercenary,
                        ["isEliminated"] = clan.IsEliminated,
                        ["tier"] = clan.Tier,
                        ["influence"] = clan.Influence,
                        ["power"] = clan.CurrentTotalStrength,
                        ["fortificationCount"] = clan.Fiefs.Count,
                        ["relationToRuler"] = leader.GetRelation(kingdom.Leader),
                        ["relationsToLeaders"] = relationsToLeaders,
                        ["traits"] = BuildTraitProfile(leader),
                        ["redressSettlementId"] = SelectRedressSettlementId(clan, kingdom),
                        ["redressPolicyId"] = string.Empty
                    });
                }

                kingdomRows.Add(new JObject
                {
                    ["kingdomId"] = kingdom.StringId ?? string.Empty,
                    ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                    ["rulerHeroId"] = kingdom.Leader?.StringId ?? string.Empty,
                    ["rulerName"] = kingdom.Leader?.Name?.ToString() ?? string.Empty,
                    ["isEliminated"] = kingdom.IsEliminated,
                    ["isPlayerKingdom"] = kingdom == playerKingdom,
                    ["isRebelRealm"] = ReignServices.Rebellion?.IsRebelKingdom(kingdom) == true,
                    ["fortificationCount"] = kingdom.Fiefs.Count,
                    ["strength"] = kingdom.CurrentTotalStrength,
                    ["enemies"] = new JArray(Kingdom.All.Where(x => x != null && !x.IsEliminated && kingdom.IsAtWarWith(x)).Select(x => x.StringId)),
                    ["totalClanPower"] = snapshotClanList.Sum(x => Math.Max(0f, x.CurrentTotalStrength)),
                    ["clans"] = clans
                });
            }

            JArray movementRows = new JArray();
            foreach (ReignRebellionMovementRecord movement in movements ?? Array.Empty<ReignRebellionMovementRecord>())
            {
                if (movement == null) continue;
                JArray memberRows = new JArray((memberships ?? Array.Empty<ReignRebellionMembershipRecord>())
                    .Where(x => x != null && string.Equals(x.MovementId, movement.MovementId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new JObject
                    {
                        ["clanId"] = x.ClanStringId ?? string.Empty, ["leaderHeroId"] = x.LeaderHeroStringId ?? string.Empty,
                        ["side"] = x.Side ?? "loyalist", ["supportScore"] = x.SupportScore, ["switchCount"] = x.SwitchCount,
                        ["lastSwitchDay"] = x.LastSwitchDay, ["playerChoiceRequired"] = x.PlayerChoiceRequired
                    }));
                movementRows.Add(new JObject
                {
                    ["movementId"] = movement.MovementId, ["parentKingdomId"] = movement.ParentKingdomStringId,
                    ["rebelKingdomId"] = movement.RebelKingdomStringId, ["leaderClanId"] = movement.LeaderClanStringId,
                    ["leaderHeroId"] = movement.LeaderHeroStringId, ["rulerHeroId"] = movement.OriginalRulerHeroStringId,
                    ["objective"] = movement.Objective, ["stage"] = movement.Stage, ["pressure"] = movement.Pressure,
                    ["relationshipPressure"] = movement.RelationshipPressure, ["traitPressure"] = movement.TraitPressure,
                    ["factualPressure"] = movement.FactualPressure, ["viability"] = movement.Viability, ["readiness"] = movement.Readiness,
                    ["demand"] = ParseRebellionJson(movement.DemandJson),
                    ["createdDay"] = movement.CreatedDay, ["updatedDay"] = movement.UpdatedDay,
                    ["civilWarStartedDay"] = movement.CivilWarStartedDay, ["rebelBattleScore"] = movement.RebelBattleScore,
                    ["originalParentStrongholdIds"] = movement.OriginalParentStrongholdIdsCsv,
                    ["originalRebelStrongholdIds"] = movement.OriginalRebelStrongholdIdsCsv,
                    ["correlationId"] = movement.CorrelationId, ["cooldownUntilDay"] = movement.CooldownUntilDay,
                    ["resolution"] = movement.Resolution, ["memberships"] = memberRows
                    , ["backingStatus"] = movement.BackingStatus ?? string.Empty
                    , ["backingAskedKingdomId"] = movement.BackingAskedKingdomStringId ?? string.Empty
                    , ["backingSponsorKingdomId"] = movement.BackingSponsorKingdomStringId ?? string.Empty
                    , ["backingRequestDay"] = movement.BackingRequestDay
                    , ["backingActionId"] = movement.BackingActionId ?? string.Empty
                    , ["backingTerminalReason"] = movement.BackingTerminalReason ?? string.Empty
                });
            }

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["campaignStartDay"] = ReignCampaignPreparationCampaignBehavior.AutonomousWorldOriginDay,
                ["playerKingdomId"] = playerKingdom?.StringId ?? string.Empty,
                ["kingdoms"] = kingdomRows,
                ["movements"] = movementRows
            };
        }

        private static string SelectRedressSettlementId(Clan clan, Kingdom kingdom)
        {
            if (clan == null || kingdom == null || clan.Tier < 3) return string.Empty;
            Town leastEstablished = kingdom.Fiefs.Where(x => x?.OwnerClan == kingdom.RulingClan)
                .OrderBy(x => x.Prosperity).ThenBy(x => x.StringId).FirstOrDefault();
            if (leastEstablished == null) leastEstablished = kingdom.Fiefs.Where(x => x?.OwnerClan != clan)
                .OrderBy(x => x.Prosperity).ThenBy(x => x.StringId).FirstOrDefault();
            return leastEstablished?.Settlement?.StringId ?? string.Empty;
        }

        private static JObject ParseRebellionJson(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json); }
            catch { return new JObject(); }
        }
    }
}
