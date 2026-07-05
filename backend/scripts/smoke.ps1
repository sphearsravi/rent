# RentCollection — post-deploy smoke test.
#
# Hits the readiness probe and a small set of canary endpoints. Exits 0 if
# everything looks healthy, non-zero otherwise. Wire into the deploy pipeline
# so a bad deploy never silently sits in production.
#
# Usage:
#   pwsh scripts/smoke.ps1                          # defaults to localhost:5032
#   pwsh scripts/smoke.ps1 -BaseUrl https://...     # other env
#   pwsh scripts/smoke.ps1 -TimeoutSec 10           # tighten the budget
#
# Exit codes:
#   0  — all checks passed
#   1  — /health/ready reported anything other than Healthy
#   2  — a canary endpoint failed
#   3  — could not reach the host
[CmdletBinding()]
param(
  [string] $BaseUrl    = "http://localhost:5032",
  [int]    $TimeoutSec = 5
)

$ErrorActionPreference = "Stop"

function Probe([string]$path, [int[]]$expectedStatusCodes) {
  $url = "$BaseUrl$path"
  try {
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = "GET"
    $req.AllowAutoRedirect = $false
    $req.Timeout = $TimeoutSec * 1000
    $resp = $req.GetResponse()
    $code = [int]$resp.StatusCode
    $body = ""
    try {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $body = $reader.ReadToEnd()
    } finally { $resp.Close() }
    return [pscustomobject]@{ Url = $url; Code = $code; Body = $body; Ok = $expectedStatusCodes -contains $code }
  } catch [System.Net.WebException] {
    $r = $_.Exception.Response
    if (-not $r) { return [pscustomobject]@{ Url = $url; Code = 0; Body = $_.Exception.Message; Ok = $false } }
    $code = [int]$r.StatusCode
    $body = ""
    try {
      $reader = New-Object System.IO.StreamReader($r.GetResponseStream())
      $body = $reader.ReadToEnd()
    } catch {}
    return [pscustomobject]@{ Url = $url; Code = $code; Body = $body; Ok = $expectedStatusCodes -contains $code }
  }
}

Write-Host "RentCollection smoke @ $BaseUrl"  -ForegroundColor Cyan
Write-Host ("-" * 60)

# 1. Readiness probe — runs the DB + Hangfire checks.
$ready = Probe "/health/ready" @(200)
if (-not $ready.Ok) {
  Write-Host "[FAIL] /health/ready returned $($ready.Code) (expected 200)" -ForegroundColor Red
  Write-Host $ready.Body
  exit 3
}
try {
  $json = $ready.Body | ConvertFrom-Json
} catch {
  Write-Host "[FAIL] /health/ready body is not valid JSON" -ForegroundColor Red
  Write-Host $ready.Body
  exit 1
}
if ($json.status -ne "Healthy") {
  Write-Host "[FAIL] /health/ready status = $($json.status)" -ForegroundColor Red
  foreach ($c in $json.checks) {
    Write-Host ("  - {0,-10} {1}  {2}" -f $c.name, $c.status, $c.description) -ForegroundColor Yellow
  }
  exit 1
}
Write-Host "[OK]   /health/ready    Healthy ($([math]::Round($json.totalDurationMs)) ms)" -ForegroundColor Green
foreach ($c in $json.checks) {
  Write-Host ("       - {0,-10} {1,-9} {2}" -f $c.name, $c.status, $c.description) -ForegroundColor DarkGray
}

# 2. Canary endpoints — should all redirect to /Account/Login (302) for an
#    unauthenticated request, proving routing + auth are wired.
$canaries = @(
  @{ Path = "/Reports";                 Expect = @(302) },
  @{ Path = "/Reports/MonthEndClose";   Expect = @(302) },
  @{ Path = "/Dashboard";               Expect = @(302) },
  @{ Path = "/Account/Login";           Expect = @(200) }
)
$failed = 0
foreach ($c in $canaries) {
  $r = Probe $c.Path $c.Expect
  if ($r.Ok) {
    Write-Host ("[OK]   {0,-22} HTTP {1}" -f $c.Path, $r.Code) -ForegroundColor Green
  } else {
    Write-Host ("[FAIL] {0,-22} HTTP {1} (expected one of {2})" -f $c.Path, $r.Code, ($c.Expect -join ",")) -ForegroundColor Red
    $failed++
  }
}

if ($failed -gt 0) {
  Write-Host ""
  Write-Host "$failed canary endpoint(s) failed." -ForegroundColor Red
  exit 2
}

Write-Host ""
Write-Host "All smoke checks passed." -ForegroundColor Green
exit 0
