using Bannerlord.EditorMcp.Server;

namespace Bannerlord.EditorMcp.Tests;

public sealed class PathAccessPolicyTests
{
    private readonly PathAccessPolicy _policy = new(new BridgeServerOptions
    {
        GameDirectory = @"D:\Games\Bannerlord",
        ProjectDirectory = @"C:\Work\BannerlordEditorMcp",
        ModDirectory = @"D:\Games\Bannerlord\Modules\McpDevelopment"
    });

    [Fact]
    public void OfficialGameContentIsReadOnly()
    {
        var nativeFile = @"D:\Games\Bannerlord\Modules\Native\SubModule.xml";
        Assert.True(_policy.CanRead(nativeFile));
        Assert.False(_policy.CanWrite(nativeFile));
    }

    [Fact]
    public void DevelopmentModuleIsWritable()
    {
        Assert.True(_policy.CanWrite(@"D:\Games\Bannerlord\Modules\McpDevelopment\SubModule.xml"));
    }

    [Fact]
    public void TraversalCannotEscapeProjectRoot()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            _policy.ResolveProjectLogicalPath(@"..\outside.txt"));
    }

    [Fact]
    public void ArbitraryPathIsNotReadableOrWritable()
    {
        const string arbitrary = @"C:\Users\someone\secret.txt";
        Assert.False(_policy.CanRead(arbitrary));
        Assert.False(_policy.CanWrite(arbitrary));
    }
}
