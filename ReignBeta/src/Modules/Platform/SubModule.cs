using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AIPortraits;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using ReignBeta.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
using ReignBeta.Court.TrainingYard;
using ReignBeta.Court.WarCouncil;
#endif
using ReignBeta.Integration;
using ReignBeta.Economy;
using ReignBeta.Family;
using ReignBeta.Government;
using ReignBeta.PartyAgency;
using ReignBeta.Runtime;
using ReignBeta.UI;
using ReignBeta.UI.HiddenInformation;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static readonly Dictionary<string, DateTime> LastTickFailureLogUtc =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static Assembly _managedUnsafeAssembly;
        private static bool _managedDependencyResolverRegistered;
        private Harmony _portraitHarmony;
        private UIExtender _uiExtender;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            EnsureManagedRuntimeDependencies();
            ReignEncyclopediaSafetyPatch.Apply();
            ReignPregnancyPatches.Apply();
            ReignMarriagePatches.Apply();
            ReignDiplomacyPatches.Apply();
            ReignIndividualRelationPatches.Apply();
            ReignDungeonEscapePatches.Apply();
            ReignArrestCustodyPatches.Apply();
            ReignHiddenInformationPatches.Apply();
            ReignSaveSyncDeletionPatch.Apply();
            ReignEconomyPatches.Apply();
            ReignTemporaryPartyGuestPatches.Apply();
            ReignMainMenuVideoPatches.Apply();
            ReignPopupKeyboardPatches.Apply();
#if !REIGN_EXCLUDE_COURT
            ReignCourtNobleSafetyPatches.Apply();
#endif
            ReignRuntimeSpriteSheets.EnsureLoaded(true);
            LoadPortraitSystem();
        }

        private static void EnsureManagedRuntimeDependencies()
        {
            if (_managedUnsafeAssembly != null)
            {
                return;
            }

            // System.Memory 4.0.1.1 requests Unsafe 4.0.4.1 while the portrait
            // codec and Reign client are compiled against the compatible v6
            // implementation. Bannerlord has no application binding redirect,
            // so load the packaged implementation before ButterLib first uses
            // System.Memory instead of relying on incidental JIT order.
            string moduleDirectory = Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
            string dependencyPath = Path.Combine(moduleDirectory ?? string.Empty,
                "System.Runtime.CompilerServices.Unsafe.dll");
            if (!File.Exists(dependencyPath))
            {
                throw new FileNotFoundException("The Reign managed dependency package is incomplete.", dependencyPath);
            }

            Assembly dependency = Assembly.LoadFrom(dependencyPath);
            if (!string.Equals(dependency.GetName().Name, "System.Runtime.CompilerServices.Unsafe",
                StringComparison.Ordinal))
            {
                throw new FileLoadException("The packaged Unsafe dependency has an unexpected assembly identity.",
                    dependencyPath);
            }

            _managedUnsafeAssembly = dependency;
            if (!_managedDependencyResolverRegistered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedRuntimeDependency;
                _managedDependencyResolverRegistered = true;
            }
        }

        private static Assembly ResolveManagedRuntimeDependency(object sender, ResolveEventArgs args)
        {
            AssemblyName requestedAssembly;
            try
            {
                requestedAssembly = new AssemblyName(args.Name);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return string.Equals(requestedAssembly.Name, "System.Runtime.CompilerServices.Unsafe",
                StringComparison.OrdinalIgnoreCase)
                ? _managedUnsafeAssembly
                : null;
        }

        protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
        {
            if (starterObject is CampaignGameStarter campaignStarter)
            {
                campaignStarter.AddBehavior(new ReignSaveSyncCampaignBehavior());
                campaignStarter.AddModel(new ReignVillageProductionCalculatorModel());
                campaignStarter.AddModel(new ReignSettlementFoodModel());
                campaignStarter.AddModel(new ReignSettlementSecurityModel());
                campaignStarter.AddModel(new ReignSettlementLoyaltyModel());
                campaignStarter.AddModel(new ReignSettlementProsperityModel());
                campaignStarter.AddModel(new ReignBuildingConstructionModel());
                campaignStarter.AddModel(new ReignBeta.ClanAccords.ReignClanAccordsFinanceModel());
                campaignStarter.AddModel(new ReignBeta.ClanAccords.ReignClanAccordsWageModel());
                campaignStarter.AddBehavior(new ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior());
                campaignStarter.AddBehavior(new ReignEconomyCampaignBehavior());
                campaignStarter.AddBehavior(new ReignKingdomEventsCampaignBehavior());
                campaignStarter.AddBehavior(new ReignGovernmentCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCampaignPreparationCampaignBehavior());
                campaignStarter.AddBehavior(new ReignSocialBalanceHarnessCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCharacterEditorCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCharacterKnowledgeCampaignBehavior());
                campaignStarter.AddBehavior(new ReignAICampaignBehavior());
                campaignStarter.AddBehavior(new ReignCampaignCommandBehavior());
                campaignStarter.AddBehavior(new ReignWorldHistoryCampaignBehavior());
                campaignStarter.AddBehavior(new ReignTemporaryPartyGuestCampaignBehavior());
                campaignStarter.AddBehavior(new ReignArrestCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCombatReputationCampaignBehavior());
                campaignStarter.AddBehavior(new ReignRulerReputationCampaignBehavior());
                campaignStarter.AddBehavior(new ReignRebellionCampaignBehavior());
                campaignStarter.AddBehavior(new ReignWorldDiplomacyCampaignBehavior());
                campaignStarter.AddBehavior(new ReignIndividualConversationBehavior());
                campaignStarter.AddBehavior(new ReignEncounteredResidentsCampaignBehavior());
                campaignStarter.AddBehavior(new ReignTavernHouseCampaignBehavior());
                campaignStarter.AddBehavior(new ReignWhoremongerCampaignBehavior());
                campaignStarter.AddBehavior(new ReignWandererPopulationCampaignBehavior());
                campaignStarter.AddBehavior(new ReignSocialEventsCampaignBehavior());
                campaignStarter.AddBehavior(new ReignRelationshipCampaignBehavior());
                campaignStarter.AddBehavior(new ReignFamilyCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCourtPersonalityReputationCampaignBehavior());
#if !REIGN_EXCLUDE_COURT
                campaignStarter.AddBehavior(new ReignWarCouncilCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCourtNobleCampaignBehavior());
                campaignStarter.AddBehavior(new ReignCourtCampaignBehavior());
                campaignStarter.AddBehavior(new ReignFamilyChambersCampaignBehavior());
                campaignStarter.AddBehavior(new ReignTrainingYardCampaignBehavior());
#endif
                // Apply authored native-character ages before new-campaign household planning.
                campaignStarter.AddBehavior(new ReignNativeStartingAgeCampaignBehavior());
                // Must follow authored court-house migration, appearance and home initialization.
                campaignStarter.AddBehavior(new ReignStartingChildrenCampaignBehavior());
                campaignStarter.AddBehavior(new PreloadCampaignBehavior());
                campaignStarter.AddBehavior(new ConversationBehavior());
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            RunTickStage("runtime sprite loading", () => ReignRuntimeSpriteSheets.EnsureLoaded());
            RunTickStage("main-thread dispatch", ReignMainThread.DrainOnMainThread);
            RunTickStage("Save Sync campaign lifecycle", ReignSaveSyncCoordinator.ObserveCampaignLifecycle);

            // Bannerlord exposes Campaign.Current a few frames before it creates
            // PlayerCharacter while a new campaign is being initialized. Campaign
            // services commonly resolve Hero.MainHero, whose getter cannot be used
            // safely during that partial state.
            if (TaleWorlds.CampaignSystem.Campaign.Current != null && CharacterObject.PlayerCharacter == null)
            {
                return;
            }

            RunTickStage(
                "campaign preparation",
                () => ReignCampaignPreparationCampaignBehavior.Instance?.ApplicationTick(dt));
            if (ReignCampaignInitializationGate.IsPending)
            {
                // Keep only the local control plane alive while the prerequisite
                // pass owns the campaign. It provides heartbeat, progress, save,
                // and shutdown/reload commands, but cannot start interactions.
                RunTickStage("live interaction host", () => ReignLiveInteractionTestHost.ApplicationTick(dt));
                RunTickStage("notable generation popup", () => ReignNotableGenerationPopupManager.ApplicationTick(dt));
                return;
            }
            RunTickStage(
                "character editor polling",
                () => ReignCharacterEditorCampaignBehavior.Instance?.ApplicationTick(dt));

            RunTickStage("continuous relationship outbox", () => ReignServerClient.RelationshipRealtimeTick(dt));
            RunTickStage("continuous native relationship projection", () => ReignRelationshipCampaignBehavior.Instance?.ApplicationTick(dt));
            RunTickStage("continuous server action execution", () => ReignAICampaignBehavior.Instance?.ApplicationTick(dt));

            // Every subsystem is isolated. A portrait, overlay, queue, or legacy
            // audit failure must never silently stop the live-test heartbeat and
            // leave a remote run permanently unverifiable.
            RunTickStage("live interaction host", () => ReignLiveInteractionTestHost.ApplicationTick(dt));
            RunTickStage("NPC dialogue audit", () => ReignNpcDialogueAuditRunner.ApplicationTick(dt));
            RunTickStage("party dialogue audit", () => ReignPartyDialogueAuditRunner.ApplicationTick(dt));
            RunTickStage("portrait encyclopedia bridge", ReignPortraitBridge.ProcessQueuedEncyclopediaOpen);
            RunTickStage("individual chat overlay", () => ReignIndividualChatScreenManager.ApplicationTick(dt));
            RunTickStage("social event overlay", () => ReignSocialEventScreenManager.ApplicationTick(dt));
            RunTickStage("party chat overlay", () => ReignPartyChatScreenManager.ApplicationTick(dt));
            RunTickStage("tavern house overlay", () => ReignTavernHouseScreenManager.ApplicationTick(dt));
            RunTickStage("temporary noble guests", () => ReignTemporaryPartyGuestCampaignBehavior.Instance?.ApplicationTick());
            RunTickStage("correspondence overlay", () => ReignCorrespondenceScreenManager.ApplicationTick(dt));
            RunTickStage("duel launch prompt", ReignDuelLaunchPromptService.Tick);
            RunTickStage("diplomacy announcement overlay", () => ReignDiplomacyAnnouncementScreenManager.ApplicationTick(dt));
            RunTickStage("government overlay", () => ReignGovernmentScreenManager.ApplicationTick(dt));
#if !REIGN_EXCLUDE_COURT
            RunTickStage("court overlay", () => ReignCourtScreenManager.ApplicationTick(dt));
            RunTickStage("castle layout overlay", () => ReignCastleLayoutScreenManager.ApplicationTick(dt));
            RunTickStage("family chambers overlay", () => ReignFamilyChambersScreenManager.ApplicationTick(dt));
            RunTickStage("training yard overlay", () => ReignTrainingYardScreenManager.ApplicationTick(dt));
            RunTickStage("royal council overlay", () => ReignRoyalCouncilScreenManager.ApplicationTick(dt));
            RunTickStage("economic report overlay", () => ReignCourtEconomicReportScreenManager.ApplicationTick(dt));
            RunTickStage("clan accords overlay", () => ReignClanAccordsScreenManager.ApplicationTick(dt));
            RunTickStage("clan accords sync", () => ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.ApplicationTick(dt));
            RunTickStage("war council overlay", () => ReignWarCouncilScreenManager.ApplicationTick(dt));
            RunTickStage("ambassador overlay", () => ReignAmbassadorScreenManager.ApplicationTick(dt));
            RunTickStage("spymaster overlay", () => ReignSpymasterScreenManager.ApplicationTick(dt));
#endif
            RunTickStage("portrait queue", PortraitQueue.DrainOnMainThread);
            RunTickStage("memory queue", MemoryQueue.DrainOnMainThread);
            RunTickStage("diagnostics", DiagnosticsRunner.Tick);
            RunTickStage("action gauntlet", () => ReignActionGauntlet.ApplicationTick(dt));
        }

        private static void RunTickStage(string stage, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                DateTime now = DateTime.UtcNow;
                if (!LastTickFailureLogUtc.TryGetValue(stage, out DateTime last)
                    || now - last >= TimeSpan.FromSeconds(15))
                {
                    LastTickFailureLogUtc[stage] = now;
                    ReignLog.Exception("Application tick stage failed: " + stage, ex);
                }
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new ReignDuelMissionBehavior());
            mission.AddMissionBehavior(new ReignEncounteredResidentMissionBehavior());
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();
            _portraitHarmony?.UnpatchAll(_portraitHarmony.Id);
            ReignPregnancyPatches.Unapply();
            ReignMarriagePatches.Unapply();
            ReignDiplomacyPatches.Unapply();
            ReignIndividualRelationPatches.Unapply();
            ReignDungeonEscapePatches.Unapply();
            ReignArrestCustodyPatches.Unapply();
            ReignHiddenInformationPatches.Unapply();
            ReignSaveSyncDeletionPatch.Unapply();
            ReignEconomyPatches.Unapply();
            ReignMainMenuVideoPatches.Unapply();
            ReignPopupKeyboardPatches.Unapply();
#if !REIGN_EXCLUDE_COURT
            ReignCourtNobleSafetyPatches.Unapply();
#endif
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            ReignLog.Info("Bannerlord Reign initialized through ReignBeta module id.");
            InformationManager.DisplayMessage(new InformationMessage("Bannerlord Reign AI world foundation loaded.", Color.FromUint(0xFFFFD36A)));
        }

        private void LoadPortraitSystem()
        {
            try
            {
                PortraitCache.EnsureRootDirectory();
				PortraitDerivativeService.StartBackgroundSharedValidation();
                _portraitHarmony = new Harmony("com.bannerlordreign.reignbeta.portraits");
                PatchPortraitHarmony(typeof(PortraitPatch.TextureSetterPatch), true);
                PatchPortraitHarmony(typeof(PortraitPatch.CharacterTableauRenderPatch), true);
                PatchPortraitHarmony(typeof(PortraitPatch.OnRenderPatch), true);
                PatchPortraitHarmony(typeof(PortraitZoomButtonPatch), true);
                PatchPortraitHarmony(typeof(MovieDiagnosticsPatch), false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait Harmony setup failed: " + ex);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Portrait patches could not load: " + ex.Message, Color.FromUint(0xFFFF6600)));
            }

            try
            {
                _uiExtender = UIExtender.Create("ReignBeta");
                _uiExtender.Register(typeof(SubModule).Assembly);
                _uiExtender.Enable();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("UIExtender setup failed: " + ex.Message);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Conversation portrait UI disabled because UIExtenderEx is not available.", Color.FromUint(0xFFFF6600)));
            }
        }

        private void PatchPortraitHarmony(Type patchType, bool required)
        {
            try
            {
                _portraitHarmony.CreateClassProcessor(patchType).Patch();
                ReignLog.Info("Portrait Harmony patch loaded: " + patchType.FullName);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait Harmony patch failed for " + patchType.FullName + ": " + ex);
                if (required)
                {
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Portrait patch failed: " + patchType.Name + ". See rgl_log.txt.", Color.FromUint(0xFFFF6600)));
                }
            }
        }

    }
}
