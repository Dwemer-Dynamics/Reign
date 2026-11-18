param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [string]$BannerlordPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap { [Console]::Error.WriteLine($_.Exception.ToString() + [Environment]::NewLine + $_.ScriptStackTrace); exit 1 }
if ($env:REIGN_VALIDATION_MODE -ne '1') { throw 'This probe requires canonical isolated validation.' }
$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$script:assemblyRoots = @([IO.Path]::GetDirectoryName($clientPath))
if ($BannerlordPath) { $script:assemblyRoots += Join-Path $BannerlordPath 'bin\Win64_Shipping_Client' }
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
$contractsPath = Join-Path ([IO.Path]::GetDirectoryName($clientPath)) 'Reign.Core.Contracts.dll'
[Reflection.Assembly]::LoadFrom($contractsPath) | Out-Null
# Newtonsoft is a nonprivate licensed-game dependency, not a client output.
# Use the same allowlisted assembly resolver as the existing portrait probe.
$jsonAssembly = [Reflection.Assembly]::Load('Newtonsoft.Json')
$jsonPath = $jsonAssembly.Location
$client = [Reflection.Assembly]::LoadFrom($clientPath)
$source = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
public static class TavernVisitNativeProbe
{
    public static int Run(Assembly client)
    {
        Type type = client.GetType("ReignBeta.Characters.Actions.ReignTavernHouseAction", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Func<string, object[], object> call = (name, args) => type.GetMethod(name, flags).Invoke(null, args);
        int passed = 0;
        Action<bool> check = ok => { if (!ok) throw new InvalidOperationException("Visit binding case " + (passed + 1) + " failed."); passed++; };
        var scope = new TavernHouseActionContext { CampaignId = "campaign", TimelineId = "timeline", TownId = "town", SessionId = "session", PlayerId = "player", MadamId = "madam" };
        scope.Roster.Add(new TavernHouseActionPerson { HeroId = "worker", Name = "Worker", Adult = true, Available = true });
        var a = new ActionAgreement { Id = "visit", PlayerId = "player", NpcId = "madam", ProposerId = "player", ConversationId = "conversation", State = AgreementState.Offered };
        var c = new ActionClause { ActionId = TavernHouseVisitAction.Id, ActorId = "player", RecipientId = "madam" };
        TavernHouseVisitAction.Bind(c, scope); c.Arguments["participant1"] = "worker"; c.Arguments["price1"] = "120"; c.Arguments["totalGold"] = "120"; a.Clauses.Add(c);
        a.PlayerAcceptedHash = a.TermsHash(); a.NpcAcceptedHash = a.TermsHash();
        Func<int> errors = () => ((List<string>)call("CheckPrepared", new object[] { a })).Count;
        check(errors() > 0); // No owning Tavern House screen.
        object owner = new object(), other = new object();
        call("Open", new object[] { owner, new Func<TavernHouseActionContext>(() => scope) });
        check(((List<string>)call("Check", new object[] { a })).Count == 0);
        check(errors() > 0); // Signatures without server preparation cannot pay.
        var prepared = new JObject(); prepared["schema"] = "reign-tavern-prepared-visit-v1"; prepared["agreementId"] = a.Id; prepared["termsHash"] = a.TermsHash();
        prepared["fingerprint"] = TavernHouseVisitAction.Fingerprint(a); prepared["madamId"] = "madam";
        prepared["preparationId"] = TavernHouseVisitAction.Hash(new[] { a.Id, a.TermsHash(), scope.CampaignId, scope.TimelineId, scope.SessionId });
        Action register = () => { var response = new JObject(); response["ok"] = true; response["preparedVisit"] = prepared; call("RegisterPrepared", new object[] { response }); };
        register(); check(errors() == 0);
        call("Close", new object[] { other }); check(errors() == 0); // Another host cannot clear the owner.
        scope.SessionId = "other"; check(errors() > 0); scope.SessionId = "session";
        scope.Roster[0].Available = false; check(errors() > 0); scope.Roster[0].Available = true;
        string hash = a.NpcAcceptedHash; a.NpcAcceptedHash = ""; check(errors() > 0); a.NpcAcceptedHash = hash;
        prepared["termsHash"] = "changed"; register(); check(errors() > 0);
        prepared["termsHash"] = a.TermsHash(); register(); check(errors() == 0);
        call("Close", new object[] { owner }); check(errors() > 0);
        bool rejected = false; try { register(); } catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidOperationException; }
        check(rejected);
        return passed;
    }
}
'@
# Resolve the installed Framework runtime facade by assembly identity. Its GAC
# location need not be beneath RuntimeEnvironment.GetRuntimeDirectory().
$facadeAssembly = [Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51')
$facade = $facadeAssembly.Location
if (-not [IO.File]::Exists($facade)) { throw 'The native Framework runtime must provide its netstandard facade for this probe.' }
Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', $facade, $contractsPath, $jsonPath)
$passed = [TavernVisitNativeProbe]::Run($client)
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory)) | Out-Null
[IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'report.json'), (@{
    schema = 'reign-tavern-visit-native-probe-v1'; ok = $true; passed = $passed; clientAssembly = $clientPath;
    clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash.ToLowerInvariant();
    referenceFacade = $facade; referenceFacadeSha256 = (Get-FileHash -LiteralPath $facade -Algorithm SHA256).Hash.ToLowerInvariant();
    nativeCampaign = $false; providers = $false; paymentInvocations = 0
} | ConvertTo-Json -Depth 5))
