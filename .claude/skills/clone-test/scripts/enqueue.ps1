<#
.SYNOPSIS
  Snapshot the current working-tree changes into a new queued job for the Test Clone runner.

  Each job is a self-contained folder under <repo>/.clone-test-queue/pending/ holding a manifest plus byte-for-byte
  copies of the selected files, so queuing several jobs never lets them clobber each other — the clone drains them one
  at a time and "goes Next".

.PARAMETER Label    Short human label for the job (becomes part of the folder name). Default: "job".
.PARAMETER Action   What the clone does after applying the files: "play" (default), "recompile", or "menu:<Menu/Path>".
.PARAMETER Filter   Optional substring; only changed paths containing it are included (e.g. "BotBrain").
.PARAMETER Session  Free-text tag for which session queued this (shown in the clone UI). Default: "claude".
#>
param(
  [string]$Label   = "job",
  [string]$Action  = "play",
  [string]$Filter  = "",
  [string]$Session = "claude"
)
$ErrorActionPreference = "Stop"

$root = (& git rev-parse --show-toplevel 2>$null)
if (-not $root) { Write-Error "Not inside a git repository."; exit 1 }
$root = $root.Trim()

$queue   = Join-Path $root ".clone-test-queue"
$pending = Join-Path $queue "pending"
New-Item -ItemType Directory -Force -Path $pending | Out-Null

# Next job number across every sub-queue, so numbering is monotonic and collision-free.
$nums = @()
foreach ($sub in "pending","processing","done","failed") {
  $d = Join-Path $queue $sub
  if (Test-Path $d) {
    Get-ChildItem $d -Directory | ForEach-Object {
      if ($_.Name -match '^(\d+)_') { $nums += [int]$Matches[1] }
    }
  }
}
$next = if ($nums.Count) { ($nums | Measure-Object -Maximum).Maximum + 1 } else { 1 }
$num  = "{0:D4}" -f $next

$safeLabel = ($Label -replace '[^A-Za-z0-9._-]', '-')
$jobName   = "${num}_$safeLabel"
$jobDir    = Join-Path $pending $jobName
$filesDir  = Join-Path $jobDir "files"
New-Item -ItemType Directory -Force -Path $filesDir | Out-Null

# Collect changed files from porcelain status (handles renames -> new path; deletions -> op "delete").
$entries = @()
$status = & git status --porcelain
foreach ($line in ($status -split "`n")) {
  $line = $line.TrimEnd("`r")
  if ($line.Length -lt 4) { continue }
  $rest = $line.Substring(3).Trim()
  if ($rest -match ' -> ') { $rest = ($rest -split ' -> ')[-1] }
  $rel = $rest.Trim('"')
  if ($Filter -and ($rel -notlike "*$Filter*")) { continue }

  $abs = Join-Path $root $rel
  if (Test-Path $abs) {
    $dst = Join-Path $filesDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item $abs $dst -Force
    $meta = "$abs.meta"
    if (Test-Path $meta) { Copy-Item $meta "$dst.meta" -Force }
    $entries += @{ path = $rel; op = "write" }
  } else {
    $entries += @{ path = $rel; op = "delete" }
  }
}

if ($entries.Count -eq 0) {
  Remove-Item $jobDir -Recurse -Force
  Write-Output "No changes to queue (filter='$Filter'). Nothing enqueued."
  exit 0
}

# Build manifest JSON by hand so the "files" array is always a JSON array (PS 5.1 ConvertTo-Json collapses single-item
# arrays into objects, which the Unity-side JsonUtility parser would reject).
function J([string]$s) { return ($s | ConvertTo-Json) }  # quoted + escaped JSON string
$fileLines = foreach ($e in $entries) { '    { "path": ' + (J $e.path) + ', "op": "' + $e.op + '" }' }
$filesBlock = if ($fileLines) { "`n" + ($fileLines -join ",`n") + "`n  " } else { "" }

$json = @"
{
  "id": $(J $jobName),
  "label": $(J $Label),
  "session": $(J $Session),
  "createdAt": $(J ((Get-Date).ToString("o"))),
  "action": $(J $Action),
  "files": [$filesBlock]
}
"@

# Write without a BOM (JsonUtility is picky about a leading BOM).
[System.IO.File]::WriteAllText((Join-Path $jobDir "manifest.json"), $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Output "Queued $jobName  ($($entries.Count) file(s), action=$Action)"
Write-Output "  -> $jobDir"
