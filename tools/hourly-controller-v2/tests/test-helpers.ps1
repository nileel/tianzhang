#requires -Version 7.0

Set-StrictMode -Version Latest

function Assert-True {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  if (-not $Condition) {
    throw "$Label expected true"
  }
}

function Assert-False {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  if ($Condition) {
    throw "$Label expected false"
  }
}

function Assert-Equal {
  param(
    $Actual,
    $Expected,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  if ($Actual -cne $Expected) {
    throw "$Label expected <$Expected> but got <$Actual>"
  }
}

function Assert-Throws {
  param(
    [Parameter(Mandatory = $true)]
    [scriptblock]$Script,
    [Parameter(Mandatory = $true)]
    [string]$MessageLike,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  $caught = $null
  try {
    & $Script | Out-Null
  } catch {
    $caught = $_.Exception
  }

  if ($null -eq $caught) {
    throw "$Label expected an exception"
  }
  if ($caught.Message -notlike "*$MessageLike*") {
    throw "$Label expected message like <$MessageLike> but got <$($caught.Message)>"
  }
}

function Write-TestUtf8 {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$Value
  )

  $parent = Split-Path -Parent $Path
  [IO.Directory]::CreateDirectory($parent) | Out-Null
  [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}
