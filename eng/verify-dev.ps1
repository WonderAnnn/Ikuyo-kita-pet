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

$trackedPrivate = (& git -C $repoRoot ls-files -- 'local-skins')
if ($LASTEXITCODE -eq 0 -and $trackedPrivate) { throw 'Private local-skins files must not be tracked by Git.' }

if ($PublishRoot) {
    $exe = Join-Path $PublishRoot 'IkuyoPet.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish output is missing IkuyoPet.exe: $PublishRoot" }
}

[pscustomobject]@{
    Repository = $repoRoot
    DotNet = $dotnet
    DotNetVersion = $version
    PublishRoot = if ($PublishRoot) { (Resolve-Path $PublishRoot).Path } else { $null }
    PrivateSkinTracked = $false
}