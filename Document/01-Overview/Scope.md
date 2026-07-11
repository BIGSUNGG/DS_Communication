---
project: DS_Communication
type: overview
status: draft
tags: [overview, scope]
updated: 2026-07-11
---

# Scope

## 포함

- **연결형** 전송: Connect / Accept → 앱이 Session 생성
- Session·Pipeline·Channel·Framing·`SendOptions`
- **끊김**: `Disconnected(DisconnectReason)` — [[0003-connection-lifecycle-options]]
- **TCP keep-alive**: 사용자 설정 (`SocketKeepAliveOptions`) — TCP / TCP_IOCP
- **스택**: TCP → RUDP(LiteNetLib) → TCP_IOCP — [[Implementation-Roadmap]]

## 제외 (앱 책임)

| 영역 | 담당 |
|------|------|
| 메시지 스키마·직렬화 | DS_MessageProtocol (`IMessageConverter` 주입) |
| RPC | DS_RPC |
| **하트비트 (ping/pong)** | 앱 (일반 메시지) |
| **재접속·backoff·논리 유저 세션** | 앱 (Connect + `new Session` + 토큰/핸드셰이크) |
| 로드밸런싱·다중 엔드포인트 | 앱 |
| 자체 RUDP | 이후 별 프로젝트 |

## 관련

- [[Home]] · [[Overview]] · [[CONTEXT]]
- [[0003-connection-lifecycle-options]] · [[Public-API]] · [[Configuration]]
