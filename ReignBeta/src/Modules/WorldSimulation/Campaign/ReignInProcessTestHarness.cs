using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using ReignBeta.Characters.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Reputation.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Rulers;

namespace ReignBeta.WorldSimulation.Campaign
{
    // Test-harness side of IReignTestHarness: forwards to the live interaction host, the action
    // gauntlet, the harness-owned fixture controls and the social-balance harness; spymaster
    // controls come from the Spymaster module. Compiled into ReignBeta.Testing.dll; SubModule
    // finds, creates and installs it at load; product code never names these harness types directly.
    internal sealed class ReignInProcessTestHarness : IReignTestHarness
    {
        public void LiveInteractionApplicationTick(float dt) => ReignLiveInteractionTestHost.ApplicationTick(dt);
        public void ActionGauntletApplicationTick(float dt) => ReignActionGauntlet.ApplicationTick(dt);
        public void TickActionGauntletSuites() => ReignActionGauntlet.TickAllSuites();
        public bool IsLiveDialogueGauntletActive => ReignActionGauntlet.IsLiveDialogueGauntletActive;
        public bool OwnsAutomationSession => ReignLiveInteractionTestHost.OwnsAutomationSession;
        public bool OwnsNpcLivesSession => ReignLiveInteractionTestHost.OwnsNpcLivesSession;
        public bool OwnsPassiveWorldSession => ReignLiveInteractionTestHost.OwnsPassiveWorldSession;
        public string GameInstanceId => ReignLiveInteractionTestHost.GameInstanceId;
        public bool IsDisposablePartyAgencyFixtureConversationActive(Hero hero) =>
            ReignLiveInteractionTestHost.IsDisposablePartyAgencyFixtureConversationActive(hero);

        public void ApplyManipulationFixtureOpportunityEvidence(JObject opportunitySnapshot) =>
            ReignLiveInteractionTestHost.ApplyManipulationFixtureOpportunityEvidence(opportunitySnapshot);
        public void ApplyManipulationFixtureWealthEvidence(Hero subject, JObject wealth) =>
            ReignLiveInteractionTestHost.ApplyManipulationFixtureWealthEvidence(subject, wealth);
        public void ApplyNpcObservableWealthEvidence(Hero subject, JObject wealth) =>
            ReignLiveInteractionTestHost.ApplyNpcObservableWealthEvidence(subject, wealth);
        public void ApplyNpcObservableClanEvidence(Hero subject, JObject clan) =>
            ReignLiveInteractionTestHost.ApplyNpcObservableClanEvidence(subject, clan);

        public CampaignBehaviorBase CreateSocialBalanceHarnessBehavior() => new ReignSocialBalanceHarnessCampaignBehavior();
        public void RecordSocialBalanceOverride(string kind, JObject evidence) =>
            ReignServices.SocialBalanceHarness?.RecordOverride(kind, evidence);

        public bool HoldNpcLivesAnnouncementDelivery(ReignNpcLivesBehavior lives, JObject row) =>
            ReignNpcLivesTestControls.HoldAnnouncementDeliveryForNativeTest(lives, row);
        public Task<Action<string>> HoldNativeRelationshipResponse(ReignRelationshipCampaignBehavior relationship,
            Func<bool> owns, IReadOnlyList<ReignNativeRelationSyncTarget> targets, JArray rawTargets) =>
            ReignLiveInteractionTestHost.HoldNativeRelationshipResponse(relationship, owns, targets, rawTargets);
        public void ObserveNativeRelationshipTarget(ReignRelationshipCampaignBehavior relationship,
            ReignNativeRelationSyncTarget target, bool nativeSetterInvoked, int? before, int? after, string disposition) =>
            ReignLiveInteractionTestHost.ObserveNativeRelationshipTarget(relationship, target, nativeSetterInvoked, before, after, disposition);
        public void CaptureGovernmentLoadFingerprintPhase(ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior government, string phase, bool reset) =>
            ReignBeta.Government.Testing.ReignGovernmentTestControls.CaptureGovernmentLoadFingerprintPhase(government, phase, reset);
        public bool TryConsumeRulerJudgmentDraw(ReignRulerCampaignBehavior rulers, ReignBeta.Shared.Rulers.RulerJudgment judgment, out double draw) =>
            ReignRulerFixtureControls.TryConsumeJudgmentDraw(rulers, judgment, out draw);
        public bool TryConsumeRulerCaptureRoll(ReignRulerCampaignBehavior rulers, Hero hero, out double roll) =>
            ReignRulerFixtureControls.TryConsumeCaptureRoll(rulers, hero, out roll);
        public Task<double?> SubmitWorldTestHeartbeatAsync(double deltaAfterDay) =>
            ReignBeta.WorldSimulation.Integration.ReignWorldTestServerClient.SubmitWorldTestHeartbeatAsync(deltaAfterDay);

#if REIGN_EXCLUDE_COURT
        public IReignSpymasterTestControls Spymaster => ReignTestHarness.IdleSpymasterControls;
#else
        public IReignSpymasterTestControls Spymaster { get; } = new ReignBeta.Spymaster.Campaign.ReignSpymasterInProcessTestControls();
#endif
    }
}
