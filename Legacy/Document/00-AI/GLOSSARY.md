---
project: DS_Communication
type: context
status: draft
tags: [ai, glossary]
updated: 2026-07-11
---

# Glossary

도메인 용어. 새 용어는 여기 먼저 추가한다.

| 용어 | 설명 |
|------|------|
| RUDP | Reliable UDP. LiteNetLib 기반 신뢰성 전송 |
| IOCP | Windows I/O Completion Port 스타일. 이 저장소의 TCP_IOCP는 `Socket` / `SocketAsyncEventArgs` 기반 TCP |
| Session | 연결 단위 송수신·수명 추상화 (`ISession` / `Session`) |
| Shared | 클라이언트/서버가 공유하는 타입·계약 어셈블리 |
| Connector | 클라이언트 측 연결 진입점 (`TCPConnector`, `RUDPConnector`) |
| Listener | 서버 측 수락 진입점 (`TCPListener`, `RUDPListener`) |
| IMessageConverter | 객체 ↔ `byte[]` 직렬화 계약. 앱이 구현·주입 |
| length-prefix | TCP 프레이밍: 4바이트 길이 + payload |
| DeliveryMethod | LiteNetLib 전송 모드. `ReliableType` / `MessageSendContext`로 지정 |
| ReliableType | RUDP 송신 신뢰성 enum (Unreliable … ReliableSequenced) |
| RUDPNetworkReceiveDispatcher | peer별 Receiver로 수신 이벤트 분배 (단일 구독) |
| connectionKey | LiteNetLib 연결 문자열. 클라이언트·서버 일치 필요 |
| MessageHandler | 수신 메시지를 타입별 Action으로 디스패치하는 앱 계층 |

## 공통 (DS 스택)

| 용어 | 설명 |
|------|------|
| netstandard2.1 | Unity 및 다중 .NET 런타임 호환 타깃 |
| NuGet | 패키지 배포 단위 (`Source/`만 IsPackable) |
| Sandbox | 샘플·데모 (`Chat`, `RUDP_Chat`, `TCP_IOCP_Chat`) |
| DS_MessageProtocol | 형제: 메시지 직렬화 포맷 |
| DS_RPC | 형제: 분산 RPC (Communication·MessageProtocol 의존) |

## 관련

- [[CONTEXT]]
- [[CONVENTIONS]]
- [[Public-API]]
