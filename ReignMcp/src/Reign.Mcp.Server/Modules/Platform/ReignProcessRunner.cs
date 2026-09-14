using System.Diagnostics;
using System.Text;

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

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
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
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The spawned process may have exited between the timeout and kill.
            }
            if (callerCancelled) throw;
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
