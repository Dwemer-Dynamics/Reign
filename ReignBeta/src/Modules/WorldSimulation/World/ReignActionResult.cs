using System.Collections.Generic;

namespace ReignBeta.World
{
    public sealed class ReignActionResult
    {
        private ReignActionResult(
            bool success,
            bool completed,
            string message,
            string resultCode,
            string outcome,
            string failureKind,
            bool retryable,
            float nextAttemptDelayDays)
        {
            Success = success;
            Completed = completed;
            Message = message ?? string.Empty;
            ResultCode = resultCode ?? string.Empty;
            Outcome = outcome ?? string.Empty;
            FailureKind = failureKind ?? string.Empty;
            Retryable = retryable;
            NextAttemptDelayDays = nextAttemptDelayDays;
            DebugMessage = string.Empty;
            ChangedEntities = new List<Dictionary<string, string>>();
            Effects = new List<Dictionary<string, string>>();
            Diagnostics = new List<Dictionary<string, string>>();
        }

        public bool Success { get; }
        public bool Completed { get; }
        public string Message { get; private set; }
        public string ResultCode { get; private set; }
        public string Outcome { get; private set; }
        public string FailureKind { get; private set; }
        public bool Retryable { get; private set; }
        public float NextAttemptDelayDays { get; private set; }
        public string DebugMessage { get; private set; }
        public List<Dictionary<string, string>> ChangedEntities { get; }
        public List<Dictionary<string, string>> Effects { get; }
        public List<Dictionary<string, string>> Diagnostics { get; }

        public static ReignActionResult Done(string message)
        {
            return new ReignActionResult(true, true, message, "completed", "completed", string.Empty, false, 0f);
        }

        public static ReignActionResult Progress(string message)
        {
            return new ReignActionResult(true, false, message, "in_progress", "progress", string.Empty, true, 0.25f);
        }

        public static ReignActionResult NoOp(string message)
        {
            return new ReignActionResult(true, true, message, "no_mechanical_change", "noop", string.Empty, false, 0f);
        }

        public static ReignActionResult Rejected(string message, string resultCode, string reasonKind)
        {
            return new ReignActionResult(false, true, message, resultCode, "rejected", reasonKind, false, 0f);
        }

        public static ReignActionResult Retry(string message, string resultCode, float nextAttemptDelayDays)
        {
            return new ReignActionResult(false, false, message, resultCode, "retry", "transient_world_state", true, nextAttemptDelayDays <= 0f ? 0.5f : nextAttemptDelayDays);
        }

        public static ReignActionResult FailTerminal(string message)
        {
            return FailTerminal(message, "execution_failed", "execution_failure");
        }

        public static ReignActionResult FailTerminal(string message, string resultCode, string failureKind)
        {
            return new ReignActionResult(false, false, message, resultCode, "failed", failureKind, false, 0f);
        }

        public static ReignActionResult FailRetryable(string message, string resultCode, string failureKind, float nextAttemptDelayDays)
        {
            return new ReignActionResult(false, false, message, resultCode, "retry", failureKind, true, nextAttemptDelayDays <= 0f ? 0.5f : nextAttemptDelayDays);
        }

        public static ReignActionResult BlockedBySettings(string message)
        {
            return new ReignActionResult(false, false, message, "blocked_by_settings", "blocked_by_settings", "settings", false, 0f);
        }

        public static ReignActionResult ValidationFailed(string message)
        {
            return new ReignActionResult(false, false, message, "validation_failed", "validation_failed", "validation", false, 0f);
        }

        public static ReignActionResult Obsolete(string message, string resultCode)
        {
            return new ReignActionResult(false, true, message, resultCode, "obsolete", "stale_world_state", false, 0f);
        }

        public static ReignActionResult DiplomacyValidationFailed(ReignWorldActionRecord action, string message)
        {
            string reason = message ?? string.Empty;
            if (action != null && action.Type == ReignWorldActionType.DiplomacyRansomPackage
                && (reason.Contains("is no longer a prisoner")
                    || reason.Contains("is not held by either involved kingdom")))
            {
                return Obsolete(reason, "ransom_target_obsolete");
            }

            return ValidationFailed(reason);
        }

        public static ReignActionResult Fail(string message)
        {
            return FailTerminal(message);
        }

        public ReignActionResult WithResultCode(string resultCode)
        {
            ResultCode = resultCode ?? string.Empty;
            return this;
        }

        public ReignActionResult WithMessage(string message)
        {
            Message = message ?? string.Empty;
            return this;
        }

        public ReignActionResult WithOutcome(string outcome)
        {
            Outcome = outcome ?? string.Empty;
            return this;
        }

        public ReignActionResult WithFailureKind(string failureKind)
        {
            FailureKind = failureKind ?? string.Empty;
            return this;
        }

        public ReignActionResult WithDebugMessage(string debugMessage)
        {
            DebugMessage = debugMessage ?? string.Empty;
            return this;
        }

        public ReignActionResult WithNextAttemptDelay(float days)
        {
            NextAttemptDelayDays = days <= 0f ? NextAttemptDelayDays : days;
            return this;
        }

        public ReignActionResult WithChangedEntity(string entityType, string id, string name, string changeType)
        {
            ChangedEntities.Add(new Dictionary<string, string>
            {
                ["entityType"] = entityType ?? string.Empty,
                ["id"] = id ?? string.Empty,
                ["name"] = name ?? string.Empty,
                ["changeType"] = changeType ?? string.Empty
            });
            return this;
        }

        public ReignActionResult WithEffect(string effectType, string entityType, string id, string name, string detail)
        {
            Effects.Add(new Dictionary<string, string>
            {
                ["effectType"] = effectType ?? string.Empty,
                ["entityType"] = entityType ?? string.Empty,
                ["id"] = id ?? string.Empty,
                ["name"] = name ?? string.Empty,
                ["detail"] = detail ?? string.Empty
            });
            return this;
        }

        public ReignActionResult WithDiagnostic(string key, string value)
        {
            Diagnostics.Add(new Dictionary<string, string>
            {
                ["key"] = key ?? string.Empty,
                ["value"] = value ?? string.Empty
            });
            return this;
        }
    }
}
