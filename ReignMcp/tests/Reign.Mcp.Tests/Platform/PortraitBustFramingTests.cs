using ReignBeta.UI.Portraits;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class PortraitBustFramingTests
{
    [Fact]
    public void CaladogKeepsOriginalCrownPixelsAndShoulderContext()
    {
        var crop = ReignPortraitBustFraming.Calculate(1086, 1448, 143d / 240,
            .4342861771583557, .0562078133225441, .09001249074935913, .0974031314253807);
        // Reviewed original: hair begins near y=44. The former chest derivative
        // begins at y=46 and its second square crop removed more of the crown.
        Assert.Equal(0, crop.Y);
        Assert.InRange(crop.X, 365, 375);
        Assert.InRange(crop.Width, 300, 306);
        Assert.InRange(crop.Height, 505, 511);
        Assert.True(crop.Y + crop.Height > 450);
        Assert.InRange((double)crop.Width / crop.Height, .593, .599);
    }

    [Theory]
    [InlineData(.4, .22, .13, .15)]
    [InlineData(.1, .07, .11, .10)]
    [InlineData(.78, .16, .12, .14)]
    public void FaceAndHeadroomStayInsideOriginalWithoutStretching(double x, double y, double width, double height)
    {
        const int sourceWidth = 1200, sourceHeight = 1600;
        var crop = ReignPortraitBustFraming.Calculate(sourceWidth, sourceHeight, 143d / 240, x, y, width, height);
        Assert.InRange(crop.X, 0, sourceWidth - crop.Width);
        Assert.InRange(crop.Y, 0, sourceHeight - crop.Height);
        Assert.True(crop.X <= x * sourceWidth);
        Assert.True(crop.X + crop.Width >= (x + width) * sourceWidth);
        Assert.True(crop.Y <= y * sourceHeight);
        Assert.True(crop.Y + crop.Height > (y + height) * sourceHeight);
        Assert.InRange((double)crop.Width / crop.Height, .593, .599);
        if (y > height * .85) Assert.InRange(y * sourceHeight - crop.Y, height * sourceHeight * .85 - 1, height * sourceHeight * .85 + 1);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidFocusUsesFullHeightFromTheTop(double x)
    {
        var crop = ReignPortraitBustFraming.Calculate(1200, 1600, 143d / 240, x, .1, .1, .1);
        Assert.Equal(0, crop.Y);
        Assert.Equal(1600, crop.Height);
        Assert.InRange(crop.Width, 950, 956);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAspectCannotCreateAnUnboundedTexture(double aspect) =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() => ReignPortraitBustFraming.Calculate(1200, 1600, aspect, .4, .1, .1, .1));
}
