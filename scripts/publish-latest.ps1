[CmdletBinding()]
param(
    [string]$DotnetPath = 'G:\IkuyoPetDev\dotnet\dotnet.exe',
    [string]$ProjectRoot = '',
    [switch]$SkipDesktopShortcut
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw "Missing .NET SDK: $DotnetPath" }
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$publishRoot = Join-Path $projectRootPath 'artifacts\publish'
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
$publishRoot = (Resolve-Path -LiteralPath $publishRoot).Path
$token = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $publishRoot "latest-staging-$token"
$uninstallerStaging = Join-Path $publishRoot "uninstaller-staging-$token"
$backup = Join-Path $publishRoot "latest-backup-$token"
$latest = Join-Path $publishRoot 'latest'
$solution = Join-Path $projectRootPath 'IkuyoPet.sln'
$project = Join-Path $projectRootPath 'src\IkuyoPet.App\IkuyoPet.App.csproj'
$uninstallerProject = Join-Path $projectRootPath 'src\IkuyoPet.Uninstaller\IkuyoPet.Uninstaller.csproj'
$assetRoot = Join-Path $projectRootPath 'assets'

function Update-DesktopShortcut {
    param([string]$TargetPath)

    if ($SkipDesktopShortcut) { return }
    try {
        $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
        if ([string]::IsNullOrWhiteSpace($desktop)) {
            Write-Warning 'Desktop folder was not resolved; shortcut update skipped.'
            return
        }
        $shortcutPath = Join-Path $desktop 'Ikuyo Pet.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $TargetPath
        $shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
        $shortcut.IconLocation = "$TargetPath,0"
        $shortcut.Description = 'Ikuyo Pet（固定指向 latest 发布目录）'
        $shortcut.Save()
        Write-Output "Desktop shortcut updated: $shortcutPath"
    }
    catch {
        Write-Warning "Desktop shortcut update skipped: $($_.Exception.Message)"
    }
}
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'App project Version is missing.' }

try {
    & $DotnetPath restore $solution --runtime win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    & $DotnetPath publish $project --configuration Release --runtime win-x64 --self-contained true --no-restore --output $staging -p:IncludeLocalOverrides=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    & $DotnetPath publish $uninstallerProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $uninstallerStaging
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish uninstaller failed with exit code $LASTEXITCODE" }
    Get-ChildItem -LiteralPath $uninstallerStaging | Copy-Item -Destination $staging -Recurse -Force
    Remove-Item -LiteralPath $uninstallerStaging -Recurse -Force

    $appExe = Join-Path $staging 'IkuyoPet.exe'
    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { throw 'Published executable is missing.' }
    $uninstallerExe = Join-Path $staging 'IkuyoPet.Uninstaller.exe'
    if (-not (Test-Path -LiteralPath $uninstallerExe -PathType Leaf)) { throw 'Published uninstaller is missing.' }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($appExe).FileVersion
    $uninstallerFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($uninstallerExe).FileVersion
    if (-not $fileVersion.StartsWith($version, [StringComparison]::Ordinal)) {
        throw "Published version $fileVersion does not match project version $version."
    }

    $resourceHashes = [ordered]@{}
    $sourceFiles = Get-ChildItem -LiteralPath $assetRoot -Recurse -File
    foreach ($sourceFile in $sourceFiles) {
        $assetRelative = $sourceFile.FullName.Substring($assetRoot.Length).TrimStart("\", "/").Replace("\", "/")
        $targetRelative = $assetRelative
        if ($assetRelative -like 'bubbles/*') {
            $targetRelative = "assets/$assetRelative"
        }
        if ($assetRelative -eq 'interactions/default/click.zh-CN.json') {
            $targetRelative = 'interactions/default/click.json'
        }
        elseif ($assetRelative -eq 'interactions/kita-click.zh-CN.json') {
            $targetRelative = 'interactions/kita-click.json'
        }

        $publishedPath = Join-Path $staging ($targetRelative.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            throw "Missing published resource: $targetRelative (source $assetRelative)"
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $publishedHash = (Get-FileHash -LiteralPath $publishedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sourceHash -ne $publishedHash) {
            throw "Published resource hash mismatch: $targetRelative"
        }
        $resourceHashes[$targetRelative] = $publishedHash
    }

    & (Join-Path $projectRootPath 'eng\verify-dev.ps1') -PublishRoot $staging | Out-Host

    $commit = (& git -C $projectRootPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve git commit.' }
    $buildInfo = [ordered]@{
        version = $version
        fileVersion = $fileVersion
        commit = $commit
        buildTimeUtc = [DateTimeOffset]::UtcNow.ToString('O')
        resourceCount = $resourceHashes.Count
        resources = $resourceHashes
        uninstaller = [ordered]@{
            file = 'IkuyoPet.Uninstaller.exe'
            fileVersion = $uninstallerFileVersion
            sha256 = (Get-FileHash -LiteralPath $uninstallerExe -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $staging 'build-info.json') -Encoding utf8

    if (Test-Path -LiteralPath $latest) {
        try {
            Move-Item -LiteralPath $latest -Destination $backup
        }
        catch {
            throw "Unable to replace artifacts/publish/latest. Close any running IkuyoPet.exe started from that directory and retry."
        }
    }
    try {
        Move-Item -LiteralPath $staging -Destination $latest
    }
    catch {
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $latest }
        throw
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    Update-DesktopShortcut (Join-Path $latest 'IkuyoPet.exe')
    Get-Content -LiteralPath (Join-Path $latest 'build-info.json') -Raw
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (Test-Path -LiteralPath $uninstallerStaging) { Remove-Item -LiteralPath $uninstallerStaging -Recurse -Force }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
}
