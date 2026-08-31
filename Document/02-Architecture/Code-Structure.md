---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, structure]
updated: 2026-08-31
---

# Code Structure

목표 소스·네임스페이스 맵. 구현 전 설계이며, 폴더가 생기면 이 노트와 맞춘다.

## 저장소 트리 (목표)

```
DS_Communication/
├── Communication.sln
├── Directory.Build.props
├── Source/
│   ├── Directory.Build.props          # IsPackable, TFM, XML docs
│   ├── Communication.Shared/
│   ├── Communication.Network.TCP/
│   ├── Communication.Network.TCP_IOCP/
│   ├── Communication.Network.RUDP/
│   ├── Communication.IPC.Stream/      # 후속
│   └── Communication.IPC.SharedMemory/ # 후속
├── Test/
│   └── Communication.Tests/           # Shared + TCP 테스트 (xUnit)
├── Sandbox/
│   └── Chat.TCP/                      # TCP 수동 검증 채팅 샘플
├── Document/                          # 이 vault
└── Legacy/                            # 아카이브 (수정·확장 대상 아님)
```

현재 활성: Shared + TCP 구현 완료, Test·Sandbox 운영 중. RUDP·TCP_IOCP 미착수. (`Sandbox/UsageExamples`는 소스 없는 빌드 잔재)

## 패키지 ↔ 폴더

| 프로젝트 | 네임스페이스 루트 | 포함 |
| ---------- | ------------------- | ------ |
| `Communication.Shared` | `Communication.Shared` | Sessions, Messages, Channels, Framing, DisconnectReason, Threading |
| `Communication.Network.TCP` | `Communication.Network.TCP` | Connector, Listener, Session, Stream ByteChannel |
| `Communication.Network.TCP_IOCP` | `Communication.Network.TCP_IOCP` | Connector, Listener, Session, IOCP ByteChannel |
| `Communication.Network.RUDP` | `Communication.Network.RUDP` | Connector, Listener, Session, MessageChannel |
| `Communication.IPC.Stream` | `Communication.IPC.Stream` | Pipe/UDS Connector·Listener·ByteChannel |
| `Communication.IPC.SharedMemory` | `Communication.IPC.SharedMemory` | SharedMemory channel 어댑터 |

의존: 모든 전송 패키지 → `Communication.Shared`만. 전송 패키지끼리 참조하지 않는다.

## Shared 내부 (실제)

```
Communication.Shared/
├── Sessions/
│   ├── ISession.cs
│   └── Session.cs
├── Messages/
│   ├── IMessageConverter.cs
│   ├── IMessageHandler.cs
│   ├── MessageHandler.cs
│   ├── MessagePipeline.cs
│   ├── MessageQueueOptions.cs
│   └── PooledBufferWriter.cs          # 내부 — 직렬화·coalesce 버퍼
├── Channels/
│   ├── IByteChannel.cs
│   ├── IMessageChannel.cs
│   └── SendOptions.cs
├── Framing/
│   ├── LengthPrefixFramer.cs
│   └── LengthPrefixFrameReader.cs
├── Connection/
│   ├── DisconnectReason.cs
│   └── DisconnectedEventArgs.cs
└── Threading/
    └── SignalGate.cs
```

공유 `IConnector`/`IListener` 인터페이스와 `ISharedMemoryChannel` 예약은 실제 필요 시점까지 보류.

## TCP 내부 (실제)

```
Communication.Network.TCP/
├── TcpConnector.cs
├── TcpListener.cs
├── TcpSession.cs
├── TcpTransportOptions.cs          # SocketKeepAliveOptions + 내부 KeepAliveApplicator
└── StreamByteChannel.cs            # TcpClient/NetworkStream → IByteChannel
```

## TCP_IOCP 내부 (목표)

```
Communication.Network.TCP_IOCP/
├── TcpIocpConnector.cs
├── TcpIocpListener.cs
├── TcpIocpSession.cs
├── TcpIocpTransportOptions.cs
└── IocpByteChannel.cs              # SocketAsyncEventArgs → IByteChannel
```

스택(전송)당 프로젝트를 나누고, Client/Server는 나누지 않는다.

## RUDP 내부 (목표)

```
Communication.Network.RUDP/
├── RudpConnector.cs
├── RudpListener.cs
├── RudpSession.cs
├── RudpMessageChannel.cs           # IMessageChannel (LiteNetLib)
└── RudpSendOptions.cs              # : SendOptions
```

## 레거시와의 대응

| Legacy | 재작성 |
| -------- | -------- |
| `TCP.Client` + `TCP.Server` + `TCP.Shared` | `Network.TCP` 하나 |
| `TCP_IOCP.Client` + `Server` + `Shared` | `Network.TCP_IOCP` 하나 |
| `RUDP.Client` + `Server` + `Shared` | `Network.RUDP` 하나 |
| Shared에 Channel 없음 | Shared `Channels/*` 도입 |
| 스택별 Sender/Receiver 복제 | Shared `MessagePipeline` + channel 구현만 스택별 |

## 관련

- [[../02-Architecture/Overview|Overview]]
- [[../02-Architecture/Components|Components]]
- [[../03-Reference/Packages|Packages]]
- [[../00-AI/CONVENTIONS|CONVENTIONS]]
- [[0002-tcp-backend-selection]]
