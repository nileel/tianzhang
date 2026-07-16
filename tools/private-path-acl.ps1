#requires -Version 7.0

function Get-PrivateAclSids {
  @(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
  )
}

function Set-PrivatePathAcl {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$Directory
  )

  $security = if ($Directory) {
    [Security.AccessControl.DirectorySecurity]::new()
  } else {
    [Security.AccessControl.FileSecurity]::new()
  }
  $security.SetAccessRuleProtection($true, $false)
  $inheritance = if ($Directory) {
    [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
  } else {
    [Security.AccessControl.InheritanceFlags]::None
  }
  foreach ($sid in Get-PrivateAclSids) {
    $rule = [Security.AccessControl.FileSystemAccessRule]::new(
      $sid,
      [Security.AccessControl.FileSystemRights]::FullControl,
      $inheritance,
      [Security.AccessControl.PropagationFlags]::None,
      [Security.AccessControl.AccessControlType]::Allow
    )
    $security.AddAccessRule($rule) | Out-Null
  }
  $fullPath = [IO.Path]::GetFullPath($Path)
  if ($Directory) {
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($fullPath), $security)
  } else {
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($fullPath), $security)
  }
}

function Assert-PrivatePathAcl {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$Directory
  )

  $acl = Get-Acl -LiteralPath $Path
  if (-not $acl.AreAccessRulesProtected) {
    throw 'Private path ACL is unsafe'
  }
  $allowed = @((Get-PrivateAclSids).Value)
  $rules = @($acl.Access)
  if ($rules.Count -ne 2) {
    throw 'Private path ACL is unsafe'
  }
  foreach ($rule in $rules) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    $expectedInheritance = if ($Directory) {
      [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    } else {
      [Security.AccessControl.InheritanceFlags]::None
    }
    if (
      $sid -notin $allowed -or
      $rule.IsInherited -or
      $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
      ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl -or
      $rule.InheritanceFlags -ne $expectedInheritance -or
      $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None
    ) {
      throw 'Private path ACL is unsafe'
    }
  }
}
