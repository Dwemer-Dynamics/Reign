using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RoyalCouncilScenarioContractTests
{
    [Fact]
    public void ManifestCatalogMcpAndUiExposeRoyalCouncilRoute()
    {
        string root = TestOptions.FindWorkspace();
        string manifestPath = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios", "capital-royal-council-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("reign_start_royal_council_test", manifest.RootElement.GetProperty("entryPoint").GetString());
        Assert.Contains("routing", manifest.RootElement.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));

        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignMcp"), "TestingTools.Court.cs"));
        Assert.Contains("reign_get_royal_council_test_manifest", mcp, StringComparison.Ordinal);
        Assert.Contains("reign_start_royal_council_test", mcp, StringComparison.Ordinal);
        Assert.Contains("start Reign Royal Council test on disposable save", mcp, StringComparison.Ordinal);
        string catalog = File.ReadAllText(Path.Combine(root, "reign.testing.json"));
        Assert.Contains("capital-royal-council-manifest.json", catalog, StringComparison.Ordinal);
        string ui = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignLiveInteractionUiCalibrationHost.cs", "/src/"));
        Assert.Contains("ReignRoyalCouncilScreenManager.TryExecuteAutomationAction", ui, StringComparison.Ordinal);
        Assert.Contains("royalCouncilProviderCallCount", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void AdviceBoundaryAndLegacyOfficeAliasesAreExplicit()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "RoyalCouncil.cs", "/src/"));
        Assert.Contains("RoyalCouncilDomainKeys", server, StringComparison.Ordinal);
        Assert.Contains("providerCallCount", server, StringComparison.Ordinal);
        Assert.Contains("nativeActions", server, StringComparison.Ordinal);
        Assert.Contains("relationshipAssessments", server, StringComparison.Ordinal);
        Assert.Contains("reputationChanges", server, StringComparison.Ordinal);
        Assert.Contains("royal_council_turns", server, StringComparison.Ordinal);
        string court = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "CourtSystem.cs", "/src/"));
        Assert.Contains("treasurer", court, StringComparison.Ordinal);
        Assert.Contains("economicadvisor", court, StringComparison.Ordinal);
        Assert.Contains("chancellor", court, StringComparison.Ordinal);
        Assert.Contains("foreignadvisor", court, StringComparison.Ordinal);
        string vm = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignRoyalCouncilScreenVM.cs", "/src/"));
        Assert.Contains("No advisor was called; no provider request was made.", vm, StringComparison.Ordinal);
        Assert.Contains("Route(string text)", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void RoyalCouncilPrefabKeepsPortraitsLabelsAndTranscriptInsideTheirFrames()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignBeta", "GUI", "Prefabs", "ReignRoyalCouncilScreen.xml");
        string source = File.ReadAllText(path);
        System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Parse(source);

        System.Xml.Linq.XElement ById(string id) => document.Descendants()
            .Single(element => string.Equals((string?)element.Attribute("Id"), id, StringComparison.Ordinal));
        int Number(System.Xml.Linq.XElement element, string name) => int.Parse((string?)element.Attribute(name) ?? "0");

        System.Xml.Linq.XElement canvas = ById("RoyalCouncilCanvas");
        Assert.Equal("reign_royal_council_modern_shell", (string?)canvas.Attribute("Sprite"));
        Assert.DoesNotContain(document.Descendants(), element =>
            string.Equals((string?)element.Attribute("Id"), "RoyalCouncilTitle", StringComparison.Ordinal));
        System.Xml.Linq.XElement canvasChildren = canvas.Elements()
            .Single(element => element.Name.LocalName == "Children");

        int canvasWidth = Number(canvas, "SuggestedWidth");
        int CanvasLeft(System.Xml.Linq.XElement element) =>
            string.Equals((string?)element.Attribute("HorizontalAlignment"), "Right", StringComparison.Ordinal)
                ? canvasWidth - Number(element, "MarginRight") - Number(element, "SuggestedWidth")
                : Number(element, "MarginLeft");

        var portraitContracts = new[]
        {
            (Role: "War", Sprite: "reign_royal_council_war_portrait_mask", FrameX: 119, FrameY: 50, FrameWidth: 258, FrameHeight: 320, ApertureX: 12, ApertureY: 8, ApertureWidth: 233, ApertureHeight: 304),
            (Role: "Spymaster", Sprite: "reign_royal_council_spymaster_portrait_mask", FrameX: 120, FrameY: 465, FrameWidth: 257, FrameHeight: 307, ApertureX: 16, ApertureY: 8, ApertureWidth: 227, ApertureHeight: 292),
            (Role: "Economic", Sprite: "reign_royal_council_economic_portrait_mask", FrameX: 1297, FrameY: 50, FrameWidth: 256, FrameHeight: 318, ApertureX: 7, ApertureY: 12, ApertureWidth: 223, ApertureHeight: 302),
            (Role: "Foreign", Sprite: "reign_royal_council_foreign_portrait_mask", FrameX: 1297, FrameY: 465, FrameWidth: 255, FrameHeight: 305, ApertureX: 8, ApertureY: 3, ApertureWidth: 223, ApertureHeight: 300)
        };

        foreach (var contract in portraitContracts)
        {
            System.Xml.Linq.XElement clip = ById(contract.Role + "PortraitClip");
            System.Xml.Linq.XElement frame = ById(contract.Role + "PortraitFrameOverlay");
            System.Xml.Linq.XElement seat = clip.Ancestors()
                .First(element => element.Name.LocalName == "Widget" && element.Attribute("DataSource") is not null);
            int clipX = CanvasLeft(seat) + Number(clip, "MarginLeft");
            int clipY = Number(seat, "MarginTop") + Number(clip, "MarginTop");
            int frameX = CanvasLeft(frame);
            int frameY = Number(frame, "MarginTop");

            Assert.Equal("true", (string?)clip.Attribute("ClipContents"));
            Assert.Equal("true", (string?)clip.Attribute("DoNotAcceptEvents"));
            Assert.Equal("@HasPortrait", (string?)clip.Attribute("IsVisible"));
            Assert.Null(clip.Attribute("Sprite"));
            Assert.Null(clip.Attribute("Color"));
            Assert.Null(clip.Attribute("AlphaFactor"));
            Assert.Equal(contract.ApertureWidth, Number(clip, "SuggestedWidth"));
            Assert.Equal(contract.ApertureHeight, Number(clip, "SuggestedHeight"));
            Assert.Equal(contract.FrameWidth, Number(frame, "SuggestedWidth"));
            Assert.Equal(contract.FrameHeight, Number(frame, "SuggestedHeight"));
            Assert.Equal(contract.Sprite, (string?)frame.Attribute("Sprite"));
            Assert.Equal((string?)seat.Attribute("DataSource"), (string?)frame.Attribute("DataSource"));
            Assert.Equal("@HasPortrait", (string?)frame.Attribute("IsVisible"));
            Assert.Equal("true", (string?)frame.Attribute("DoNotAcceptEvents"));
            Assert.Equal(contract.FrameX, frameX);
            Assert.Equal(contract.FrameY, frameY);
            Assert.Equal(contract.ApertureX, clipX - frameX);
            Assert.Equal(contract.ApertureY, clipY - frameY);
            Assert.Equal(canvasChildren, seat.Parent);
            Assert.Equal(canvasChildren, frame.Parent);
            Assert.True(canvasChildren.Elements().ToList().IndexOf(frame) >
                        canvasChildren.Elements().ToList().IndexOf(seat));

            System.Xml.Linq.XElement children = clip.Elements()
                .Single(element => element.Name.LocalName == "Children");
            Assert.Equal(2, children.Elements().Count());
            System.Xml.Linq.XElement nativePortrait = children.Elements()
                .Single(element => element.Name.LocalName == "ImageIdentifierWidget");
            System.Xml.Linq.XElement generatedPortrait = children.Elements()
                .Single(element => element.Name.LocalName == "ReignPortraitWidget");
            foreach (System.Xml.Linq.XElement child in new[] { nativePortrait, generatedPortrait })
            {
                Assert.Equal("Fixed", (string?)child.Attribute("WidthSizePolicy"));
                Assert.Equal("Fixed", (string?)child.Attribute("HeightSizePolicy"));
                Assert.Equal("true", (string?)child.Attribute("DoNotAcceptEvents"));
                Assert.Equal("Center", (string?)child.Attribute("HorizontalAlignment"));
                Assert.True(Number(child, "SuggestedWidth") >= contract.ApertureWidth);
                Assert.True(Number(child, "SuggestedHeight") >= contract.ApertureHeight);
                Assert.Null(child.Attribute("Sprite"));
                Assert.Null(child.Attribute("Color"));
                Assert.Null(child.Attribute("AlphaFactor"));
            }
            // Native and generated portraits remain untouched centered squares.
            // The clip and the opaque seat-specific plate own the final aperture;
            // neither source is pre-cut, stretched, or positioned differently.
            Assert.Equal("Center", (string?)nativePortrait.Attribute("VerticalAlignment"));
            Assert.Equal(Number(nativePortrait, "SuggestedWidth"), Number(nativePortrait, "SuggestedHeight"));
            Assert.Equal("Center", (string?)generatedPortrait.Attribute("VerticalAlignment"));
            Assert.Null(generatedPortrait.Attribute("MarginTop"));
            Assert.Equal(Number(generatedPortrait, "SuggestedWidth"), Number(generatedPortrait, "SuggestedHeight"));
            double generatedAspect = double.Parse(
                (string?)generatedPortrait.Attribute("TargetAspect") ?? "0",
                System.Globalization.CultureInfo.InvariantCulture);
            double renderedAspect = (double)Number(generatedPortrait, "SuggestedWidth")
                / Number(generatedPortrait, "SuggestedHeight");
            Assert.InRange(Math.Abs(generatedAspect - renderedAspect), 0d, 0.001d);
            Assert.Equal("@PortraitId", (string?)nativePortrait.Attribute("ImageId"));
            Assert.Equal("@PortraitAdditionalArgs", (string?)nativePortrait.Attribute("AdditionalArgs"));
            Assert.Equal("@PortraitTextureProviderName", (string?)nativePortrait.Attribute("TextureProviderName"));
            Assert.Equal("ReignBeta.UI.Widgets.ReignPortraitWidget", (string?)generatedPortrait.Attribute("Type"));
            Assert.Equal("false", (string?)generatedPortrait.Attribute("UseFullBody"));
            Assert.Null(generatedPortrait.Attribute("UseEllipseMask"));
            Assert.DoesNotContain(clip.Descendants(),
                element => element.Name.LocalName == "ReignAspectMaskedTextureWidget");
        }

        Assert.DoesNotContain("ReignAspectMaskedTextureWidget", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseEllipseMask", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Reign.Portrait.EllipseMask", source, StringComparison.Ordinal);

        System.Xml.Linq.XElement transcript = ById("CouncilTranscriptPanel");
        System.Xml.Linq.XElement scrollbar = ById("CouncilTranscriptScrollbar");
        int transcriptRight = Number(transcript, "MarginLeft") + Number(transcript, "SuggestedWidth");
        Assert.True(transcriptRight < Number(scrollbar, "MarginLeft"));
        Assert.Equal("..\\CouncilTranscriptScrollbar", (string?)transcript.Attribute("VerticalScrollbar"));
        Assert.Equal("true", (string?)ById("CouncilTranscriptClip").Attribute("ClipContents"));
    }
}
