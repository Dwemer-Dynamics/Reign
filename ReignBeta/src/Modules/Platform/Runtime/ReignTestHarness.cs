using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Runtime
{
    // The only way product code reaches the in-game test harness (live interaction host,
    // action gauntlet, fixtures and test runtimes). SubModule installs the harness at load;
    // until then, or when no harness is installed, the idle harness below answers exactly
    // as an installed harness does when nothing is running.
    internal interface IReignTestHarness
    {
        void LiveInteractionApplicationTick(float dt);
        void ActionGauntletApplicationTick(float dt);
        void TickActionGauntletSuites();
        bool IsLiveDialogueGauntletActive { get; }
        bool OwnsAutomationSession { get; }
        bool OwnsNpcLivesSession { get; }
        bool OwnsPassiveWorldSession { get; }
        string GameInstanceId { get; }
        bool IsDisposablePartyAgencyFixtureConversationActive(Hero hero);

        // Fixture evidence applied to product context payloads while a fixture owns them.
        void ApplyManipulationFixtureOpportunityEvidence(JObject opportunitySnapshot);
        void ApplyManipulationFixtureWealthEvidence(Hero subject, JObject wealth);
        void ApplyNpcObservableWealthEvidence(Hero subject, JObject wealth);
        void ApplyNpcObservableClanEvidence(Hero subject, JObject clan);

        // Campaign behaviour the harness contributes, or null. SubModule adds it at its fixed
        // registration position.
        CampaignBehaviorBase CreateSocialBalanceHarnessBehavior();
        void RecordSocialBalanceOverride(string kind, JObject evidence);

        IReignSpymasterTestControls Spymaster { get; }
    }

    // Deterministic spymaster test controls consulted by the spymaster office.
    internal interface IReignSpymasterTestControls
    {
        bool Active { get; }
        bool SuppressIrreversibleNativeConsequences { get; }
        string RunId { get; }
        bool TryNext(string purpose, out float unitRoll);
        bool TryTakeMissionOutcome(string missionId, out float unitRoll);
        void DiscardMissionOutcome(string missionId);
        void DiscardMissionConsequence(string missionId);
        bool TryTakeMissionConsequence(string missionId, out float unitRoll);
        bool TryTakeBreakoutOutcome(string missionId, out float unitRoll);
        JObject PendingForeignAgentActionControl();
        void RecordForeignAgentActionReceipt(JObject receipt);
        void RecordNativeIntent(string type, JObject data = null);
    }

    internal static class ReignTestHarness
    {
        internal static IReignTestHarness Current { get; private set; } = IdleTestHarness.Instance;
        internal static IReignSpymasterTestControls IdleSpymasterControls => IdleTestHarness.Instance;

        // Called once by SubModule.OnSubModuleLoad.
        internal static void Install(IReignTestHarness harness) => Current = harness ?? IdleTestHarness.Instance;

        private sealed class IdleTestHarness : IReignTestHarness, IReignSpymasterTestControls
        {
            internal static readonly IdleTestHarness Instance = new IdleTestHarness();
            public void LiveInteractionApplicationTick(float dt) { }
            public void ActionGauntletApplicationTick(float dt) { }
            public void TickActionGauntletSuites() { }
            public bool IsLiveDialogueGauntletActive => false;
            public bool OwnsAutomationSession => false;
            public bool OwnsNpcLivesSession => false;
            public bool OwnsPassiveWorldSession => false;
            public string GameInstanceId => string.Empty;
            public bool IsDisposablePartyAgencyFixtureConversationActive(Hero hero) => false;
            public void ApplyManipulationFixtureOpportunityEvidence(JObject opportunitySnapshot) { }
            public void ApplyManipulationFixtureWealthEvidence(Hero subject, JObject wealth) { }
            public void ApplyNpcObservableWealthEvidence(Hero subject, JObject wealth) { }
            public void ApplyNpcObservableClanEvidence(Hero subject, JObject clan) { }
            public CampaignBehaviorBase CreateSocialBalanceHarnessBehavior() => null;
            public void RecordSocialBalanceOverride(string kind, JObject evidence) { }
            public IReignSpymasterTestControls Spymaster => this;
            public bool Active => false;
            public bool SuppressIrreversibleNativeConsequences => false;
            public string RunId => string.Empty;
            public bool TryNext(string purpose, out float unitRoll) { unitRoll = 0f; return false; }
            public bool TryTakeMissionOutcome(string missionId, out float unitRoll) { unitRoll = 0f; return false; }
            public void DiscardMissionOutcome(string missionId) { }
            public void DiscardMissionConsequence(string missionId) { }
            public bool TryTakeMissionConsequence(string missionId, out float unitRoll) { unitRoll = 0f; return false; }
            public bool TryTakeBreakoutOutcome(string missionId, out float unitRoll) { unitRoll = 0f; return false; }
            public JObject PendingForeignAgentActionControl() => null;
            public void RecordForeignAgentActionReceipt(JObject receipt) { }
            public void RecordNativeIntent(string type, JObject data = null) { }
        }
    }
}
