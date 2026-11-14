using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private const string EconomyProviderGuardSchema = "reign-native-provider-free-guard-v1";

    [McpServerTool(Name = "reign_get_war_economy_provider_guard", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads the installed provider-free economy guard marker. A marker alone does not prove zero provider dispatches; read the running provider status and retain native test evidence too.")]
    public static async Task<object> GetWarEconomyProviderGuard(ReignMcpOptions options, ReignProcessRunner runner, CancellationToken cancellationToken = default)
    {
        var (distro, directory) = EconomyProviderGuardLocation();
        await RequireEconomyDistroRunning(runner, options, distro, cancellationToken).ConfigureAwait(false);
        return ReadEconomyProviderGuard(directory);
    }

    [McpServerTool(Name = "reign_set_war_economy_provider_guard", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Prepares or releases a persistent provider-generation tripwire for one enrolled economy test. Requires verification control, exact confirmation, the installed managed server stopped and Bannerlord/BLSE closed. Never starts a process group, changes provider credentials/settings, launches a game, or mutates a save. Release preserves the marker as evidence; only its owning task/run can release it.")]
    public static async Task<object> SetWarEconomyProviderGuard(ReignMcpOptions options, ReignProcessRunner runner,
        CampaignTestService campaignTests, string campaignId, string campaignTestRunId, string requestingTaskId,
        [AllowedValues("prepare", "release")] string operation = "prepare",
        [Description("Exact text: manage Reign provider-free economy test guard")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation, "manage Reign provider-free economy test guard");
        if (operation is not ("prepare" or "release")) throw new ArgumentException("Unsupported guard operation.");
        if (!Guid.TryParse(requestingTaskId, out var taskId)) throw new ArgumentException("A task UUID is required.");
        var authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (IsEconomyNativeGameProcess(process.ProcessName))
                        throw new InvalidOperationException("Close Bannerlord and BLSE before changing the provider guard.");
                }
                catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Native process ownership could not be checked."); }
            }
        }
        var (distro, directory) = EconomyProviderGuardLocation();
        await RequireEconomyDistroRunning(runner, options, distro, cancellationToken).ConfigureAwait(false);
        var stopped = await runner.RunAsync("wsl.exe", new[] { "-d", distro, "--", "systemctl", "show", "reignserver.service",
            "--property=LoadState", "--property=ActiveState", "--property=SubState", "--property=MainPID" },
            options.WorkspaceRoot, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken).ConfigureAwait(false);
        RequireEconomyServerStopped(stopped.Ok, stopped.StandardOutput);
        string path = Path.Combine(directory, "provider-free.json");
        RequireEconomyGuardDataRoot(directory);
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            using var marker = ReadBoundedEconomyGuard(path);
            RequireEconomyGuardOwner(marker.RootElement, authorization.CampaignTestRunId, authorization.CampaignId, taskId.ToString());
            if (operation == "release")
            {
                string retained = Path.Combine(directory, "completed-provider-free-" + Guid.NewGuid().ToString("N") + ".json");
                File.Move(path, retained);
                return new { ok = true, operation, retainedMarker = retained, savesChanged = 0, providerSettingsChanged = false };
            }
            return ReadEconomyProviderGuard(directory);
        }
        if (operation == "release") return new { ok = true, operation, alreadyReleased = true };
        string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Game Saves", authorization.BaselineSaveName + ".sav");
        string baselineHash;
        using (var save = File.OpenRead(savePath)) baselineHash = Convert.ToHexString(SHA256.HashData(save)).ToLowerInvariant();
        var payload = new Dictionary<string, string>
        {
            ["schema"] = EconomyProviderGuardSchema, ["runId"] = authorization.CampaignTestRunId,
            ["campaignId"] = authorization.CampaignId, ["timelineId"] = authorization.TimelineId,
            ["baselineSaveName"] = authorization.BaselineSaveName, ["baselineSha256"] = baselineHash,
            ["currentSaveName"] = authorization.CurrentSaveName, ["taskId"] = taskId.ToString(),
            ["createdUtc"] = DateTime.UtcNow.ToString("O")
        };
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            JsonSerializer.Serialize(output, payload);
        return ReadEconomyProviderGuard(directory);
    }

    private static async Task RequireEconomyDistroRunning(ReignProcessRunner runner, ReignMcpOptions options, string distro, CancellationToken cancellationToken)
    {
        var running = await runner.RunAsync("wsl.exe", new[] { "--list", "--running", "--quiet" }, options.WorkspaceRoot,
            TimeSpan.FromSeconds(20), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!running.Ok || !EconomyDistroIsRunning(running.StandardOutput, distro))
            throw new InvalidOperationException("The selected WSL distro must already be running; guard tools never boot a distro or start its services.");
    }
    internal static bool EconomyDistroIsRunning(string output, string distro) => output.Replace("\0", "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(x.Trim(), distro, StringComparison.OrdinalIgnoreCase));

    internal static bool IsEconomyNativeGameProcess(string name) => name.StartsWith("Bannerlord", StringComparison.OrdinalIgnoreCase)
        || name.Contains("BLSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("TaleWorlds.MountAndBlade", StringComparison.OrdinalIgnoreCase);

    internal static void RequireEconomyServerStopped(bool ok, string output)
    {
        var values = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().Split('=', 2))
            .Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1], StringComparer.Ordinal);
        if (!ok || values.GetValueOrDefault("LoadState") != "loaded" || values.GetValueOrDefault("MainPID") != "0"
            || values.GetValueOrDefault("ActiveState") != "inactive" || values.GetValueOrDefault("SubState") != "dead")
            throw new InvalidOperationException("The installed managed Reign server must be fully stopped before changing the provider guard.");
    }

    internal static void RequireEconomyGuardOwner(JsonElement marker, string runId, string campaignId, string taskId)
    {
        if (marker.GetProperty("schema").GetString() != EconomyProviderGuardSchema
            || marker.GetProperty("runId").GetString() != runId || marker.GetProperty("campaignId").GetString() != campaignId
            || marker.GetProperty("taskId").GetString() != taskId)
            throw new InvalidOperationException("The provider guard belongs to another task/run or is invalid.");
    }

    private static (string Distro, string Directory) EconomyProviderGuardLocation()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Native economy guard setup requires the installed Windows/WSL Reign host.");
        string installation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reign", "installation.json");
        using var document = JsonDocument.Parse(File.ReadAllText(installation));
        return ParseEconomyProviderGuardLocation(document.RootElement);
    }

    internal static (string Distro, string Directory) ParseEconomyProviderGuardLocation(JsonElement record)
    {
        string distro = record.GetProperty("wslDistro").GetString() ?? "";
        string root = record.GetProperty("dataRoot").GetString() ?? "";
        return ValidateEconomyProviderGuardLocation(record.GetProperty("serverMode").GetString() ?? "", distro, root);
    }

    internal static (string Distro, string Directory) ValidateEconomyProviderGuardLocation(string mode, string distro, string root)
    {
        if (mode != "dwemerdistro-wsl" || !Regex.IsMatch(distro, "^[A-Za-z0-9_-]{1,80}\\z"))
            throw new InvalidOperationException("The managed WSL installation identity is invalid.");
        string prefix = @"\\wsl.localhost\" + distro + @"\";
        string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || root.Split('\\', '/').Contains(".."))
            throw new InvalidOperationException("The provider guard must remain within the selected installed WSL data root.");
        return (distro, Path.Combine(full, "tests", "war-economy"));
    }

    private static JsonDocument ReadBoundedEconomyGuard(string path)
        => ReadBoundedEconomyGuard(path, out _);

    internal static JsonDocument ReadBoundedEconomyGuard(string path, out string markerSha256)
    {
        var info = new FileInfo(path);
        if (info.Length > 16384 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The provider guard marker is oversized or redirected.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Bound the actual read too: the file may grow after the metadata check.
        byte[] buffer = new byte[16385];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = input.Read(buffer, length, buffer.Length - length);
            if (read == 0) break;
            length += read;
        }
        if (length > 16384)
            throw new InvalidOperationException("The provider guard marker is oversized or redirected.");
        var payload = buffer.AsMemory(0, length);
        markerSha256 = Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant();
        return JsonDocument.Parse(payload);
    }
    private static object ReadEconomyProviderGuard(string directory)
    {
        RequireEconomyGuardDataRoot(directory);
        string path = Path.Combine(directory, "provider-free.json");
        try { File.GetAttributes(path); }
        catch (FileNotFoundException) { return new { armed = false, runtimeEvidenceRequired = true }; }
        catch (DirectoryNotFoundException) { return new { armed = false, runtimeEvidenceRequired = true }; }
        using var marker = ReadBoundedEconomyGuard(path, out string markerSha256);
        var owner = new Dictionary<string, string?>();
        foreach (string key in new[] { "schema", "runId", "campaignId", "timelineId", "taskId", "baselineSaveName", "baselineSha256", "currentSaveName", "createdUtc" })
            owner[key] = marker.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new { armed = true, markerPath = path, owner, markerSha256,
            runtimeEvidenceRequired = true, snapshotProvesZeroDispatches = false };
    }

    private static void RequireEconomyGuardDataRoot(string directory)
    {
        string dataRoot = Path.GetDirectoryName(Path.GetDirectoryName(directory))!;
        if (!Directory.Exists(dataRoot))
            throw new InvalidOperationException("The installed runtime data root is unavailable; guard state is unknown.");
    }
}
