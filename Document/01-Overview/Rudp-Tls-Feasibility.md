---
project: DS_Communication
type: overview
status: implemented
tags: [review, rudp, tls, dtls, security]
updated: 2026-09-08
---

# RUDP TLS 지원 가능성 검토

- 날짜: 2026-09-08
- 성격: 검토 — **구현 완료(동일일, ADR [[../05-Decisions/0009-rudp-tls-dtls|0009]]로 격상 · 패키지 2.5.0)**
- 관련: [[Audit-Full-Scan|Audit-Full-Scan]] §1 잔여 항목 · [[Production-Readiness-Review|Production-Readiness-Review]] 5번 · [[../04-Guides/Security|Security]] · [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]]

## 질문

RUDP는 평문 + 옵션 `Crc32cEnabled`(무결성 검출뿐, 기밀성·인증 없음). 공개망 클라이언트 직결 부적합 판정([[(Audit-Full-Scan)]])을 해소할 수 있는가 — "TLS"를 지원 가능하게 할 수 있는가.

## 결론

**가능하다.** 단, TCP의 `SslStream` 방식은 재사용 불가이며 **DTLS**(TLS over datagram)가 표준 해법. .NET BCL은 DTLS를 제공하지 않으므로 **BouncyCastle.Cryptography 도입이 전제**다.

| 확인 항목 | 결과 |
| --- | --- |
| `SslStream`의 UDP 사용 | 불가 — `Stream` 전용(바이트 스트림 가정). TCP TLS 패턴(핸드셰이크→프레이밍)의 스트림 결합부만 미적용 |
| .NET BCL DTLS | 부재 — `System.Net.Security`에 데이터그램 TLS 없음 |
| BouncyCastle.Cryptography | **확인됨** — 2.7.0이 netstandard2.0 대상(→ netstandard2.1·Unity 호환), `Org.BouncyCastle.Tls`에 `DtlsClientProtocol`·`DtlsServerProtocol`·`DtlsTransport`·`DtlsRecordLayer`(재전송·리플레이 윈도 포함) 제공 |
| LiteNetLib 자체 암호화 | 대체 불가 — `XorEncryptLayer`는 기지평문 취약 난독화(기존 판정과 동일) |

## 권장 통합 설계 (기존 구조와의 정합)

LiteNetLib 연결 확립(연결 키 수락) **후**, 이미 존재하는 reliable-ordered 채널 위에서 DTLS 핸드셰이크를 돌린다 — TCP의 "수락 → 핸드셰이크 → `Accepted`" 패턴(ADR 0008)을 그대로 미러.

1. **옵션**: `RudpTransportOptions.Tls` — `TcpTlsOptions`와 동일 필드 세트(`ServerCertificate` / `TargetHost` / `RemoteCertificateValidation` / `HandshakeTimeout`). API 일관성 유지.
2. **타이밍**: `RudpNetHost.OnPeerConnected`에서 채널 생성 → 핸드셰이크 **성공 후에만** `PeerAccepted` 발화. 실패·상한 초과는 채널 폐기 + 슬롯 회수(TCP 계약 동일 — 연결 고갈 방어 재사용).
3. **데이터 경로**: 채널 랩 — 송신 시 DTLS 레코드 인코딩, 수신 시 디코딩. DTLS 레코드는 메시지 경계를 보존하므로 프레이머 불필요(RUDP 채널의 메시지 지향 성격 유지). `RudpSendOptions`의 신뢰/비신뢰 구분도 그대로 — 레코드만 암호화된다.
4. **어댑터**: BC의 `DatagramTransport`는 pull 기반(블로킹 수신) — push 기반인 채널의 `MessageReceived`를 큐+대기로 갖다 붙이는 소형 어댑터 1개.

## 주의점

1. **폴링 스레드 블로킹 금지** — `OnPeerConnected`는 호스트 폴링 스레드에서 호출된다. 여기서 핸드셰이크를 동기 수행하면 전체 접속의 이벤트 드레인이 멈춘다. `Task.Run` 오프로드 + `HandshakeTimeout` 필수.
2. **이중 신뢰성(무해)** — 이미 reliable인 채널 위의 DTLS는 핸드셰이크 재전송·리플레이 윈도가 잠자기도 한다. 오버헤드만 있고 위험은 없음(가정의 부분집합). 대신 "레코드 레이어만" 단돸 사용은 핸드셰이크가 필요해 성립하지 않는다.
3. **MTU** — 레코드 오버헤드 약 13–29B. `ReliableOrdered`는 LiteNetLib 분할이 흡수하지만, 비신뢰 방식(분할 불가)은 payload 상한에서 오버헤드를 다시 빼야 한다(기존 MTU 사전 거부 검증과 충돌 없게).
4. **와이어 비호환** — TLS 켠 쪽 ↔ 안 켠 쪽은 핸드셰이크 실패로 끊긴다. `Crc32cEnabled`와 마찬가지로 양단 일치 필요 — 문서에 명시.
5. **성능** — BC 관리형 암호 구현은 `SslStream`(OS 네이티브) 대비 느리다. 메시지당 AES 계열 대칭 암호 비용이며, 병목이 실측되면 그때 대안(자체 AEAD 세션) 재검토. 선최적화 금지.
6. **의존성** — BouncyCastle.Cryptography 약 3–4MB, 순수 C#이라 Unity AOT 무난. RUDP.Shared에만 참조(LiteNetLib 은닉 패턴 재사용, ADR 0007).
7. **슬로로리스** — 핸드셰이크 상한은 TCP와 동일하게 "연결 열고 핸드셰이크 끌어안기" 방어에 쓴다(기본 15초 재사용 검토).

## 더 가벼운 대안

- **PSK 우선 지원** — 인증서 없이 사전 공유 키(BC가 PSK 스위트 지원). 인증서 운영 부담이 없어 내부망·신뢰 도메인 간 통신이면 이게 첫 단계로 더 작다. 공개망 서버 인증이 필요해지면 인증서 경로로 확장.
- **BCL만으로 자체 핸드셰이크**(P-256 ECDH + HKDF + AES-GCM 등 Noise류) — 의존성 0이지만 표준성·검증성 포기, 보안 프로토콜 자체 구현의 리뷰 부담. **추천하지 않음.**

## 상용화(Unity 서버) 구현 권장안 — 2026-09-08 확정 전제

**전제**: 이 라이브러리를 상용 Unity 게임 서버에 투입, 공개망 클라이언트 직결. 위 권장 경로(BouncyCastle DTLS)를 확정 권장으로 채택 — Unity 서버 조건이 오히려 BC 선택을 강화한다.

### Unity 런타임 관점에서 BC가 유리한 이유

- **AOT(IL2CPP/Mono) 안전** — BC는 순수 C#, 리플렉션 의존 적음. `SslStream`은 Unity Mono/IL2CPP에서 미실측([[Production-Readiness-Review]]의 "Unity 런타임 실측" 갭) — OS 네이티브 TLS 우회 자체가 리스크 회피다.
- **OS 인증서 스토어 불필요** — BC TLS는 자체 `Certificate` 타입을 쓰므로 파일(PEM/DER)에서 로드. 게임 전용 서버는 공개 CA+도메인이 없는 경우가 많다.
- **암호 프리미티브도 BC 제공** — X25519·P-256 ECDHE·AES-GCM·ChaCha20Poly1305가 전부 BC 자체 구현이라 Unity 플랫폼별 BCL 암호 편차의 영향을 받지 않는다.

### 프로토콜 버전·스위트

- **DTLS 1.2 기준 설계** — BC C#의 DTLS는 1.2 중심(활성 유지보수 확인: 재조립 버퍼 메모리 고갈 DoS 픽스 등). DTLS 1.3 C# 지원은 릴리스 노트상 미확인 — 채택 시점에 재확인하되 대응은 1.2로 간다.
- 권장 스위트: `TLS_ECDHE_*_WITH_AES_128_GCM_SHA256` 계열(ECDHE: X25519 또는 P-256, 인증서: ECDSA P-256 또는 RSA-2048+). AEAD는 게임 메시지(수십 바이트)에 오버헤드 최소.
- 이미 reliable 채널 위라 TLS 1.3의 주요 이점(1-RTT·강화된 핸드셰이크 프라이버시)의 체감이 작다 — 1.2 미지원을 만회할 이유 없음.
- **의존성 갱신 정책** — BC 과거 DTLS 결함(재조립 버퍼 DoS) 픽스 이력이 있으므로 최신 안정판(2.7.0+) 추적을 라이브러리 릴리스 절차에 포함.

### 인증·키 운영 (상용 체크리스트)

- **클라이언트 검증은 핀닝(SPKI SHA-256 지문) 기본** — 게임 전용 서버는 공개 도메인 인증서를 갖기 어렵다. BC의 `TlsAuthentication.NotifyServerCertificate`에서 지문 검사. 무조건 통과 콜백 금지는 [[../04-Guides/Security|Security]] 원칙과 동일.
- **서버 키 보호** — 개인 키 파일 권한·(Windows면 DPAPI) 보호, 로테이션은 이중 인증서 수용 또는 재시작 창으로.
- **PSK는 서버 간 인프라 링크 한정 옵션** — 클라이언트 내장 PSK는 추출 가능하므로 공개망 클라이언트엔 인증서 경로만.
- 기본 `ConnectionKey` 교체(공개 상수 — [[../04-Guides/Security|Security]] ⚠️ 행)와 병행. TLS는 전송 인증이지 앱 인증이 아니다.

### 구현 형태 (컴포넌트 5개)

1. `RudpTlsOptions` — `TcpTlsOptions` 필드 세트 + 핀닝 콜백.
2. `DatagramTransport` 어댑터 — 핸드셰이크 동안 내부 채널 `MessageReceived`를 큐로 받아 pull 제공(블로킹 대기 + 타임아웃).
3. 핸드셰이크 러너 — 폴링 스레드 밖 실행, `HandshakeTimeout` 강제, 실패 시 채널 폐기+슬롯 회수.
4. `RudpTlsChannel : IMessageChannel` — 송신 `DtlsTransport.Send`, 수신 pump(async 소비자 — 스레드 1개/접속 아님)가 레코드 복호화 후 `MessageReceived` 발화.
5. 와이어 결합 — `RudpNetHost.OnPeerConnected`·`RudpConnector.ConnectAsync`에 핸드셰이크 게이트.

### 게이트 (구현 전 반드시)

- **성능 게이트** — BC 관리형 대칭 암호 처리량을 Sandbox 벤치마크로 먼저 실측(메시지 크기·초당 처리). 부족하면 그때 탈출로(레코드 레이어 키 추출 또는 Noise류 자체 AEAD) 검토 — 선최적화 금지.
- **TCP 보완 계획(지금 만들지 않음)** — Unity 서버에서 `SslStream`이 문제를 일으키면 BC `TlsServerProtocol`(Stream 위 TLS)로 TCP TLS 백엔드도 동일 의존성으로 교체 가능. RUDP TLS 실측 이후 판단.
- **테스트** — 루프백 핸드셰이크·핀닝 실패 거부·상한 초과·신뢰/비신뢰 혼합 송신·큰 payload 분할·핸드셰이크 중 끊김 회수.

## 판정 요약

- 지원 가능. 권장 경로: **BouncyCastle DTLS + 연결 후 reliable 채널 핸드셰이크 + 채널 랩 암호화**, 옵션·계약은 ADR 0008의 TCP TLS와 동일 계열로 유지.
- 공수: 옵션 + 어댑터 + 채널 랩 + 리스너/커넥터 분기 + 루프백 테스트 — ADR 0008(TCP TLS) 구현과 비슷하거나 어댑터만큼 크다.
- 완화되는 기존 판정: Audit-Full-Scan §1 "RUDP 기밀성 없음 — 실 해결은 범위 밖"의 **범위 밖 항목에 대한 실현 가능성 답변**. Production-Readiness-Review의 "공개망엔 TCP TLS 또는 VPN" 대안이 하나 늘어난다.
