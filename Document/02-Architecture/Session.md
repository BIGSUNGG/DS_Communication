---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, session]
updated: 2026-08-31
---

# Session

앱이 Channel 위에 **직접 생성하는** 연결형 메시지 단위.

## 한 줄

Connector/Listener → Channel → 앱이 `new *Session(...)`. 끊김 관측은 Session 이벤트만.

## 책임

| 한다 | 하지 않는다 |
| ------ | ------------- |
| Send / Disconnect / IsConnected | Connect/Accept (Connector/Listener) |
| Pipeline·Channel 소유 | Converter 구현 |
| `Disconnected(DisconnectReason)` — 구독자별 호출, 예외 격리(Trace) | Handler 끊김 콜백 · 재접속 (앱) |

## 생성 (앱)

```text
if (!await connector.ConnectAsync(...)) return;
var session = new TcpSession(connector.Channel, converter, s => new MyHandler(s));
```

Accept 콜백에서도 동일하게 `new TcpSession(channel, ...)`.

## 공개 계약

[[Public-API]].

## 관련

- [[Channel]] · [[Pipeline]] · [[Handler]]
- [[0006-session-ownership-and-converter]] · [[0003-connection-lifecycle-options]]
