[CmdletBinding()]
param(
    [string] $PublishRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = if ($env:IKUYO_PET_DOTNET) { $env:IKUYO_PET_DOTNET } else { 'G:\IkuyoPetDev\dotnet\dotnet.exe' }

if (-not (Test-Path -LiteralPath $dotnet)) { throw "Missing .NET SDK: $dotnet" }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'IkuyoPet.sln'))) { throw "Not a repository root: $repoRoot" }

$version = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^10\.') { throw "Requires .NET 10 SDK, got '$version'" }

$trackedPrivate = @(& git -C $repoRoot ls-files -- 'local-skins/**' 'local-assets/**')
if ($LASTEXITCODE -eq 0 -and $trackedPrivate.Count -gt 0) {
    throw "Private local-skins and local-assets files must not be tracked by Git: $($trackedPrivate -join ', ')"
}
$trackedGenerated = @(& git -C $repoRoot ls-files -- 'tmp/**' 'output/**' 'artifacts/**' 'bin/**' 'obj/**' 'TestResults/**')
if ($LASTEXITCODE -eq 0 -and $trackedGenerated.Count -gt 0) {
    throw "Generated files must not be tracked by Git: $($trackedGenerated -join ', ')"
}

$assetRoot = Join-Path $repoRoot 'assets'
$publicMappings = @(
    Get-ChildItem -LiteralPath $assetRoot -Recurse -File | ForEach-Object {
        $assetRelative = $_.FullName.Substring($assetRoot.Length).TrimStart("\", "/").Replace("\", "/")
        $targetRelative = $assetRelative
        if ($assetRelative -like 'bubbles/*') {
            $targetRelative = "assets/$assetRelative"
        }
        elseif ($assetRelative -eq 'interactions/default/click.zh-CN.json') {
            $targetRelative = 'interactions/default/click.json'
        }
        elseif ($assetRelative -eq 'interactions/kita-click.zh-CN.json') {
            $targetRelative = 'interactions/kita-click.json'
        }
        [pscustomobject]@{
            Source = $_.FullName
            SourceRelative = $assetRelative
            TargetRelative = $targetRelative
        }
    }
)
$publicRelativeFiles = @($publicMappings.TargetRelative)
$publicSourceMissing = @(
    $publicMappings | Where-Object {
        -not (Test-Path -LiteralPath $_.Source -PathType Leaf)
    } | ForEach-Object TargetRelative
)
if ($publicSourceMissing.Count -gt 0) {
    throw "Public source assets are missing: $($publicSourceMissing -join ', ')"
}

$publishResolved = $null
$publishMissing = @()
$publishHasPrivateOverrides = $false
$publishUninstallerPresent = $null
if ($PublishRoot) {
    $publishResolved = (Resolve-Path -LiteralPath $PublishRoot).Path
    $exe = Join-Path $publishResolved 'IkuyoPet.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Publish output is missing IkuyoPet.exe: $publishResolved" }
    $uninstallerExe = Join-Path $publishResolved 'IkuyoPet.Uninstaller.exe'
    if (-not (Test-Path -LiteralPath $uninstallerExe -PathType Leaf)) { throw "Publish output is missing IkuyoPet.Uninstaller.exe: $publishResolved" }
    $publishUninstallerPresent = $true

    $publishMissing = @(
        $publicRelativeFiles | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $publishResolved $_) -PathType Leaf)
        }
    )
    if ($publishMissing.Count -gt 0) {
        throw "Publish output is missing public assets: $($publishMissing -join ', ')"
    }

    $publishHasPrivateOverrides =
        (Test-Path -LiteralPath (Join-Path $publishResolved 'local-skins')) -or
        (Test-Path -LiteralPath (Join-Path $publishResolved 'interactions\ikuyo-click.json'))
    if ($publishHasPrivateOverrides) {
        throw 'Publish output contains private local overrides.'
    }
}

[pscustomobject]@{
    Repository = $repoRoot
    DotNet = $dotnet
    DotNetVersion = $version
    PublishRoot = $publishResolved
    PublicSourceFiles = $publicRelativeFiles.Count
    PublicAssetsPresent = ($publicSourceMissing.Count -eq 0)
    PublicPublishAssetsPresent = if ($PublishRoot) { $publishMissing.Count -eq 0 } else { $null }
    PublishUninstallerPresent = $publishUninstallerPresent
    PublishHasPrivateOverrides = $publishHasPrivateOverrides
    PrivateSkinTracked = ($trackedPrivate | Where-Object { $_ -like 'local-skins/*' }).Count -gt 0
    PrivateAssetsTracked = $trackedPrivate.Count -gt 0
    GeneratedFilesTracked = $trackedGenerated.Count -gt 0
    PrivateBrandingPresent = Test-Path -LiteralPath (Join-Path $repoRoot 'local-assets\branding\icon\icon256.ico')
    PrivateInteractionPresent = Test-Path -LiteralPath (Join-Path $repoRoot 'local-assets\interactions\ikuyo-click.zh-CN.json')
}
