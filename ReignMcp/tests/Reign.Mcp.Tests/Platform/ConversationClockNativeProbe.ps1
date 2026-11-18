param(
    [Parameter(Mandatory=$true)][string]$ClientAssembly,
    [Parameter(Mandatory=$true)][string]$BannerlordPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:clockRoots = @([IO.Path]::GetDirectoryName($ClientAssembly), $gameBin)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:clockRoots) {
        $path = Join-Path $root $name
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$result = @{ ok=$false; evidenceKind='OFFLINE SYNTHETIC - NOT IN-SCENE ACCEPTANCE'; nativeAcceptance=$false; timerAccountingQualified=$false }
try {
    $client = [Reflection.Assembly]::LoadFrom($ClientAssembly)
    $native = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $library = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.Library.dll'))
    $netstandard = [Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location
    $references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Runtime.Serialization.dll', $netstandard, $native.Location, $library.Location)
    foreach ($name in @('TaleWorlds.Core.dll', 'TaleWorlds.SaveSystem.dll', 'TaleWorlds.ObjectSystem.dll', 'TaleWorlds.Localization.dll', 'TaleWorlds.DotNet.dll', 'Newtonsoft.Json.dll')) {
        $references += Join-Path $gameBin $name
    }
    Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using Newtonsoft.Json.Linq;

public static class IsolatedConversationClockProbe {
    static BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly native = typeof(Campaign).Assembly;
    static Type trackerType = native.GetType("TaleWorlds.CampaignSystem.MapTimeTracker", true);
    static int checks, callbacks, ticked;
    static List<double> callbackTimes = new List<double>();
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static long T(string name) { return (long)typeof(CampaignTime).GetField(name, S).GetValue(null); }
    static void Set(string name, object value) { typeof(CampaignTime).GetField(name, S).SetValue(null, value); }
    static long Read(object tracker, string name) { return (long)trackerType.GetProperty(name, I).GetValue(tracker, null); }
    static object Tracker(long ticks) {
        object time = Activator.CreateInstance(typeof(CampaignTime), I, null, new object[] { ticks }, null);
        return Activator.CreateInstance(trackerType, I, null, new object[] { time }, null);
    }
    static void Calendar(int days, int weeks) {
        // Native units from the installed default model; only calendar options
        // are synthetic. No Game, engine, save manager or real campaign exists.
        var model = new DefaultCampaignTimeModel();
        Set("SunRise", model.SunRise); Set("SunSet", model.SunSet);
        Set("MillisecondInSecond", model.MillisecondInSecond); Set("SecondsInMinute", model.SecondsInMinute);
        Set("MinutesInHour", model.MinutesInHour); Set("HoursInDay", model.HoursInDay);
        Set("DaysInWeek", days); Set("WeeksInSeason", weeks); Set("SeasonsInYear", model.SeasonsInYear);
        Set("TimeTicksPerMillisecond", model.TimeTicksPerMillisecond);
        Set("TimeTicksPerSecond", model.MillisecondInSecond * model.TimeTicksPerMillisecond);
        Set("TimeTicksPerMinute", model.SecondsInMinute * T("TimeTicksPerSecond"));
        Set("TimeTicksPerHour", model.MinutesInHour * T("TimeTicksPerMinute"));
        Set("TimeTicksPerDay", model.HoursInDay * T("TimeTicksPerHour"));
        Set("TimeTicksPerWeek", days * T("TimeTicksPerDay"));
        Set("TimeTicksPerSeason", weeks * T("TimeTicksPerWeek"));
        Set("TimeTicksPerYear", model.SeasonsInYear * T("TimeTicksPerSeason"));
    }
    static void Attach(Campaign campaign, object tracker) {
        typeof(Campaign).GetProperty("MapTimeTracker", I).SetValue(campaign, tracker, null);
    }
    static void Pulse(MethodInfo pulse, object tracker) { pulse.Invoke(null, new object[] { tracker }); }
    static bool Reject(MethodInfo pulse, object tracker) {
        try { Pulse(pulse, tracker); return false; } catch (TargetInvocationException) { return true; }
    }
    static void OnEvent(MBCampaignEvent e, params object[] args) { callbacks++; callbackTimes.Add(((CampaignTime)args[0]).ToHours); }
    public static Dictionary<string, object> Run(Assembly client) {
        Type adapter = client.GetType("ReignBeta.Dialogue.Runtime.ReignConversationClockProbe", true);
        MethodInfo pulse = adapter.GetMethod("PulseFiveMinutes", S);
        var campaign = (Campaign)FormatterServices.GetUninitializedObject(typeof(Campaign));
        var currentSetter = typeof(Campaign).GetProperty("Current", S).GetSetMethod(true);
        Check(Campaign.Current == null, "fresh process has no campaign");
        currentSetter.Invoke(null, new object[] { campaign });
        try {
            foreach (int[] calendar in new[] { new[] { 7, 3 }, new[] { 3, 2 } }) {
                Calendar(calendar[0], calendar[1]);
                foreach (string boundary in new[] { "TimeTicksPerHour", "TimeTicksPerDay", "TimeTicksPerSeason", "TimeTicksPerYear" }) {
                    long end = T(boundary), minute = T("TimeTicksPerMinute");
                    object tracker = Tracker(end - 2 * minute); Attach(campaign, tracker);
                    Pulse(pulse, tracker);
                    Check(Read(tracker, "NumTicks") == end + 3 * minute, "exact " + boundary);
                    Check(CampaignTime.DeltaTime.ToMinutes == 0, "zero delta at boundary");
                    if (boundary == "TimeTicksPerYear") Check(CampaignTime.Now.GetYear == 1 && CampaignTime.Now.GetDayOfYear == 0, "native year rollover");
                    if (boundary == "TimeTicksPerSeason") Check(CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Summer && CampaignTime.Now.GetDayOfSeason == 0, "native season rollover");
                    if (boundary == "TimeTicksPerDay") Check(CampaignTime.Now.GetHourOfDay == 0 && CampaignTime.Now.GetDayOfSeason == 1, "native midnight rollover");
                }
            }
            Calendar(7, 3);
            object hundred = Tracker(0); Attach(campaign, hundred);
            var absolute = new MBCampaignEvent(CampaignTime.Hours(1), CampaignTime.Hours(1));
            absolute.AddHandler(OnEvent);
            for (int i = 1; i <= 100; i++) {
                Pulse(pulse, hundred);
                Check(CampaignTime.Now.ToMinutes == 5 * i, "each pulse exactly five minutes " + i);
                Check(CampaignTime.DeltaTime.ToMinutes == 0, "no persistent delta " + i);
            }
            Check(CampaignTime.Now.ToMinutes == 500, "100 pulses total 500 minutes");
            absolute.CheckUpdate();
            Check(callbacks == 8 && callbackTimes.Distinct().Count() == 1, "absolute hourly events catch up at one current timestamp");
            absolute.CheckUpdate(); Check(callbacks == 8, "absolute events not duplicated on second update");

            Type tickerType = typeof(CampaignPeriodicEventManager).GetNestedType("PeriodicTicker`1", BindingFlags.NonPublic).MakeGenericType(typeof(int));
            object ticker = Activator.CreateInstance(tickerType, true);
            var items = new MBList<int>(); for (int i = 0; i < 24; i++) items.Add(i);
            tickerType.GetMethod("Initialize", I).Invoke(ticker, new object[] { items, new Action<int>(x => ticked++), false });
            MethodInfo tickSome = tickerType.GetMethod("PeriodicTickSome", I);
            tickSome.Invoke(ticker, new object[] { CampaignTime.DeltaTime.ToHours });
            Check(ticked == 0 && (double)tickerType.GetProperty("TickDebt", I).GetValue(ticker, null) == 0, "raw clock delta loses native entity debt");
            tickSome.Invoke(ticker, new object[] { 5d / 60d });
            Check(ticked > 0, "positive native ticker control executes callbacks");

            foreach (string field in new[] { "_numTicks", "_deltaTimeInTicks" })
                Check(trackerType.GetField(field, I).GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"), "native save annotation " + field);
            object restored = Tracker(Read(hundred, "NumTicks"));
            Attach(campaign, restored);
            Check(CampaignTime.Now.ToMinutes == 500 && CampaignTime.DeltaTime.ToMinutes == 0, "detached tracker field roundtrip only");
            Attach(campaign, Tracker(0)); Check(CampaignTime.Now.ToMinutes == 0, "earlier detached state restores native clock");
            Check(Reject(pulse, new object()), "foreign tracker rejected");
            object overflow = Tracker(long.MaxValue - 1);
            Check(Reject(pulse, overflow) && Read(overflow, "NumTicks") == long.MaxValue - 1, "overflow rejected before mutation");
            object badUnits = Tracker(0); Set("TimeTicksPerSecond", 1L);
            Check(Reject(pulse, badUnits) && Read(badUnits, "NumTicks") == 0, "uninitialized or changed units rejected");
            Calendar(7, 3);
            Type sessionType = client.GetType("ReignBeta.Dialogue.Runtime.ReignConversationClockProbeSession", true);
            MethodInfo run = sessionType.GetMethod("Run", I);
            MethodInfo observe = sessionType.GetMethod("ObserveLater", S);
            MethodInfo lease = sessionType.GetMethod("LeaseIsCurrent", S);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Check((bool)lease.Invoke(null, new object[] { now.AddSeconds(45).ToString("o"), now }), "current command lease accepted");
            foreach (string expires in new[] { "", "invalid", now.AddSeconds(-1).ToString("o"), now.AddMinutes(2).ToString("o") })
                Check(!(bool)lease.Invoke(null, new object[] { expires, now }), "invalid stale or overlong lease rejected");
            Func<JObject> after = () => new JObject { { "ticks", 3000000 } };
            int mutations = 0;
            Func<JObject> mutation = () => { mutations++; return new JObject { { "advancedMinutes", 5 } }; };
            object session = Activator.CreateInstance(sessionType, true);
            Func<string, string, Func<JObject>, JObject> call = (profile, id, apply) => (JObject)run.Invoke(session,
                new object[] { profile, id, new JObject { { "ticks", 0 } }, apply, after });
            JObject receipt = call("pulse", "one", mutation);
            Check(mutations == 1 && receipt.Value<bool>("requiresCheckpointRestore"), "one pulse latches recovery");
            JObject duplicate = call("pulse", "one", mutation);
            Check(mutations == 1 && duplicate.Value<bool>("duplicate"), "duplicate does not mutate");
            bool differentRejected = false;
            try { call("pulse", "two", mutation); } catch (TargetInvocationException) { differentRejected = true; }
            Check(differentRejected && mutations == 1, "different id cannot bypass attempt latch");
            Check(call("observe", "observation", mutation).Value<bool>("requiresCheckpointRestore") && mutations == 1, "observe preserves restore requirement");
            foreach (string reason in new[] { "focus changed", "campaign swapped", "save started" }) {
                JObject observed = (JObject)receipt.DeepClone();
                Func<JObject> fail = () => { throw new InvalidOperationException(reason); };
                Check(!(bool)observe.Invoke(null, new object[] { observed, fail }), "late observation fails " + reason);
                Check(observed["pulse"] != null && observed.Value<bool>("requiresCheckpointRestore")
                    && !observed.Value<bool>("timerAccountingQualified") && !observed.Value<bool>("inSceneAcceptance")
                    && observed.Value<string>("schema") == "reign-conversation-clock-probe-v1"
                    && observed.Value<string>("observationFailure") == reason, "late failure retains mutation evidence " + reason);
            }
            session = Activator.CreateInstance(sessionType, true);
            Func<JObject> failedMutation = () => { mutations++; throw new InvalidOperationException("partial pulse failure"); };
            JObject failed = call("pulse", "failed", failedMutation);
            Check(failed.Value<bool>("requiresCheckpointRestore") && failed["advancedMinutes"].Type == JTokenType.Null, "partial mutation is unknown not zero");
            JObject failedRetry = call("pulse", "failed", failedMutation);
            Check(mutations == 2 && failedRetry.Value<bool>("duplicate") && failedRetry["failure"] != null, "failed retry never mutates again");
            Check(!failedRetry.Value<bool>("nativeSaveInterception"), "native save limitation explicit");
            session = Activator.CreateInstance(sessionType, true);
            Func<JObject> failedCapture = () => { throw new InvalidOperationException("capture failed after pulse"); };
            JObject captureFailure = (JObject)run.Invoke(session,
                new object[] { "pulse", "capture-failure", new JObject(), mutation, failedCapture });
            Check(captureFailure["pulse"] != null && captureFailure["failure"] != null
                && captureFailure.Value<bool>("requiresCheckpointRestore") && captureFailure["advancedMinutes"].Type == JTokenType.Null,
                "post-pulse capture failure retains receipt and unknown advancement");
            return new Dictionary<string, object> {
                {"ok",true}, {"checks",checks}, {"hundredPulseMinutes",500}, {"tickerDebtLossReproduced",true},
                {"absoluteCatchUpCallbacks",callbacks}, {"absoluteCallbackDistinctTimes",callbackTimes.Distinct().Count()},
                {"nativeModuleMvid",native.ManifestModule.ModuleVersionId.ToString()},
                {"evidenceKind","OFFLINE SYNTHETIC - NOT IN-SCENE ACCEPTANCE"}, {"nativeAcceptance",false},
                {"timerAccountingQualified",false}, {"nativeSaveRoundtrip",false}, {"productionEnabled",false}
            };
        } finally { currentSetter.Invoke(null, new object[] { null }); }
    }
}
'@
    $result = [IsolatedConversationClockProbe]::Run($client)
} catch {
    $result['failure'] = $_.Exception.ToString()
    if ($_.Exception.InnerException) { $result['failure'] += ' ' + $_.Exception.InnerException.ToString() }
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if (!$result.ok) { throw $result.failure }
