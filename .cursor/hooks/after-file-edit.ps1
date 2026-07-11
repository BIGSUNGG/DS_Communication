param()
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "_lib.ps1")

try {
  $inputObj = Read-HookInput
  $abs = $null
  if ($inputObj -and $inputObj.file_path) { $abs = [string]$inputObj.file_path }
  $rel = Get-RelativeRepoPath $abs
  if ($rel) { Add-SessionEdit $rel }

  if ($rel -and (Test-CodePath $rel) -and -not (Test-DocumentPath $rel)) {
    Write-HookJson ([ordered]@{
      additional_context = "Code path edited: $rel. Sync Document/ this turn (CONTEXT mapping + ds-document-vault). Update Architecture/Reference notes and Changelog if structure or public API changed."
    })
  } else {
    Write-HookJson ([ordered]@{})
  }
} catch {
  Write-HookJson ([ordered]@{})
}