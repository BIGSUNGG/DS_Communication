---
project: DS_Communication
type: context
status: stable
tags: [ai, conventions]
updated: 2026-09-05
---

# Conventions

이 Document vault와 코드 문서화에 공통으로 적용한다. 세 DS 프로젝트 Document 구조는 동일하다.

## Vault 구조

| 폴더 | 역할 |
| ------ | ------ |
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
| ------ | ------ | ----- |
| NuGet / 프로젝트 | `Communication.{영역}.{전송}[.{역할}]` | `Communication.Network.RUDP.Server` |
| Shared 계약 | `Communication.Shared.*` | `Communication.Shared.Sessions` |
| 전송 구현 | 네임스페이스는 **스택당 하나** — 3분할해도 `.Shared`/`.Server`/`.Client`가 같은 네임스페이스를 쓴다 | `Communication.Network.RUDP` |
| 전송당 패키지 수 | **서버·클라이언트가 갈리는 스택은 Shared/Server/Client 3분할**(서버·클라이언트 독립 설치), 나머지는 1개 | TCP·RUDP = 3분할 / TCP_IOCP·IPC = 1개 |

「스택당 1 패키지」·「3분할 금지」 초기 규칙은 **폐기**됐다 (TCP 2.0.0, RUDP 1.0.0). 레거시 `Communication.Network.TCP.Client` 형태와 같은 3분할로 돌아왔지만, 레거시와 달리 **네임스페이스는 스택당 하나로 유지**하고 `.Shared`의 `InternalsVisibleTo`로 내부 API를 공유한다 — [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]], [[../03-Reference/Packages|Packages]].

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
