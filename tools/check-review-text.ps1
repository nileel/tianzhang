#requires -Version 7.0

param(
  [string[]]$Paths = @("AGENTS.md", "dev-mgmt"),
  [switch]$StrictResidual
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path
$findings = New-Object System.Collections.Generic.List[string]

function Get-RelativePath {
  param([string]$FullName)
  if ($FullName.StartsWith($root)) {
    return $FullName.Substring($root.Length).TrimStart("\", "/")
  }
  return $FullName
}

function Resolve-ProjectPath {
  param([string]$InputPath)

  if ($InputPath -eq "dev-mgmt") {
    $dev = (-join @([char]0x5F00, [char]0x53D1, [char]0x7BA1, [char]0x7406))
    return (Join-Path $root $dev)
  }
  return (Join-Path $root $InputPath)
}

function Resolve-TargetFiles {
  param([string[]]$InputPaths)

  $result = New-Object System.Collections.Generic.List[System.IO.FileInfo]
  $expanded = @()
  foreach ($raw in $InputPaths) {
    $expanded += @($raw -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
  }

  foreach ($item in $expanded) {
    $path = Resolve-ProjectPath $item
    if (Test-Path -Path $path -PathType Container) {
      Get-ChildItem -Path $path -Recurse -File -Include *.txt,*.md,*.csv,*.ps1 | ForEach-Object { $result.Add($_) }
    } elseif (Test-Path -Path $path -PathType Leaf) {
      $result.Add((Get-Item -Path $path))
    } else {
      $findings.Add("MISSING_PATH`t$item")
    }
  }
  return $result
}

$files = Resolve-TargetFiles -InputPaths $Paths

foreach ($file in $files) {
  $text = [System.IO.File]::ReadAllText($file.FullName)
  $relative = Get-RelativePath $file.FullName

  $line = 1
  foreach ($ch in $text.ToCharArray()) {
    $code = [int][char]$ch
    if ($code -eq 0 -or $code -eq 8 -or $code -eq 11 -or $code -eq 12) {
      $findings.Add("CONTROL_CHAR`t$relative`tline=$line`tchar=$code")
    }
    if ($ch -eq "`n") {
      $line++
    }
  }

  if ($StrictResidual) {
    $patterns = @(
      @{ Name = "BACKTICK_R"; Pattern = "``r" },
      @{ Name = "BACKTICK_N"; Pattern = "``n" },
      @{ Name = "LITERAL_CRLF"; Pattern = "\\r\\n" }
    )

    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
      foreach ($entry in $patterns) {
        if ($lines[$i] -match $entry.Pattern) {
          $findings.Add("$($entry.Name)`t$relative`tline=$($i + 1)`t$($lines[$i].Trim())")
        }
      }
    }
  }
}

if ($findings.Count -gt 0) {
  "check-review-text: FAILED"
  $findings | Sort-Object
  exit 1
}

"check-review-text: OK ($($files.Count) files checked)"
