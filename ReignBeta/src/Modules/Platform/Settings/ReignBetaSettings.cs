using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using AIPortraits;
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
        private bool _executeConversationActions = true;
        private bool _campaignCommandEngineEnabled = true;
        private bool _allowAutonomousWorldTicks = true;
        private bool _useLocalServer = true;
        private bool _receiveServerActionCommands = true;
        private bool _eventsEnabled = true;
        private bool _tournamentCelebrationsEnabled = true;
        private bool _partyChatEnabled = true;
        private bool _correspondenceEnabled = true;
#if !REIGN_EXCLUDE_COURT
        // Court is a player-facing Reign workspace, not an opt-in test fixture.
        // Keep an explicitly loaded MCM false authoritative, but default to on
        // when MCM restoration is late or no saved setting exists.
        private bool _courtSystemEnabled = true;
        private bool _castleChatImageGeneration = true;
#endif
        private bool _passiveRelationshipDirectorEnabled = true;
        private bool _ambientRelationshipDriftEnabled = true;
        private bool _reignControlledNpcMarriageEnabled = true;
        private bool _pregnancyCombatRestrictionsEnabled = true;
        private bool _generatedWildernessEventsEnabled = true;
        private bool _eventRelationshipChangesEnabled = true;
        private bool _kingdomEventsEnabled = true;
        private bool _aiPortraitsEnabled = true;
        private bool _revealAllCharacterStatsAndRelationships;
        private bool _mcmTestModeEnabled;
        private bool _dialogueComplianceTestModeEnabled;
        private string _localServerUrl = "http://127.0.0.1:5101";

        private Action _openPartyChatAction = ReignBetaDebugActions.OpenPartyChat;
        private Action _openCorrespondenceAction = ReignBetaDebugActions.OpenCorrespondence;
        private Action _forceGeneratedWildernessEventAction = ReignBetaDebugActions.ForceGeneratedWildernessEvent;
        private Action _forceEventInCurrentTownAction = ReignBetaDebugActions.ForceEventInCurrentTown;
        private Action _requestConversationPortraitAction = ReignBetaDebugActions.RequestConversationPortrait;
        private Action _createPlayerPortraitAction = ReignBetaDebugActions.CreatePlayerPortrait;
        private Action _clearPlayerPortraitAction = ReignBetaDebugActions.ClearPlayerPortrait;
        private Action _reloadPortraitsFromDiskAction = ReignBetaDebugActions.ReloadPortraitsFromDisk;
		private Action _prepareSharedPortraitCacheAction = ReignBetaDebugActions.PrepareSharedPortraitCache;
		private Action _startLordEncyclopediaSourceScanAction = ReignBetaDebugActions.StartLordEncyclopediaSourceScan;
        private Action _diagnoseCurrentConversationPortraitAction = ReignBetaDebugActions.DiagnoseCurrentConversationPortrait;
        private Action _diagnoseCurrentIdentityAction = ReignBetaDebugActions.DiagnoseCurrentConversationIdentity;
        private Action _resetCurrentIdentityAction = ReignBetaDebugActions.ResetCurrentConversationIdentity;
        private Action _prepareRoyalCourtTestRealmAction = ReignBetaDebugActions.ConfirmSetupPlayerKingdomTestScenario;
        private Action _makePlayerKnowEveryoneAction = ReignBetaDebugActions.MakePlayerKnowEveryone;
        private Action _triggerRandomDiplomacyPopupTestAction = ReignBetaDebugActions.TriggerRandomDiplomacyPopupTest;
        private Action _forceKingdomEventAction = ReignBetaDebugActions.OpenForceKingdomEventMenu;
        private Action _startNpcDialogueAuditAction = ReignNpcDialogueAuditRunner.StartFromMcm;
        private Action _stopNpcDialogueAuditAction = ReignNpcDialogueAuditRunner.StopFromMcm;
        private Action _showNpcDialogueAuditSummaryAction = ReignNpcDialogueAuditRunner.ShowLastSummaryFromMcm;

        public override string Id => "ReignBeta";
        public override string DisplayName => "Bannerlord Reign";
        public override string FolderName => "ReignBeta";
        public override string FormatType => "json2";
        public override int UIVersion => 2;
#if !REIGN_EXCLUDE_COURT
        // MCM exposes AttributeGlobalSettings.Instance after its own discovery
        // pass. Court is enabled by default during that bounded gap; once MCM
        // supplies an instance, an explicitly persisted false stays authoritative.
        public static bool IsCourtSystemAvailable => Instance?.CourtSystemEnabled ?? true;
#endif

        [SettingPropertyBool("Enable Bannerlord Reign AI World", Order = 0, RequireRestart = false)]
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
                    ReignFamilyCampaignBehavior.Instance?.OnPregnancyRestrictionSettingsChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Social Events", Order = 2, RequireRestart = false, HintText = "Turns Bannerlord Reign social events on or off.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool EventsEnabled
        {
            get { return _eventsEnabled; }
            set
            {
                if (value != _eventsEnabled)
                {
                    _eventsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Tournament Celebrations", Order = 3, RequireRestart = false, HintText = "When enabled, towns with active tournaments may announce celebration events.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool TournamentCelebrationsEnabled
        {
            get { return _tournamentCelebrationsEnabled; }
            set
            {
                if (value != _tournamentCelebrationsEnabled)
                {
                    _tournamentCelebrationsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Party Chat", Order = 4, RequireRestart = false, HintText = "Enables the Bannerlord Reign party/group chat overlay. Default hotkey is backslash.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool PartyChatEnabled
        {
            get { return _partyChatEnabled; }
            set
            {
                if (value != _partyChatEnabled)
                {
                    _partyChatEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Correspondence", Order = 5, RequireRestart = false, HintText = "Enables letters between the player and known living characters. Default hotkey is Ctrl+M.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool CorrespondenceEnabled
        {
            get { return _correspondenceEnabled; }
            set
            {
                if (value != _correspondenceEnabled)
                {
                    _correspondenceEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

#if !REIGN_EXCLUDE_COURT
        [SettingPropertyBool("Enable Court System", Order = 8, RequireRestart = false, HintText = "Enables the in-development royal court simulation. Court is available only while you are the current ruler of a kingdom and are in a town or castle belonging to that kingdom.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool CourtSystemEnabled
        {
            get { return _courtSystemEnabled; }
            set
            {
                if (value != _courtSystemEnabled)
                {
                    _courtSystemEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Castle Chat Image Generation", Order = 9, RequireRestart = false, IsToggle = true, HintText = "Generates room scenes for Castle Chat. This remains independent from the general AI Portraits switch; required NPC portrait masters may still be prepared for a castle scene.")]
        [SettingPropertyGroup("Castle Chat", GroupOrder = 4)]
        public bool CastleChatImageGeneration
        {
            get { return _castleChatImageGeneration; }
            set
            {
                if (value != _castleChatImageGeneration)
                {
                    _castleChatImageGeneration = value;
                    OnPropertyChanged();
                }
            }
        }
#endif

        private bool _tavernHouseImageGeneration = true;

        [SettingPropertyBool("Tavern House Scene Images", Order = 1, RequireRestart = false, HintText = "Generates arrival and Look Again scenes for confirmed tavern house visits. Portrait generation remains available independently.")]
        [SettingPropertyGroup("Tavern House", GroupOrder = 5)]
        public bool TavernHouseImageGeneration
        {
            get { return _tavernHouseImageGeneration; }
            set { if (_tavernHouseImageGeneration == value) return; _tavernHouseImageGeneration = value; OnPropertyChanged(); }
        }

        [SettingPropertyBool("Enable Passive NPC Relationships", Order = 6, RequireRestart = false, HintText = "Master switch for deterministic ambient relationship chemistry and rarer validated NPC relationship events.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool PassiveRelationshipDirectorEnabled
        {
            get { return _passiveRelationshipDirectorEnabled; }
            set { if (value != _passiveRelationshipDirectorEnabled) { _passiveRelationshipDirectorEnabled = value; OnPropertyChanged(); } }
        }

        [SettingPropertyBool("Ambient Relationship Drift", Order = 7, RequireRestart = false, HintText = "Allows adult NPCs sharing a party or settlement to develop small, personality-driven directional relationship changes. This makes no LLM calls.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool AmbientRelationshipDriftEnabled
        {
            get { return _ambientRelationshipDriftEnabled; }
            set { if (value != _ambientRelationshipDriftEnabled) { _ambientRelationshipDriftEnabled = value; OnPropertyChanged(); } }
        }

        [SettingPropertyBool("Use Reign-Controlled NPC Marriage", Order = 7, RequireRestart = false, HintText = "Disables Bannerlord's random autonomous NPC matchmaking and lets Reign govern NPC marriages. Player courtship remains native.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool ReignControlledNpcMarriageEnabled
        {
            get { return _reignControlledNpcMarriageEnabled; }
            set { if (value != _reignControlledNpcMarriageEnabled) { _reignControlledNpcMarriageEnabled = value; OnPropertyChanged(); } }
        }

        [SettingPropertyBool("Restrict Pregnant NPCs From Combat", Order = 9, RequireRestart = false, HintText = "When enabled, pregnant NPCs withdraw to safe fortifications and cannot join parties or perform combat actions until childbirth. Turning this off does not reconstruct parties already changed.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool PregnancyCombatRestrictionsEnabled
        {
            get { return _pregnancyCombatRestrictionsEnabled; }
            set
            {
                if (value != _pregnancyCombatRestrictionsEnabled)
                {
                    _pregnancyCombatRestrictionsEnabled = value;
                    OnPropertyChanged();
                    ReignFamilyCampaignBehavior.Instance?.OnPregnancyRestrictionSettingsChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Generated Wilderness Events", Order = 5, RequireRestart = false, HintText = "When enabled, Bannerlord Reign may create standalone AI social events while traveling outside settlements.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool GeneratedWildernessEventsEnabled
        {
            get { return _generatedWildernessEventsEnabled; }
            set
            {
                if (value != _generatedWildernessEventsEnabled)
                {
                    _generatedWildernessEventsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Conversation Relationship Changes", Order = 7, RequireRestart = false, HintText = "When enabled, individual chat, party chat, social events, and wilderness events apply evidence-grounded personal relationship changes between the player and NPCs and among participating NPCs.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool EventRelationshipChangesEnabled
        {
            get { return _eventRelationshipChangesEnabled; }
            set
            {
                if (value != _eventRelationshipChangesEnabled)
                {
                    _eventRelationshipChangesEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Kingdom Catastrophes and Boons", Order = 10, RequireRestart = false, HintText = "Enables the saved 1% daily global roll for kingdom-wide economic, diplomatic, and orderly-succession events.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool KingdomEventsEnabled
        {
            get { return _kingdomEventsEnabled; }
            set
            {
                if (value != _kingdomEventsEnabled)
                {
                    _kingdomEventsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Show Debug Messages", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
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

        [SettingPropertyBool("Execute Conversation Actions", Order = 3, RequireRestart = false, HintText = "Allows validated actions arising directly from conversation, including gifts, transfers, attacks, and duels. Lethal actions still require their own strict native validation.")]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool ExecuteConversationActions
        {
            get { return _executeConversationActions; }
            set
            {
                if (value != _executeConversationActions)
                {
                    _executeConversationActions = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Campaign Command Engine", Order = 4, RequireRestart = false, HintText = "Supervises accepted NPC party and army orders. It never controls the player's main party and still requires sovereign authority or the current commander's explicit agreement.")]
        [SettingPropertyGroup("Execution", GroupOrder = 1)]
        public bool CampaignCommandEngineEnabled
        {
            get { return _campaignCommandEngineEnabled; }
            set
            {
                if (value != _campaignCommandEngineEnabled)
                {
                    _campaignCommandEngineEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Ruler-Driven NPC Diplomacy", Order = 5, RequireRestart = false, HintText = "Lets the Reign world director replace random native NPC diplomacy. The player's kingdom is excluded until court integration is available.")]
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

        [SettingPropertyBool("Use Local Bannerlord Reign Server", Order = 0, RequireRestart = false, HintText = "When enabled, Bannerlord Reign posts world events to the local sidecar server.")]
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

        [SettingPropertyBool("Receive Server Action Commands", Order = 2, RequireRestart = false, HintText = "When enabled, the local server can send validated diplomacy and strategy commands to the in-game Bannerlord Reign ledger.")]
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

        [SettingPropertyBool("Enable AI Portraits", Order = 0, RequireRestart = false, IsToggle = true, HintText = "When off, Bannerlord Reign stops replacing portraits and disables portrait generation.")]
        [SettingPropertyGroup("AI Portraits", GroupOrder = 3)]
        public bool AiPortraitsEnabled
        {
            get { return _aiPortraitsEnabled; }
            set
            {
                if (value != _aiPortraitsEnabled)
                {
                    _aiPortraitsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Reveal All Character Relationships", Order = 0, RequireRestart = false, IsToggle = true, HintText = "Cheat: reveals exact relationship levels for every character. Character skills are always visible.")]
        [SettingPropertyGroup("Cheats", GroupOrder = 6)]
        public bool RevealAllCharacterStatsAndRelationships
        {
            get { return _revealAllCharacterStatsAndRelationships; }
            set
            {
                if (value != _revealAllCharacterStatsAndRelationships)
                {
                    _revealAllCharacterStatsAndRelationships = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Enable Debug Controls", Order = 0, RequireRestart = false, IsToggle = true, HintText = "Enables the guarded test controls in this Debug tab. Leave off during ordinary play.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
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

        public bool DialogueComplianceTestModeEnabled
        {
            get { return _dialogueComplianceTestModeEnabled; }
            set
            {
                if (value != _dialogueComplianceTestModeEnabled)
                {
                    _dialogueComplianceTestModeEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("Start Event In Current Town", Order = 3, RequireRestart = false, Content = "Start Event", HintText = "Creates a Bannerlord Reign social event in the town you are currently visiting. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ForceEventInCurrentTownAction
        {
            get { return _forceEventInCurrentTownAction; }
            set { _forceEventInCurrentTownAction = value ?? ReignBetaDebugActions.ForceEventInCurrentTown; }
        }

        [SettingPropertyButton("Open Party Chat", Order = 1, RequireRestart = false, Content = "Open", HintText = "Opens the Bannerlord Reign party/group chat overlay. The hotkey is backslash.")]
        [SettingPropertyGroup("Utilities", GroupOrder = 4)]
        public Action OpenPartyChatAction
        {
            get { return _openPartyChatAction; }
            set { _openPartyChatAction = value ?? ReignBetaDebugActions.OpenPartyChat; }
        }

        [SettingPropertyButton("Open Correspondence", Order = 2, RequireRestart = false, Content = "Open", HintText = "Opens correspondence with known living characters. Default hotkey is Ctrl+M.")]
        [SettingPropertyGroup("Utilities", GroupOrder = 4)]
        public Action OpenCorrespondenceAction
        {
            get { return _openCorrespondenceAction; }
            set { _openCorrespondenceAction = value ?? ReignBetaDebugActions.OpenCorrespondence; }
        }

        [SettingPropertyButton("Start Wilderness Event", Order = 4, RequireRestart = false, Content = "Start Event", HintText = "Forces a standalone Bannerlord Reign wilderness social event when the campaign map is in a valid travel state. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ForceGeneratedWildernessEventAction
        {
            get { return _forceGeneratedWildernessEventAction; }
            set { _forceGeneratedWildernessEventAction = value ?? ReignBetaDebugActions.ForceGeneratedWildernessEvent; }
        }

        [SettingPropertyButton("Know Every Living Hero", Order = 2, RequireRestart = false, Content = "Know Everyone", HintText = "Marks the player as knowing every living hero in both Bannerlord and Reign. It does not make every hero know the player. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action MakePlayerKnowEveryoneAction
        {
            get { return _makePlayerKnowEveryoneAction; }
            set { _makePlayerKnowEveryoneAction = value ?? ReignBetaDebugActions.MakePlayerKnowEveryone; }
        }

        [SettingPropertyButton("Prepare Royal Court Test Realm", Order = 5, RequireRestart = false, Content = "Prepare Realm", HintText = "On a dedicated test save, makes the player the ruler of a supplied kingdom with two personal fortifications and four regular noble vassal clans. The player is moved inside the granted town when setup completes. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action PrepareRoyalCourtTestRealmAction
        {
            get { return _prepareRoyalCourtTestRealmAction; }
            set { _prepareRoyalCourtTestRealmAction = value ?? ReignBetaDebugActions.ConfirmSetupPlayerKingdomTestScenario; }
        }

        [SettingPropertyButton("Test Random Ruler Diplomacy Roll", Order = 6, RequireRestart = false, Content = "Roll Initiative", HintText = "Selects one eligible non-player ruler and makes their normal, unmodified diplomacy initiative rolls using the seven main court trait groups. An event is created only if a real roll passes and the ruler chooses a valid action. Requires Debug Controls and the local diplomacy systems.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action TriggerRandomDiplomacyPopupTestAction
        {
            get { return _triggerRandomDiplomacyPopupTestAction; }
            set { _triggerRandomDiplomacyPopupTestAction = value ?? ReignBetaDebugActions.TriggerRandomDiplomacyPopupTest; }
        }

        [SettingPropertyButton("Force Kingdom Catastrophe or Boon", Order = 40, RequireRestart = false, Content = "Choose Event", HintText = "Choose any kingdom event archetype and apply it to a valid kingdom without changing the production 1% daily chance. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ForceKingdomEventAction
        {
            get { return _forceKingdomEventAction; }
            set { _forceKingdomEventAction = value ?? ReignBetaDebugActions.OpenForceKingdomEventMenu; }
        }

        [SettingPropertyButton("Run NPC Dialogue Audit", Order = 7, RequireRestart = false, Content = "Choose NPC", HintText = "Searches for an adult living NPC, opens the production individual-chat overlay, and runs 30 audited exchanges across five scenes. Keeps test history and memories in this save. Requires Debug Controls and the local server.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action StartNpcDialogueAuditAction
        {
            get { return _startNpcDialogueAuditAction; }
            set { _startNpcDialogueAuditAction = value ?? ReignNpcDialogueAuditRunner.StartFromMcm; }
        }

        [SettingPropertyButton("Stop NPC Dialogue Audit", Order = 8, RequireRestart = false, Content = "Stop", HintText = "Stops the active NPC dialogue audit after the current server request and preserves a partial report.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action StopNpcDialogueAuditAction
        {
            get { return _stopNpcDialogueAuditAction; }
            set { _stopNpcDialogueAuditAction = value ?? ReignNpcDialogueAuditRunner.StopFromMcm; }
        }

        [SettingPropertyButton("Show NPC Dialogue Audit Report", Order = 9, RequireRestart = false, Content = "Show", HintText = "Shows the latest run status, assertion totals, and durable server report path.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ShowNpcDialogueAuditSummaryAction
        {
            get { return _showNpcDialogueAuditSummaryAction; }
            set { _showNpcDialogueAuditSummaryAction = value ?? ReignNpcDialogueAuditRunner.ShowLastSummaryFromMcm; }
        }

        [SettingPropertyButton("Request Conversation Portrait", Order = 10, RequireRestart = false, Content = "Request", HintText = "Queues an AI portrait for the hero currently in one-to-one conversation. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action RequestConversationPortraitAction
        {
            get { return _requestConversationPortraitAction; }
            set { _requestConversationPortraitAction = value ?? ReignBetaDebugActions.RequestConversationPortrait; }
        }

        [SettingPropertyButton("Create Player Portrait", Order = 11, RequireRestart = false, Content = "Create", HintText = "Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action CreatePlayerPortraitAction
        {
            get { return _createPlayerPortraitAction; }
            set { _createPlayerPortraitAction = value ?? ReignBetaDebugActions.CreatePlayerPortrait; }
        }

        [SettingPropertyButton("Clear Player Portrait", Order = 12, RequireRestart = false, Content = "Clear", HintText = "Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ClearPlayerPortraitAction
        {
            get { return _clearPlayerPortraitAction; }
            set { _clearPlayerPortraitAction = value ?? ReignBetaDebugActions.ClearPlayerPortrait; }
        }

		[SettingPropertyButton("Reload Portraits From Disk", Order = 13, RequireRestart = false, Content = "Reload", HintText = "Clears in-memory portrait textures and rebuilds campaign hero links so shared and campaign-local portrait files are loaded fresh. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ReloadPortraitsFromDiskAction
        {
            get { return _reloadPortraitsFromDiskAction; }
            set { _reloadPortraitsFromDiskAction = value ?? ReignBetaDebugActions.ReloadPortraitsFromDisk; }
        }

		[SettingPropertyButton("Prepare Shared Portrait Cache", Order = 14, RequireRestart = false, Content = "Prepare", HintText = "Validates shared portrait display derivatives. Legacy custom portraits remain unchanged; full-body portrait.png masters use the chest and full-body tiers. Requires Debug Controls.")]
		[SettingPropertyGroup("Debug", GroupOrder = 5)]
		public Action PrepareSharedPortraitCacheAction
		{
			get { return _prepareSharedPortraitCacheAction; }
			set { _prepareSharedPortraitCacheAction = value ?? ReignBetaDebugActions.PrepareSharedPortraitCache; }
		}

		[SettingPropertyButton("Start Lord Encyclopedia Source Scan", Order = 15, RequireRestart = false, Content = "Arm Scan", HintText = "Prepares every living lord and lady for full-size 3D source capture. After arming, open each lord's Encyclopedia page; Bannerlord Reign saves source.png and metadata into that hero's portrait folder. Requires Debug Controls.")]
		[SettingPropertyGroup("Debug", GroupOrder = 5)]
		public Action StartLordEncyclopediaSourceScanAction
		{
			get { return _startLordEncyclopediaSourceScanAction; }
			set { _startLordEncyclopediaSourceScanAction = value ?? ReignBetaDebugActions.StartLordEncyclopediaSourceScan; }
		}

		[SettingPropertyButton("Diagnose Current Conversation Portrait", Order = 16, RequireRestart = false, Content = "Diagnose", HintText = "Checks the current conversation hero's cache key, files, index link, and texture factory state. Full details are written to rgl_log.txt. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action DiagnoseCurrentConversationPortraitAction
        {
            get { return _diagnoseCurrentConversationPortraitAction; }
            set { _diagnoseCurrentConversationPortraitAction = value ?? ReignBetaDebugActions.DiagnoseCurrentConversationPortrait; }
        }

        [SettingPropertyButton("Diagnose Current NPC Identity", Order = 20, RequireRestart = false, Content = "Diagnose", HintText = "Shows whether the current one-to-one NPC knows, merely believes, or does not know the player's identity. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action DiagnoseCurrentIdentityAction
        {
            get { return _diagnoseCurrentIdentityAction; }
            set { _diagnoseCurrentIdentityAction = value ?? ReignBetaDebugActions.DiagnoseCurrentConversationIdentity; }
        }

        [SettingPropertyButton("Reset Current NPC Identity", Order = 21, RequireRestart = false, Content = "Reset", HintText = "Deletes the current one-to-one NPC's identity knowledge of the player so first-meeting behavior can be tested again. Requires Debug Controls.")]
        [SettingPropertyGroup("Debug", GroupOrder = 5)]
        public Action ResetCurrentIdentityAction
        {
            get { return _resetCurrentIdentityAction; }
            set { _resetCurrentIdentityAction = value ?? ReignBetaDebugActions.ResetCurrentConversationIdentity; }
        }
    }
}
