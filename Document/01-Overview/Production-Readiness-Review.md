---
project: DS_Communication
type: overview
status: stable
tags: [review, production, readiness]
updated: 2026-09-09
---

# Production-Readiness Review — 상용 서버 라이브러리 투입 검토 (2026-09-09)

> 질문: "이 프로젝트를 실제 유니티 서버 상용화 서비스의 라이브러리로 사용해도 문제가 없는가?"
> 방법: Source 전 30파일(~3.3k줄) 직접 열독 + 테스트 실행 + netstandard2.1 Release 빌드 검증. [[Audit-Full-Scan|감사 문서]]의 주장을 코드로 재확인.

## 결론

**조건부 사용 가능.** TCP(+옵션 TLS) 스택은 상용 전용 서버 투입 가능 수준. RUDP는 **기본 평문**이지만 2.5.0부터 옵션 DTLS 1.2(`RudpTransportOptions.Tls`, [[../05-Decisions/0009-rudp-tls-dtls|ADR 0009]])로 기밀성 확보 가능 — DTLS 미설정시에는 여전히 공개망 클라이언트 직결에 부적합(신뢰망·VPN 전제). 단, 실서비스 소크·부하 실측이 없어 "몇 접속까지, 어떤 처리량"에 대한 근거는 부재 — 점진적 트래픽 노출로 검증하며 투입해야 한다.

## 검증 근거 (직접 확인)

| 항목 | 결과 |
| ------ | ------ |
| 테스트 | 148/148 통과 (net10.0, 26초) — 고갈 공격 회귀 2건 + 폭풍 청urn 소크 2건(TCP 16동시×3 wave RST/FIN 혼합·RUDP 상한 거부 혼합·포트 재바인딩 — RUDP 즉시, TCP 정지 후 같은 포트 재시기) 포함 |
| netstandard2.1 Release 빌드 | 오류 0 (Unity 2021.2+ API 레벨 호환 타깃) |
| 동시성 설계 | 끊김 1회 래치·늦은 구독자 재생·SignalGate — 경쟁 경로 전부 가드 확인 |
| 와이어 방어 | MaxFrameLength(기본 4MB)·FrameTimeout(30s)·선할당 없는 수신 버퍼·fail-closed 확인 |

## 코드 강점 (열독으로 확인한 것)

1. **끊김 정확성** — `Session`: 세션당 정확히 1회, 늦은 구독자 즉시 재생, 구독자 예외 격리, send-after-disconnect는 동기 throw 아님.
2. **백프레셔 양방향** — 송신 슬롯(`MaxPendingMessages`) + 수신 흐름 제어(RUDP 경로 초과 시 `FlowControl` 단절). 메모리 무제한 누적 경로 없음.
3. **송신 격리** — 직렬화 실패는 항목만 격리(세션 유지), 부분 프레임 되감기. coalesce 배치 + ArrayPool + 지연 컴팩트(O(N²) 회피).
4. **TCP 수락 강건성** — `MaxConnections` 초과 즉시 닫기 + Dispose 훅으로 슬롯 회수, TLS 핸드셰이크 15초 상한 + 정지 후 늦은 완료 폐기, 수락 예외 50ms 백오프 재시도.
5. **RUDP 격리** — 호스트당 폴링 스레드 1개, 수락 전 슬롯 예약, peer id 재사용 소유자 확인 회수, 세션 생성 창구 래치, 비분할 방식 MTU 사전 거부, 클라이언트 역방향 접속 거부, 기본 키 시작 경고.
6. **폴링 루프 생존성** — 폴링 예외 격리 + 동일 오류 초당 1회 로그 제한(도배 방지).

## 상용화 전 갭 (투입 판단에 남는 것)

1. **소크 부재(부분 완화)** — 자동화된 청urn 소크(동시 접속 폭풍·RST/FIN 혼합 이탈·포트 재사용 — TCP는 TIME_WAIT 잔존 시 한도 내 재시기·슬롯 회복)는 테스트 스위트로 들어왔으나(2026-09-09, 148테스트), **24시간+ 장기 실서비스 소크**(누수·재접속 폭주의 실트래픽 검증)는 여전히 미수행 — 스테이징 실측 필요.
2. **벤치마크 부재** — 동시 접속 수·처리량 스펙 산정 근거 없음. 용량 계획은 스테이징 실측 후.
3. **관측성** — `Trace` 기반만. 접속 수·큐 깊이·끊김 원인 카운터 같은 메트릭은 앱이 `Session` 이벤트·`ActiveConnectionCount`로 직접 수집해야 한다. Unity 전용 서버에선 Trace 리스너 설정 필요(기본 무출력).
4. **송신 타임아웃 없음** — 수신이 멈춘 피어에 대한 `WriteAsync`는 소켓 닫기 전까지 대기 가능. half-open 감지는 **앱 하트비트(응답 기반)·OS keep-alive 책임**(문서화됨) — 반드시 앱이 응답 타임아웃을 구현.
5. **RUDP 기본 평문** — DTLS 미설정시 평문 UDP + 키 없는 CRC. 공개망 클라이언트 통신은 `Tls` 옵션(DTLS 1.2, 2.5.0+) 또는 TCP TLS/VPN. **세션 생성 창구 계약**: RUDP `Accepted` 통지 시점에 세션을 동기 생성해야 한다 — 채널을 다른 스레드로 넘겨 생성을 미루면 그 사이 도착한 메시지는 유실(메시지 단위 채널은 구독 전 버퍼링 없음, TCP는 스트림 버퍼링으로 무관).
6. **Unity 런타임 실측 부재** — netstandard2.1 컴파일은 확인했으나 Unity(Mono) 리눅스/윈도 전용 서버 빌드에서의 실동작은 미검증.

## 앱 책임 (투입 시 반드시 이행 — [[../04-Guides/Security|Security]] 체크리스트)

인증·세션 관리, 하트비트·재접속, 메시지 레벨 속도 제한, 안전한 `IMessageConverter` 선택(RCE 유발 직렬화기 금지), 공개망에선 `ConnectionKey` 기본값 교체.

## 권고 투입 절차

1. TCP + TLS 구성으로 시작(RUDP는 신뢰망 한정).
2. 스테이징에서 24시간+ 소크 — 재접속 폭주·비정상 종료 시나리오 포함.
3. 부하 실측으로 `MaxConnections`·`MaxPendingMessages`·`MaxFrameLength` 튜닝.
4. 앱 계층에 응답 타임아웃·메트릭 수집 구현 후 상용 노출.

## 관련

- [[Audit-Full-Scan]] · [[../04-Guides/Security|Security]] · [[Scope]]
