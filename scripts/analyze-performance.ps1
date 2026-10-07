param([Parameter(Mandatory=$true)][string]$Path, [double]$SkipSeconds = 10)
$ErrorActionPreference = 'Stop'
$samples = @(Get-Content -LiteralPath $Path | ForEach-Object {
    try { $entry = $_ | ConvertFrom-Json -ErrorAction Stop; if ($entry.Type -eq 'sample' -and $entry.ElapsedSeconds -ge $SkipSeconds) { $entry } } catch { }
})
if (!$samples.Count) { throw 'No complete diagnostic samples in the selected interval.' }
function Percentile($Values, [double]$P) {
    $ordered = @($Values | Sort-Object)
    if (!$ordered.Count) { return 0 }
    return $ordered[[Math]::Min($ordered.Count - 1, [int][Math]::Ceiling($ordered.Count * $P) - 1)]
}
$slow = @($samples | ForEach-Object { $_.SlowFrames })
$errors = @($samples | ForEach-Object { $_.Status.Error; $_.Status.MediaError } | Where-Object { $_ } | Select-Object -Unique)
$report = [ordered]@{
    Log = (Resolve-Path -LiteralPath $Path).Path
    Samples = $samples.Count
    FirstUtc = $samples[0].Utc; LastUtc = $samples[-1].Utc
    AverageFps = ($samples.FramesPerSecond | Measure-Object -Average).Average
    MissedFrames = ($samples.MissedInInterval | Measure-Object -Sum).Sum
    AverageCpuPercentOfOneCore = ($samples.CpuPercentOfOneCore | Measure-Object -Average).Average
    PeakWorkingSetMiB = ($samples.WorkingSetBytes | Measure-Object -Maximum).Maximum / 1MB
    PeakManagedMiB = ($samples.ManagedBytes | Measure-Object -Maximum).Maximum / 1MB
    AudioUnderrunsAtEnd = $samples[-1].Status.AudioUnderruns
    SlowFrameCount = $slow.Count
    SlowFrameP95Milliseconds = Percentile $slow.Milliseconds 0.95
    SlowFramePeakMilliseconds = ($slow.Milliseconds | Measure-Object -Maximum).Maximum
    Gc0DuringInterval = $samples[-1].Gc0 - $samples[0].Gc0
    Gc1DuringInterval = $samples[-1].Gc1 - $samples[0].Gc1
    Gc2DuringInterval = $samples[-1].Gc2 - $samples[0].Gc2
    RouteErrors = $errors
    Note = 'FPS is sampled from published engine counters; startup, seeks and counter resets can affect intervals. Slow-frame percentiles cover recorded slow frames only.'
}
$report | ConvertTo-Json -Depth 6
