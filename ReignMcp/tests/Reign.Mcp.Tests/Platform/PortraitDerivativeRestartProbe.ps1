param(
    [Parameter(Mandatory = $true)][string]$Workspace,
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$FixtureRoot,
    [Parameter(Mandatory = $true)][string]$HeroDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('publish', 'publish-outfit', 'prepared', 'committed', 'recover', 'inspect', 'inspect-held', 'inspect-outfit-source', 'changed-receipt')][string]$Phase,
    [Parameter(Mandatory = $true)][string]$BannerlordPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap { [Console]::Error.WriteLine($_.Exception.ToString() + [Environment]::NewLine + $_.ScriptStackTrace); exit 1 }

if ($env:REIGN_VALIDATION_MODE -ne '1') { throw 'The derivative restart probe requires canonical validation.' }
$generated = Join-Path ([IO.Path]::GetFullPath($Workspace)) '.codex-build'
$root = [IO.Path]::GetFullPath($FixtureRoot)
$hero = [IO.Path]::GetFullPath($HeroDirectory)
$case = [IO.Path]::GetDirectoryName($hero)
if ([IO.Path]::GetDirectoryName($root) -ne $generated -or
    [IO.Path]::GetFileName($root) -cnotmatch '^reign-d49-[0-9a-f]{32}$' -or
    [IO.Path]::GetDirectoryName($case) -ne $root -or
    [IO.Path]::GetFileName($case) -cnotin @('prepared', 'committed') -or
    [IO.Path]::GetFileName($hero) -cne 'hero') {
    throw 'This probe requires an exact task-owned derivative fixture.'
}
foreach ($directory in @($generated, $root, $case, $hero)) {
    if (([IO.File]::GetAttributes($directory) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The derivative fixture cannot follow a reparse point.'
    }
}

$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$script:assemblyRoots = @([IO.Path]::GetDirectoryName($clientPath),
    (Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'))
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $script:assemblyRoots) {
        $path = Join-Path $directory $name
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
[Reflection.Assembly]::Load('Newtonsoft.Json') | Out-Null
$client = [Reflection.Assembly]::LoadFrom($clientPath)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$cache = $client.GetType('AIPortraits.PortraitCache', $true)
$derivatives = $client.GetType('AIPortraits.PortraitDerivativeService', $true)
# Use CLR strings for MethodInfo.Invoke. Windows PowerShell wraps Join-Path output.
$source = [IO.Path]::Combine($hero, 'custom.png')
$receipt = [IO.Path]::Combine($hero, '.outfit_portrait.json')

$baseMaster = [IO.Path]::Combine($hero, 'portrait.png')
if ($Phase -in @('inspect', 'inspect-held')) {
    $current = $derivatives.GetMethod('HasCurrentFullBodyProducts', $flags).Invoke($null, [object[]]@($source, $hero))
    if ($current -ne ($Phase -eq 'inspect')) {
        throw 'The fresh reader did not respect the exact derivative recovery state.'
    }
    exit 0
}
if ($Phase -eq 'inspect-outfit-source') {
    $baseCurrent = $derivatives.GetMethod('HasCurrentFullBodyProducts', $flags).Invoke($null, [object[]]@($baseMaster, $hero))
    $preferred = $derivatives.GetMethod('ResolvePreferredV2Source', $flags).Invoke($null, [object[]]@($source, $hero))
    if (-not $baseCurrent -or $preferred -cne $source) {
        throw 'The restored base derivatives hid the retained confirmed outfit.'
    }
    exit 0
}
if ($Phase -eq 'publish') { $source = $baseMaster }

$gate = $cache.GetMethod('ProductCommitLock', $flags).Invoke($null, [object[]]@($hero))
[Threading.Monitor]::Enter($gate)
try {
    if ($Phase -eq 'recover') {
        if (-not $cache.GetMethod('RecoverLocalPortraitProduct', $flags).Invoke($null, [object[]]@($hero))) {
            throw 'The fresh client process could not settle the derivative fixture.'
        }
        exit 0
    }

    $observe = $null
    if ($Phase -notin @('publish', 'publish-outfit')) {
        $observe = [Action[bool]]{
            param([bool]$committed)
            if ($Phase -eq 'prepared' -and -not $committed) { [Environment]::Exit(86) }
            if ($Phase -eq 'committed' -and $committed) { [Environment]::Exit(87) }
            if ($Phase -eq 'changed-receipt' -and -not $committed) {
                # A formatting-only change leaves the JSON and the raw source valid.
                [IO.File]::AppendAllText($receipt, [Environment]::NewLine)
            }
        }
    }
    try {
        $product = $derivatives.GetMethod('BuildAndPublishV2', $flags).Invoke($null, [object[]]@($source, $hero, $observe))
    }
    catch {
        if ($Phase -eq 'changed-receipt') {
            $failure = $_.Exception
            while (($failure -is [System.Management.Automation.MethodInvocationException] -or
                    $failure -is [Reflection.TargetInvocationException]) -and $null -ne $failure.InnerException) {
                $failure = $failure.InnerException
            }
            $transaction = [Reflection.Assembly]::LoadFrom([IO.Path]::Combine([IO.Path]::GetDirectoryName($clientPath), 'ReignBeta.Core.dll')).GetType('AIPortraits.PortraitProductTransaction', $true)
            $hasJournal = $transaction.GetMethod('HasRecoveryJournal', $flags).Invoke($null, [object[]]@($hero))
            if ($failure.GetType() -eq [IO.IOException] -and $null -eq $failure.InnerException -and
                $failure.Message -ceq 'The confirmed outfit portrait or its focus receipt changed during derivative preparation.' -and
                -not $hasJournal) {
                exit 88
            }
            [Console]::Error.WriteLine("Changed-receipt failure: $($failure.GetType().FullName), HRESULT $($failure.HResult), recovery journal: $hasJournal.")
        }
        throw
    }
    if ($Phase -notin @('publish', 'publish-outfit') -or $null -eq $product) { throw 'The exact derivative commit phase did not complete as expected.' }
}
finally { [Threading.Monitor]::Exit($gate) }
exit 0
