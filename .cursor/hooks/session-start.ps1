param()
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "_lib.ps1")

try {
  $inputObj = Read-HookInput
  Reset-SessionState

  $display = "DS_Communication"

  $ctx = @"
[$display] Document vault sync is mandatory for this repo.
Before code/test work, read Document/00-AI/CONTEXT.md (then GLOSSARY / Architecture / Reference as needed).
After changing Source/, Test/, Sandbox/, Examples/, or TemplateSource/, update Document/ in the same turn (Packages, Components, Public-API, Data-Flow, Scope, ADR, FAQ as applicable).
Human MOC: Document/01-Overview/Home.md. Follow skill ds-document-vault and rule document-sync.
"@

  Write-HookJson ([ordered]@{
    env = [ordered]@{
      DS_DOCUMENT_ROOT = "Document"
      DS_DOCUMENT_CONTEXT = "Document/00-AI/CONTEXT.md"
      DS_PROJECT_DISPLAY = $display
    }
    additional_context = $ctx
  })
} catch {
  Write-HookJson ([ordered]@{})
}