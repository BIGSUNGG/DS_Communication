---
project: DS_Communication
type: overview
status: draft
tags: [proposal, upstream, ds-rpc, security]
updated: 2026-09-09
---

# Proposals — 상위 저장소(DS_RPC) 활용 제안

> 스펙 개선 영역 5의 의무 절차: DS_Communication이 만든 기능 중 상위가 활용하면 좋은 것을 이 노트에 기록한다.
> **상위 저장소는 직접 수정하지 않는다** — 반영 여부·시점은 DS_RPC 루프의 몫.

## 배경

DS_RPC → DS_MessageProtocol, DS_Communication 의존. DS_Communication 2.1.0~2.3.0에서 전송 보안·운영 옵션이 추가됐다 — 상위가 통과시켜 주기만 하면 앱이 즉시 쓸 수 있는 것들이다.

## P1 — TCP TLS 노출 (2.1.0+)

- **제안**: DS_RPC의 전송 구성(TCP 커넥터·리스너 옵션)이 `TcpTransportOptions`를 앱에 통과시키거나 최소한 `Tls`(`TcpTlsOptions`)를 노출한다.
- **근거**: 공개망 RPC 엔드포인트의 기밀성·서버 인증은 현재 앱이 TLS 옵션을 못 넘기면 불가능하다.
- **주의 전달**: TLS 1.3에서 클라이언트가 인증서를 거부해도 서버 측 `Accepted`는 발생할 수 있다(Schannel이 검증 결과를 핸드셰이크 완료 후 보고) — DS_RPC 수용 핸들러도 채널 소유·정리 계약을 지켜야 한다. 핸드셰이크 상한은 `TcpTlsOptions.HandshakeTimeout`(기본 15초).

## P2 — RUDP CRC32c 일괄 적용 (2.2.0+)

- **제안**: DS_RPC가 RUDP 전송을 구성할 때 `RudpTransportOptions.Crc32cEnabled`를 클라이언트·서버 양단에 **같은 값**으로 설정한다(와이어 비호환 — 한쪽만 켜면 통신 불가).
- **근거**: 신뢰 불가 채널(Unreliable·Sequenced)을 쓰는 RPC 경로에서 손상 패킷의 사전 폐기(프로토콜 처리 전)가 무결성 사고를 줄인다. IPv4 UDP 체크섬은 0일 수 있다.

## P3 — 연결·프레임 타임아웃 정책 통일 (2.0.x+)

- **제안**: DS_RPC 재접속 정책이 `ConnectTimeout`(TCP·RUDP), `FrameTimeout`(`MessageQueueOptions`, 기본 30초 — 슬로로리스 방어)를 노출해 앱이 RPC 레벨 정책으로 통합하게 한다.
- **근거**: 침묵 경로(블랙홀)에서 OS 기본 SYN 재시도(수십 초)보다 낮은 상한이 RPC 체감 지연을 결정한다.

## P4 — 운영 신호 소비

- **제안**: `listener.ActiveConnectionCount`(TCP·RUDP)와 `DisconnectReason`(특히 `FlowControl` — 수신 미처리 상한 단절)을 DS_RPC 연결 관리자가 구독해 백프레셔·서버 포화 상태를 상위 메트릭으로 올린다.
- **근거**: `MaxPendingMessages` 초과 단절은 "서버가 느리다"의 정확한 신호다 — 재접속 폭주 대신 백오프 지표로 쓸 수 있다.

## 관련

- [[../04-Guides/Getting-Started|Getting-Started]] §6 전송 보안 옵션
- [[../05-Decisions/0008-tcp-tls-sslstream|ADR 0008]] · [[../04-Guides/Security|Security & Production Checklist]]
