using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorMcp.Server;

public interface IBridgeClient
{
    bool IsConnected { get; }
    BridgeHello? Hello { get; }
    Task<BridgeResponse> SendAsync(BridgeRequest request, CancellationToken cancellationToken);
}
