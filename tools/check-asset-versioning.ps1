[CmdletBinding()]
param(
  [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "ProjectRoot does not exist: $root" }
& git -C $root rev-parse --is-inside-work-tree | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ProjectRoot is not a Git worktree: $root" }

$artRelative = 'src/Assets/Art'
$artPath = Join-Path $root $artRelative
$binaryExtensions = @('.psd','.psb','.png','.jpg','.jpeg','.tga','.exr','.hdr','.wav','.mp3','.ogg','.flac','.aiff','.fbx','.blend','.mp4','.mov','.webm','.ttf','.otf')
$unityTextExtensions = @('.meta','.unity','.prefab','.asset','.mat')
$errors = [System.Collections.Generic.List[string]]::new()

function Get-RelativeGitPath {
  param([string]$Path)
  $rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
  if (-not $Path.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Path is outside ProjectRoot: $Path" }
  return ($Path.Substring($rootPrefix.Length) -replace '\\','/')
}

function Get-FilterAttribute {
  param([string]$RelativePath)
  $output = @(& git -C $root check-attr filter -- $RelativePath)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { throw "git check-attr failed for $RelativePath" }
  $parts = $output[0] -split ': ', 3
  if ($parts.Count -ne 3) { throw "Unexpected git check-attr output for ${RelativePath}: $($output[0])" }
  return $parts[2]
}

if (-not (Test-Path -LiteralPath $artPath -PathType Container)) {
  'check-asset-versioning: OK (src/Assets/Art does not exist yet)'
  exit 0
}
if (-not (Test-Path -LiteralPath (Join-Path $root '.gitattributes') -PathType Leaf)) {
  throw 'Missing .gitattributes while src/Assets/Art exists.'
}

$artFiles = @(Get-ChildItem -LiteralPath $artPath -Recurse -File)
foreach ($file in $artFiles) {
  $relative = Get-RelativeGitPath $file.FullName
  $extension = $file.Extension.ToLowerInvariant()
  if ($binaryExtensions -contains $extension -and (Get-FilterAttribute $relative) -ne 'lfs') {
    $errors.Add("ERROR`tASSET_LFS_MISSING`t$relative`tBinary runtime asset is not tracked by Git LFS.") | Out-Null
  }
}

$assetsPath = Join-Path $root 'src/Assets'
if (Test-Path -LiteralPath $assetsPath -PathType Container) {
  foreach ($file in @(Get-ChildItem -LiteralPath $assetsPath -Recurse -File)) {
    $relative = Get-RelativeGitPath $file.FullName
    if ($unityTextExtensions -contains $file.Extension.ToLowerInvariant() -and (Get-FilterAttribute $relative) -eq 'lfs') {
      $errors.Add("ERROR`tUNITY_TEXT_IN_LFS`t$relative`tUnity text asset must remain in ordinary Git.") | Out-Null
    }
  }
}

if ($errors.Count -gt 0) {
  'check-asset-versioning: FAILED'
  $errors | Sort-Object
  exit 1
}

& git -C $root rev-parse --verify --quiet HEAD 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  & git -C $root lfs fsck
  if ($LASTEXITCODE -ne 0) { throw 'git lfs fsck failed.' }
}
'check-asset-versioning: OK'
