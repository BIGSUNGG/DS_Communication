---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, structure]
updated: 2026-09-05
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
│   ├── Communication.Network.TCP.Shared/
│   ├── Communication.Network.TCP.Server/
│   ├── Communication.Network.TCP.Client/
│   ├── Communication.Network.TCP_IOCP/
│   ├── Communication.Network.RUDP.Shared/
│   ├── Communication.Network.RUDP.Server/
│   ├── Communication.Network.RUDP.Client/
│   ├── Communication.IPC.Stream/      # 후속
│   └── Communication.IPC.SharedMemory/ # 후속
├── Test/
│   └── Communication.Tests/           # Shared + TCP + RUDP 테스트 (xUnit)
├── Sandbox/
│   ├── Chat.TCP/                      # TCP 수동 검증 채팅 샘플
│   └── Chat.RUDP/                     # RUDP 수동 검증 채팅 샘플 (--selftest 포함)
├── Document/                          # 이 vault
└── Legacy/                            # 아카이브 (수정·확장 대상 아님)
```

현재 활성: Shared + TCP(Shared/Server/Client 3분할) + RUDP(Shared/Server/Client 3분할) 구현 완료, Test·Sandbox 운영 중. TCP_IOCP 미착수.

## 패키지 ↔ 폴더

| 프로젝트 | 네임스페이스 루트 | 포함 |
| ---------- | ------------------- | ------ |
| `Communication.Shared` | `Communication.Shared` | Sessions, Messages, Channels, Framing, DisconnectReason, Threading |
| `Communication.Network.TCP.Shared` | `Communication.Network.TCP` | TcpSession, StreamByteChannel, TcpTransportOptions |
| `Communication.Network.TCP.Server` | `Communication.Network.TCP` | TcpListener |
| `Communication.Network.TCP.Client` | `Communication.Network.TCP` | TcpConnector |
| `Communication.Network.TCP_IOCP` | `Communication.Network.TCP_IOCP` | Connector, Listener, Session, IOCP ByteChannel |
| `Communication.Network.RUDP.Shared` | `Communication.Network.RUDP` | RudpSession, RudpMessageChannel, RudpSendOptions·RudpDeliveryMethod, RudpTransportOptions, 내부 RudpNetHost |
| `Communication.Network.RUDP.Server` | `Communication.Network.RUDP` | RudpListener |
| `Communication.Network.RUDP.Client` | `Communication.Network.RUDP` | RudpConnector |
| `Communication.IPC.Stream` | `Communication.IPC.Stream` | Pipe/UDS Connector·Listener·ByteChannel |
| `Communication.IPC.SharedMemory` | `Communication.IPC.SharedMemory` | SharedMemory channel 어댑터 |

의존: 모든 전송 패키지 → `Communication.Shared`만. 전송 패키지끼리 참조하지 않는다 (같은 스택의 `.Shared` 참조만 예외). LiteNetLib은 **RUDP.Shared만** 참조한다 — Server·Client는 전이 참조로 받고 LiteNetLib 타입을 코드에 등장시키지 않는다.

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
Communication.Network.TCP.Shared/
├── TcpSession.cs
├── TcpTransportOptions.cs          # SocketKeepAliveOptions + 내부 KeepAliveApplicator
└── StreamByteChannel.cs            # TcpClient/NetworkStream → IByteChannel

Communication.Network.TCP.Server/
└── TcpListener.cs

Communication.Network.TCP.Client/
└── TcpConnector.cs
```

네임스페이스는 셋 다 `Communication.Network.TCP` 유지. Server·Client는 TCP.Shared의 `InternalsVisibleTo`로 내부 생성자·`KeepAliveApplicator` 사용.

## TCP_IOCP 내부 (목표)

```
Communication.Network.TCP_IOCP/
├── TcpIocpConnector.cs
├── TcpIocpListener.cs
├── TcpIocpSession.cs
├── TcpIocpTransportOptions.cs
└── IocpByteChannel.cs              # SocketAsyncEventArgs → IByteChannel
```

TCP·RUDP는 Shared/Server/Client 3프로젝트로 분할(서버·클라이언트 독립 설치). 나머지 스택은 필요 시까지 1 프로젝트 유지.

## RUDP 내부 (실제)

```
Communication.Network.RUDP.Shared/
├── RudpSession.cs                 # : Session — IMessageChannel 경로
├── RudpMessageChannel.cs          # : IMessageChannel (LiteNetLib NetPeer 래퍼, MTU 가드)
├── RudpSendOptions.cs             # : SendOptions — 불변 + 전송 방식별 공용 인스턴스 5개
├── RudpDeliveryMethod.cs          # 5값 (LiteNetLib DeliveryMethod와 같은 이름·값)
├── RudpTransportOptions.cs        # MaxConnections·DisconnectTimeout·ConnectionKey·IPv6
└── RudpNetHost.cs                 # 내부 — NetManager 소유, 폴링 스레드 1개, peer 등록부, 수락 정책

Communication.Network.RUDP.Server/
└── RudpListener.cs                # Accepted(IMessageChannel), LocalPort, ActiveConnectionCount

Communication.Network.RUDP.Client/
└── RudpConnector.cs               # ConnectAsync → Channel
```

네임스페이스는 셋 다 `Communication.Network.RUDP` 유지. Server·Client는 RUDP.Shared의 `InternalsVisibleTo`로 내부 `RudpNetHost`·`RudpMessageChannel` 생성자를 사용한다. LiteNetLib 타입은 `RudpNetHost`·`RudpMessageChannel` 두 파일에만 등장 — [[0007-rudp-three-way-split-and-polling]].

## 레거시와의 대응

| Legacy | 재작성 |
| -------- | -------- |
| `TCP.Client` + `TCP.Server` + `TCP.Shared` | `Network.TCP.Shared` + `.Server` + `.Client` (2.0.0부터 재분할) |
| `TCP_IOCP.Client` + `Server` + `Shared` | `Network.TCP_IOCP` 하나 |
| `RUDP.Client` + `Server` + `Shared` | `Network.RUDP.Shared` + `.Server` + `.Client` (3분할 유지, LiteNetLib 1.3.5 → 2.1.4) |
| Shared에 Channel 없음 | Shared `Channels/*` 도입 |
| 스택별 Sender/Receiver 복제 | Shared `MessagePipeline` + channel 구현만 스택별 |

## 관련

- [[../02-Architecture/Overview|Overview]]
- [[../02-Architecture/Components|Components]]
- [[../03-Reference/Packages|Packages]]
- [[../00-AI/CONVENTIONS|CONVENTIONS]]
- [[0002-tcp-backend-selection]]
- [[0007-rudp-three-way-split-and-polling]]
