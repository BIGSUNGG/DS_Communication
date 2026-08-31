---
project: DS_Communication
type: overview
status: draft
tags: [overview, moc]
updated: 2026-08-31
---

# Home — DS_Communication

연결형 통신 라이브러리 재작성 설계 문서의 시작점.

## 지금 상태

- **설계**: 앱이 Session 생성, Session-only 끊김, Converter IBufferWriter/Span, TCP/RUDP/IOCP, **재접속·하트비트는 앱**, TCP keep-alive 사용자 설정
- **구현 순서**: Shared → Test → TCP(+Sandbox) → RUDP(+Sandbox) → TCP_IOCP(+Sandbox) — [[Implementation-Roadmap]]
- **구현**: Shared + TCP 완료 — 테스트 23 통과, `Sandbox/Chat.TCP` 실행 검증. RUDP·TCP_IOCP 미착수
- **아카이브**: `Legacy/`

## 읽기 맵

1. [[Scope]] · [[Feature-Spec]]
2. [[Overview]]
3. [[Code-Structure]]
4. [[Data-Flow]]
5. [[Components]]
6. [[Session]] · [[Pipeline]] · [[Channel]] · [[Handler]]
7. [[Public-API]] · [[Getting-Started]] · [[Implementation-Roadmap]] · [[Packages]]
8. ADR [[0001-transport-channel-abstraction]] … [[0006-session-ownership-and-converter]]

AI·에이전트: [[CONTEXT]]부터.

## 형제 스택

DS_RPC → DS_MessageProtocol + DS_Communication
