using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketInterfaceContractTests
{
    [Fact]
    public void GenericUiCleanupClosesProductionCourtAudienceWithoutDecidingIt()
    {
        string root = TestOptions.FindWorkspace();
        string manager = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ReignCourtPetitionScreenManager.cs"));
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionUiCalibrationHost.cs"));

        int closeStart = manager.IndexOf("internal static bool TryCloseAutomationAudience()",
            StringComparison.Ordinal);
        int closeEnd = manager.IndexOf("private static void Close(bool returnToCourt)",
            closeStart, StringComparison.Ordinal);
        Assert.True(closeStart >= 0 && closeEnd > closeStart);
        string automationClose = manager[closeStart..closeEnd];
        Assert.Contains("_vm?.CalibrationMode == true", automationClose, StringComparison.Ordinal);
        Assert.Contains("Close(false);", automationClose, StringComparison.Ordinal);
        Assert.DoesNotContain("Execute", automationClose, StringComparison.Ordinal);

        int cleanupStart = host.IndexOf("private static void CloseCalibrationScreens()",
            StringComparison.Ordinal);
        int calibrationClose = host.IndexOf("ReignCourtPetitionScreenManager.CloseCalibrationFixture();",
            cleanupStart, StringComparison.Ordinal);
        int productionClose = host.IndexOf("ReignCourtPetitionScreenManager.TryCloseAutomationAudience();",
            cleanupStart, StringComparison.Ordinal);
        Assert.True(cleanupStart >= 0 && calibrationClose > cleanupStart
            && productionClose > calibrationClose);
        Assert.Contains("if (ReignCourtPetitionScreenManager.TryCloseAutomationAudience())",
            host, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeHallArtUsesHostSettlementAndNeverVisitorCulture()
    {
        string root = TestOptions.FindWorkspace();
        string vm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtLifeScreenVM.cs"));
        string constructor = vm[..vm.IndexOf("[DataSourceProperty]", StringComparison.Ordinal)];
        // Visitor, family, patronage and international audiences share this constructor.
        // Its environment must come from the saved host even when the lead is foreign or absent.
        Assert.Contains("Settlement.Find(matter.SettlementId ?? string.Empty)", constructor);
        Assert.Contains("audienceSettlement?.Culture?.StringId ?? \"generic\"", constructor);
        Assert.DoesNotContain("hero?.Culture", constructor);
        Assert.DoesNotContain("?? \"empire\"", constructor);
    }

    [Fact]
    public void DeploymentFrozenAuthorityMatchesCurrentInterfaceInventoryAndContract()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta");
        string deployment = File.ReadAllText(Path.Combine(root, "tools", "GauntletXmlPreviewer", "deploy-ui-transaction.ps1"));
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "GauntletXmlPreviewer", "calibration", "ui-catalog.json")));
        using JsonDocument atlas = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "GUI", "RuntimeSpriteSheets", "manifest.json")));
        int prefabs = catalog.RootElement.GetProperty("interfaces").EnumerateArray()
            .Select(item => item.GetProperty("prefab").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int atlasFiles = atlas.RootElement.GetProperty("categories").EnumerateObject()
            .Sum(category => category.Value.GetProperty("sheets").GetArrayLength()
                + category.Value.GetProperty("parts").EnumerateObject().Count());
        Match frozenCount = Regex.Match(deployment, @"\$expectedUiAuthorityFileCount = (\d+)");
        Assert.True(frozenCount.Success);
        // Three registry/manifest files and two client assemblies are also in this frozen inventory.
        Assert.Equal(prefabs + atlasFiles + 5, int.Parse(frozenCount.Groups[1].Value));

        Match frozenContract = Regex.Match(deployment,
            @"'GUI\\UiCalibration\\modern-style-contract\.json'\s*=\s*'([a-f0-9]{64})'");
        Assert.True(frozenContract.Success);
        string actualContractHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
            Path.Combine(root, "GUI", "UiCalibration", "modern-style-contract.json")))).ToLowerInvariant();
        Assert.Equal(actualContractHash, frozenContract.Groups[1].Value);
    }

    [Fact]
    public void CourtDocketUsesIndependentCardsFiniteFourItemScrollAndNoTermMetadata()
    {
        string root = TestOptions.FindWorkspace();
        string xml = File.ReadAllText(Path.Combine(root, "ReignBeta", "GUI", "Prefabs",
            "ReignCourtScreen.xml"));
        string vm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtScreenVM.cs"));
        string scroll = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "Widgets", "ReignDocketCardScrollPanel.cs"));
        string behavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Court", "ReignRulerDocketCampaignBehavior.cs"));
        string assetTool = File.ReadAllText(Path.Combine(root, "ReignBeta", "artwork",
            "ui-modern-style-kit", "tools", "prepare_ruler_docket_interface_assets.py"));
        string deployUi = File.ReadAllText(Path.Combine(root, "ReignBeta", "tools",
            "GauntletXmlPreviewer", "deploy-ui-transaction.ps1"));

        Assert.Contains("reign_court_chancellor_card_modern", xml, StringComparison.Ordinal);
        Assert.Contains("reign_court_petition_card_modern", xml, StringComparison.Ordinal);
        Assert.Contains("ReignDocketCardScrollPanel", xml, StringComparison.Ordinal);
        Assert.Contains("ItemCount=\"@DocketItemCount\"", xml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@DocketScrollVisible\"", xml, StringComparison.Ordinal);
        Assert.Contains("Handle=\"CompactCourtDocketScrollbarHandleHitbox\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("SuggestedWidth=\"28\" SuggestedHeight=\"80\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("SuggestedWidth=\"2\" HorizontalAlignment=\"Center\" Sprite=\"BlankWhiteSquare_9\" Color=\"#7E6A4DFF\"",
            xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sprite=\"reign_court_scroll_track\"", xml,
            StringComparison.Ordinal);
        int chancellorPortrait = xml.IndexOf("PortraitCacheKey=\"@ChancellorPortraitCacheKey\"",
            StringComparison.Ordinal);
        int chancellorCard = xml.IndexOf("Sprite=\"reign_court_chancellor_card_modern\"",
            StringComparison.Ordinal);
        Assert.True(chancellorPortrait >= 0 && chancellorCard > chancellorPortrait);
        Assert.Contains("chancellorPortraitFrameIntegrated", assetTool, StringComparison.Ordinal);
        Assert.Contains("chancellorPortraitApertureCenterAlpha", assetTool, StringComparison.Ordinal);
        Assert.Contains("if aperture_center_alpha != 0", assetTool, StringComparison.Ordinal);

        int listStart = xml.IndexOf("Id=\"CompactCourtDocketList\"", StringComparison.Ordinal);
        int listEnd = xml.IndexOf("</ReignDocketCardScrollPanel>", listStart, StringComparison.Ordinal);
        Assert.True(listStart >= 0 && listEnd > listStart);
        string petitionList = xml.Substring(listStart, listEnd - listStart);
        Assert.DoesNotContain("Text=\"@Status\"", petitionList, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"@Meta\"", petitionList, StringComparison.Ordinal);

        Assert.Contains("DocketItemCount => DailyAgenda.Count", vm, StringComparison.Ordinal);
        Assert.Contains("DocketScrollVisible => DocketItemCount > 4", vm, StringComparison.Ordinal);
        Assert.Contains("displayedPetitioners", vm, StringComparison.Ordinal);
        Assert.Contains("private const int VisibleCards = 4", scroll, StringComparison.Ordinal);
        Assert.Contains("if (!CanScroll)", scroll, StringComparison.Ordinal);
        Assert.Contains("ResetToTop();", scroll, StringComparison.Ordinal);
        Assert.Contains("Math.Min(ScrollableCardCount, index)", scroll, StringComparison.Ordinal);
        Assert.Contains("int restingIndex = CardIndexForOffset(", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeWheelSeen", scroll, StringComparison.Ordinal);
        Assert.Contains("if (Input.IsMouseScrollChanged) return;", scroll, StringComparison.Ordinal);
        Assert.Contains("_pointerDragActive", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("if (nextIndex == _currentCardIndex) return;", scroll,
            StringComparison.Ordinal);
        Assert.Contains("CollapseDuplicatePendingPetitioners", behavior, StringComparison.Ordinal);
        Assert.Contains("pendingPetitioners.Add(candidate.Petitioner.StringId)", behavior,
            StringComparison.Ordinal);
        Assert.Contains("ProcessRulerDocketMidnightRefusals(day);", behavior,
            StringComparison.Ordinal);
        Assert.Contains("state.LastMidnightDecisionDay = day;", behavior,
            StringComparison.Ordinal);
        Assert.Contains("x.ReceivedDay < day", behavior, StringComparison.Ordinal);
        Assert.Contains("midnight_automatic_refusal", behavior, StringComparison.Ordinal);
        Assert.Contains("QueuePetitionRelationChanges(petition, false);", behavior,
            StringComparison.Ordinal);
        Assert.DoesNotContain("if (DailyAgenda.Count >= 5) break;", vm,
            StringComparison.Ordinal);
        Assert.Contains("ReignCourtLifeRules.OrdinarySlots(campaignId, timelineId, day, replies)", behavior,
            StringComparison.Ordinal);
        Assert.Contains("DocketCourtLifeMatters", vm,
            StringComparison.Ordinal);
        Assert.Contains("Command.Click=\"ExecuteOpenCourtHistory\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"HISTORY\"", xml, StringComparison.Ordinal);
        Assert.Contains("SuggestedWidth=\"80\" SuggestedHeight=\"20\" MarginLeft=\"11\" MarginTop=\"117\" Command.Click=\"ExecuteOpenCourtHistory\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"HISTORY\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@ChancellorToggleVisible\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"80\" SuggestedHeight=\"20\" MarginLeft=\"106\" MarginTop=\"117\" Command.Click=\"ExecuteChancellorActive\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@ChancellorToggleVisible\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"80\" SuggestedHeight=\"20\" MarginLeft=\"201\" MarginTop=\"117\" Command.Click=\"ExecuteChancellorInactive\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("ChancellorToggleVisible", vm, StringComparison.Ordinal);
        Assert.Contains("return office != null && !office.IsVacant && !office.Emergency", vm,
            StringComparison.Ordinal);
        Assert.Contains("Sprite=\"reign_court_office_modern_shell\"", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Sprite=\"gold_frame_9\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sprite=\"reign_court_advance_button\"", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Sprite=\"reign_court_command_button\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("chancellorControlsUseStateAwareRuntimePlates", assetTool,
            StringComparison.Ordinal);
        Assert.Contains("chancellorCardControlPlateCount\": 0", assetTool,
            StringComparison.Ordinal);
        Assert.Contains("chancellorPaleTargetPixelCount", assetTool,
            StringComparison.Ordinal);
        Assert.Equal(4, xml.Split("Sprite=\"reign_court_navigation_button_modern\"",
            StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Sprite=\"reign_castle_layout_navigation_button\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("courtNavigationUsesExactApprovedModernShellButtonCopy", assetTool,
            StringComparison.Ordinal);
        Assert.Contains("courtNavigationBodyRgbPixelTransformationCount\": 0", assetTool,
            StringComparison.Ordinal);
        Assert.Contains("$expectedUiAuthorityFileCount = 318", deployUi,
            StringComparison.Ordinal);
        Assert.Contains("validated-native-portrait-generator", deployUi, StringComparison.Ordinal);
        Assert.Contains("$nativePortraitGeneratorFileCount", deployUi, StringComparison.Ordinal);
        Assert.DoesNotContain("Brush=\"ConversationItem.SoundBrush\" Command.Click=\"ExecuteOpenCourtHistory\"",
            xml, StringComparison.Ordinal);
        Assert.Contains("ReignDocketHistoryRecord history in _court.RulerDocketState.History", vm,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PetitionScreenUsesIntegratedAperturesAndConversationBeforeExplicitDecision()
    {
        string root = TestOptions.FindWorkspace();
        string xml = File.ReadAllText(Path.Combine(root, "ReignBeta", "GUI", "Prefabs",
            "ReignCourtPetitionScreen.xml"));
        string vm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtPetitionScreenVM.cs"));
        string sceneClient = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Integration", "ReignRulerPetitionSceneClient.cs"));
        string domain = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Court", "ReignRulerDocketDomain.cs"));
        string client = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Integration", "ReignCourtServerClient.cs"));
        string server = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules", "Court",
            "RulerDocketDialogue.cs"));
        string normalClient = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Platform",
            "Integration", "ReignServerClient.cs"));
        string normalServer = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules", "Platform",
            "Program.cs"));
        string textureFactory = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Portraits",
            "UI", "EventArt", "ReignEventArtTextureFactory.cs"));
        string assetTool = File.ReadAllText(Path.Combine(root, "ReignBeta", "artwork",
            "ui-modern-style-kit", "tools", "prepare_ruler_docket_interface_assets.py"));
        string designRules = File.ReadAllText(Path.Combine(root, "docs", "agent",
            "REIGN_INTERFACE_DESIGN_RULES.md"));
        string modernContract = File.ReadAllText(Path.Combine(root, "ReignBeta", "GUI",
            "UiCalibration", "modern-style-contract.json"));

        int scene = xml.IndexOf("Id=\"CourtPetitionSceneAperture\"", StringComparison.Ordinal);
        int shell = xml.IndexOf("Id=\"CourtPetitionApprovedShell\"", StringComparison.Ordinal);
        int activeList = xml.IndexOf("Id=\"CourtPetitionActiveAttendeeList\"",
            StringComparison.Ordinal);
        int attendeePortrait = xml.IndexOf("PortraitCacheKey=\"@PortraitCacheKey\"",
            activeList, StringComparison.Ordinal);
        int attendeeCard = xml.IndexOf("Sprite=\"reign_court_petition_attendee_card_modern\"",
            activeList, StringComparison.Ordinal);
        Assert.True(scene >= 0 && shell > scene && activeList > shell);
        Assert.True(attendeePortrait > activeList && attendeeCard > attendeePortrait);
        Assert.Contains("Sprite=\"reign_court_petition_modern_shell\"", xml, StringComparison.Ordinal);
        Assert.Contains("Sprite=\"reign_court_petition_attendee_card_modern\"", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CourtPetitionAttendeePanelCover", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("CourtPetitionAttendeePanelFrame", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sprite=\"gold_frame_9\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Id=\"CourtPetitionPortraitAperture\"", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"@PetitionerSpeech\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"ATTENDEES\"", xml, StringComparison.Ordinal);
        Assert.Contains("DataSource=\"{ActiveParticipants}\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("DataSource=\"{AvailableAttendees}\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Command.Click=\"ExecuteToggleActive\"", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reign_social_event_active_portrait_mask", xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reign_social_event_available_portrait_mask", xml,
            StringComparison.Ordinal);
        Assert.Contains("DataSource=\"{Transcript}\"", xml, StringComparison.Ordinal);
        Assert.Contains("<EditableTextWidget", xml, StringComparison.Ordinal);
        Assert.Contains("Text=\"@InputText\"", xml, StringComparison.Ordinal);
        Assert.Contains("Command.Click=\"ExecuteSend\"", xml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@DecisionControlsVisible\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("reign_wilderness_event_modern_shell", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("reign_social_event_preview_oval_mask", xml, StringComparison.Ordinal);

        Assert.Contains("MBBindingList<ReignChatLineVM> Transcript", vm, StringComparison.Ordinal);
        Assert.Contains("MBBindingList<ReignSocialEventAttendeeVM> ActiveParticipants", vm,
            StringComparison.Ordinal);
        Assert.Contains("MBBindingList<ReignSocialEventAttendeeVM> AvailableAttendees", vm,
            StringComparison.Ordinal);
        Assert.Contains("private const int MaxActiveParticipants = 4", vm,
            StringComparison.Ordinal);
        Assert.Contains("EnsurePetitionerIsActive", vm, StringComparison.Ordinal);
        Assert.Contains("ToggleAudienceAttendee", vm, StringComparison.Ordinal);
        Assert.Contains("BuildCourtAudience", vm, StringComparison.Ordinal);
        Assert.Contains("Settlement.Find(_court.Session?.HostSettlementStringId", vm,
            StringComparison.Ordinal);
        Assert.Contains("Hero.AllAliveHeroes", vm, StringComparison.Ordinal);
        Assert.Contains("RequestReactionAsync(\"conversation\"", vm, StringComparison.Ordinal);
        Assert.Contains("ConversationEnabled", vm, StringComparison.Ordinal);
        Assert.Contains("RequestDialogueResponseAsync(speaker, audienceTurn", vm,
            StringComparison.Ordinal);
        Assert.Contains("BuildPetitionConversationContext", vm, StringComparison.Ordinal);
        Assert.Contains("ActiveAudienceHeroes().ToList()", vm, StringComparison.Ordinal);
        Assert.Contains("[\"activeParticipants\"] = activeParticipants", vm,
            StringComparison.Ordinal);
        Assert.Contains("court attendee and witness", vm, StringComparison.Ordinal);
        Assert.Contains("established identity, personality, characteristics, knowledge, relationship toward the ruler, memories, and recent conversation history",
            vm, StringComparison.Ordinal);
        Assert.Contains("[\"conversationMode\"] = \"ruler_petition\"", vm,
            StringComparison.Ordinal);
        Assert.Contains("sceneContextDirective", normalClient, StringComparison.Ordinal);
        Assert.Contains("[\"actionResolutionIndex\"] = rulerPetition", normalClient,
            StringComparison.Ordinal);
        Assert.Contains("? new JObject()", normalClient, StringComparison.Ordinal);
        Assert.Contains("else if (!rulerPetitionAudience && !nobleDocketAudience && !courtLifeAudience)", normalServer,
            StringComparison.Ordinal);
        Assert.Contains("payload[\"playerText\"]", client, StringComparison.Ordinal);
        Assert.Contains("payload[\"transcript\"]", client, StringComparison.Ordinal);
        Assert.Contains("phase != \"conversation\"", server, StringComparison.Ordinal);
        Assert.Contains("player_text TEXT NOT NULL DEFAULT ''", server, StringComparison.Ordinal);
        Assert.Contains("[\"nativeActions\"] = new ArrayList()", server, StringComparison.Ordinal);
        string scenePresentation = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Integration", "ReignCourtAudienceScenePresentation.cs"));
        Assert.Contains("BuildCourtPetitionSceneImageId", scenePresentation, StringComparison.Ordinal);
        Assert.Contains("ReignRulerPetitionSceneClient.Create", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMemorySceneImageId(petition.SceneAssetPath)", vm,
            StringComparison.Ordinal);
        Assert.Contains("\"CourtPetitionArt\"", textureFactory,
            StringComparison.Ordinal);
        Assert.Contains("candidate.StartsWith(petitionArtRoot", textureFactory,
            StringComparison.Ordinal);
        Assert.Contains("BuildCourtPetitionReferenceImageId", textureFactory,
            StringComparison.Ordinal);
        Assert.Contains("ReignCourtAudienceScene.CompleteCast", sceneClient, StringComparison.Ordinal);
        Assert.Contains("ReignCourtAudienceReferenceCapture.ReadAsync", scenePresentation, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateSharedPortraitAsync", sceneClient, StringComparison.Ordinal);
        Assert.DoesNotContain("PortraitCache.SourcePath(portraitKey)", sceneClient,
            StringComparison.Ordinal);
        Assert.Contains("SuggestedWidth=\"1264\" SuggestedHeight=\"711\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\" EventImageId=\"@EventImageId\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@PostponeVisible\" IsEnabled=\"@CanPostpone\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Command.Click=\"ExecutePostpone\"", xml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"POSTPONE\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("TECHNICAL RETURN", xml, StringComparison.Ordinal);
        Assert.Contains("PostponeVisible => !DecisionComplete", vm, StringComparison.Ordinal);
        Assert.Contains("CanPostpone => PostponeVisible && !Busy", vm,
            StringComparison.Ordinal);
        Assert.Contains("ExecutePostpone()", vm, StringComparison.Ordinal);
        Assert.Contains("ActiveAudienceHeroIds", domain, StringComparison.Ordinal);
        Assert.Contains("ConversationLines", domain, StringComparison.Ordinal);
        Assert.Contains("RestoreConversation();", vm, StringComparison.Ordinal);
        Assert.Contains("prepare_petition_attendee_shell", assetTool, StringComparison.Ordinal);
        Assert.Contains("retired petitioner portrait aperture remains transparent", assetTool,
            StringComparison.Ordinal);
        Assert.Contains("must use one complete card raster asset",
            designRules, StringComparison.Ordinal);
        Assert.Contains("When the card scrolls, its artwork, frames, graphics, text, and controls move together.",
            designRules, StringComparison.Ordinal);
        Assert.Contains("completePortraitCardRule", modernContract, StringComparison.Ordinal);
        Assert.Contains("court-petition-attendee-card-portrait-aperture", modernContract,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NobleMatterAudiencePreservesTruthBoundaryAndExplicitRulerAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string vm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtNobleMatterScreenVM.cs"));
        string manager = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ReignCourtPetitionScreenManager.cs"));
        string nobleBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Court", "ReignNobleDocketCampaignBehavior.cs"));
        string screen = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtScreenVM.cs"));
        string reputation = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules",
            "Reputation", "CourtSocialReputation.cs"));
        string dialogue = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules",
            "Court", "RulerDocketDialogue.cs"));
        string server = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules",
            "Platform", "Program.cs"));

        Assert.Contains("TryActivateNobleMatter", manager, StringComparison.Ordinal);
        Assert.Contains("card.Payload is ReignNobleDocketMatter", screen, StringComparison.Ordinal);
        Assert.Contains("private const int MaxActiveParticipants = 4", vm, StringComparison.Ordinal);
        Assert.Contains("conversationMode\"] = \"noble_docket\"", vm, StringComparison.Ordinal);
        Assert.Contains("participant?.KnowsCanonicalTruth == true", vm, StringComparison.Ordinal);
        Assert.Contains("_matter.Evidence.Where(x => x.Revealed)", vm, StringComparison.Ordinal);
        Assert.Contains("TryRuleNobleMatter", vm, StringComparison.Ordinal);
        Assert.Contains("DEFER TO CHANCELLOR", vm, StringComparison.Ordinal);
        Assert.Contains("CONVICT SELECTED", vm, StringComparison.Ordinal);
        Assert.Contains("ACQUIT ALL", vm, StringComparison.Ordinal);
        Assert.Contains("courtTag(\"murderer\"", reputation, StringComparison.Ordinal);
        Assert.Contains("courtTag(\"convicted_murderer\"", reputation, StringComparison.Ordinal);
        Assert.Contains("courtTag(\"corrupt\"", reputation, StringComparison.Ordinal);
        Assert.Contains("courtTag(\"traitor\"", reputation, StringComparison.Ordinal);
        Assert.DoesNotContain("sixty percent", dialogue, StringComparison.Ordinal);
        Assert.DoesNotContain("twenty-five percent", dialogue, StringComparison.Ordinal);
        Assert.DoesNotContain("zero percent", dialogue, StringComparison.Ordinal);
        Assert.Contains("VisibleNobleAcceptanceTier(visibleReply)",
            dialogue, StringComparison.Ordinal);
        Assert.Contains("Never mention tiers, percentages, scores, relationship values, penalties, or game mechanics.",
            vm, StringComparison.Ordinal);
        Assert.DoesNotContain("You do not possess the hidden canonical truth", vm,
            StringComparison.Ordinal);
        Assert.Contains("Everything visible must sound like a noble speaking in Calradia.",
            vm, StringComparison.Ordinal);
        Assert.Contains("string visiblePremise = VisibleMatterPremise();", vm,
            StringComparison.Ordinal);
        Assert.Contains("News has only just arrived: \" + visiblePremise", vm,
            StringComparison.Ordinal);
        Assert.Contains("MurderVictimName()", vm, StringComparison.Ordinal);
        Assert.Contains("Hero.FindFirst(x =>", vm, StringComparison.Ordinal);
        Assert.Contains("_matter.VictimHeroId", vm, StringComparison.Ordinal);
        Assert.Contains("string chancellorHeroId = office != null && !office.IsVacant", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("!string.Equals(x.StringId, chancellorHeroId", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("Select the noble you find guilty before convicting.", vm,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Four living suspects stand before the throne", vm,
            StringComparison.Ordinal);
        Assert.DoesNotContain("News has only just arrived: \" + _matter.Premise", vm,
            StringComparison.Ordinal);
        Assert.Contains("+ CompleteSentence(outcome) + \" React to it.\"", vm,
            StringComparison.Ordinal);
        Assert.Contains("ContainsForbiddenNobleDocketStatistics(reply)", server,
            StringComparison.Ordinal);
        Assert.Contains("ReignRulerDocketRules.SanitizeVisibleNobleReply(", vm,
            StringComparison.Ordinal);
        Assert.Contains("nobleDocketMechanicalRepairApplied", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoyalProclamationRequiresExactPreviewAndRecordsImmutableNonMechanicalHistory()
    {
        string root = TestOptions.FindWorkspace();
        string xml = File.ReadAllText(Path.Combine(root, "ReignBeta", "GUI", "Prefabs",
            "ReignCourtScreen.xml"));
        string screen = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "ViewModels", "ReignCourtScreenVM.cs"));
        string behavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Court", "ReignRulerDocketCampaignBehavior.cs"));

        int proclamation = xml.IndexOf("Command.Click=\"ExecuteRoyalProclamation\"",
            StringComparison.Ordinal);
        int family = xml.IndexOf("Command.Click=\"ExecuteOpenFamilyChambers\"",
            StringComparison.Ordinal);
        Assert.True(proclamation >= 0 && family > proclamation);
        int proclamationPlate = xml.IndexOf("Sprite=\"reign_court_navigation_button_modern\"", proclamation,
            StringComparison.Ordinal);
        Assert.True(proclamationPlate > proclamation && proclamationPlate < family);
        Assert.Contains("SuggestedWidth=\"245\" SuggestedHeight=\"55\" MarginLeft=\"630\" MarginTop=\"642\" Command.Click=\"ExecuteRoyalProclamation\"",
            xml, StringComparison.Ordinal);
        Assert.Contains("Text=\"ROYAL PROCLAMATION\"", xml, StringComparison.Ordinal);
        Assert.Contains("PreviewRoyalProclamation", screen, StringComparison.Ordinal);
        Assert.Contains("The following exact text will become immutable, globally known history with no direct mechanics:",
            screen, StringComparison.Ordinal);
        Assert.Contains("_court.TryIssueRoyalProclamation(exactText", screen,
            StringComparison.Ordinal);
        Assert.Contains("Type = \"royal_proclamation\"", behavior, StringComparison.Ordinal);
        Assert.Contains("Summary = exactText", behavior, StringComparison.Ordinal);
        Assert.Contains("\"major_world\"", behavior, StringComparison.Ordinal);
        Assert.Contains("[\"exactText\"] = exactText", behavior, StringComparison.Ordinal);
        Assert.Contains("[\"mechanicalEffects\"] = false", behavior, StringComparison.Ordinal);
        Assert.Contains("[\"immutable\"] = true", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void TownGoldFixtureControlsTheCompleteProductionTrendWindow()
    {
        string root = TestOptions.FindWorkspace();
        string fixture = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "Court", "ReignRulerDocketInGameTests.cs"));

        Assert.Contains("state.SettlementSamples.RemoveAll(x => x.Day >= day - 3", fixture,
            StringComparison.Ordinal);
        Assert.Contains("Prosperity = capital.Town.Prosperity + 100f", fixture,
            StringComparison.Ordinal);
        Assert.Contains("Day = day, Prosperity = capital.Town.Prosperity", fixture,
            StringComparison.Ordinal);
    }
}
