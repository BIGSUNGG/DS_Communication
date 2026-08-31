---
project: DS_Communication
type: reference
status: draft
tags: [reference, packages]
updated: 2026-08-31
---

# Packages

| 패키지 | TFM (목표) | 의존 | 상태 |
| -------- | ------------ | ------ | ------ |
| `Communication.Shared` | netstandard2.1 | (없음) | 구현 완료 — 테스트 포함 |
| `Communication.Network.TCP` | netstandard2.1 | Shared | 구현 완료 — loopback 테스트 포함 |
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
| Network.TCP | Stream `IByteChannel`, `SocketKeepAliveOptions` |
| Network.RUDP | LiteNetLib `IMessageChannel`, `RudpSendOptions` |
| Network.TCP_IOCP | IOCP `IByteChannel` |

## 규칙

- 스택당 1 패키지 (Client/Server 분할 없음)
- 전송 패키지 상호 참조 없음
- 자체 RUDP는 이후 **별 프로젝트**

## 관련

- [[../02-Architecture/Overview|Overview]] · [[../02-Architecture/Code-Structure|Code-Structure]] · [[0002-tcp-backend-selection]] · [[0005-rudp-litenetlib-interim]]
