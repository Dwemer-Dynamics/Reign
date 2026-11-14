namespace Reign.Mcp.Tests;

public sealed class SinglePassConversationTests
{
    [Theory]
    [InlineData("ReignServer/src/Modules/Platform/Program.cs", "Dictionary<string, object> llm = GenerateSinglePassConversation(dialogueLlmRequest", "llmMs = phaseTimer.ElapsedMilliseconds;")]
    [InlineData("ReignServer/src/Modules/Platform/Program.cs", "Dictionary<string, object> llm = GenerateSinglePassConversation(eventLlmRequest", "llmMs = phaseTimer.ElapsedMilliseconds;")]
    [InlineData("ReignServer/src/Modules/Relationships/Relationships.cs", "Dictionary<string, object> llm = GenerateSinglePassConversation(llmRequest", "Dictionary<string, object> parsed = PrepareCorrespondenceDelivery(llm);")]
    [InlineData("ReignServer/src/Modules/Dialogue/ConversationRouteMemory.cs", "var result = GenerateSinglePassConversation(request", "context.Result = result;")]
    public void RuntimeDeliveryDoesNotDispatchRepairOrSemanticReview(string path, string start, string end)
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), path));
        int begin = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(begin >= 0, path);
        int finish = source.IndexOf(end, begin, StringComparison.Ordinal);
        Assert.True(finish > begin, path);
        string route = source[begin..finish];
        foreach (string retired in new[] { "RetryMalformed", "RetryDialogueIntegrity", "RetryRoleplay", "RetryMotive", "RetryKinship", "EnforcePolitical", "FinishDialogueRepairs", "RepairMissingCommunication", "EnforceConversationAgency", "EnforceApproachOpening", "RetryCastleOpening" })
            Assert.DoesNotContain(retired, route);
    }

    [Fact]
    public void RuntimeClockPulseIsRejectedBeforeMainThreadDispatch()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/src/Modules/Dialogue/Campaign/ReignLiveInteractionConversationClockHost.cs"));
        int guard = source.IndexOf("if (command.Value<string>(\"profile\") != \"observe\")", StringComparison.Ordinal);
        int dispatch = source.IndexOf("result = await ReignMainThread.InvokeAsync", StringComparison.Ordinal);
        Assert.True(guard >= 0 && dispatch > guard);
    }
}
