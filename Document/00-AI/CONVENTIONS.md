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
| `02-Architecture/` | 구조·컴포넌트·데이터 흐름·코드 구조 |
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

## 패키지·네임스페이스 명명

| 영역 | 규칙 | 예 |
|------|------|-----|
| NuGet / 프로젝트 | `Communication.{영역}.{전송}` | `Communication.Network.TCP` |
| Shared 계약 | `Communication.Shared.*` | `Communication.Shared.Sessions` |
| 전송 구현 | `Communication.Network.{전송}` / `Communication.IPC.{종류}` | `Communication.Network.RUDP` |
| 전송당 패키지 수 | **1개** (Client/Server/Shared 3분할 금지) | TCP / TCP_IOCP / RUDP 각각 Connector+Listener |

레거시 `Communication.Network.TCP.Client` 형태는 재작성 목표에서 폐기한다. 호환이 필요하면 Legacy만 참고.

## ADR

- 파일명: `NNNN-short-title.md` (예: `0001-transport-channel-abstraction.md`)
- Status / Context / Decision / Consequences 섹션 필수

## 작성 상태

- `stub`: 섹션만 있는 자리표시
- `draft`: 초안, 사실 검증 필요 (설계 단계 포함)
- `stable`: 합의·코드와 동기화된 내용

## 설계 vs 구현

- 활성 소스가 비어 있어도 **합의된 목표 아키텍처**는 `draft`로 vault에 둔다.
- 구현이 vault와 어긋나면 같은 턴에 문서를 고친다.
- `Legacy/`는 참고용이며 활성 진실 소스가 아니다.

## 관련

- [[CONTEXT]]
- [[Home]]
