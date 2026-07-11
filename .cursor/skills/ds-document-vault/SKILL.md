---
name: ds-document-vault
description: Keep the project Document Obsidian vault synchronized with code, tests, and project structure. Use when writing or analyzing code, adding tests, changing packages or public APIs, updating architecture, or when the user mentions Document, vault, docs sync, or documentation updates.
---

# DS Document Vault

## When to use

- Any `Source/`, `Test/`, `Sandbox/`, `Examples/`, or `TemplateSource/` change
- Code analysis that discovers outdated or missing docs
- Explicit Document / vault / docs sync requests

## Checklist

Copy and track:

```
Document Sync:
- [ ] Read Document/00-AI/CONTEXT.md
- [ ] Classify change (API / structure / behavior / fix / guide)
- [ ] Update only the mapped notes (see reference.md)
- [ ] Refresh frontmatter status + updated
- [ ] Add one Changelog line if content changed
- [ ] Preserve [[WikiLink]] and CONVENTIONS
```

## Workflow

1. **Read** `Document/00-AI/CONTEXT.md`, then `GLOSSARY.md` if terms matter.
2. **Classify** the change:
   - **API** → Public-API (+ Packages if surface area changes)
   - **Structure** → Overview, Components, Scope, Packages
   - **Behavior** → Data-Flow (+ FAQ if failure modes change)
   - **Decision** → new ADR from `05-Decisions/_Template.md`
   - **Usage** → Getting-Started / How-To
3. **Edit minimally** — same turn as code; no drive-by doc rewrites.
4. **Frontmatter** — set `status` to `draft` or `stable`; set `updated` to today (`YYYY-MM-DD`).
5. **Changelog** — one bullet under `_meta/Changelog.md` for meaningful doc updates.
6. **CONTEXT** — update only if the one-line summary, package list, or sibling deps changed.

## Do not

- Invent architecture not present in code
- Duplicate long code dumps into Document
- Skip Document when only tests change if tests reveal new contracts or behavior

## Additional resources

- Mapping tables and ADR rules: [reference.md](reference.md)
- Vault conventions: `Document/00-AI/CONVENTIONS.md`