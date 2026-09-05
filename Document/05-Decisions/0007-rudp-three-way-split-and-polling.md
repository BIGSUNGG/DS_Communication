---
project: DS_Communication
type: adr
status: stable
tags: [adr, rudp, litenetlib, threading]
updated: 2026-09-05
---

# ADR 0007: RUDP 3분할 · 전용 폴링 루프 1개 · DeliveryMethod 은닉

## Status

Accepted

## Context

[[0005-rudp-litenetlib-interim]]이 RUDP를 LiteNetLib로 구현한다고 확정했지만, **패키지 구성**(1개 vs 3분할)과 **수신 스레딩 모델**은 정하지 않았다. 두 가지가 추가로 주어졌다.

1. TCP는 2.0.0에서 `Shared`/`Server`/`Client` 3분할했다(서버·클라이언트 독립 설치). 「스택당 1 패키지」 규칙은 TCP에서 이미 폐기됐다.
2. 서버가 여러 클라이언트를 받는다. LiteNetLib는 `NetManager` 인스턴스당 폴링 기반(`PollEvents()`)이고 `UnsyncedEvents=false` 기본값에서는 이벤트가 큐에 쌓인다 — 이 이벤트를 **누가 어떤 스레드에서** 드레인할지가 병목 구조를 결정한다.

또한 `IMessageChannel` 경로에는 바이트 채널 같은 수신 루프가 없어 **원격 끊김을 스스로 감지할 수 없다**. `Communication.Shared`는 수정하지 않는 것이 제약이다.

## Decision

1. **RUDP도 TCP 선례를 따라 3분할**한다 — `Communication.Network.RUDP.Shared`(RudpSession·RudpMessageChannel·RudpSendOptions/RudpDeliveryMethod·RudpTransportOptions·내부 `RudpNetHost`), `.Server`(RudpListener), `.Client`(RudpConnector). 네임스페이스는 셋 다 `Communication.Network.RUDP` 유지. Server·Client는 RUDP.Shared의 `InternalsVisibleTo`로 내부 `RudpNetHost`를 공유한다.
2. **LiteNetLib 2.1.4 고정**, `PackageReference`는 **RUDP.Shared에만** 둔다(Server·Client는 전이 참조). LiteNetLib 타입은 `RudpNetHost`·`RudpMessageChannel` 내부에서만 등장하고 **공개 API 시그니처에는 나타나지 않는다** — [[0005-rudp-litenetlib-interim]]의 은닉 원칙을 패키지 경계까지 강화.
3. **수신 스레딩 = 호스트당 전용 폴링 스레드 1개.** `UnsyncedEvents=false`(기본)를 유지해 이벤트를 큐에 모으고, 백그라운드 스레드 1개가 `PollEvents()` + 1ms 대기 루프를 돈다. 스레드 수는 **클라이언트 수와 무관하게 고정**이다. 수신 payload는 `IMessageChannel.MessageReceived` → `MessagePipeline`의 **세션별 디스패치 큐**로 넘어가므로 앱 핸들러는 폴링 스레드에서 실행되지 않는다 — 느린 클라이언트 1개가 다른 접속의 수신을 막지 못한다. 폴링 간격은 고정 1ms(옵션으로 노출하지 않음).
4. **전송 옵션은 메시지별로 지정한다.** `RudpDeliveryMethod` enum은 LiteNetLib `DeliveryMethod`와 **같은 이름·같은 값**(ReliableUnordered=0, Sequenced=1, ReliableOrdered=2, ReliableSequenced=3, Unreliable=4)을 갖고, `RudpSendOptions : SendOptions`(불변)로 감싸 노출한다. 매핑은 `RudpMessageChannel` 내부 switch. 옵션 미지정 시 기본은 `ReliableOrdered`. 전송 방식별 **공용 정적 인스턴스 5개**를 제공해 송신 경로에서 옵션 할당을 0으로 만든다([[0004-send-options-and-handler-api]]의 「기본 송신은 할당 없음」 원칙).
5. **MTU 가드**: 분할(fragmentation)이 불가능한 방식(Sequenced·ReliableSequenced·Unreliable)으로 `peer.GetMaxSinglePacketSize(method)`를 넘는 payload를 보내면 `SendAsync`가 `ArgumentException`을 던진다 — 조용한 유실 대신 즉시 실패. 분할 가능 방식(ReliableOrdered·ReliableUnordered)은 제한하지 않는다.
6. **수락 공개면은 `RudpListener.Accepted(IMessageChannel)` 하나**(TCP와 완전 대칭, [[0006-session-ownership-and-converter]]의 「앱이 Session 생성」 유지). peer 접속·끊김 추적과 접속 수 관리는 리스너 내부에서만 한다. 상한은 `OnConnectionRequest`에서 **수락 전에 슬롯을 예약**해 같은 폴링 배치의 여러 요청이 `MaxConnections`를 함께 넘는 경쟁을 막는다(TCP의 수락 시점 예약과 동일 의도). 상한 초과·키 불일치는 `ConnectionRequest.Reject()`. 클라이언트 호스트는 들어오는 접속 요청을 무조건 거부한다(임시 포트 보호).
7. **원격 끊김은 채널 통지로 이어 붙인다.** `RudpMessageChannel`의 internal `TransportDisconnected`를 `RudpSession`이 구독해 `Session.MarkDisconnected`로 전달한다 — Shared 수정 없이 메시지 채널 경로의 끊김 관측을 `Session.Disconnected` 하나로 유지. LiteNetLib `DisconnectReason` → Shared `DisconnectReason` 매핑: `Timeout`→Timeout, `RemoteConnectionClose`→Remote, `DisconnectPeerCalled`→Local, 나머지(ConnectionFailed·HostUnreachable·InvalidProtocol·UnknownHost 등)→Error.
8. **클라이언트 자원은 채널이 호스트까지 소유**한다. 클라이언트는 peer가 하나뿐이라 `RudpMessageChannel.Dispose()`가 `RudpNetHost`(폴링 스레드·NetManager)도 정리한다 — 앱이 세션/채널만 Dispose하면 남는 자원이 없다(TCP에서 채널이 `TcpClient`를 소유하는 것과 동일). 서버 채널은 여러 peer가 한 호스트를 공유하므로 소유하지 않는다.
9. **정지는 `NetManager.Stop(true)`** — 접속 중 peer에 끊김 메시지를 보내 상대가 `DisconnectTimeout`(기본 5초) 대기 대신 `RemoteConnectionClose`로 즉시 끊김을 본다.
10. **수신 버퍼는 `NetPacketReader.Recycle()`로 반환**한다. LiteNetLib `AutoRecycle` 기본값이 `false`이므로 호출자가 반환해야 하며, `RudpNetHost.OnNetworkReceive`가 `finally`에서 보장한다. payload는 `GetRemainingBytesMemory()`의 제로카피 뷰를 콜백 안에서만 넘긴다(`IMessageChannel` 계약: payload는 콜백 안에서만 유효 — 파이프라인이 그 안에서 역직렬화해 복사한다).

## Consequences

### Positive

- 서버·클라이언트 독립 설치 — TCP와 동일한 패키지 경험.
- 접속 수가 늘어도 스레드 수가 늘지 않고, 앱 핸들러가 네트워크 스레드를 점유하지 않는다.
- LiteNetLib이 공개면에 없으므로 이후 **자체 RUDP로 교체해도 앱 코드가 안 깨진다**(Channel 계약만 맞추면 Session·Pipeline 재사용 — [[0005-rudp-litenetlib-interim]]의 출구 전략).
- `netstandard2.1` 유지로 Unity 호환(LiteNetLib 2.1.4가 netstandard2.1 제공, MIT, 의존성 0).

### Negative

- 폴링 간격 1ms 고정 — 초저지연 요구가 실측으로 확인되면 `RudpTransportOptions`로 노출해야 한다(코드에 `ponytail:` 주석으로 표시).
- **분할 불가 방식의 MTU 초과 `ArgumentException`은 세션을 끊는다.** `MessagePipeline`은 채널 `SendAsync`의 예외를 전부 채널 오류로 취급해 해당 항목의 flush를 예외 완료시키고 `Disconnected(Error)`로 끊는다(예외 객체는 `DisconnectedEventArgs.Exception`에 보존). 「송신 실패는 항목 격리」는 **직렬화 실패에만** 적용된다. 항목 격리로 내리려면 Shared 수정이 필요해 이 결정의 범위 밖으로 둔다.
- 호스트당 폴링 스레드 + LiteNetLib 자체 스레드(논리·수신)가 상주한다 — 프로세스당 리스너/커넥터 인스턴스 수만큼만 늘고 접속 수와는 무관.

## Alternatives considered

- **LiteNetLib `DeliveryMethod`를 공개면에 직접 노출** — 매핑 코드 0줄이지만 앱이 LiteNetLib에 컴파일 종속되어 [[0005-rudp-litenetlib-interim]] 2번 위반, 자체 RUDP 교체 시 breaking. 거부.
- **`UnsyncedEvents = true`(폴링 루프 없음)** — 지연은 최저지만 수신 콜백이 LiteNetLib 소켓 스레드에서 실행되어, 앱 코드가 한 번만 블록해도 **전체 접속의 수신**이 멈춘다. 「여러 클라이언트 병목 없음」 요구와 정면 충돌. 거부.
- **peer(클라이언트)당 수신 스레드** — 접속 수만큼 스레드 증가, 컨텍스트 스위칭·메모리 비용. 거부.
- **RUDP 1 패키지 유지** — 서버 앱이 클라이언트 코드를, 클라이언트 앱이 서버 코드를 함께 설치해야 하고 TCP 선례와 어긋남. 거부.
- **리스너가 세션까지 생성해 노출** — 앱 코드는 줄지만 [[0006-session-ownership-and-converter]]의 세션 소유 원칙 위반. 거부.

## 관련

- [[0001-transport-channel-abstraction]] · [[0004-send-options-and-handler-api]] · [[0005-rudp-litenetlib-interim]] · [[0006-session-ownership-and-converter]]
- [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]] · [[../03-Reference/Packages|Packages]] · [[../02-Architecture/Code-Structure|Code-Structure]] · [[../02-Architecture/Channel|Channel]]
