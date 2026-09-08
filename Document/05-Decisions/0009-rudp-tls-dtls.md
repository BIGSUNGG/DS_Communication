---
project: DS_Communication
type: decision
status: accepted
tags: [adr, security, rudp, tls, dtls]
updated: 2026-09-08
---

# ADR 0009 — RUDP TLS(DTLS 1.2, BouncyCastle) 옵션 추가

- 날짜: 2026-09-08
- 상태: 승인(구현 완료)
- 관련: [[0007-rudp-three-way-split-and-polling]], [[0008-tcp-tls-sslstream]], [[../01-Overview/Rudp-Tls-Feasibility|Rudp-Tls-Feasibility]], [[../04-Guides/Security|Security & Production Checklist]]

## 배경

Audit-Full-Scan §1의 잔여 항목 "RUDP 기밀성 없음" — 평문 UDP + 키 없는 CRC32c로는 공개망 클라이언트 직결이 불가능하다는 판정이었다. 실 해결(DTLS급)은 LiteNetLib 임시 스택 범위 밖으로 미뤄져 있었고, [[../01-Overview/Rudp-Tls-Feasibility|Rudp-Tls-Feasibility]] 검토(2026-09-08)가 상용 Unity 서버 전제의 구현 경로를 확정했다: `SslStream`은 UDP에 못 쓰고(스트림 전용), .NET BCL에 DTLS가 없으므로 **BouncyCastle.Cryptography** 도입이 전제.

## 결정

1. **DTLS 1.2 + BouncyCastle.Cryptography 2.7.0** — `RudpTransportOptions.Tls`(`RudpTlsOptions`) 설정 시 연결 확립(LiteNetLib 키 수락) **후** 이미 신뢰적인 채널 위에서 DTLS 핸드셰이크를 완료한 뒤에만 채널을 전달한다. TCP TLS(ADR 0008)의 "수락 → 핸드셰이크 → `Accepted`" 패턴 미러. 기본 `null` = 평문(기존 동작 유지).
2. **BC 타입은 공개면 은닉** — `RudpTlsOptions`는 `X509Certificate2`·콜백만 노출, 어댑터(`DatagramTransport`)·핸드셰이크 러너·채널 랩은 전부 internal. 의존성은 RUDP.Shared에만(ADR 0007 LiteNetLib 은닉 패턴 재사용).
3. **클라이언트 검증은 fail-closed** — OS 인증서 스토어 검증 계약이 없으므로 `RemoteCertificateValidation`(핀닝, 게임 표준 경로) 또는 `TargetHost`(SAN/CN 일치) 중 하나를 반드시 설정한다. 둘 다 없으면 서버 인증서를 **기본 거부**한다. 무조건 통과 콜백 금지는 TCP와 동일 원칙.
4. **메시지 경계 보존 — 내부 3바이트 봉투 + 청킹** — TLS 명세상 레코드 평문은 16,384바이트 상한(압축 길이 uint16)이다. 채널 랩이 모든 레코드에 `[flags(1)][len(2)]` 봉투를 붙이고, 16,381바이트 초과 메시지는 `ReliableOrdered`에서만 다중 레코드로 청킹해 수신측이 재조립한다(상한 64MB). 그 외 전송 방식(비분할)의 초과 payload는 기존 MTU 사전 거부 철학대로 `ArgumentException`으로 즉시 실패. 봉투 위반 수신은 fail-closed(채널 폐기 → 세션 단절).
5. **스위트·버전** — DTLS 1.2 고정(BC C#의 DTLS 1.3은 미확인·신뢰 채널 위 이점 제한), ECDHE + AES-GCM(RSA-2048+/ECDSA P-256 인증서, 인증서 키 종류가 스위트 선택). 핸드셰이크 상한 기본 15초 — 폴링 스레드 밖 전용 태스크에서 실행, 실패·상한 초과는 채널 폐기 + 슬롯 회수(TCP 계약 동일).
6. **수신 복호화는 펌프 태스크** — 폴링 스레드가 아니라 연결당 비동기 펌프(대기 중 비용 없음)가 레코드를 복호화한다 — 단일 폴링 스레드가 모든 접속의 암호 연산을 점유하지 않는다(ADR 0007 병목 구조 유지).

## BouncyCastle 이중 빌드 함정 (구현 노트)

BC 2.7.0의 **netstandard2.0 빌드에는 Span 기반 `Receive`/`Send` 오버로드가 없고** net6.0+ 빌드에만 있다. netstandard2.1 어셈블리에서 구현하면 컴파일타임엔 인터페이스 구현으로 묶이지 않아(비가상 메서드로 발행됨) .NET 6+ 호스트가 net6.0 빌드를 로드하는 순간 **TypeLoadException**이 난다. 대응: 어댑터의 Span 오버로드를 `virtual`로 발행하고 클래스 봉인을 해제한다(양쪽 빌드를 모두 만족). Unity(Mono/netstandard 프로파일 → netstandard2.0 빌드 로드)에서는 Span 오버로드가 단순 여분 메서드로 남아 무해.

또 하나: BC의 Span 기반 수신은 **빈 큐에서 무기한 블록**할 수 있어 펌프가 락을 잡은 채 멈추면 송신까지 교착된다 — 펌프는 `HasPendingData`(원시 레코드 큐 잔량) 확인 하에서만 `Receive`를 호출한다.

## 검증

- 테스트 11건 신설(`RudpTlsTests`, 135 → 146): 핀닝 연결 왕복(RSA·ECDSA)·TargetHost 일치/불일치·핀닝 불일치 거부·검증 수단 없음 기본 거부·핸드셰이크 상한(평문 피어) 슬롯 회수·핸드셰이크 중 끊김 슬롯 회수·전송 방식 혼합 왕복·200KB 청킹 왕복·비분할 방식 상한 거부.
- Sandbox `Chat.RUDP --tls-selftest`(핀닝·전 방식·청킹) 통과, `--bench [--tls]` 성능 게이트 측정: **평문 1,790 msg/s vs TLS 1,703 msg/s(512B 에코 왕복, 약 5% 오버헤드)** — 게임 메시지 크기에서 BC 관리형 암호 비용은 전송 지연 대비 미미. 4KB 메시지 1.33 MB/s.

## 결과

- Audit-Full-Scan §1 "RUDP 기밀성 없음" 해소, Production-Readiness "공개망엔 TCP TLS 또는 VPN" 제약 완화.
- 의존성 +1(BouncyCastle.Cryptography, RUDP.Shared에만). 패키지 2.5.0.
- 명시적 제외(후속 검토): DTLS 1.3, PSK, TCP TLS의 BC 백엔드 교체(Unity Mono `SslStream` 실측 후), 세션 재개.
