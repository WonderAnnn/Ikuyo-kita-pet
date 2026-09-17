param(
    [string]$Executable = '.\artifacts\publish\latest\IkuyoPet.exe',
    [int]$WarmupSeconds = 30,
    [int]$SampleSeconds = 300,
    [string]$Output = '.\artifacts\diagnostics\runtime-performance.json'
)

$ErrorActionPreference = 'Stop'
if ($WarmupSeconds -lt 0) { throw 'WarmupSeconds must be non-negative.' }
if ($SampleSeconds -le 0) { throw 'SampleSeconds must be positive.' }

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$process = $null
$cpuSamples = [System.Collections.Generic.List[double]]::new()
$memorySamples = [System.Collections.Generic.List[int64]]::new()
$startupMilliseconds = $null

try {
    $process = Start-Process -FilePath $Executable -PassThru
    while ($stopwatch.Elapsed.TotalSeconds -lt $WarmupSeconds) {
        if ($process.HasExited) { throw 'Measured process exited during warmup.' }
        Start-Sleep -Seconds 1
        $process.Refresh()
    }
    $startupMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 0)

    $previousCpu = $process.TotalProcessorTime
    $previousAt = [DateTimeOffset]::UtcNow
    $sampleStopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($sampleStopwatch.Elapsed.TotalSeconds -lt $SampleSeconds) {
        if ($process.HasExited) { break }
        Start-Sleep -Seconds 1
        $process.Refresh()
        $now = [DateTimeOffset]::UtcNow
        $cpu = $process.TotalProcessorTime
        $elapsedSeconds = ($now - $previousAt).TotalSeconds
        if ($elapsedSeconds -gt 0) {
            $cpuSamples.Add([math]::Max(0, (($cpu - $previousCpu).TotalSeconds / $elapsedSeconds) * 100 / [Environment]::ProcessorCount))
        }
        $memorySamples.Add([int64]$process.PrivateMemorySize64)
        $previousCpu = $cpu
        $previousAt = $now
    }

    $outputPath = [IO.Path]::GetFullPath($Output)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [ordered]@{
        measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executableName = [IO.Path]::GetFileName($Executable)
        startupMilliseconds = $startupMilliseconds
        averageCpuPercent = if ($cpuSamples.Count -eq 0) { 0 } else { [math]::Round(($cpuSamples | Measure-Object -Average).Average, 3) }
        privateWorkingSetBytes = if ($memorySamples.Count -eq 0) { 0 } else { [int64](($memorySamples | Measure-Object -Average).Average) }
        sampleCount = $cpuSamples.Count
    } | ConvertTo-Json | Set-Content -LiteralPath $outputPath -Encoding utf8
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) { $process.Kill() }
    }
    if ($process) { $process.Dispose() }
}
