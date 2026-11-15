using System;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Runtime
{
    // Diagnostic primitive only. There is deliberately no conversation/journal hook,
    // setting, Harmony patch or production enable switch. Timer accounting is unqualified.
    internal static class ReignConversationClockProbe
    {
        internal const string Schema = "reign-conversation-clock-probe-v1";
        internal const string QualifiedNativeModule = "aa6e6095-4606-4c71-a5a8-2f29566591af";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static JObject Read(object tracker)
        {
            Type type = RequireTracker(tracker);
            return new JObject
            {
                ["ticks"] = ReadLong(type, tracker, "NumTicks"),
                ["deltaTicks"] = ReadLong(type, tracker, "DeltaTimeInTicks"),
                ["nativeModuleMvid"] = type.Module.ModuleVersionId.ToString(),
                ["timerAccountingQualified"] = false
            };
        }

        internal static JObject PulseFiveMinutes(object tracker)
        {
            Type type = RequireTracker(tracker);
            Type time = type.Assembly.GetType("TaleWorlds.CampaignSystem.CampaignTime", true);
            long second = (long)time.GetField("TimeTicksPerSecond", Static).GetValue(null);
            long minute = (long)time.GetField("TimeTicksPerMinute", Static).GetValue(null);
            long expected = checked(5L * minute);
            if (second <= 0 || minute != checked(60L * second)
                || (long)(300f * (float)second) != expected)
                throw new InvalidOperationException("Native five-minute float/integer units do not match.");
            long before = ReadLong(type, tracker, "NumTicks");
            long afterExpected = checked(before + expected);
            MethodInfo tick = type.GetMethod("Tick", Instance, null, new[] { typeof(float) }, null);
            if (tick == null || tick.ReturnType != typeof(void))
                throw new InvalidOperationException("The qualified native clock Tick signature is unavailable.");
            // Never expose a five-minute DeltaTime to a later mission frame or save.
            // This does NOT reconcile entity ticker debt; the native diagnostic must
            // be discarded by restoring its enrolled checkpoint, never used in play.
            try { tick.Invoke(tracker, new object[] { 300f }); }
            finally { tick.Invoke(tracker, new object[] { 0f }); }
            JObject after = Read(tracker);
            if (after.Value<long>("ticks") != afterExpected || after.Value<long>("deltaTicks") != 0)
                throw new InvalidOperationException("Native clock postcondition failed; restore the disposable checkpoint without saving.");
            after["beforeTicks"] = before;
            after["advancedTicks"] = expected;
            after["advancedMinutes"] = 5;
            return after;
        }

        private static Type RequireTracker(object tracker)
        {
            Type type = tracker?.GetType();
            if (type == null || type.FullName != "TaleWorlds.CampaignSystem.MapTimeTracker"
                || type.Module.ModuleVersionId.ToString() != QualifiedNativeModule)
                throw new InvalidOperationException("Conversation clock probe is not qualified for this native assembly.");
            return type;
        }

        private static long ReadLong(Type type, object instance, string name)
        {
            PropertyInfo property = type.GetProperty(name, Instance);
            if (property == null || property.PropertyType != typeof(long))
                throw new InvalidOperationException("Native clock member unavailable: " + name);
            return (long)property.GetValue(instance, null);
        }
    }

    // Kept independent of native/GUI state so failure recovery is exercised with
    // real receipts in the offline assembly probe, not just source assertions.
    internal sealed class ReignConversationClockProbeSession
    {
        internal static bool LeaseIsCurrent(string expiresUtc, DateTimeOffset now)
        {
            return DateTimeOffset.TryParse(expiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires)
                && expires > now && expires - now <= TimeSpan.FromMinutes(1);
        }

        private string _probeId;
        private JObject _receipt;
        internal bool Attempted => _receipt != null;

        internal static JObject NewReceipt(string probeId)
        {
            return new JObject
            {
                ["schema"] = ReignConversationClockProbe.Schema, ["probeId"] = probeId,
                ["timerAccountingQualified"] = false, ["inSceneAcceptance"] = false,
                ["evidenceKind"] = "guarded-native-raw-clock-diagnostic",
                ["requiresCheckpointRestore"] = false,
                ["nativeSaveInterception"] = false,
                ["saveLimitation"] = "Guarded checkpoint saves are blocked after a pulse. Native manual/autosaves are not intercepted; restore Current without saving before closing chat."
            };
        }

        internal JObject Run(string profile, string probeId, JObject before,
            Func<JObject> pulse, Func<JObject> captureAfter)
        {
            if (profile != "observe" && profile != "pulse")
                throw new ArgumentException("Unknown clock diagnostic profile.");
            if (string.IsNullOrWhiteSpace(probeId)) throw new ArgumentException("A probe ID is required.");
            if (profile == "pulse" && Attempted)
            {
                if (_probeId != probeId)
                    throw new InvalidOperationException("A clock probe already attempted mutation in this loaded campaign. Restore Current without saving before another pulse.");
                JObject duplicate = (JObject)_receipt.DeepClone();
                duplicate["duplicate"] = true;
                return duplicate;
            }
            JObject result = NewReceipt(probeId);
            result["before"] = before.DeepClone();
            result["advancedMinutes"] = 0;
            result["requiresCheckpointRestore"] = Attempted;
            if (profile == "observe") return result;
            // Store the restore requirement before ANY mutation or postcondition.
            _probeId = probeId;
            _receipt = result;
            result["requiresCheckpointRestore"] = true;
            result["duplicate"] = false;
            try
            {
                result["pulse"] = pulse();
                result["advancedMinutes"] = 5;
                result["after"] = captureAfter();
            }
            catch (Exception ex)
            {
                result["failure"] = ex.GetBaseException().Message;
                result["advancedMinutes"] = null; // Partial mutation is unknown, never reported as zero.
            }
            return (JObject)result.DeepClone();
        }

        internal static bool ObserveLater(JObject result, Func<JObject> observe)
        {
            try { result["later"] = observe(); return true; }
            catch (Exception ex)
            {
                result["observationFailure"] = ex.GetBaseException().Message;
                return false;
            }
        }
    }
}
