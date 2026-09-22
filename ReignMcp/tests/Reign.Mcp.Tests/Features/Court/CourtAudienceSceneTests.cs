using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class CourtAudienceSceneTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "reign-court-art-test-" + Guid.NewGuid().ToString("N"));
    private static Task Current() => Task.CompletedTask;
    private static Task<byte[]> Reference(CourtScenePerson p) => Task.FromResult(p.Portrait);
    private static JObject Reply() => new() { ["ok"] = true, ["imageBase64"] = Convert.ToBase64String(Solid(30, 50, 70)) };

    private CourtSceneSnapshot Scene(int count = 7) => new()
    {
        CampaignId = "test-court-art", TimelineId = "main", AudienceId = "audience-1",
        HostId = "imperial-host", CultureId = "empire", PublicContext = "The guests request an audience.",
        Folder = _folder, Hall = Solid(4, 8, 12),
        People = Enumerable.Range(0, count).Select(i => new CourtScenePerson
        {
            Id = "courtier-" + i, Name = "Guest " + i, Age = 30 + i,
            ReferenceStamp = "identity-" + i, Portrait = Solid((byte)(40 + i * 20), 25, 35)
        }).ToList()
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(25)]
    public async Task OneRequestHasOneCompositeContainingEveryCourtierAndTheHostHall(int count)
    {
        CourtSceneSnapshot scene = Scene(count);
        int calls = 0;
        JObject? captured = null;
        string path = await ReignCourtAudienceScene.PrepareAsync(scene, Reference, request =>
        { calls++; captured = request; return Task.FromResult(Reply()); }, Current);
        Assert.Equal(1, calls);
        Assert.NotNull(captured);
        Assert.Single(captured.Properties(), p => p.Name.EndsWith("Base64", StringComparison.Ordinal));
        Assert.Equal(scene.People.Select(p => p.Id), captured["orderedParticipants"]!.Values<string>());
        Assert.Equal(scene.HostId, captured.Value<string>("settlementId"));
        byte[] pixels = PngReencode.DecodeToRgba(Convert.FromBase64String(captured.Value<string>("contactSheetBase64")!), out int width, out int height);
        int columns = (int)Math.Ceiling(Math.Sqrt(count)), rows = (count + columns - 1) / columns;
        for (int i = 0; i < count; i++)
        {
            int x = i % columns * (1280 / columns) + (1280 / columns - 16) / 2;
            int y = i / columns * (height / rows) + (height / rows - 16) / 2;
            Assert.Equal((byte)(40 + i * 20), pixels[(y * width + x) * 4]);
            Assert.Equal(25, pixels[(y * width + x) * 4 + 1]);
        }
        Assert.Equal(4, pixels[((height / 2) * width + 2184) * 4]);
        JObject receipt = JObject.Parse(File.ReadAllText(path + ".json"));
        Assert.Equal(1, receipt.Value<int>("inputImageCount"));
        Assert.Equal(count, ((JArray)receipt["referenceSha256"]!).Count);
    }

    [Fact]
    public async Task ReopeningSharesPendingWorkThenReusesThePersistedImage()
    {
        CourtSceneSnapshot scene = Scene();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<JObject> Provider(JObject _) { calls++; entered.TrySetResult(); return gate.Task; }
        Task<string> first = ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<string> reopened = ReignCourtAudienceScene.PrepareAsync(Scene(), Reference, Provider, Current);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, calls);
        gate.SetResult(Reply());
        Assert.Equal(await first, await reopened);
        string reloaded = await ReignCourtAudienceScene.PrepareAsync(Scene(), _ => throw new Exception("Cache reuse must not render portraits"), Provider, Current);
        Assert.Equal(await first, reloaded);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("culture")]
    [InlineData("cast")]
    [InlineData("portrait")]
    [InlineData("hall")]
    [InlineData("timeline")]
    public async Task ChangedSceneIdentityInvalidatesOnlyThatScene(string change)
    {
        var scene = Scene(); int calls = 0;
        Task<JObject> Provider(JObject _) { calls++; return Task.FromResult(Reply()); }
        string first = await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        switch (change)
        {
            case "host": scene.HostId = "battanian-host"; break;
            case "culture": scene.CultureId = "battania"; break;
            case "cast": scene.People.RemoveAt(6); break;
            case "portrait": scene.People[0].ReferenceStamp = "new-portrait"; break;
            case "hall": scene.Hall = Solid(2, 4, 6); break;
            case "timeline": scene.TimelineId = "fork"; break;
        }
        string second = await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MissingOrUnreadableReferenceSendsNoPartialSceneAndCanRetry()
    {
        var scene = Scene(); int calls = 0;
        Task<JObject> Provider(JObject _) { calls++; return Task.FromResult(Reply()); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReignCourtAudienceScene.PrepareAsync(scene,
            p => Task.FromResult(p.Id == "courtier-5" ? Array.Empty<byte>() : p.Portrait), Provider, Current));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReignCourtAudienceScene.PrepareAsync(scene,
            p => Task.FromResult(p.Id == "courtier-5" ? new byte[] { 1, 2, 3 } : p.Portrait), Provider, Current));
        Assert.Equal(0, calls);
        await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProviderFailureCanRetryWithoutPerPersonCalls()
    {
        var scene = Scene(); int calls = 0;
        Task<JObject> Provider(JObject _) => Task.FromResult(++calls == 1
            ? new JObject { ["ok"] = false, ["error"] = "test failure" } : Reply());
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current));
        Assert.Empty(ReignCourtAudienceScene.CachedPath(scene));
        await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CampaignChangeDuringProviderCallCannotPublishOrPersist()
    {
        var scene = Scene(); bool owns = true;
        Task Ensure() => owns ? Task.CompletedTask : Task.FromException(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => ReignCourtAudienceScene.PrepareAsync(scene, Reference,
            _ => { owns = false; return Task.FromResult(Reply()); }, Ensure));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task CorruptCachedImageIsNotReportedReady()
    {
        var scene = Scene(); int calls = 0;
        Task<JObject> Provider(JObject _) { calls++; return Task.FromResult(Reply()); }
        string path = await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Empty(ReignCourtAudienceScene.CachedPath(scene));
        await ReignCourtAudienceScene.PrepareAsync(scene, Reference, Provider, Current);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CastDeduplicatesStableIdentityWithoutDroppingDistinctCourtiers()
    {
        var scene = Scene(); scene.People.Add(scene.People[0]);
        Assert.Equal(7, ReignCourtAudienceScene.CompleteCast(scene.People).Count);
        scene.People.Add(new CourtScenePerson { Id = "courtier-0", ReferenceStamp = "conflicting" });
        Assert.Throws<InvalidOperationException>(() => ReignCourtAudienceScene.CompleteCast(scene.People));
        Assert.Throws<InvalidOperationException>(() => ReignCourtAudienceScene.CompleteCast(Array.Empty<CourtScenePerson>()));
        Assert.Throws<InvalidOperationException>(() => ReignCourtAudienceScene.CompleteCast(Scene(49).People));
    }

    [Theory]
    [InlineData("empire")]
    [InlineData("battania")]
    [InlineData("sturgia")]
    [InlineData("vlandia")]
    [InlineData("aserai")]
    [InlineData("khuzait")]
    public void EverySupportedHostCultureHasItsOwnApprovedReference(string culture)
    {
        Assert.EndsWith("-" + culture + ".png", ReignCourtAudienceScene.HallReference(culture));
        Assert.Equal(ReignCourtAudienceScene.HallReference(culture), ReignCourtAudienceScene.HallReference(" " + culture.ToUpperInvariant() + " "));
    }

    [Fact]
    public void UnknownCultureUsesApprovedGenericAndChildrenRetainAgeInstructions()
    {
        Assert.EndsWith("-reference.png", ReignCourtAudienceScene.HallReference("custom"));
        Assert.Equal(ReignCourtAudienceScene.HallReference("custom"), ReignCourtAudienceScene.HallReference(null!));
        var scene = Scene(2); scene.People[1].Age = 7;
        var request = ReignCourtAudienceScene.BuildRequest(scene, Solid(1, 2, 3), 2);
        Assert.Contains("age 7.0", request.Value<string>("prompt"));
        Assert.Contains("Preserve children's exact developmental ages", request.Value<string>("prompt"));
        Assert.Equal(2, ((JArray)request["orderedParticipants"]!).Count);
    }

    private static byte[] Solid(byte r, byte g, byte b)
    {
        byte[] pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255; }
        return PngEncoder.EncodeRgba(pixels, 16, 16);
    }

    [Fact]
    public void ClosingAViewRetainsTheImageForReopenWithoutUpdatingTheClosedScreen()
    {
        var state = new CourtSceneViewState(); int revision = state.Begin();
        int saved = 0, shown = 0;
        state.Close();
        Assert.True(state.Publish(revision, "image.png", true, _ => saved++, _ => shown++));
        Assert.Equal(1, saved); Assert.Equal(0, shown); Assert.Equal("ready", state.Status);
    }

    [Fact]
    public void StaleCastAndChangedCampaignCannotOverwriteOrShowAnImage()
    {
        var state = new CourtSceneViewState(); int old = state.Begin(), current = state.Begin();
        void Unexpected(string _) => throw new Exception("Stale image was published");
        Assert.False(state.Publish(old, "old.png", true, Unexpected, Unexpected));
        Assert.False(state.Publish(current, "new-campaign.png", false, Unexpected, Unexpected));
        state.Fail(old, "old failure");
        Assert.Equal("pending", state.Status); Assert.Empty(state.Error);
    }

    [Fact]
    public void DisabledPendingReadyAndFailedAreDistinctFromDialogueReadiness()
    {
        var state = new CourtSceneViewState(); int revision = state.Begin();
        Assert.Equal("pending", state.Status);
        state.Disable(revision); Assert.Equal("disabled", state.Status); Assert.Empty(state.Path);
        revision = state.Begin(); state.Fail(revision, "no portrait");
        Assert.Equal("failed", state.Status); Assert.Empty(state.Path);
        revision = state.Begin();
        Assert.True(state.Publish(revision, "ready.png", true, _ => { }, _ => { }));
        Assert.Equal("ready", state.Status); Assert.Empty(state.Error);
    }

    [Fact]
    public void FractionalAgingDoesNotBuyAnotherSceneButAVisibleAgeChangeInvalidatesIt()
    {
        var scene = Scene(); scene.People[0].Age = 35.001;
        string first = scene.Key;
        scene.People[0].Age = 35.05;
        Assert.Equal(first, scene.Key);
        scene.People[0].Age = 36;
        Assert.NotEqual(first, scene.Key);
        Assert.Equal(30, ReignCourtAudienceScene.ReferenceAge(double.NaN));
    }

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
