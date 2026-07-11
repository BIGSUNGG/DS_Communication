---
project: DS_Communication
type: troubleshoot
status: draft
tags: [known-issues, performance, architecture]
updated: 2026-07-11
---

# Known Issues — 구조·성능·병목

코드 분석(2026-07-11) 기준. 수정 전까지 알려진 문제와 병목을 심각도순으로 기록한다. 사용법 함정은 [[FAQ]].

## P0 — 동작 깨짐 / 리소스 폭주

### SemaphoreSlim(0, 1) + 연속 `Release`

| 위치 | `MessageHandler`, TCP/RUDP `*MessageSender` |
|------|---------------------------------------------|
| 증상 | 큐에 메시지가 쌓인 채 처리 루프가 아직 `Wait`하지 않으면 `Release()` → **SemaphoreFullException** |
| 영향 | 고부하 송신·수신 핸들러 중단 |
| 방향 | `maxCount` 확대, 또는 `Channel<T>` / 단일 시그널(`Interlocked`)로 교체 |

### `Session.SendAsync(message, context)`가 context 무시

| 위치 | `Communication.Shared` `Session` |
|------|----------------------------------|
| 증상 | context 오버로드가 `SendAsync(message)`만 호출 |
| 영향 | RUDP `MessageSendContext` / `ReliableType`이 Session 경로로 전달되지 않음. [[Public-API]]·[[Data-Flow]] 계약과 불일치 |
| 방향 | `_messageSender.SendAsync(message, context)`로 위임 |

### TCP_IOCP Accept 루프 — pending 시 무한 재투기

| 위치 | `TCP_IOCP.Server` `TCPListener.ListenAsync` |
|------|---------------------------------------------|
| 증상 | `AcceptAsync`가 pending(`true`)이면 대기 없이 while 재진입 → SAEA·pending Accept 무제한 가능 |
| 영향 | CPU·메모리 폭주 |
| 방향 | 완료 콜백에서 다음 Accept 1건만 post (전형적 IOCP Accept) |

### TCP_IOCP Sender `_isSending` 레이스

| 위치 | `TCP_IOCP.Shared` `TCPMessageSender.SendAsync` |
|------|------------------------------------------------|
| 증상 | `_isSending` 검사·설정에 락 없음 |
| 영향 | 동시 `SendAsync` 시 큐에만 쌓이고 전송이 시작되지 않을 수 있음 |
| 방향 | 락 / `Interlocked` / 단일 송신 펌프 |

## P1 — 성능 병목

### 메시지당 할당·복사

| 경로 | 문제 |
|------|------|
| `IMessageConverter.Serialize` → `byte[]` | 송신마다 힙 할당 |
| TCP 수신 `new byte[messageLength]` | 본문마다 할당 |
| RUDP 수신 `new byte[AvailableBytes]` | 패킷마다 복사+할당 |
| Sandbox Converter `Span.ToArray()` | Deserialize 전 불필요 복사 |

`System.Buffers`는 패키지 참조만 있고 ArrayPool/파이프라인 활용은 거의 없다. GC 압박이 커진다.

### TCP 송신 I/O

| 문제 | 영향 |
|------|------|
| 메시지마다 `FlushAsync` | 버퍼링·Nagle 무력화, 처리량↓ 지연↑ |
| length 4B + payload **분리 `WriteAsync`** | 시스템콜 2회 |

### 세션당 태스크·스레드 비용

송신 큐 루프 + 수신 루프 + `MessageHandler` 큐 루프가 세션마다 붙는다. 연결 수에 비례해 스레드풀·스케줄링 비용이 증가한다.

### RUDP `PollEvents` + `Task.Delay(15)`

| 위치 | `RUDPConnector`, `RUDPListener.ListenAsync` |
|------|---------------------------------------------|
| 영향 | 이벤트 처리 지연 하한 ≈ **15ms** (실시간 게임에서 체감) |

### Accept마다 `Task.Run`

TCP / TCP_IOCP / RUDP 모두 연결 콜백을 `Task.Run`으로 띄운다. 동시 접속 폭주 시 스레드풀 고갈·지연 가능.

## P2 — 구조·일관성

### 전송 스택 3벌 복제

TCP / TCP_IOCP / RUDP가 Session·Sender·Receiver·Listener를 각각 유지. length-prefix·큐·Dispose 패턴이 중복되어, Semaphore 등 수정이 세 곳에 반복된다.

### TCP Client/Server 의존 비대칭

`TCP.Client` / `TCP.Server`는 Shared ProjectReference가 없고 Connector·Listener만 제공한다. IOCP·RUDP는 Shared를 끌어온다. 소비 패턴이 스택마다 다르다 → [[FAQ]], [[Components]].

### `TCP.Server`에 EF Core Tools

네트워크 리스너 패키지에 `Microsoft.EntityFrameworkCore.Tools`가 포함되어 있다. 런타임과 무관해 보이며 패키지 경계가 흐려진다.

### RUDP `connectionKey` 미검증

`RUDPListener`는 키를 저장하지만 Accept 시 `request.Accept()`만 호출한다. FAQ의 “키 일치”와 구현이 어긋난다.

### Dispose / 비동기 수명

- `MessageHandler.Dispose`: `Wait` 후 `Cancel` — 세마포어 대기 중이면 타임아웃까지 지연
- `ProcessMessageQueueLoopAsync`가 `async void` — 예외·수명 추적 어려움

## 우선 수정 제안

1. SemaphoreFullException (송신·핸들러)
2. Session context 전달 (RUDP QoS)
3. IOCP Accept 루프
4. IOCP `_isSending` 동기화
5. 메시지당 Flush / 이중 Write / 할당 경로
6. RUDP Poll 간격·connectionKey
7. 공통 송신·수신 파이프라인으로 중복 축소

## 관련

- [[FAQ]]
- [[Data-Flow]]
- [[Components]]
- [[Public-API]]
- [[Overview]]
