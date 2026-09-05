---
project: DS_Communication
type: reference
status: draft
tags: [reference, packages]
updated: 2026-09-05
---

# Packages

| 패키지 | TFM (목표) | 의존 | 상태 |
| -------- | ------------ | ------ | ------ |
| `Communication.Shared` | netstandard2.1 | (없음) | 구현 완료 — 테스트 포함, 2.0.0 배포 |
| `Communication.Network.TCP.Shared` | netstandard2.1 | Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.TCP.Server` | netstandard2.1 | TCP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.TCP.Client` | netstandard2.1 | TCP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.RUDP.Shared` | netstandard2.1 | Shared + **LiteNetLib 2.1.4** | 구현 완료 — 테스트 포함, 2.0.0 배포 |
| `Communication.Network.RUDP.Server` | netstandard2.1 | RUDP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.RUDP.Client` | netstandard2.1 | RUDP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.TCP_IOCP` | netstandard2.1 | Shared | 미착수 |
| `Communication.IPC.Stream` | netstandard2.1 | Shared | 후속 |
| `Communication.IPC.SharedMemory` | netstandard2.1 | Shared | 후속 |

구현 순서: [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]. RUDP 인터림: [[0005-rudp-litenetlib-interim]], RUDP 구성·스레딩: [[0007-rudp-three-way-split-and-polling]].

패키징 제외: `Test/Communication.Tests` (xUnit), `Sandbox/Chat.TCP`, `Sandbox/Chat.RUDP`.

## 역할

| 패키지 | 역할 |
| -------- | ------ |
| Shared | Session, Pipeline, Channel 계약, Framing, `SendOptions`, `DisconnectReason` |
| Network.TCP.Shared | `TcpSession`, Stream `IByteChannel`, `TcpTransportOptions`·`SocketKeepAliveOptions` |
| Network.TCP.Server | `TcpListener` 수락 루프 |
| Network.TCP.Client | `TcpConnector` 연결 |
| Network.RUDP.Shared | `RudpSession`, LiteNetLib `IMessageChannel`(`RudpMessageChannel`), `RudpSendOptions`·`RudpDeliveryMethod`, `RudpTransportOptions`, 내부 `RudpNetHost`(NetManager 소유·폴링 루프 1개·peer 등록부) |
| Network.RUDP.Server | `RudpListener` 수락 (`Accepted(IMessageChannel)`, `MaxConnections` 슬롯 예약) |
| Network.RUDP.Client | `RudpConnector` 연결 (채널이 호스트까지 소유) |
| Network.TCP_IOCP | IOCP `IByteChannel` |

## 규칙

- TCP·RUDP는 Shared/Server/Client 3 패키지로 분할 (서버·클라이언트 독립 설치 목적) — 「스택당 1 패키지」 규칙은 폐기
- 나머지 스택(TCP_IOCP·IPC)은 1 패키지 유지 (분할 필요 시 TCP·RUDP 선례 따름)
- 전송 패키지 상호 참조 없음 (같은 스택의 .Shared 제외)
- LiteNetLib `PackageReference`는 **RUDP.Shared에만** — Server·Client는 전이 참조, LiteNetLib 타입은 공개 API에 노출 금지 ([[0007-rudp-three-way-split-and-polling]])
- 자체 RUDP는 이후 **별 프로젝트**
- 배포: GitHub Actions `nuget-publish.yml` — `v*` 태그 → Communication.Shared, `tcp/v*` 태그 → TCP 3종, `rudp/v*` 태그 → RUDP 3종 (2.0.0 배포 완료)

## 관련

- [[../02-Architecture/Overview|Overview]] · [[../02-Architecture/Code-Structure|Code-Structure]] · [[0002-tcp-backend-selection]] · [[0005-rudp-litenetlib-interim]] · [[0007-rudp-three-way-split-and-polling]]
