using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private readonly NpcMeetingMovementEvidence _meetingMovement = new NpcMeetingMovementEvidence();

        internal void ObserveMeetingMovement(Hero hero, string operation, string target = "", string detail = "", bool allowed = true)
        {
            if (!ReignLiveInteractionTestHost.OwnsAutomationSession || hero == null || hero == Hero.MainHero
                || !_meetingTravel.Values.Any(j => !j.Terminal && (j.Meeting.IssuerId == hero.StringId || j.Meeting.TargetId == hero.StringId))) return;
            // Method identities only: no source paths, local values or arbitrary object dumps.
            var callers = (new StackTrace(2, false).GetFrames() ?? new StackFrame[0])
                .Select(f => f.GetMethod()).Where(m => m != null)
                .Select(m => (m.DeclaringType?.FullName ?? "") + "." + m.Name);
            _meetingMovement.Add(hero.StringId, CampaignTime.Now.ToDays, operation, hero.HeroState.ToString(),
                hero.CurrentSettlement?.StringId ?? "", hero.PartyBelongedTo?.StringId ?? "", target, detail,
                IsMeetingHeld(hero.StringId), allowed, callers);
        }

        private void OnMeetingHeroGetsBusy(Hero hero, HeroGetsBusyReasons reason) =>
            ObserveMeetingMovement(hero, "hero_gets_busy", detail: reason.ToString());

        private void OnMeetingTeleportRequested(Hero hero, Settlement settlement, MobileParty party, TeleportHeroAction.TeleportationDetail detail) =>
            ObserveMeetingMovement(hero, "teleport_requested", settlement?.StringId ?? party?.StringId ?? "", detail.ToString());

        private JObject MeetingMovementSnapshot(Hero hero)
        {
            var rows = _meetingMovement.ForHero(hero.StringId);
            return new JObject { ["schema"] = "reign-meeting-native-movement-evidence-v1",
                ["origin"] = "read_only_operator_observation", ["records"] = JArray.FromObject(rows),
                ["retainedForHero"] = _meetingMovement.CountForHero(hero.StringId), ["limit"] = NpcMeetingMovementEvidence.HeroLimit,
                ["truncated"] = _meetingMovement.CountForHero(hero.StringId) > rows.Count };
        }

        private static JObject NativeMeetingTeleportSnapshot(Hero hero)
        {
            var native = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<TeleportationCampaignBehavior>();
            if (native == null) return new JObject { ["available"] = false };
            bool found = native.GetTargetOfTeleportingHero(hero, out bool governor, out bool leader, out var target);
            double arrival = native.GetHeroArrivalTimeToDestination(hero).ToDays;
            return new JObject { ["available"] = true, ["pending"] = found,
                ["governorSettlementId"] = hero.GovernorOf?.Settlement?.StringId ?? "",
                ["deferredForMeeting"] = Instance?.HasOwnedMeetingTeleportDeferral(hero.StringId) == true,
                ["targetKind"] = target is Settlement ? "settlement" : target is MobileParty ? "party" : "",
                ["targetId"] = (target as Settlement)?.StringId ?? (target as MobileParty)?.StringId ?? "",
                ["arrivalDay"] = found && !double.IsNaN(arrival) && !double.IsInfinity(arrival) ? (JToken)new JValue(arrival) : JValue.CreateNull(),
                ["governor"] = governor, ["partyLeader"] = leader };
        }
    }

    internal static class NpcMeetingCharacterEntryObservationPatch
    {
        // Observation only. Unexpected displacement must still interrupt truthfully.
        private static void Prefix(Hero __0, Settlement __1) =>
            ReignNpcLivesBehavior.Instance?.ObserveMeetingMovement(__0, "enter_settlement", __1?.StringId ?? "");
    }
}
