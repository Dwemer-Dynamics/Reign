using System.Text.Json;
using Bannerlord.EditorMcp.Protocol;
using Bannerlord.EditorMcp.Server;

namespace Bannerlord.EditorMcp.Tests;

public sealed class TerrainSamplingTests
{
    [Fact]
    public async Task TiledReadForwardsLogicalGridWindow()
    {
        var bridge = new CapturingBridgeClient();

        await EditorTools.InspectTerrain(
            bridge,
            sampleColumns: 257,
            sampleRows: 257,
            sampleColumnOffset: 256,
            sampleRowOffset: 512,
            totalSampleColumns: 1025,
            totalSampleRows: 1025);

        Assert.NotNull(bridge.LastRequest);
        Assert.Equal(BridgeCommands.SceneInspectTerrain, bridge.LastRequest.Command);
        using var arguments = JsonDocument.Parse(bridge.LastRequest.ArgumentsJson);
        var root = arguments.RootElement;
        Assert.Equal(257, root.GetProperty("sampleColumns").GetInt32());
        Assert.Equal(257, root.GetProperty("sampleRows").GetInt32());
        Assert.Equal(256, root.GetProperty("sampleColumnOffset").GetInt32());
        Assert.Equal(512, root.GetProperty("sampleRowOffset").GetInt32());
        Assert.Equal(1025, root.GetProperty("totalSampleColumns").GetInt32());
        Assert.Equal(1025, root.GetProperty("totalSampleRows").GetInt32());
    }

    [Fact]
    public async Task LegacyBoundedReadUsesItsOwnDimensionsAsTheLogicalGrid()
    {
        var bridge = new CapturingBridgeClient();

        await EditorTools.InspectTerrain(bridge, sampleColumns: 128, sampleRows: 96);

        using var arguments = JsonDocument.Parse(bridge.LastRequest!.ArgumentsJson);
        var root = arguments.RootElement;
        Assert.Equal(128, root.GetProperty("totalSampleColumns").GetInt32());
        Assert.Equal(96, root.GetProperty("totalSampleRows").GetInt32());
        Assert.Equal(0, root.GetProperty("sampleColumnOffset").GetInt32());
        Assert.Equal(0, root.GetProperty("sampleRowOffset").GetInt32());
    }

    [Theory]
    [InlineData(258, 1, 0, 0, 258, 1)]
    [InlineData(1, 258, 0, 0, 1, 258)]
    [InlineData(257, 257, 769, 0, 1025, 1025)]
    [InlineData(257, 257, 0, 769, 1025, 1025)]
    [InlineData(1, 1, 0, 0, 4098, 1)]
    public async Task InvalidOrOutOfBoundsTileIsRejectedBeforeTransport(
        int sampleColumns,
        int sampleRows,
        int sampleColumnOffset,
        int sampleRowOffset,
        int totalSampleColumns,
        int totalSampleRows)
    {
        var bridge = new CapturingBridgeClient();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => EditorTools.InspectTerrain(
            bridge,
            sampleColumns,
            sampleRows,
            sampleColumnOffset,
            sampleRowOffset,
            totalSampleColumns,
            totalSampleRows));

        Assert.Null(bridge.LastRequest);
    }

    [Fact]
    public async Task WindowArgumentsRequireSampling()
    {
        var bridge = new CapturingBridgeClient();

        await Assert.ThrowsAsync<ArgumentException>(() => EditorTools.InspectTerrain(
            bridge,
            sampleColumnOffset: 1));

        Assert.Null(bridge.LastRequest);
    }

    private sealed class CapturingBridgeClient : IBridgeClient
    {
        public bool IsConnected => true;
        public BridgeHello? Hello => null;
        public BridgeRequest? LastRequest { get; private set; }

        public Task<BridgeResponse> SendAsync(BridgeRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new BridgeResponse
            {
                RequestId = request.RequestId,
                Ok = true
            });
        }
    }
}
