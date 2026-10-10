using ReignBeta.UI;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class KeepAccessTests
{
    [Theory]
    [InlineData(true, false, "town_keep")]
    [InlineData(false, true, "castle")]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    public void NativeKeepDestinationAcceptsTownsAndCastlesOnly(bool isTown, bool isCastle, string? expected)
    {
        Assert.Equal(expected, ReignKeepAccessPolicy.GetNativeMenuId(isTown, isCastle));
    }
}
