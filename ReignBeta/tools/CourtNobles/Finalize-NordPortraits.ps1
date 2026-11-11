[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerAssembly,
    [Parameter(Mandatory=$true)][string]$PortraitDirectory,
    [ValidateRange(0.25,1.0)][double]$ReviewedHeadroomFaceHeights = 0.25,
    [switch]$DetectOnly
)
# File authoring only. Never invoke server Main, a listener, settings or a DB.
# Bind the caller's canonical build; retain its report and these hash receipts.
$ErrorActionPreference = 'Stop'
$ServerAssembly = [IO.Path]::GetFullPath($ServerAssembly)
$artifactRoot = Split-Path -Parent $ServerAssembly
$PortraitDirectory = [IO.Path]::GetFullPath($PortraitDirectory)
$sourcePath = Join-Path $PortraitDirectory 'portrait.png'
$assemblyHash = (Get-FileHash -LiteralPath $ServerAssembly).Hash.ToLowerInvariant()
$masterHash = (Get-FileHash -LiteralPath $sourcePath).Hash.ToLowerInvariant()
$resolver = [Func[System.Runtime.Loader.AssemblyLoadContext,Reflection.AssemblyName,Reflection.Assembly]] {
    param($context,$name)
    $candidate = Join-Path $artifactRoot ($name.Name + '.dll')
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $context.LoadFromAssemblyPath($candidate) }
    return $null
}
[System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving($resolver)
$session = $null; $options = $null
try {
    $assembly = [System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($ServerAssembly)
    $decodeArgs = [object[]]@([IO.File]::ReadAllBytes($sourcePath),0,0)
    $rgba = $assembly.GetType('AIPortraits.PngReencode',$true).GetMethod('DecodeToRgba').Invoke($null,$decodeArgs)
    $width = [int]$decodeArgs[1]; $height = [int]$decodeArgs[2]
    if ($width -lt 768 -or $height -lt 1024 -or $width/$height -lt (2/3) -or $width/$height -gt (3/4)) { throw 'Final master requires a full-size approved 2:3 through 3:4 portrait canvas.' }
    $program = $assembly.GetType('ReignBetaServer.Program',$true)
    $flags = [Reflection.BindingFlags]'NonPublic,Static'
    $focusField = $program.GetField('PortraitFocusSession',$flags)
    $modelPath = Join-Path $artifactRoot 'portrait_models/version-RFB-320.onnx'
    $modelHash = (Get-FileHash -LiteralPath $modelPath).Hash.ToLowerInvariant()
    if ($modelHash -ne '34cd7e60aeff28744c657de7a3dc64e872d506741de66987f3426f2b79f88017') { throw 'Approved face-model hash mismatch.' }
    # This host's AppDomain has another base directory: explicitly bind the
    # hash-verified model. Production detector math and crop code are untouched.
    $optionsType = $focusField.FieldType.Assembly.GetType('Microsoft.ML.OnnxRuntime.SessionOptions',$true)
    $options = [Activator]::CreateInstance($optionsType)
    $severity = $optionsType.GetProperty('LogSeverityLevel')
    $severity.SetValue($options,[Enum]::Parse($severity.PropertyType,'ORT_LOGGING_LEVEL_ERROR'))
    $session = $focusField.FieldType.GetConstructor([type[]]@([string],$optionsType)).Invoke([object[]]@([string]$modelPath,$options.PSObject.BaseObject))
    $focusField.SetValue($null,$session)
    $focus = $program.GetMethod('DetectPortraitFaceFocus',$flags).Invoke($null,[object[]]@($rgba,$width,$height))
    $core = $assembly.GetType('ReignPortraits.PortraitDerivativeCore',$true)
    $compositionError = $core.GetMethod('PortraitCompositionError').Invoke($null,[object[]]@($focus))
    if ($compositionError) { throw $compositionError }
    if ($focus.CandidateCount -ne 1) { throw 'Exactly one reliably detected face is required.' }
    $receipt = [ordered]@{schema='reign-nord-portrait-finalization-v1'; masterSha256=$masterHash;
        artifactAssemblySha256=$assemblyHash; faceModelSha256=$modelHash; width=$width; height=$height;
        focus=$focus; productionDetector=$true; productionCropBuilder=$true; databaseAccess=$false;
        masterModified=$false; derivativesWritten=(-not $DetectOnly); completedUtc=[DateTime]::UtcNow.ToString('o')}
    if (-not $DetectOnly) {
        $encode = $assembly.GetType('AIPortraits.PngEncoder',$true).GetMethod('EncodeRgba').CreateDelegate([Func[byte[],int,int,byte[]]])
        $build = $core.GetMethod('BuildRequiredFocusedV2').Invoke($null,[object[]]@($rgba,$width,$height,[IO.FileInfo]::new($sourcePath),$encode,$focus))
        if ($ReviewedHeadroomFaceHeights -gt 0.25) {
            # An explicit author-reviewed crop may retain more hair/headroom.
            # Preserve the measured face box, output dimensions, aspect ratios,
            # unmodified master/zoom, and production resampling/PNG codecs.
            $cropY = [Math]::Max(0,[int][Math]::Round(($focus.Y-$focus.Height*$ReviewedHeadroomFaceHeights)*$height,[MidpointRounding]::AwayFromZero))
            $cropMethod = $core.GetMethod('CropRgba',[Reflection.BindingFlags]'NonPublic,Static')
            $resizeMethod = $core.GetMethod('ResizeBilinear')
            $m = $build.Manifest
            $crop = $cropMethod.Invoke($null,[object[]]@($rgba,$width,$height,$m.CropX,$cropY,$m.CropWidth,$m.CropHeight))
            $wide = $cropMethod.Invoke($null,[object[]]@($rgba,$width,$height,$m.WideCropX,$cropY,$m.WideCropWidth,$m.WideCropHeight))
            $small = $resizeMethod.Invoke($null,[object[]]@($crop,$m.CropWidth,$m.CropHeight,$m.ThumbnailWidth,$m.ThumbnailHeight))
            $wideSmall = $resizeMethod.Invoke($null,[object[]]@($wide,$m.WideCropWidth,$m.WideCropHeight,$m.PartyThumbnailWidth,$m.PartyThumbnailHeight))
            $chest = $resizeMethod.Invoke($null,[object[]]@($crop,$m.CropWidth,$m.CropHeight,$m.PortraitWidth,$m.PortraitHeight))
            $build.ThumbnailPng = $encode.Invoke($small,$m.ThumbnailWidth,$m.ThumbnailHeight)
            $build.PartyThumbnailPng = $encode.Invoke($wideSmall,$m.PartyThumbnailWidth,$m.PartyThumbnailHeight)
            $build.PortraitPng = $encode.Invoke($chest,$m.PortraitWidth,$m.PortraitHeight)
            $m.CropY = $cropY; $m.WideCropY = $cropY
            $receipt.reviewedHeadroomFaceHeights = $ReviewedHeadroomFaceHeights
            $receipt.reviewedCropTop = $cropY
        }
        $utf8 = [Text.UTF8Encoding]::new($false)
        $manifestBytes = $utf8.GetBytes(($build.Manifest | ConvertTo-Json -Depth 12))
        $commitArgs = [object[]]@($sourcePath.PSObject.BaseObject,$PortraitDirectory.PSObject.BaseObject,$build,$manifestBytes,$null)
        if (-not $core.GetMethod('CommitV2').Invoke($null,$commitArgs)) { throw ('Production derivative commit failed: ' + $commitArgs[4]) }
        if (-not $core.GetMethod('IsCurrentV2').Invoke($null,[object[]]@($build.Manifest,$sourcePath.PSObject.BaseObject,$PortraitDirectory.PSObject.BaseObject))) { throw 'Written derivatives failed current-product integrity.' }
        $receipt.derivatives = $build.Manifest
        [IO.File]::WriteAllText((Join-Path $PortraitDirectory 'finalization-receipt.json'),($receipt | ConvertTo-Json -Depth 15),$utf8)
    }
    if ((Get-FileHash -LiteralPath $sourcePath).Hash.ToLowerInvariant() -ne $masterHash) { throw 'Master changed during finalization.' }
    $receipt | ConvertTo-Json -Depth 15
} finally {
    if ($session) { $session.Dispose() }; if ($options) { $options.Dispose() }
    [System.Runtime.Loader.AssemblyLoadContext]::Default.remove_Resolving($resolver)
}
