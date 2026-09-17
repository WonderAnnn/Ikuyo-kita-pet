[CmdletBinding()]
param(
    [string] $OutputRoot = '',
    [switch] $SkipRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = if ($env:IKUYO_PET_DOTNET) { $env:IKUYO_PET_DOTNET } else { 'G:\IkuyoPetDev\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Missing .NET SDK: $dotnet" }

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\publish\win-x64' }
$output = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $output | Out-Null

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = 'true'
if (Test-Path -LiteralPath 'G:\IkuyoPetDev\nuget\packages') { $env:NUGET_PACKAGES = 'G:\IkuyoPetDev\nuget\packages' }

$project = Join-Path $repoRoot 'src\IkuyoPet.App\IkuyoPet.App.csproj'
if (-not $SkipRestore) {
    & $dotnet restore $project --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
}
& $dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $output --no-restore -p:IncludeLocalOverrides=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$stalePrivateSkin = Join-Path $output 'local-skins'
if (Test-Path -LiteralPath $stalePrivateSkin) { Remove-Item -LiteralPath $stalePrivateSkin -Recurse -Force }
$stalePrivateInteraction = Join-Path $output 'interactions\ikuyo-click.json'
if (Test-Path -LiteralPath $stalePrivateInteraction) { Remove-Item -LiteralPath $stalePrivateInteraction -Force }

$publishedExe = Join-Path $output 'IkuyoPet.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Publish output is missing IkuyoPet.exe: $output"
}

$publicFiles = @(
    'interactions\default\click.json',
    'interactions\kita-click.json',
    'branding\icon\icon256.ico',
    'skins\kita-original\1.0.0\manifest.json',
    'skins\kita-original\1.0.0\idle.png',
    'skins\kita-original\1.0.0\remind.png',
    'assets\bubbles\kita\kita_cloud2-source.png',
    'assets\bubbles\kita\kita_cloud3-source.png'
)
$missing = @($publicFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $output $_) -PathType Leaf) })
if ($missing.Count -gt 0) { throw "Publish output is missing public resources: $($missing -join ', ')" }

if ((Test-Path -LiteralPath (Join-Path $output 'local-skins')) -or
    (Test-Path -LiteralPath (Join-Path $output 'interactions\ikuyo-click.json'))) {
    throw 'Publish output contains private local overrides.'
}

& (Join-Path $PSScriptRoot 'verify-dev.ps1') -PublishRoot $output | Out-Host
Write-Output "Publish complete: $(Join-Path $output 'IkuyoPet.exe')"
