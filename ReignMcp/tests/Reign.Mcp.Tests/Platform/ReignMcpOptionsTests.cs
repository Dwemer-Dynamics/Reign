using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignMcpOptionsTests
{
    [Fact]
    public void DefaultProcessTimeoutCoversMeasuredOfflineVerificationRuntime()
    {
        var options = new ReignMcpOptions
        {
            WorkspaceRoot = TestOptions.FindWorkspace(),
            ServerBaseUri = new Uri("http://127.0.0.1:5101"),
            BuildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build", "test-options")
        };

        Assert.Equal(TimeSpan.FromMinutes(30), options.ProcessTimeout);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5101")]
    [InlineData("http://127.0.0.2:9000")]
    [InlineData("http://[::1]:5101")]
    public void AcceptsNumericLoopbackOrigins(string value)
    {
        var uri = ReignMcpOptions.ParseLoopbackUri(value);
        Assert.Equal("http", uri.Scheme);
    }

    [Theory]
    [InlineData("https://127.0.0.1:5101")]
    [InlineData("http://localhost:5101")]
    [InlineData("http://192.168.1.4:5101")]
    [InlineData("http://127.0.0.1:5101/api")]
    [InlineData("http://user:pass@127.0.0.1:5101")]
    public void RejectsNonLocalOrAmbiguousOrigins(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ReignMcpOptions.ParseLoopbackUri(value));
    }

    [Fact]
    public void RejectsBuildRootOutsideWorkspace()
    {
        var workspace = TestOptions.FindWorkspace();
        Assert.Throws<InvalidOperationException>(() =>
            ReignMcpOptions.EnsureWithin(workspace, Path.GetPathRoot(workspace)!, "test"));
    }
}
