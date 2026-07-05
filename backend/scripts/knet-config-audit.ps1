# Pre-deployment K-NET configuration audit.
#
# Verifies every piece of K-NET wiring that has to be right before tenants
# can pay. Run this script as part of the pre-deploy checklist (see
# RELEASE.md). Exits 0 if all green, non-zero if any check fails.
#
# What it checks:
#   1. Database: every Property with AcceptKNET=true has a complete set
#      of K-NET credentials (TranportalId/Pwd/Key + MID + AccountID)
#   2. Database: no two Properties share the same K-NET MID (routing
#      collision would split payments incorrectly)
#   3. Database: no orphaned PaymentIdempotency rows past their TTL
#   4. AppSettings: K-NET section is populated with non-placeholder values
#   5. AppSettings: Knet:UseSandbox matches the environment expectation
#   6. AppSettings: Knet:Enabled is true (or warn that it's killed)
#
# Usage:
#   pwsh scripts/knet-config-audit.ps1
#   pwsh scripts/knet-config-audit.ps1 -ExpectProduction         # asserts UseSandbox=false
#   pwsh scripts/knet-config-audit.ps1 -AppSettings path/to/file -DbConnectionString "..."
#
# Exit codes:
#   0  all clear
#   1  one or more BLOCKING issues
#   2  warnings only (non-blocking)
[CmdletBinding()]
param(
  [string] $DbConnectionString = "Server=$($env:COMPUTERNAME)\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;",
  [string] $AppSettings        = (Join-Path (Split-Path -Parent $PSScriptRoot) "RentCollection.Web\appsettings.json"),
  [switch] $ExpectProduction
)

$ErrorActionPreference = "Stop"
$blocking = 0
$warnings = 0

function Pass([string]$msg) { Write-Host "[OK]   $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow; $script:warnings++ }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red;   $script:blocking++ }

Write-Host "K-NET configuration audit" -ForegroundColor Cyan
Write-Host ("-" * 60)
Write-Host "DB:           $DbConnectionString"
Write-Host "AppSettings:  $AppSettings"
Write-Host "Expectation:  $(if ($ExpectProduction) { 'production (UseSandbox=false)' } else { 'sandbox/dev' })"
Write-Host ""

# ---------- DB checks ----------
Write-Host "[1/6] Property K-NET credentials..." -ForegroundColor Cyan
try {
  $conn = New-Object System.Data.SqlClient.SqlConnection($DbConnectionString)
  $conn.Open()
  $cmd = $conn.CreateCommand()
  # CASE expression — T-SQL doesn't support `IS NULL` as a boolean expression
  # outside of WHERE / CHECK contexts.
  $cmd.CommandText = @"
SELECT PropertyId, PropertyName,
       KnetTranportalId, KnetTranportalPwd, KnetTerminalResourceKey,
       KNETMID, KNETAccountID,
       CASE WHEN KnetTranportalId IS NULL OR KnetTranportalId = '' THEN 1 ELSE 0 END AS UsesDefaultCreds
FROM Properties
WHERE AcceptKNET = 1
ORDER BY PropertyId
"@
  $r = $cmd.ExecuteReader()
  $rows = New-Object System.Collections.ArrayList
  $script:anyBuildingRelyingOnDefaults = $false
  while ($r.Read()) {
    $usesDefaults = [int]$r["UsesDefaultCreds"] -eq 1
    if ($usesDefaults) { $script:anyBuildingRelyingOnDefaults = $true }
    [void]$rows.Add(@{
      PropertyId = $r["PropertyId"]
      Name = $r["PropertyName"]
      TranportalId = if ($r["KnetTranportalId"] -is [DBNull]) { $null } else { $r["KnetTranportalId"] }
      TranportalPwd = if ($r["KnetTranportalPwd"] -is [DBNull]) { $null } else { $r["KnetTranportalPwd"] }
      TerminalKey = if ($r["KnetTerminalResourceKey"] -is [DBNull]) { $null } else { $r["KnetTerminalResourceKey"] }
      Mid = if ($r["KNETMID"] -is [DBNull]) { $null } else { $r["KNETMID"] }
      AccountId = if ($r["KNETAccountID"] -is [DBNull]) { $null } else { $r["KNETAccountID"] }
      UsesDefaults = $usesDefaults
    })
  }
  $r.Close()

  if ($rows.Count -eq 0) {
    Warn "No buildings have AcceptKNET=true - K-NET payments are effectively disabled."
  }
  foreach ($row in $rows) {
    $missing = @()
    if (-not $row.TranportalId)  { $missing += "KnetTranportalId" }
    if (-not $row.TranportalPwd) { $missing += "KnetTranportalPwd" }
    if (-not $row.TerminalKey)   { $missing += "KnetTerminalResourceKey" }
    if (-not $row.Mid)           { $missing += "KNETMID" }
    if (-not $row.AccountId)     { $missing += "KNETAccountID" }
    if ($missing.Count -gt 0) {
      Fail ("Property {0} ({1}) - missing K-NET fields: {2}" -f $row.PropertyId, $row.Name, ($missing -join ", "))
    } else {
      Pass ("Property {0,-3} ({1}) - credentials complete" -f $row.PropertyId, $row.Name)
    }
  }

  # ---------- MID uniqueness ----------
  Write-Host ""
  Write-Host "[2/6] K-NET MID uniqueness..." -ForegroundColor Cyan
  $cmd.CommandText = @"
SELECT KNETMID, COUNT(*) AS dupes
FROM Properties
WHERE AcceptKNET = 1 AND KNETMID IS NOT NULL AND KNETMID <> ''
GROUP BY KNETMID
HAVING COUNT(*) > 1
"@
  $r = $cmd.ExecuteReader()
  $dupes = New-Object System.Collections.ArrayList
  while ($r.Read()) { [void]$dupes.Add(@{ Mid = $r["KNETMID"]; Count = $r["dupes"] }) }
  $r.Close()
  if ($dupes.Count -eq 0) {
    Pass "All K-NET MIDs are unique across buildings"
  } else {
    foreach ($d in $dupes) { Fail ("MID {0} used by {1} buildings - payments will be routed to wrong account" -f $d.Mid, $d.Count) }
  }

  # ---------- Idempotency hygiene ----------
  Write-Host ""
  Write-Host "[3/6] PaymentIdempotency hygiene..." -ForegroundColor Cyan
  $cmd.CommandText = "SELECT COUNT(*) FROM PaymentIdempotencies WHERE ExpiresAt < SYSUTCDATETIME() AND IsProcessed = 0"
  $orphans = [int]$cmd.ExecuteScalar()
  if ($orphans -eq 0) {
    Pass "No orphan idempotency rows past their TTL"
  } elseif ($orphans -le 10) {
    Warn "$orphans expired-but-unprocessed idempotency rows (cleanup recommended)"
  } else {
    Fail "$orphans expired-but-unprocessed idempotency rows - suggests Hangfire cleanup job is failing"
  }

  $conn.Close()
} catch {
  Fail "DB checks errored: $($_.Exception.Message)"
}

# ---------- AppSettings checks ----------
Write-Host ""
Write-Host "[4/6] appsettings.json K-NET section..." -ForegroundColor Cyan
if (-not (Test-Path $AppSettings)) {
  Fail "appsettings.json not found at: $AppSettings"
} else {
  $json = Get-Content $AppSettings -Raw | ConvertFrom-Json
  if (-not $json.Knet) {
    Fail "appsettings.json has no 'Knet' section"
  } else {
    $knet = $json.Knet
    # URLs are always required — the controller reads them regardless of
    # building overrides. Credentials are required ONLY when at least one
    # building doesn't have its own; otherwise they're a downgrade-to-WARN.
    $alwaysRequired = @("PaymentApiUrl", "SuccessUrl", "ErrorUrl")
    foreach ($f in $alwaysRequired) {
      if (-not $knet.$f -or $knet.$f -match '<.*>|TODO|CHANGEME|placeholder') {
        Fail "Knet.$f is missing or a placeholder ('$($knet.$f)')"
      } else {
        Pass "Knet.$f populated"
      }
    }
    $credFields = @("TranportalId", "TranportalPwd", "TerminalResourceKey")
    foreach ($f in $credFields) {
      $blank = (-not $knet.$f) -or ($knet.$f -match '<.*>|TODO|CHANGEME|placeholder')
      if ($blank) {
        if ($script:anyBuildingRelyingOnDefaults) {
          Fail "Knet.$f is missing and at least one building has no override - payments from that building will fail at the gateway"
        } else {
          Warn "Knet.$f is empty but every building has its own credentials - defaults unused"
        }
      } else {
        Pass "Knet.$f populated"
      }
    }

    # ---------- Sandbox / production expectation ----------
    Write-Host ""
    Write-Host "[5/6] Sandbox / production flag..." -ForegroundColor Cyan
    $useSandbox = if ($null -eq $knet.UseSandbox) { $true } else { [bool]$knet.UseSandbox }
    if ($ExpectProduction -and $useSandbox) {
      Fail "Knet.UseSandbox=true but -ExpectProduction was passed - this would route real payments to the sandbox gateway"
    } elseif (-not $ExpectProduction -and -not $useSandbox) {
      Warn "Knet.UseSandbox=false in a non-production audit run - verify this matches the target environment"
    } else {
      Pass "Knet.UseSandbox=$useSandbox matches expected mode"
    }

    # ---------- Kill switch ----------
    Write-Host ""
    Write-Host "[6/6] Kill switch state..." -ForegroundColor Cyan
    $enabled = if ($null -eq $knet.Enabled) { $true } else { [bool]$knet.Enabled }
    if (-not $enabled) {
      Warn "Knet.Enabled=false - kill switch is active. Tenants cannot initiate K-NET payments."
    } else {
      Pass "Knet.Enabled=true - payments are enabled"
    }
  }
}

# ---------- Summary ----------
Write-Host ""
Write-Host ("-" * 60)
if ($blocking -gt 0) {
  Write-Host "AUDIT FAILED - $blocking blocking issue(s), $warnings warning(s)." -ForegroundColor Red
  exit 1
} elseif ($warnings -gt 0) {
  Write-Host "AUDIT PASSED WITH WARNINGS - $warnings warning(s). Review before deploy." -ForegroundColor Yellow
  exit 2
} else {
  Write-Host "AUDIT PASSED - all K-NET configuration looks correct." -ForegroundColor Green
  exit 0
}
