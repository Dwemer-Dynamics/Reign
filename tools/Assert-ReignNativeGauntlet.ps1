[CmdletBinding()]
param(
    [string]$LogPath = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\logs\action-gauntlet.jsonl',
    [datetime]$SinceUtc = [datetime]::MinValue,
    [int]$ExpectedActionCount = 75
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$verificationRoot = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\server\app\data\tests\verification'

function Get-Value {
    param([object]$Object, [string]$Name, [object]$Default = $null)
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function To-Array {
    param([object]$Value)
    if ($null -eq $Value) { return @() }
    return @($Value)
}

function Has-Effect {
    param([object]$Data, [string]$EffectType = '', [string]$Detail = '')
    foreach ($effect in @(To-Array (Get-Value $Data 'effects' $null))) {
        $type = [string](Get-Value $effect 'effectType' '')
        $actualDetail = [string](Get-Value $effect 'detail' '')
        if (($EffectType.Length -eq 0 -or $type -eq $EffectType) -and ($Detail.Length -eq 0 -or $actualDetail -eq $Detail)) { return $true }
    }
    return $false
}

function Has-Change {
    param([object]$Data, [string]$ChangeType)
    foreach ($change in @(To-Array (Get-Value $Data 'changedEntities' $null))) {
        if ([string](Get-Value $change 'changeType' '') -eq $ChangeType) { return $true }
    }
    return $false
}

function Add-Failure {
    param([System.Collections.Generic.List[string]]$Failures, [bool]$Condition, [string]$Message)
    if (-not $Condition) { $Failures.Add($Message) }
}

function Test-ExactNativePostcondition {
    param([string]$Suite, [string]$Name, [object]$Data)
    $failures = [System.Collections.Generic.List[string]]::new()
    $code = [string](Get-Value $Data 'resultCode' '')
    $success = [bool](Get-Value $Data 'resultSuccess' $false)

    if ($Suite -eq 'Failure/Validation') {
        Add-Failure $failures (-not $success) 'Expected validation rejection was reported as a successful native action.'
        return @($failures)
    }

    Add-Failure $failures $success 'Native action resultSuccess was false.'
    Add-Failure $failures ($code -notmatch '(?i)pending_.+_hook|_unavailable$|no_mechanical_change|completed_no_effect') ("Non-mechanical or pending result code: $code")

    $movement = @{
        FollowOnMap = 'order=follow_on_map'
        StopFollowing = 'order=hold'
        GoToSettlement = 'order=go_to_settlement'
        PatrolAroundSettlement = 'order=patrol'
        WaitNearSettlement = 'order=wait_near'
        RaidVillage = 'order=raid_village'
        BesiegeSettlement = 'order=besiege'
        AttackParty = 'order=attack_party'
        AttackPlayerParty = 'order=attack_player_party'
        LeavePlayerAlone = 'order=leave_player_alone'
        AttackSettlement = 'order=raid'
    }
    if ($movement.ContainsKey($Name)) {
        Add-Failure $failures (Has-Effect $Data 'movement_order' $movement[$Name]) ("Missing exact movement effect $($movement[$Name]).")
        Add-Failure $failures (Has-Change $Data 'movement_order_changed') 'Native party movement order did not change.'
    }

    switch ($Name) {
        'FollowInScene' { Add-Failure $failures (Has-Change $Data 'mission_follow_started') 'Mission follow behavior did not start.' }
        'ShowTheWay' { Add-Failure $failures (Has-Change $Data 'mission_guide_started') 'Mission guide behavior did not start.' }
        'SurrenderToPlayer' { Add-Failure $failures (Has-Change $Data 'party_surrendered') 'The party did not actually surrender to the player.' }
        'JoinClan' { Add-Failure $failures (Has-Change $Data 'hero_clan_changed') 'The hero did not actually join the target clan.' }
        'LeaveClan' { Add-Failure $failures (Has-Change $Data 'hero_clan_changed') 'The hero did not actually leave the clan.' }
        'DuelPlayer' { Add-Failure $failures (Has-Change $Data 'duel_mission_started') 'The duel mission did not actually start.' }
        'RecruitAndRecover' {
            Add-Failure $failures ((Has-Change $Data 'movement_order_changed') -and (Has-Effect $Data 'movement_order' 'order=recruit_and_recover')) 'Recruit-and-recover did not issue its exact native recovery movement order.'
        }
        'FormArmy' {
            Add-Failure $failures ((Has-Change $Data 'army_created') -or (Has-Effect $Data 'army_created')) 'No native army was created.'
        }
    }

    return @($failures)
}

if (-not (Test-Path -LiteralPath $LogPath)) { throw "Gauntlet log not found: $LogPath" }

$latest = @{}
foreach ($line in Get-Content -LiteralPath $LogPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $row = $line | ConvertFrom-Json } catch { continue }
    $phase = [string](Get-Value $row 'phase' '')
    if (-not $phase.StartsWith('action.', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    $timestamp = [datetime]::MinValue
    [datetime]::TryParse([string](Get-Value $row 'timestamp' ''), [ref]$timestamp) | Out-Null
    if ($timestamp.ToUniversalTime() -lt $SinceUtc.ToUniversalTime()) { continue }
    $data = Get-Value $row 'data' $null
    $suite = [string](Get-Value $data 'suite' '')
    $name = [string](Get-Value $data 'name' '')
    if ([string]::IsNullOrWhiteSpace($suite) -or [string]::IsNullOrWhiteSpace($name)) { continue }
    $latest[$suite + '/' + $name] = $row
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($key in @($latest.Keys | Sort-Object)) {
    $row = $latest[$key]
    $data = $row.data
    $failures = [System.Collections.Generic.List[string]]::new()
    if ([string](Get-Value $row 'status' '') -ne 'passed') { $failures.Add('The in-client gauntlet assertion failed: ' + [string](Get-Value $row 'summary' '')) }
    foreach ($failure in @(Test-ExactNativePostcondition ([string]$data.suite) ([string]$data.name) $data)) { $failures.Add($failure) }
    $results.Add([pscustomobject]@{
        suite = [string]$data.suite
        name = [string]$data.name
        passed = $failures.Count -eq 0
        resultCode = [string](Get-Value $data 'resultCode' '')
        outcome = [string](Get-Value $data 'outcome' '')
        failures = @($failures)
        effects = @(To-Array (Get-Value $data 'effects' $null))
        changedEntities = @(To-Array (Get-Value $data 'changedEntities' $null))
    })
}

$runId = 'native-exact-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$countCorrect = $results.Count -eq $ExpectedActionCount
$passed = $countCorrect -and @($results | Where-Object { -not $_.passed }).Count -eq 0
$report = [pscustomobject]@{
    ok = $passed
    runId = $runId
    completedUtc = [datetime]::UtcNow.ToString('o')
    logPath = $LogPath
    sinceUtc = $SinceUtc.ToUniversalTime().ToString('o')
    expectedActionCount = $ExpectedActionCount
    actionCount = $results.Count
    passedCount = @($results | Where-Object passed).Count
    failedCount = @($results | Where-Object { -not $_.passed }).Count
    countCorrect = $countCorrect
    results = $results
}
$destination = Join-Path $verificationRoot ($(if ($passed) { 'runs' } else { 'failures' }))
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$reportPath = Join-Path $destination ($runId + '.json')
$report | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $reportPath -Encoding UTF8

[pscustomobject]@{
    ok = $passed
    runId = $runId
    actionCount = $report.actionCount
    passedCount = $report.passedCount
    failedCount = $report.failedCount
    countCorrect = $countCorrect
    failedCases = @($results | Where-Object { -not $_.passed } | Select-Object suite,name,resultCode,failures)
    reportPath = $reportPath
} | ConvertTo-Json -Depth 20 -Compress

if (-not $passed) { exit 1 }
