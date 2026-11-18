using Newtonsoft.Json.Linq;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    // Test-harness side of IReignTestHarness: forwards to the live interaction host, the action
    // gauntlet and the social-balance harness; spymaster controls come from the Spymaster module.
    // SubModule installs it at load; product code never names these harness types directly.
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
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(kind, evidence);

#if REIGN_EXCLUDE_COURT
        public IReignSpymasterTestControls Spymaster => ReignTestHarness.IdleSpymasterControls;
#else
        public IReignSpymasterTestControls Spymaster { get; } = new ReignBeta.Court.ReignSpymasterInProcessTestControls();
#endif
    }
}
