function Get-TzgUnicodeCodePointCount {
  param([Parameter(Mandatory = $true)][string]$Value)

  $count = 0
  for ($index = 0; $index -lt $Value.Length; $index++) {
    if (
      [char]::IsHighSurrogate($Value[$index]) -and
      $index + 1 -lt $Value.Length -and
      [char]::IsLowSurrogate($Value[$index + 1])
    ) {
      $index++
    }
    $count++
  }
  $count
}

function ConvertFrom-TzgAutomationCommitMessage {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Message,
    [string]$ExpectedTask,
    [string]$ExpectedState
  )

  $singleLine = '[^\r\n]+'
  $pattern = "\A(?<subject>$singleLine)\r?\n\r?\n" +
    "Automation: (?<automation>$singleLine)\r?\n" +
    "Task: (?<task>$singleLine)\r?\n" +
    "State: (?<state>$singleLine)\r?\n" +
    "Result: (?<result>$singleLine)\r?\n" +
    "Impact: (?<impact>$singleLine)\r?\n" +
    "Verify: (?<verify>$singleLine)\r?\n" +
    "Plain: (?<plain>$singleLine)\r?\n?\z"
  $messageMatch = [regex]::Match(
    $Message,
    $pattern,
    [Text.RegularExpressions.RegexOptions]::CultureInvariant
  )
  if (-not $messageMatch.Success) {
    throw 'Automation commit metadata format is invalid.'
  }

  $subject = $messageMatch.Groups['subject'].Value
  $automation = $messageMatch.Groups['automation'].Value
  $task = $messageMatch.Groups['task'].Value
  $state = $messageMatch.Groups['state'].Value
  $resultText = $messageMatch.Groups['result'].Value
  $impactText = $messageMatch.Groups['impact'].Value
  $verifyText = $messageMatch.Groups['verify'].Value
  $plainText = $messageMatch.Groups['plain'].Value

  foreach ($value in @(
      $subject,
      $automation,
      $task,
      $state,
      $resultText,
      $impactText,
      $verifyText,
      $plainText
    )) {
    if (
      [string]::IsNullOrWhiteSpace($value) -or
      $value -cne $value.Trim() -or
      $value.Length -gt 2000 -or
      $value -match '[\x00-\x1F\x7F]'
    ) {
      throw 'Automation commit metadata value is invalid.'
    }
  }
  if (
    $automation -cne 'tzg-hourly-controller' -or
    $state -cnotin @('completed', 'pending_review') -or
    (-not [string]::IsNullOrWhiteSpace($ExpectedTask) -and $task -cne $ExpectedTask) -or
    (-not [string]::IsNullOrWhiteSpace($ExpectedState) -and $state -cne $ExpectedState)
  ) {
    throw 'Automation commit metadata identity is invalid.'
  }

  $contracts = @(
    [pscustomobject]@{
      Text = $resultText
      Pattern = '^问题=(?<goal>.+?)；完成=(?<completed>.+)$'
      Groups = @('goal', 'completed')
      MaximumCodePoints = 1000
    },
    [pscustomobject]@{
      Text = $impactText
      Pattern = '^影响=(?<impact>.+?)；边界=(?<boundary>.+)$'
      Groups = @('impact', 'boundary')
      MaximumCodePoints = 1000
    },
    [pscustomobject]@{
      Text = $verifyText
      Pattern = '^验证=(?<verification>.+?)；后续=(?<next>.+)$'
      Groups = @('verification', 'next')
      MaximumCodePoints = 1000
    },
    [pscustomobject]@{
      Text = $plainText
      Pattern = '^发生=(?<plainHappened>.+?)；影响=(?<plainImpact>.+?)；需要=(?<plainAction>.+)$'
      Groups = @('plainHappened', 'plainImpact', 'plainAction')
      MaximumCodePoints = 200
    }
  )
  $parsed = [ordered]@{}
  foreach ($contract in $contracts) {
    $fieldMatch = [regex]::Match(
      [string]$contract.Text,
      [string]$contract.Pattern,
      [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    if (-not $fieldMatch.Success) {
      throw 'Automation commit metadata fields are invalid.'
    }
    foreach ($groupName in @($contract.Groups)) {
      $groupValue = $fieldMatch.Groups[$groupName].Value
      if (
        [string]::IsNullOrWhiteSpace($groupValue) -or
        (Get-TzgUnicodeCodePointCount -Value $groupValue) -gt [int]$contract.MaximumCodePoints
      ) {
        throw 'Automation commit metadata fields are invalid.'
      }
      $parsed[$groupName] = $groupValue
    }
  }

  [pscustomobject][ordered]@{
    Subject = $subject
    Automation = $automation
    Task = $task
    State = $state
    ResultText = $resultText
    ImpactText = $impactText
    VerifyText = $verifyText
    PlainText = $plainText
    Goal = [string]$parsed.goal
    Completed = [string]$parsed.completed
    Impact = [string]$parsed.impact
    Boundary = [string]$parsed.boundary
    Verification = [string]$parsed.verification
    Next = [string]$parsed.next
    PlainHappened = [string]$parsed.plainHappened
    PlainImpact = [string]$parsed.plainImpact
    PlainAction = [string]$parsed.plainAction
  }
}
