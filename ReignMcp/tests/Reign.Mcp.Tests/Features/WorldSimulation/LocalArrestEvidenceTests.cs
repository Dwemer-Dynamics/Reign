using Newtonsoft.Json;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.World;

namespace Reign.Mcp.Tests
{
    public sealed class LocalArrestEvidenceTests
    {
        [Theory]
        [InlineData("exact", true)]
        [InlineData("unscoped", false)]
        [InlineData("wrong-campaign", false)]
        [InlineData("wrong-timeline", false)]
        [InlineData("foreign-ruler", false)]
        [InlineData("successor", false)]
        [InlineData("joined-leader", false)]
        [InlineData("new-movement-leader", false)]
        [InlineData("founder-after-handoff", true)]
        [InlineData("secret-plot", false)]
        [InlineData("resolved", false)]
        [InlineData("missing-realm", false)]
        public void RebellionRequiresTheExactPublicFounderAndOriginalChargingRuler(string changed, bool supported)
        {
            var movement = new ReignRebellionMovementRecord
            {
                MovementId = "reb_movement", PublicOutbreakCampaignId = "campaign", PublicOutbreakTimelineId = "timeline",
                ParentKingdomStringId = "parent", OriginalRulerHeroStringId = "ruler", LeaderHeroStringId = "founder",
                PublicOutbreakFounderHeroId = "founder", PublicOutbreakRulerHeroId = "ruler",
                RebelKingdomStringId = "rebels", CivilWarStartedDay = 12, Stage = "civil_war"
            };
            string campaign = "campaign", timeline = "timeline", parent = "parent", ruler = "ruler", accused = "founder";
            switch (changed)
            {
                case "unscoped": movement.PublicOutbreakTimelineId = string.Empty; break;
                case "wrong-campaign": campaign = "campaign2"; break;
                case "wrong-timeline": timeline = "fork"; break;
                case "foreign-ruler": parent = "foreign"; break;
                case "successor": ruler = "successor"; break;
                case "joined-leader": accused = "joined_clan_leader"; break;
                case "new-movement-leader": movement.LeaderHeroStringId = accused = "new_leader"; break;
                case "founder-after-handoff": movement.LeaderHeroStringId = "new_leader"; break;
                case "secret-plot": movement.Stage = "grievance"; break;
                case "resolved": movement.ResolutionApplied = true; break;
                case "missing-realm": movement.RebelKingdomStringId = string.Empty; break;
            }
            Assert.Equal(supported ? "rebellion_founder:reb_movement" : string.Empty,
                movement.PublicRebellionArrestEvidenceId(campaign, timeline, parent, ruler, accused));
        }

        [Theory]
        [InlineData("exact", true)]
        [InlineData("json-round-trip", true)]
        [InlineData("unscoped", false)]
        [InlineData("wrong-campaign", false)]
        [InlineData("wrong-timeline", false)]
        [InlineData("legacy-assignment", false)]
        [InlineData("new-assignment-for-hero", false)]
        [InlineData("sponsor-ruler-accused", false)]
        [InlineData("wrong-sponsor", false)]
        [InlineData("other-realm", false)]
        [InlineData("wrong-assignment-settlement", false)]
        [InlineData("hidden", false)]
        [InlineData("awaiting-exposure-receipt", false)]
        [InlineData("undetected", false)]
        [InlineData("unattributed", false)]
        [InlineData("no-harm", false)]
        [InlineData("unrelated-effect", false)]
        public void SabotageRequiresAnAttributedHarmfulActionFromTheExactExposedAssignment(string changed, bool supported)
        {
            var agent = new ReignForeignAgentRecord
            {
                AgentHeroStringId = "agent", SponsorKingdomStringId = "sponsor", Exposed = true,
                AssignmentId = "assignment", AssignmentTimelineId = "timeline", SettlementStringId = "town"
            };
            var action = new ReignForeignAgentActionRecord
            {
                AgentHeroStringId = "agent", SponsorKingdomStringId = "sponsor", Detected = true, SponsorAttributed = true,
                AssignmentId = "assignment", AssignmentCampaignId = "campaign", AssignmentTimelineId = "timeline",
                TargetKingdomStringId = "victim_realm", SettlementStringId = "town", ActionId = "disruption",
                Magnitude = -1f, EffectType = "security"
            };
            string campaign = "campaign", timeline = "timeline", target = "victim_realm", accused = "agent";
            switch (changed)
            {
                case "json-round-trip":
                    agent = JsonConvert.DeserializeObject<ReignForeignAgentRecord>(JsonConvert.SerializeObject(agent))!;
                    action = JsonConvert.DeserializeObject<ReignForeignAgentActionRecord>(JsonConvert.SerializeObject(action))!;
                    break;
                case "unscoped": action.AssignmentCampaignId = string.Empty; break;
                case "wrong-campaign": campaign = "campaign2"; break;
                case "wrong-timeline": timeline = "fork"; break;
                case "legacy-assignment": action.AssignmentId = string.Empty; break;
                case "new-assignment-for-hero": agent.AssignmentId = "replacement"; break;
                case "sponsor-ruler-accused": accused = "sponsor_ruler"; break;
                case "wrong-sponsor": agent.SponsorKingdomStringId = "another_sponsor"; break;
                case "other-realm": target = "other"; break;
                case "wrong-assignment-settlement": agent.SettlementStringId = "other_town"; break;
                case "hidden": agent.Exposed = false; break;
                case "awaiting-exposure-receipt": agent.ExposurePending = true; break;
                case "undetected": action.Detected = false; break;
                case "unattributed": action.SponsorAttributed = false; break;
                case "no-harm": action.Magnitude = 0f; break;
                case "unrelated-effect": action.EffectType = "rumor"; break;
            }
            Assert.Equal(supported ? "foreign_agent_sabotage:disruption" : string.Empty,
                action.PublicSabotageArrestEvidenceId(campaign, timeline, target, accused, agent));
        }
    }
}

namespace TaleWorlds.SaveSystem
{
    // The engine owns serialization. These tests execute the saved record's evidence predicate.
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class SaveableFieldAttribute : Attribute
    {
        public SaveableFieldAttribute(int id) { }
    }
}
