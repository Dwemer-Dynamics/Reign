using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PartyChatSessionRequestTests
{
    [Theory]
    [InlineData("main", 1)]
    [InlineData("main_recovered_branch", 2)]
    [InlineData("main_recovered_branch", 8)]
    public void SerializedSessionRetainsItsTimelineAndEntireAudience(string timeline, int npcCount)
    {
        string[] npcs = Enumerable.Range(0, npcCount).Select(i => "npc_" + i).ToArray();
        JObject wire = JObject.Parse(ReignPartyChatSessionRequest.Create("campaign", timeline,
            "party_chat_session", npcs[0], "player", npcs).ToString(Formatting.None));

        Assert.Equal("campaign", wire.Value<string>("campaignId"));
        Assert.Equal(timeline, wire.Value<string>("timelineId"));
        Assert.Equal("party_chat_session", wire.Value<string>("sessionId"));
        Assert.Equal(wire.Value<string>("sessionId"), wire.Value<string>("conversationSessionId"));
        Assert.Equal("party_chat", wire.Value<string>("channel"));
        Assert.Equal(npcs[0], wire.Value<string>("npcId"));
        Assert.Equal("player", wire.Value<string>("playerId"));
        Assert.Equal(npcs.Append("player"), wire["participants"]!.Values<string>());
    }

    [Fact]
    public void ReopeningAfterRollbackUsesTheNewTimelineAndNewAudience()
    {
        string[] participants = { "ira", "rhagaea", "ira", "", "player", " " };
        JObject before = ReignPartyChatSessionRequest.Create("campaign", "before", "session_before",
            "ira", "player", participants);
        JObject after = ReignPartyChatSessionRequest.Create("campaign", "after", "session_after",
            "rhagaea", "player", new[] { "rhagaea", "companion" });
        Assert.Equal(new[] { "ira", "rhagaea", "player" }, before["participants"]!.Values<string>());
        Assert.Equal("before", before.Value<string>("timelineId"));
        Assert.Equal("after", after.Value<string>("timelineId"));
        Assert.Equal(new[] { "rhagaea", "companion", "player" }, after["participants"]!.Values<string>());
        Assert.Equal("session_before", before.Value<string>("sessionId"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ASessionCannotSilentlyOmitItsTimeline(string? timeline)
        => Assert.Throws<ArgumentException>(() => ReignPartyChatSessionRequest.Create("campaign", timeline!,
            "session", "ira", "player", new[] { "ira", "rhagaea" }));
}
