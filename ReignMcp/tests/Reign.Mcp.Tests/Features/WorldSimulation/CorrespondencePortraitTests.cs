using System.Xml.Linq;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class CorrespondencePortraitTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), path));

    [Fact]
    public void ContactRailHasNoNativeTableauxAndOnlyVisibleSmallestAiThumbnails()
    {
        var prefab = XDocument.Parse(Read("ReignBeta/GUI/Prefabs/ReignCorrespondenceScreen.xml"));
        Assert.DoesNotContain(prefab.Descendants(), element => element.Name.LocalName == "ImageIdentifierWidget");
        var portraits = prefab.Descendants("ReignPortraitWidget").ToArray();
        Assert.Equal(2, portraits.Length);
        Assert.All(portraits, portrait => Assert.Equal("true", (string?)portrait.Attribute("VisibleThumbnailOnly")));
        Assert.Equal(2, prefab.Descendants().Count(element => (string?)element.Attribute("Sprite") == "reign_modern_portrait_empty_fill"));
        string contact = Read("ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignCorrespondenceContactVM.cs");
        Assert.DoesNotContain("CharacterCode", contact);
        Assert.DoesNotContain("CharacterImageIdentifierVM", contact);
        Assert.Contains("hero?.HasMet == true", contact);
    }

    [Fact]
    public void CloseCancelsPortraitWorkAlongsideMailboxRequests()
    {
        string manager = Read("ReignBeta/src/Modules/Dialogue/UI/ReignCorrespondenceScreenManager.cs");
        Assert.Equal(2, manager.Split("ReignPortraitWidget.CancelThumbnailLoads(_layer?.UIContext)").Length - 1);
        string factory = Read("ReignBeta/src/Modules/Portraits/AIPortraits/TextureFactory.cs");
        string asynchronous = factory[(factory.IndexOf("public static async Task<TaleWorlds.TwoDimension.Texture> GetOrBuildThumbnailAsync", StringComparison.Ordinal))..];
        asynchronous = asynchronous[..asynchronous.IndexOf("private static byte[] ContainToAspect", StringComparison.Ordinal)];
        Assert.Contains("Task.Run", asynchronous);
        Assert.Contains("GetBytes(id, PortraitQualityTier.Thumbnail)", asynchronous);
        Assert.DoesNotContain("PortraitQualityTier.Zoom", asynchronous);
        Assert.Contains("ReignMainThread.InvokeAsync", asynchronous);
        Assert.Contains("cancellation.IsCancellationRequested", asynchronous);
    }

    [Fact]
    public void KnownHeroUpdatesCoverActualConversationsAndExcludeCalibrationEvents()
    {
        string knowledge = Read("ReignBeta/src/Modules/Characters/Campaign/ReignHeroKnowledge.cs");
        Assert.Contains("hero.IsKnownToPlayer = true", knowledge);
        Assert.Contains("hero.SetHasMet", knowledge);
        Assert.Contains("ReignMainThread.IsMainThread", knowledge);
        Assert.Contains("ReferenceEquals(campaign", knowledge);
        Assert.Contains("if (!_calibrationMode) ReignHeroKnowledge.MarkMet(hero)", Read("ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignIndividualChatScreenVM.cs"));
        Assert.Contains("if (!_calibrationMode) ReignBeta.Campaign.ReignHeroKnowledge.MarkMet(_session.Record.GetAttendees())", Read("ReignBeta/src/Modules/WorldSimulation/UI/ReignSocialEventScreenManager.cs"));
        string client = Read("ReignBeta/src/Modules/Platform/Integration/ReignServerClient.cs");
        Assert.Contains("ReignHeroKnowledge.MarkMet(activeHeroes)", client);
        Assert.Contains("ReignHeroKnowledge.MarkMet(speaker)", client);
        Assert.Contains("ReignHeroKnowledge.MarkMet(hero)", client);
    }

    [Fact]
    public void SavedEventRepairRequiresAttendanceAndDoesNotRevealExternalStoryCharacters()
    {
        string events = Read("ReignBeta/src/Modules/WorldSimulation/Campaign/ReignSocialEventsCampaignBehavior.cs");
        int start = events.IndexOf("private void RepairLoadedSessionState()", StringComparison.Ordinal);
        string repair = events[start..events.IndexOf("private void OnDailyTick()", start, StringComparison.Ordinal)];
        Assert.Contains("record.Status == SocialEventStatus.Attended || record.Status == SocialEventStatus.Resolved", repair);
        Assert.Contains("ReignHeroKnowledge.MarkMet(record.GetAttendees())", repair);
        Assert.DoesNotContain("GetGeneratedExternalHero", repair);
        Assert.DoesNotContain("SocialEventStatus.Pending", repair);
    }
}
