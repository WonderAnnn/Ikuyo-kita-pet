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

$trackedPrivate = (& git -C $repoRoot ls-files -- 'local-skins' 'local-assets')
if ($LASTEXITCODE -eq 0 -and $trackedPrivate) { throw 'Private local-skins and local-assets files must not be tracked by Git.' }

if ($PublishRoot) {
    $exe = Join-Path $PublishRoot 'IkuyoPet.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish output is missing IkuyoPet.exe: $PublishRoot" }

    $publicInteraction = Join-Path $PublishRoot 'interactions\default\click.json'
    if (-not (Test-Path -LiteralPath $publicInteraction)) {
        throw "Publish output is missing public interaction fallback: $publicInteraction"
    }

    $privateSource = Join-Path $repoRoot 'local-assets\interactions\ikuyo-click.zh-CN.json'
    $privateOutput = Join-Path $PublishRoot 'interactions\ikuyo-click.json'
    if ((Test-Path -LiteralPath $privateSource) -and -not (Test-Path -LiteralPath $privateOutput)) {
        throw "Private interaction source exists but publish output is missing: $privateOutput"
    }
}

[pscustomobject]@{
    Repository = $repoRoot
    DotNet = $dotnet
    DotNetVersion = $version
    PublishRoot = if ($PublishRoot) { (Resolve-Path $PublishRoot).Path } else { $null }
    PrivateSkinTracked = $false
    PrivateAssetsTracked = $false
    PrivateBrandingPresent = Test-Path -LiteralPath (Join-Path $repoRoot 'local-assets\branding\icon\icon256.ico')
    PrivateInteractionPresent = Test-Path -LiteralPath (Join-Path $repoRoot 'local-assets\interactions\ikuyo-click.zh-CN.json')
}
