using AIPortraits;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class PortraitAvailabilityCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedFramesProbeBothMissingAndPresentPortraitsOnce(bool present)
    {
        var fixture = new Fixture();
        int probes = 0;
        for (int frame = 0; frame < 3600; frame++)
            Assert.Equal(present, fixture.Cache.GetOrAdd("campaign|hero", () => { probes++; return present; }));
        Assert.Equal(1, probes);
        Assert.Empty(fixture.Pending);
    }

    [Fact]
    public void ExpiredEntryReturnsImmediatelyAndCoalescesBackgroundDiscovery()
    {
        var fixture = new Fixture();
        bool present = false;
        int probes = 0;
        Assert.False(fixture.Cache.GetOrAdd("campaign|hero", () => { probes++; return present; }));
        present = true;
        fixture.Now = 100;
        for (int frame = 0; frame < 600; frame++)
        {
            Assert.True(fixture.Cache.TryGet("campaign|hero", out bool cached));
            Assert.False(cached);
        }
        Assert.Equal(1, probes);
        Assert.Single(fixture.Pending);
        fixture.Pending.Dequeue()();
        Assert.True(fixture.Cache.TryGet("campaign|hero", out bool refreshed));
        Assert.True(refreshed);
        Assert.Equal(2, probes);
        Assert.Equal(1, fixture.Changes);
    }

    [Fact]
    public void GenerationInvalidatesMissWithoutWaitingForRefreshAndOldProbeCannotOverwriteIt()
    {
        var fixture = new Fixture();
        Assert.False(fixture.Cache.GetOrAdd("campaign|hero", () => false));
        fixture.Now = 100;
        fixture.Cache.TryGet("campaign|hero", out _);
        fixture.Cache.Invalidate("campaign|hero");
        Assert.True(fixture.Cache.GetOrAdd("campaign|hero", () => true));
        fixture.Pending.Dequeue()();
        fixture.Cache.TryGet("campaign|hero", out bool present);
        Assert.True(present);
    }

    [Fact]
    public void CampaignKeysStayIsolatedAndClearForcesFreshDiscovery()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Cache.GetOrAdd("first|main_hero", () => true));
        Assert.False(fixture.Cache.GetOrAdd("second|main_hero", () => false));
        fixture.Cache.Clear();
        Assert.False(fixture.Cache.GetOrAdd("first|main_hero", () => false));
    }

    [Fact]
    public void RefreshFailurePreservesPreviousValueAndCanRetry()
    {
        var fixture = new Fixture();
        bool fail = false;
        bool present = true;
        Assert.True(fixture.Cache.GetOrAdd("campaign|hero", () => fail ? throw new IOException("share temporarily unavailable") : present));
        fail = true;
        fixture.Now = 100;
        fixture.Cache.TryGet("campaign|hero", out _);
        fixture.Pending.Dequeue()();
        Assert.Single(fixture.Errors);
        Assert.True(fixture.Cache.TryGet("campaign|hero", out bool retained));
        Assert.True(retained);
        fail = false;
        present = false;
        fixture.Now = 200;
        fixture.Cache.TryGet("campaign|hero", out _);
        fixture.Pending.Dequeue()();
        fixture.Cache.TryGet("campaign|hero", out bool removed);
        Assert.False(removed);
        Assert.Equal(1, fixture.Changes);
    }

    [Fact]
    public void InitialFailureIsNotPermanentlyCached()
    {
        var fixture = new Fixture();
        Assert.Throws<IOException>(() => fixture.Cache.GetOrAdd("campaign|hero", () => throw new IOException()));
        Assert.True(fixture.Cache.GetOrAdd("campaign|hero", () => true));
    }

    private sealed class Fixture
    {
        public long Now;
        public int Changes;
        public Queue<Action> Pending { get; } = new();
        public List<Exception> Errors { get; } = new();
        public PortraitAvailabilityCache Cache { get; }

        public Fixture() => Cache = new PortraitAvailabilityCache(() => Now, 100, Pending.Enqueue, () => Changes++, Errors.Add);
    }
}
