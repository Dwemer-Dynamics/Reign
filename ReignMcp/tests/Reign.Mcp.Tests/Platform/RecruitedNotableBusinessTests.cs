using ReignBeta.Campaign;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class RecruitedNotableBusinessTests
{
    [Fact]
    public void EveryBusinessTransfersEvenWhenNativeCallsRemoveLiveCollectionEntries()
    {
        var owned = new List<string> { "forge", "brewery", "caravan" };
        var successor = new List<string>();
        int count = ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, asset =>
        {
            owned.Remove(asset);
            successor.Add(asset);
        });
        Assert.Equal(3, count);
        Assert.Empty(owned);
        Assert.Equal(new[] { "forge", "brewery", "caravan" }, successor);
    }

    [Fact]
    public void FailureDoesNotLoseRemainingAssetsAndRetryCannotTransferCompletedBusinessesTwice()
    {
        var owned = new List<string> { "forge", "caravan" };
        var successor = new List<string>();
        Assert.Throws<InvalidOperationException>(() => ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, asset =>
        {
            if (asset == "caravan") throw new InvalidOperationException("Native transfer unavailable");
            owned.Remove(asset);
            successor.Add(asset);
        }));
        Assert.Equal(new[] { "caravan" }, owned);
        Assert.Equal(new[] { "forge" }, successor);
        Assert.Equal(1, ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, asset =>
        {
            owned.Remove(asset);
            successor.Add(asset);
        }));
        Assert.Equal(new[] { "forge", "caravan" }, successor);
        Assert.Equal(0, ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, _ => Assert.Fail("Duplicate transfer")));
    }

    [Fact]
    public void NativeNoOpCannotBeReportedAsCompleted()
    {
        var owned = new List<string> { "forge", "caravan" };
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, _ => calls++));
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "forge", "caravan" }, owned);
    }

    [Fact]
    public void StaleOwnershipSnapshotDoesNotTakeAnotherOwnersBusiness()
    {
        string[] snapshot = { "forge", "caravan" };
        var owned = new HashSet<string> { "caravan" };
        var transferred = new List<string>();
        Assert.Equal(1, ReignBusinessOwnershipRelease.ReleaseAll(snapshot, owned.Contains, asset =>
        {
            owned.Remove(asset);
            transferred.Add(asset);
        }));
        Assert.Equal(new[] { "caravan" }, transferred);
    }

    [Fact]
    public void OwnershipAddedDuringNativeCallbacksRequiresAnotherPass()
    {
        var owned = new List<string> { "forge" };
        Assert.Throws<InvalidOperationException>(() => ReignBusinessOwnershipRelease.ReleaseAll(owned, owned.Contains, asset =>
        {
            owned.Remove(asset);
            owned.Add("caravan");
        }));
        Assert.Equal(new[] { "caravan" }, owned);
    }
}
