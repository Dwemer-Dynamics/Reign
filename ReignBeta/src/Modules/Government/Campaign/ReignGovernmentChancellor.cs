using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private readonly Dictionary<string, string> _crownDecisionsInFlight = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool IsActiveGovernmentChancellor(string kingdomId, string heroId)
        {
#if REIGN_EXCLUDE_COURT
            return false;
#else
            Kingdom kingdom = FindKingdom(kingdomId);
            var office = ReignCourtCampaignBehavior.Instance?.Chancellor;
            Hero chancellor = FindHero(heroId);
            // This is the player's existing salaried office, not an invented NPC office
            // or emergency regency. Government delegation was approved only while Active.
            return kingdom != null && kingdom.Leader == Hero.MainHero && office != null
                && office.State == ReignChancellorOfficeState.Active && Same(office.HeroId, heroId)
                && chancellor != null && chancellor.IsAlive && !chancellor.IsPrisoner
                && chancellor.CurrentSettlement != null && chancellor.CurrentSettlement == GovernmentCapital(kingdom)
                && GovernmentAttendanceBlock(chancellor, false).Length == 0;
#endif
        }

        private static string GovernmentChancellorId()
        {
#if REIGN_EXCLUDE_COURT
            return null;
#else
            return ReignCourtCampaignBehavior.Instance?.Chancellor?.HeroId;
#endif
        }

        public string GetGovernmentCrownRepresentation(string businessId)
        {
            var record = _business.FirstOrDefault(b => Same(b.BusinessId, businessId));
            if (record == null) return "No matter selected.";
            if (record.CrownVoteStatus == "pending") return "The Active Chancellor is considering the Crown's position.";
            if (record.CrownVoteSource == "chancellor")
                return (FindHero(record.CrownActorHeroId)?.Name?.ToString() ?? "The Chancellor") + " represented the absent ruler."
                    + (record.FrozenAuthorityLevel == 5 ? " This recommendation carries no vote." : " The Crown remains responsible for its vote.");
            if (record.CrownVoteStatus == "abstained") return "The Crown abstained. The government's votes retain their own shares.";
            if (IsGovernmentRulerPresent(businessId)) return "The ruler is present and may record the Crown's position.";
            string candidate = GovernmentChancellorId();
            return IsActiveGovernmentChancellor(record.KingdomStringId, candidate)
                ? "The Active Chancellor will represent the absent ruler."
                : "No Active Chancellor is available. The Crown will abstain; at sole rule the matter must await the ruler.";
        }

        partial void RequestGovernmentCrownDeputy(ReignGovernmentBusinessRecord record)
        {
            if (record == null || _crownDecisionsInFlight.ContainsKey(record.BusinessId)) return;
            string chancellorId = GovernmentChancellorId();
            if (!IsActiveGovernmentChancellor(record.KingdomStringId, chancellorId))
            {
                record.CrownVoteStatus = "abstained";
                record.CrownVoteSource = "unavailable";
                record.CrownOptionId = "";
                record.CrownActorHeroId = "";
                return;
            }
            Hero ruler = FindKingdom(record.KingdomStringId)?.Leader;
            Hero chancellor = FindHero(chancellorId);
            string timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string correlation = "government_crown_" + record.BusinessId + "_" + Guid.NewGuid().ToString("N");
            record.CrownVoteStatus = "pending";
            record.CrownActorHeroId = chancellorId;
            _crownDecisionsInFlight[record.BusinessId] = correlation;
            var payload = new JObject {
                ["correlationId"] = correlation, ["businessId"] = record.BusinessId,
                ["heroStringId"] = chancellorId, ["heroName"] = chancellor.Name.ToString(),
                ["playerHeroStringId"] = ruler.StringId, ["rulerName"] = ruler.Name.ToString(),
                ["kingdomId"] = record.KingdomStringId, ["officeState"] = "Active",
                ["authorityLevel"] = record.FrozenAuthorityLevel, ["worldDay"] = CurrentDay(),
                ["locationId"] = chancellor.CurrentSettlement?.StringId ?? "",
                ["title"] = record.Title, ["summary"] = record.Summary,
                ["options"] = GetPublicBusinessOptions(record), ["rulerRecommendation"] = record.RecommendedOptionId,
                ["sceneContext"] = "Government proceedings at " + chancellor.CurrentSettlement?.Name + "." };
            _ = CompleteGovernmentCrownDecisionAsync(record, ruler.StringId, chancellorId, timeline, payload);
        }

        private async Task CompleteGovernmentCrownDecisionAsync(ReignGovernmentBusinessRecord reserved,
            string rulerId, string chancellorId, string timeline, JObject payload)
        {
            JObject response = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { response = await ReignServerClient.RequestGovernmentCrownDecisionAsync(payload).ConfigureAwait(false); }
                catch { response = null; }
                if (response?.Value<bool?>("ok") == true) break;
                if (attempt < 2) await Task.Delay(1500 * (attempt + 1)).ConfigureAwait(false);
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                string requestId = payload.Value<string>("correlationId");
                if (!_crownDecisionsInFlight.TryGetValue(reserved.BusinessId, out string currentRequest)
                    || currentRequest != requestId) return false;
                _crownDecisionsInFlight.Remove(reserved.BusinessId);
                // The same business ID can survive reload; reference identity prevents
                // an old response crossing a load/timeline or office/ruler change.
                if (Instance != this || !_business.Any(b => ReferenceEquals(b, reserved))
                    || (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") != timeline
                    || GovernmentBusinessRules.IsClosed(reserved.Status)
                    || reserved.CrownVoteStatus != "pending"
                    || !Same(reserved.CrownActorHeroId, chancellorId)) return false;
                // A returning ruler supersedes a pending deputy request even before
                // choosing an option. Do not turn that return into a Crown abstention.
                if (IsGovernmentRulerPresent(reserved.BusinessId))
                {
                    reserved.CrownVoteStatus = "unrecorded";
                    reserved.CrownVoteSource = "";
                    reserved.CrownOptionId = "";
                    reserved.CrownActorHeroId = "";
                    ChangedBusiness(reserved);
                    return false;
                }
                bool valid = FindKingdom(reserved.KingdomStringId)?.Leader?.StringId == rulerId
                    && IsActiveGovernmentChancellor(reserved.KingdomStringId, chancellorId)
                    && !IsGovernmentRulerPresent(reserved.BusinessId);
                if (valid && response?.Value<bool?>("ok") == true
                    && RecordGovernmentCrownPosition(reserved.BusinessId, chancellorId, response.Value<string>("optionId"), "chancellor", out _))
                {
                    var transcript = JArray.Parse(reserved.TranscriptJson ?? "[]");
                    transcript.Add(new JObject { ["speakerHeroId"] = chancellorId,
                        ["speakerName"] = FindHero(chancellorId)?.Name?.ToString() ?? "Chancellor",
                        ["text"] = GovernmentBusinessRules.PublicText(response.Value<string>("publicReason")),
                        ["day"] = CurrentDay(), ["kind"] = "crown_position" });
                    reserved.TranscriptJson = transcript.ToString(Formatting.None);
                }
                else
                {
                    reserved.CrownVoteStatus = "abstained";
                    reserved.CrownVoteSource = "unavailable";
                    reserved.CrownOptionId = "";
                    reserved.CrownActorHeroId = "";
                    var transcript = JArray.Parse(reserved.TranscriptJson ?? "[]");
                    transcript.Add(new JObject { ["speakerName"] = "Clerk", ["day"] = CurrentDay(),
                        ["text"] = "No valid Crown position was received. The Crown abstains; the remaining votes retain their lawful shares." });
                    reserved.TranscriptJson = transcript.ToString(Formatting.None);
                }
                ChangedBusiness(reserved);
                TickGovernmentBusiness();
                return true;
            }).ConfigureAwait(false);
        }
    }
}
