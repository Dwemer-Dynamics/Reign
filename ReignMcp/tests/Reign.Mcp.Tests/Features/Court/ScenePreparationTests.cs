using System.Xml.Linq;
using ReignBeta.UI;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class ScenePreparationTests
{
    [Fact]
    public void CastleChatUsesOnlyApprovedCastleFallbackWhenGeneratedArtIsUnavailable()
    {
        string root = TestOptions.FindWorkspace();
        string vm = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignPartyChatScreenVM.cs"));
        string prefab = File.ReadAllText(Path.Combine(root,
            "ReignBeta/GUI/Prefabs/ReignCastleChatScreen.xml"));
        Assert.Contains("ShowCastleApprovedFallback => !HasCastleArt", vm);
        Assert.DoesNotContain("CastleTavernArt", vm);
        Assert.Contains("Sprite=\"reign_castle_chat_approved_fallback\"", prefab);
        Assert.DoesNotContain("ReignTavernArtWidget", prefab);
        Assert.DoesNotContain("CastleTavernArt", prefab);
    }

    [Fact]
    public async Task DialogueAndArtStartBeforeEitherCompletes()
    {
        var dialogue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var art = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool dialogueStarted = false, artStarted = false;
        Task run = ReignScenePreparationCoordinator.RunAsync(new object(),
            () => { dialogueStarted = true; return dialogue.Task; },
            () => { artStarted = true; return art.Task; });
        Assert.True(dialogueStarted);
        Assert.True(artStarted);
        dialogue.SetResult(true);
        Assert.False(run.IsCompleted);
        art.SetResult(true);
        await run;
    }

    [Fact]
    public async Task ReopeningSharesPendingArtAndDoesNotReopenWhenArtFinishes()
    {
        object scene = new();
        var art = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int opens = 0, requests = 0;
        Task Open() { opens++; return Task.CompletedTask; }
        Task Paint() { requests++; return art.Task; }
        Task first = ReignScenePreparationCoordinator.RunAsync(scene, Open, Paint);
        Task second = ReignScenePreparationCoordinator.RunAsync(scene, Open, Paint);
        Assert.Equal(2, opens);
        Assert.Equal(1, requests);
        art.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task FailedArtDoesNotSuppressDialogueAndCanBeRetried()
    {
        object scene = new();
        int opens = 0;
        Task Open() { opens++; return Task.CompletedTask; }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReignScenePreparationCoordinator.RunAsync(scene, Open,
                () => throw new InvalidOperationException("provider unavailable")));
        bool retried = false;
        await ReignScenePreparationCoordinator.RunAsync(scene, Open,
            () => { retried = true; return Task.CompletedTask; });
        Assert.Equal(2, opens);
        Assert.True(retried);
    }

    [Fact]
    public void EveryCustomFontPageResolvesToAnActualRegisteredSprite()
    {
        string root = TestOptions.FindWorkspace();
        string gui = Path.Combine(root, "ReignBeta", "GUI");
        XDocument sprites = XDocument.Load(Path.Combine(gui, "ReignBetaSpriteData.xml"));
        var names = sprites.Descendants("SpritePart").Select(x => x.Element("Name")?.Value).ToHashSet();
        foreach (string descriptor in Directory.GetFiles(Path.Combine(gui, "Fonts"), "*.fnt", SearchOption.AllDirectories))
        {
            XDocument font = XDocument.Load(descriptor);
            foreach (XElement page in font.Descendants("page"))
            {
                string sprite = Path.GetFileNameWithoutExtension(page.Attribute("file")!.Value);
                Assert.True(names.Contains(sprite), $"{descriptor}: page {sprite} has no SpritePart; native RichText would dereference a null FontSprite.");
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(gui, "SpriteParts"), sprite + ".png", SearchOption.AllDirectories));
            }
        }
    }

    [Fact]
    public void ArtFlowsKeepDialogueIndependentAndRefreshOnlyTheOpenView()
    {
        string root = TestOptions.FindWorkspace();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        foreach (string path in new[] {
            "ReignBeta/src/Modules/Court/UI/ReignCastleChatPreparation.cs",
            "ReignBeta/src/Modules/Court/FamilyChambers/UI/ReignFamilyChambersPreparation.cs" })
            Assert.Contains("ReignScenePreparationCoordinator.RunAsync", Read(path));
        string petition = Read("ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtPetitionScreenVM.cs");
        Assert.Contains("_ = RequestReactionAsync(\"opening\"", petition);
        Assert.Contains("_scene?.Refresh()", petition);
        string manager = Read("ReignBeta/src/Modules/Dialogue/UI/ReignPartyChatScreenManager.cs");
        Assert.Contains("_dataSource?.RefreshCastleArt()", manager);
        string vm = Read("ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignPartyChatScreenVM.cs");
        Assert.Contains("_isFinalized || !_castleMode", vm);
        string scene = Read("ReignBeta/src/Modules/Court/Integration/ReignCastleSceneClient.cs");
        Assert.Equal(2, scene.Split("await PublishSceneAsync(session, path)").Length - 1);
        string publication = scene[scene.IndexOf("private static Task PublishSceneAsync", StringComparison.Ordinal)..];
        Assert.Contains("ReignMainThread.InvokeAsync", publication);
        Assert.True(publication.IndexOf("ReignEventArtTextureFactory.Clear()", StringComparison.Ordinal)
            < publication.IndexOf("session.ImageStatus = \"ready\"", StringComparison.Ordinal));
    }
}
