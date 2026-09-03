---
project: DS_Communication
type: reference
status: draft
tags: [reference, packages]
updated: 2026-09-04
---

# Packages

| 패키지 | TFM (목표) | 의존 | 상태 |
| -------- | ------------ | ------ | ------ |
| `Communication.Shared` | netstandard2.1 | (없음) | 구현 완료 — 테스트 포함, 2.0.0 배포 |
| `Communication.Network.TCP.Shared` | netstandard2.1 | Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.TCP.Server` | netstandard2.1 | TCP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.TCP.Client` | netstandard2.1 | TCP.Shared | 구현 완료 — 2.0.0 배포 |
| `Communication.Network.RUDP` | netstandard2.1 | Shared + **LiteNetLib** | 미착수 |
| `Communication.Network.TCP_IOCP` | netstandard2.1 | Shared | 미착수 |
| `Communication.IPC.Stream` | netstandard2.1 | Shared | 후속 |
| `Communication.IPC.SharedMemory` | netstandard2.1 | Shared | 후속 |

구현 순서: [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]. RUDP 인터림: [[0005-rudp-litenetlib-interim]].

패키징 제외: `Test/Communication.Tests` (xUnit), `Sandbox/Chat.TCP`.

## 역할

| 패키지 | 역할 |
| -------- | ------ |
| Shared | Session, Pipeline, Channel 계약, Framing, `SendOptions`, `DisconnectReason` |
| Network.TCP.Shared | `TcpSession`, Stream `IByteChannel`, `TcpTransportOptions`·`SocketKeepAliveOptions` |
| Network.TCP.Server | `TcpListener` 수락 루프 |
| Network.TCP.Client | `TcpConnector` 연결 |
| Network.RUDP | LiteNetLib `IMessageChannel`, `RudpSendOptions` |
| Network.TCP_IOCP | IOCP `IByteChannel` |

## 규칙

- TCP는 Shared/Server/Client 3 패키지로 분할 (2.0.0부터; 서버·클라이언트 독립 설치 목적)
- 나머지 스택은 1 패키지 유지 (분할 필요 시 TCP 선례 따름)
- 전송 패키지 상호 참조 없음 (같은 스택의 .Shared 제외)
- 자체 RUDP는 이후 **별 프로젝트**
- 배포: GitHub Actions `nuget-publish.yml` — `v*` 태그 → Communication.Shared, `tcp/v*` 태그 → TCP 3종

## 관련

- [[../02-Architecture/Overview|Overview]] · [[../02-Architecture/Code-Structure|Code-Structure]] · [[0002-tcp-backend-selection]] · [[0005-rudp-litenetlib-interim]]
