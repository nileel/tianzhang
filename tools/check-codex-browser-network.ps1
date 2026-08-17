#requires -Version 7.0

[CmdletBinding()]
param(
  [ValidateRange(1, 30)]
  [int]$TimeoutSeconds = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$targetHost = 'ab.chatgpt.com'
$hostsPath = Join-Path $env:WINDIR 'System32\drivers\etc\hosts'

function New-FailedResult {
  param(
    [long]$DurationMs,
    [string]$Message
  )

  [pscustomobject][ordered]@{
    status = 'failed'
    durationMs = $DurationMs
    error = $Message
  }
}

function Get-ElapsedMilliseconds {
  param([Diagnostics.Stopwatch]$Stopwatch)

  $Stopwatch.Stop()
  return $Stopwatch.ElapsedMilliseconds
}

function Invoke-DohLookup {
  param(
    [string]$Provider,
    [string]$Uri
  )

  $stopwatch = [Diagnostics.Stopwatch]::StartNew()
  try {
    $response = Invoke-WebRequest -Uri $Uri -Headers @{ Accept = 'application/dns-json' } -NoProxy -TimeoutSec $TimeoutSeconds
    $content = if ($response.Content -is [byte[]]) {
      [Text.Encoding]::UTF8.GetString($response.Content)
    } else {
      [string]$response.Content
    }
    $payload = $content | ConvertFrom-Json
    $addresses = @(
      $payload.Answer |
        Where-Object { [int]$_.type -eq 1 } |
        ForEach-Object { [string]$_.data } |
        Sort-Object -Unique
    )
    $duration = Get-ElapsedMilliseconds $stopwatch
    if ($addresses.Count -eq 0) {
      return [pscustomobject][ordered]@{
        provider = $Provider
        status = 'failed'
        durationMs = $duration
        httpStatus = [int]$response.StatusCode
        addresses = @()
        error = 'DoH response contained no IPv4 answers.'
      }
    }
    return [pscustomobject][ordered]@{
      provider = $Provider
      status = 'ok'
      durationMs = $duration
      httpStatus = [int]$response.StatusCode
      addresses = $addresses
    }
  } catch {
    return [pscustomobject][ordered]@{
      provider = $Provider
      status = 'failed'
      durationMs = Get-ElapsedMilliseconds $stopwatch
      httpStatus = $null
      addresses = @()
      error = $_.Exception.Message
    }
  }
}

$hostsMatches = @(
  Select-String -LiteralPath $hostsPath -Pattern 'Codex temporary workaround|ab\.chatgpt\.com' -CaseSensitive:$false -ErrorAction Stop
)

$systemStopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
  $systemAddresses = @(
    [Net.Dns]::GetHostAddresses($targetHost) |
      ForEach-Object { $_.IPAddressToString } |
      Sort-Object -Unique
  )
  $systemLookup = [pscustomobject][ordered]@{
    status = 'ok'
    durationMs = Get-ElapsedMilliseconds $systemStopwatch
    addresses = $systemAddresses
  }
} catch {
  $systemLookup = New-FailedResult (Get-ElapsedMilliseconds $systemStopwatch) $_.Exception.Message
}

$dnsStopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
  $dnsAddresses = @(
    Resolve-DnsName -Name $targetHost -Type A -DnsOnly -ErrorAction Stop |
      Where-Object { $_.Type -eq 'A' } |
      ForEach-Object { [string]$_.IPAddress } |
      Sort-Object -Unique
  )
  $traditionalDns = [pscustomobject][ordered]@{
    status = 'ok'
    durationMs = Get-ElapsedMilliseconds $dnsStopwatch
    addresses = $dnsAddresses
  }
} catch {
  $traditionalDns = New-FailedResult (Get-ElapsedMilliseconds $dnsStopwatch) $_.Exception.Message
}

$dohResults = @(
  Invoke-DohLookup -Provider 'Cloudflare' -Uri "https://cloudflare-dns.com/dns-query?name=$targetHost&type=A"
  Invoke-DohLookup -Provider 'Google' -Uri "https://dns.google/resolve?name=$targetHost&type=A"
)

$tcpStopwatch = [Diagnostics.Stopwatch]::StartNew()
$tcpClient = [Net.Sockets.TcpClient]::new()
try {
  [void]$tcpClient.ConnectAsync($targetHost, 443).WaitAsync([TimeSpan]::FromSeconds($TimeoutSeconds)).GetAwaiter().GetResult()
  $tcp443 = [pscustomobject][ordered]@{
    status = 'ok'
    durationMs = Get-ElapsedMilliseconds $tcpStopwatch
    remoteAddress = ([Net.IPEndPoint]$tcpClient.Client.RemoteEndPoint).Address.ToString()
  }
} catch {
  $tcp443 = New-FailedResult (Get-ElapsedMilliseconds $tcpStopwatch) $_.Exception.Message
} finally {
  $tcpClient.Dispose()
}

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($null -eq $curl) {
  $httpsProbe = [pscustomobject][ordered]@{
    status = 'failed'
    error = 'curl.exe is unavailable.'
  }
} else {
  $format = '%{http_code}|%{remote_ip}|%{time_namelookup}|%{time_connect}|%{time_appconnect}|%{time_starttransfer}|%{time_total}'
  $curlOutput = & $curl.Source --noproxy '*' --silent --show-error --head --max-time $TimeoutSeconds --output NUL --write-out $format "https://$targetHost/" 2>&1
  $curlExit = $LASTEXITCODE
  $parts = ([string]$curlOutput).Trim().Split('|')
  if ($curlExit -eq 0 -and $parts.Count -eq 7) {
    $httpsProbe = [pscustomobject][ordered]@{
      status = 'ok'
      httpStatus = [int]$parts[0]
      remoteAddress = $parts[1]
      dnsMs = [Math]::Round(([double]$parts[2] * 1000), 2)
      tcpMs = [Math]::Round(([double]$parts[3] * 1000), 2)
      tlsMs = [Math]::Round(([double]$parts[4] * 1000), 2)
      firstByteMs = [Math]::Round(([double]$parts[5] * 1000), 2)
      totalMs = [Math]::Round(([double]$parts[6] * 1000), 2)
    }
  } else {
    $httpsProbe = [pscustomobject][ordered]@{
      status = 'failed'
      exitCode = $curlExit
      error = ([string]$curlOutput).Trim()
    }
  }
}

$report = [pscustomobject][ordered]@{
  target = $targetHost
  capturedAt = [DateTimeOffset]::Now.ToString('o')
  temporaryHostsWorkaround = [pscustomobject][ordered]@{
    present = $hostsMatches.Count -gt 0
    matchingLineCount = $hostsMatches.Count
  }
  systemLookup = $systemLookup
  traditionalDns = $traditionalDns
  doh = $dohResults
  tcp443 = $tcp443
  https = $httpsProbe
}

[Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 8))

$failed = @(
  $systemLookup.status
  $traditionalDns.status
  $tcp443.status
  $httpsProbe.status
  $dohResults | ForEach-Object { $_.status }
) -contains 'failed'

if ($failed) {
  exit 1
}
