using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.Campaign;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class RelationshipNativePullWireTests
{
    [Theory]
    [InlineData("exact")]
    [InlineData("unsuccessful")]
    [InlineData("idempotent")]
    [InlineData("old_lease")]
    [InlineData("wrong_command")]
    [InlineData("wrong_case")]
    [InlineData("wrong_checkpoint")]
    [InlineData("already_completed")]
    [InlineData("other_campaign")]
    [InlineData("other_instance")]
    [InlineData("other_run")]
    [InlineData("other_mode")]
    public void NativeRelationshipFixtureAcceptsOnlyItsFreshLeasedCommand(string change)
    {
        // The poll response contains the issued command. The ack echoes the accepted
        // command and its separate run summary, including the inherited enrollment.
        var issued = (JObject)JObject.Parse("""
            {"ok":true,"found":true,"command":{"campaignId":"campaign","gameInstanceId":"native",
            "runId":"run","commandId":"command","mode":"structured_actions",
            "operation":"structured_action_test","status":"leased","leaseId":"lease",
            "testCase":{"caseId":"one","operationId":"response-1","phase":"fixture","kind":"fixture",
                "actionId":"fixture.relationship_native_pull","arguments":{"control":"observe"}},
            "enrollment":{"source":"reign_mcp_structured_actions","checkpointSavePointId":"retained"}}}
            """)["command"]!;
        var accepted = (JObject)issued.DeepClone();
        accepted["status"] = "accepted";
        var run = JObject.Parse("""
            {"status":"running","mode":"structured_actions","campaignId":"campaign",
                "gameInstanceId":"native","runId":"run"}
            """);
        var ack = new JObject { ["ok"] = true, ["command"] = accepted, ["run"] = run };

        switch (change)
        {
            case "unsuccessful": ack["ok"] = false; break;
            case "idempotent": ack["idempotent"] = true; break;
            case "old_lease": accepted["leaseId"] = "old"; break;
            case "wrong_command": accepted["commandId"] = "other"; break;
            case "wrong_case": accepted["testCase"]!["operationId"] = "other"; break;
            case "wrong_checkpoint": accepted["enrollment"]!["checkpointSavePointId"] = "other"; break;
            case "already_completed": accepted["status"] = "completed"; break;
            case "other_campaign": run["campaignId"] = "other"; break;
            case "other_instance": run["gameInstanceId"] = "other"; break;
            case "other_run": run["runId"] = "other"; break;
            case "other_mode": run["mode"] = "passive_world"; break;
        }

        Assert.Equal(change == "exact", ReignRelationshipNativePullWire.MatchesAccepted(ack, issued, "campaign", "native"));
    }

    [Fact]
    public void LoadIdentityEvidenceHashesExactBytesAndKeepsAnAbsentIdentityAbsent()
    {
        const string value = "load-session-α";
        const string expected = "0EB4FC4CF65E020611E6260C2546A4CC9B05C8E7D7A307F3B745A384AFE9DA6D";
        Assert.Equal(expected, ReignRelationshipNativePullWire.LoadIdentitySha256(value));
        Assert.NotEqual(expected, ReignRelationshipNativePullWire.LoadIdentitySha256(value + " "));
        Assert.Null(ReignRelationshipNativePullWire.LoadIdentitySha256(string.Empty));
        Assert.Null(ReignRelationshipNativePullWire.LoadIdentitySha256(null));
    }
}
