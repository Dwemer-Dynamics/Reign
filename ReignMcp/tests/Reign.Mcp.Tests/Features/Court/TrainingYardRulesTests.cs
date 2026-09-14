using Reign.Core.Contracts.TrainingYard;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class TrainingYardRulesTests
{
    [Fact]
    public void InterfaceKeepsSelectedPortraitBehindItsShellOwnedAperture()
    {
        string root = TestOptions.FindWorkspace();
        string xml = File.ReadAllText(Path.Combine(root, "ReignBeta", "GUI", "Prefabs",
            "ReignTrainingYardScreen.xml"));
        string vm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "TrainingYard", "ReignTrainingYardScreenVM.cs"));
        string materializer = File.ReadAllText(Path.Combine(root, "ReignBeta", "artwork",
            "ui-modern-style-kit", "tools", "materialize_training_yard_shell.py"));

        int selectedPortrait = xml.IndexOf("PortraitCacheKey=\"@SelectedPortraitCacheKey\"", StringComparison.Ordinal);
        int shell = xml.IndexOf("Id=\"TrainingYardShell\"", StringComparison.Ordinal);
        Assert.True(selectedPortrait >= 0 && shell > selectedPortrait);
        string selectedPortraitLayer = xml.Substring(0, shell);
        Assert.DoesNotContain("reign_family_chambers_portrait_mask", selectedPortraitLayer,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@HasSelectedGeneratedPortrait\"", xml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"@HasGeneratedPortrait\"", xml, StringComparison.Ordinal);
        Assert.Contains("HasSelectedGeneratedPortrait", vm, StringComparison.Ordinal);
        Assert.Contains("HasGeneratedPortrait => TextureFactory.Has(PortraitCacheKey)", vm, StringComparison.Ordinal);
        Assert.Contains("add_selected_portrait_aperture", materializer, StringComparison.Ordinal);
        Assert.Contains("image.paste((0, 0, 0, 0), mask=mask)", materializer, StringComparison.Ordinal);
        Assert.DoesNotContain("HeightSizePolicy=\"StretchToParent\" Sprite=\"BlankWhiteSquare_9\" Color=\"#030303FF\"",
            xml, StringComparison.Ordinal);
        Assert.Contains("make_campaign_overlay(image)", materializer, StringComparison.Ordinal);
        Assert.Contains("clean.alpha_composite(image)", materializer, StringComparison.Ordinal);
        Assert.Contains("centered(draw, \"TRAINING YARD\", 836, 31", materializer, StringComparison.Ordinal);
        Assert.DoesNotContain("REIGN TRAINING YARD", materializer, StringComparison.Ordinal);
        Assert.DoesNotContain("REIGN TRAINING YARD", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void TrainingUsesBannerlordsNativeSettlementWaitClock()
    {
        string root = TestOptions.FindWorkspace();
        string manager = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "TrainingYard", "ReignTrainingYardScreenManager.cs"));

        Assert.Contains("TaleWorlds.CampaignSystem.GameMenus.GameMenu.SwitchToMenu(\"town_wait_menus\")",
            manager, StringComparison.Ordinal);
        Assert.Contains("waitMenu.StartWait();", manager, StringComparison.Ordinal);
        Assert.Contains("CampaignTimeControlMode.UnstoppableFastForward", manager, StringComparison.Ordinal);
        Assert.Contains("waitMenu.EndWait();", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("campaign.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;",
            manager, StringComparison.Ordinal);
    }

    [Fact]
    public void ReignWheelCallbacksUseOneOwnerAndTheCallbackScopedDelta()
    {
        string root = TestOptions.FindWorkspace();
        string[] paths =
        {
            Path.Combine(root, "ReignBeta", "src", "Modules", "Court", "TrainingYard",
                "ReignTrainingYardSnapScrollPanel.cs"),
            Path.Combine(root, "ReignBeta", "src", "Modules", "Court", "UI", "Widgets",
                "ReignAmbassadorSnapScrollPanel.cs"),
            Path.Combine(root, "ReignBeta", "src", "Modules", "Court", "UI", "Widgets",
                "ReignDocketCardScrollPanel.cs"),
            Path.Combine(root, "ReignBeta", "src", "Modules", "Government", "UI", "Widgets",
                "ReignGovernmentMemberScrollPanel.cs"),
            Path.Combine(root, "ReignBeta", "src", "Modules", "UI", "UI", "Widgets",
                "ReignAutoScrollPanel.cs"),
        };

        foreach (string path in paths)
        {
            string source = File.ReadAllText(path);
            int callback = source.IndexOf("OnMouseScroll()", StringComparison.Ordinal);
            int lateUpdate = source.IndexOf("OnLateUpdate", callback, StringComparison.Ordinal);
            string callbackBody = lateUpdate > callback
                ? source.Substring(callback, lateUpdate - callback)
                : source.Substring(callback);
            Assert.Contains("Context.EventManager.DeltaMouseScroll", callbackBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Input.DeltaMouseScroll", callbackBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Input.DeltaMouseScroll", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_nativeWheelSeen", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fallbackDelta", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ContainsPointer()", source, StringComparison.Ordinal);
        }

        string warCouncil = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Court",
            "UI", "Widgets", "ReignWarCouncilMapWidget.cs"));
        Assert.Contains("float delta = Context.EventManager.DeltaMouseScroll;", warCouncil,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Input.DeltaMouseScroll", warCouncil, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeWheelSeen", warCouncil, StringComparison.Ordinal);
        Assert.DoesNotContain("fallbackDelta", warCouncil, StringComparison.Ordinal);
        int lordCallback = warCouncil.IndexOf("protected override void OnMouseScroll()",
            warCouncil.IndexOf("ReignWarCouncilLordScrollPanel", StringComparison.Ordinal),
            StringComparison.Ordinal);
        int lordLateUpdate = warCouncil.IndexOf("protected override void OnLateUpdate", lordCallback,
            StringComparison.Ordinal);
        string lordCallbackBody = warCouncil.Substring(lordCallback, lordLateUpdate - lordCallback);
        Assert.Contains("ResetTweenSpeed();", lordCallbackBody, StringComparison.Ordinal);
        Assert.Contains("ApplyLordOffset", lordCallbackBody, StringComparison.Ordinal);
        Assert.DoesNotContain("base.OnMouseScroll();", lordCallbackBody, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyOrUnavailableGeneratedPortraitNeverEntersNativeTextureRendering()
    {
        string root = TestOptions.FindWorkspace();
        string widget = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Portraits",
            "UI", "Widgets", "ReignPortraitWidget.cs"));
        string factory = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Portraits",
            "AIPortraits", "TextureFactory.cs"));

        Assert.Contains("string.IsNullOrWhiteSpace(_portraitCacheKey) || !TextureFactory.Has(_portraitCacheKey)",
            widget, StringComparison.Ordinal);
        Assert.Contains("Leave the native ImageIdentifierWidget underneath as the fallback.",
            widget, StringComparison.Ordinal);
        Assert.Contains("could not assign an engine texture name", factory, StringComparison.Ordinal);
        Assert.Contains("texture.ReleaseAfterNumberOfFrames(2);", factory, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 50, 16)]
    [InlineData(100, 150, 41)]
    [InlineData(150, 200, 58)]
    [InlineData(200, 200, 66)]
    [InlineData(300, 300, 100)]
    public void RateUsesLeadershipPlusBestWeaponDividedBySix(int leadership, int weapon, int expected)
    {
        Assert.Equal(expected, ReignTrainingYardRules.CalculateHourlyXp(leadership, weapon));
    }

    [Fact]
    public void WeaponCollectionUsesOnlyItsHighestValue()
    {
        Assert.Equal(50, ReignTrainingYardRules.CalculateHourlyXp(120, new[] { 20, 60, 180, 40, 80, 10 }));
    }

    [Theory]
    [InlineData(16, 20, 1000, 320)]
    [InlineData(100, 20, 300, 300)]
    [InlineData(0, 20, 300, 0)]
    [InlineData(100, 0, 300, 0)]
    public void StackAwardIsCountScaledAndClamped(int rate, int count, int gainable, int expected)
    {
        Assert.Equal(expected, ReignTrainingYardRules.CalculateStackAward(rate, count, gainable));
    }

    [Theory]
    [InlineData(16, 296.875d)]
    [InlineData(41, 115.85365853658537d)]
    [InlineData(58, 81.89655172413794d)]
    [InlineData(66, 71.96969696969697d)]
    [InlineData(100, 47.5d)]
    public void PublishedTierOneToSixDurationsUseFourThousandSevenHundredFiftyXp(int rate, double expectedHours)
    {
        Assert.Equal(4750, ReignTrainingYardRules.TierOneToSixXp);
        Assert.Equal(expectedHours, ReignTrainingYardRules.EstimateHours(4750, rate), 10);
    }

    [Fact]
    public void ZeroRateHasNoFiniteCompletionTime()
    {
        Assert.True(double.IsPositiveInfinity(ReignTrainingYardRules.EstimateHours(4750, 0)));
    }

    [Theory]
    [InlineData(10, 5, 0, -1, 0)]
    [InlineData(10, 5, 0, 1, 1)]
    [InlineData(10, 5, 4, 1, 5)]
    [InlineData(10, 5, 5, 1, 5)]
    [InlineData(3, 5, 0, 1, 0)]
    public void ScrollIndexStopsAtCompleteCardEndpoints(int itemCount, int visibleItems, int current, int direction, int expected)
    {
        Assert.Equal(expected, ReignTrainingYardRules.ResolveScrollIndex(itemCount, visibleItems, current, direction));
    }

    [Theory]
    [InlineData(10, 5, 0, 500f, 0f)]
    [InlineData(10, 5, 1, 500f, 100f)]
    [InlineData(10, 5, 5, 500f, 500f)]
    [InlineData(3, 5, 2, 500f, 0f)]
    public void ScrollOffsetSnapsToExactCardBoundaries(int itemCount, int visibleItems, int index, float maximum, float expected)
    {
        Assert.Equal(expected, ReignTrainingYardRules.ResolveScrollOffset(itemCount, visibleItems, index, maximum));
    }
}
