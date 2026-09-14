param(
    [string]$LogPath = "D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\logs\action-gauntlet.jsonl"
)

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Error "Log not found: $LogPath"
    exit 1
}

$rows = Get-Content -LiteralPath $LogPath -ErrorAction Stop |
    Where-Object { $_.Trim().Length -gt 0 } |
    ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $_ -ne $null -and $_.phase -like "live_dialogue.*" }

if (-not $rows) {
    Write-Host "No live dialogue beta rows found."
    exit 0
}

$latestStart = $rows |
    Where-Object { $_.phase -eq "live_dialogue.start" } |
    Select-Object -Last 1

$runId = if ($latestStart) { $latestStart.correlationId } else { ($rows | Select-Object -Last 1).correlationId }
$runRows = $rows | Where-Object { $_.correlationId -eq $runId }
$caseRows = $runRows | Where-Object { $_.phase -ne "live_dialogue.start" -and $_.phase -ne "live_dialogue.summary" }
$summary = $runRows | Where-Object { $_.phase -eq "live_dialogue.summary" } | Select-Object -Last 1

Write-Host "Latest live dialogue beta run: $runId"
if ($summary) {
    Write-Host $summary.summary
}

$caseRows |
    Select-Object `
        @{Name="case"; Expression={$_.data.id}},
        status,
        @{Name="failureClass"; Expression={$_.data.failureClass}},
        @{Name="executionState"; Expression={$_.data.executionState}},
        @{Name="expected"; Expression={$_.data.expectedType}},
        @{Name="queued"; Expression={($_.data.queuedTypes -join ", ")}},
        @{Name="durationMs"; Expression={$_.data.durationMs}},
        @{Name="message"; Expression={$_.summary}} |
    Format-Table -AutoSize -Wrap

$failed = $caseRows | Where-Object { $_.status -eq "failed" }
if ($failed) {
    Write-Host ""
    Write-Host "Failures:"
    $failed | ForEach-Object {
        Write-Host "- $($_.data.id): $($_.summary)"
        if ($_.data.actionShadowPreview) {
            Write-Host "  shadow: $($_.data.actionShadowPreview)"
        }
        if ($_.data.error) {
            Write-Host "  error: $($_.data.error)"
        }
    }
}
