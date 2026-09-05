---
project: DS_Communication
type: overview
status: draft
tags: [overview, moc]
updated: 2026-09-05
---

# Home — DS_Communication

연결형 통신 라이브러리 재작성 설계 문서의 시작점.

## 지금 상태

- **설계**: 앱이 Session 생성, Session-only 끊김, Converter IBufferWriter/Span, TCP/RUDP/IOCP, **재접속·하트비트는 앱**, TCP keep-alive 사용자 설정
- **구현 순서**: Shared → Test → TCP(+Sandbox) → RUDP(+Sandbox) → TCP_IOCP(+Sandbox) — [[Implementation-Roadmap]]
- **구현**: Shared + TCP + RUDP 완료 — 테스트 71 통과, `Sandbox/Chat.TCP`·`Sandbox/Chat.RUDP`(`--selftest` 포함) 실행 검증. TCP_IOCP 미착수
- **아카이브**: `Legacy/`

## 읽기 맵

1. [[../01-Overview/Scope|Scope]] · [[Feature-Spec]]
2. [[../02-Architecture/Overview|Overview]]
3. [[Code-Structure]]
4. [[../02-Architecture/Data-Flow|Data-Flow]]
5. [[../02-Architecture/Components|Components]]
6. [[Session]] · [[Pipeline]] · [[Channel]] · [[Handler]]
7. [[../03-Reference/Public-API|Public-API]] · [[../04-Guides/Getting-Started|Getting-Started]] · [[Implementation-Roadmap]] · [[../03-Reference/Packages|Packages]]
8. [[../04-Guides/Security|Security & Production Checklist]]
9. ADR [[0001-transport-channel-abstraction]] … [[0007-rudp-three-way-split-and-polling]]

AI·에이전트: [[../00-AI/CONTEXT|CONTEXT]]부터.

## 형제 스택

DS_RPC → DS_MessageProtocol + DS_Communication
