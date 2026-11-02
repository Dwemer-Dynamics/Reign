using Newtonsoft.Json;
using ReignBeta.Runtime;
using ReignBeta.Save;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PartyChatContinuationTests
{
    private static ReignPartyChatContinuation Fixture() => new() {
        CampaignId = "campaign-a", PlayerId = "player", SourceSessionId = "old-session",
        LocationId = "town-a", RoomId = "", MapX = 10, MapY = 20, PlayerInputCount = 42, TranscriptSequence = 125,
        ParticipantIds = new() { "ira", "rhagaea" }, SelectedIds = new() { "rhagaea" },
        DisplayLines = new() { new() { Role = "player", Text = "*I raise my hand.* We agreed to meet at dawn.", ExchangeId = "old-session_turn_42" } },
        ContextLines = new() { new() { Sequence = 125, SessionId = "old-session", Role = "player", SpeakerHeroStringId = "player",
            Text = "*I raise my hand.* We agreed to meet at dawn.", ExchangeId = "old-session_turn_42", AudienceHeroStringId = "rhagaea" } }
    };
    private static string Reason(ReignPartyChatContinuation saved, string campaign = "campaign-a", string player = "player",
        string location = "town-a", string room = "", double x = 10, double y = 20, string[]? available = null)
        => saved.UnavailableReason(campaign, player, location, room, x, y, available ?? new[] { "ira", "rhagaea" });

    [Fact] public void NativeSaveRoundTripRetainsFullDisplayAndAttributedContext()
    {
        var saved = Fixture();
        saved.DisplayLines.AddRange(Enumerable.Range(0, 3000).Select(i => new ReignPartyChatSavedLine {
            Speaker = "Ira", Role = "npc", Text = $"Line {i}: " + new string('x', 400), ExchangeId = $"exchange-{i}"
        }));
        saved.DisplayLines.Add(new() { Role = "view", Text = "A letter changes hands.", InlineImagePath = "inline.png", FullImagePath = "full.png" });
        var encoded = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(saved));
        var restored = JsonConvert.DeserializeObject<ReignPartyChatContinuation>(ReignSavePayloadCodec.Decode(encoded))!;
        Assert.Equal("", Reason(restored));
        Assert.Equal(JsonConvert.SerializeObject(saved), JsonConvert.SerializeObject(restored));
        Assert.Equal(3002, restored.DisplayLines.Count);
        Assert.Equal(42, restored.PlayerInputCount);
        Assert.Equal(125, restored.TranscriptSequence);
        Assert.Equal("rhagaea", restored.ContextLines.Single().AudienceHeroStringId);
        Assert.Equal("full.png", restored.DisplayLines.Last().FullImagePath);
    }
    [Fact] public void ExtraBystandersAndRosterOrderDoNotBlockOriginalMembers()
        => Assert.Equal("", Reason(Fixture(), available: new[] { "stranger", "rhagaea", "ira" }));
    [Theory]
    [InlineData("ira")]
    [InlineData("rhagaea")]
    [InlineData("")]
    public void AnyMissingOriginalMemberBlocksContinuation(string onlyPresent)
        => Assert.NotEmpty(Reason(Fixture(), available: new[] { onlyPresent }));
    [Fact] public void MovingBetweenClickAndSendFailsAFreshPresenceCheck()
    {
        var saved = Fixture();
        Assert.Equal("", Reason(saved));
        Assert.NotEmpty(Reason(saved, available: new[] { "ira" }));
    }
    [Theory]
    [InlineData("other-campaign", "player", "town-a", "")]
    [InlineData("campaign-a", "other-player", "town-a", "")]
    [InlineData("campaign-a", "player", "town-b", "")]
    [InlineData("campaign-a", "player", "town-a", "lordshall")]
    public void CampaignPlayerSettlementAndRoomMustMatch(string campaign, string player, string location, string room)
        => Assert.NotEmpty(Reason(Fixture(), campaign, player, location, room));
    [Fact] public void OverlandLocationIsTheActualPositionRatherThanOnlyWithYourParty()
    {
        var saved = Fixture(); saved.LocationId = "overland";
        Assert.Equal("", Reason(saved, location: "overland"));
        Assert.NotEmpty(Reason(saved, location: "overland", x: 11));
        Assert.NotEmpty(Reason(saved, location: "overland", y: double.NaN));
    }
    [Fact] public void LoadingOlderSaveRestoresItsOwnHistoryWithoutFutureLines()
    {
        var current = Fixture();
        var oldNativeSave = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(current));
        current.DisplayLines.Add(new() { Role = "npc", Text = "This happened only in the future." });
        current.PlayerInputCount++;
        var loaded = JsonConvert.DeserializeObject<ReignPartyChatContinuation>(ReignSavePayloadCodec.Decode(oldNativeSave))!;
        Assert.Single(loaded.DisplayLines);
        Assert.Equal(42, loaded.PlayerInputCount);
        Assert.DoesNotContain(loaded.DisplayLines, line => line.Text.Contains("future"));
    }
    [Fact] public void ClearingSelectionDoesNotEraseTheConversation()
    {
        var saved = Fixture(); saved.SelectedIds.Clear();
        Assert.Equal("", Reason(saved)); Assert.Equal(2, saved.ParticipantIds.Count);
        Assert.Single(saved.DisplayLines);
    }
    [Fact] public void MissingOrUnsupportedSnapshotFailsClosed()
    {
        Assert.NotEmpty(Reason(new()));
        var saved = Fixture(); saved.Version = 2; Assert.NotEmpty(Reason(saved));
        saved = Fixture(); saved.SelectedIds.Add("not-a-participant"); Assert.NotEmpty(Reason(saved));
    }
}
