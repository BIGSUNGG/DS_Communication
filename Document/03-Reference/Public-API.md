---
project: DS_Communication
type: reference
status: draft
tags: [reference, api]
updated: 2026-09-05
---

# Public API

합의된 공개 계약. ADR: [[0004-send-options-and-handler-api]], [[0006-session-ownership-and-converter]], [[0003-connection-lifecycle-options]], [[0007-rudp-three-way-split-and-polling]].

## Connect / Listen — Session은 앱이 생성

```text
// Client
Task<bool> ConnectAsync(..., CancellationToken cancellationToken = default);
IByteChannel Channel { get; }     // TCP / TCP_IOCP — Connect 성공 후
IMessageChannel Channel { get; }  // RUDP — Connect 성공 후

// 앱
if (!await connector.ConnectAsync(host, port)) return;
var session = new TcpSession(connector.Channel, converter, s => new ChatHandler(s));   // TCP
var session = new RudpSession(connector.Channel, converter, s => new ChatHandler(s));   // RUDP

// Server
listener.Accepted += channel => { var session = new TcpSession(channel, ...); };
listener.Start(...);
```

`Accepted`는 수락 루프가 수락마다 최신 구독자를 읽는다 — `Start` 이후 구독자도 채널을 받는다. `TcpListener.Accepted`는 `IByteChannel`, `RudpListener.Accepted`는 `IMessageChannel`을 넘긴다(세션 생성은 언제나 앱 — [[0006-session-ownership-and-converter]]).

## TCP keep-alive (사용자 설정)

TCP / TCP_IOCP Connector·Listener 생성 시 옵션으로 전달.

```text
class SocketKeepAliveOptions
{
    bool Enabled;
    TimeSpan IdleTime;      // 유휴 후 첫 probe (OS 지원 범위)
    TimeSpan Interval;      // probe 간격
}

class TcpTransportOptions
{
    SocketKeepAliveOptions? KeepAlive;  // null = OS 기본, 명시 시 적용
    bool NoDelay;                       // TCP_NODELAY, 기본 true (라이브러리 coalesce와 중복되는 Nagle 해제)
    int? MaxConnections;                // 동시 수락 상한, null = 무제한
    int? ConnectTimeout;                // 연결 시도 상한(ms), null = OS 기본(SYN 재시도 수십 초)
}
```

- `Enabled = false` 또는 옵션 생략: keep-alive 변경 없음(또는 OS 기본).
- Unity/netstandard2.1에서 일부 필드는 OS가 무시할 수 있음 — 문서/Configuration에 플랫폼 노트.

## ISession

```text
Task SendAsync(object message);
Task SendAsync(object message, SendOptions? options);
Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default);

void Disconnect();
bool IsConnected();

event EventHandler<DisconnectedEventArgs> Disconnected;
// DisconnectedEventArgs.Reason: DisconnectReason { Local, Remote, Error, Timeout, FlowControl }
// DisconnectedEventArgs.Exception? (Error·Timeout·FlowControl일 때)
```

**재접속 이벤트 없음.** 앱이 `Disconnected` 후 `ConnectAsync` + `new Session`.

## 런타임 의미 (합의 2026-08-31)

- 끊김·Dispose·파이프라인 미부착 세션의 `SendAsync` / `SendAndFlushAsync`는 **예외로 완료된 Task**를 반환한다 (동기 throw 아님, 무시 아님). `SendAndFlushAsync`는 토큰이 이미 취소됐으면 큐잉 없이 즉시 취소 완료 Task를 반환한다.
- 큐 백프레셔 상한 도달 시 **공간 날 때까지 비동기 대기**한다 (드롭·예외 아님). 메시지 채널(`IMessageChannel`) 수신 경로는 **슬롯 대기(메시지 보유)까지 상한에 포함**해 강제하고, 초과 시 `DisconnectReason.FlowControl`로 **실패 폐쇄 단절** — 무제한 누적 없음 (바이트 채널 경로는 수신 루프가 추가 읽기를 멈추는 백프레셔 유지).
- 핸들러 `Action`이 던진 예외는 **Trace 후 수신 루프 계속** — 세션 끊김으로 격상하지 않는다.
- 송신 직렬화·프레임 검증 실패는 **해당 항목의 플러시만 예외 완료**하고 송신 루프 계속 — 세션 끊김으로 격상하지 않는다 (수신 격리와 대칭).
- `InlineDispatch` 기본 `false` (내부 큐) — [[../03-Reference/Configuration|Configuration]].

## SendOptions

```text
class SendOptions { }                       // Shared — 빈 마커 클래스

enum RudpDeliveryMethod                     // LiteNetLib DeliveryMethod와 같은 이름·같은 값
{
    ReliableUnordered = 0,                  // 유실·중복 없음, 순서 보장 없음
    Sequenced         = 1,                  // 유실 가능, 중복 없음, 순서 보장 (오래된 패킷 드롭)
    ReliableOrdered   = 2,                  // 유실·중복 없음, 순서 보장 (기본값)
    ReliableSequenced = 3,                  // 마지막 패킷만 신뢰, 분할 불가
    Unreliable        = 4,                  // 유실·중복 가능, 순서 보장 없음
}

sealed class RudpSendOptions : SendOptions
{
    RudpSendOptions(RudpDeliveryMethod deliveryMethod);
    RudpDeliveryMethod DeliveryMethod { get; }

    // 송신 경로 할당 0 — 공용 인스턴스
    static RudpSendOptions ReliableOrdered { get; }
    static RudpSendOptions ReliableUnordered { get; }
    static RudpSendOptions Sequenced { get; }
    static RudpSendOptions ReliableSequenced { get; }
    static RudpSendOptions Unreliable { get; }
}
```

메시지별로 다른 전송 방식을 지정할 수 있다:

```text
await session.SendAsync(chatMessage, RudpSendOptions.ReliableOrdered);
await session.SendAsync(position,    RudpSendOptions.Unreliable);
```

- `options`가 `null`이거나 `RudpSendOptions`가 아니면 **`ReliableOrdered`**로 보낸다.
- LiteNetLib 타입은 공개 API에 나타나지 않는다 — `RudpDeliveryMethod` → `DeliveryMethod` 매핑은 `RudpMessageChannel` 내부.

## RUDP (LiteNetLib)

```text
sealed class RudpListener : IDisposable
{
    RudpListener(IPAddress address, int port);
    void Start(RudpTransportOptions? options = null);
    void Stop();
    event Action<IMessageChannel> Accepted;   // 세션 생성은 앱이
    int LocalPort { get; }                    // 포트 0 바인딩 시 실제 포트
    int ActiveConnectionCount { get; }        // MaxConnections 상한 기준
}

sealed class RudpConnector
{
    Task<bool> ConnectAsync(string host, int port, RudpTransportOptions? options = null,
                            CancellationToken cancellationToken = default);
    IMessageChannel? Channel { get; }         // 성공 후; 실패 시 null
}

class RudpTransportOptions
{
    int? MaxConnections;            // 동시 수락 상한, null = 무제한
    int DisconnectTimeout;          // 끊김 판정(ms), 기본 5000 — UDP half-open 감지의 유일한 신호
    string ConnectionKey;           // 접속 검증 키, 기본 "DS_Communication.RUDP"
    bool IPv6;                      // 기본 false (IPv4만)
    int? ConnectTimeout;            // 연결 시도 상한(ms), null = LiteNetLib 기본(≈5초)
}

class RudpSession : Session
{
    RudpSession(IMessageChannel channel, IMessageConverter converter,
                Func<ISession, IMessageHandler> handlerFactory, MessageQueueOptions? queueOptions = null);
}

sealed class RudpMessageChannel : IMessageChannel
{
    bool IsConnected { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, SendOptions? options = null, CancellationToken ct = default);
    event Action<ReadOnlyMemory<byte>> MessageReceived;   // 폴링 스레드에서 발생, payload는 콜백 안에서만 유효
    void Dispose();
}
```

**RUDP 런타임 의미**

- **수신 스레딩**: 호스트(리스너/커넥터)당 전용 폴링 스레드 1개. 스레드 수는 접속 수와 무관하게 고정이고, 앱 핸들러는 `MessagePipeline`의 세션별 디스패치 큐에서 돌아 폴링 스레드를 점유하지 않는다.
- **MTU 가드**: 분할 불가 방식(`Sequenced`·`ReliableSequenced`·`Unreliable`)으로 `peer.GetMaxSinglePacketSize(method)` 초과 payload를 보내면 `SendAsync`가 **`ArgumentException`**. 이 예외는 파이프라인에서 채널 오류로 취급되어 해당 항목 flush를 예외 완료시키고 **세션을 `Disconnected(Error)`로 끊는다**(예외는 `DisconnectedEventArgs.Exception`에 보존). 「송신 실패 항목 격리」는 직렬화 실패에만 적용된다.
- **원격 끊김**: 메시지 채널 경로에는 수신 루프가 없어 `RudpSession`이 채널의 peer 끊김 통지를 `Session.Disconnected`로 이어 붙인다. LiteNetLib 이유 → Shared `DisconnectReason` 매핑: `Timeout`→Timeout, `RemoteConnectionClose`→Remote, `DisconnectPeerCalled`→Local, 나머지→Error.
- **접속 수 상한**: `MaxConnections`는 접속 요청 시점에 슬롯을 **예약**해 검사한다(같은 폴링 배치의 다수 요청이 상한을 함께 넘지 못함). 초과·키 불일치는 `Reject()` 되고 `Accepted` 통지가 없다. peer 끊김 또는 채널 Dispose 시 슬롯 회수.
- **자원 소유**: 클라이언트는 peer가 하나뿐이라 `RudpMessageChannel.Dispose()`가 내부 호스트(폴링 스레드·NetManager)까지 정리한다. 서버 채널은 호스트를 소유하지 않으며 `RudpListener.Stop()`이 `NetManager.Stop(true)`로 접속 중 peer에 끊김 메시지를 보낸다.
- **하트비트·재접속은 앱**. `DisconnectTimeout`(기본 5000ms)이 UDP half-open 감지의 유일한 신호다 — [[0003-connection-lifecycle-options]].

## Handler

```text
interface IMessageHandler
{
    void HandleMessage(object message);
}
```

## Converter

```text
interface IMessageConverter
{
    void Serialize(object message, IBufferWriter<byte> writer);
    object Deserialize(ReadOnlySpan<byte> message);
}
```

## 관련

- [[../04-Guides/Getting-Started|Getting-Started]]
- [[Session]] · [[Handler]] · [[Pipeline]] · [[Channel]]
- [[Implementation-Roadmap]] · [[../03-Reference/Configuration|Configuration]]
