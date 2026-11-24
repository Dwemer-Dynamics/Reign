using ArenaOverhaul.ArenaPractice;
using ArenaOverhaul.CampaignBehaviors;
using ArenaOverhaul.CampaignBehaviors.BehaviorManagers;
using ArenaOverhaul.Extensions;
using ArenaOverhaul.Helpers;
using ArenaOverhaul.Models;
using ArenaOverhaul.ModSettings;

using Bannerlord.BUTR.Shared.Helpers;
using Bannerlord.ButterLib.HotKeys;
using Bannerlord.UIExtenderEx;

using HarmonyLib;

using MCM.Abstractions.Base.PerSave;

using System;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ArenaOverhaul
{
    public class SubModule : MBSubModuleBase
    {
        private const string SLoaded = "{=miUO4w1hn}Loaded Arena Overhaul!";
        private const string SErrorLoading = "{=5KIeqRJJX}Arena Overhaul failed to load! See details in the mod log.";
        private const string SErrorInitialising = "{=LRhyA9mqB}Error initialising Arena Overhaul! See details in the mod log. Error text: \"{EXCEPTION_MESSAGE}\"";

        private static readonly UIExtender Extender = UIExtender.Create("ArenaOverhaulUI");
        private static HotKeyManager? AOHotKeyManager;

        internal static FluentPerSaveSettings? PerSaveSettings;

        public bool Patched { get; private set; }
        public bool OnBeforeInitialModuleScreenSetAsRootWasCalled { get; private set; } = false;

        private Harmony? _arenaOverhaulHarmonyInstance;
        private bool _uiActivationNeedsCleanup;
        public Harmony? ArenaOverhaulHarmonyInstance { get => _arenaOverhaulHarmonyInstance; private set => _arenaOverhaulHarmonyInstance = value; }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Patched = false;

            Extender.Register(typeof(SubModule).Assembly);
        }

        protected override void OnSubModuleUnloaded()
        {
            try
            {
                DisableArenaUi();
            }
            finally
            {
                Patched = false;
                try { HarmonyHelper.RollBack(_arenaOverhaulHarmonyInstance); }
                finally { base.OnSubModuleUnloaded(); }
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            try
            {
                if (OnBeforeInitialModuleScreenSetAsRootWasCalled)
                {
                    return;
                }
                OnBeforeInitialModuleScreenSetAsRootWasCalled = true;

                if (!HarmonyHelper.PatchAll(ref _arenaOverhaulHarmonyInstance, "Arena hook installation", "Initialization error - {0}"))
                {
                    MessageHelper.ErrorMessage(SErrorLoading.ToLocalizedString());
                    return;
                }

                try
                {
                    _uiActivationNeedsCleanup = true;
                    Extender.Enable();
                }
                catch
                {
                    try { DisableArenaUi(); }
                    finally { HarmonyHelper.RollBack(_arenaOverhaulHarmonyInstance); }
                    throw;
                }

                Patched = true;
                RegisterOptionalTeamPracticeShortcut();
                InformationManager.DisplayMessage(new InformationMessage(SLoaded.ToLocalizedString(), Color.FromUint(4282569842U)));
            }
            catch (Exception ex)
            {
                DebugHelper.HandleException(ex, "OnBeforeInitialModuleScreenSetAsRoot", "Initialization error - {0}", SErrorInitialising);
            }
        }

        private void DisableArenaUi()
        {
            if (!_uiActivationNeedsCleanup)
                return;

            Extender.Disable();
            _uiActivationNeedsCleanup = false;
        }

        // Team practice itself does not depend on its optional hero-switch shortcut.
        private static void RegisterOptionalTeamPracticeShortcut()
        {
            try
            {
                AOHotKeyManager ??= HotKeyManager.Create("ArenaOverhaul");
                if (AOHotKeyManager is not null)
                {
                    AOHotKeyManager.Add<TeamPracticeHotKeyController>();
                    AOHotKeyManager.Build();
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.TryLogOptionalIssue($"Optional shortcut unavailable - {ex}", "Arena team-practice shortcut");
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (Patched && game.GameType is Campaign)
            {
                //CampaignGameStarter
                CampaignGameStarter gameStarter = (CampaignGameStarter) gameStarterObject;
                //Behaviors
                AOArenaBehaviorManager._companionPracticeSettings ??= [];
                gameStarter.AddBehavior(new AOArenaBehavior());
                //Models
                AddGameModels(gameStarterObject, gameStarter);
            }
        }

        private void AddGameModels(IGameStarter gameStarterObject, CampaignGameStarter gameStarter)
        {
            DecoratorModelHelper.AddDecoratorModel<TournamentModel, ArenaOverhaulTournamentModel, DefaultTournamentModel>(gameStarterObject, gameStarter, (previouslyAssignedModel) => new ArenaOverhaulTournamentModel(previouslyAssignedModel));
        }

        public override void OnAfterGameInitializationFinished(Game game, object starterObject)
        {
            if (!Patched || game.GameType is not Campaign)
            {
                return;
            }

            CompanionPracticeSettings.RegisterCompanionPracticeSettings();
        }

        public override void OnGameEnd(Game game)
        {
            if (Patched && game.GameType is Campaign)
                CompanionPracticeSettings.UnregisterCompanionPracticeSettings();
        }
    }
}