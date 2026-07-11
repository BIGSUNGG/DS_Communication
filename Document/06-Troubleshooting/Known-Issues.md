---
project: DS_Communication
type: troubleshoot
status: draft
tags: [known-issues, performance, architecture]
updated: 2026-07-11
---

# Known Issues — 구조·성능·병목

코드 분석(2026-07-11) 및 수정 반영. 사용법 함정은 [[FAQ]].

## Fixed (2026-07-11)

| 이슈 | 조치 |
|------|------|
| SemaphoreSlim(0,1) + 연속 Release | Interlocked 게이트 + 단일 시그널 (`MessageHandler`, TCP/RUDP Sender) |
| `Session.SendAsync(message, context)` context 무시 | `_messageSender.SendAsync(message, context)` 위임 |
| TCP_IOCP Accept pending 무한 재투기 | Accept 1건 post → TCS 대기 → 처리 후 다음 |
| TCP_IOCP `_isSending` 레이스 | `Interlocked` + 큐 더블체크 |
| TCP 메시지마다 Flush / 이중 Write | Flush 제거, length+payload 단일 `WriteAsync` (ArrayPool) |
| RUDP Poll 15ms | `Task.Delay(1)` |
| RUDP `connectionKey` 미검증 | `AcceptIfKey(_connectionKey)` |
| Accept마다 `Task.Run` | `_ = onClientAccepted(...)` 로 완화 |
| `MessageHandler` Dispose/`async void` | Cancel→Release→Wait; `async Task` 루프 |
| TCP.Server EF Core Tools | PackageReference 제거 |

## Open — 성능

### 메시지당 할당·복사

| 경로 | 문제 |
|------|------|
| `IMessageConverter.Serialize` → `byte[]` | 송신마다 힙 할당 |
| TCP 수신 `new byte[messageLength]` | 본문마다 할당 |
| RUDP 수신 `new byte[AvailableBytes]` | 패킷마다 복사+할당 |
| Sandbox Converter `Span.ToArray()` | Deserialize 전 불필요 복사 |

Converter API·수신 경로 ArrayPool 전면 도입은 별도 작업.

### 세션당 태스크·스레드 비용

송신 큐 루프 + 수신 루프 + `MessageHandler` 큐 루프가 세션마다 붙는다. 연결 수에 비례해 비용이 증가한다.

## Open — 구조

### 전송 스택 3벌 복제

TCP / TCP_IOCP / RUDP Session·Sender·Receiver·Listener 중복. 공통 파이프라인 통합은 별도 ADR/작업.

### TCP Client/Server 의존 비대칭

`TCP.Client` / `TCP.Server`는 Shared ProjectReference 없음 (Connector·Listener만). 의도된 소비 패턴 → [[FAQ]], [[Components]].

## 관련

- [[FAQ]]
- [[Data-Flow]]
- [[Components]]
- [[Public-API]]
- [[Overview]]
- [[Changelog]]
