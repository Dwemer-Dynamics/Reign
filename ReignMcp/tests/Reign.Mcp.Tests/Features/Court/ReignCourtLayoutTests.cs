using ReignBeta.UI;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class ReignCourtLayoutTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(2560, 1600)]
    [InlineData(2559, 1599)]
    [InlineData(3440, 1440)]
    [InlineData(3840, 1600)]
    [InlineData(3840, 2160)]
    [InlineData(1920, 1440)]
    public void NativeAdapterPreservesPhysicalFitAfterGauntletAspectCorrection(float width, float height)
    {
        // Independent native formula, verified in the installed UIContext.UpdateScale.
        float nativeBase = height / 1080f;
        float aspect = width / height;
        if (aspect < (1920f / 1080f) * .98f)
            nativeBase *= aspect / ((1920f / 1080f) * .98f);
        var adapter = ReignCourtLayout.ComputeNativeLayout(width, height, nativeBase);
        float actualScale = nativeBase * adapter.ScaleModifier;
        var fit = ReignCourtLayout.Compute(width, height);
        Assert.InRange(Math.Abs(actualScale - fit.Scale), 0f, .00001f);
        float actualBottom = (adapter.TopInset + ReignCourtLayout.Height) * actualScale;
        float targetBottom = (fit.TopInset + ReignCourtLayout.Height) * fit.Scale;
        Assert.InRange(Math.Abs(actualBottom - targetBottom), 0f, .001f);
    }

    [Fact]
    public void NativeSixteenByTenAdapterReachesRetainedPhysicalReference()
    {
        var adapter = ReignCourtLayout.ComputeNativeLayout(2560f, 1600f, 1.3605442f);
        Assert.InRange(adapter.ScaleModifier, 1.140505f, 1.140525f);
        float actualScale = 1.3605442f * adapter.ScaleModifier;
        Assert.InRange(actualScale, 1.551711f, 1.551732f);
        Assert.InRange((adapter.TopInset + 854f) * actualScale, 1529.24f, 1529.27f);
        // The former height-based conversion misses the bottom by over120px.
        float oldScale = 1.3605442f * ReignCourtLayout.Compute(2560f, 1600f).Scale / (1600f / 1080f);
        Assert.True((adapter.TopInset + 854f) * oldScale < 1410f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidNativeBaseHasFiniteSafeModifier(float nativeBase)
    {
        Assert.Equal(1f, ReignCourtLayout.ComputeNativeLayout(2560f, 1600f, nativeBase).ScaleModifier);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(2560, 1600)]
    [InlineData(2559, 1598)]
    [InlineData(3440, 1440)]
    [InlineData(3840, 1600)]
    [InlineData(3840, 2160)]
    public void UniformCourtFillsReservedHeightWithoutCoveringHudOrLeavingViewport(float width, float height)
    {
        var fit = ReignCourtLayout.Compute(width, height);
        float hudScale = Math.Min(height / 1080f, width / (1920f * .98f));
        float top = fit.TopInset * fit.Scale;
        float bottom = top + ReignCourtLayout.Height * fit.Scale;
        float availableBottom = height - ReignCourtLayout.BottomHudInset * hudScale;
        Assert.InRange(Math.Abs(top - 150f * hudScale), 0f, .001f);
        Assert.InRange(Math.Abs(bottom - availableBottom), 0f, .001f);
        Assert.True(ReignCourtLayout.Width * fit.Scale <= width - 48f * hudScale + .001f);
        Assert.True(fit.Scale > hudScale, "The court grows relative to the prior base-scale lock.");
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(float.NaN, 1080)]
    [InlineData(1920, float.PositiveInfinity)]
    public void InvalidViewportRetainsSafeAuthoredGeometry(float width, float height)
    {
        Assert.Equal((1f, 150f), ReignCourtLayout.Compute(width, height));
    }
}
