#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('1920','1366')][string[]]$Resolutions = @('1920','1366'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
$ffmpeg = (& python -c 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())').Trim()
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $ffmpeg)) { throw 'Existing imageio-ffmpeg runtime is unavailable.' }
foreach ($width in $Resolutions) {
    $frames = Join-Path $repository "src/Logs/d12-visibility/$width/%04d.jpg"
    $output = Join-Path $PSScriptRoot "evidence/$width/visibility.mp4"
    $frameCount = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot "evidence/$width/capture-frames.txt") -Raw)
    if ($frameCount -le 0) { throw "Invalid captured frame count: $width" }
    & $ffmpeg -hide_banner -loglevel error -y -framerate 30 -i $frames -frames:v $frameCount -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart $output
    if ($LASTEXITCODE -ne 0) { throw "Video encoding failed: $width" }
    Write-Output "Encoded $output"
}
