---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, pipeline]
updated: 2026-09-01
---

# Pipeline

`MessagePipeline` — 메시지 ↔ 와이어의 **공통 경로**. Legacy Sender+Receiver를 Shared 한 벌로 대체.

## 한 줄

큐 · coalesce · Converter · (Framer) · [[Channel]] · [[Handler]]. 하트비트 필터 없음(앱 메시지).

## 책임

| 한다 | 하지 않는다 |
| ------ | ------------- |
| enqueue, 백프레셔, Serialize(writer), Write/Send | Connect / Accept / Session 생성 |
| Read → Unframe → Deserialize(span) → Handler | 하트비트 제어 프레임 |
| `SendOptions`를 Channel까지 전달 | 재접속 Connect 루프 |
| 끊김 시 Session.`MarkDisconnected` | Handler 끊김 콜백 |
| `SendAndFlushAsync` 완료 신호 | |

## 송신

1. `SendAsync(message, options?)`
2. 큐 + 백프레셔
3. Serialize → `IBufferWriter` → (length-prefix + coalesce if byte channel) → Channel
4. 직렬화 결과가 빈 페이로드면 송신 거부(바이트 채널은 `MaxFrameLength` 상한도 검사) — 빈 프레임은 상대편 EOF와 구분이 안 됨. 직렬화·검증 실패는 **해당 항목만 격리**(플러시 예외 완료 + 부분 프레임 폐기, 슬롯 반환)하고 송신 루프는 계속 — 세션 끊김으로 격상하지 않는다. 성공 경로는 **Flush 완료가 슬롯 해제보다 먼저** — Dispose가 그 사이에 슬롯을 정리해도 호출자 완료는 보장된다
5. RUDP: `options as RudpSendOptions` → Delivery 매핑

## 수신

스트림: `LengthPrefixFrameReader` — **단일 누적 버퍼**(ArrayPool, 기본 ~64KB)에 부분 읽기를 모으고, 완성된 프레임은 내부 버퍼의 **제로카피 슬라이스**로 반환(다음 읽기 전까지 유효). 버퍼는 **선언된 프레임 길이가 아니라 실제 누적된 데이터 기준으로만 성장**(버퍼가 가득 찼을 때만 2배) — 헤더만으로 거대 버퍼를 선할당하는 메모리 증폭 공격 방지. 슬라이스 → Deserialize → Handler.
메시지 채널: payload span → Deserialize → Handler.
EOF/오류 → Session.`MarkDisconnected` → `Disconnected` 이벤트. 길이 0 프레임은 프로토콜 위반(Error).

## 옵션

`MessageQueueOptions` — MaxPendingMessages, coalesce 한도, InlineDispatch(Handler 쪽과 연동).

## Legacy 대비

스택별 Sender/Receiver 복제 → Pipeline 1 + Channel만 스택별. 공개 계층에 Sender/Receiver 이름 없음.

## 관련

- [[Session]] · [[Channel]] · [[Handler]] · [[Data-Flow]] · [[0004-send-options-and-handler-api]]
