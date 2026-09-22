using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorBridge;

internal sealed class PipeBridgeClient : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<BridgeHello> _helloFactory;
    private readonly ConcurrentQueue<PendingBridgeCommand> _commands = new ConcurrentQueue<PendingBridgeCommand>();
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly object _pipeLock = new object();
    private NamedPipeClientStream? _pipe;
    private Task? _worker;

    internal PipeBridgeClient(string pipeName, Func<BridgeHello> helloFactory)
    {
        _pipeName = pipeName;
        _helloFactory = helloFactory;
    }

    internal bool Connected
    {
        get
        {
            lock (_pipeLock)
            {
                return _pipe?.IsConnected == true;
            }
        }
    }

    internal void Start()
    {
        if (_worker is not null)
        {
            return;
        }

        _worker = Task.Factory.StartNew(
            Run,
            _stop.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    internal bool TryDequeue(out PendingBridgeCommand? command) => _commands.TryDequeue(out command);

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                pipe.Connect(2000);

                lock (_pipeLock)
                {
                    _pipe = pipe;
                }

                using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" })
                {
                    writer.WriteLine(BridgeJson.Serialize(_helloFactory()));
                    while (!_stop.IsCancellationRequested && pipe.IsConnected)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            break;
                        }

                        var request = BridgeJson.Deserialize<BridgeRequest>(line);
                        if (request is null)
                        {
                            continue;
                        }

                        using (var pending = new PendingBridgeCommand(request))
                        {
                            _commands.Enqueue(pending);
                            WaitHandle.WaitAny(new[] { pending.Completed.WaitHandle, _stop.Token.WaitHandle });
                            if (_stop.IsCancellationRequested)
                            {
                                break;
                            }

                            var response = pending.Response ?? BridgeResponse.Rejected(
                                request,
                                "bridge_no_response",
                                "The editor thread did not produce a response.",
                                string.Empty);
                            writer.WriteLine(BridgeJson.Serialize(response));
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Normal while the MCP server or editor is restarting.
            }
            catch (TimeoutException)
            {
                // The MCP server is not running yet.
            }
            catch (Exception)
            {
                // Do not throw through a background thread into the editor.
            }
            finally
            {
                lock (_pipeLock)
                {
                    if (ReferenceEquals(_pipe, pipe))
                    {
                        _pipe = null;
                    }
                }

                pipe?.Dispose();
            }

            _stop.Token.WaitHandle.WaitOne(1000);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_pipeLock)
        {
            _pipe?.Dispose();
            _pipe = null;
        }

        try
        {
            _worker?.Wait(2000);
        }
        catch (AggregateException)
        {
            // Shutdown is best effort and must not destabilize the editor.
        }

        _stop.Dispose();
    }
}
