$settingsPath = 'C:\Users\speed\Desktop\ReignBeta\src\Settings\ReignBetaSettings.cs'
$debugPath = 'C:\Users\speed\Desktop\ReignBeta\src\Campaign\ReignBetaDebugActions.cs'

$settings = @'
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using ReignBeta.Campaign;
using System;

namespace ReignBeta.Settings
{
    public sealed class ReignBetaSettings : AttributeGlobalSettings<ReignBetaSettings>
    {
        private bool _enabled = true;
        private bool _debugMessagesEnabled = true;
        private bool _executeDiplomacyActions = true;
        private bool _executeStrategyPlans = true;
        private bool _executeInternalPoliticsActions = true;
        private bool _allowAutonomousWorldTicks;
        private bool _useLocalServer = true;
        private bool _receiveServerActionCommands = true;
        private bool _mcmTestModeEnabled;
        private string _localServerUrl = "http://127.0.0.1:5101";

        private Action _queueDebugDiplomacyAction = ReignBetaDebugActions.QueuePlayerKingdomPeaceOrWar;
        private Action _queueDebugCapturePlanAction = ReignBetaDebugActions.QueueFirstLordCapturePlan;
        private Action _showLedgerAction = ReignBetaDebugActions.ShowLedgerSummary;

        private Action _showTestTargetsAction = ReignBetaDebugActions.ShowTestTargetSummary;
        private Action _queueTestDeclareWarAction = ReignBetaDebugActions.QueueTestDeclareWar;
        private Action _queueTestMakePeaceAction = ReignBetaDebugActions.QueueTestMakePeace;
        private Action _queueTestTributePeaceAction = ReignBetaDebugActions.QueueTestTributePeace;
        private Action _queueTestRecordPromiseAction = ReignBetaDebugActions.QueueTestRecordPromise;
        private Action _queueTestReparationsPeaceAction = ReignBetaDebugActions.QueueTestReparationsPeace;
        private Action _queueTestSettlementSurrenderAction = ReignBetaDebugActions.QueueTestSettlementSurrenderPeace;
        private Action _queueTestFullSurrenderAction = ReignBetaDebugActions.QueueTestFullSurrenderPeace;
        private Action _queueTestRecruitRecoverAction = ReignBetaDebugActions.QueueTestRecruitAndRecover;
        private Action _queueTestFormArmyAction = ReignBetaDebugActions.QueueTestFormArmy;
        private Action _queueTestAttackSettlementAction = ReignBetaDebugActions.QueueTestAttackSettlement;
        private Action _queueTestCapturePlanAction = ReignBetaDebugActions.QueueTestCapturePlan;
        private Action _queueTestRulingClanRebellionAction = ReignBetaDebugActions.QueueTestRulingClanRebellion;
        private Action _queueTestInstallRulingClanAction = ReignBetaDebugActions.QueueTestInstallRulingClan;

        public override string Id => "ReignBeta";
        public override string DisplayName => "ReignBeta";
        public override string FolderName => "ReignBeta";
        public override string FormatType => "json2";
        public override int UIVersion => 1;

        [SettingPropertyBool("Enable ReignBeta AI World", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (value != _enabled)
                {
                    _enabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Show Debug Messages", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool DebugMessagesEnabled
        {
            get { return _debugMessagesEnabled; }
            set
            {
                if (value != _debugMessagesEnabled)
                {
                    _debugMessagesEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Execute Diplomacy Actions", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool ExecuteDiplomacyActions
        {
            get { return _executeDiplomacyActions; }
            set
            {
                if (value != _executeDiplomacyActions)
                {
                    _executeDiplomacyActions = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Execute Strategy Plans", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool ExecuteStrategyPlans
        {
            get { return _executeStrategyPlans; }
            set
            {
                if (value != _executeStrategyPlans)
                {
                    _executeStrategyPlans = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Execute Internal Politics Actions", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool ExecuteInternalPoliticsActions
        {
            get { return _executeInternalPoliticsActions; }
            set
            {
                if (value != _executeInternalPoliticsActions)
                {
                    _executeInternalPoliticsActions = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Allow Autonomous World Ticks", Order = 3, RequireRestart = false, HintText = "Reserved for server-driven AI world simulation. Keep disabled while testing direct actions.")]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool AllowAutonomousWorldTicks
        {
            get { return _allowAutonomousWorldTicks; }
            set
            {
                if (value != _allowAutonomousWorldTicks)
                {
                    _allowAutonomousWorldTicks = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Use Local ReignBeta Server", Order = 0, RequireRestart = false, HintText = "When enabled, ReignBeta posts world events to the local sidecar server.")]
        [SettingPropertyGroup("Local Server", GroupOrder = 2)]
        public bool UseLocalServer
        {
            get { return _useLocalServer; }
            set
            {
                if (value != _useLocalServer)
                {
                    _useLocalServer = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyText("Local Server URL", -1, false, "", Order = 1, RequireRestart = false, HintText = "Default: http://127.0.0.1:5101")]
        [SettingPropertyGroup("Local Server", GroupOrder = 2)]
        public string LocalServerUrl
        {
            get { return _localServerUrl; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:5101" : value.Trim();
                if (next != _localServerUrl)
                {
                    _localServerUrl = next;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Receive Server Action Commands", Order = 2, RequireRestart = false, HintText = "When enabled, the local server can send validated diplomacy and strategy commands to the in-game ReignBeta ledger.")]
        [SettingPropertyGroup("Local Server", GroupOrder = 2)]
        public bool ReceiveServerActionCommands
        {
            get { return _receiveServerActionCommands; }
            set
            {
                if (value != _receiveServerActionCommands)
                {
                    _receiveServerActionCommands = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("Queue Peace/War Test", Order = 0, RequireRestart = false, Content = "Queue", HintText = "Legacy quick test: queues peace with the first enemy kingdom, or declares war on the first valid kingdom if you are at peace.")]
        [SettingPropertyGroup("Debug", GroupOrder = 3)]
        public Action QueueDebugDiplomacyAction
        {
            get { return _queueDebugDiplomacyAction; }
            set { _queueDebugDiplomacyAction = value ?? ReignBetaDebugActions.QueuePlayerKingdomPeaceOrWar; }
        }

        [SettingPropertyButton("Queue Capture Plan Test", Order = 1, RequireRestart = false, Content = "Queue", HintText = "Legacy quick test: queues the strongest available non-player lord in your kingdom to attack the nearest hostile fortification.")]
        [SettingPropertyGroup("Debug", GroupOrder = 3)]
        public Action QueueDebugCapturePlanAction
        {
            get { return _queueDebugCapturePlanAction; }
            set { _queueDebugCapturePlanAction = value ?? ReignBetaDebugActions.QueueFirstLordCapturePlan; }
        }

        [SettingPropertyButton("Show Ledger Summary", Order = 2, RequireRestart = false, Content = "Show", HintText = "Shows the current ReignBeta action ledger counts.")]
        [SettingPropertyGroup("Debug", GroupOrder = 3)]
        public Action ShowLedgerAction
        {
            get { return _showLedgerAction; }
            set { _showLedgerAction = value ?? ReignBetaDebugActions.ShowLedgerSummary; }
        }

        [SettingPropertyBool("Enable MCM Test Mode", Order = 0, RequireRestart = false, HintText = "Required before the MCM test buttons queue world-changing actions.")]
        [SettingPropertyGroup("MCM Test Mode", GroupOrder = 4)]
        public bool McmTestModeEnabled
        {
            get { return _mcmTestModeEnabled; }
            set
            {
                if (value != _mcmTestModeEnabled)
                {
                    _mcmTestModeEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("Show Test Targets", Order = 1, RequireRestart = false, Content = "Show", HintText = "Shows which kingdoms, lords, settlements, and clans the test buttons will try to use.")]
        [SettingPropertyGroup("MCM Test Mode", GroupOrder = 4)]
        public Action ShowTestTargetsAction
        {
            get { return _showTestTargetsAction; }
            set { _showTestTargetsAction = value ?? ReignBetaDebugActions.ShowTestTargetSummary; }
        }

        [SettingPropertyButton("Declare War", Order = 0, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestDeclareWarAction
        {
            get { return _queueTestDeclareWarAction; }
            set { _queueTestDeclareWarAction = value ?? ReignBetaDebugActions.QueueTestDeclareWar; }
        }

        [SettingPropertyButton("Make Peace", Order = 1, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestMakePeaceAction
        {
            get { return _queueTestMakePeaceAction; }
            set { _queueTestMakePeaceAction = value ?? ReignBetaDebugActions.QueueTestMakePeace; }
        }

        [SettingPropertyButton("Offer Tribute Peace", Order = 2, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestTributePeaceAction
        {
            get { return _queueTestTributePeaceAction; }
            set { _queueTestTributePeaceAction = value ?? ReignBetaDebugActions.QueueTestTributePeace; }
        }

        [SettingPropertyButton("Record Promise", Order = 3, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestRecordPromiseAction
        {
            get { return _queueTestRecordPromiseAction; }
            set { _queueTestRecordPromiseAction = value ?? ReignBetaDebugActions.QueueTestRecordPromise; }
        }

        [SettingPropertyButton("Demand Reparations Peace", Order = 4, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestReparationsPeaceAction
        {
            get { return _queueTestReparationsPeaceAction; }
            set { _queueTestReparationsPeaceAction = value ?? ReignBetaDebugActions.QueueTestReparationsPeace; }
        }

        [SettingPropertyButton("Demand Settlement Peace", Order = 5, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestSettlementSurrenderAction
        {
            get { return _queueTestSettlementSurrenderAction; }
            set { _queueTestSettlementSurrenderAction = value ?? ReignBetaDebugActions.QueueTestSettlementSurrenderPeace; }
        }

        [SettingPropertyButton("Demand Full Surrender", Order = 6, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Diplomacy", GroupOrder = 5)]
        public Action QueueTestFullSurrenderAction
        {
            get { return _queueTestFullSurrenderAction; }
            set { _queueTestFullSurrenderAction = value ?? ReignBetaDebugActions.QueueTestFullSurrenderPeace; }
        }

        [SettingPropertyButton("Recruit And Recover", Order = 0, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Strategy", GroupOrder = 6)]
        public Action QueueTestRecruitRecoverAction
        {
            get { return _queueTestRecruitRecoverAction; }
            set { _queueTestRecruitRecoverAction = value ?? ReignBetaDebugActions.QueueTestRecruitAndRecover; }
        }

        [SettingPropertyButton("Form Army", Order = 1, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Strategy", GroupOrder = 6)]
        public Action QueueTestFormArmyAction
        {
            get { return _queueTestFormArmyAction; }
            set { _queueTestFormArmyAction = value ?? ReignBetaDebugActions.QueueTestFormArmy; }
        }

        [SettingPropertyButton("Attack Settlement", Order = 2, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Strategy", GroupOrder = 6)]
        public Action QueueTestAttackSettlementAction
        {
            get { return _queueTestAttackSettlementAction; }
            set { _queueTestAttackSettlementAction = value ?? ReignBetaDebugActions.QueueTestAttackSettlement; }
        }

        [SettingPropertyButton("Capture Settlement Plan", Order = 3, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Strategy", GroupOrder = 6)]
        public Action QueueTestCapturePlanAction
        {
            get { return _queueTestCapturePlanAction; }
            set { _queueTestCapturePlanAction = value ?? ReignBetaDebugActions.QueueTestCapturePlan; }
        }

        [SettingPropertyButton("Start Ruling Clan Rebellion", Order = 0, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Politics", GroupOrder = 7)]
        public Action QueueTestRulingClanRebellionAction
        {
            get { return _queueTestRulingClanRebellionAction; }
            set { _queueTestRulingClanRebellionAction = value ?? ReignBetaDebugActions.QueueTestRulingClanRebellion; }
        }

        [SettingPropertyButton("Install Ruling Clan", Order = 1, RequireRestart = false, Content = "Queue")]
        [SettingPropertyGroup("MCM Test Mode - Politics", GroupOrder = 7)]
        public Action QueueTestInstallRulingClanAction
        {
            get { return _queueTestInstallRulingClanAction; }
            set { _queueTestInstallRulingClanAction = value ?? ReignBetaDebugActions.QueueTestInstallRulingClan; }
        }
    }
}
'@

$debug = @'
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public static class ReignBetaDebugActions
    {
        public static void QueuePlayerKingdomPeaceOrWar()
        {
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (behavior == null || playerKingdom == null)
            {
                Show("No active player kingdom or ReignBeta behavior.");
                return;
            }

            Kingdom warTarget = FindEnemyKingdom(playerKingdom);
            if (warTarget != null)
            {
                behavior.QueueMakePeace(playerKingdom, warTarget, "Debug peace action from ReignBeta.");
                return;
            }

            Kingdom target = FindPeacefulKingdom(playerKingdom);
            if (target == null)
            {
                Show("No valid kingdom target found.");
                return;
            }

            behavior.QueueDeclareWar(playerKingdom, target, "Debug war action from ReignBeta.");
        }

        public static void QueueFirstLordCapturePlan()
        {
            if (!TryGetPlayerKingdom(out Kingdom playerKingdom))
            {
                return;
            }

            Hero actor = FindTestLord(playerKingdom);
            Settlement target = FindHostileFortification(playerKingdom, actor);
            if (actor == null || target == null)
            {
                Show("Need a non-player lord and a hostile fortification for the capture test.");
                return;
            }

            ReignAICampaignBehavior.Instance.QueueCaptureSettlement(actor, target, "Debug capture plan from ReignBeta.");
        }

        public static void ShowLedgerSummary()
        {
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
            if (behavior == null)
            {
                Show("ReignBeta behavior is not active.");
                return;
            }

            int total = behavior.Actions.Count;
            int active = behavior.Actions.Count(x => x != null && !x.IsTerminal);
            int proposed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Proposed);
            int accepted = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Accepted);
            int completed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Completed);
            int failed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Failed);
            Show("Ledger total=" + total + " active=" + active + " proposed=" + proposed + " accepted=" + accepted + " completed=" + completed + " failed=" + failed + ".");
        }

        public static void ShowTestTargetSummary()
        {
            if (!TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Hero lord = FindTestLord(kingdom);
            Kingdom enemy = FindEnemyKingdom(kingdom);
            Kingdom peaceful = FindPeacefulKingdom(kingdom);
            Settlement hostile = FindHostileFortification(kingdom, lord);
            Settlement anyTarget = hostile ?? FindAnyUsefulSettlement(kingdom);
            Clan claimant = FindClaimantClan(kingdom);
            IEnumerable<Clan> supporters = FindSupporterClans(kingdom, claimant);

            Show("Test targets: kingdom=" + kingdom.InformalName
                + ", enemy=" + NameOrNone(enemy)
                + ", peaceful=" + NameOrNone(peaceful)
                + ", lord=" + NameOrNone(lord)
                + ", settlement=" + NameOrNone(anyTarget)
                + ", claimant=" + NameOrNone(claimant)
                + ", supporters=" + string.Join(",", supporters.Select(x => x.Name.ToString()).DefaultIfEmpty("none")) + ".");
        }

        public static void QueueTestDeclareWar()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No peaceful kingdom available for declare war test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: declare war."
            }, "declare war");
        }

        public static void QueueTestMakePeace()
        {
            QueuePeaceAction(ReignWorldActionType.DiplomacyMakePeace, "MCM test: make peace.", null, "make peace");
        }

        public static void QueueTestTributePeace()
        {
            JObject terms = new JObject
            {
                ["dailyTribute"] = 150,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyOfferTributePeace, "MCM test: offer tribute peace.", terms, "tribute peace");
        }

        public static void QueueTestRecordPromise()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor) ?? FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No kingdom target available for promise test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyRecordPromise,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: diplomatic promise recorded for future memory."
            }, "record promise");
        }

        public static void QueueTestReparationsPeace()
        {
            JObject terms = new JObject
            {
                ["reparationsGold"] = 15000,
                ["dailyTribute"] = 200,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyDemandReparationsPeace, "MCM test: demand reparations for peace.", terms, "reparations peace");
        }

        public static void QueueTestSettlementSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSettlementPeace, "MCM test: demand settlement surrender for peace.", false);
        }

        public static void QueueTestFullSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSurrenderPeace, "MCM test: demand full surrender terms.", true);
        }

        public static void QueueTestRecruitAndRecover()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyRecruitAndRecover, "MCM test: recruit and recover.", "recruit and recover", false);
        }

        public static void QueueTestFormArmy()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyFormArmy, "MCM test: form army.", "form army", false);
        }

        public static void QueueTestAttackSettlement()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyAttackSettlement, "MCM test: attack settlement.", "attack settlement", true);
        }

        public static void QueueTestCapturePlan()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyCaptureSettlement, "MCM test: capture settlement plan.", "capture settlement plan", true);
        }

        public static void QueueTestRulingClanRebellion()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Clan claimant = FindClaimantClan(kingdom);
            if (claimant == null)
            {
                Show("No non-ruling clan available for rebellion test.");
                return;
            }

            List<string> supporters = FindSupporterClans(kingdom, claimant).Select(x => x.StringId).ToList();
            JObject terms = new JObject
            {
                ["supporterClanIds"] = new JArray(supporters)
            };

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.PoliticsStartRulingClanRebellion,
                Source = "mcm_test",
                ActorKingdomStringId = kingdom.StringId,
                ActorClanStringId = claimant.StringId,
                SupporterClanIdsCsv = string.Join(",", supporters),
                TermsJson = terms.ToString(Formatting.None),
                Reason = "MCM test: ruling clan rebellion."
            }, "ruling clan rebellion");
        }

        public static void QueueTestInstallRulingClan()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Clan claimant = FindClaimantClan(kingdom);
            if (claimant == null)
            {
                Show("No non-ruling clan available for install ruling clan test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.PoliticsInstallRulingClan,
                Source = "mcm_test",
                ActorKingdomStringId = kingdom.StringId,
                ActorClanStringId = claimant.StringId,
                Reason = "MCM test: install new ruling clan."
            }, "install ruling clan");
        }

        private static void QueuePeaceAction(ReignWorldActionType type, string reason, JObject terms, string label)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = reason
            }, label);
        }

        private static void QueueSurrenderAction(ReignWorldActionType type, string reason, bool includeReparations)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            Settlement settlement = FindEnemyFortificationOwnedBy(target);
            if (settlement == null)
            {
                Show("Enemy kingdom has no town/castle available for surrender test.");
                return;
            }

            JObject terms = new JObject
            {
                ["settlementIds"] = new JArray(settlement.StringId)
            };

            if (includeReparations)
            {
                terms["reparationsGold"] = 25000;
                terms["dailyTribute"] = 300;
                terms["durationDays"] = 45;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TargetSettlementStringId = settlement.StringId,
                TermsJson = terms.ToString(Formatting.None),
                Reason = reason
            }, type == ReignWorldActionType.DiplomacyDemandSurrenderPeace ? "full surrender peace" : "settlement surrender peace");
        }

        private static void QueueStrategyAction(ReignWorldActionType type, string reason, string label, bool requireHostile)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Hero actor = FindTestLord(kingdom);
            if (actor == null)
            {
                Show("No non-player lord with an active party found.");
                return;
            }

            Settlement target = requireHostile ? FindHostileFortification(kingdom, actor) : FindHostileFortification(kingdom, actor) ?? FindAnyUsefulSettlement(kingdom);
            if (target == null)
            {
                Show("No valid settlement target found for " + label + ".");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorHeroStringId = actor.StringId,
                ActorKingdomStringId = kingdom.StringId,
                TargetSettlementStringId = target.StringId,
                Reason = reason,
                MinimumTroops = 40,
                DesiredStrength = 350,
                MaxAttempts = type == ReignWorldActionType.StrategyCaptureSettlement ? 24 : 3
            }, label);
        }

        private static void QueueAction(ReignWorldActionRecord action, string label)
        {
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
            if (behavior == null)
            {
                Show("ReignBeta behavior is not active.");
                return;
            }

            if (!ReignActionValidator.Validate(action, out string failure))
            {
                Show("Cannot queue " + label + ": " + failure);
                return;
            }

            behavior.EnqueueAction(action);
            Show("MCM test queued " + label + ".");
        }

        private static bool RequireTestMode()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || settings.McmTestModeEnabled)
            {
                return true;
            }

            Show("Enable ReignBeta > MCM Test Mode > Enable MCM Test Mode before queuing test actions.");
            return false;
        }

        private static bool TryGetPlayerKingdom(out Kingdom kingdom)
        {
            kingdom = Clan.PlayerClan?.Kingdom;
            if (ReignAICampaignBehavior.Instance == null || kingdom == null)
            {
                Show("No active player kingdom or ReignBeta behavior.");
                return false;
            }

            return true;
        }

        private static Kingdom FindEnemyKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && actor.IsAtWarWith(x));
        }

        private static Kingdom FindPeacefulKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && !actor.IsAtWarWith(x));
        }

        private static Hero FindTestLord(Kingdom kingdom)
        {
            return kingdom?.AliveLords
                .Where(x => x != null && x != Hero.MainHero && x.PartyBelongedTo != null && !x.PartyBelongedTo.IsMainParty)
                .OrderByDescending(x => x.PartyBelongedTo.Party?.EstimatedStrength ?? 0f)
                .FirstOrDefault();
        }

        private static Settlement FindHostileFortification(Kingdom kingdom, Hero actor)
        {
            if (kingdom == null)
            {
                return null;
            }

            Vec2 origin = actor?.PartyBelongedTo == null ? Vec2.Zero : actor.PartyBelongedTo.GetPosition2D;
            return Settlement.All
                .Where(x => x != null
                    && x.IsFortification
                    && x.MapFaction != null
                    && kingdom.IsAtWarWith(x.MapFaction)
                    && !x.IsUnderSiege)
                .OrderBy(x => origin == Vec2.Zero ? 0f : origin.DistanceSquared(x.GetPosition2D))
                .FirstOrDefault();
        }

        private static Settlement FindEnemyFortificationOwnedBy(Kingdom target)
        {
            return Settlement.All
                .Where(x => x != null && x.IsFortification && x.MapFaction == target && !x.IsUnderSiege)
                .OrderByDescending(x => x.Prosperity)
                .FirstOrDefault();
        }

        private static Settlement FindAnyUsefulSettlement(Kingdom kingdom)
        {
            return kingdom?.Settlements.FirstOrDefault(x => x != null && !x.IsHideout)
                ?? Settlement.All.FirstOrDefault(x => x != null && !x.IsHideout);
        }

        private static Clan FindClaimantClan(Kingdom kingdom)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .FirstOrDefault();
        }

        private static IEnumerable<Clan> FindSupporterClans(Kingdom kingdom, Clan claimant)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != claimant && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .Take(2)
                ?? Enumerable.Empty<Clan>();
        }

        private static string NameOrNone(Kingdom kingdom)
        {
            return kingdom == null ? "none" : kingdom.InformalName.ToString();
        }

        private static string NameOrNone(Hero hero)
        {
            return hero == null ? "none" : hero.Name.ToString();
        }

        private static string NameOrNone(Settlement settlement)
        {
            return settlement == null ? "none" : settlement.Name.ToString();
        }

        private static string NameOrNone(Clan clan)
        {
            return clan == null ? "none" : clan.Name.ToString();
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[ReignBeta] " + message, Color.FromUint(0xFF66CCFF)));
        }
    }
}
'@

[System.IO.File]::WriteAllText($settingsPath, $settings, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($debugPath, $debug, [System.Text.UTF8Encoding]::new($false))

Select-String -LiteralPath $settingsPath,$debugPath -Pattern 'McmTestModeEnabled|QueueTestDeclareWar|QueueTestFullSurrender|QueueTestRulingClanRebellion|ShowTestTargetSummary' -Context 0,1
