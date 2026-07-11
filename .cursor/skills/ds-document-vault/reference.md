# Document Vault Reference

## Vault layout

| Path | Role |
|------|------|
| `00-AI/CONTEXT.md` | AI entry; read first |
| `00-AI/GLOSSARY.md` | Domain terms |
| `00-AI/CONVENTIONS.md` | Frontmatter, links, ADR rules |
| `01-Overview/Home.md` | Human MOC |
| `01-Overview/Scope.md` | In/out of scope, sibling deps |
| `02-Architecture/Overview.md` | High-level structure |
| `02-Architecture/Components.md` | Package/assembly map |
| `02-Architecture/Data-Flow.md` | Runtime paths |
| `03-Reference/Packages.md` | NuGet / project packages |
| `03-Reference/Public-API.md` | Public entry points |
| `03-Reference/Configuration.md` | Build/runtime config |
| `04-Guides/Getting-Started.md` | Quick start |
| `04-Guides/How-To.md` | Task recipes |
| `05-Decisions/` | ADRs (`NNNN-title.md`) |
| `06-Troubleshooting/FAQ.md` | Pitfalls |
| `_meta/Changelog.md` | Document changelog |

## Code tree → Document

| Code area | Document targets |
|-----------|------------------|
| New/renamed project under `Source/` | Packages, Components, Overview, Scope |
| Public types / attributes / APIs | Public-API, Glossary (new terms) |
| Serialization / wire / protocol behavior | Data-Flow, Public-API |
| Client/server transport | Data-Flow, Components |
| `Test/` asserting contracts | Public-API or FAQ if behavior clarified |
| `Sandbox/` / `Examples/` samples | Getting-Started, How-To |
| `Directory.Build.props` versions/deps | Configuration, Scope (sibling deps) |
| Breaking change / trade-off | ADR under `05-Decisions/` |

## Sibling stack

```
DS_RPC → DS_MessageProtocol
DS_RPC → DS_Communication
```

Reflect dependency changes in `01-Overview/Scope.md` and `00-AI/CONTEXT.md`.

## ADR file name

`05-Decisions/NNNN-short-title.md` — copy `_Template.md` (Status / Context / Decision / Consequences).

## Frontmatter

```yaml
---
project: <repo>
type: context|overview|architecture|reference|guide|adr|troubleshoot
status: stub|draft|stable
tags: []
updated: YYYY-MM-DD
---
```

Links: Obsidian `[[WikiLink]]` by note name (no extension).