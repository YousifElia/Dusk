# Renders every screen and theme to docs/screenshots, and builds the animated demos in docs/media.
# Dusk renders the images itself (--snapshot), so nothing is captured from your screen and no
# screen colours change. Run it from anywhere: powershell -ExecutionPolicy Bypass -File scripts\make-media.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "bin\Dusk.exe"
$shots = Join-Path $root "docs\screenshots"
$media = Join-Path $root "docs\media"
$frames = Join-Path $env:TEMP "dusk-frames"

if (-not (Test-Path $exe)) { & (Join-Path $root "build.cmd") | Out-Null }
foreach ($dir in @($shots, $media, $frames)) { New-Item -ItemType Directory -Force $dir | Out-Null }

function Snap($file, $arguments) {
    $p = Start-Process $exe -ArgumentList ("--snapshot `"$file`" " + $arguments) -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(30000)) { throw "Timed out rendering $file" }
    if ($p.ExitCode -ne 0) { throw "Dusk exited with $($p.ExitCode) rendering $file" }
}

Write-Host "Screenshots..."
$evening = 21.37
foreach ($screen in @("horizon", "dial", "strata", "colors", "firstrun", "tray")) {
    foreach ($theme in @("light", "dark")) {
        Snap (Join-Path $shots "$screen-$theme.png") "--screen $screen --theme $theme --hour $evening --scale 1.25"
    }
}
Snap (Join-Path $shots "horizon-day-light.png") "--screen horizon --theme light --hour 12.5 --scale 1.25"
Snap (Join-Path $shots "horizon-day-dark.png") "--screen horizon --theme dark --hour 12.5 --scale 1.25"
Snap (Join-Path $shots "darkroom-light.png") "--screen horizon --theme light --hour $evening --scale 1.25 --darkroom"
Snap (Join-Path $shots "darkroom-tray-light.png") "--screen tray --theme light --hour $evening --scale 1.25 --darkroom"

Write-Host "Day-cycle frames..."
$cycle = @()
for ($i = 0; $i -lt 24; $i++) {
    $hour = [math]::Round($i * 24 / 24, 2)
    $file = Join-Path $frames ("day-{0:00}.png" -f $i)
    Snap $file "--screen horizon --theme light --hour $hour --scale 0.6"
    $cycle += $file
}
$views = @()
foreach ($screen in @("horizon", "dial", "strata")) {
    $file = Join-Path $frames "view-$screen.png"
    Snap $file "--screen $screen --theme light --hour $evening --scale 0.6"
    $views += $file
}
$themes = @()
foreach ($theme in @("light", "dark")) {
    $file = Join-Path $frames "theme-$theme.png"
    Snap $file "--screen horizon --theme $theme --hour $evening --scale 0.6"
    $themes += $file
}

Write-Host "Building GIFs..."
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\WPF"
$gifTool = Join-Path $root "bin\MakeGif.exe"
& $csc -nologo -optimize+ -target:exe -out:$gifTool "-r:$wpf\PresentationCore.dll" "-r:$wpf\WindowsBase.dll" `
    "-r:$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\System.Xaml.dll" (Join-Path $root "tools\MakeGif.cs")
if ($LASTEXITCODE -ne 0) { throw "Couldn't build MakeGif" }

& $gifTool (Join-Path $media "day-cycle.gif") 12 @cycle
& $gifTool (Join-Path $media "views.gif") 120 @views
& $gifTool (Join-Path $media "themes.gif") 150 @themes

Get-ChildItem $shots, $media | ForEach-Object { "  {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
Write-Host "Done."
