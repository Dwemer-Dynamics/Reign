[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputCatalog,
    [Parameter(Mandatory = $true)]
    [string]$PreviousManifest,
    [Parameter(Mandatory = $true)]
    [string]$CurrentManifest,
    [Parameter(Mandatory = $true)]
    [string]$ReferenceCatalog,
    [Parameter(Mandatory = $true)]
    [string]$OutputCatalog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-That {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Replace-WholeName {
    param(
        [string]$Value,
        [hashtable]$NameMap,
        [regex]$NamePattern
    )
    return $NamePattern.Replace(
        $Value,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return [string]$NameMap[$match.Value]
        })
}

function Update-StringValues {
    param(
        $Value,
        [hashtable]$NameMap,
        [regex]$NamePattern
    )
    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) {
        return Replace-WholeName $Value $NameMap $NamePattern
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in @($Value.Keys)) {
            $Value[$key] = Update-StringValues $Value[$key] $NameMap $NamePattern
        }
        return $Value
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in @($Value.PSObject.Properties)) {
            $property.Value = Update-StringValues $property.Value $NameMap $NamePattern
        }
        return $Value
    }
    if ($Value -is [System.Collections.IList]) {
        for ($index = 0; $index -lt $Value.Count; $index++) {
            $Value[$index] = Update-StringValues $Value[$index] $NameMap $NamePattern
        }
        return ,$Value
    }
    return $Value
}

function Add-NameMapping {
    param(
        [hashtable]$NameMap,
        [string]$Old,
        [string]$New
    )
    if ([string]::IsNullOrWhiteSpace($Old) -or $Old -ceq $New) { return }
    if ($NameMap.ContainsKey($Old)) {
        Assert-That ([string]$NameMap[$Old] -ceq $New) "Conflicting replacements for '$Old'."
        return
    }
    $NameMap[$Old] = $New
}

$catalog = Get-Content -LiteralPath $InputCatalog -Raw | ConvertFrom-Json
$previous = Get-Content -LiteralPath $PreviousManifest -Raw | ConvertFrom-Json
$current = Get-Content -LiteralPath $CurrentManifest -Raw | ConvertFrom-Json
$reference = Get-Content -LiteralPath $ReferenceCatalog -Raw | ConvertFrom-Json

$previousById = @{}
foreach ($record in @($previous.records)) {
    $previousById[[string]$record.id] = $record
}
$currentById = @{}
foreach ($record in @($current.records)) {
    $currentById[[string]$record.id] = $record
}

Assert-That ($previousById.Count -eq 720) "Previous manifest must contain 720 court nobles."
Assert-That ($currentById.Count -eq 720) "Current manifest must contain 720 court nobles."

$nameMap = @{}
foreach ($heroId in @($currentById.Keys)) {
    Assert-That ($previousById.ContainsKey($heroId)) "Previous manifest is missing $heroId."
    $oldRecord = $previousById[$heroId]
    $newRecord = $currentById[$heroId]
    Add-NameMapping $nameMap ([string]$oldRecord.name) ([string]$newRecord.name)
    Add-NameMapping $nameMap ([string]$oldRecord.house_name) ([string]$newRecord.house_name)
    Add-NameMapping $nameMap ([string]$oldRecord.house_surname) ([string]$newRecord.house_surname)
    Add-NameMapping $nameMap ([string]$oldRecord.given_name) ([string]$newRecord.given_name)
}

$escapedNames = @(
    $nameMap.Keys |
        Sort-Object @{ Expression = { $_.Length }; Descending = $true }, @{ Expression = { $_ }; Descending = $false } |
        ForEach-Object { [regex]::Escape($_) }
)
Assert-That ($escapedNames.Count -gt 0) "No changed court names were found."
$patternText = '(?<![\p{L}\p{N}_])(?:' + ($escapedNames -join '|') + ')(?![\p{L}\p{N}_])'
$namePattern = [regex]::new(
    $patternText,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

$courtProfileCount = @(
    $catalog.profiles | Where-Object { [string]$_.source -eq "reign_court" }
).Count
Assert-That ($courtProfileCount -eq 720) "Expected 720 court profiles, found $courtProfileCount."

# Apply every rename in one regex pass per string. This prevents a replacement
# from cascading when a new name happens to equal another character's old name,
# and updates family references wherever they occur in the catalog.
foreach ($profile in @($catalog.profiles)) {
    [void](Update-StringValues $profile $nameMap $namePattern)
}

Assert-That ($reference.manifest.reignSourceHash -is [string]) "Reference catalog is missing its Reign source hash."
$catalog.manifest.reignSourceHash = [string]$reference.manifest.reignSourceHash
$catalog.generatedUtc = [DateTime]::UtcNow.ToString("o")

$outputFullPath = [System.IO.Path]::GetFullPath($OutputCatalog)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$json = $catalog | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText(
    $outputFullPath,
    $json,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Court profile catalog rename passed: 720 profiles and all family references updated without regeneration."
