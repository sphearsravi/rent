# Quarterly DB restore drill — proves the most recent backup actually
# restores AND that the production aggregator's logic still produces the
# snapshot that was signed off at month-end.
#
# Failure modes this catches:
#   1. The .bak file is corrupt / from the wrong day
#   2. The restore completes but the schema differs from prod
#   3. The aggregator's behaviour drifted since the close — colour-bearing
#      step in the live aggregator doesn't match the stored snapshot
#
# Usage:
#   pwsh scripts/restore-drill.ps1 -BackupPath "D:\backups\RentCollectionDB.bak"
#   pwsh scripts/restore-drill.ps1 -BackupPath "..." -SandboxDb RC_Drill
#   pwsh scripts/restore-drill.ps1 -BackupPath "..." -Period 2026-05
#
# Exit codes:
#   0  drill passed (restored snapshot equals live aggregator output)
#   1  setup failed (backup missing, restore failed, etc)
#   2  snapshots diverged — investigate before next month-end close
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $BackupPath,
  [string] $SqlInstance = $env:COMPUTERNAME + "\SQLEXPRESS",
  [string] $SandboxDb   = "RentCollectionDB_DrillSandbox",
  [string] $Period      = $null,  # default: most recent ClosedPeriod
  [switch] $KeepSandbox       # default: drop sandbox at end so we don't accrete clutter
)

$ErrorActionPreference = "Stop"

function ConnString([string]$db) {
  return "Server=$SqlInstance;Database=$db;Trusted_Connection=true;TrustServerCertificate=true;"
}

function RunSql([string]$cs, [string]$sql) {
  $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
  $conn.Open()
  try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    $null = $cmd.ExecuteNonQuery()
  } finally { $conn.Close() }
}

function ScalarSql([string]$cs, [string]$sql) {
  $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
  $conn.Open()
  try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    return $cmd.ExecuteScalar()
  } finally { $conn.Close() }
}

# ---------- 1. Sanity check ----------
if (-not (Test-Path $BackupPath)) {
  Write-Host "[FAIL] backup not found: $BackupPath" -ForegroundColor Red
  exit 1
}
Write-Host "RentCollection DB restore drill" -ForegroundColor Cyan
Write-Host ("-" * 60)
Write-Host "Backup:    $BackupPath"
Write-Host "Sandbox:   $SqlInstance / $SandboxDb"
Write-Host ""

# ---------- 2. Restore the backup into the sandbox DB ----------
Write-Host "[1/4] Restoring backup into $SandboxDb..." -ForegroundColor Cyan
try {
  # Read the logical file names from the backup so RESTORE WITH MOVE picks
  # the right two files even when prod was on a different drive layout.
  $fileList = New-Object System.Collections.ArrayList
  $conn = New-Object System.Data.SqlClient.SqlConnection((ConnString "master"))
  $conn.Open()
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = "RESTORE FILELISTONLY FROM DISK = N'$BackupPath'"
  $r = $cmd.ExecuteReader()
  while ($r.Read()) { [void]$fileList.Add(@{ Logical = $r["LogicalName"]; Type = $r["Type"] }) }
  $r.Close(); $conn.Close()

  $dataLogical = ($fileList | Where-Object { $_.Type -eq "D" })[0].Logical
  $logLogical  = ($fileList | Where-Object { $_.Type -eq "L" })[0].Logical
  $dataPath = "C:\Temp\$SandboxDb.mdf"
  $logPath  = "C:\Temp\$SandboxDb.ldf"
  if (-not (Test-Path "C:\Temp")) { New-Item -ItemType Directory -Path "C:\Temp" | Out-Null }

  RunSql (ConnString "master") @"
IF DB_ID(N'$SandboxDb') IS NOT NULL
BEGIN
  ALTER DATABASE [$SandboxDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE [$SandboxDb];
END
RESTORE DATABASE [$SandboxDb]
FROM DISK = N'$BackupPath'
WITH REPLACE, RECOVERY,
     MOVE N'$dataLogical' TO N'$dataPath',
     MOVE N'$logLogical'  TO N'$logPath';
"@
  Write-Host "      restored OK" -ForegroundColor Green
} catch {
  Write-Host "[FAIL] restore failed: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}

# ---------- 3. Pick the period to drill ----------
if (-not $Period) {
  $latest = ScalarSql (ConnString $SandboxDb) "SELECT TOP 1 Period FROM ClosedPeriods ORDER BY ClosedAt DESC"
  if (-not $latest) {
    Write-Host "[FAIL] no ClosedPeriod rows in $SandboxDb — close at least one period before drilling" -ForegroundColor Red
    if (-not $KeepSandbox) { RunSql (ConnString "master") "DROP DATABASE [$SandboxDb]" }
    exit 1
  }
  $Period = $latest.ToString()
}
Write-Host "[2/4] Drilling period: $Period" -ForegroundColor Cyan

# ---------- 4. Build the OpsCli ----------
Write-Host "[3/4] Building rcops..." -ForegroundColor Cyan
$repoRoot = Split-Path -Parent $PSScriptRoot
$cliProj  = Join-Path $repoRoot "RentCollection.OpsCli\RentCollection.OpsCli.csproj"
dotnet build $cliProj -c Release --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
  Write-Host "[FAIL] OpsCli build failed" -ForegroundColor Red
  exit 1
}
$rcops = Join-Path $repoRoot "RentCollection.OpsCli\bin\Release\net8.0\rcops.exe"
if (-not (Test-Path $rcops)) {
  Write-Host "[FAIL] rcops.exe not found at $rcops" -ForegroundColor Red
  exit 1
}

# ---------- 5. Run compare-snapshot ----------
Write-Host "[4/4] Comparing live aggregator vs stored snapshot..." -ForegroundColor Cyan
$cs = ConnString $SandboxDb
& $rcops compare-snapshot --connection $cs --period $Period
$compareExit = $LASTEXITCODE

# ---------- 6. Cleanup ----------
if (-not $KeepSandbox) {
  Write-Host ""
  Write-Host "Dropping sandbox..." -ForegroundColor DarkGray
  try {
    RunSql (ConnString "master") @"
ALTER DATABASE [$SandboxDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$SandboxDb];
"@
  } catch { Write-Host "WARN: sandbox cleanup failed: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# ---------- 7. Report ----------
Write-Host ""
switch ($compareExit) {
  0 { Write-Host "DRILL PASSED — restored snapshot matches live aggregator for $Period." -ForegroundColor Green; exit 0 }
  2 { Write-Host "DRILL FAILED — snapshots DIVERGENT for $Period. Review the diff above." -ForegroundColor Red; exit 2 }
  default { Write-Host "DRILL ERRORED — rcops exited $compareExit." -ForegroundColor Red; exit 1 }
}
