using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Bannerlord.EditorMcp.Protocol;

public sealed class WriteSafetyContext
{
    public bool BridgeEnabled { get; set; }
    public bool ReadOnly { get; set; }
    public bool SafeWritesArmed { get; set; }
    public string ConfiguredTargetModule { get; set; } = string.Empty;
    public string ConfiguredDisposableScene { get; set; } = string.Empty;
    public string CurrentScene { get; set; } = string.Empty;
    public string CurrentRevision { get; set; } = string.Empty;
}

public sealed class SafetyRejection
{
    public SafetyRejection(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }
}

public static class WriteSafetyValidator
{
    private static readonly Regex ModuleIdPattern = new Regex(
        "^[A-Za-z][A-Za-z0-9_.-]{0,63}$",
        RegexOptions.CultureInvariant);

    public static bool IsValidModuleId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && ModuleIdPattern.IsMatch(value);
    }

    public static SafetyRejection? Validate(BridgeRequest request, WriteSafetyContext context)
    {
        if (!context.BridgeEnabled)
        {
            return new SafetyRejection("bridge_disabled", "Bridge Enabled is off in the editor.");
        }

        if (context.ReadOnly || !context.SafeWritesArmed)
        {
            return new SafetyRejection("writes_disarmed", "The editor bridge is read-only or Safe Writes Armed is off.");
        }

        if (!IsValidModuleId(request.TargetModule) ||
            !string.Equals(request.TargetModule, context.ConfiguredTargetModule, StringComparison.Ordinal))
        {
            return new SafetyRejection("invalid_target_module", "The target module does not match the editor controller allowlist.");
        }

        if (string.IsNullOrWhiteSpace(context.ConfiguredDisposableScene) ||
            !string.Equals(request.Scene, context.ConfiguredDisposableScene, StringComparison.Ordinal) ||
            !string.Equals(request.Scene, context.CurrentScene, StringComparison.Ordinal))
        {
            return new SafetyRejection("invalid_target_scene", "Writes are allowed only in the configured disposable scene.");
        }

        if (!string.Equals(request.ExpectedSceneRevision, context.CurrentRevision, StringComparison.Ordinal))
        {
            return new SafetyRejection(
                "stale_scene_revision",
                "The expected scene revision is stale. Reinspect the scene before writing.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 160)
        {
            return new SafetyRejection(
                "invalid_idempotency_key",
                "A non-empty idempotency key of at most 160 characters is required.");
        }

        return null;
    }
}

public enum IdempotencyCheck
{
    New,
    Replay,
    Conflict
}

public sealed class IdempotencyRegistry
{
    private readonly Dictionary<string, string> _fingerprints =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IdempotencyCheck Check(string key, string fingerprint)
    {
        if (!_fingerprints.TryGetValue(key, out var existing))
        {
            return IdempotencyCheck.New;
        }

        return string.Equals(existing, fingerprint, StringComparison.Ordinal)
            ? IdempotencyCheck.Replay
            : IdempotencyCheck.Conflict;
    }

    public void Commit(string key, string fingerprint)
    {
        _fingerprints[key] = fingerprint;
    }
}
