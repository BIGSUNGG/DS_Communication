---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, channel]
updated: 2026-07-11
---

# Channel

전송 매체의 **I/O 어댑터**. 바이트 또는 메시지 단위로 읽고 쓸 뿐이며, 큐·직렬화·핸들러는 모른다.

## 한 줄

Channel = “이 연결에 데이터를 넣고 빼는 구멍”. [[Pipeline]]이 Channel 위에서 Framing·메시지 경계를 맞춘다.

## 왜 있는가

- TCP / TCP_IOCP / Named Pipe / UDS는 I/O API만 다르고, 메시지 파이프라인은 같다 → **`IByteChannel`로 통일**.
- RUDP·SharedMemory는 바이트 스트림이 아님 → **별 채널 계약**으로 억지 변환을 피한다.
- Legacy처럼 스택마다 Sender/Receiver 전체를 복제하지 않고, Channel 구현만 스택 패키지에 둔다.

## 채널 종류

### `IByteChannel` (스트림)

| 항목 | 내용 |
|------|------|
| 사용 | TCP, TCP_IOCP, 후속 IPC.Stream |
| 연산 | `ReadAsync(Memory<byte>)`, `WriteAsync(ReadOnlyMemory<byte>)`, 연결 상태 |
| 메시지 경계 | **없음** — [[Pipeline]] + `LengthPrefixFramer`가 담당 |
| 구현 예 | `StreamByteChannel` (`NetworkStream`), `IocpByteChannel` (`SocketAsyncEventArgs`) |

### `IMessageChannel` (메시지)

| 항목 | 내용 |
|------|------|
| 사용 | RUDP |
| 연산 | payload `Send` + 수신 콜백/펌프, `SendOptions`(신뢰성·순서) |
| 메시지 경계 | 전송 계층이 제공 → **Framer 불필요** |
| 구현 예 | `RudpMessageChannel` |

### `ISharedMemoryChannel` (후속)

| 항목 | 내용 |
|------|------|
| 사용 | IPC.SharedMemory |
| 연산 | 슬롯 Claim / Commit / Consume |
| 비고 | 바이트 스트림 Framing과 혼합하지 않음 |

## 책임

| 한다 | 하지 않는다 |
|------|-------------|
| OS/라이브러리 소켓·피어에 바이트/메시지 전달 | `Serialize` / `Deserialize` |
| 읽기 0·오류 등 끊김 신호를 상위에 노출 | 송신 큐·coalesce·flush TCS |
| (가능하면) 취소·Dispose로 대기 중 I/O 정리 | 타입별 Handler 호출 |
| | 하트비트 (앱 메시지) |

## Pipeline과의 경계

```text
앱 메시지
  → Pipeline: Serialize, (Framer), 큐
    → Channel.Write / Send
Channel.Read / 수신
  → Pipeline: (Unframe), Deserialize
    → Handler
```

Channel은 “이미 준비된 버퍼/페이로드”만 다룬다. length-prefix 4바이트를 붙이는 것은 Framer·Pipeline 쪽이다.

## 패키지 배치

- **계약**: `Communication.Shared` (`Channels/`)
- **구현**: 각 스택 패키지 (`Network.TCP`, `Network.TCP_IOCP`, `Network.RUDP`, …)
- 전송 패키지끼리 Channel을 참조하지 않는다.

## Legacy와의 차이

| Legacy | 재작성 |
|--------|--------|
| `NetworkStream` / `Socket` / `NetPeer`가 Sender·Receiver에 직접 박힘 | Channel 뒤에 숨김 |
| I/O + 큐 + 직렬화가 한 클래스 | I/O만 Channel |

## 관련

- [[Session]]
- [[Pipeline]]
- [[Data-Flow]]
- [[0001-transport-channel-abstraction]]
- [[0002-tcp-backend-selection]]
- [[Components]]
