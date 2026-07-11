# Shared helpers for Document-sync hooks. Dot-source from other scripts.

$script:HooksDir = $PSScriptRoot
$script:RepoRoot = (Resolve-Path (Join-Path $HooksDir "..\..")).Path
$script:StatePath = Join-Path $HooksDir ".session-edits.json"

$script:CodePattern = '(?i)^(Source|Test|Sandbox|Examples|TemplateSource)(/|\\)'
$script:DocPattern = '(?i)^Document(/|\\)'

function Get-RelativeRepoPath {
  param([string]$AbsolutePath)
  if ([string]::IsNullOrWhiteSpace($AbsolutePath)) { return $null }
  try {
    $full = [System.IO.Path]::GetFullPath($AbsolutePath)
    $root = [System.IO.Path]::GetFullPath($script:RepoRoot)
    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
      return $full.Substring($root.Length).TrimStart('\', '/')
    }
  } catch {}
  return $AbsolutePath
}

function Test-CodePath([string]$RelPath) {
  if ([string]::IsNullOrWhiteSpace($RelPath)) { return $false }
  return $RelPath -match $script:CodePattern
}

function Test-DocumentPath([string]$RelPath) {
  if ([string]::IsNullOrWhiteSpace($RelPath)) { return $false }
  return $RelPath -match $script:DocPattern
}

function Read-SessionState {
  $empty = [ordered]@{ code = @(); document = @(); updatedAt = (Get-Date).ToString("o") }
  if (-not (Test-Path $script:StatePath)) { return $empty }
  try {
    $raw = Get-Content -Raw -Path $script:StatePath -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return $empty }
    $obj = $raw | ConvertFrom-Json
    $code = @()
    $doc = @()
    if ($obj.code) { $code = @($obj.code) }
    if ($obj.document) { $doc = @($obj.document) }
    return [ordered]@{ code = $code; document = $doc; updatedAt = $obj.updatedAt }
  } catch {
    return $empty
  }
}

function Write-SessionState($State) {
  $payload = [ordered]@{
    code       = @($State.code | Select-Object -Unique)
    document   = @($State.document | Select-Object -Unique)
    updatedAt  = (Get-Date).ToString("o")
  }
  $json = $payload | ConvertTo-Json -Compress
  [System.IO.File]::WriteAllText($script:StatePath, $json, [System.Text.UTF8Encoding]::new($false))
}

function Reset-SessionState {
  Write-SessionState ([ordered]@{ code = @(); document = @() })
}

function Add-SessionEdit([string]$RelPath) {
  if ([string]::IsNullOrWhiteSpace($RelPath)) { return }
  $state = Read-SessionState
  $code = [System.Collections.Generic.List[string]]::new()
  $doc = [System.Collections.Generic.List[string]]::new()
  foreach ($p in @($state.code)) { if ($p) { [void]$code.Add([string]$p) } }
  foreach ($p in @($state.document)) { if ($p) { [void]$doc.Add([string]$p) } }
  if (Test-CodePath $RelPath) {
    if (-not $code.Contains($RelPath)) { [void]$code.Add($RelPath) }
  }
  if (Test-DocumentPath $RelPath) {
    if (-not $doc.Contains($RelPath)) { [void]$doc.Add($RelPath) }
  }
  Write-SessionState ([ordered]@{ code = $code.ToArray(); document = $doc.ToArray() })
}

function Read-HookInput {
  $raw = [Console]::In.ReadToEnd()
  if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
  try { return ($raw | ConvertFrom-Json) } catch { return $null }
}

function Write-HookJson($Object) {
  $json = $Object | ConvertTo-Json -Compress -Depth 8
  [Console]::Out.Write($json)
}