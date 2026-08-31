---
project: DS_Communication
type: overview
status: draft
tags: [overview, feature-spec]
updated: 2026-08-31
---

# Feature Spec — 레거시에서 이어받을 기능 명세

레거시 스택(`Legacy/`)이 지원하는 기능 중 새 구현으로 이어받을 항목의 스펙이다.
구현 완료 판단은 이 문서를 기준으로 한다. 범위 경계는 [[../01-Overview/Scope|Scope]], API 형태는 [[../03-Reference/Public-API|Public-API]], 순서는 [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]].

레거시 기준 자료: `Legacy/Document/03-Reference/Public-API.md`, `Configuration.md`, `06-Troubleshooting/Known-Issues.md`.

## F1. 연결·수락

| ID | 기능 | 스펙 |
| ---- | ------ | ------ |
| F1-1 | TCP 연결 | `TCPConnector`로 host/port 연결. Connector는 **Channel만 열고** Session 생성은 앱 책임 (ADR 0006, [[../05-Decisions/0006-session-ownership-and-converter]]) |
| F1-2 | TCP 수락 | `TCPListener`가 Accept → `IByteChannel` 전달(`Accepted`). 세션 생성은 앱 |
| F1-3 | TCP_IOCP 연결·수락 | SocketAsyncEventArgs 기반 Connect/Accept. 동작은 F1-1·F1-2와 동일 |
| F1-4 | RUDP 연결 | LiteNetLib 연결 + poll. **연결 키** 불일치 시 연결 거부(서버 `AcceptIfKey`) |
| F1-5 | RUDP 수락 | `RUDPListener` peer 수락; 수신은 단일 구독 디스패처로 분배 (F4-3) |
| F1-6 | 연결 취소 | `ConnectAsync`의 `CancellationToken` 지원 |

## F2. 세션 수명·송신

| ID | 기능 | 스펙 |
| ---- | ------ | ------ |
| F2-1 | 큐잉 송신 | `SendAsync(message, SendOptions?)` — fire-and-forget 큐잉 |
| F2-2 | 완료 대기 송신 | `SendAndFlushAsync` — 큐잉 후 **wire 기록까지** await |
| F2-3 | 끊김 통지 | `Disconnected(DisconnectedEventArgs)` 1회. `Reason = Local / Remote / Error`, Error면 `Exception` 포함 (ADR 0003, [[../05-Decisions/0003-connection-lifecycle-options]]) |
| F2-4 | 연결 상태 | `IsConnected()` = 로컬 끊김 플래그 AND transport 상태 |
| F2-5 | 명시 끊김 | `Disconnect()` — 로컬 주도, `Reason=Local` 통지 |
| F2-6 | 리소스 정리·끊김 후 송신 | Session/파이프라인 Dispose; 끊김·Dispose 후 송신은 **예외로 완료된 Task** 반환 (동기 throw 아님) |

재접속·하트비트 이벤트는 없다 — 앱이 `Disconnected` 후 Connect + 새 Session ([[../01-Overview/Scope|Scope]]).

## F3. 메시지 파이프라인

| ID | 기능 | 스펙 |
| ---- | ------ | ------ |
| F3-1 | Converter | `Serialize(object, IBufferWriter<byte>)` / `Deserialize(ReadOnlySpan<byte>)` — 송신 힙 할당 제거 (레거시 `byte[]` 계약 폐기) |
| F3-2 | TCP 프레이밍 | length-prefix(4B LE) + payload 단일 write; 송신 coalesce 배치 |
| F3-3 | 백프레셔 | 송신/Handler 큐 상한(`MaxPendingMessages`, 기본 `10_000`); 상한 도달 시 **공간 날 때까지 비동기 대기** |
| F3-4 | 핸들러 디스패치 | 타입→핸들러 등록 디스패치; **미등록 타입은 skip**(예외 아님); 핸들러 `Action` 예외는 **Trace 후 수신 루프 계속** |
| F3-5 | 디스패치 모드 | 내부 큐 / `InlineDispatch`(수신 경로 동기 디스패치) 선택 — 기본 **내부 큐**(`InlineDispatch=false`, 레거시 동일) |
| F3-6 | 수신 버퍼 | 수신 버퍼 풀 렌탈/반환(ArrayPool 등) |
| F3-7 | 웨이크업 | 연속 신호에도 안전 단일 시그널 게이트(레거시 `SignalGate` 역할) |
| F3-8 | 정리 순서 | Dispose 시 Cancel → 신호 → 대기 (레거시 `MessageHandler` 정리 방식 유지) |

## F4. 전송별 기능

| ID | 기능 | 스펙 |
| ---- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| F4-1 | TCP keep-alive | `SocketKeepAliveOptions { Enabled, IdleTime, Interval }` — 사용자 설정, 미설정은 OS 기본 ([[../03-Reference/Configuration]]) |
| F4-2 | RUDP 신뢰성 선택 | 메시지별 DeliveryMethod 선택(레거시 `MessageSendContext`/`ReliableType`, 기본 신뢰 순서 전송); `RudpSendOptions`로 전달 ([[../03-Reference/Public-API]]) |
| F4-3 | RUDP 수신 분배 | peer → Receiver 단일 구독 분배(레거시 `RUDPNetworkReceiveDispatcher` 역할) |
| F4-4 | RUDP poll | poll 간격 옵션(레거시 기본 1ms, `Task.Delay` 폴링) |

## F5. 플랫폼·패키지

| ID | 기능 | 스펙 |
| ---- | -------- | ---------------------------------------------------------------------------------------------------------- |
| F5-1 | 대상 프레임워크 | `netstandard2.1` (Unity 호환), nullable·XML 문서 |
| F5-2 | 패키지 구성 | 전송당 **1 패키지** (레거시 Client/Server/Shared 3분할 폐기) |
| F5-3 | 채널 추상 | `IByteChannel`로 전송 추상; 세션·파이프라인은 전송 비의존 (ADR 0001, [[../05-Decisions/0001-transport-channel-abstraction]]) |

## F6. 검증

| ID   | 기준        | 스펙                                        |
| ---- | --------- | ----------------------------------------- |
| F6-1 | Shared 단위 | 프레이밍·큐 백프레셔·끊김 플래그 등 순수 로직 테스트            |
| F6-2 | 전송 통합     | 스택별 loopback 연결·송수신·끊김 테스트(전송 완료 시점마다 추가) |
| F6-3 | Sandbox   | 스택별 채팅 샘플로 연결·메시지·끊김 수동 확인                |

순서·완료 조건 상세: [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]].

## 이어받지 않음 (레거시와 차이)

| 레거시 | 새 프로젝트 | 근거 |
| -------- | ------------- | ------ |
| `byte[] Serialize(object)` | `IBufferWriter`/`Span` | Known-Issues §3.1 · ADR 0006 |
| Client/Server/Shared 3분할 패키지·레거시 네임스페이스 별칭 | 전송당 1 패키지, canonical 네임스페이스만 | [[../00-AI/CONVENTIONS]] |
| 라이브러리 재접속·하트비트 | 없음 — 앱 책임 | ADR 0003 · [[../01-Overview/Scope]] |
| 생성자 노출 `pollIntervalMs` | `RudpTransportOptions`(구현 시 확정) | [[../03-Reference/Configuration]] |

## 구현 상태 (2026-08-31)

| 영역 | 상태 |
| ---- | ---- |
| F1-1·F1-2·F1-6 TCP 연결·수락·취소 | 구현 — `TcpConnector`·`TcpListener`, loopback 테스트 통과 |
| F2 세션 수명·송신 (F2-1~F2-6) | 구현 — 끊김 후 송신 faulted Task 포함 |
| F3 메시지 파이프라인 (F3-1~F3-8) | 구현 — `IBufferWriter` Converter, 백프레셔 대기, 핸들러 예외 격리 |
| F4-1 TCP keep-alive | 구현 — Windows IOControl / Unix 원시 옵션, 미지원 필드 무시 |
| F5 플랫폼·패키지 | 구현 — netstandard2.1, 전송당 1 패키지, `IByteChannel` |
| F6 검증 (Shared·TCP 범위) | 충족 — `Test/Communication.Tests` 23건 통과, `Sandbox/Chat.TCP` 실행 확인 |
| F1-3 (TCP_IOCP), F1-4·F1-5·F4-2~F4-4 (RUDP) | 미착수 — 로드맵 4~5단계 |
| F4-3 RUDP 수신 분배 등 | 미착수 |

## 관련

- [[../01-Overview/Scope|Scope]] · [[../01-Overview/Home|Home]]
- [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]] · [[../03-Reference/Public-API|Public-API]] · [[../03-Reference/Configuration|Configuration]]
- [[../02-Architecture/Session|Session]] · [[../02-Architecture/Pipeline|Pipeline]] · [[../02-Architecture/Handler|Handler]]
