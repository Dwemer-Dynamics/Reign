[CmdletBinding()]
param(
    [string]$SourceModuleData = "D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBox\ModuleData",
    [string]$OutputRoot = "",
    [string]$ExistingRosterRoot = "",
    [string]$ExistingManifestPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\..\staging\court_nobles"
}
if ([string]::IsNullOrWhiteSpace($ExistingRosterRoot)) {
    $ExistingRosterRoot = Join-Path $PSScriptRoot "..\..\ModuleData"
}
if ([string]::IsNullOrWhiteSpace($ExistingManifestPath)) {
    $ExistingManifestPath = Join-Path $PSScriptRoot "..\..\staging\court_nobles\court_nobles_manifest.json"
}

function Assert-That {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-Attr {
    param([System.Xml.XmlElement]$Element, [string]$Name)
    return $Element.GetAttribute($Name)
}

function Get-DisplayText {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "Unknown" }
    return ($Value -replace '^\{=[^}]+\}', '').Trim()
}

function Get-CultureKey {
    param([string]$Culture)
    return ($Culture -replace '^Culture\.', '').ToLowerInvariant()
}

function Get-RoleDefinitions {
    return @(
        [pscustomobject]@{ Key = "father"; Role = "household head"; Age = 44; IsFemale = $false },
        [pscustomobject]@{ Key = "mother"; Role = "household matriarch"; Age = 42; IsFemale = $true },
        [pscustomobject]@{ Key = "child_1"; Role = "eldest child"; Age = 25; IsFemale = $false },
        [pscustomobject]@{ Key = "child_2"; Role = "second child"; Age = 23; IsFemale = $true },
        [pscustomobject]@{ Key = "child_3"; Role = "third child"; Age = 20; IsFemale = $false },
        [pscustomobject]@{ Key = "child_4"; Role = "youngest child"; Age = 19; IsFemale = $true }
    )
}

function Get-FaceTemplateId {
    param([string]$CultureKey, [bool]$IsFemale)
    if ($IsFemale) { return "townswoman_" + $CultureKey }
    return "townsman_" + $CultureKey
}

function Get-NameBlueprints {
    return @{
        empire = @{
            male = @{ Start = @("Aev", "Ant", "Bal", "Basil", "Cal", "Ceryn", "Dec", "Dren", "Ery", "Flav", "Gav", "Greg", "Had", "Her", "Iov", "Isar", "Jor", "Kyr", "Leon", "Leth", "Marv", "Max", "Nicol", "Orien", "Oth", "Pet", "Prok", "Quint", "Rhag", "Ruf", "Sar", "Sev", "Theod", "Tim", "Tyn", "Ulp", "Val", "Varro", "Weren", "Xand", "Ysid", "Zarek", "Zen", "Zot"); Middle = @(""); End = @("ion", "os", "es", "or", "ius", "ian", "eron", "ax", "ander", "icus", "enus", "orian") }
            female = @{ Start = @("Aev", "Ant", "Bal", "Basil", "Cal", "Ceryn", "Dec", "Dren", "Ery", "Flav", "Gav", "Greg", "Had", "Her", "Iov", "Isar", "Jor", "Kyr", "Leon", "Leth", "Marv", "Max", "Nicol", "Orien", "Oth", "Pet", "Prok", "Quint", "Rhag", "Ruf", "Sar", "Sev", "Theod", "Tim", "Tyn", "Ulp", "Val", "Varro", "Weren", "Xand", "Ysid", "Zarek", "Zen", "Zot"); Middle = @(""); End = @("ia", "ara", "ene", "is", "illa", "ora", "ina", "yra", "iana", "essa", "oria", "anthe") }
            house = @{ Start = @("Aurel", "Cassian", "Corven", "Damar", "Dracon", "Elar", "Helian", "Istran", "Luceran", "Merov", "Nerion", "Pellian", "Quintar", "Sorian", "Valer", "Vard"); Middle = @(""); End = @("ides", "nion", "aris", "enus", "oria", "alon", "icus", "eran", "orius", "entis", "arian", "eon") }
        }
        sturgia = @{
            male = @{ Start = @("Alek", "Bor", "Bran", "Chern", "Dob", "Draz", "Eryk", "Fedor", "Gnev", "Igor", "Kaz", "Khor", "Lad", "Miro", "Nov", "Olek", "Pred", "Rad", "Rurik", "Svel", "Tvar", "Vlad", "Yar", "Zor"); Middle = @(""); End = @("an", "ir", "ov", "ek", "imir", "islav", "odan", "yen") }
            female = @{ Start = @("Alek", "Bor", "Bran", "Chern", "Dob", "Draz", "Eryk", "Fedor", "Gnev", "Igor", "Kaz", "Khor", "Lad", "Miro", "Nov", "Olek", "Pred", "Rad", "Rurik", "Svel", "Tvar", "Vlad", "Yar", "Zor"); Middle = @(""); End = @("a", "ena", "iva", "ysa", "mira", "slava", "anka", "yena") }
            house = @{ Start = @("Bel", "Drov", "Gor", "Kars", "Lesh", "Moroz", "Nov", "Olek", "Prav", "Rud", "Svar", "Turov", "Vez", "Yar", "Zor", "Vlad"); Middle = @(""); End = @("evich", "orin", "oslav", "enko", "avik", "ets", "omir", "yrev", "ovich", "ograd", "ensky", "ovar") }
        }
        vlandia = @{
            male = @{ Start = @("Ald", "Amaur", "Baud", "Ber", "Ced", "Cor", "Dau", "Dro", "Etien", "Evr", "Foul", "Gal", "Gaut", "Hadr", "Lior", "Mont", "Odo", "Per", "Ren", "Roul", "Ser", "Thi", "Val", "Yves"); Middle = @(""); End = @("ic", "ard", "on", "et", "ain", "ier", "aud", "ois") }
            female = @{ Start = @("Ald", "Amaur", "Baud", "Ber", "Ced", "Cor", "Dau", "Dro", "Etien", "Evr", "Foul", "Gal", "Gaut", "Hadr", "Lior", "Mont", "Odo", "Per", "Ren", "Roul", "Ser", "Thi", "Val", "Yves"); Middle = @(""); End = @("a", "elle", "ine", "ette", "iane", "ise", "aud", "erie") }
            house = @{ Start = @("Auber", "Belmont", "Corv", "Demer", "Estrel", "Faucon", "Gerv", "Haut", "Lorien", "Montel", "Orman", "Perrin", "Roche", "Serren", "Tour", "Valmont"); Middle = @(""); End = @("court", "aine", "evre", "mont", "iers", "onne", "ault", "eron", "fort", "ville", "ac", "ard") }
        }
        aserai = @{
            male = @{ Start = @("Az", "Bah", "Dar", "Emir", "Fai", "Faris", "Ham", "Haz", "Idr", "Jal", "Kam", "Kir", "Lat", "Malik", "Maz", "Nas", "Omar", "Qad", "Rash", "Saf", "Samir", "Tari", "Yaz", "Zahir"); Middle = @(""); End = @("an", "id", "ir", "un", "im", "ad", "aq", "ar") }
            female = @{ Start = @("Az", "Bah", "Dar", "Emir", "Fai", "Faris", "Ham", "Haz", "Idr", "Jal", "Kam", "Kir", "Lat", "Malik", "Maz", "Nas", "Omar", "Qad", "Rash", "Saf", "Samir", "Tari", "Yaz", "Zahir"); Middle = @(""); End = @("a", "iya", "ara", "in", "ira", "una", "ah", "aya") }
            house = @{ Start = @("Alzar", "Bahir", "Damas", "Fazir", "Hadram", "Jalal", "Kash", "Latif", "Mazal", "Nasir", "Qadir", "Rashan", "Sahir", "Tariq", "Yazid", "Zahir"); Middle = @(""); End = @("qani", "zid", "amir", "rani", "shar", "qir", "hadi", "sul", "far", "dani", "raz", "mal") }
        }
        khuzait = @{
            male = @{ Start = @("Ar", "Batu", "Bor", "Chag", "Cing", "Del", "Doru", "Eke", "Erden", "Gans", "Gerei", "Hul", "Jor", "Kadan", "Khar", "Mung", "Noy", "Orun", "Qor", "Sang", "Temur", "Torg", "Ulag", "Yel"); Middle = @(""); End = @("gan", "tem", "ur", "dai", "bek", "tai", "qai", "mur") }
            female = @{ Start = @("Ar", "Batu", "Bor", "Chag", "Cing", "Del", "Doru", "Eke", "Erden", "Gans", "Gerei", "Hul", "Jor", "Kadan", "Khar", "Mung", "Noy", "Orun", "Qor", "Sang", "Temur", "Torg", "Ulag", "Yel"); Middle = @(""); End = @("a", "ai", "ene", "ul", "un", "jin", "ara", "tai") }
            house = @{ Start = @("Altug", "Boroq", "Chagan", "Dorbet", "Erket", "Gansar", "Hulagu", "Jorq", "Kharg", "Mungol", "Noyan", "Qorchi", "Sangur", "Torgut", "Ulagan", "Yelbek"); Middle = @(""); End = @("tai", "qor", "jin", "dai", "gen", "mur", "bek", "sai", "qan", "gur", "chi", "batu") }
        }
        battania = @{
            male = @{ Start = @("Aed", "Aer", "Bevan", "Bran", "Cadan", "Caer", "Der", "Dov", "Eir", "Elid", "Fenn", "Gwy", "Ior", "Llew", "Madoc", "Mor", "Ner", "Ow", "Pry", "Rhyd", "Rian", "Tor", "Sul", "Uther"); Middle = @(""); End = @("an", "oc", "in", "ar", "wyn", "ric", "dan", "eth") }
            female = @{ Start = @("Aed", "Aer", "Bevan", "Bran", "Cadan", "Caer", "Der", "Dov", "Eir", "Elid", "Fenn", "Gwy", "Ior", "Llew", "Madoc", "Mor", "Ner", "Ow", "Pry", "Rhyd", "Rian", "Tor", "Sul", "Uther"); Middle = @(""); End = @("a", "eth", "wen", "ia", "wyn", "enne", "ara", "elis") }
            house = @{ Start = @("Aber", "Bryn", "Caer", "Dun", "Emrys", "Glen", "Ior", "Llew", "Mor", "Ner", "Pen", "Rhyd", "Sul", "Tor", "Trev", "Wyre"); Middle = @(""); End = @("dawn", "cairn", "mor", "wych", "glen", "rath", "der", "vane", "bryn", "dun", "mere", "tor") }
        }
    }
}

function Get-ExistingGameNames {
    param([string]$ModulesRoot)
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $officialModules = @("Native", "SandBoxCore", "SandBox", "StoryMode", "BirthAndDeath", "CustomBattle", "NavalDLC")
    foreach ($moduleName in $officialModules) {
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

function Get-LevenshteinDistance {
    param(
        [string]$First,
        [string]$Second
    )
    $firstValue = $First.ToLowerInvariant()
    $secondValue = $Second.ToLowerInvariant()
    $previous = New-Object int[] ($secondValue.Length + 1)
    $current = New-Object int[] ($secondValue.Length + 1)
    for ($column = 0; $column -le $secondValue.Length; $column++) {
        $previous[$column] = $column
    }
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

function Get-StableNameRank {
    param([string]$Salt, [string]$Candidate)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Salt + "|" + $Candidate)
        return [System.BitConverter]::ToString($sha.ComputeHash($bytes)).Replace("-", "")
    }
    finally {
        $sha.Dispose()
    }
}

function Join-CulturalNameParts {
    param([string[]]$Parts)
    $result = ""
    foreach ($partValue in $Parts) {
        $part = [string]$partValue
        if ([string]::IsNullOrWhiteSpace($part)) { continue }
        if ($result.Length -gt 0) {
            $lastCharacter = [char]::ToLowerInvariant($result[$result.Length - 1])
            $firstCharacter = [char]::ToLowerInvariant($part[0])
            if ($lastCharacter -eq $firstCharacter) {
                $part = $part.Substring(1)
            }
        }
        $result += $part
    }
    return $result
}

function New-DiverseAuthoredNamePool {
    param(
        [hashtable]$Blueprint,
        [int]$Count,
        [string]$Salt,
        [int]$MinimumDistance,
        [System.Collections.Generic.HashSet[string]]$ExistingNames,
        [System.Collections.Generic.HashSet[string]]$UsedNames,
        [System.Collections.Generic.List[string]]$ComparisonNames
    )
    $candidateByName = @{}
    foreach ($start in @($Blueprint.Start)) {
        foreach ($middle in @($Blueprint.Middle)) {
            foreach ($end in @($Blueprint.End)) {
                $candidate = Join-CulturalNameParts @($start, $middle, $end)
                if (-not $candidateByName.ContainsKey($candidate)) {
                    $candidateByName[$candidate] = [pscustomobject]@{
                        Name = $candidate
                        Rank = Get-StableNameRank $Salt $candidate
                    }
                }
            }
        }
    }
    $rankedCandidates = @($candidateByName.Values | Sort-Object Rank, Name)

    Assert-That ($rankedCandidates.Count -ge 75) (
        "The $Salt name pool must contain at least 75 culturally authored candidates.")
    $selected = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $rankedCandidates) {
        $candidate = $entry.Name
        if ($ExistingNames.Contains($candidate) -or $UsedNames.Contains($candidate)) { continue }
        $distinct = $true
        foreach ($prior in $ComparisonNames) {
            if ((Get-LevenshteinDistance $candidate $prior) -lt $MinimumDistance) {
                $distinct = $false
                break
            }
        }
        if (-not $distinct) { continue }
        [void]$selected.Add($candidate)
        [void]$ComparisonNames.Add($candidate)
        [void]$UsedNames.Add($candidate)
        if ($selected.Count -eq $Count) { break }
    }
    if ($selected.Count -ne $Count) {
        throw "The $Salt pool produced only $($selected.Count) of $Count names at minimum edit distance $MinimumDistance."
    }
    return ,@($selected)
}

function Write-XmlDocument {
    param([System.Xml.XmlDocument]$Document, [string]$Path)
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = "  "
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try { $Document.Save($writer) }
    finally { $writer.Dispose() }
}

function New-HeroNode {
    param(
        [System.Xml.XmlDocument]$Document,
        [pscustomobject]$Record,
        [hashtable]$IdsByRole
    )

    $hero = $Document.CreateElement("Hero")
    $hero.SetAttribute("id", $Record.id)
    $hero.SetAttribute("faction", $Record.clan_id)
    if ($Record.role -eq "father") {
        $hero.SetAttribute("spouse", "Hero." + $IdsByRole["mother"])
    }
    elseif ($Record.role -eq "mother") {
        $hero.SetAttribute("spouse", "Hero." + $IdsByRole["father"])
    }
    else {
        $hero.SetAttribute("father", "Hero." + $IdsByRole["father"])
        $hero.SetAttribute("mother", "Hero." + $IdsByRole["mother"])
    }
    $hero.SetAttribute("text", "A member of " + $Record.house_name + ", a landless noble family sworn to the realm and resident at " + $Record.home_name + ".")
    return $hero
}

$lordsPath = Join-Path $SourceModuleData "lords.xml"
$heroesPath = Join-Path $SourceModuleData "heroes.xml"
$settlementsPath = Join-Path $SourceModuleData "settlements.xml"
$clansPath = Join-Path $SourceModuleData "spclans.xml"
Assert-That (Test-Path -LiteralPath $lordsPath) "Could not find lords.xml at $lordsPath"
Assert-That (Test-Path -LiteralPath $heroesPath) "Could not find heroes.xml at $heroesPath"
Assert-That (Test-Path -LiteralPath $settlementsPath) "Could not find settlements.xml at $settlementsPath"
Assert-That (Test-Path -LiteralPath $clansPath) "Could not find spclans.xml at $clansPath"

[xml]$lordsDocument = Get-Content -LiteralPath $lordsPath -Raw
[xml]$heroesDocument = Get-Content -LiteralPath $heroesPath -Raw
[xml]$settlementsDocument = Get-Content -LiteralPath $settlementsPath -Raw
[xml]$clansDocument = Get-Content -LiteralPath $clansPath -Raw

$sourceClansById = @{}
$nobleBannerKeys = New-Object System.Collections.Generic.List[string]
foreach ($clan in @($clansDocument.Factions.Faction)) {
    $sourceClanId = Get-Attr $clan "id"
    if (-not [string]::IsNullOrWhiteSpace($sourceClanId)) {
        $sourceClansById[$sourceClanId] = $clan
    }
    $bannerKey = Get-Attr $clan "banner_key"
    if ((Get-Attr $clan "is_noble") -eq "true" -and -not [string]::IsNullOrWhiteSpace($bannerKey)) {
        $nobleBannerKeys.Add($bannerKey)
    }
}
Assert-That ($nobleBannerKeys.Count -gt 0) "No native noble banner keys were found."

$sourceHeroFactionById = @{}
foreach ($hero in @($heroesDocument.Heroes.Hero)) {
    $sourceHeroFactionById[(Get-Attr $hero "id")] = Get-Attr $hero "faction"
}

$sourceNobles = @($lordsDocument.NPCCharacters.NPCCharacter | Where-Object {
    (Get-Attr $_ "occupation") -eq "Lord" -and (Get-Attr $_ "is_hero") -eq "true"
})
Assert-That ($sourceNobles.Count -gt 0) "No vanilla noble NPCCharacter templates were found."

$existingRosterById = @{}
$existingRosterPath = Join-Path $ExistingRosterRoot "reign_court_lords.xml"
if (Test-Path -LiteralPath $existingRosterPath) {
    [xml]$existingRosterDocument = Get-Content -LiteralPath $existingRosterPath -Raw
    foreach ($existingCharacter in @($existingRosterDocument.NPCCharacters.NPCCharacter)) {
        $existingRosterById[(Get-Attr $existingCharacter "id")] = $existingCharacter
    }
}
$existingManifestById = @{}
if (Test-Path -LiteralPath $ExistingManifestPath) {
    $existingManifest = Get-Content -LiteralPath $ExistingManifestPath -Raw | ConvertFrom-Json
    foreach ($existingRecord in @($existingManifest.records)) {
        $existingManifestById[[string]$existingRecord.id] = $existingRecord
    }
}

$templatesByClanAndSex = @{}
$templatesByCultureAndSex = @{}
$existingNpcIds = @{}
foreach ($noble in $sourceNobles) {
    $nobleId = Get-Attr $noble "id"
    $existingNpcIds[$nobleId] = $true
    $isFemale = (Get-Attr $noble "is_female") -eq "true"
    $sexKey = if ($isFemale) { "female" } else { "male" }
    $cultureKey = Get-CultureKey (Get-Attr $noble "culture")
    $cultureTemplateKey = $cultureKey + "|" + $sexKey
    if (-not $templatesByCultureAndSex.ContainsKey($cultureTemplateKey)) {
        $templatesByCultureAndSex[$cultureTemplateKey] = New-Object System.Collections.ArrayList
    }
    [void]$templatesByCultureAndSex[$cultureTemplateKey].Add($noble)

    $faction = $sourceHeroFactionById[$nobleId]
    if (-not [string]::IsNullOrWhiteSpace($faction)) {
        $clanTemplateKey = $faction + "|" + $sexKey
        if (-not $templatesByClanAndSex.ContainsKey($clanTemplateKey)) {
            $templatesByClanAndSex[$clanTemplateKey] = New-Object System.Collections.ArrayList
        }
        [void]$templatesByClanAndSex[$clanTemplateKey].Add($noble)
    }
}

$fortifiedSettlements = @($settlementsDocument.Settlements.Settlement | Where-Object { $null -ne $_.SelectSingleNode("Components/Town") })
Assert-That ($fortifiedSettlements.Count -eq 120) "Expected 120 towns and castles, found $($fortifiedSettlements.Count)."

$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
$moduleDataOutput = Join-Path $outputRootFull "ModuleData"
New-Item -ItemType Directory -Path $moduleDataOutput -Force | Out-Null

$charactersOutput = New-Object System.Xml.XmlDocument
[void]$charactersOutput.AppendChild($charactersOutput.CreateXmlDeclaration("1.0", "utf-8", $null))
$charactersRoot = $charactersOutput.CreateElement("NPCCharacters")
[void]$charactersOutput.AppendChild($charactersRoot)

$heroesOutput = New-Object System.Xml.XmlDocument
[void]$heroesOutput.AppendChild($heroesOutput.CreateXmlDeclaration("1.0", "utf-8", $null))
$heroesRoot = $heroesOutput.CreateElement("Heroes")
[void]$heroesOutput.AppendChild($heroesRoot)

$clansOutput = New-Object System.Xml.XmlDocument
[void]$clansOutput.AppendChild($clansOutput.CreateXmlDeclaration("1.0", "utf-8", $null))
$clansRoot = $clansOutput.CreateElement("Factions")
[void]$clansOutput.AppendChild($clansRoot)

$roles = Get-RoleDefinitions
$nameBlueprints = Get-NameBlueprints
$modulesRoot = [System.IO.DirectoryInfo]$SourceModuleData
$modulesRoot = $modulesRoot.Parent.Parent.FullName
$existingGameNames = Get-ExistingGameNames $modulesRoot
$usedNameParts = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$usedDisplayNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$namePools = @{}
foreach ($cultureGroup in @($fortifiedSettlements | Group-Object { Get-CultureKey (Get-Attr $_ "culture") })) {
    $cultureKey = [string]$cultureGroup.Name
    $householdCount = $cultureGroup.Count
    $houseComparisons = New-Object System.Collections.Generic.List[string]
    $givenComparisons = New-Object System.Collections.Generic.List[string]
    $housePool = New-DiverseAuthoredNamePool `
        $nameBlueprints[$cultureKey].house `
        $householdCount `
        ("reign-court-v4|" + $cultureKey + "|house") `
        4 `
        $existingGameNames `
        $usedNameParts `
        $houseComparisons
    $malePool = New-DiverseAuthoredNamePool `
        $nameBlueprints[$cultureKey].male `
        ($householdCount * 3) `
        ("reign-court-v4|" + $cultureKey + "|male") `
        2 `
        $existingGameNames `
        $usedNameParts `
        $givenComparisons
    $femalePool = New-DiverseAuthoredNamePool `
        $nameBlueprints[$cultureKey].female `
        ($householdCount * 3) `
        ("reign-court-v4|" + $cultureKey + "|female") `
        2 `
        $existingGameNames `
        $usedNameParts `
        $givenComparisons
    $namePools[$cultureKey] = @{
        house = @($housePool)
        male = @($malePool)
        female = @($femalePool)
        house_index = 0
        male_index = 0
        female_index = 0
    }
}
$records = New-Object System.Collections.Generic.List[object]
$recordIds = @{}

for ($settlementIndex = 0; $settlementIndex -lt $fortifiedSettlements.Count; $settlementIndex++) {
    $settlement = $fortifiedSettlements[$settlementIndex]
    $settlementId = Get-Attr $settlement "id"
    $settlementName = Get-DisplayText (Get-Attr $settlement "name")
    $settlementType = if ((Get-Attr $settlement.Components.Town "is_castle") -eq "true") { "castle" } else { "town" }
    $originalClanId = Get-Attr $settlement "owner"
    $culture = Get-Attr $settlement "culture"
    $cultureKey = Get-CultureKey $culture
    Assert-That ($nameBlueprints.ContainsKey($cultureKey)) "No court-name blueprint exists for $culture at $settlementId."
    Assert-That (-not [string]::IsNullOrWhiteSpace($originalClanId)) "No owner clan was set for $settlementId."
    $originalClanObjectId = $originalClanId -replace '^Faction\.', ''
    $originalClan = $sourceClansById[$originalClanObjectId]
    Assert-That ($null -ne $originalClan) "The native owner clan $originalClanId was not found for $settlementId."
    $kingdomId = Get-Attr $originalClan "super_faction"
    Assert-That (-not [string]::IsNullOrWhiteSpace($kingdomId)) "The native owner clan $originalClanId has no kingdom."
    $clanObjectId = "reign_house_" + $settlementId
    $clanId = "Faction." + $clanObjectId

    $cultureNamePool = $namePools[$cultureKey]
    $houseSurname = $cultureNamePool.house[$cultureNamePool.house_index]
    $cultureNamePool.house_index++
    $houseName = "House " + $houseSurname
    $householdId = "reign_court_" + $settlementId
    $idsByRole = @{}

    foreach ($role in $roles) {
        $id = $householdId + "_" + $role.Key
        Assert-That (-not $recordIds.ContainsKey($id)) "Generated duplicate hero id $id."
        Assert-That (-not $existingNpcIds.ContainsKey($id)) "Generated id collides with a vanilla noble: $id."
        $recordIds[$id] = $true
        $idsByRole[$role.Key] = $id
    }

    $clanNode = $clansOutput.CreateElement("Faction")
    $clanNode.SetAttribute("id", $clanObjectId)
    $clanNode.SetAttribute("initial_home_settlement", "Settlement." + $settlementId)
    $clanNode.SetAttribute("name", $houseSurname)
    $clanNode.SetAttribute("tier", "1")
    $clanNode.SetAttribute("owner", "Hero." + $idsByRole["father"])
    $clanNode.SetAttribute("culture", $culture)
    $clanNode.SetAttribute("super_faction", $kingdomId)
    $clanNode.SetAttribute("is_noble", "true")
    $clanNode.SetAttribute("banner_key", $nobleBannerKeys[$settlementIndex % $nobleBannerKeys.Count])
    [void]$clansRoot.AppendChild($clanNode)

    foreach ($roleIndex in 0..($roles.Count - 1)) {
        $role = $roles[$roleIndex]
        $id = $idsByRole[$role.Key]
        $sexKey = if ($role.IsFemale) { "female" } else { "male" }
        $clanTemplateKey = $clanId + "|" + $sexKey
        $cultureTemplateKey = $cultureKey + "|" + $sexKey
        $templatePool = $templatesByClanAndSex[$clanTemplateKey]
        if ($null -eq $templatePool -or $templatePool.Count -eq 0) {
            $templatePool = $templatesByCultureAndSex[$cultureTemplateKey]
        }
        Assert-That ($null -ne $templatePool -and $templatePool.Count -gt 0) "No $sexKey noble template was found for $clanId / $culture."
        $template = $templatePool[($settlementIndex + $roleIndex) % $templatePool.Count]

        $nameIndexKey = $sexKey + "_index"
        $givenName = $cultureNamePool[$sexKey][$cultureNamePool[$nameIndexKey]]
        $cultureNamePool[$nameIndexKey]++
        Assert-That ($givenName -notmatch '\s') (
            "Generated given name must be exactly one name: $givenName")
        Assert-That (-not $givenName.Equals($houseSurname, [System.StringComparison]::OrdinalIgnoreCase)) (
            "Generated given name must remain separate from its clan surname: $givenName")
        $displayName = $givenName + " " + $houseSurname
        Assert-That (-not $existingGameNames.Contains($displayName)) "Generated display name matches an existing game name: $displayName"
        Assert-That ($usedDisplayNames.Add($displayName)) "Generated duplicate display name: $displayName"
        $record = [pscustomobject][ordered]@{
            id = $id
            household_id = $householdId
            house_name = $houseName
            role = $role.Key
            role_label = $role.Role
            age = $role.Age
            is_female = $role.IsFemale
            given_name = $givenName
            house_surname = $houseSurname
            name = $displayName
            native_name = $givenName
            clan_id = $clanId
            original_clan_id = $originalClanId
            kingdom_id = $kingdomId
            culture = $culture
            home_settlement_id = $settlementId
            home_name = $settlementName
            home_type = $settlementType
            father_id = if ($role.Key -like "child_*") { $idsByRole["father"] } else { $null }
            mother_id = if ($role.Key -like "child_*") { $idsByRole["mother"] } else { $null }
            spouse_id = if ($role.Key -eq "father") { $idsByRole["mother"] } elseif ($role.Key -eq "mother") { $idsByRole["father"] } else { $null }
            portrait_key = $id
            minor_noble = $true
            party_priority = "last_resort"
            source_template_id = if ($existingManifestById.ContainsKey($id)) {
                [string]$existingManifestById[$id].source_template_id
            } else {
                Get-Attr $template "id"
            }
        }
        $records.Add($record)

        $existingCharacter = $existingRosterById[$id]
        if ($null -ne $existingCharacter) {
            $character = $charactersOutput.ImportNode($existingCharacter, $true)
            $character.SetAttribute("name", $givenName)
            $character.SetAttribute("age", [string]$role.Age)
        }
        else {
            $character = $charactersOutput.ImportNode($template, $true)
            $character.SetAttribute("id", $id)
            $character.SetAttribute("name", $givenName)
            $character.SetAttribute("age", [string]$role.Age)
            $character.SetAttribute("culture", $culture)
            $character.SetAttribute("occupation", "Lord")
            $character.SetAttribute("is_hero", "true")
            if ($role.IsFemale) { $character.SetAttribute("is_female", "true") }
            else { $character.RemoveAttribute("is_female") }
            $face = $character.SelectSingleNode("face")
            if ($null -eq $face) {
                $face = $charactersOutput.CreateElement("face")
                [void]$character.PrependChild($face)
            }
            else {
                $face.RemoveAll()
            }
            $faceTemplate = $charactersOutput.CreateElement("face_key_template")
            $faceTemplate.SetAttribute("value", "BodyProperty." + (Get-FaceTemplateId $cultureKey $role.IsFemale))
            [void]$face.AppendChild($faceTemplate)
        }
        [void]$charactersRoot.AppendChild($character)
    }

    foreach ($record in @($records | Where-Object { $_.household_id -eq $householdId })) {
        [void]$heroesRoot.AppendChild((New-HeroNode -Document $heroesOutput -Record $record -IdsByRole $idsByRole))
    }
}

$expectedCount = $fortifiedSettlements.Count * $roles.Count
Assert-That ($records.Count -eq $expectedCount) "Expected $expectedCount court-noble records, generated $($records.Count)."

$recordById = @{}
foreach ($record in $records) { $recordById[$record.id] = $record }
foreach ($child in @($records | Where-Object { $_.role -like "child_*" })) {
    $father = $recordById[$child.father_id]
    $mother = $recordById[$child.mother_id]
    Assert-That ($null -ne $father -and $null -ne $mother) "Child $($child.id) is missing a generated parent."
    Assert-That (($father.age - $child.age) -ge 17) "Father $($father.id) is too young for child $($child.id)."
    Assert-That (($mother.age - $child.age) -ge 17) "Mother $($mother.id) is too young for child $($child.id)."
}

$charactersPath = Join-Path $moduleDataOutput "reign_court_lords.xml"
$heroesPathOutput = Join-Path $moduleDataOutput "reign_court_heroes.xml"
$clansPathOutput = Join-Path $moduleDataOutput "reign_court_clans.xml"
Write-XmlDocument -Document $charactersOutput -Path $charactersPath
Write-XmlDocument -Document $heroesOutput -Path $heroesPathOutput
Write-XmlDocument -Document $clansOutput -Path $clansPathOutput

$manifest = [ordered]@{
    roster_version = 4
    generated_utc = [DateTime]::UtcNow.ToString("o")
    source_module_data = $SourceModuleData
    household_count = $fortifiedSettlements.Count
    nobles_per_holding = $roles.Count
    noble_count = $records.Count
    existing_game_names_checked = $existingGameNames.Count
    records = $records
}
$manifestPath = Join-Path $outputRootFull "court_nobles_manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

$fragmentPath = Join-Path $outputRootFull "SubModule.Xmls.fragment.xml"
@"
<Xmls>
  <XmlNode>
    <XmlName id="Factions" path="reign_court_clans"/>
    <IncludedGameTypes>
      <GameType value="Campaign"/>
      <GameType value="CampaignStoryMode"/>
    </IncludedGameTypes>
  </XmlNode>
  <XmlNode>
    <XmlName id="NPCCharacters" path="reign_court_lords"/>
    <IncludedGameTypes>
      <GameType value="Campaign"/>
      <GameType value="CampaignStoryMode"/>
    </IncludedGameTypes>
  </XmlNode>
  <XmlNode>
    <XmlName id="Heroes" path="reign_court_heroes"/>
    <IncludedGameTypes>
      <GameType value="Campaign"/>
      <GameType value="CampaignStoryMode"/>
    </IncludedGameTypes>
  </XmlNode>
</Xmls>
"@ | Set-Content -LiteralPath $fragmentPath -Encoding utf8

# Reload the emitted XML so a malformed generated document fails at generation time.
[xml]$validatedCharacters = Get-Content -LiteralPath $charactersPath -Raw
[xml]$validatedHeroes = Get-Content -LiteralPath $heroesPathOutput -Raw
[xml]$validatedClans = Get-Content -LiteralPath $clansPathOutput -Raw
Assert-That (@($validatedCharacters.NPCCharacters.NPCCharacter).Count -eq $expectedCount) "Generated character XML count does not match the roster."
Assert-That (@($validatedHeroes.Heroes.Hero).Count -eq $expectedCount) "Generated hero XML count does not match the roster."
Assert-That (@($validatedClans.Factions.Faction).Count -eq $fortifiedSettlements.Count) "Generated clan XML count does not match the households."

$reportPath = Join-Path $outputRootFull "generation_report.txt"
@(
    "Court noble staging generation passed.",
    "Fortified holdings: $($fortifiedSettlements.Count)",
    "Towns: $(@($fortifiedSettlements | Where-Object { (Get-Attr $_.Components.Town 'is_castle') -ne 'true' }).Count)",
    "Castles: $(@($fortifiedSettlements | Where-Object { (Get-Attr $_.Components.Town 'is_castle') -eq 'true' }).Count)",
    "Households: $($fortifiedSettlements.Count)",
    "Court nobles: $($records.Count)",
    "Landless clans: $($fortifiedSettlements.Count)",
    "Clan-name minimum edit distance within culture: 4",
    "Given-name minimum edit distance within culture: 2",
    "Culturally authored candidate minimum per pool: 75",
    "Existing authored NPC definitions preserved: $($existingRosterById.Count)",
    "Existing game names checked: $($existingGameNames.Count)",
    "NPCCharacter XML: $charactersPath",
    "Hero XML: $heroesPathOutput",
    "Clan XML: $clansPathOutput",
    "No files were copied to the live Bannerlord Modules directory."
) | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host (Get-Content -LiteralPath $reportPath -Raw)
