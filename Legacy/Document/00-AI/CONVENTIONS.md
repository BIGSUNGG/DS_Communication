---
project: DS_Communication
type: context
status: stable
tags: [ai, conventions]
updated: 2026-07-11
---

# Conventions

이 Document vault와 코드 문서화에 공통으로 적용한다. 세 DS 프로젝트 Document 구조는 동일하다.

## Vault 구조

| 폴더 | 역할 |
|------|------|
| `00-AI/` | AI·에이전트 진입점 |
| `01-Overview/` | 사람용 Home / 범위 |
| `02-Architecture/` | 구조·컴포넌트·데이터 흐름 |
| `03-Reference/` | 패키지·API·설정 레퍼런스 |
| `04-Guides/` | 시작·How-To |
| `05-Decisions/` | ADR |
| `06-Troubleshooting/` | FAQ·장애 |
| `_meta/` | Changelog 등 메타 |

## Frontmatter

```yaml
---
project: DS_Communication
type: context|overview|architecture|reference|guide|adr|troubleshoot
status: stub|draft|stable
tags: []
updated: YYYY-MM-DD
---
```

## 링크

- Obsidian `[[WikiLink]]` 사용 (파일명 기준, 확장자 생략)
- 한 개념 = 한 파일
- AI는 [[CONTEXT]]에서 시작해 링크를 따라간다

## ADR

- 파일명: `NNNN-short-title.md` (예: `0001-use-rudp.md`)
- 템플릿: [[_Template]]
- Status / Context / Decision / Consequences 섹션 필수

## 작성 상태

- `stub`: 섹션만 있는 자리표시
- `draft`: 초안, 사실 검증 필요
- `stable`: 합의된 내용

## 관련

- [[CONTEXT]]
- [[Home]]