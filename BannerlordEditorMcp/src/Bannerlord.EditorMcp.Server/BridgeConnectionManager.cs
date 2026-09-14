using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Bannerlord.EditorMcp.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bannerlord.EditorMcp.Server;

public sealed class BridgeConnectionManager : BackgroundService, IBridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BridgeServerOptions _options;
    private readonly ILogger<BridgeConnectionManager> _logger;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _connectionLock = new();
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private TaskCompletionSource<bool>? _connectionLost;
    private BridgeHello? _hello;

    public BridgeConnectionManager(BridgeServerOptions options, ILogger<BridgeConnectionManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _pipe?.IsConnected == true && _hello is not null;
            }
        }
    }

    public BridgeHello? Hello
    {
        get
        {
            lock (_connectionLock)
            {
                return _hello;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _options.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                _logger.LogInformation("Waiting for Bannerlord editor bridge on pipe {PipeName}", _options.PipeName);
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);

                var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                var helloLine = await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                var hello = DeserializeHello(helloLine);
                var connectionLost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                lock (_connectionLock)
                {
                    _pipe = pipe;
                    _reader = reader;
                    _writer = writer;
                    _hello = hello;
                    _connectionLost = connectionLost;
                }

                _logger.LogInformation(
                    "Editor bridge connected: instance {InstanceId}, scene {SceneName}, module {ModuleId}",
                    hello.BridgeInstanceId,
                    hello.SceneName,
                    hello.TargetModuleId);

                await connectionLost.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Editor bridge connection failed");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            finally
            {
                DropConnection(pipe);
            }
        }
    }

    public async Task<BridgeResponse> SendAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StreamReader reader;
            StreamWriter writer;
            lock (_connectionLock)
            {
                if (_pipe?.IsConnected != true || _reader is null || _writer is null || _hello is null)
                {
                    throw new InvalidOperationException("The Bannerlord editor bridge is not connected.");
                }

                reader = _reader;
                writer = _writer;
            }

            try
            {
                var requestJson = JsonSerializer.Serialize(request, JsonOptions);
                await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
                var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    throw new IOException("The editor bridge closed the pipe without a response.");
                }

                var response = JsonSerializer.Deserialize<BridgeResponse>(responseLine, JsonOptions)
                    ?? throw new IOException("The editor bridge returned an empty response.");
                if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
                {
                    throw new IOException("The editor bridge returned a mismatched request identifier.");
                }

                return response;
            }
            catch
            {
                SignalConnectionLost();
                throw;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static BridgeHello DeserializeHello(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new IOException("The editor bridge did not send a handshake.");
        }

        var hello = JsonSerializer.Deserialize<BridgeHello>(json, JsonOptions)
            ?? throw new IOException("The editor bridge sent an invalid handshake.");
        if (!string.Equals(hello.Kind, "hello", StringComparison.Ordinal) ||
            !string.Equals(hello.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
        {
            throw new IOException($"Unsupported editor bridge protocol '{hello.ProtocolVersion}'.");
        }

        return hello;
    }

    private void SignalConnectionLost()
    {
        TaskCompletionSource<bool>? signal;
        lock (_connectionLock)
        {
            signal = _connectionLost;
        }

        signal?.TrySetResult(true);
    }

    private void DropConnection(NamedPipeServerStream? expectedPipe)
    {
        lock (_connectionLock)
        {
            if (expectedPipe is not null && !ReferenceEquals(expectedPipe, _pipe))
            {
                expectedPipe.Dispose();
                return;
            }

            _hello = null;
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe?.Dispose();
            _reader = null;
            _writer = null;
            _pipe = null;
            _connectionLost = null;
        }
    }
}
