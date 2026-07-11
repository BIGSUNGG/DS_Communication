---
project: DS_Communication
type: guide
status: draft
tags: [guide, how-to]
updated: 2026-07-11
---

# How-To

자주 하는 작업 레시피.

## 목록

- [[#스택 선택]]
- [[#커스텀 Session·Handler·Converter]]
- [[#TCP 서버 띄우기]]
- [[#RUDP 신뢰성 모드 지정]]

## 스택 선택

| 필요 | 패키지 세트 |
|------|-------------|
| 단순 TCP / 크로스플랫폼 스트림 | `TCP.Client` 또는 `TCP.Server` + `TCP.Shared` + `Shared` |
| Windows Socket 비동기 TCP | `TCP_IOCP.*` + `Shared` |
| UDP 기반 신뢰성·DeliveryMethod | `RUDP.*` + `Shared` (LiteNetLib) |

직렬화·RPC가 필요하면 이 라이브러리가 아니라 DS_MessageProtocol / DS_RPC를 쓴다.

## 커스텀 Session·Handler·Converter

Sandbox 패턴 (`Sandbox/*/Shared/DefaultMessageConverter.cs`, `*Session`, `*MessageHandler`):

1. `IMessageConverter` — `Serialize` / `Deserialize` 구현
2. `MessageHandler` 상속 — `RegisterMessageType()`에서 `typeof(T) →` Action 등록
3. `TCPSession` 또는 `RUDPSession` 상속 — 생성자에서 Receiver/Sender factory에 Converter·Handler 전달
4. Connector/Listener 콜백에서 세션 인스턴스 생성

## TCP 서버 띄우기

1. `new TCPListener(IPAddress.Any, port)` → `Start()`
2. `ListenAsync(onClientAccepted, token)` — 콜백에서 `TcpClient`로 Session 생성
3. 종료 시 `Stop()`, CancellationToken으로 Accept 루프 중단

참고: `Sandbox/Chat/Server/Program.cs`

## RUDP 신뢰성 모드 지정

```csharp
await session.SendAsync(message, new MessageSendContext(ReliableType.Unreliable));
```

기본 `SendAsync(message)`는 `ReliableOrdered`. enum 값은 LiteNetLib `DeliveryMethod`에 대응 ([[GLOSSARY]], [[Public-API]]).

서버·클라이언트의 `connectionKey`를 동일하게 맞춘다.

## 관련

- [[Getting-Started]]
- [[FAQ]]
- [[Data-Flow]]
- [[Components]]
