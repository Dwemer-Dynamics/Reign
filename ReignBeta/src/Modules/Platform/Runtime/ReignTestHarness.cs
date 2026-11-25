using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Platform.Runtime
{
    // The only way product code reaches the in-game test harness (live interaction host,
    // action gauntlet, fixtures and test runtimes), which is compiled into the optional
    // ReignBeta.Testing.dll. SubModule loads and installs it at load; until then, or when no
    // harness is installed, the idle harness below answers as an installed harness does when
    // nothing is running.
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

        // Product forwarders into harness-owned controls. Each answers as the harness does when
        // no fixture has armed it: nothing held, nothing captured, no prepared draw or roll.
        bool HoldNpcLivesAnnouncementDelivery(ReignBeta.Characters.Campaign.ReignNpcLivesBehavior lives, JObject row);
        Task<Action<string>> HoldNativeRelationshipResponse(ReignRelationshipCampaignBehavior relationship,
            Func<bool> owns, IReadOnlyList<ReignNativeRelationSyncTarget> targets, JArray rawTargets);
        void ObserveNativeRelationshipTarget(ReignRelationshipCampaignBehavior relationship,
            ReignNativeRelationSyncTarget target, bool nativeSetterInvoked, int? before, int? after, string disposition);
        void CaptureGovernmentLoadFingerprintPhase(ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior government, string phase, bool reset);
        bool TryConsumeRulerJudgmentDraw(ReignBeta.WorldSimulation.Rulers.ReignRulerCampaignBehavior rulers, ReignBeta.Shared.Rulers.RulerJudgment judgment, out double draw);
        bool TryConsumeRulerCaptureRoll(ReignBeta.WorldSimulation.Rulers.ReignRulerCampaignBehavior rulers, Hero hero, out double roll);
        Task<double?> SubmitWorldTestHeartbeatAsync(double deltaAfterDay);

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

        internal const string TestingAssemblyFileName = "ReignBeta.Testing.dll";

        internal static void Install(IReignTestHarness harness) => Current = harness ?? IdleTestHarness.Instance;

        // Called once, first, by SubModule.OnSubModuleLoad. Loads the optional harness assembly that
        // ships next to ReignBeta.dll and installs its single IReignTestHarness implementation. A module
        // without the assembly, or one that fails to load, keeps the idle harness.
        internal static void InstallFromOptionalAssembly(Assembly clientAssembly)
        {
            try
            {
                string directory = Path.GetDirectoryName(clientAssembly.Location);
                string path = Path.Combine(directory ?? string.Empty, TestingAssemblyFileName);
                if (!File.Exists(path)) return;
                Assembly testing = Assembly.LoadFrom(path);
                Type[] harnessTypes = LoadableTypes(testing)
                    .Where(type => type.IsClass && !type.IsAbstract && typeof(IReignTestHarness).IsAssignableFrom(type))
                    .ToArray();
                if (harnessTypes.Length != 1)
                    throw new InvalidOperationException(TestingAssemblyFileName + " must contain exactly one test harness; found " + harnessTypes.Length + ".");
                Install((IReignTestHarness)Activator.CreateInstance(harnessTypes[0]));
            }
            catch (Exception ex)
            {
                Install(null);
                ReignBeta.Platform.Integration.ReignLog.Warn("Reign test harness was not installed; continuing without it: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // GetTypes throws when any harness type fails to load; keep the loadable ones and log why the rest failed.
        private static Type[] LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception loaderException in (ex.LoaderExceptions ?? new Exception[0]).Where(item => item != null).Take(5))
                    ReignBeta.Platform.Integration.ReignLog.Warn("Reign test harness type failed to load: " + loaderException.GetType().Name + ": " + loaderException.Message);
                return ex.Types.Where(type => type != null).ToArray();
            }
        }

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
            public bool HoldNpcLivesAnnouncementDelivery(ReignBeta.Characters.Campaign.ReignNpcLivesBehavior lives, JObject row) => false;
            public Task<Action<string>> HoldNativeRelationshipResponse(ReignRelationshipCampaignBehavior relationship,
                Func<bool> owns, IReadOnlyList<ReignNativeRelationSyncTarget> targets, JArray rawTargets) => null;
            public void ObserveNativeRelationshipTarget(ReignRelationshipCampaignBehavior relationship,
                ReignNativeRelationSyncTarget target, bool nativeSetterInvoked, int? before, int? after, string disposition) { }
            public void CaptureGovernmentLoadFingerprintPhase(ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior government, string phase, bool reset) { }
            public bool TryConsumeRulerJudgmentDraw(ReignBeta.WorldSimulation.Rulers.ReignRulerCampaignBehavior rulers, ReignBeta.Shared.Rulers.RulerJudgment judgment, out double draw) { draw = 0; return false; }
            public bool TryConsumeRulerCaptureRoll(ReignBeta.WorldSimulation.Rulers.ReignRulerCampaignBehavior rulers, Hero hero, out double roll) { roll = 0; return false; }
            public Task<double?> SubmitWorldTestHeartbeatAsync(double deltaAfterDay) => Task.FromResult<double?>(null);
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
