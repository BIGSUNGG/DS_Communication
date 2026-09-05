---
project: DS_Communication
type: overview
status: draft
tags: [overview, feature-spec]
updated: 2026-09-05
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
| F1-4 | RUDP 연결 | `RudpConnector`가 LiteNetLib 연결 + 전용 폴링 루프. **연결 키** 불일치 시 서버가 `AcceptIfKey`로 거부. 실패(거부·호스트 해석 불가·재시도 소진)는 `false` |
| F1-5 | RUDP 수락 | `RudpListener`가 peer 수락 → `Accepted(IMessageChannel)`; 수신은 호스트의 peer 등록부가 채널로 분배 (F4-3) |
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
| F3-2 | TCP 프레이밍 | length-prefix(4B LE) + payload 단일 write; 송신 coalesce 배치; 직렬화·프레임 검증 실패는 **해당 항목만 격리**(플러시 예외 완료)하고 송신 계속 — 끊김으로 격상 안 함 |
| F3-3 | 백프레셔 | 송신/Handler 큐 상한(`MaxPendingMessages`, 기본 `10_000`); 상한 도달 시 **공간 날 때까지 비동기 대기**. 한계: 메시지 채널(`IMessageChannel`) 수신은 콜백 차단 방지를 위해 슬롯 대기를 비동기로 넘겨, 핸들러가 밀리면 대기자가 상한 넘어 누적 가능(바이트 채널은 상한 유지) |
| F3-4 | 핸들러 디스패치 | 타입→핸들러 등록 디스패치; **미등록 타입은 skip**(예외 아님); 핸들러 `Action` 예외는 **Trace 후 수신 루프 계속** |
| F3-5 | 디스패치 모드 | 내부 큐 / `InlineDispatch`(수신 경로 동기 디스패치) 선택 — 기본 **내부 큐**(`InlineDispatch=false`, 레거시 동일) |
| F3-6 | 수신 버퍼 | 수신 버퍼 풀 렌탈/반환(ArrayPool 등) |
| F3-7 | 웨이크업 | 연속 신호에도 안전 단일 시그널 게이트(레거시 `SignalGate` 역할) |
| F3-8 | 정리 순서 | Dispose 시 Cancel → 신호; 루프는 자체 스레드에서 Dispose가 호출될 수 있어 조인 없이 취소·채널 닫기로 탈출 보장 (레거시 '대기'는 앱 스레드 Dispose 전용이었음) |
| F3-9 | 수신 프레임 마감 | `FrameTimeout`(기본 `30초`, `null`/`0` 비활성화): 프레임 첫 바이트 도착 시 시작, 마감 내 미완성 시 `DisconnectReason.Timeout` 단절 — 슬로로리스(부분 프레임 끌어안기) 방어. 완전 유휴 연결은 대상 아님(하트비트는 앱 책임) |

## F4. 전송별 기능

| ID | 기능 | 스펙 |
| ---- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| F4-1 | TCP keep-alive | `SocketKeepAliveOptions { Enabled, IdleTime, Interval }` — 사용자 설정, 미설정은 OS 기본 ([[../03-Reference/Configuration]]) |
| F4-2 | RUDP 신뢰성 선택 | 메시지별 전송 방식 선택 — `RudpSendOptions`(불변) + `RudpDeliveryMethod` 5값(레거시 `MessageSendContext`/`ReliableType` 대응). 기본 `ReliableOrdered`, 전송 방식별 공용 인스턴스로 송신 할당 0 ([[../03-Reference/Public-API]]) |
| F4-3 | RUDP 수신 분배 | peer → 채널 등록부(`RudpNetHost`)가 `MessageReceived`로 분배(레거시 `RUDPNetworkReceiveDispatcher` 역할) |
| F4-4 | RUDP poll | 호스트당 **전용 폴링 스레드 1개**, 간격 고정 1ms(옵션 아님). 스레드 수는 접속 수와 무관 — [[../05-Decisions/0007-rudp-three-way-split-and-polling]] |
| F4-5 | RUDP MTU 가드 | 분할 불가 방식(`Sequenced`·`ReliableSequenced`·`Unreliable`)으로 MTU 초과 payload 송신 시 `ArgumentException` — 조용한 유실 대신 즉시 실패 (**신규**, 레거시 없음) |

## F5. 플랫폼·패키지

| ID | 기능 | 스펙 |
| ---- | -------- | ---------------------------------------------------------------------------------------------------------- |
| F5-1 | 대상 프레임워크 | `netstandard2.1` (Unity 호환), nullable·XML 문서 |
| F5-2 | 패키지 구성 | TCP·RUDP는 **Shared/Server/Client 3분할**(서버·클라이언트 독립 설치), TCP_IOCP·IPC는 1 패키지. 초기 「전송당 1 패키지」 규칙은 폐기 |
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
| 레거시 네임스페이스 별칭(`RUDP.Client` 등 프로젝트별) | 3분할은 유지하되 **네임스페이스는 스택당 하나**(`Communication.Network.RUDP`) + `InternalsVisibleTo` | [[../00-AI/CONVENTIONS]] · [[../05-Decisions/0007-rudp-three-way-split-and-polling]] |
| 라이브러리 재접속·하트비트 | 없음 — 앱 책임 | ADR 0003 · [[../01-Overview/Scope]] |
| 생성자 노출 `pollIntervalMs` | 옵션 아님 — 고정 1ms 전용 폴링 스레드 | [[../03-Reference/Configuration]] |
| 레거시 `ReliableType`(byte enum) | `RudpDeliveryMethod` — LiteNetLib `DeliveryMethod`와 같은 이름·값, 공개면은 자체 enum | [[../05-Decisions/0007-rudp-three-way-split-and-polling]] |

## 구현 상태 (2026-09-05)

| 영역 | 상태 |
| ---- | ---- |
| F1-1·F1-2·F1-6 TCP 연결·수락·취소 | 구현 — `TcpConnector`·`TcpListener`, loopback 테스트 통과 |
| F1-4·F1-5 RUDP 연결·수락 | 구현 — `RudpConnector`·`RudpListener`(연결 키·`MaxConnections` 슬롯 예약), loopback 테스트 통과 |
| F2 세션 수명·송신 (F2-1~F2-6) | 구현 — 끊김 후 송신 faulted Task 포함. RUDP는 `RudpSession`이 peer 끊김 통지를 `Disconnected`로 이어 붙임 |
| F3 메시지 파이프라인 (F3-1~F3-9) | 구현 — `IBufferWriter` Converter, 백프레셔 대기, 핸들러 예외 격리, 직렬화 실패 항목 격리. `IMessageChannel` 경로(F3-2 프레이밍 제외) RUDP에서 사용 |
| F4-1 TCP keep-alive | 구현 — Windows IOControl / Unix 원시 옵션, 미지원 필드 무시 |
| F4-2~F4-5 RUDP 전송 옵션·분배·poll·MTU 가드 | 구현 — `RudpSendOptions`/`RudpDeliveryMethod` 5방식 왕복 테스트, 폴링 스레드 1개, 분할 불가 방식 MTU 초과 `ArgumentException` 테스트 |
| F5 플랫폼·패키지 | 구현 — netstandard2.1, TCP·RUDP 3분할, `IByteChannel`+`IMessageChannel` |
| F6 검증 (Shared·TCP·RUDP 범위) | 충족 — `Test/Communication.Tests` **71건 통과**(Shared·TCP 66 + RUDP loopback 5), `Sandbox/Chat.TCP`·`Sandbox/Chat.RUDP`(`--selftest` 5/5 왕복, exit 0) 실행 확인 |
| F1-3 (TCP_IOCP), F4-1의 IOCP keep-alive | 미착수 — 로드맵 5단계 |

## 관련

- [[../01-Overview/Scope|Scope]] · [[../01-Overview/Home|Home]]
- [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]] · [[../03-Reference/Public-API|Public-API]] · [[../03-Reference/Configuration|Configuration]]
- [[../02-Architecture/Session|Session]] · [[../02-Architecture/Pipeline|Pipeline]] · [[../02-Architecture/Handler|Handler]]
