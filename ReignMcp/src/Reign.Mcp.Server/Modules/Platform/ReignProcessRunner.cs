using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Reign.Mcp.Server;

public sealed class ReignProcessRunner(
    ReignMcpOptions options,
    SensitiveDataRedactor redactor)
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan? timeout = null,
        string? artifactDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var pair in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        string? linuxInput = null;
        string? linuxPidFile = null;
        if (OperatingSystem.IsWindows() && Path.GetFileName(executable) == "ReignBetaServer.dll")
        {
            if (environment?.GetValueOrDefault("REIGN_VALIDATION_MODE") != "1"
                || environment.GetValueOrDefault("REIGN_DB_NAME") != "ReignValidation")
                throw new InvalidOperationException("Server tooling requires an isolated Linux validation database.");
            // Send inputs through stdin, keeping credentials out of process arguments.
            Directory.CreateDirectory(Path.Combine(options.BuildRoot, "linux-processes"));
            linuxPidFile = Path.Combine(options.BuildRoot, "linux-processes", Guid.NewGuid().ToString("N") + ".json");
            linuxInput = JsonSerializer.Serialize(new { executable, arguments, workingDirectory, environment, pidFile = linuxPidFile,
                timeout = Math.Max(1, (timeout ?? options.ProcessTimeout).TotalSeconds - 2) });
            startInfo.FileName = "wsl.exe";
            startInfo.RedirectStandardInput = true;
            startInfo.ArgumentList.Clear();
            foreach (var argument in new[] { "-d", Environment.GetEnvironmentVariable("REIGN_WSL_DISTRO") ?? "DwemerAI4Skyrim3",
                "-u", "dwemer", "--", "python3", "-c", LinuxVerificationRunner })
                startInfo.ArgumentList.Add(argument);
        }
        else if (OperatingSystem.IsLinux() && Path.GetFileName(executable) == "ReignBetaServer.dll")
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Insert(0, executable);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }
        if (linuxInput is not null)
        {
            await process.StandardInput.WriteAsync(linuxInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var outputTask = DrainAsync(
            process.StandardOutput,
            options.MaxToolTextBytes / 2,
            cancellationToken);
        var errorTask = DrainAsync(
            process.StandardError,
            options.MaxToolTextBytes / 2,
            cancellationToken);
        var timedOut = false;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? options.ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bool callerCancelled = cancellationToken.IsCancellationRequested;
            timedOut = !callerCancelled;
            try
            {
                if (linuxPidFile is not null && File.Exists(linuxPidFile))
                {
                    // Match Linux process start time before terminating the owned group; never trust a reused PID.
                    using var identity = JsonDocument.Parse(await File.ReadAllTextAsync(linuxPidFile, CancellationToken.None));
                    var cleanup = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var argument in new[] { "-d", Environment.GetEnvironmentVariable("REIGN_WSL_DISTRO") ?? "DwemerAI4Skyrim3",
                        "-u", "dwemer", "--", "python3", "-c",
                        "import os,pathlib,signal,sys; p=int(sys.argv[1]); f=pathlib.Path('/proc')/str(p)/'stat'; same=f.exists() and f.read_text().split(') ',1)[1].split()[19]==sys.argv[2]; os.killpg(p,signal.SIGKILL) if same and os.getsid(p)==p else None",
                        identity.RootElement.GetProperty("pid").GetInt32().ToString(), identity.RootElement.GetProperty("started").GetString()! })
                        cleanup.ArgumentList.Add(argument);
                    using var terminator = Process.Start(cleanup);
                    if (terminator is not null)
                        await terminator.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            catch { /* Cleanup can race with Linux process exit. */ }
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The spawned process may have exited between the timeout and kill.
            }
            if (callerCancelled) throw;
        }
        finally
        {
            if (linuxPidFile is not null) File.Delete(linuxPidFile);
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        stopwatch.Stop();
        var exitCode = process.HasExited ? process.ExitCode : -1;
        return new ProcessResult
        {
            Ok = !timedOut && exitCode == 0,
            ExitCode = exitCode,
            Command = RenderCommand(executable, arguments),
            WorkingDirectory = workingDirectory,
            DurationMs = stopwatch.ElapsedMilliseconds,
            TimedOut = timedOut,
            OutputTruncated = output.Truncated || error.Truncated,
            StandardOutput = redactor.RedactText(output.Text),
            StandardError = redactor.RedactText(error.Text),
            ArtifactDirectory = artifactDirectory
        };
    }

    // The Linux child owns a separate process group and a bounded lifetime, including after WSL relay loss.
    private const string LinuxVerificationRunner = """
        import json,os,pathlib,re,signal,subprocess,sys
        request=json.load(sys.stdin)
        def mapped(value):
            if len(value)>2 and value[1]==':' and value[2] in (chr(92),'/'):
                return subprocess.check_output(['wslpath','-a','-u',value.replace(chr(92),'/')],text=True).strip()
            return value
        working=mapped(request['workingDirectory'])
        environment=os.environ.copy()
        environment.update({k:mapped(v) for k,v in request['environment'].items()})
        environment.pop('REIGN_INSTALLATION_FILE',None)
        environment['REIGN_DATA_ROOT']=os.path.join(working,'data')
        process=subprocess.Popen(['dotnet',mapped(request['executable']),*[mapped(a) for a in request['arguments']]],
            cwd=working,env=environment,start_new_session=True)
        started=(pathlib.Path('/proc')/str(process.pid)/'stat').read_text().split(') ',1)[1].split()[19]
        pathlib.Path(mapped(request['pidFile'])).write_text(json.dumps({'pid':process.pid,'started':started}))
        def stop(signum,frame):
            try: os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError: pass
            raise SystemExit(128+signum)
        for sig in (signal.SIGTERM,signal.SIGINT,signal.SIGHUP): signal.signal(sig,stop)
        try: sys.exit(process.wait(timeout=request['timeout']))
        except subprocess.TimeoutExpired: stop(signal.SIGTERM,None)
        """;

    private static async Task<(string Text, bool Truncated)> DrainAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 32 * 1024));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
            if (read > remaining)
            {
                truncated = true;
            }
        }
        return (builder.ToString(), truncated);
    }

    private static string RenderCommand(string executable, IReadOnlyList<string> arguments)
    {
        return Path.GetFileName(executable) + " " + string.Join(" ", arguments.Select(argument =>
            argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : argument));
    }
}
