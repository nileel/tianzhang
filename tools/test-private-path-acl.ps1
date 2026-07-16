#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'private-path-acl.ps1'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) {
  throw 'private-path-acl.ps1 is missing'
}

. $helper

$helperSource = Get-Content -Raw -LiteralPath $helper
if ($helperSource -match '(?m)\bSet-Acl\b') {
  throw 'private ACL helper must not use Set-Acl because it writes inherited SACL data'
}
if ($helperSource -notmatch '\[IO\.FileSystemAclExtensions\]::SetAccessControl') {
  throw 'private ACL helper must write only the descriptor access section'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$root = Join-Path $tempRoot ('tzg-private-acl-' + [guid]::NewGuid().ToString('N'))
$file = Join-Path $root 'private.json'

try {
  New-Item -ItemType Directory -Path $root | Out-Null
  [IO.File]::WriteAllText($file, '{}', [Text.UTF8Encoding]::new($false))

  Set-PrivatePathAcl -Path $root -Directory
  Set-PrivatePathAcl -Path $file
  Assert-PrivatePathAcl -Path $root -Directory
  Assert-PrivatePathAcl -Path $file

  if ((Get-Content -Raw -LiteralPath $file) -cne '{}') {
    throw 'ACL write changed file content'
  }

  'test-private-path-acl: OK'
} finally {
  $resolvedRoot = [IO.Path]::GetFullPath($root)
  $prefix = $tempRoot + [IO.Path]::DirectorySeparatorChar
  if (-not $resolvedRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing unsafe ACL test cleanup'
  }
  if (Test-Path -LiteralPath $resolvedRoot) {
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  }
}
