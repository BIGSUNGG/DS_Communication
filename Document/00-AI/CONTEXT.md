---
project: DS_Communication
type: context
status: draft
tags: [ai, entry]
updated: 2026-07-11
---

# CONTEXT — DS_Communication

> **AI: 이 vault를 다룰 때 먼저 이 파일을 읽는다.**

## 한 줄 요약

연결형 통신 전송 계층 재작성. Connector는 Channel만 열고 **앱이 Session 생성**. Converter는 **IBufferWriter / Span**. 끊김은 **Session `Disconnected(DisconnectReason)`만**. **재접속·하트비트는 앱**. TCP keep-alive는 **사용자 설정**. 스택: TCP → RUDP(LiteNetLib) → TCP_IOCP. 순서: [[Implementation-Roadmap]]. 지금은 설계 문서 단계.

## 저장소

- GitHub: https://github.com/BIGSUNGG/DS_Communication
- 문서 vault 루트: `Document/` (이 폴더가 Obsidian Vault)
- 아카이브: `Legacy/` (이전 TCP/TCP_IOCP/RUDP 스택·구 vault)

## 읽을 순서

1. [[CONTEXT]] (지금)
2. [[GLOSSARY]]
3. [[Scope]]
4. [[Overview]] (Architecture)
5. [[Code-Structure]]
6. [[Data-Flow]]
7. [[Components]] — 요약; 상세 [[Session]], [[Pipeline]], [[Channel]], [[Handler]]
8. [[Public-API]] · [[Getting-Started]] · [[Implementation-Roadmap]]
9. [[Packages]]
10. ADR — [[0001-transport-channel-abstraction]], [[0002-tcp-backend-selection]], [[0003-connection-lifecycle-options]], [[0004-send-options-and-handler-api]], [[0005-rudp-litenetlib-interim]], [[0006-session-ownership-and-converter]]
11. [[CONVENTIONS]]

## 목표 패키지 요약

| 패키지 | 설명 |
|--------|------|
| `Communication.Shared` | Session·Channel·Framing·Pipeline·`SendOptions`·`DisconnectReason` |
| `Communication.Network.TCP` | TcpClient/NetworkStream 기반 Connector·Listener·Session |
| `Communication.Network.TCP_IOCP` | SocketAsyncEventArgs 기반 Connector·Listener·Session |
| `Communication.Network.RUDP` | LiteNetLib 기반 (인터림); 이후 자체 RUDP는 별 프로젝트 |
| `Communication.IPC.Stream` | Named Pipe / UDS → `IByteChannel` (후속) |
| `Communication.IPC.SharedMemory` | `ISharedMemoryChannel` 어댑터 (후속) |

상세: [[Packages]], [[Overview]], [[Code-Structure]]

## 형제 프로젝트

- DS_Communication — 네트워크·IPC 전송
- DS_MessageProtocol — 메시지 직렬화
- DS_RPC — 분산 RPC (위 둘에 의존)

의존 방향: **DS_RPC → DS_MessageProtocol, DS_Communication**

## 관련 노트

- 사람용 시작: [[Home]]
- 사용 예시: [[Getting-Started]]
- 범위: [[Scope]]
- 규칙: [[CONVENTIONS]]
