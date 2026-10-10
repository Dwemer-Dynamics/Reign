using ReignBeta.UI;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class ReignBanterWindowTests
{
    [Fact]
    public void Default_window_keeps_the_original_map_clearance()
    {
        var window = new ReignBanterWindowState(1672, 941);
        Assert.Equal(1234, window.Left); Assert.Equal(599, window.Top);
        Assert.Equal(420, window.Width); Assert.Equal(220, window.Height);
        window.SetViewport(1920, 1080);
        Assert.Equal(1482, window.Left); Assert.Equal(738, window.Top);
    }

    [Fact]
    public void Drag_keeps_capture_outside_the_panel_until_release()
    {
        var window = new ReignBanterWindowState(1672, 941);
        Assert.False(window.CapturesPointer(10, 10));
        Assert.True(window.CapturesPointer(1250, 610));
        Assert.True(window.BeginManipulation(1250, 610, false));
        Assert.True(window.MovePointer(10, 10));
        Assert.Equal(8, window.Left); Assert.Equal(8, window.Top);
        Assert.True(window.CapturesPointer(3000, 2000));
        window.EndManipulation();
        Assert.False(window.CapturesPointer(3000, 2000));
        Assert.False(window.MovePointer(500, 500));
    }

    [Fact]
    public void Minimize_keeps_a_movable_bar_and_restores_the_expanded_size()
    {
        var window = new ReignBanterWindowState(1672, 941);
        window.Restore(new ReignBanterWindowPreferences { LeftRatio = .1f, TopRatio = .1f, Width = 640, Height = 380 });
        float left = window.Left, top = window.Top;
        window.ToggleCollapsed();
        Assert.True(window.Collapsed); Assert.Equal(42, window.Height);
        Assert.Equal(left, window.Left); Assert.Equal(top, window.Top); Assert.Equal(640, window.Width);
        Assert.False(window.CapturesPointer(left + 20, top + 100));
        Assert.False(window.BeginManipulation(left, top, true));
        Assert.True(window.BeginManipulation(left, top, false));
        window.MovePointer(left + 100, top + 50); window.EndManipulation();
        window.ToggleCollapsed();
        Assert.Equal(left + 100, window.Left); Assert.Equal(top + 50, window.Top);
        Assert.Equal(640, window.Width); Assert.Equal(380, window.Height);
    }

    [Fact]
    public void Expanding_a_bar_at_the_bottom_recovers_the_whole_transcript()
    {
        var window = new ReignBanterWindowState(1672, 941);
        window.ToggleCollapsed(); window.BeginManipulation(window.Left, window.Top, false);
        window.MovePointer(10000, 10000); window.EndManipulation();
        Assert.Equal(891, window.Top);
        window.ToggleCollapsed(); Assert.Equal(713, window.Top);
        Assert.Equal(933, window.Top + window.Height);
    }

    [Theory]
    [InlineData(960, 540)] [InlineData(1672, 941)] [InlineData(1920, 1080)] [InlineData(3440, 1440)]
    public void Extreme_resize_and_drag_stay_accessible(float viewportWidth, float viewportHeight)
    {
        var window = new ReignBanterWindowState(viewportWidth, viewportHeight);
        window.BeginManipulation(window.Left, window.Top, false); window.MovePointer(-10000, -10000); window.EndManipulation();
        window.BeginManipulation(0, 0, true); window.MovePointer(10000, 10000); window.EndManipulation();
        Assert.InRange(window.Width, 320, 1000); Assert.InRange(window.Height, 200, 700);
        Assert.True(window.Left + window.Width <= viewportWidth - 8);
        Assert.True(window.Top + window.Height <= viewportHeight - 8);
        window.BeginManipulation(0, 0, true); window.MovePointer(-10000, -10000); window.EndManipulation();
        Assert.Equal(320, window.Width); Assert.Equal(200, window.Height);
    }

    [Fact]
    public void Preferences_roundtrip_position_size_and_minimized_state()
    {
        var first = new ReignBanterWindowState(1672, 941);
        first.Restore(new ReignBanterWindowPreferences { LeftRatio = .2f, TopRatio = .3f, Width = 600, Height = 400, Collapsed = true });
        var reopened = new ReignBanterWindowState(1672, 941); reopened.Restore(first.Capture());
        Assert.Equal(first.Left, reopened.Left); Assert.Equal(first.Top, reopened.Top);
        Assert.Equal(first.Width, reopened.Width); Assert.Equal(first.ExpandedHeight, reopened.ExpandedHeight);
        Assert.True(reopened.Collapsed);
        reopened.SetViewport(960, 540); reopened.ToggleCollapsed();
        Assert.True(reopened.Left >= 8); Assert.True(reopened.Top >= 8);
        Assert.True(reopened.Left + reopened.Width <= 952); Assert.True(reopened.Top + reopened.Height <= 532);
    }

    [Fact]
    public void Corrupt_and_future_preferences_cannot_lose_the_window()
    {
        var window = new ReignBanterWindowState(1672, 941);
        window.Restore(new ReignBanterWindowPreferences { Schema = 99, Width = 99999 });
        Assert.Equal(420, window.Width);
        window.Restore(new ReignBanterWindowPreferences { LeftRatio = float.NaN, TopRatio = float.PositiveInfinity, Width = float.PositiveInfinity, Height = -1 });
        Assert.Equal(420, window.Width); Assert.Equal(220, window.Height);
        Assert.False(window.BeginManipulation(float.NaN, 0, false));
        Assert.False(window.CapturesPointer(float.NaN, 0));
        window.SetViewport(float.NaN, float.NegativeInfinity);
        Assert.Equal(1234, window.Left); Assert.Equal(599, window.Top);
    }
}
