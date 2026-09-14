[CmdletBinding()]
param(
    [string]$StagingRoot = "",
    [string]$SourceModuleData = "D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBox\ModuleData",
    [string]$ExistingRosterRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($StagingRoot)) {
    $StagingRoot = Join-Path $PSScriptRoot "..\..\staging\court_nobles"
}
if ([string]::IsNullOrWhiteSpace($ExistingRosterRoot)) {
    $ExistingRosterRoot = Join-Path $PSScriptRoot "..\..\ModuleData"
}

function Assert-That {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-Attr {
    param([System.Xml.XmlElement]$Element, [string]$Name)
    return $Element.GetAttribute($Name)
}

function Normalize-ElementXml {
    param([System.Xml.XmlElement]$Element)
    $normalized = ($Element.OuterXml -replace '>\s+<', '><') -replace '\s+/>', '/>'
    return [System.Text.RegularExpressions.Regex]::Replace($normalized, '<([A-Za-z_][A-Za-z0-9_.-]*)([^>]*)/>', '<$1$2></$1>')
}

function Get-DisplayText {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return ($Value -replace '^\{=[^}]+\}', '').Trim()
}

function Get-LevenshteinDistance {
    param([string]$First, [string]$Second)
    $firstValue = $First.ToLowerInvariant()
    $secondValue = $Second.ToLowerInvariant()
    $previous = New-Object int[] ($secondValue.Length + 1)
    $current = New-Object int[] ($secondValue.Length + 1)
    for ($column = 0; $column -le $secondValue.Length; $column++) { $previous[$column] = $column }
    for ($row = 1; $row -le $firstValue.Length; $row++) {
        $current[0] = $row
        for ($column = 1; $column -le $secondValue.Length; $column++) {
            $cost = if ($firstValue[$row - 1] -eq $secondValue[$column - 1]) { 0 } else { 1 }
            $current[$column] = [Math]::Min(
                [Math]::Min($current[$column - 1] + 1, $previous[$column] + 1),
                $previous[$column - 1] + $cost)
        }
        $swap = $previous
        $previous = $current
        $current = $swap
    }
    return $previous[$secondValue.Length]
}

function Get-ExistingGameNames {
    param([string]$ModulesRoot)
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($moduleName in @("Native", "SandBoxCore", "SandBox", "StoryMode", "BirthAndDeath", "CustomBattle", "NavalDLC")) {
        $modulePath = Join-Path $ModulesRoot $moduleName
        if (-not (Test-Path -LiteralPath $modulePath)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $modulePath -Recurse -File -Filter "*.xml") {
            $raw = [System.IO.File]::ReadAllText($file.FullName)
            foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($raw, '\bname\s*=\s*"([^"]+)"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $display = Get-DisplayText ([System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value))
                if (-not [string]::IsNullOrWhiteSpace($display)) { [void]$names.Add($display) }
            }
        }
    }
    return ,$names
}

$stagingRootFull = [System.IO.Path]::GetFullPath($StagingRoot)
$charactersPath = Join-Path $stagingRootFull "ModuleData\reign_court_lords.xml"
$heroesPath = Join-Path $stagingRootFull "ModuleData\reign_court_heroes.xml"
$clansPath = Join-Path $stagingRootFull "ModuleData\reign_court_clans.xml"
$manifestPath = Join-Path $stagingRootFull "court_nobles_manifest.json"
$fragmentPath = Join-Path $stagingRootFull "SubModule.Xmls.fragment.xml"

foreach ($path in @($charactersPath, $heroesPath, $clansPath, $manifestPath, $fragmentPath)) {
    Assert-That (Test-Path -LiteralPath $path) "Missing staged output: $path"
}

[xml]$charactersDocument = Get-Content -LiteralPath $charactersPath -Raw
[xml]$heroesDocument = Get-Content -LiteralPath $heroesPath -Raw
[xml]$clansDocument = Get-Content -LiteralPath $clansPath -Raw
[xml]$sourceLordsDocument = Get-Content -LiteralPath (Join-Path $SourceModuleData "lords.xml") -Raw
[xml]$sourceClansDocument = Get-Content -LiteralPath (Join-Path $SourceModuleData "spclans.xml") -Raw
[xml]$sourceSettlementsDocument = Get-Content -LiteralPath (Join-Path $SourceModuleData "settlements.xml") -Raw
[xml]$existingRosterDocument = Get-Content -LiteralPath (Join-Path $ExistingRosterRoot "reign_court_lords.xml") -Raw
[xml]$fragmentDocument = Get-Content -LiteralPath $fragmentPath -Raw
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$modulesRoot = ([System.IO.DirectoryInfo]$SourceModuleData).Parent.Parent.FullName
$existingGameNames = Get-ExistingGameNames $modulesRoot
$behaviorPath = Join-Path $PSScriptRoot "..\..\src\Court\ReignCourtNobleCampaignBehavior.cs"
Assert-That (Test-Path -LiteralPath $behaviorPath) "Missing court noble campaign behavior."
$behaviorSource = [System.IO.File]::ReadAllText($behaviorPath)
Assert-That ($behaviorSource -match 'MinimumFaceSliderValue\s*=\s*-25') "Court noble face minimum must remain -25."
Assert-That ($behaviorSource -match 'MaximumFaceSliderValue\s*=\s*24') "Court noble face maximum must remain 24."
Assert-That ($behaviorSource -match 'if\s*\(index\s*>\s*0\)\s*choices\.Add') "Female hairstyle selection must exclude bald index 0."
Assert-That ($behaviorSource -match 'FaceGen\.SetHair\(ref constrained, hair, hero\.IsFemale \? 0 : -1, -1\)') "Every court noble must receive the selected non-bald hairstyle."
$completedMigrationGuard = $behaviorSource.IndexOf('if (_householdClanMigrationVersion >= HouseholdClanMigrationVersion)', [System.StringComparison]::Ordinal)
$matureFiefAssertion = $behaviorSource.IndexOf('New court house unexpectedly owns a settlement', [System.StringComparison]::Ordinal)
Assert-That ($completedMigrationGuard -ge 0) "Completed household migrations must short-circuit on later save loads."
Assert-That ($matureFiefAssertion -gt $completedMigrationGuard) "The completed-migration guard must run before new-campaign fief assertions."

$records = @($manifest.records)
$expectedCount = [int]$manifest.household_count * [int]$manifest.nobles_per_holding
Assert-That ($manifest.roster_version -eq 4) "Expected court roster version 4."
Assert-That ($manifest.household_count -eq 120) "Expected 120 households in the staged manifest."
Assert-That ($manifest.nobles_per_holding -eq 6) "Expected six nobles per fortified holding."
Assert-That ($records.Count -eq $expectedCount) "Manifest roster count does not match its declared size."

$charactersById = @{}
foreach ($character in @($charactersDocument.NPCCharacters.NPCCharacter)) {
    $id = Get-Attr $character "id"
    Assert-That (-not $charactersById.ContainsKey($id)) "Duplicate NPCCharacter id: $id"
    $charactersById[$id] = $character
}

$heroesById = @{}
foreach ($hero in @($heroesDocument.Heroes.Hero)) {
    $id = Get-Attr $hero "id"
    Assert-That (-not $heroesById.ContainsKey($id)) "Duplicate Hero id: $id"
    $heroesById[$id] = $hero
}
Assert-That ($charactersById.Count -eq $expectedCount) "NPCCharacter count does not match the roster."
Assert-That ($heroesById.Count -eq $expectedCount) "Hero count does not match the roster."

$sourceNoblesById = @{}
foreach ($sourceNoble in @($sourceLordsDocument.NPCCharacters.NPCCharacter | Where-Object {
    (Get-Attr $_ "occupation") -eq "Lord" -and (Get-Attr $_ "is_hero") -eq "true"
})) {
    $sourceNoblesById[(Get-Attr $sourceNoble "id")] = $sourceNoble
}
$existingRosterById = @{}
foreach ($existingCharacter in @($existingRosterDocument.NPCCharacters.NPCCharacter)) {
    $existingRosterById[(Get-Attr $existingCharacter "id")] = $existingCharacter
}
$sourceClansById = @{}
foreach ($clan in @($sourceClansDocument.Factions.Faction)) {
    $sourceClansById["Faction." + (Get-Attr $clan "id")] = $clan
}
$sourceSettlementOwners = @{}
foreach ($settlement in @($sourceSettlementsDocument.Settlements.Settlement)) {
    $sourceSettlementOwners[(Get-Attr $settlement "id")] = Get-Attr $settlement "owner"
}
$clansById = @{}
foreach ($clan in @($clansDocument.Factions.Faction)) {
    $id = "Faction." + (Get-Attr $clan "id")
    Assert-That (-not $clansById.ContainsKey($id)) "Duplicate generated clan id: $id"
    $clansById[$id] = $clan
}
Assert-That ($clansById.Count -eq 120) "Expected 120 generated landless clans."

$recordsById = @{}
$generatedGivenNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$generatedHouseNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$generatedDisplayNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($record in $records) {
    Assert-That (-not $recordsById.ContainsKey($record.id)) "Duplicate manifest id: $($record.id)"
    $recordsById[$record.id] = $record
    Assert-That ($charactersById.ContainsKey($record.id)) "Missing NPCCharacter for $($record.id)"
    Assert-That ($heroesById.ContainsKey($record.id)) "Missing Hero for $($record.id)"
    Assert-That ($clansById.ContainsKey($record.clan_id)) "Missing generated clan faction on $($record.id): $($record.clan_id)"
    Assert-That ($sourceClansById.ContainsKey($record.original_clan_id)) "Invalid original native clan on $($record.id): $($record.original_clan_id)"
    Assert-That ($sourceSettlementOwners[$record.home_settlement_id] -eq $record.original_clan_id) "Native settlement ownership was not preserved for $($record.id)."

    $character = $charactersById[$record.id]
    Assert-That ((Get-Attr $character "occupation") -eq "Lord") "Generated character $($record.id) is not a lord."
    Assert-That ((Get-Attr $character "is_hero") -eq "true") "Generated character $($record.id) is not marked as a hero."
    Assert-That ([int](Get-Attr $character "age") -ge 19) "Generated adult $($record.id) is too close to Bannerlord's coming-of-age boundary."
    Assert-That ((Get-Attr $character "culture") -eq $record.culture) "Culture mismatch on $($record.id)."
    Assert-That ([int](Get-Attr $character "age") -eq [int]$record.age) "Age mismatch on $($record.id)."
    Assert-That ((Get-Attr $character "name") -eq $record.given_name) "Native-style first-name mismatch on $($record.id)."
    Assert-That ($record.given_name -notmatch '\s') "Given name contains an embedded surname on $($record.id)."
    Assert-That ($record.native_name -eq $record.given_name) "Manifest native name mismatch on $($record.id)."
    Assert-That ($record.name -eq ($record.given_name + " " + $record.house_surname)) "Display-name composition mismatch on $($record.id)."
    Assert-That ($null -ne $character.SelectSingleNode("face")) "Missing face data on $($record.id)."
    Assert-That ($null -eq $character.SelectSingleNode("face/BodyProperties")) "Copied fixed BodyProperties remain on $($record.id)."
    $sexPrefix = if ($record.is_female) { "townswoman_" } else { "townsman_" }
    $expectedFaceTemplate = "BodyProperty." + $sexPrefix + ($record.culture -replace '^Culture\.', '').ToLowerInvariant()
    $faceTemplate = $character.SelectSingleNode("face/face_key_template")
    Assert-That ($null -ne $faceTemplate -and (Get-Attr $faceTemplate "value") -eq $expectedFaceTemplate) "Incorrect random face template on $($record.id)."
    Assert-That ($null -ne $character.SelectSingleNode("Equipments")) "Missing equipment data on $($record.id)."
    Assert-That (-not $existingGameNames.Contains($record.given_name)) "Generated given name matches an existing game name: $($record.given_name)"
    Assert-That (-not $existingGameNames.Contains($record.house_surname)) "Generated house name matches an existing game name: $($record.house_surname)"
    Assert-That (-not $existingGameNames.Contains($record.name)) "Generated full name matches an existing game name: $($record.name)"
    Assert-That ($generatedGivenNames.Add($record.given_name)) "Duplicate generated given name: $($record.given_name)"
    Assert-That ($generatedDisplayNames.Add($record.name)) "Duplicate generated display name: $($record.name)"
    [void]$generatedHouseNames.Add($record.house_surname)

    $sourceTemplate = $sourceNoblesById[$record.source_template_id]
    Assert-That ($null -ne $sourceTemplate) "Missing source template $($record.source_template_id) for $($record.id)."
    $existingCharacter = $existingRosterById[$record.id]
    if ($null -ne $existingCharacter) {
        $baselineClone = $existingCharacter.CloneNode($true)
        $generatedClone = $character.CloneNode($true)
        $baselineClone.RemoveAttribute("name")
        $generatedClone.RemoveAttribute("name")
        $baselineClone.RemoveAttribute("age")
        $generatedClone.RemoveAttribute("age")
        Assert-That ((Normalize-ElementXml $baselineClone) -eq (Normalize-ElementXml $generatedClone)) "Existing character data changed beyond the first name on $($record.id)."
    }
    else {
        $sourceChildren = @($sourceTemplate.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] -and $_.Name -ne "face" })
        $generatedChildren = @($character.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] -and $_.Name -ne "face" })
        Assert-That ($sourceChildren.Count -eq $generatedChildren.Count) "Child XML shape changed on $($record.id)."
        for ($childIndex = 0; $childIndex -lt $sourceChildren.Count; $childIndex++) {
            Assert-That ((Normalize-ElementXml $sourceChildren[$childIndex]) -eq (Normalize-ElementXml $generatedChildren[$childIndex])) "Nested noble attributes changed on $($record.id)."
        }
    }

    $hero = $heroesById[$record.id]
    Assert-That ((Get-Attr $hero "faction") -eq $record.clan_id) "Hero clan mismatch on $($record.id)."
    if ($record.role -eq "father") {
        Assert-That ((Get-Attr $hero "spouse") -eq ("Hero." + $record.spouse_id)) "Father spouse link mismatch on $($record.id)."
    }
    elseif ($record.role -eq "mother") {
        Assert-That ((Get-Attr $hero "spouse") -eq ("Hero." + $record.spouse_id)) "Mother spouse link mismatch on $($record.id)."
    }
    else {
        Assert-That ((Get-Attr $hero "father") -eq ("Hero." + $record.father_id)) "Father link mismatch on $($record.id)."
        Assert-That ((Get-Attr $hero "mother") -eq ("Hero." + $record.mother_id)) "Mother link mismatch on $($record.id)."
        $father = $recordsById[$record.father_id]
        $mother = $recordsById[$record.mother_id]
        Assert-That (([int]$father.age - [int]$record.age) -ge 17) "Father age is implausible for $($record.id)."
        Assert-That (([int]$mother.age - [int]$record.age) -ge 17) "Mother age is implausible for $($record.id)."
    }
}

Assert-That ($generatedHouseNames.Count -eq 120) "Expected 120 unique authored house names."

foreach ($cultureGroup in @($records | Group-Object culture)) {
    $givenNames = @($cultureGroup.Group.given_name)
    for ($firstIndex = 0; $firstIndex -lt $givenNames.Count; $firstIndex++) {
        for ($secondIndex = $firstIndex + 1; $secondIndex -lt $givenNames.Count; $secondIndex++) {
            Assert-That ((Get-LevenshteinDistance $givenNames[$firstIndex] $givenNames[$secondIndex]) -ge 2) (
                "Given names are confusingly similar in $($cultureGroup.Name): $($givenNames[$firstIndex]) / $($givenNames[$secondIndex])")
        }
    }
    $houseNames = @($cultureGroup.Group.house_surname | Sort-Object -Unique)
    for ($firstIndex = 0; $firstIndex -lt $houseNames.Count; $firstIndex++) {
        for ($secondIndex = $firstIndex + 1; $secondIndex -lt $houseNames.Count; $secondIndex++) {
            Assert-That ((Get-LevenshteinDistance $houseNames[$firstIndex] $houseNames[$secondIndex]) -ge 4) (
                "Clan names are confusingly similar in $($cultureGroup.Name): $($houseNames[$firstIndex]) / $($houseNames[$secondIndex])")
        }
    }
}

foreach ($household in @($records | Group-Object household_id)) {
    Assert-That ($household.Count -eq 6) "Household $($household.Name) does not contain six existing heroes."
    $clanIds = @($household.Group.clan_id | Sort-Object -Unique)
    Assert-That ($clanIds.Count -eq 1) "Household $($household.Name) maps to multiple clans."
    $clan = $clansById[$clanIds[0]]
    $father = @($household.Group | Where-Object role -eq "father")[0]
    Assert-That ((Get-Attr $clan "owner") -eq ("Hero." + $father.id)) "Clan leader mismatch for $($household.Name)."
    Assert-That ((Get-Attr $clan "name") -eq $father.house_surname) "Clan surname mismatch for $($household.Name)."
    Assert-That ((Get-Attr $clan "tier") -eq "1") "Landless clan tier mismatch for $($household.Name)."
    Assert-That ((Get-Attr $clan "is_noble") -eq "true") "Clan is not noble for $($household.Name)."
    Assert-That ((Get-Attr $clan "initial_home_settlement") -eq ("Settlement." + $father.home_settlement_id)) "Clan residence mismatch for $($household.Name)."
    Assert-That ((Get-Attr $clan "super_faction") -eq $father.kingdom_id) "Clan kingdom mismatch for $($household.Name)."
    Assert-That (-not [string]::IsNullOrWhiteSpace((Get-Attr $clan "banner_key"))) "Clan banner is missing for $($household.Name)."
}

Assert-That ($fragmentDocument.Xmls.XmlNode.Count -eq 3) "Activation fragment must contain exactly three XML nodes."
Assert-That ($fragmentDocument.Xmls.XmlNode[0].XmlName.id -eq "Factions") "First activation node must load Factions."
Assert-That ($fragmentDocument.Xmls.XmlNode[1].XmlName.id -eq "NPCCharacters") "Second activation node must load NPCCharacters."
Assert-That ($fragmentDocument.Xmls.XmlNode[2].XmlName.id -eq "Heroes") "Third activation node must load Heroes."
foreach ($node in @($fragmentDocument.Xmls.XmlNode)) {
    $gameTypes = @($node.IncludedGameTypes.GameType | ForEach-Object { $_.value })
    Assert-That ($gameTypes -contains "Campaign") "Activation node is missing Campaign support."
    Assert-That ($gameTypes -contains "CampaignStoryMode") "Activation node is missing CampaignStoryMode support."
}

Write-Host "Court noble staging validation passed: $expectedCount existing NPCCharacters, $expectedCount existing Heroes, 120 independent landless clans."
