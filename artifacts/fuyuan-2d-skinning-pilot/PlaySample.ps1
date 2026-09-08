#requires -Version 7.0
$player = Join-Path $PSScriptRoot 'Build/FuyuanPilot.exe'
if (-not (Test-Path -LiteralPath $player)) { throw "Player missing: $player. Open UnityProject and use Fuyuan Pilot/Build Standalone Player." }
# This entry point is explicitly for the user to watch and interact with the sample.
Start-Process -FilePath $player -ArgumentList @('-screen-fullscreen','0','-screen-width','1920','-screen-height','1080') -WindowStyle Normal
