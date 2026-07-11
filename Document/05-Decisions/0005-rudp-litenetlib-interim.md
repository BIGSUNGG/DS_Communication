---
project: DS_Communication
type: adr
status: draft
tags: [adr, rudp, litenetlib]
updated: 2026-07-11
---

# ADR 0005: RUDP via LiteNetLib (interim)

## Status

Accepted

## Context

RUDP 스택이 필요하고, 자체 신뢰 UDP 스택은 범위가 크다. 레거시도 LiteNetLib를 사용했다.

## Decision

1. **현재 `Communication.Network.RUDP`는 LiteNetLib에 의존**해 `IMessageChannel` / Session을 구현한다.
2. LiteNetLib 타입은 패키지 **내부에 감추고**, 앱 공개면은 Shared·RUDP 패키지의 Session/Connector/Listener/`RudpSendOptions`만 노출한다.
3. **이후** LiteNetLib 없는 **자체 RUDP 구현은 별 프로젝트/패키지**로 도입한다 (이 저장소 RUDP를 당장 교체하지 않거나, 교체 시 major). 일정은 구현 완료 후.
4. LiteNetLib keep-alive와 앱 하트비트는 앱이 조율한다 (라이브러리 하트비트 없음 — [[0003-connection-lifecycle-options]]).

## Consequences

### Positive

- RUDP를 빠르게 검증·Sandbox 채팅까지 가져갈 수 있다.
- 이후 자체 구현 시 Channel 계약만 맞추면 Pipeline/Session을 재사용한다.

### Negative

- 외부 의존·라이선스·버전 고정이 필요하다.
- 자체 구현 전까지 LiteNetLib 특성(폴링 등)이 스택에 남는다.

## 관련

- [[Implementation-Roadmap]]
- [[Packages]]
- [[Channel]]
