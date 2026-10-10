param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$BannerlordPath,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Run only as a canonical test child. No game, campaigns, providers or services.
$probeRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$initialProgress = [IO.Path]::Combine($probeRoot, 'progress.json')
[IO.Directory]::CreateDirectory($probeRoot) | Out-Null
[IO.File]::WriteAllText($initialProgress, '{"stage":"script-entered"}')
$working = Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
function Record-ProbeStage([string]$Stage) {
    [IO.File]::WriteAllText((Join-Path $probeRoot 'progress.json'),
        ([ordered]@{ stage = $Stage; utc = [DateTime]::UtcNow.ToString('o'); processId = $PID } | ConvertTo-Json -Compress))
}
Record-ProbeStage 'initializing'
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$workshop = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
$script:probeAssemblyRoots = @(
    [IO.Path]::GetDirectoryName($clientPath), $gameBin,
    (Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Native\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.UIExtenderEx\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859222409\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859238197\bin\Win64_Shipping_Client')
)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $script:probeAssemblyRoots) {
        $path = Join-Path $directory $file
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Require-Exception([scriptblock]$Action, [type]$Expected) {
    try { & $Action | Out-Null }
    catch {
        $exception = $_.Exception
        while ($exception.InnerException) { $exception = $exception.InnerException }
        Require ($exception.GetType() -eq $Expected) ('Unexpected exception: ' + $exception)
        return
    }
    throw ('Expected exception was not thrown: ' + $Expected.FullName)
}
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
Record-ProbeStage 'loading-assemblies'
$nativeInput = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.InputSystem.dll'))
$sandbox = [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.GauntletUI.dll'))
$client = [Reflection.Assembly]::LoadFrom($clientPath)
Record-ProbeStage 'resolving-native-types'
$inputType = $nativeInput.GetType('TaleWorlds.InputSystem.InputContext', $true)
$keyType = $nativeInput.GetType('TaleWorlds.InputSystem.GameKey', $true)
$keysField = $inputType.GetField('_registeredGameKeys', $flags)
$downKeysField = $inputType.GetField('_downInputKeys', $flags)
$nativeRelease = $inputType.GetMethod('IsGameKeyReleased', [type[]]@([int]))
$baseType = $sandbox.GetType('SandBox.GauntletUI.Map.GauntletMapBarGlobalLayer', $true)
$baseMethod = $baseType.GetMethod('HandlePanelSwitchingInput', $flags)
$baseInstance = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($baseType)
Record-ProbeStage 'reproducing-native-exception'
$empty = [Activator]::CreateInstance($inputType)
Require-Exception { $nativeRelease.Invoke($empty, [object[]]@(37)) } ([ArgumentOutOfRangeException])
Require-Exception { $baseMethod.Invoke($baseInstance, [object[]]@($empty)) } ([ArgumentOutOfRangeException])
$navalPath = Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.GauntletUI.dll'
$navalMethod = $null
$navalInstance = $null
if ([IO.File]::Exists($navalPath)) {
    $naval = [Reflection.Assembly]::LoadFrom($navalPath)
    $navalType = $naval.GetType('NavalDLC.GauntletUI.Map.GauntletNavalMapBarGlobalLayer', $true)
    $navalMethod = $navalType.GetMethod('HandlePanelSwitchingInput', $flags)
    $navalInstance = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($navalType)
    Require-Exception { $navalMethod.Invoke($navalInstance, [object[]]@($empty)) } ([ArgumentOutOfRangeException])
}
$patchType = $client.GetType('ReignBeta.Integration.ReignMapBarInputSafetyPatch', $true)
Record-ProbeStage 'applying-local-map-patch'
$apply = $patchType.GetMethod('Apply', $flags)
$unapply = $patchType.GetMethod('Unapply', $flags)
$safeRelease = $patchType.GetMethod('IsRegisteredGameKeyReleased', $flags)
$apply.Invoke($null, @()) | Out-Null
$apply.Invoke($null, @()) | Out-Null
$owner = 'com.bannerlordreign.reignbeta.mapbar.input_safety'
Require (@([HarmonyLib.Harmony]::GetPatchInfo($baseMethod).Transpilers | Where-Object owner -eq $owner).Count -eq 1) 'Base patch missing or registered twice.'
if ($navalMethod) {
    Require (@([HarmonyLib.Harmony]::GetPatchInfo($navalMethod).Transpilers | Where-Object owner -eq $owner).Count -eq 1) 'Naval patch missing or registered twice.'
}
$cases = 0
Record-ProbeStage 'checking-registered-keys'
try {
    foreach ($count in @(0, 1, 37, 38, 44, 45, 46, 70)) {
        foreach ($populated in @($false, $true)) {
            $input = [Activator]::CreateInstance($inputType)
            $keys = $keysField.GetValue($input)
            for ($i = 0; $i -lt $count; $i++) {
                $key = if ($populated) { [Runtime.Serialization.FormatterServices]::GetUninitializedObject($keyType) } else { $null }
                $keys.Add($key)
            }
            Require (!$baseMethod.Invoke($baseInstance, [object[]]@($input))) 'Base handler consumed an unpressed/missing key.'
            if ($navalMethod) { Require (!$navalMethod.Invoke($navalInstance, [object[]]@($input))) 'Naval handler consumed an unpressed/missing key.' }
            foreach ($id in @(-1, 0, 37, 38, 40, 41, 42, 43, 45, 70, [int]::MaxValue)) {
                Require (!$safeRelease.Invoke($null, [object[]]@($input, $id))) 'Guard consumed an unpressed/missing key.'
                $cases++
            }
            Require ([object]::ReferenceEquals($keys, $keysField.GetValue($input)) -and $keys.Count -eq $count) 'Guard mutated the key registration table.'
        }
    }
    Require (!$safeRelease.Invoke($null, [object[]]@($null, 37))) 'Null context was not rejected.'
    # A registered key must still call native release tracking, and unrelated
    # errors must remain visible rather than becoming a blanket exception catch.
    $valid = [Activator]::CreateInstance($inputType)
    $keysField.GetValue($valid).Add([Runtime.Serialization.FormatterServices]::GetUninitializedObject($keyType))
    Require ($nativeRelease.Invoke($valid, [object[]]@(0)) -eq $safeRelease.Invoke($null, [object[]]@($valid, 0))) 'Native release result changed.'
    $downKeysField.SetValue($valid, $null)
    Require-Exception { $safeRelease.Invoke($null, [object[]]@($valid, 0)) } ([NullReferenceException])

    $behavior = $client.GetType('ReignBeta.Campaign.ReignSocialEventsCampaignBehavior', $true)
    Record-ProbeStage 'checking-map-screen-types'
    $mapCheck = $behavior.GetMethod('IsCampaignMapScreen', $flags)
    $mapScreen = [type]::GetType('SandBox.View.Map.MapScreen, SandBox.View', $true)
    $inventory = $sandbox.GetType('SandBox.GauntletUI.GauntletInventoryScreen', $true)
    $social = $client.GetType('ReignBeta.UI.ReignSocialEventScreen', $true)
    Require ($mapCheck.Invoke($null, [object[]]@($mapScreen))) 'Campaign map type rejected.'
    foreach ($screen in @($inventory, $social, $null)) {
        Require (!$mapCheck.Invoke($null, [object[]]@($screen))) 'A non-map screen was accepted for wilderness presentation.'
    }

    $recordType = $client.GetType('ReignBeta.Events.SocialEventRecord', $true)
    Record-ProbeStage 'checking-pending-record-save-fields'
    $record = [Activator]::CreateInstance($recordType)
    $record.EventId = 'reign_wilderness_plain_probe'
    $record.IsGeneratedWildernessEvent = $true
    $record.StatusValue = 0
    $record.AnnouncementSent = $false
    $record.ExpiresDay = 10
    $record.GeneratedOpeningText = 'Retain this exact opening while the player stays in town.'
    $record.GeneratedInitiatorHeroStringId = 'probe_initiator';
    Require ($record.IsDeferredWildernessEvent -and $record.IsOpen(9999) -and !$record.HasTimedOut(9999)) 'Pending wilderness event expired in town.'
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'Newtonsoft.Json.dll')) | Out-Null
    $json = [Newtonsoft.Json.JsonConvert]::SerializeObject($record)
    $restored = [Newtonsoft.Json.JsonConvert]::DeserializeObject($json, $recordType)
    Require ($restored.IsDeferredWildernessEvent -and $restored.IsOpen(9999) -and !$restored.HasTimedOut(9999)) 'Managed pending-record round trip lost its state.'
    Require ($restored.GeneratedOpeningText -eq $record.GeneratedOpeningText) 'Round trip changed generated text.'
    Require ($restored.GeneratedInitiatorHeroStringId -eq $record.GeneratedInitiatorHeroStringId) 'Round trip lost the wilderness initiator.'
    $saved = @($recordType.GetFields() | Where-Object { @($_.GetCustomAttributes($false) | Where-Object { $_.GetType().FullName -eq 'TaleWorlds.SaveSystem.SaveableFieldAttribute' }).Count -gt 0 })
    Require ($saved.Count -eq 28) 'Unexpected native save field layout; initiator must be the additive field 28.'
    foreach ($name in @('StatusValue', 'AnnouncementSent', 'IsGeneratedWildernessEvent', 'EventId', 'GeneratedOpeningText', 'GeneratedInitiatorHeroStringId')) {
        Require ($saved.Name -contains $name) ('Pending state is missing a native save field: ' + $name)
    }
    foreach ($state in @(1, 2, 3)) {
        $restored.StatusValue = $state
        Require (!$restored.IsDeferredWildernessEvent) 'An attended/resolved event would be reopened.'
    }
    $restored.StatusValue = 0
    $restored.EventId = 'reign_event_town_probe'
    $restored.IsGeneratedWildernessEvent = $false
    Require (!$restored.IsOpen(9999) -and $restored.HasTimedOut(9999)) 'Ordinary town-event expiry changed.'
} finally {
    Record-ProbeStage 'removing-local-map-patch'
    $unapply.Invoke($null, @()) | Out-Null
}
Require-Exception { $baseMethod.Invoke($baseInstance, [object[]]@($empty)) } ([ArgumentOutOfRangeException])
if ($navalMethod) { Require-Exception { $navalMethod.Invoke($navalInstance, [object[]]@($empty)) } ([ArgumentOutOfRangeException]) }
Require ([HarmonyLib.Harmony]::GetPatchInfo($nativeRelease) -eq $null) 'Global InputContext release method was patched.'
Record-ProbeStage 'writing-report'
[ordered]@{
    schema = 'reign-mapbar-input-probe-v1'; ok = $true
    nativeExceptionReproduced = $true; baseMapBarPassed = $true
    navalMapBarPassed = ($null -ne $navalMethod); guardedLookupCases = $cases
    pendingRoundTripPassed = $true; nativeSaveFieldContractPassed = $true
    clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
    inputAssemblySha256 = (Get-FileHash -LiteralPath $nativeInput.Location -Algorithm SHA256).Hash
    sandboxAssemblySha256 = (Get-FileHash -LiteralPath $sandbox.Location -Algorithm SHA256).Hash
    limitations = 'Managed assembly behavior and record serialization only; no rendered native UI or real Bannerlord save/load acceptance.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $probeRoot 'report.json') -Encoding UTF8
Write-Output ('Map-bar input probe passed; ' + $cases + ' guarded lookups; evidence: ' + $probeRoot)
