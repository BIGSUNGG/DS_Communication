---
project: DS_Communication
type: overview
status: draft
tags: [overview, moc]
updated: 2026-09-09
---

# Home — DS_Communication

연결형 통신 라이브러리 재작성 설계 문서의 시작점.

## 지금 상태

- **설계**: 앱이 Session 생성, Session-only 끊김(늦은 구독자 즉시 재생 보장), Converter IBufferWriter/Span, TCP/RUDP, **재접속·하트비트는 앱**, TCP keep-alive 사용자 설정
- **전송 보안(옵션)**: TCP TLS(SslStream, 2.1.0+) · RUDP CRC32c 무결성(2.2.0+) · 기본 연결 키 시작 경고(2.3.0+) — [[../04-Guides/Getting-Started|Getting-Started]] §6
- **구현 순서**: Shared → Test → TCP(+Sandbox) → RUDP(+Sandbox) → TCP_IOCP — [[Implementation-Roadmap]]
- **구현**: Shared + TCP + RUDP 완료 — 테스트 **134** 통과(결정적 스위트), NuGet **2.4.0** 배포(7패키지), `Sandbox/Chat.TCP`·`Chat.RUDP`(`--selftest` 포함) 실행 검증. TCP_IOCP는 **니즈·벤치마크 근거 확보 전 보류**(2026-09-08)
- **감사·인계**: [[Audit-Full-Scan|5개 영역 전수 감사]] · [[Proposals-Upstream|DS_RPC 활용 제안]]
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
9. ADR [[0001-transport-channel-abstraction]] … [[0007-rudp-three-way-split-and-polling]] · [[0008-tcp-tls-sslstream|0008 TCP TLS]]

AI·에이전트: [[../00-AI/CONTEXT|CONTEXT]]부터.

## 형제 스택

DS_RPC → DS_MessageProtocol + DS_Communication
