param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$BannerlordPath,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$probeRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$working = Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:assemblyRoots = @($gameBin, (Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Native\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'), [IO.Path]::GetDirectoryName($clientPath))
foreach ($module in @('Bannerlord.MBOptionScreen', 'Bannerlord.ButterLib', 'Bannerlord.UIExtenderEx')) {
    $script:assemblyRoots += Join-Path $BannerlordPath ('Modules\' + $module + '\bin\Win64_Shipping_Client')
}
$workshopRoot = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
foreach ($workshopId in @('2859238197', '2859188632', '2859222409')) {
    $script:assemblyRoots += Join-Path $workshopRoot ($workshopId + '\bin\Win64_Shipping_Client')
}
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:assemblyRoots) {
        $path = Join-Path $root $name
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$prepared = [Collections.Generic.List[string]]::new()
$hooks = [Collections.Generic.List[string]]::new()
$failure = $null
try {
    $sandbox = [Reflection.Assembly]::LoadFrom((Join-Path $script:assemblyRoots[1] 'SandBox.dll'))
    $campaign = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public,DeclaredOnly'
    $prison = $sandbox.GetType('SandBox.CampaignBehaviors.PrisonBreakCampaignBehavior', $true)
    foreach ($name in @('settlement_prison_break_success_continue_on_consequence',
        'prison_break_end_with_success_on_consequence', 'prison_break_end_with_success_companion')) {
        $method = $prison.GetMethod($name, $flags)
        if ($null -eq $method -or $method.ReturnType -ne [void]) { throw "Native prison hook unavailable: $name" }
        $expectedParameters = if ($name -eq 'settlement_prison_break_success_continue_on_consequence') { 1 } else { 0 }
        if ($method.GetParameters().Length -ne $expectedParameters) { throw "Native prison signature changed: $name" }
        $hooks.Add($name)
    }
    $heroType = $campaign.GetType('TaleWorlds.CampaignSystem.Hero', $true)
    if ($prison.GetField('_prisonerHero', $flags).FieldType -ne $heroType) { throw 'Native rescued-hero field changed.' }
    $side = $campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.MapEventSide', $true)
    $join = $side.GetMethod('AddPartyInternal', $flags)
    if ($null -eq $join -or $join.GetParameters().Length -ne 1 -or
        $join.GetParameters()[0].ParameterType.FullName -ne 'TaleWorlds.CampaignSystem.Party.PartyBase') {
        throw 'Native pre-join battle hook changed.'
    }
    $hooks.Add('MapEventSide.AddPartyInternal')
    $client = [Reflection.Assembly]::LoadFrom($clientPath)
    foreach ($typeName in @('ReignBeta.Campaign.ReignRescueConversationBehavior',
        'ReignBeta.Campaign.ReignPlayerApproachCampaignBehavior', 'ReignBeta.Integration.ReignServerClient',
        'ReignBeta.UI.ReignPlayerApproachPopup')) {
        $type = $client.GetType($typeName, $true)
        $types = @($type)
        if ($typeName -ne 'ReignBeta.Integration.ReignServerClient') {
            # Resolve native calls inside compiler-generated async/closure bodies too.
            $types += @($type.GetNestedTypes($flags) | Where-Object { !$_.ContainsGenericParameters })
        }
        $members = @($types | ForEach-Object { $_.GetMethods($flags) } | Where-Object { !$_.ContainsGenericParameters -and !$_.IsAbstract })
        # ServerClient is a shared partial type. Bound this proof to approach-owned calls.
        if ($typeName -eq 'ReignBeta.Integration.ReignServerClient') {
            $members = @($members | Where-Object { $_.Name -match 'Approach|Attention' })
        }
        foreach ($method in $members) {
            [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)
            $prepared.Add($method.DeclaringType.FullName + '.' + $method.Name)
        }
    }
    if ($prepared.Count -lt 20) { throw 'Insufficient approach method coverage.' }
} catch { $failure = $_.Exception.ToString() }
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
$report = [ordered]@{
    schema = 'reign-player-approach-native-probe-v1'; ok = ($null -eq $failure)
    clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash.ToLowerInvariant()
    nativeHooks = @($hooks); preparedMethods = @($prepared); failure = $failure
    isolation = 'Managed binding/JIT only; no method invocation, Harmony install, game, provider, listener, campaign, save or deployment.'
}
[IO.File]::WriteAllText((Join-Path $probeRoot 'report.json'), ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
if ($failure) { [Console]::Error.WriteLine($failure); exit 1 }
[Console]::WriteLine('Approach native binding checks passed: ' + $prepared.Count + ' methods, ' + $hooks.Count + ' hooks.')
