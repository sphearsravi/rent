# Fix the mojibake bytes saved in source files. The corruption happened
# when something read UTF-8 bytes as Windows-1252 and re-encoded back to
# UTF-8 — `≤` (E2 89 A4) became the 3-character sequence `â‰¤` and got
# persisted that way in the file. This script finds the known corrupt
# patterns, replaces them with the intended character, and rewrites each
# file as UTF-8 with BOM so Razor + browsers + Windows tooling all read
# the same encoding next time.
#
# Usage:
#   pwsh scripts/fix-mojibake.ps1                # fix all .cshtml/.css/.js under repo root
#   pwsh scripts/fix-mojibake.ps1 -DryRun        # print what would change without writing
#   pwsh scripts/fix-mojibake.ps1 -Verbose       # show each replacement applied per file
[CmdletBinding()]
param(
  [string]   $Root  = (Split-Path -Parent $PSScriptRoot),
  [string[]] $Globs = @('*.cshtml', '*.css', '*.js', '*.html'),
  [switch]   $DryRun
)

# Map of corrupted-bytes-as-string → intended character. Each entry uses
# explicit Unicode escapes so this very script doesn't get re-corrupted.
# Pattern format: the 2- or 3-character mojibake sequence as it appears
# when the corrupted file is read as UTF-8.
$pairs = @(
  # ≤ U+2264 — originally E2 89 A4. Mis-decoded as W-1252: â ‰ ¤. Re-encoded UTF-8: C3 A2 E2 80 B0 C2 A4.
  @{ Bad = [string]([char]0x00E2 + [char]0x2030 + [char]0x00A4); Good = [string][char]0x2264; Name = "less-equal" }
  # ≥ U+2265
  @{ Bad = [string]([char]0x00E2 + [char]0x2030 + [char]0x00A5); Good = [string][char]0x2265; Name = "greater-equal" }
  # — em-dash U+2014. Mis-decoded: â € ”. Re-encoded UTF-8: C3 A2 E2 82 AC E2 80 9D
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x201D); Good = [string][char]0x2014; Name = "em-dash" }
  # – en-dash U+2013. Mis-decoded: â € “
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x201C); Good = [string][char]0x2013; Name = "en-dash" }
  # ' right single quote U+2019. Mis-decoded: â € ™
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x2122); Good = [string][char]0x2019; Name = "right-single-quote" }
  # ' left single quote U+2018. Mis-decoded: â € ˜
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x02DC); Good = [string][char]0x2018; Name = "left-single-quote" }
  # " left double quote U+201C. Mis-decoded: â € œ
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x0153); Good = [string][char]0x201C; Name = "left-double-quote" }
  # " right double quote U+201D. Mis-decoded: â €
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x009D); Good = [string][char]0x201D; Name = "right-double-quote" }
  # … ellipsis U+2026. Mis-decoded: â € ¦
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x00A6); Good = [string][char]0x2026; Name = "ellipsis" }
  # • bullet U+2022. Mis-decoded: â € ¢
  @{ Bad = [string]([char]0x00E2 + [char]0x20AC + [char]0x00A2); Good = [string][char]0x2022; Name = "bullet" }
  # ─ box horizontal U+2500. Mis-decoded: â ” €
  @{ Bad = [string]([char]0x00E2 + [char]0x201D + [char]0x20AC); Good = [string][char]0x2500; Name = "box-horizontal" }
  # │ box vertical U+2502. Mis-decoded: â ” ‚
  @{ Bad = [string]([char]0x00E2 + [char]0x201D + [char]0x201A); Good = [string][char]0x2502; Name = "box-vertical" }
  # ✓ check U+2713. Mis-decoded: â œ "
  @{ Bad = [string]([char]0x00E2 + [char]0x0153 + [char]0x201C); Good = [string][char]0x2713; Name = "check-mark" }
  # ✗ cross U+2717
  @{ Bad = [string]([char]0x00E2 + [char]0x0153 + [char]0x2014); Good = [string][char]0x2717; Name = "cross-mark" }
  # ← left arrow U+2190
  @{ Bad = [string]([char]0x00E2 + [char]0x2020 + [char]0x0090); Good = [string][char]0x2190; Name = "left-arrow" }
  # → right arrow U+2192
  @{ Bad = [string]([char]0x00E2 + [char]0x2020 + [char]0x2019); Good = [string][char]0x2192; Name = "right-arrow" }
  # · middle dot U+00B7. Mis-decoded: Â ·
  @{ Bad = [string]([char]0x00C2 + [char]0x00B7); Good = [string][char]0x00B7; Name = "middle-dot" }
  # © U+00A9
  @{ Bad = [string]([char]0x00C2 + [char]0x00A9); Good = [string][char]0x00A9; Name = "copyright" }
  # NBSP U+00A0 → keep as regular space
  @{ Bad = [string]([char]0x00C2 + [char]0x00A0); Good = " "; Name = "nbsp" }
)

# Collect target files
$files = Get-ChildItem -Path $Root -Recurse -Include $Globs -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.git|TestResults|wwwroot[\\/]lib)\\' }

Write-Host "Scanning $($files.Count) files under $Root" -ForegroundColor Cyan
Write-Host ("-" * 60)

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$totalFilesChanged = 0
$totalReplacements = 0
$perPatternCount = @{}

foreach ($file in $files) {
  $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
  $original = $content
  $changesInFile = 0
  $patternsHit = @()

  foreach ($pair in $pairs) {
    $bad = $pair.Bad
    $good = $pair.Good
    $name = $pair.Name
    # Count occurrences (string.Split returns N+1 parts where N is occurrence count)
    $occurrences = ($content.Length - $content.Replace($bad, '').Length) / $bad.Length
    if ($occurrences -gt 0) {
      $content = $content.Replace($bad, $good)
      $changesInFile += $occurrences
      $patternsHit += "$name($occurrences)"
      $perPatternCount[$name] = ($perPatternCount[$name] + $occurrences)
    }
  }

  if ($changesInFile -gt 0) {
    $totalFilesChanged++
    $totalReplacements += $changesInFile
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\','/')
    if ($DryRun) {
      Write-Host ("[would fix] {0,-3} : {1} -- {2}" -f $changesInFile, $rel, ($patternsHit -join ", ")) -ForegroundColor Yellow
    } else {
      [System.IO.File]::WriteAllText($file.FullName, $content, $utf8Bom)
      Write-Host ("[fixed]    {0,-3} : {1} -- {2}" -f $changesInFile, $rel, ($patternsHit -join ", ")) -ForegroundColor Green
    }
  }
}

Write-Host ""
Write-Host ("-" * 60)
if ($DryRun) {
  Write-Host "Dry run complete. Would change $totalFilesChanged file(s), $totalReplacements replacement(s)." -ForegroundColor Yellow
} else {
  Write-Host "Done. Changed $totalFilesChanged file(s), $totalReplacements replacement(s)." -ForegroundColor Green
}

if ($perPatternCount.Count -gt 0) {
  Write-Host ""
  Write-Host "By pattern:" -ForegroundColor Cyan
  $perPatternCount.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object {
    Write-Host ("  {0,-22} {1}" -f $_.Key, $_.Value)
  }
}
