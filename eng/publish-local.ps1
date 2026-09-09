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
& $dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $output --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$privateSkin = Join-Path $repoRoot 'local-skins'
if (Test-Path -LiteralPath $privateSkin) {
    Copy-Item -LiteralPath $privateSkin -Destination (Join-Path $output 'local-skins') -Recurse -Force
}

& (Join-Path $PSScriptRoot 'verify-dev.ps1') -PublishRoot $output | Out-Host
Write-Output "Publish complete: $(Join-Path $output 'IkuyoPet.exe')"