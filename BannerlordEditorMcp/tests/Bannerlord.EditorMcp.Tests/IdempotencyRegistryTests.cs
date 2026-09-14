using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorMcp.Tests;

public sealed class IdempotencyRegistryTests
{
    [Fact]
    public void DuplicateFingerprintIsReplay()
    {
        var registry = new IdempotencyRegistry();
        registry.Commit("key", "fingerprint-a");

        Assert.Equal(IdempotencyCheck.Replay, registry.Check("key", "fingerprint-a"));
    }

    [Fact]
    public void DuplicateKeyWithDifferentFingerprintIsConflict()
    {
        var registry = new IdempotencyRegistry();
        registry.Commit("key", "fingerprint-a");

        Assert.Equal(IdempotencyCheck.Conflict, registry.Check("key", "fingerprint-b"));
    }
}
