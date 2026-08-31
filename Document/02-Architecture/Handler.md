---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, handler]
updated: 2026-09-01
---

# Handler

수신 앱 메시지의 **동기 디스패처**.

## 한 줄

Deserialize된 객체를 타입별 `Action`으로 호출. **끊김은 다루지 않는다** — [[Session]] `Disconnected`만.

## 책임

| 한다 | 하지 않는다 |
| ------ | ------------- |
| `void HandleMessage(object)` | Serialize / Channel I/O |
| `RegisterMessageType` / `Register<T>` | Connect / Disconnect / 끊김 통지 |
| InlineDispatch 또는 내부 큐 | `OnDetectedDisconnection` (없음) |

## 계약

```text
interface IMessageHandler
{
    void HandleMessage(object message);
}
```

`MessageHandler` 기본 구현의 등록 테이블은 `ConcurrentDictionary` — 디스패치 스레드 읽기와 동시 안전, 세션 시작 후 지연 등록도 경쟁 없다.

## 관련

- [[Pipeline]] · [[Session]] · [[../03-Reference/Public-API|Public-API]] · [[0006-session-ownership-and-converter]]
