param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$BannerlordPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap { [Console]::Error.WriteLine($_.Exception.ToString() + [Environment]::NewLine + $_.ScriptStackTrace); exit 1 }
if ($env:REIGN_VALIDATION_MODE -ne '1') { throw 'This probe requires canonical isolated validation.' }

$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$script:assemblyRoots = @([IO.Path]::GetDirectoryName($clientPath), (Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'))
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:assemblyRoots) {
        $candidate = Join-Path $root $name
        if ([IO.File]::Exists($candidate)) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$contracts = [Reflection.Assembly]::LoadFrom((Join-Path $script:assemblyRoots[0] 'Reign.Core.Contracts.dll'))
$json = [Reflection.Assembly]::Load('Newtonsoft.Json')
$facade = [Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51')
$client = [Reflection.Assembly]::LoadFrom($clientPath)

$source = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;

public sealed class GovernmentVerifiedForkProbe
{
    private const string Campaign = "construction_probe";
    private const string OriginalTimeline = "source_timeline";
    private const string Child = "verified_fork_11111111111111111111111111111111";
    private const string Grandchild = "verified_fork_22222222222222222222222222222222";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance;
    private readonly Type governmentType;
    private readonly Type recordType;
    private readonly Type verifiedLoadType;
    private readonly Type forkType;
    private readonly object government;
    private readonly IList rows;
    private readonly List<string> passed = new List<string>();

    public GovernmentVerifiedForkProbe(Assembly client)
    {
        governmentType = client.GetType("ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior", true);
        recordType = client.GetType("ReignBeta.Government.ReignGovernmentResolutionRecord", true);
        verifiedLoadType = client.GetType("ReignBeta.Persistence.Integration.ReignSaveSyncCoordinator+VerifiedLoad", true);
        forkType = client.GetType("ReignBeta.WorldSimulation.Integration.ReignWorldHistoryVerifiedFork", true);
        government = Activator.CreateInstance(governmentType);
        rows = (IList)governmentType.GetField("_resolutions", Members).GetValue(government);
    }

    public string[] Run()
    {
        object row = MakeRow();
        JObject original = Offer(row);
        Capture(row);
        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        JObject adopted = Offer(row);
        Check(Revision(row) == 2 && adopted["observation"] == null
            && JToken.DeepEquals(adopted["acceptedTerms"], original["acceptedTerms"])
            && (string)adopted["verifiedForks"][0]["childTimelineId"] == Child
            && (string)adopted["verifiedForks"][0]["savePointId"] == "point_1"
            && (string)adopted["verifiedForks"][0]["sourceHeadEventId"] == "event_1",
            "pending_same_terms_and_no_observation");

        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        Check(Revision(row) == 2 && JToken.DeepEquals(Offer(row), adopted), "no_second_adoption");

        Capture(row);
        JObject reopen = Reply(Child, Child);
        object unchanged = ReadFork(reopen, Child);
        Adopt(unchanged);
        Check(unchanged == null && Revision(row) == 2 && JToken.DeepEquals(Offer(row), adopted), "unchanged_child");

        Capture(row);
        Adopt(ReadFork(Reply(Child, Grandchild, "point_2", "event_2", 11, 106.0), Child, "point_2", "event_2", 11));
        Check(Revision(row) == 3 && ((JArray)Offer(row)["verifiedForks"]).Count == 2
            && (string)Offer(row)["verifiedForks"][1]["sourceTimelineId"] == Child
            && (string)Offer(row)["verifiedForks"][1]["sourceHeadEventId"] == "event_2"
            && JToken.DeepEquals(Offer(row)["acceptedTerms"], original["acceptedTerms"]), "changed_child_direct_anchor");

        row = MakeRow(103.5);
        JToken observed = Offer(row)["observation"].DeepClone();
        Capture(row);
        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        Capture(row);
        Check(Revision(row) == 2 && JToken.DeepEquals(Offer(row)["observation"], observed)
            && (bool)Call(government, governmentType, "HasLoadedConstructionForTimeline", Campaign, Child)
            && !(bool)Call(government, governmentType, "HasLoadedConstructionForTimeline", Campaign, OriginalTimeline),
            "pre_checkpoint_observation_retained_and_readable_on_child");

        row = MakeRow(105.0);
        Capture(row);
        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        Check(Revision(row) == 1 && Offer(row)["verifiedForks"] == null, "post_checkpoint_observation_excluded");

        row = MakeRow();
        Capture(row);
        object replacement = MakeRow();
        rows[0] = replacement;
        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        Check(Revision(replacement) == 1 && Revision(row) == 1, "replacement_row_rejected");

        row = MakeRow();
        Capture(row);
        Set(row, "Revision", 2L);
        Adopt(ReadFork(Reply(OriginalTimeline, Child), OriginalTimeline));
        Check(Revision(row) == 2 && Offer(row)["verifiedForks"] == null, "changed_revision_rejected");

        JObject wrongLoad = Reply(OriginalTimeline, Child);
        wrongLoad["verifiedFork"]["loadSessionId"] = "superseded_session";
        Check(Rejects(wrongLoad), "superseded_load_rejected");

        JObject wrongAnchor = Reply(OriginalTimeline, Child);
        wrongAnchor["verifiedFork"]["sourceHeadEventId"] = "unrelated_event";
        Check(Rejects(wrongAnchor), "different_source_anchor_rejected");
        return passed.ToArray();
    }

    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed construction fork case: " + name);
        passed.Add(name);
    }

    private object MakeRow(double? observedDay = null)
    {
        JObject terms = new JObject {
            { "resolutionId", "resolution_1" }, { "campaignId", Campaign }, { "timelineId", OriginalTimeline },
            { "kingdomId", "kingdom_1" }, { "settlementId", "town_1" }, { "previousProjectId", "waterworks" },
            { "projectId", "barracks" }, { "levelAtAcceptance", 2 }, { "levelCapAtAcceptance", 3 },
            { "acceptedDay", 100.5 }, { "dueDay", 114.5 }
        };
        JObject offer = new JObject { { "schemaVersion", 1 }, { "settlementId", "town_1" },
            { "projectId", "barracks" }, { "acceptedTerms", terms } };
        if (observedDay.HasValue)
            offer["observation"] = new JObject { { "schemaVersion", 1 }, { "terms", terms.DeepClone() },
                { "transition", "level_completed" }, { "observedDay", observedDay.Value }, { "observedLevel", 3 } };
        object row = Activator.CreateInstance(recordType);
        Set(row, "ResolutionId", "resolution_1");
        Set(row, "KingdomStringId", "kingdom_1");
        Set(row, "TemplateId", "R083");
        Set(row, "TargetSettlementStringId", "town_1");
        Set(row, "SelectedRoute", 0);
        Set(row, "RouteActionValue", (int)ReignGovernmentResolutionAction.ChangeConstruction);
        Set(row, "AcceptedDay", 100.5f);
        Set(row, "DueDay", 114.5f);
        Set(row, "Revision", 1L);
        Set(row, "Status", "active");
        Set(row, "EvidenceJson", new JObject { { "construction", offer } }.ToString());
        return row;
    }

    private static JObject Reply(string sourceTimeline, string openedTimeline, string point = "point_1",
        string head = "event_1", long sequence = 10, double day = 104.0)
    {
        bool changed = sourceTimeline != openedTimeline;
        JObject reply = new JObject { { "ok", true }, { "historyMissing", false }, { "campaignId", Campaign },
            { "sharedActivityGeneration", "generation_1" }, { "savedSequence", sequence },
            { "timelineId", openedTimeline }, { "branched", changed }, { "parentTimelineId", sourceTimeline } };
        if (changed)
            reply["verifiedFork"] = new JObject { { "schemaVersion", 1 }, { "campaignId", Campaign },
                { "savePointId", point }, { "loadSessionId", "session_1" }, { "restoreGeneration", "generation_1" },
                { "sourceTimelineId", sourceTimeline }, { "sourceSequence", sequence }, { "sourceHeadEventId", head },
                { "childTimelineId", openedTimeline }, { "checkpointDay", day }, { "nativeMembershipVerified", false } };
        return reply;
    }

    private object ReadFork(JObject reply, string sourceTimeline, string point = "point_1", string head = "event_1", long sequence = 10)
    {
        object load = Activator.CreateInstance(verifiedLoadType, Members, null,
            new object[] { 1, Campaign, point, "session_1", "generation_1", "http://fixture.invalid", "rolled_back" }, null);
        return Call(null, forkType, "ReadOpenReply", reply, load, sourceTimeline, sequence, head);
    }

    private bool Rejects(JObject reply)
    {
        try { ReadFork(reply, OriginalTimeline); }
        catch (TargetInvocationException error)
        {
            if (error.InnerException is InvalidOperationException) return true;
            throw;
        }
        return false;
    }

    private void Capture(object row)
    {
        rows.Clear();
        rows.Add(row);
        Call(government, governmentType, "CaptureLoadedConstructionResolutions");
    }

    private void Adopt(object fork) { Call(government, governmentType, "AdoptLoadedConstructionFork", fork); }
    private long Revision(object row) { return (long)recordType.GetField("Revision").GetValue(row); }
    private JObject Offer(object row) { return (JObject)JObject.Parse((string)recordType.GetField("EvidenceJson").GetValue(row))["construction"]; }
    private void Set(object row, string field, object value) { recordType.GetField(field).SetValue(row, value); }
    private static object Call(object owner, Type type, string method, params object[] args)
    {
        return type.GetMethod(method, Members).Invoke(owner, args);
    }
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', $facade.Location, $contracts.Location, $json.Location)
$probe = [GovernmentVerifiedForkProbe]::new($client)
$passed = $probe.Run()
[IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'report.json'), ([ordered]@{
    schema = 'reign-government-verified-fork-client-probe-v1'
    clientAssembly = $clientPath
    clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash.ToLowerInvariant()
    passed = @($passed)
    nativeCampaign = $false
    providers = $false
} | ConvertTo-Json -Depth 5))
