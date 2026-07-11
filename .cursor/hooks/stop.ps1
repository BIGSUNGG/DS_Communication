param()
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "_lib.ps1")

try {
  $inputObj = Read-HookInput
  $status = if ($inputObj -and $inputObj.status) { [string]$inputObj.status } else { "completed" }
  $loopCount = 0
  if ($inputObj -and $null -ne $inputObj.loop_count) { $loopCount = [int]$inputObj.loop_count }

  if ($status -ne "completed" -or $loopCount -ge 1) {
    Write-HookJson ([ordered]@{})
    exit 0
  }

  $state = Read-SessionState
  $codeEdits = @($state.code)
  $docEdits = @($state.document)

  if ($codeEdits.Count -gt 0 -and $docEdits.Count -eq 0) {
    $list = ($codeEdits | Select-Object -First 8) -join ", "
    $msg = @"
Document sync required: code/test paths changed ($list) but Document/ was not updated in this session.
Read Document/00-AI/CONTEXT.md and skill ds-document-vault, then update the mapped notes (Packages/Components/Public-API/Data-Flow/Scope/FAQ/ADR as needed), frontmatter updated, and _meta/Changelog.md. Do this before finishing.
"@
    Write-HookJson ([ordered]@{ followup_message = $msg.Trim() })
  } else {
    Write-HookJson ([ordered]@{})
  }
} catch {
  Write-HookJson ([ordered]@{})
}