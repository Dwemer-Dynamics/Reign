using System.Text.Json;
using System.Xml.Linq;

namespace Reign.Mcp.Tests;

public sealed class TavernHouseUiContractTests
{
    [Fact]
    public void DeploymentIncludesValidatedCastDataOutsideTheFrozenUiAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string deployment = File.ReadAllText(Path.Combine(root,
            "ReignBeta/tools/GauntletXmlPreviewer/deploy-ui-transaction.ps1"));
        string mapping = "@('client', 'ModuleData\\reign_tavern_cast.json', 'ModuleData\\reign_tavern_cast.json', 'validated-client-content')";
        int inventoryStart = deployment.IndexOf("$validatedSupportMappings = @(", StringComparison.Ordinal);
        int inventoryEnd = deployment.IndexOf("foreach ($mapping in $validatedSupportMappings)", inventoryStart, StringComparison.Ordinal);
        Assert.True(inventoryStart >= 0 && inventoryEnd > inventoryStart);
        Assert.Contains(mapping, deployment.Substring(inventoryStart, inventoryEnd - inventoryStart), StringComparison.Ordinal);
        Assert.Equal(1, deployment.Split(new[] { mapping }, StringSplitOptions.None).Length - 1);
        Assert.Contains("$expectedUiAuthorityFileCount = 318", deployment, StringComparison.Ordinal);
        Assert.Contains("$validatedSupportMappings.Count +", deployment, StringComparison.Ordinal);
        var project = XDocument.Load(Path.Combine(root, "ReignBeta/ReignBeta.csproj"));
        var content = project.Descendants().Single(item => item.Name.LocalName == "Content"
            && (string?)item.Attribute("Include") == "ModuleData\\reign_tavern_cast.json");
        Assert.Equal("ModuleData\\reign_tavern_cast.json", content.Elements().Single(item => item.Name.LocalName == "TargetPath").Value);
        Assert.NotEqual("Never", content.Elements().Single(item => item.Name.LocalName == "CopyToOutputDirectory").Value);
    }

    [Fact]
    public void EveryTavernStateIsRegisteredForTheCompletePreviewMatrix()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignBeta/tools/GauntletXmlPreviewer/calibration/ui-catalog.json")));
        var entry = catalog.RootElement.GetProperty("interfaces").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "tavern-house");
        Assert.Equal("ReignTavernHouseScreen", entry.GetProperty("movie").GetString());
        Assert.Equal("ui-open --target tavern-house", entry.GetProperty("liveAction").GetString());
        var states = entry.GetProperty("previewStates").EnumerateArray().ToArray();
        foreach (string state in new[] { "negotiating", "quote", "payment-pending", "paid", "image-pending",
            "image-failed", "images-disabled", "portraits-missing", "max-workers-long-chat", "portrait-preview", "recruitment-offer" })
        {
            var variant = states.Single(item => item.GetProperty("id").GetString() == "tavern-house-" + state);
            Assert.True(variant.GetProperty("previewOnly").GetBoolean());
            Assert.False(variant.GetProperty("nativeInjection").GetBoolean());
        }
        Assert.Contains("tavern-house", catalog.RootElement.GetProperty("nativeCalibration")
            .GetProperty("providerFreeFixtureTargets").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void NativeCalibrationRoutesThroughAProviderFreeFixture()
    {
        string root = TestOptions.FindWorkspace();
        string host = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionUiCalibrationHost.cs"));
        string manager = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Characters/UI/ReignTavernHouseScreenManager.cs"));
        string vm = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Characters/UI/ReignTavernHouseScreenVM.cs"));
        Assert.Contains("ReignTavernHouseScreenManager.OpenForCalibration();", host, StringComparison.Ordinal);
        Assert.Contains("tavernHouseCalibrationFixture", host, StringComparison.Ordinal);
        Assert.Contains("IsCalibrationFixture", manager, StringComparison.Ordinal);
        int branch = vm.IndexOf("if (_fixture)", StringComparison.Ordinal);
        int production = vm.IndexOf("_house = ReignTavernHouseCampaignBehavior.Instance", branch, StringComparison.Ordinal);
        Assert.True(branch >= 0 && production > branch);
        string fixture = vm.Substring(branch, production - branch);
        Assert.DoesNotContain("EnsureTown", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("PostTavernHouseAsync", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratePortrait", fixture, StringComparison.Ordinal);
        Assert.Contains("if (_fixture) return;", vm, StringComparison.Ordinal);
        Assert.Contains("if (_fixture || ReignBetaSettings.Instance == null) return;", vm, StringComparison.Ordinal);
        Assert.Contains("if (_fixture || member?.Hero == null) return;", vm, StringComparison.Ordinal);
        Assert.Contains("if (!_fixture &&", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void TavernCompositionKeepsCompleteWorkerCardsAboveTheShell()
    {
        string root = TestOptions.FindWorkspace();
        var xml = XDocument.Load(Path.Combine(root, "ReignBeta/GUI/Prefabs/ReignTavernHouseScreen.xml"));
        XElement ById(string id) => xml.Descendants().Single(item => (string?)item.Attribute("Id") == id);
        var canvas = ById("TavernHouseCanvas");
        Assert.Equal("1672", (string?)canvas.Attribute("SuggestedWidth"));
        Assert.Equal("941", (string?)canvas.Attribute("SuggestedHeight"));
        Assert.Equal("true", (string?)canvas.Attribute("DoNotUseCustomScaleAndChildren"));
        Assert.Equal("Horizontal", (string?)ById("TavernWorkerViewport").Attribute("MouseScrollAxis"));
        Assert.Equal("true", (string?)ById("TavernWorkerClip").Attribute("ClipContents"));
        Assert.Contains(ById("TavernWorkerList"), ById("TavernWorkerCardPlate").Ancestors());
        var siblings = canvas.Element("Children")!.Elements().ToList();
        Assert.True(siblings.IndexOf(ById("TavernHouseShell")) < siblings.IndexOf(ById("TavernWorkerViewport")));
        Assert.Contains(xml.Descendants(), item => (string?)item.Attribute("Command.Click") == "ExecuteLookAgain"
            && (string?)item.Attribute("IsEnabled") == "@CanLookAgain");
        Assert.Contains(xml.Descendants(), item => (string?)item.Attribute("IsVisible") == "@PaymentConfirmationPending"
            && (string?)item.Attribute("Command.Click") == "ExecuteAcceptAgreement");
    }
}
