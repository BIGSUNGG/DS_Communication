---
project: DS_Communication
type: decision
status: accepted
tags: [adr, security, tcp, tls]
updated: 2026-09-09
---

# ADR 0008 — TCP TLS(SslStream) 옵션 추가

- 날짜: 2026-09-09
- 상태: 승인(구현 완료)
- 관련: [[0002-tcp-backend-selection]], [[0006-session-ownership-and-converter]], [[../../04-Guides/Security|Security & Production Checklist]]

## 배경

프로덕션 체크리스트에서 **전송 암호화 부재**가 유일한 ❌ 항목이었다 — TCP·RUDP 모두 평문이었고, 공개망 투입 시 "별도 암호화 레이어(SslStream 등)를 겹쳐야 한다(로드맵 항목)"로만 문서화돼 있었다. 상용 Unity 게임 서버 전송 계층의 실서비스 취약 최우선 항목(스펙 개선 영역 1번 — 보안).

## 결정

1. **TCP에만 TLS를 넣는다** — `TcpTransportOptions.Tls`(`TcpTlsOptions`). 기본 `null`은 평문 그대로(하위호환, 공개 API는 추가만).
2. **핸드셰이크는 프레임 통신 전에 완료** — 서버는 `ServerCertificate` 설정 시 수락 연결마다 핸드셰이크를 먼저 완료하고 성공 시에만 `Accepted`에 채널을 전달한다. 클라이언트는 `Tls` 설정 시 핸드셰이크 후 `Channel`을 노출한다. 세션·파이프라인·프레이밍은 스트림 위에서 그대로 동작(`StreamByteChannel`에 확립된 스트림 생성자 추가).
3. **핸드셰이크 상한 15초(기본)** — `HandshakeTimeout`. 슬로로리스(연결만 열고 ClientHello를 끌어안기) 방어. netstandard2.1 SslStream 인증 API에 취소 토큰이 없어 시간 경쟁 + 스트림 폐기로 중단한다.
4. **서버 핸드셰이크는 수락 루프를 점유하지 않는다** — 연결별 태스크로 비동기 처리. 상한 슬롯은 수락 시점에 예약(동시 핸드셰이크 포함 상한 강제), 실패 시 핸드셰이크 태스크가·성공 시 채널 Dispose가 회수 — 정확히 1회.
5. **검증은 기본 OS 정책** — `RemoteCertificateValidation` 콜백은 개발용 자체 서명 수용 등 커스텀 정책용. 무조건 통과 콜백은 중간자 공격을 여는 것이므로 문서에서 금지 고지.
6. **RUDP에는 넣지 않는다** — LiteNetLib `XorEncryptLayer`는 기지평문 공격에 취약한 난독화일 뿐이라 "암호화"로 제공하지 않는다. RUDP 기밀성은 상위(TLS 유사 계층·VPN) 몫으로 남긴다.

## 결과

- TCP는 옵션 하나(`Tls`)로 공개망 투입이 가능해졌다. RUDP는 평문 유지(문서 고지).
- **TLS 1.3 노트**: 클라이언트가 인증서 검증 실패로 끊어도 **서버 측 핸드셰이크는 이미 완료돼 `Accepted`가 발생할 수 있다**(Schannel은 검증 결과를 핸드셰이크 완료 후 보고). 수용 핸들러는 언제나처럼 채널을 소유·정리해야 슬롯이 회수된다 — 이는 계약(채널 소유자는 구독자)의 재확인이지 예외가 아니다.
- 테스트 8건 추가(루프백 에코·기본 검증 거부·평문 클라이언트 거부·양측 핸드셰이크 상한·옵션 검증). Windows Schannel 테스트 인증서는 **PFX 재수입(UserKeySet|PersistKeySet)**으로 키를 지속시켜야 한다(ephemeral 키 서버 인증 불가), Linux는 serverAuth EKU 필요 — CI(ubuntu)·로컬(Windows) 모두 충족.
