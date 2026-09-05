---
project: DS_Communication
type: context
status: draft
tags: [ai, entry]
updated: 2026-09-05
---

# CONTEXT — DS_Communication

> **AI: 이 vault를 다룰 때 먼저 이 파일을 읽는다.**

## 한 줄 요약

연결형 통신 전송 계층 재작성. Connector는 Channel만 열고 **앱이 Session 생성**. Converter는 **IBufferWriter / Span**. 끊김은 **Session `Disconnected(DisconnectReason)`만**. **재접속·하트비트는 앱**. TCP keep-alive는 **사용자 설정**. 스택: TCP → RUDP(LiteNetLib) → TCP_IOCP. 순서: [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]. 로드맵 1~4단계 완료 — Shared·TCP·RUDP 구현, 테스트 85 통과, Sandbox/Chat.TCP·Chat.RUDP 검증. 송신 직렬화 실패는 항목 격리(끊김 아님). RUDP는 TCP처럼 **Shared/Server/Client 3분할**, LiteNetLib 2.1.4를 **RUDP.Shared에만** 참조해 타입을 공개면에서 은닉, **호스트당 전용 폴링 스레드 1개**(접속 수와 무관) + 세션별 디스패치 큐, 메시지별 `RudpSendOptions`/`RudpDeliveryMethod` — [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]]. 다음은 TCP_IOCP.

## 저장소

- GitHub: <https://github.com/BIGSUNGG/DS_Communication>
- 문서 vault 루트: `Document/` (이 폴더가 Obsidian Vault)
- 아카이브: `Legacy/` (이전 TCP/TCP_IOCP/RUDP 스택·구 vault)

## 읽을 순서

1. [[../00-AI/CONTEXT|CONTEXT]] (지금)
2. [[../00-AI/GLOSSARY|GLOSSARY]]
3. [[../01-Overview/Scope|Scope]]
4. [[../02-Architecture/Overview|Overview]] (Architecture)
5. [[../02-Architecture/Code-Structure|Code-Structure]]
6. [[../02-Architecture/Data-Flow|Data-Flow]]
7. [[../02-Architecture/Components|Components]] — 요약; 상세 [[../02-Architecture/Session|Session]], [[../02-Architecture/Pipeline|Pipeline]], [[../02-Architecture/Channel|Channel]], [[../02-Architecture/Handler|Handler]]
8. [[../03-Reference/Public-API|Public-API]] · [[../04-Guides/Getting-Started|Getting-Started]] · [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]
9. [[../03-Reference/Packages|Packages]]
10. [[../04-Guides/Security|Security & Production Checklist]] — 컨버터 안전 제약·프로덕션 하드닝 현황
11. ADR — [[../05-Decisions/0001-transport-channel-abstraction|0001-transport-channel-abstraction]], [[../05-Decisions/0002-tcp-backend-selection|0002-tcp-backend-selection]], [[../05-Decisions/0003-connection-lifecycle-options|0003-connection-lifecycle-options]], [[../05-Decisions/0004-send-options-and-handler-api|0004-send-options-and-handler-api]], [[../05-Decisions/0005-rudp-litenetlib-interim|0005-rudp-litenetlib-interim]], [[../05-Decisions/0006-session-ownership-and-converter|0006-session-ownership-and-converter]], [[../05-Decisions/0007-rudp-three-way-split-and-polling|0007-rudp-three-way-split-and-polling]]
12. [[../00-AI/CONVENTIONS|CONVENTIONS]]

## 목표 패키지 요약

| 패키지 | 설명 |
| -------- | ------ |
| `Communication.Shared` | Session·Channel·Framing·Pipeline·`SendOptions`·`DisconnectReason` (2.0.0 배포) |
| `Communication.Network.TCP.Shared` | TcpSession·StreamByteChannel·TcpTransportOptions (2.0.0 배포) |
| `Communication.Network.TCP.Server` | TcpListener 수락 루프 (2.0.0 배포) |
| `Communication.Network.TCP.Client` | TcpConnector 연결 (2.0.0 배포) |
| `Communication.Network.TCP_IOCP` | SocketAsyncEventArgs 기반 Connector·Listener·Session |
| `Communication.Network.RUDP.Shared` | RudpSession·RudpMessageChannel·RudpSendOptions/RudpDeliveryMethod·RudpTransportOptions·내부 RudpNetHost (LiteNetLib 2.1.4) |
| `Communication.Network.RUDP.Server` | RudpListener 수락 (호스트당 폴링 스레드 1개) |
| `Communication.Network.RUDP.Client` | RudpConnector 연결 |
| `Communication.IPC.Stream` | Named Pipe / UDS → `IByteChannel` (후속) |
| `Communication.IPC.SharedMemory` | `ISharedMemoryChannel` 어댑터 (후속) |

상세: [[../03-Reference/Packages|Packages]], [[../02-Architecture/Overview|Overview]], [[../02-Architecture/Code-Structure|Code-Structure]]

## 형제 프로젝트

- DS_Communication — 네트워크·IPC 전송
- DS_MessageProtocol — 메시지 직렬화
- DS_RPC — 분산 RPC (위 둘에 의존)

의존 방향: **DS_RPC → DS_MessageProtocol, DS_Communication**

## 관련 노트

- 사람용 시작: [[../01-Overview/Home|Home]]
- 기능 스펙(이어받을 레거시 기능): [[../01-Overview/Feature-Spec|Feature-Spec]]
- 사용 예시: [[../04-Guides/Getting-Started|Getting-Started]]
- 범위: [[../01-Overview/Scope|Scope]]
- 규칙: [[../00-AI/CONVENTIONS|CONVENTIONS]]
