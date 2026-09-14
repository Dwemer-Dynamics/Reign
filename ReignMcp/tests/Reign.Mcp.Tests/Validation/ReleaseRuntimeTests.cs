using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReleaseRuntimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RuntimeBuildRequiresBothTrustedGatesBeforeInspectingPaths(bool build, bool offline)
    {
        var options = TestOptions.Create() with { AllowBuild = build, AllowOfflineVerification = offline };
        var service = new ReignValidationService(options, new ReignProjectCatalog(options),
            new ReignProcessRunner(options, new SensitiveDataRedactor()), new NeverAudit());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildReleaseRuntimeAsync("", ""));
        Assert.Contains("trusted build and offline-verification", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildReleasePackageAsync("", "", ""));
    }

    [Fact]
    public async Task RuntimeBuildRejectsRelativeToolsBeforeAcquiringLease()
    {
        var options = TestOptions.Create() with { AllowBuild = true, AllowOfflineVerification = true };
        var service = new ReignValidationService(options, new ReignProjectCatalog(options),
            new ReignProcessRunner(options, new SensitiveDataRedactor()), new NeverAudit());
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildReleaseRuntimeAsync("python.exe", "models"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildReleasePackageAsync("../wrong", "python.exe", "spec.json"));
    }

    private sealed class NeverAudit : IReignRepositoryHygieneAudit
    {
        public Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unsafe runtime input reached the audit.");
    }
}
