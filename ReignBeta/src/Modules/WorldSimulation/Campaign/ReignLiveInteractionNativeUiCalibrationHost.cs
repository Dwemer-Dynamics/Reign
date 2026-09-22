#if !REIGN_EXCLUDE_COURT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AIPortraits;
using Helpers;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.UI.Calibration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly HashSet<string> NativeUiCalibrationTargets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "native-initial-screen",
                "native-game-menu",
                "native-encyclopedia-hero",
                "native-clan-members",
                "native-marriage-offer",
                "native-heir-selection",
                "native-skill-grid-item",
                "native-character-developer",
                "native-crafting-hero",
                "native-crafting",
                "native-education",
                "native-recruit-volunteer",
                "native-game-menu-party",
                "native-conversation",
                "native-quests"
            };

        private static string _nativeUiCalibrationTarget = string.Empty;
        private static DateTime _nativeUiCalibrationReadyAfterUtc = DateTime.MinValue;
        private static string _nativeUiCalibrationFixtureHeroId = string.Empty;
        private static string _nativeUiCalibrationConversationToken = string.Empty;
        private static Hero _nativeUiCalibrationMarriagePlayerHero;
        private static Hero _nativeUiCalibrationMarriageOtherHero;

        private static bool IsNativeUiTarget(string target)
        {
            return !string.IsNullOrWhiteSpace(target)
                && NativeUiCalibrationTargets.Contains(target);
        }

        private static async Task<LiveCommandResult> NativeUiOpenAsync(string target)
        {
            if (string.Equals(target, "native-initial-screen", StringComparison.OrdinalIgnoreCase))
            {
                return LiveCommandResult.Failed(
                    "The native InitialScreen exists before a campaign heartbeat. Use the guarded ReignLiveTest game start-menu lifecycle route for that target.",
                    UiStateJson());
            }

            if (NativeCampaign.Current == null || Game.Current?.GameStateManager == null)
                return LiveCommandResult.Failed("A loaded Bannerlord campaign is required for this native UI calibration target.");

            string openError = await ReignMainThread.InvokeAsync(() =>
            {
                CloseCalibrationScreens();
                _uiCalibrationTarget = string.Empty;
                _uiCalibrationMovie = string.Empty;
                _nativeUiCalibrationTarget = target;
                _nativeUiCalibrationReadyAfterUtc = DateTime.UtcNow.AddSeconds(2);
                _nativeUiCalibrationFixtureHeroId = string.Empty;
                _nativeUiCalibrationConversationToken = string.Empty;
                if (!ReignUiCalibrationService.TryAuthorizeHeadlessNativeMovie(
                    NativeUiMovieForTarget(target), out string authorizationError))
                    return authorizationError;
                return OpenNativeUiCalibrationTarget(target);
            }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(openError))
            {
                await ReignMainThread.InvokeAsync(CloseNativeUiCalibrationTarget).ConfigureAwait(false);
                return LiveCommandResult.Failed(openError, UiStateJson());
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!(string.Equals(target, "native-crafting-hero", StringComparison.OrdinalIgnoreCase)
                        ? Game.Current?.GameStateManager?.ActiveState is CraftingState
                        : IsNativeUiTargetOpen(target))
                    && DateTime.UtcNow < deadline)
                await Task.Delay(100).ConfigureAwait(false);

            if (string.Equals(target, "native-crafting-hero", StringComparison.OrdinalIgnoreCase))
            {
                string popupError = await ReignMainThread.InvokeAsync(OpenCraftingHeroPopup).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(popupError))
                {
                    await ReignMainThread.InvokeAsync(CloseNativeUiCalibrationTarget).ConfigureAwait(false);
                    return LiveCommandResult.Failed(popupError, UiStateJson());
                }
                deadline = DateTime.UtcNow.AddSeconds(15);
                while (!IsNativeUiTargetOpen(target) && DateTime.UtcNow < deadline)
                    await Task.Delay(100).ConfigureAwait(false);
            }

            if (!IsNativeUiTargetOpen(target))
            {
                JObject state = UiStateJson();
                await ReignMainThread.InvokeAsync(CloseNativeUiCalibrationTarget).ConfigureAwait(false);
                return LiveCommandResult.Failed(
                    "The production native Bannerlord screen did not become visible before the bounded deadline.",
                    state);
            }

            _uiCalibrationTarget = target;
            _uiCalibrationMovie = NativeUiMovieForTarget(target);
            return LiveCommandResult.Completed(
                "Production native Bannerlord UI opened for Reign augmentation calibration.",
                UiStateJson());
        }

        private static string OpenNativeUiCalibrationTarget(string target)
        {
            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (mapState == null)
                return "The campaign map state is unavailable for native UI calibration.";

            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            bool requiresSettlement = target == "native-game-menu"
                || target == "native-game-menu-party"
                || target == "native-recruit-volunteer";
            if (requiresSettlement
                && (settlement == null || (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage)))
                return "This native UI target requires the player to be inside a settlement.";

            switch (target)
            {
                case "native-game-menu":
                    if (settlement?.IsTown != true)
                        return "The Reign tavern-art fixture requires the player to be inside a town.";
                    GameMenu.ActivateGameMenu("town_backstreet");
                    return string.Empty;

                case "native-game-menu-party":
                    GameMenu.ActivateGameMenu(settlement.IsVillage
                        ? "village"
                        : settlement.IsCastle ? "castle" : "town");
                    return string.Empty;

                case "native-recruit-volunteer":
                    GameMenu.ActivateGameMenu(settlement.IsVillage
                        ? "village"
                        : settlement.IsCastle ? "castle" : "town");
                    NativeCampaign.Current.CurrentMenuContext?.OpenRecruitVolunteers();
                    return NativeCampaign.Current.CurrentMenuContext?.Handler == null
                        ? "The native settlement menu handler is unavailable for the recruitment fixture."
                        : string.Empty;
            }

            if (mapState.AtMenu)
                mapState.ExitMenuMode();

            switch (target)
            {
                case "native-clan-members":
                {
                    ClanState state = Game.Current.GameStateManager.CreateState<ClanState>(
                        new object[] { Hero.MainHero });
                    Game.Current.GameStateManager.PushState(state);
                    return string.Empty;
                }
                case "native-character-developer":
                case "native-skill-grid-item":
                {
                    CharacterDeveloperState state =
                        Game.Current.GameStateManager.CreateState<CharacterDeveloperState>(
                            new object[] { Hero.MainHero });
                    Game.Current.GameStateManager.PushState(state);
                    return string.Empty;
                }
                case "native-crafting":
                case "native-crafting-hero":
                {
                    CraftingTemplate template = CraftingTemplate.All.FirstOrDefault();
                    if (template == null) return "No native crafting template is available.";
                    CraftingHelper.OpenCrafting(template);
                    return string.Empty;
                }
                case "native-education":
                {
                    Hero child = FindEducationCalibrationChild();
                    if (child == null)
                        return "No living child is available for the native education fixture.";
                    _nativeUiCalibrationFixtureHeroId = child.StringId ?? string.Empty;
                    EducationState state = Game.Current.GameStateManager.CreateState<EducationState>(
                        new object[] { child });
                    Game.Current.GameStateManager.PushState(state);
                    return string.Empty;
                }
                case "native-quests":
                {
                    QuestsState state = Game.Current.GameStateManager.CreateState<QuestsState>();
                    Game.Current.GameStateManager.PushState(state);
                    return string.Empty;
                }
                case "native-encyclopedia-hero":
                {
                    Hero hero = FindCalibrationHero();
                    if (hero == null) return "No adult hero is available for the native encyclopedia fixture.";
                    _nativeUiCalibrationFixtureHeroId = hero.StringId ?? string.Empty;
                    NativeCampaign.Current.EncyclopediaManager?.GoToLink(hero.EncyclopediaLink);
                    return string.Empty;
                }
                case "native-marriage-offer":
                {
                    Tuple<Hero, Hero> pair = FindMarriageCalibrationPair();
                    if (pair == null)
                        return "No suitable player-clan and foreign-clan hero pair is available for the native marriage-offer fixture.";
                    MarriageOfferCampaignBehavior behavior =
                        NativeCampaign.Current.GetCampaignBehavior<MarriageOfferCampaignBehavior>();
                    if (behavior == null)
                        return "The native MarriageOfferCampaignBehavior is unavailable.";
                    _nativeUiCalibrationMarriagePlayerHero = pair.Item1;
                    _nativeUiCalibrationMarriageOtherHero = pair.Item2;
                    _nativeUiCalibrationFixtureHeroId =
                        (pair.Item1.StringId ?? string.Empty) + "," + (pair.Item2.StringId ?? string.Empty);
                    behavior.CreateMarriageOffer(pair.Item1, pair.Item2);
                    Hero suitor = pair.Item1.IsFemale ? pair.Item2 : pair.Item1;
                    Hero maiden = pair.Item1.IsFemale ? pair.Item1 : pair.Item2;
                    CampaignEventDispatcher.Instance.OnMarriageOfferedToPlayer(suitor, maiden);
                    return string.Empty;
                }
                case "native-heir-selection":
                {
                    Dictionary<Hero, int> heirs = FindHeirCalibrationCandidates();
                    if (heirs.Count == 0)
                        return "No adult hero is available for the native heir-selection fixture.";
                    _nativeUiCalibrationFixtureHeroId = string.Join(",", heirs.Keys.Select(hero => hero.StringId));
                    return InvokeScreenMethod(
                        "OnHeirSelectionRequested",
                        new object[] { heirs },
                        out _)
                        ? string.Empty
                        : "The native map screen could not open HeirSelectionPopup.";
                }
                case "native-conversation":
                {
                    Hero hero = FindCalibrationHero();
                    if (hero?.CharacterObject == null)
                        return "No adult hero is available for the native conversation fixture.";
                    _nativeUiCalibrationConversationToken = Guid.NewGuid().ToString("N");
                    ConversationPortraitMixin.BeginAutomationProbeFromPatch(_nativeUiCalibrationConversationToken);
                    _nativeUiCalibrationFixtureHeroId = hero.StringId ?? string.Empty;
                    ConversationCharacterData player = new ConversationCharacterData(
                        CharacterObject.PlayerCharacter,
                        PartyBase.MainParty,
                        true,
                        true,
                        false,
                        true,
                        true,
                        true);
                    ConversationCharacterData partner = new ConversationCharacterData(
                        hero.CharacterObject,
                        hero.PartyBelongedTo?.Party,
                        true,
                        true,
                        false,
                        true,
                        true,
                        true);
                    CampaignMapConversation.OpenConversation(player, partner);
                    return string.Empty;
                }
                default:
                    return "Unsupported native UI calibration target '" + target + "'.";
            }
        }

        private static string OpenCraftingHeroPopup()
        {
            if (!(Game.Current?.GameStateManager?.ActiveState is CraftingState))
                return "The native crafting state is not active.";
            object dataSource = GetScreenMemberValue("_dataSource");
            object popup = GetMemberValue(dataSource, "CraftingHeroPopup");
            if (popup == null)
                return "The production CraftingHeroPopup view model is unavailable.";
            MethodInfo open = FindInstanceMethod(popup.GetType(), "ExecuteOpenPopup", 0);
            if (open == null)
                return "The production CraftingHeroPopup open command is unavailable.";
            open.Invoke(popup, null);
            _nativeUiCalibrationReadyAfterUtc = DateTime.UtcNow.AddSeconds(1);
            return string.Empty;
        }

        private static bool IsNativeUiTargetOpen(string target)
        {
            bool open;
            switch (target)
            {
                case "native-clan-members":
                    open = Game.Current?.GameStateManager?.ActiveState is ClanState;
                    break;
                case "native-character-developer":
                case "native-skill-grid-item":
                    open = Game.Current?.GameStateManager?.ActiveState is CharacterDeveloperState;
                    break;
                case "native-crafting":
                    open = Game.Current?.GameStateManager?.ActiveState is CraftingState;
                    break;
                case "native-crafting-hero":
                {
                    object popup = GetMemberValue(GetScreenMemberValue("_dataSource"), "CraftingHeroPopup");
                    open = Game.Current?.GameStateManager?.ActiveState is CraftingState
                        && ReadBooleanMember(popup, "IsVisible");
                    break;
                }
                case "native-education":
                    open = Game.Current?.GameStateManager?.ActiveState is EducationState;
                    break;
                case "native-quests":
                    open = Game.Current?.GameStateManager?.ActiveState is QuestsState;
                    break;
                case "native-game-menu":
                    open = IsCurrentMenu("town_backstreet");
                    break;
                case "native-game-menu-party":
                    open = IsSettlementMenuOpen();
                    break;
                case "native-recruit-volunteer":
                    open = ReadBooleanMember(ScreenManager.TopScreen, "IsInRecruitment");
                    break;
                case "native-encyclopedia-hero":
                    open = ReadBooleanMember(
                        GetMemberValue(ScreenManager.TopScreen, "EncyclopediaScreenManager"),
                        "IsEncyclopediaOpen");
                    break;
                case "native-marriage-offer":
                    open = ReadBooleanMember(ScreenManager.TopScreen, "IsMarriageOfferPopupActive");
                    break;
                case "native-heir-selection":
                    open = ReadBooleanMember(ScreenManager.TopScreen, "IsHeirSelectionPopupActive");
                    break;
                case "native-conversation":
                    open = NativeCampaign.Current?.ConversationManager?.IsConversationInProgress == true
                        && ConversationPortraitMixin.AutomationAttached
                        && !string.IsNullOrWhiteSpace(_nativeUiCalibrationConversationToken)
                        && string.Equals(
                            ConversationPortraitMixin.AutomationProbeToken,
                            _nativeUiCalibrationConversationToken,
                            StringComparison.Ordinal);
                    break;
                default:
                    open = false;
                    break;
            }
            return open && DateTime.UtcNow >= _nativeUiCalibrationReadyAfterUtc;
        }

        private static void CloseNativeUiCalibrationTarget()
        {
            string target = _nativeUiCalibrationTarget;
            string conversationToken = _nativeUiCalibrationConversationToken;
            try
            {
                if (string.IsNullOrWhiteSpace(target)) return;
                switch (target)
                {
                    case "native-crafting-hero":
                    {
                        object popup = GetMemberValue(GetScreenMemberValue("_dataSource"), "CraftingHeroPopup");
                        FindInstanceMethod(popup?.GetType(), "ExecuteClosePopup", 0)?.Invoke(popup, null);
                        goto case "native-crafting";
                    }
                    case "native-clan-members":
                    case "native-character-developer":
                    case "native-skill-grid-item":
                    case "native-crafting":
                    case "native-education":
                    case "native-quests":
                        if (!(Game.Current?.GameStateManager?.ActiveState is MapState))
                            Game.Current?.GameStateManager?.PopState(0);
                        break;
                    case "native-encyclopedia-hero":
                    {
                        object encyclopedia = GetMemberValue(ScreenManager.TopScreen, "EncyclopediaScreenManager");
                        FindInstanceMethod(encyclopedia?.GetType(), "CloseEncyclopedia", 0)?.Invoke(encyclopedia, null);
                        break;
                    }
                    case "native-marriage-offer":
                        if (_nativeUiCalibrationMarriagePlayerHero != null &&
                            _nativeUiCalibrationMarriageOtherHero != null)
                        {
                            Hero suitor = _nativeUiCalibrationMarriagePlayerHero.IsFemale
                                ? _nativeUiCalibrationMarriageOtherHero
                                : _nativeUiCalibrationMarriagePlayerHero;
                            Hero maiden = _nativeUiCalibrationMarriagePlayerHero.IsFemale
                                ? _nativeUiCalibrationMarriagePlayerHero
                                : _nativeUiCalibrationMarriageOtherHero;
                            CampaignEventDispatcher.Instance.OnMarriageOfferCanceled(suitor, maiden);
                        }
                        else
                        {
                            InvokeScreenMethod("CloseMarriageOfferPopup", null, out _);
                        }
                        _nativeUiCalibrationMarriagePlayerHero = null;
                        _nativeUiCalibrationMarriageOtherHero = null;
                        break;
                    case "native-heir-selection":
                        InvokeScreenMethod("OnHeirSelectionOver", new object[] { null }, out _);
                        break;
                    case "native-recruit-volunteer":
                    {
                        object handler = NativeCampaign.Current?.CurrentMenuContext?.Handler;
                        FindInstanceMethod(handler?.GetType(), "CloseRecruitVolunteers", 0)?.Invoke(handler, null);
                        break;
                    }
                    case "native-conversation":
                        bool exactCalibrationConversation = !string.IsNullOrWhiteSpace(conversationToken)
                            && string.Equals(
                                ConversationPortraitMixin.AutomationProbeToken,
                                conversationToken,
                                StringComparison.Ordinal);
                        if (exactCalibrationConversation
                            && NativeCampaign.Current?.ConversationManager?.IsConversationInProgress == true)
                            NativeCampaign.Current.ConversationManager.EndConversation();
                        break;
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Native UI calibration close failed for " + target + ": " + ex.Message);
            }
            finally
            {
                ConversationPortraitMixin.ResetAutomationProbeFromPatch(conversationToken);
                _nativeUiCalibrationTarget = string.Empty;
                _nativeUiCalibrationReadyAfterUtc = DateTime.MinValue;
                _nativeUiCalibrationFixtureHeroId = string.Empty;
                _nativeUiCalibrationConversationToken = string.Empty;
                ReignUiCalibrationService.ClearAuthorizedHeadlessNativeMovie();
            }
        }

        private static void AppendNativeUiCalibrationState(JObject state)
        {
            string target = _nativeUiCalibrationTarget;
            state["nativeUiCalibrationTarget"] = target;
            state["nativeUiCalibrationOpen"] = IsNativeUiTarget(target)
                && IsNativeUiTargetOpen(target);
            state["nativeUiCalibrationReady"] = IsNativeUiTarget(target)
                && IsNativeUiTargetOpen(target);
            state["nativeUiCalibrationFixtureHeroId"] = _nativeUiCalibrationFixtureHeroId;
            state["nativeUiCalibrationProviderFree"] = true;
            state["nativeUiCalibrationSavedCampaign"] = false;
            state["nativeUiCalibrationActiveState"] =
                Game.Current?.GameStateManager?.ActiveState?.GetType().FullName ?? string.Empty;
            state["nativeUiCalibrationTopScreen"] =
                ScreenManager.TopScreen?.GetType().FullName ?? string.Empty;
            if (string.Equals(target, "native-conversation", StringComparison.OrdinalIgnoreCase))
            {
                state["nativeConversationPortraitMixinAttached"] = ConversationPortraitMixin.AutomationAttached;
                state["nativeConversationCalibrationBound"] = !string.IsNullOrWhiteSpace(_nativeUiCalibrationConversationToken)
                    && string.Equals(
                        ConversationPortraitMixin.AutomationProbeToken,
                        _nativeUiCalibrationConversationToken,
                        StringComparison.Ordinal);
                state["nativeConversationPortraitZoomVisible"] = ConversationPortraitMixin.AutomationPortraitZoomVisible;
                state["nativeConversationPortraitZoomSide"] = ConversationPortraitMixin.AutomationPortraitZoomSide;
            }
        }

        private static bool TryExecuteNativeUiAutomationAction(
            string target,
            string action,
            out string error)
        {
            error = string.Empty;
            if (!string.Equals(target, "native-conversation", StringComparison.OrdinalIgnoreCase))
            {
                error = "No native UI automation actions are registered for '" + target + "'.";
                return false;
            }
            if (!string.Equals(_nativeUiCalibrationTarget, target, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(_nativeUiCalibrationConversationToken))
            {
                error = "Native Conversation automation is restricted to the exact calibration fixture opened by this LiveTest session.";
                return false;
            }
            if (NativeCampaign.Current?.ConversationManager?.IsConversationInProgress != true)
            {
                error = "The native Conversation screen is not open.";
                return false;
            }
            return ConversationPortraitMixin.TryExecuteAutomationAction(
                _nativeUiCalibrationConversationToken,
                action,
                out error);
        }

        private static string NativeUiMovieForTarget(string target)
        {
            switch (target)
            {
                case "native-game-menu": return "GameMenu";
                case "native-encyclopedia-hero": return "EncyclopediaHeroPage";
                case "native-clan-members": return "ClanMembers";
                case "native-marriage-offer": return "MarriageOfferPopup";
                case "native-heir-selection": return "HeirSelectionPopup";
                case "native-skill-grid-item": return "SkillGridItem";
                case "native-character-developer": return "CharacterDeveloper";
                case "native-crafting-hero": return "CraftingHeroPopup";
                case "native-crafting": return "Crafting";
                case "native-education": return "EducationGainedProperties";
                case "native-recruit-volunteer": return "RecruitVolunteerTuple";
                case "native-game-menu-party": return "GameMenuPartyItem";
                case "native-conversation": return "SPConversation";
                case "native-quests": return "QuestsScreen";
                default: return string.Empty;
            }
        }

        private static Hero FindEducationCalibrationChild()
        {
            Hero fallback = null;
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || !hero.IsAlive || !hero.IsChild) continue;
                if (hero.Clan == Clan.PlayerClan) return hero;
                if (fallback == null) fallback = hero;
            }
            return fallback;
        }

        private static Tuple<Hero, Hero> FindMarriageCalibrationPair()
        {
            Clan playerClan = Clan.PlayerClan;
            var marriageModel = NativeCampaign.Current?.Models?.MarriageModel;
            if (playerClan == null || marriageModel == null) return null;

            foreach (Hero playerHero in playerClan.Heroes)
            {
                if (playerHero == null ||
                    playerHero.IsChild ||
                    !playerHero.IsAlive ||
                    playerHero.IsPrisoner ||
                    playerHero.Clan == null)
                    continue;
                foreach (Hero otherHero in Hero.AllAliveHeroes)
                {
                    if (otherHero == null ||
                        otherHero.IsChild ||
                        !otherHero.IsAlive ||
                        otherHero.IsPrisoner ||
                        otherHero.Clan == null ||
                        otherHero.Clan == playerClan ||
                        otherHero.Clan.IsEliminated ||
                        otherHero.IsFemale == playerHero.IsFemale)
                        continue;
                    if (marriageModel.IsCoupleSuitableForMarriage(playerHero, otherHero))
                        return Tuple.Create(playerHero, otherHero);
                }
            }
            return null;
        }

        private static Dictionary<Hero, int> FindHeirCalibrationCandidates()
        {
            Dictionary<Hero, int> heirs = new Dictionary<Hero, int>();
            int score = 80;
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero.IsChild || !hero.IsAlive || hero.IsPrisoner) continue;
                heirs[hero] = score;
                score -= 10;
                if (heirs.Count >= 3) break;
            }
            return heirs;
        }

        private static bool IsCurrentMenu(string menuId)
        {
            return Game.Current?.GameStateManager?.ActiveState is MapState mapState
                && mapState.AtMenu
                && string.Equals(
                    NativeCampaign.Current?.CurrentMenuContext?.GameMenu?.StringId,
                    menuId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSettlementMenuOpen()
        {
            string menuId = NativeCampaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? string.Empty;
            return Game.Current?.GameStateManager?.ActiveState is MapState mapState
                && mapState.AtMenu
                && (menuId == "town" || menuId == "castle" || menuId == "village");
        }

        private static object GetScreenMemberValue(string memberName)
        {
            return GetMemberValue(ScreenManager.TopScreen, memberName);
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName)) return null;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null) return property.GetValue(instance, null);
                FieldInfo field = type.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }

        private static bool ReadBooleanMember(object instance, string memberName)
        {
            object value = GetMemberValue(instance, memberName);
            return value is bool result && result;
        }

        private static MethodInfo FindInstanceMethod(
            Type type,
            string methodName,
            int? parameterCount = null)
        {
            while (type != null)
            {
                MethodInfo method = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                        && (!parameterCount.HasValue
                            || candidate.GetParameters().Length == parameterCount.Value));
                if (method != null) return method;
                type = type.BaseType;
            }
            return null;
        }

        private static bool InvokeScreenMethod(
            string methodName,
            object[] arguments,
            out object result)
        {
            result = null;
            object screen = ScreenManager.TopScreen;
            int parameterCount = arguments?.Length ?? 0;
            MethodInfo method = FindInstanceMethod(
                screen?.GetType(),
                methodName,
                parameterCount);
            if (method == null) return false;
            result = method.Invoke(screen, arguments);
            return true;
        }
    }
}
#endif
