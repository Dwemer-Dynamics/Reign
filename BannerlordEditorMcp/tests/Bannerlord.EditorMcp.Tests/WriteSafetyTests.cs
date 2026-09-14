using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorMcp.Tests;

public sealed class WriteSafetyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("1bad")]
    [InlineData("bad/module")]
    [InlineData("bad module")]
    [InlineData("..")]
    public void InvalidModuleIdsAreRejected(string moduleId)
    {
        Assert.False(WriteSafetyValidator.IsValidModuleId(moduleId));
    }

    [Fact]
    public void DisarmedWritesAreRejected()
    {
        var rejection = WriteSafetyValidator.Validate(CreateRequest(), CreateContext(safeWritesArmed: false));
        Assert.NotNull(rejection);
        Assert.Equal("writes_disarmed", rejection.Code);
    }

    [Fact]
    public void StaleRevisionsAreRejected()
    {
        var request = CreateRequest();
        request.ExpectedSceneRevision = "session:4";

        var rejection = WriteSafetyValidator.Validate(request, CreateContext(safeWritesArmed: true));
        Assert.NotNull(rejection);
        Assert.Equal("stale_scene_revision", rejection.Code);
    }

    [Fact]
    public void ConfiguredDisposableSceneAndCurrentRevisionAreAccepted()
    {
        var rejection = WriteSafetyValidator.Validate(CreateRequest(), CreateContext(safeWritesArmed: true));
        Assert.Null(rejection);
    }

    private static BridgeRequest CreateRequest()
    {
        return new BridgeRequest
        {
            TargetModule = "McpDevelopment",
            Scene = "mcp_disposable_scene",
            ExpectedSceneRevision = "session:5",
            IdempotencyKey = "test-create-v1"
        };
    }

    private static WriteSafetyContext CreateContext(bool safeWritesArmed)
    {
        return new WriteSafetyContext
        {
            BridgeEnabled = true,
            ReadOnly = false,
            SafeWritesArmed = safeWritesArmed,
            ConfiguredTargetModule = "McpDevelopment",
            ConfiguredDisposableScene = "mcp_disposable_scene",
            CurrentScene = "mcp_disposable_scene",
            CurrentRevision = "session:5"
        };
    }
}
