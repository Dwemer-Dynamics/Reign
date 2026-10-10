using ReignBeta.Runtime;

namespace Reign.Mcp.Tests;

public sealed class WildernessEventCompletionTests
{
    [Fact]
    public async Task ProviderResultWaitsForTheGameThreadAndPublishesExactlyOnce()
    {
        var provider = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new TaskCompletionSource<Func<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int completions = 0;
        string? received = null;
        var run = ReignWildernessEventCompletion.RunAsync(provider.Task,
            work => { queued.SetResult(work); return dispatched.Task; },
            () => true,
            result => { received = result; completions++; return true; });
        Assert.False(queued.Task.IsCompleted);
        provider.SetResult("the exact generated event");
        var action = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, completions);
        Assert.False(run.IsCompleted);
        dispatched.SetResult(action());
        Assert.True(await run);
        Assert.Equal(1, completions);
        Assert.Equal("the exact generated event", received);
    }

    [Fact]
    public async Task CampaignChangeAfterTheProviderCompletesRejectsStaleResults()
    {
        bool sameCampaign = true;
        var queued = new TaskCompletionSource<Func<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int completions = 0;
        var run = ReignWildernessEventCompletion.RunAsync(Task.FromResult("old campaign"),
            work => { queued.SetResult(work); return dispatched.Task; },
            () => sameCampaign,
            _ => { completions++; return true; });
        var action = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sameCampaign = false;
        dispatched.SetResult(action());
        Assert.False(await run);
        Assert.Equal(0, completions);
    }

    [Fact]
    public async Task FailedGenerationNeverDispatchesOrCreatesAnEvent()
    {
        var failure = new InvalidOperationException("provider failed");
        int dispatches = 0;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReignWildernessEventCompletion.RunAsync(Task.FromException<string>(failure),
                work => { dispatches++; return Task.FromResult(work()); }, () => true, _ => true));
        Assert.Same(failure, thrown);
        Assert.Equal(0, dispatches);
    }

    [Fact]
    public async Task CancelledGenerationNeverDispatches()
    {
        int dispatches = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReignWildernessEventCompletion.RunAsync(Task.FromCanceled<string>(new CancellationToken(true)),
                work => { dispatches++; return Task.FromResult(work()); }, () => true, _ => true));
        Assert.Equal(0, dispatches);
    }

    [Fact]
    public async Task PresentationFailureRemainsObservableForTheOwningRequest()
    {
        var failure = new InvalidOperationException("presentation failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReignWildernessEventCompletion.RunAsync(Task.FromResult("ready"),
                work => Task.FromResult(work()), () => true, _ => throw failure));
        Assert.Same(failure, thrown);
    }
}
