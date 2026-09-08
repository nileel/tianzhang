#requires -Version 7.0
$editor = 'C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe'
$project = Join-Path $PSScriptRoot 'UnityProject'
if (-not (Test-Path -LiteralPath $editor)) { throw 'Use Unity Hub to open UnityProject with Unity 6000.3.18f1.' }
# This entry point is explicitly for the user to edit the delivered experiment.
Start-Process -FilePath $editor -ArgumentList @('-projectPath',('"'+$project+'"'),'-openfile',('"'+$project+'/Assets/FuyuanPilot/Scenes/FuyuanSkinningPilot.unity"')) -WindowStyle Normal
