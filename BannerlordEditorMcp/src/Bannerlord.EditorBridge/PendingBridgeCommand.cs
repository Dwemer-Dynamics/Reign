using System;
using System.Threading;
using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorBridge;

internal sealed class PendingBridgeCommand : IDisposable
{
    internal PendingBridgeCommand(BridgeRequest request)
    {
        Request = request;
    }

    internal BridgeRequest Request { get; }
    internal BridgeResponse? Response { get; private set; }
    internal ManualResetEventSlim Completed { get; } = new ManualResetEventSlim(false);

    internal void Complete(BridgeResponse response)
    {
        Response = response;
        Completed.Set();
    }

    public void Dispose() => Completed.Dispose();
}
