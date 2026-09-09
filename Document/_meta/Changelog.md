---
project: DS_Communication
type: overview
status: draft
tags: [meta, changelog]
updated: 2026-09-09
---

# Changelog

Document vault 변경 기록 (코드 릴리스 노트 아님).

## 2026-09-09 (루트 문서 영어화 — README·TCP·RUDP)

- **루트 문서 3종 영어 (재)작성** — `README.md`(소개·Quick Start·전 기능 사용법·보안 요약), `TCP.md`·`RUDP.md`(전송별 사용법 신설, 루트). 서브에이전트 writer/reviewer 루프로 Source 대조 검증. 코드 무변경 — 문서만 변경

## 2026-09-09 (2.5.1 릴리스 — 상용 하드닝)

- **패치 릴리스 2.5.1** — 공개 API 변화 없음. 하기 같은 날 섹션의 상용 하드닝(수용 루프 생존성·TLS/plain 경로 스트림 생성 가드·`TcpConnector` 정리·`OnNetworkError` 로그 제한·폭풍 청urn 소크·문서 동기화)을 전 패키지 7종 통일 버전으로 게시. 게시 관문(CI verify: build+test+sandbox selftest) 통과 확인 후 태그 `v2.5.1`·`tcp/v2.5.1`·`rudp/v2.5.1`

## 2026-09-09 (상용 하드닝 — 수용 루프 생존성·폭풍 청urn)

- **TCP 수용 루프 생존성 결함 수정** — `TcpListener.AcceptLoopAsync`에서 소켓 옵션 적용(`NoDelay`)이 try 밖에 있어, 수용 직후 상대가 RST로 끊는 경합에서 예외가 나면 수용 루프가 조용히 죽어 **서버 전체가 연결을 받지 못하는 전면 장애**가 됐었다. 옵션 적용 실패는 해당 연결만 버리고 수용 계속으로 격리. 같은 클래스의 `HandshakeTlsAsync`는 `SslStream` 생성(GetStream 포함)이 try 밖이라 스트림 확보 실패 시 **상한 슬롯 미회수 + 클라이언트 누수 + 미관찰 태스크 예외**로 새었다 — 생성까지 실패 범위에 넣고 null 안전 정리
- **`TcpConnector` 동일 계열 수정** — TLS 스트림 생성 try 진입·소켓 옵션 적용 가드(실패는 연결 실패 `false` 확정, 예외 방출·리소스 누수 없음)
- **폭풍 청urn 소크 회귀 2건 추가(146 → 148)** — `ChurnSoakTests`: TCP 16동시×3 wave(절반 RST·절반 FIN 즉시 이탈 + 절반 왕복), RUDP 10동시×상한 4(수용·거부·즉시 이탈 혼합). 검증: 매 wave `ActiveConnectionCount` 0 회복(슬롯 누수 없음)·태그 정합성·폭풍 뒤 서버 생존·포트 재바인딩(RUDP는 즉시 — UDP엔 TIME_WAIT 없음. TCP는 리스너 정지 후 같은 포트 — 서버 선행 종료 소켓이 TIME_WAIT에 남으면 일시 거부되어 15초 한도 재시기). 동시 RST 청urn은 수용 직후 끊김 경합을 확률적 노출
- **RUDP 세션 생성 창구 계약 문서화** — 메시지 단위 채널은 구독 전 도착 메시지를 버퍼링하지 않는다: `RudpListener.Accepted` 통지 시점에 세션을 **동기 생성**해야 하고, 채널을 다른 스레드로 넘겨 생성을 미루면 그 사이 메시지가 유실된다(TCP는 스트림 버퍼링으로 동일 창구 없음) → [[../03-Reference/Public-API|Public-API]]·`RudpListener.Accepted` XML 문서
- **볼트 동기화** — [[../01-Overview/Production-Readiness-Review|Production-Readiness-Review]]·[[../01-Overview/Audit-Full-Scan|Audit-Full-Scan]]의 2.5.0 이전 잔여 주장("RUDP 기밀성 없음") 현행화(DTLS 옵션 존재, 기본은 평문 유지), 소크 갭 표기 갱신(자동 청urn 소크 추가, 24시간+ 실서비스 소크는 여전히 부재)
- **리뷰 후속 정밀화(같은 날)** — ① TCP 폭풍 테스트가 주장만 하고 수행하지 않던 포트 재바인딩을 실제로 수행(TCP 꼬리 신설 — 리스너 정지 후 같은 포트 15초 한도 재시기, TIME_WAIT 정직 서술) ② plain TCP 경로의 `StreamByteChannel` 생성(GetStream)이 가드 밖이던 사각 제거 — `TcpListener`·`TcpConnector` 비TLS 경로, Mono 계열 런타임의 관측 단절 소켓 throw에도 루프 생존·정리·슬롯 회수(훅 미등록 시 catch에서 회수 — exactly-once 유지) ③ `RudpNetHost.OnNetworkError` 초당 1회 로그 제한(ICMP unreachable 폭풍 도배 방지 — 폴링 예외와 동일 관례) ④ 문서 정확화: [[../00-AI/CONTEXT|CONTEXT]] 낡은 테스트 수(135→148)·ADR 읽기 목록에 0009 추가, Public-API 오탈자

## 2026-09-08 (RUDP TLS 구현 — 2.5.0)

- **ADR [[0009-rudp-tls-dtls]] — RUDP 옵션 TLS(DTLS 1.2, BouncyCastle.Cryptography 2.7.0) 구현** — 검토([[../01-Overview/Rudp-Tls-Feasibility|Rudp-Tls-Feasibility]])를 ADR로 격상하고 구현 완료. `RudpTransportOptions.Tls`(`RudpTlsOptions`) — 연결 확립 후 신뢰 채널 위 핸드셰이크 선행 완료(폴링 스레드 밖, 상한 15초, 실패·초과 폐기+슬롯 회수), 클라 검증 핀닝/`TargetHost` 필수(미설정 기본 거부), BC 타입 공개면 비노출(ADR 0007 패턴), 메시지 경계 보존은 내부 3바이트 봉투+청킹(16,381B 초과 `ReliableOrdered`만, 64MB 상한, 위반 fail-closed). 구현 함정 2건 해결 기록 — BC 이중 빌드(netstandard2.0에 Span 오버로드 없음 → virtual 발행), 빈 큐 BC 수신 무기한 블록(펌프 `HasPendingData` 가드)
- **테스트 135 → 146** — `RudpTlsTests` 11건: 핀닝 왕복(RSA·ECDSA)·TargetHost 일치/불일치·핀닝 불일치 거부·기본 거부·상한 슬롯 회수·중 끊김 슬롯 회수·전 방식 혼합·200KB 청킹·비분할 상한 거체
- **Sandbox** — `Chat.RUDP --tls-selftest`(핀닝·전 방식·청킹)·`--bench [--tls]` 성능 게이트 추가. **벤치마크(512B 에코 왕복): 평문 1,790 msg/s vs TLS 1,703 msg/s ≈ 5% 오버헤드** — 게임 메시지 크기에서 BC 관리형 암호 비용은 전송 지연 대비 미미(4KB: 1.33 MB/s)
- **볼트 동기화** — [[../04-Guides/Security|Security]] ❌→✅(RUDP 기밀성), [[../01-Overview/Feature-Spec|Feature-Spec]] F4-10, [[../03-Reference/Public-API|Public-API]](`RudpTlsOptions`·TLS 런타임 의미), [[../03-Reference/Configuration|Configuration]](`Tls` 행), [[../03-Reference/Packages|Packages]](BC 의존 규칙), [[../04-Guides/Getting-Started|Getting-Started]] §6 RUDP TLS 예시, [[../00-AI/CONTEXT|CONTEXT]] 갱신, 패키지 7종 **2.4.1 → 2.5.0**(미태그·미배포 — 릴리스는 별도 확인 후)
- **2.5.0 배포 완료(후일 같은 날)** — 커밋 `aff1d3e` → 태그 ×3(`v2.5.0`·`tcp/v2.5.0`·`rudp/v2.5.0`) → Actions 4건 전부 성공(검증 ubuntu: Release 빌드 + 테스트 146 전수 + Sandbox 셀프테스트, 게시 3건 — "Your package was pushed"). NuGet 7종 2.5.0 업로드 확인

## 2026-09-08 (RUDP TLS 검토)

- **[[../01-Overview/Rudp-Tls-Feasibility|Rudp-Tls-Feasibility]] 신설** — "RUDP에 TLS 지원 가능한가" 검토. 결론: **가능** — SslStream은 UDP 불가, 표준 해법 DTLS, BCL 부재 → BouncyCastle.Cryptography(netstandard2.0, DTLS 클래스 확인) 도입 전제. 권장 설계: LiteNetLib 연결 확립 후 reliable 채널 위 핸드셰이크(ADR 0008 패턴 미러) + 채널 랩 레코드 암호화. 주의점 7건(폴링 스레드 블로킹 금지·MTU·와이어 비호환·BC 성능 등)과 경량 대안(PSK 우선) 기록. 승인 시 ADR 0009로 격상 예정
- **동 노트에 상용화(Unity 서버) 구현 권장안 추가** — BC DTLS 확정 권장(AOT 안전·OS 스토어 불필요·BC 프리미티브로 플랫폼 편차 회피), DTLS 1.2 기준(BC C# 1.3 미확인), 클라 검증은 핀닝 기본, PSK는 인프라 링크 한정, 성능 게이트(Sandbox 벤치마크 선행), TCP SslStream 실패 시 BC TLS 교체 가능성 기록

## 2026-09-09 (상용 투입 검토)

- **[[../01-Overview/Production-Readiness-Review|Production-Readiness-Review]] 신설** — Source 전 30파일 열독 + 테스트 135/135 실행 + netstandard2.1 Release 빌드 검증. 결론: 조건부 사용 가능(TCP+TLS 상용 투입 가능, RUDP는 신뢰망 한정). 갭: 소크·벤치마크·관측성·송신 타임아웃·Unity 런타임 실측. [[../00-AI/CONTEXT|CONTEXT]] 관련 노트 링크 추가

## 2026-09-09 (릴리스 2.4.1)

- **패키지 2.4.1 배포(patch)** — 정지 후 완료된 TLS 핸드셰이크 늦은 `Accepted` 차단(`1fe96aa`, 테스트 135). 커밋 `e018fbf` → 태그 ×3 → Actions 4건 전부 성공. 표기 갱신 — 세션 8번째 릴리스(2.0.1→2.4.1)

## 2026-09-09 (사이클 26 — 정지 중 TLS 핸드셰이크 늦은 콜백 차단)

- **PENDING P3 ② 해소** — `TcpListener` 정지 이후 완료된 TLS 핸드셰이크가 `Accepted`를 늦게 발화하던 타이밍 결함 차단: 핸드셰이크 완료→전달 사이에 수락 루프 토큰(Stop이 취소)을 검사해 폐기+슬롯 회수. 보류 사유(테스트 불가·등록부 위험)가 재분석으로 무너짐 — 늦은 ClientHello 클라이언트로 결정적 재현. 테스트 +1(134→135). 루프 종료 시점 종결 단위 — 미출시(다음 릴리스 단위 후보)

## 2026-09-09 (사이클 22 — 잔여 노트 동기화)

- **[[../00-AI/GLOSSARY|GLOSSARY]]·[[../01-Overview/Feature-Spec|Feature-Spec]] 최신화** — 세션 중 반영이 누락됐던 마지막 두 노트. Feature-Spec: F4-7 TCP TLS·F4-8 RUDP CRC32c·F4-9 기본 키 경고 신규 행, F2-3 늦은 구독 재생 서술. GLOSSARY: `TcpTransportOptions`·`TcpTlsOptions`·끊김 재생 보장 신규, `RudpTransportOptions` 행에 `ConnectTimeout`·`Crc32cEnabled` 추가. 06-Troubleshooting은 미생성 — 실제 문제 발생 전 생성은 억지스러운 작업이라 판단·기록만

## 2026-09-09 (사이클 21 — 홈 노트 동기화)

- **[[../01-Overview/Home|Home]](사람용 진입점) 최신화** — 2.4.0 현행 반영: 테스트 110→134, 전송 보안 옵션 행(TLS·CRC32c·기본 키 경고) 추가, TCP_IOCP "미착수"→보류(2026-09-08 결정 서술), ADR 목록 0008 포함, 감사·상위 제안 노트 링크, 재생 보장 서술. YAML 갱신

## 2026-09-09 (사이클 20 — 테스트 스위트 강화)

- **신규 컨텍스트 리뷰어의 테스트 스위트 감사(124테스트 전수) → VACUOUS 1·WEAK 6 전량 강화**
  - **[VACUOUS] `Tls_Server_RejectsPlaintextClient`** — Accepted 즉시 폐기 구독자가 count→0을 만드는 경로와 거부가 구분 안 됐다 → `acceptedCount==0` 단언 추가(평문 수용 회귀 탐지 가능)
  - **[WEAK×6]** — TCP 에코 완복 내용 미검증(`ping`/`pong` 서술 추가), keep-alive **배선** 미검증(소켓 옵션==1 직접 단언 — Apply 호출 누락 회귀 탐지), 클라이언트 TLS 상한의 시간 증거 부재(Stopwatch 범위 200–2000ms + WaitAsync 상한), coalesce 테스트의 샘플링 경쟁(SendAndFlush 완료 후 세는 결정적 구조로), 청urn 태그 `Contains`→정확 시퀀스(혼입·중복 탐지) ×2
  - 감사 요약: 124중 117 SOUND — 이벤트 핸들러 내 단언·정적 상태 누출·예외 타입 모호성 등 나머지 사냥 축은 청정 확인

## 2026-09-09 (릴리스 2.4.0)

- **패키지 2.4.0 배포(minor)** — 2.3.1 이후 단위: ①RUDP 리스너 정지 무통지 방치 수정(살아있는 세션에 `Local` 통지 + 죽은 피어 송신 즉시 실패) ②끊김 통지 전달 보장(채널 래치 + `Session.Disconnected` 늦은 구독자 즉시 재생 — 공개 이벤트 보장 강화 → minor). 커밋 `dd968a6` → 태그 ×3 → Actions 4건 전부 성공(**경화된 게시 워크플로 첫 태그 실행** — env 간접화 pack·persist-credentials 없음 실증). [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 표기 갱신

## 2026-09-09 (사이클 18 — 끊김 통지 전달 보장)

- **늦은 구독자 끊김 통지 보장(보류 P3 ① 완전 폐쇄)** — ①`RudpMessageChannel` 래치: 구독자 없이 발생한 전송 단절을 원인과 함께 기록, `RudpSession` 생성자가 구독 직후 회수(이벤트·래치 양쪽 경로 정확히 1회) ②`Session.Disconnected` 커스텀 접근자 — 이미 끊긴 세션에 늦게 구독해도 즉시 1회 재생(구독당 1회·표준 이벤트 의미론, 락으로 구독·발화 경쟁 폐쇄, 구독자 예외 격리 유지). 테스트 +3(131→134): 늦은 구독 재생·구독당 1회·수용 창구 시뮬. 도중 내 테스트 기대 오류(재구독 중복 가정) 1건 — 표준 의미론으로 정정. [[../02-Architecture/Session|Session]]·[[../03-Reference/Public-API|Public-API]] 동기화. DS_RPC 회귀 77/77

## 2026-09-09 (사이클 17 — 독립 리뷰 기반 RUDP 정지 통지 결함 수정)

- **신규 컨텍스트 reviewer 서브에이전트 적대적 결함 리뷰(전 Source 30파일) → P2 1건·P3 4건**
  - **[P2 수정] `RudpNetHost.Stop` 무통지 세션 방치** — 폴링 스레드 정지로 NetManager 끊김 이벤트가 드레인되지 않아 리스너 Stop/Dispose 시 서버 세션이 끊김을 영원히 모르고(UDP엔 EOF 없음) 송신도 죽은 피어로 조용히 유실됐다. `Stop`이 살아있는 채널에 `ReleaseChannel(Local)`로 직접 통지(채널 Dispose의 소유자 확인으로 중복 없음). `RudpMessageChannel.SendAsync`에 피어 `ConnectionState` 가드 추가 — 침묵 유실 대신 즉시 실패. 테스트 +1(130→131): Stop→서버 세션 `Disconnected(Local)`
  - **[P3 수정] `EnqueueAsync` flush 경쟁의 비휘발성 읽기** — 스토퍼의 `Volatile.Write`와 짝이 안 맞아 이론적 flush 무한 대기 가능성 → `Volatile.Read`로 폐쇄
  - **[P3 보류 3건 → PENDING]** — 수용 경칭 래치 설계, Stop 후 TLS 핸드셰이크 최대 15초 지연 콜백, 프레이머 경계 CTS 재사용(재실장 시 위험 설계 노트)
  - 리뷰가 청정 확인한 축: 슬롯 회수 전 경로·MaxFrameLength 양측·FrameTimeout 무력화 없음·SignalGate 무손실 웨이크업·핫패스 무할당

## 2026-09-09 (사이클 16 — 배포 파이프라인 보안 경화)

- **워크플로 시크릿 처리 경화(zizmor 전수 스캔 기반)** — 전 기계 스캔(lens full: jscpd·madge·gitleaks 무결정, zizmor 실결과)에서 나온 배포 파이프라인 소급: ①`persist-credentials: false` 전 5 checkout(nuget-publish 4·ci 1) — 잡이 git 자격증명을 쓰지 않으므로 지속 금지 ②`template-injection` 3곳(pack 단계 `${{ }}` run 확장 → env 경유로 간접화). YAML 유효성·게이트(130/130) 통과. **[보류]** `use-trusted-publishing`(OIDC)은 NuGet.org 측 신뢰 게시 설정 필요 → [[../00-AI/PENDING|PENDING]] — 제품 코드·패키지 무변경(다음 태그부터 적용)

## 2026-09-09 (릴리스 2.3.1 — 패키지 랜딩 페이지 보안 동기화)

- **README 전송 보안 절 추가 + 2.3.1 배포(patch)** — 루트 README는 `Source/Directory.Build.props`가 모든 nupkg에 링크하는 **패키지 랜딩 페이지**인데 TLS·CRC32c 언급이 전혀 없었다(소비자 도달성 공백 — 문서-코드 불일치). TCP TLS·RUDP CRC32c·기본 키 경고 요약 절 추가 후 패키지 내 README 반영(nupkg 압축 확인)·커밋 `f44cfb4` → 태그 ×3 → Actions 4건 전부 성공. 직전 반복의 “출시 불가” 판정 정정: Source/ 불변이어도 패키지 콘텐츠(README) 변경은 소비자 가시 단위다

## 2026-09-09 (사이클 14 — 청urn 소크 회귀)

- **연결 청urn 소크 테스트 2건 추가(128→130)** — 감사 노트의 잔여 리스크 “소크 부재” 중 자동화 가능한 축을 매움: TCP 60라운드·RUDP 40라운드 반복 접속→라운드 태그 왕복→단결, **매 라운드 ActiveConnectionCount 0 회복 단언**(슬롯 카운터=내장 누수 탐지기) + 태그 정합성(세션 간 메시지 혼입 탐지). 촘촘한 MaxConnections(TCP 8·RUDP 4)로 누수 시 조기 실패. 전체 소요 12초→19초. 도중 테스트 코드 오류(`HandlerTarget` 미존재 멤버) 1건 발견·기존 팩터리 캡처 패턴으로 수정

## 2026-09-09 (사이클 13 — 전수 스캔 감사)

- **[[../01-Overview/Audit-Full-Scan|Audit-Full-Scan]] 신규** — 스펙 종료 조건의 전제 산출물: 5개 영역별 처리 상태(완료 항목·근거), 잔여 사유 명시 항목(RUDP 기밀성=범위 밖, TCP_IOCP=사용자 보류, 벤치마크=선택, 소크=운영 데이터 필요), 정직 잔여 리스크 4건. 결론: 처리 가능한 실질 항목 소진 상태 — 신규 단위는 니즈·상위 피드백·실측 데이터 발현 조건. [[../00-AI/CONTEXT|CONTEXT]] 관련 노트에 링크

## 2026-09-09 (사이클 12 — SignalGate 직접 계약 핀)

- **[[../00-AI/CONTEXT|CONTEXT]] 테스트 수 122→128** — `SignalGate`(공개 동시성 기반, 송션·디스패치 루프 웨이크업)이 간접 커버뿐이던 테스트 공백을 직접 핀 6건으로 마감: 신호 웨이크업·퍼밋 래치(선행 신호)·연속 신호 단일 퍼밋 붕괴·조건부 재신호(참/거짓)·Dispose 해제(대기자 완료→이후 ODE). 도중 테스트 어설션 방향 오류(NotEqual) 1건 발견·수정 — 게이트 동작 자체는 정상 확인

## 2026-09-09 (사이클 11 — 상위 제안 노트)

- **[[../01-Overview/Proposals-Upstream|Proposals-Upstream]] 신규** — 스펙 개선 영역 5 의무 절차(상위 활용 제안 기록, 상위 저장소는 미수정): P1 TCP TLS 옵션 통과(TLS 1.3 `Accepted` 주의 포함), P2 RUDP CRC32c 양단 일괄 설정, P3 연결·프레임 타임아웃 정책 통일, P4 `ActiveConnectionCount`·`DisconnectReason.FlowControl` 운영 신호 소비. [[../00-AI/CONTEXT|CONTEXT]] 관련 노트에 링크

## 2026-09-09 (릴리스 2.3.0)

- **패키지 2.3.0 배포(minor)** — 2.2.1 이후 단위: RUDP 기본 연결 키 시작 경고(운영자 가시 진단 — 키 값 미노출) + 송션 경로 검증·격리 통합(동작 보존 구조). 커밋 `cec704b` → 태그 `v2.3.0`·`tcp/v2.3.0`·`rudp/v2.3.0` → Actions 4건 전부 성공. [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 표기 갱신

## 2026-09-09 (사이클 10 — 송션 루프 중복 제거)

- **`MessagePipeline` 송션 양측 경로(바이트·메시지) 중복 통합** — 페이로드 검증(`ValidatePayloadLength`)과 항목 격리(`IsolateFailedSend`)를 단일 헬퍼로 추출, 두 경로가 같은 검증·격리 계약을 공유한다(기존 갈라짐: 메시지 경로 문구·분기가 별도 유지). 예외 타입·격리 순서·슬롯 반환 불변 — 테스트는 타입 수준 핀이므로 문구 통합만 반영(`OversizeSend` 서브스트링 갱신). DS_RPC 회귀 77/77 통과 — 동작 보존 구조 개선(스펙 영역 4)

## 2026-09-09 (사이클 9 — 보안 옵션 안내)

- **[[../04-Guides/Getting-Started|Getting-Started]] 신규 절 6 “전송 보안 옵션”** — 출시됐으나 가이드에 안내가 없던 두 기능의 사용법 추가: TCP TLS(서버 인증서·클라이언트 검증·핸드셰이크 상한·TLS 1.3 `Accepted` 주의) · RUDP CRC32c(양단 설정·프로토콜 처리 전 폐기·검출 전용 한계). 기존 하트비트 절은 7로 이동. 문서 전용 — 코드 무변경

## 2026-09-09 (사이클 8 — 기본 키 시작 경고)

- **RUDP 기본 연결 키 시작 경고** — `RudpListener.Start`가 공개 상수 기본 키로 시작되면 Trace 경고를 남긴다(문서 전용이던 ⚠️ 위험을 운영자 가시 신호로 전환). 키 값 자체는 미노출(시크릿 처리). 테스트 +1(121→122, `TraceCapture` 더블): 기본 키 경고 발생·커스텀 키 무경고·키 값 미노출. [[../04-Guides/Security|Security]]·[[../03-Reference/Configuration|Configuration]] ConnectionKey 행 갱신

## 2026-09-09 (릴리스 2.2.1)

- **패키지 2.2.1 배포(patch)** — 2.2.0 이후 내부 수정 2단위: 프레이머 지연 컴팩트(핫패스 O(N²) 복사 제거) + 네트워크 테스트 순차 실행(결정성). 사용법·기능 무변화. 7개 패키지 통일 bump → 커밋 `b91af87` → 태그 `v2.2.1`·`tcp/v2.2.1`·`rudp/v2.2.1` → Actions 4건 전부 성공. [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 표기 갱신

## 2026-09-09 (사이클 7 — 테스트 결정성)

- **네트워크 루프백 테스트 순차 실행** — TCP·RUDP·TLS 루프백 3클래스를 `[Collection("network-loopback")]`로 묶어 xUnit 기본 클래스 병렬 실행 간섭(CPU 경합 → 타이밍 마감 드문 누락)을 제거했다. 순수 로직 클래스는 병렬 유지, 전체 8초→12초로 안정 통과(121/121). 반복 재보고됐던 검증기 타임아웃 보고의 원인 판정·조치는 [[../00-AI/PENDING|PENDING]] 해결 항목에 기록 — 제품 코드 무변경

## 2026-09-09 (사이클 6 — 프레이머 핫패스)

- **`LengthPrefixFrameReader` 지연 컴팩트** — 기존엔 `ReadFrameAsync`마다 미처리 데이터를 무조건 앞으로 당겨 한 세그먼트에 묶인 N프레임이 O(N²) 복사였다. 파싱을 `_offset` 기준으로 바꾸고 **읽기 창(4KB 하한)이 부족할 때만** 컴팩트→그래도 부족하면 성장. 동작 계약(제로카피 슬라이스·EOF·마감·성장 상한) 불변. 테스트 +1(120→121): 파이프라인 50프레임 컴팩트 ≤1회 회귀 핀(`CompactionCount` 내부 노출). DS_RPC 회귀(빌드+테스트 77건) 통과 — [[../02-Architecture/Pipeline|Pipeline]] 수신 절 갱신

## 2026-09-09 (릴리스 2.2.0)

- **패키지 2.2.0 배포** — RUDP CRC32c 무결성 옵션(minor) 단위. 7개 패키지 버전 통일 bump → 커밋 `d73d67b` → 태그 `v2.2.0`·`tcp/v2.2.0`·`rudp/v2.2.0` push → Actions 4건(verify+publish×3·CI) 전부 성공. [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 버전 표기 갱신

## 2026-09-09 (사이클 5 — RUDP 무결성)

- **RUDP 패킷 무결성 검사(CRC32c) 옵션 추가** — `RudpTransportOptions.Crc32cEnabled`(기본 `false`, 양단 같은 설정 필요·와이어 비호환): 송신마다 체크섬(4바이트) 부여, 수신은 **체크섬 위반 패킷을 프로토콜 처리 전 폐기** — IPv4 UDP 체크섬이 0일 수 있어 손상 패킷이 앱까지 스며드는 것을 전송 가장자리에서 차단(위조 접속 요청은 슬롯 예약 전 폐기). 검출 전용(키 없는 CRC — 능동 공격자 재계산 가능), 기밀성·인증 없음. 테스트 2건 추가(118→120): CRC 켠 양단 왕복, 체크섬 없는 위조 접속 요청의 슬롯 예약 차단
- [[../04-Guides/Security|Security]] RUDP 행 분리(✅ 무결성 옵션 / ❌ 기밀성 미제공), [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]] 옵션 동기화, [[../05-Decisions/0008-tcp-tls-sslstream|ADR 0008]] 결정 6 보강(암호화 거부 유지·무결성 추가)

## 2026-09-09 (릴리스 2.1.0)

- **패키지 2.1.0 배포** — TCP TLS 옵션 기능(minor) 단위. 7개 패키지 버전 통일 bump → 커밋 `3c841c1` → 태그 `v2.1.0`(Shared)·`tcp/v2.1.0`(TCP 3종)·`rudp/v2.1.0`(RUDP 3종) push → Actions 4건(verify+publish×3·CI) 전부 성공. [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 버전 표기 갱신

## 2026-09-09 (사이클 4 — TCP TLS)

- **TCP 전송 TLS(SslStream) 옵션 추가** — [[../05-Decisions/0008-tcp-tls-sslstream|ADR 0008]]: `TcpTlsOptions`(`ServerCertificate`·`TargetHost`·`RemoteCertificateValidation`·`HandshakeTimeout` 15초 기본), `TcpTransportOptions.Tls`, `StreamByteChannel` 확립 스트림 생성자. 서버는 수락 연결마다 연결별 태스크로 핸드셰이크 선행(상한 슬롯 예약→성공 시 채널 Dispose·실패 시 태스크가 회수, 정확히 1회), 실패·상한 초과는 조용히 폐기 후 수락 계속. 클라이언트는 핸드셰이크 실패 시 연결 실패(`false`) 확정. TLS 1.3 노트(클라이언트 검증 실패 후에도 서버 `Accepted` 가능 — 채널 소유 계약 재확인)·테스트 인증서 플랫폼 주의(Windows ephemeral 키 불가→PFX 재수입, Linux serverAuth EKU 필요) 문서화. 테스트 8건 추가(110→118)
- [[../04-Guides/Security|Security]] ❌유일 항목(평문 전송) 해소 — TCP ✅옵션 전환, RUDP 평문 고지(XorEncryptLayer는 난독화일 뿐 미제공 유지). [[../03-Reference/Configuration|Configuration]] `TcpTlsOptions` 항·[[../03-Reference/Public-API|Public-API]] 옵션 블록·[[../02-Architecture/Components|Components]] TCP 행 동기화

## 2026-09-08 (사이클 3 — TCP_IOCP 착수 보류)

- **로드맵 5단계 TCP_IOCP 보류 결정 기록** (2026-09-08 사용자 결정, 코드 불변경) — 기존 TCP 스택도 .NET 비동기 소켓(IOCP 기반) 위에서 동작해 순수 이점이 불명확하므로 실측 니즈·벤치마크 근거 확보 전까지 착수 보류. [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]] 5단계 항목·현재 상태 줄과 [[../00-AI/CONTEXT|CONTEXT]] 요약 서술 갱신

## 2026-09-08 (사이클 2 — 테스트 인프라 품질)

- **감사 원장 잔여 테스트 인프라 3건 해소(제품 코드 무변경, 110건 동일 통과)**
  - `FakeByteChannel` **EOF 래치** — `Complete()`가 EOF를 걸고 이후 `Feed`는 폐기, 드레인된 EOF는 `ReadAsync`가 즉시 0 재전달(실제 스트림 반복 EOF와 일치). 기존 테스트는 모두 Feed-then-Complete 순서라 무영향 확인
  - 「끊김 부재」 증명의 **결정적 강건화** — 4개 테스트의 `Task.Delay(50)+Assert.Null`을 제거: 격리 결정은 fault 관측 전 동기 확정, 후속 송신 완료 자체가 파이프라인 생존 증명(정지 시 fault), 잠자는 수신 루프 외 비동기 끊김원 없음을 근거로 즉시 단언
  - `Connect_ToClosedPort_ReturnsFalse` **경쟁 완화** — placeholder 포트 선점 시(이론상) 새 포트로 최대 5회 재시도해 false 판정을 항상 검증된 빈 포트에 대해서만 내리고, Dispose 직후 connect로 창 최소화

## 2026-09-08

- **감사 원장 잔여 문서-코드 불일치 정리** — [[../02-Architecture/Components|Components]]가 존재하지 않는 `IConnector`/`IListener` 인터페이스를 계약 타입으로 나열한 것을 실제 공개 API(스택별 구체 `TcpConnector`·`TcpListener`·`RudpConnector`·`RudpListener`)로 수정하고 TCP_IOCP 절에 「후속 — 미구현」 표기. ADR [[0001-transport-channel-abstraction|0001]] Decision 3·[[0002-tcp-backend-selection|0002]] Decision 5에 3분할 출하(TCP 2.0.0, RUDP)에 따른 **amended 각주** 추가(근거 [[0007-rudp-three-way-split-and-polling|ADR 0007]]), [[0003-connection-lifecycle-options|ADR 0003]] `DisconnectReason` 목록에 `Timeout`·`FlowControl` 보강
- **`DisconnectedEventArgs.Exception` XML 문서 사실 보정** — TCP 바이트 채널 경로는 `Timeout`(`TimeoutException`)·`FlowControl`(`InvalidOperationException`)도 예외를 실으나 RUDP 전송 끊김 통지는 `null`임을 명시 (동작 변경 없음, `MessagePipeline`·`RudpSession` 코드 확인) → [[../03-Reference/Public-API|Public-API]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F2-3) 동기화
- **테스트 수 통일** — Roadmap 107 → **110**(실측), Feature-Spec 구현 상태 71 → 110, F3 범위 F3-1~F3-10으로 갱신 → [[../02-Architecture/Data-Flow|Data-Flow]] 상태 다이어그램·종료 표에 `Timeout`·`FlowControl` 반영, [[../00-AI/GLOSSARY|GLOSSARY]] `DisconnectReason` 행 보강
- **v2.0.1 패치 릴리스** — v2.0.0 이후 적체된 내부 수정(tcp listener CTS release 6e7d914, accept-loop trace e09613d, rudp poll trace throttle c6641a8, sandbox fix 146b23d, CI release 게이트 강화 58819a4)·이번 문서 정리를 반영. 사용법 변화 없음. `Source/*` csproj `<Version>` 2.0.1 통일, 태그 `v2.0.1`·`tcp/v2.0.1`·`rudp/v2.0.1`로 Actions 검증·배포 → NuGet 7종 2.0.1 확인 → [[../00-AI/CONTEXT|CONTEXT]]·[[../03-Reference/Packages|Packages]] 버전 표기 갱신

## 2026-09-05 (후반 31)

- **배포 게이트에 샌드박스 셀프테스트 추가** — `nuget-publish.yml`의 `verify` 잡(실제 **배포를 차단**하는 게이트)이 개발 CI(12차 신설·13차 확장)보다 느슨했다 — 빌드+테스트만 돌리고 두 샘플의 왕복 스모크는 빠져 있었다. `ci.yml`의 셀프테스트 단계와 동일한 명령(양 스택 exit 0 요구)을 `verify`에도 추가 — 출시 게이트가 개발 게이트 이상으로 엄격해짐

## 2026-09-05 (후반 30)

- **`TcpListener` CTS 수명 주기 정리** — `Stop()`이 `_cts`를 Cancel만 하고 **dispose·해제하지 않아**, 중지 후 리스너가 취소된 CTS를 계속 보유했다(재시작 시 이전 CTS는 새 객체로 교체되지만, 최종 중지 후엔 보유가 남음). Cancel이 토큰 상태를 고정하므로 수락 루프의 모든 탈출 경로가 취소 토큰으로 끝나고 — 취소 후 새 등록 경로가 없어 — dispose+null은 모든 인터리빙에서 안전하다. 17차의 재시작 핀 테스트가 수명 주기를 보호(재시작 경로 10/10·스위트 110/110·빌드 0/0 확인)

## 2026-09-05 (후반 27)

- **`KeepAliveApplicator` 직접 커버리지 신설 (마지막 무테스트 소스 단위)** — 소스 전체에서 직접 테스트가 없던 유일한 단위였다. 계약 3건 고정: ①`null`·`Enabled=false`는 소켓을 건드리지 않음, ②`Enabled=true`+값 지정은 keep-alive 플래그를 세우고 예외 없음, ③값 0(기본)은 '켜기만' — 플랫폼별 세부 값(IOCTL·raw option)은 CI 영향 없이 계약만 검증(크로스 플랫폼 안전). **테스트 107 → 110건 통과**

## 2026-09-05 (후반 26)

- **Public-API.md 마지막 드리프트 2건 정리** — ①`DisconnectReason` 목록에 `FlowControl`(10차 추가) 누락 → `{ Local, Remote, Error, Timeout, FlowControl }` + 예외 보존 표기도 갱신, ②백프레셔 절의 「메시지 채널 수신은 무제한 누적 가능」 한계 문구(3차 이전 기준, MessageQueueOptions·F3-3·Configuration에서는 이미 수정됐지만 Public-API만 남아 있던 마지막 잔재) → 슬롯 대기 포함 상한 + `FlowControl` 실패 폐쇄 계약으로 교체. 이로써 **vault 전체에서 3차 이전의 구(舊) 한계 문구가 소멸**

## 2026-09-05 (후반 25)

- **RUDP 폴링 루프 예외 로그 플러드 방지(스로틀)** — 1ms 재시도 루프가 예외마다 Trace를 남겨, 지속 폴링 실패 시 **초당 ~1000줄** 로그를 쏟는 구조였다(23차의 TCP 수락 루프 진단 추가와 대칭 — 쪽은 50ms 백오프가 있어 20줄/초 상한, 이쪽은 무방비). `_lastPollErrorTick` + `Interlocked`로 **초당 1회** 기록으로 제한(동일 오류 플러드 방지, 격리·계속 동작 불변). netstandard2.1 호환(`DateTime.UtcNow.Ticks` — `Environment.TickCount64`는 미존재). **xUnit2013 경고 1건 정리** — `LateDispose` 테스트의 `Assert.Equal(1, accepted.Count)` → `Assert.Single`

## 2026-09-05 (후반 24)

- **Chat.RUDP 참조 서버 세션 목록 메모리 누수 수정** — `RunServerAsync`의 `sessions` 리스트가 접속마다 `Add`만 하고 끊김 시 제거하지 않아, 장기 구동 서버가 **종료된 세션(+채널·호스트 그래프)을 무제한 보유**했다(샘플을 복사한 앱이 같은 누수를 배우는 결함). `Disconnected` 구독에서 `sessions.Remove(session)` 추가 — 리스트는 살아있는 세션 추적용으로만 유지. 빌드·셀프테스트 exit 0·스위트 107/107 확인

## 2026-09-05 (후반 23)

- **TCP 수락 루프 예외 진단 추가** — 비취소 수락 예외가 50ms 재시도와 함께 **조용히** 삼켜져, 수락이 지속 실패하면 죽은 리스너가 로그 없이 방치되는 상태였다(RUDP 폴링 루프는 예외를 기록하는데 TCP 수락 루프만 침묵). `Trace.TraceError`로 진단 추가 — RUDP 폴링 루프와 동일한 관례, 동작 변경 없음(기존 취소·재시도 경로 유지)

## 2026-09-05 (후반 21)

- **Implementation-Roadmap 테스트 수 동기화** — 「현재 상황」 줄이 **71**에서 멈춰 있었다(16차 이전 기준에서 갱신 누락, 실제 107). 단계 목록(1~4 ✅·5 TCP_IOCP)은 정확했고 행의 수치만 스위트 규모와 모순 — 폐기된 구성 내역(「Shared 66 + RUDP loopback 5」)은 제거하고 현행 수로 갱신. 이로써 vault 전체에서 코드와 모순되는 수치·주장이 남지 않았다

## 2026-09-05 (후반 20)

- **옵션 검증 계약 회귀 테스트 17건 신설** (`OptionsValidationTests.cs`) — 공개 설정 표면(`TcpTransportOptions`·`RudpTransportOptions`·`MessageQueueOptions`)의 검증 가드(≤0·빈 키·상한 초과 거부)가 단 한 건도 직접 검증돼 있지 않았다. 이론 10종으로 전 가드 고정 — 전부 통과(누락된 검증이 있었다면 실패했을 것). 커버리지 스윕도 함께 완료: `Session.md`·`Channel.md`·`Overview.md`·`Code-Structure.md`·`GLOSSARY.md` 전부 드리프트 없음 확인. **테스트 90 → 107건 통과**

## 2026-09-05 (후반 19)

- **메시지 채널 송신 오류 → flush 원예외 + `Error` 단절 계약 회귀 테스트** — ADR 0007의 잔존 계약(채널 오류는 직렬화 실패와 달리 항목 격리 대상이 아님)이 미검증이었다. `FakeMessageChannel.FailSend`로 와이어 실패를 주입해 flush가 **원래 예외**로 끝나고, 파이프라인이 `DisconnectReason.Error`로 단절하며 예외 객체가 보존되는지 검증. 단절 호출을 제거하면 테스트가 실패하는 것 확인(판별 검증). **테스트 89 → 90건 통과**

## 2026-09-05 (후반 18)

- **Feature-Spec에 F4-6(연결 시도 상한) 신설** — 6차(RUDP `ConnectTimeout`)·9차(TCP `ConnectTimeout`) 구현이 스펙 계약 문서에 누락돼 있었다(11차의 F3-10과 같은 계열 갭). 양 전송의 기본값·의미론(초과 시 `false`, 취소와 독립)을 스펙 행으로 고정. 마무리 스윕: ADR 0001/0002/0005·Data-Flow.md 전부 드리프트 없음 확인

## 2026-09-05 (후반 17)

- **리스너 재시작 생명주기 회귀 테스트 2건 추가** — `Stop()` 후 `Start()`(재구성용 재시작)가 새 바인딩으로 수락을 재개하는 경로가 양쪽 모두 미검증이었다. ①`TcpListener`: 중지 → 재시작 → 새 포트 접속·수락, ②`RudpListener`: 중지(`_host` 교체) → 재시작 → 새 호스트·바인딩으로 수락 + `ActiveConnectionCount` 갱신 — 두 경로 모두 통과(재시작은 정상 동작, 계약으로 고정). **테스트 87 → 89건 통과**

## 2026-09-05 (후반 16)

- **수신 역직렬화 실패 → Error 단절 계약 회귀 테스트 2건 추가** — 보안 가이드의 「역직렬화 실패는 예외 → 파이프라인이 `Error` 단절로 격리(손상 세션 유지 금지)」가 스위트에 고정돼 있지 않았다. 테스트 더블에 `SelectiveThrowingDeserializer`(지정 본문에서만 역직렬화 예외) 추가, 두 경로 모두 검증 — ①바이트 경로(`ReceiveLoopByteAsync` catch), ②메시지 채널 경로(`OnMessageChannelReceived` catch): `Error` 단절 + 예외 보존 + 실패 프레임은 핸들러에 미도달. 메시지 경로의 단절 호출을 제거하면 해당 테스트만 실패하는 것을 확인(판별 검증). **테스트 85 → 87건 통과**

## 2026-09-05 (후반 15)

- **ADR 설계 기록 드리프트 동기화 (2건)** — ADR은 설계 결정의 사실 기록인데 ①0007의 Negative에 「송신 실패 격리는 직렬화 실패에만 적용」이 남아 5차(MaxFrameLength 양 경로 사전 검사) 이후의 계약과 모순(초대형 송신은 채널 도달 전 항목 격리로 세션 생존 — 채널 예외 경로=MTU 가드에만 세션 단절 잔존), ②0004의 「`InlineDispatch`로 즉시/큐 선택」이 2차의 메시지 채널 큐 강제를 미반영. 두 ADR 모두 후속 수정 표기로 갱신

## 2026-09-05 (후반 14)

- **README 드리프트 동기화 (NuGet 패키지에 실리는 사용자 문서)** — README는 전 패키지에 팩되는 문서인데, ① 패키지 표에 **RUDP 3종 누락**(Shared·TCP 3종만 표시 — 배포 문단의 `rudp/v*` 태그와 모순), ② 저장소 구조의 샌드박스 행이 「수동 검증」으로 남아 13차의 `--selftest` 미반영. RUDP 3종 행(의존 관계 포함)·설치 스니펫 추가, 양 샘플 행을 `--selftest` 표기로 갱신, 설치 코드 블록에 `console` 언어 태그

## 2026-09-05 (후반 13)

- **Chat.TCP `--selftest` 신설 + CI에 샌드박스 스모크 연결** — RUDP 샘플에만 있던 스크립트형 자가 검증(프로세스 내 루프백 서버+클라이언트 왕복, exit 0/1)을 TCP 샘플에도 추가(왕복 3회, 서버 측 수신 검증 — RUDP와 동일 패턴). 12차에 신설한 `.github/workflows/ci.yml`에 **샌드박스 셀프테스트 단계 추가** — `Chat.TCP`·`Chat.RUDP` 순서로 실행, 한쪽이라도 exit≠0이면 CI 실패. 로컬에서 양쪽 셀프테스트 exit 0 확인(전송 스택의 실제 왕복이 CI에 묶임 — 「샘플 실행 검증」이 수동 절차에서 자동 게이트로). 샘플 주석도 `--selftest` 사용법 갱신

## 2026-09-05 (후반 12)

- **CI 워크플로 신설 — 모든 브랜치 push·PR에서 빌드+테스트** — 기존에 검증이 실행되는 곳은 릴리스 태그 push(`nuget-publish.yml`의 `verify` 잡)뿐이라, 일반 push·PR은 **자동 검증 없이** 지나갔다(회귀가 릴리스 시점에만 드러남). `.github/workflows/ci.yml` 신설: push(전 브랜치) + pull_request 트리거, `verify` 잡과 동일한 단계(동일 SHA 핀 액션, dotnet 10.0.x, Release 빌드 + `--no-build` 테스트). 비밀 정보 없음 — secrets 미접촉. 로컬에서 동일 명령으로 Release 빌드·테스트 85건 통과 확인

## 2026-09-05 (후반 11)

- **Feature-Spec(스펙 계약 문서) 드리프트 동기화** — F3-3(백프레셔)·F3-4(핸들러 디스패치)·F3-5(디스패치 모드) 세 행이 실행 코드와 모순된 상태였다(2차 InlineDispatch 강제·3차 흐름 제어·8차 베이스 폴백이 스펙에 미반영). 각 행을 실제 계약으로 갱신 — ①F3-3: 「한계: 무제한 누적」 → 슬롯 대기 포함 상한 + `FlowControl` 실패 폐쇄, ②F3-4: 정확 타입 미등록 시 가장 구체적 베이스 폴백 후에만 skip, ③F3-5: 메시지 채널 경로는 `InlineDispatch` 무시·항상 큐 강제. **F3-10(프레임 길이 상한) 신설** — `MaxFrameLength`의 양 경로 동일 계약(바이트: 프레이머 거부·송신 격리 / 메시지: 송신 사전 격리·수신 역직렬화 전 거부)을 스펙에 명시(4·5차 구현 누락분)

## 2026-09-05 (후반 10)

- **`DisconnectReason.FlowControl` 신설 — 흐름 제어 단절을 일급 원인으로** — 3차 때 만든 수신 백프레셔 단절(선언된 `MaxPendingMessages` 상한 초과)이 `Error` + 예외 메시지 파싱으로만 구분 가능했다. 이제 열거형 멤버로 노출되어 앱이 메시지 파싱 없이 흐름 제어 단절을 감지할 수 있다. 파이프라인의 나머지 오류 경로(송신 루프·수신 역직렬화)는 `Error` 유지. 회귀 테스트 단언을 `FlowControl`로 갱신(콜백이 `Error`로 돌아가면 실패 — 확인). **닥트 드리프트 복구**: 3차 검증 과정의 `git checkout`이 되돌린 `MessageQueueOptions.MaxPendingMessages` 리마크(「무제한 누적」 한계 문구)를 실제 구현(상한 강제 + 흐름 제어 단절)과 일치하도록 복원 — 배포된 코드와 문서가 모순되던 상태 수정. Components의 `DisconnectReason` 표·Configuration의 `MaxPendingMessages` 행 동기화

## 2026-09-05 (후반 9)

- **TCP `ConnectTimeout` 옵션 신설 — 반개방 호스트 연결 실패 상한** — 호스트가 응답하지 않으면 OS SYN 재시도가 수십 초(Windows ≈21초)까지 끌었다(조정 수단 없음, RUDP의 5초 고정보다 더 악화된 UX). `TcpTransportOptions.ConnectTimeout`(ms, 기본 `null`=OS 기본)을 `TcpConnector.ConnectAsync`에 연결 — `Task.WhenAny` 경쟁으로 상한이 먼저 걸리면 `false`를 확정하고 진행 중 연결의 최종 예외는 관찰만 한다(미관찰 방지). 사용자 취소는 기존대로 `OperationCanceledException`. 회귀 테스트 2건 — ①TEST-NET-1(192.0.2.1) 블랙홀: 상한 1000ms 설정 시 2.5초 이내 false 확정(배선 제거 시 OS 기본 수십 초 — 실패 확인), ②상한을 설정해도 빠른 로컬 연결은 영향 없음. **Public-API 문서 동기화**: `TcpTransportOptions` 스니펫에 `MaxConnections`·`ConnectTimeout` 보강, 누락돼 있던 RUDP 옵션 표(`RudpTransportOptions` 5항목, 직전 단계 `ConnectTimeout` 포함) 신설(8차에서 만든 문서 드리프트 수정). **테스트 83 → 85건 통과** → [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]

## 2026-09-05 (후반 8)

- **`MessageHandler` 상속 기반 디스패치 폴백** — 정확 타입 미등록 메시지를 조용히 skip하던 동작이 조용한 메시지 유실의 근원이었다(컨버터가 다형 직렬화를 쓰면 파생 타입이 도착하는데 베이스만 등록한 앱은 도착을 못 본다). 이제 정확 타입이 없으면 **등록된 베이스 타입(상속·인터페이스) 중 가장 구체적인 것**으로 분배하고, 맞는 핸들러가 전혀 없을 때만 Trace 후 skip한다(기존 경고 계약 유지). 동률 후보(인터페이스·클래스 교차)는 먼저 발견된 쪽 — 등록 수가 작다고 보고 miss마다 선형 스캔. 회귀 테스트 4건 — 파생→베이스 폴백, 가장 구체적 베이스 우선, 정확 타입이 폴백보다 우선, 상속 관계 없는 타입은 여전히 skip(픽스 없이 폴백 2건 실패 — 확인). **테스트 79 → 83건 통과**

## 2026-09-05 (후반 7)

- **RUDP 채널 등록부 소유자 확인 회수 — peer id 재사용 교차 제거 버그 수정** — LiteNetLib은 peer id를 회수 후 풀에서 재사용한다. 기존 `ReleaseChannel`은 id만으로 `TryRemove`해서, **끊긴 세션의 늦은 Dispose**(앱이 정리를 미룬 경우)가 같은 id를 물려받은 새 세션의 등록부 항목을 잘못 걷어낼 수 있었다 — 새 세션의 슬롯이 조기 반환돼 `MaxConnections` 상한 강제가 무너지고(초과 수락), 채널이 고아화돼 이후 끊김 통지가 끊긴다. `TryGetValue` + `ReferenceEquals`로 현재 소유자가 이 채널일 때만 회수하도록 수정. 회귀 테스트 `LateDispose_AfterPeerIdReuse_DoesNotCorruptNewSession` — A 타임아웃 → id 풀 반환 → B가 같은 id 수락 → A의 늦은 Dispose(서버 쪽 채널) → B가 슬롯을 계속 점유하므로 (MaxConnections=1) C는 거부되어야 한다(픽스 없으면 C 수락 — 실패 확인). 테스트 작성 중 리스너 `DisconnectTimeout`이 B까지 죽이는 타이밍 함정(B 키핑얼라이브)과 슬롯 예약 vs Accepted 통지의 폴링 간격 경쟁도 함께 해소. **테스트 78 → 79건 통과**

## 2026-09-05 (후반 6)

- **RUDP `ConnectTimeout` 옵션 신설 — 침묵 호스트 연결 실패 상한** — 호스트가 응답하지 않으면(패킷 유실·블랙홀) 연결 실패는 LiteNetLib 재전송 소진으로만 결정되는데 기본값이 약 5초(500ms × 10회) 고정이라 조정 수단이 없었다. `RudpTransportOptions.ConnectTimeout`(ms, 기본 `null`=LiteNetLib 기본 유지)을 설정하면 재전송 간격 100ms × 시도 횟수 상한으로 환산해 그 이내에 연결 실패를 확정한다(`RudpNetHost` ctor). 회귀 테스트 `ConnectTimeout_SilentHost_FailsFastWithinBound` — **로컬 UDP 블랙홀**(수신 즉시 폐기, 응답 없음) 대상 연결이 상한 400ms 설정 시 2.5초 이내 false로 끝나는지 검증(배선 제거 시 기본 ≈5초로 실패 — 확인). **테스트 77 → 78건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 5)

- **메시지 채널(RUDP) 송신에도 `MaxFrameLength` 사전 검사 — 송신 실패 항목 격리 계약 완성** — 직전 단계(수신 거부)와 대칭: 수신 측이 동일 상한으로 세션을 끊으므로 초과 메시지를 와이어에 내보내면 상대방 세션을 죽이는 결과가 됐다(로컬 과실이 원격 단절로). `SendLoopMessageAsync`가 직렬화 직후 상한 검사를 추가해 초과 항목은 flush `ArgumentException`으로 격리하고 세션은 유지한다 — 바이트 경로의 기존 계약(「송신 격리·수신 거부」)이 양 경로에서 동일하게 성립. 회귀 테스트 `OversizeSend_OnMessageChannel_Isolated_SessionStaysConnected` — 상한 256에 1KB 송신 → flush 예외 + 세션 생존 + 서버 미수신 + 이후 정상 왕복 검증(픽스 없으면 예외 없이 나가 서버 단절 — 실패 확인). **테스트 76 → 77건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 4)

- **메시지 단위 채널(RUDP) 수신에도 `MaxFrameLength` 강제 — 수신 메모리 증폭 방어 완성** — 기존엔 상한이 바이트 경로(TCP 프레이머)에만 적용됐고, RUDP는 LiteNetLib 재조립이 자체 상한을 갖지 않아(MaxFragmentsCount 65535 × MTU ≒ **90MB**) 사실상 무제한 재조립·역직렬화가 가능했다(보안 가이드의 「수신 메모리 증폭 방어」가 RUDP에선 성립하지 않음). `MessagePipeline.OnMessageChannelReceived`가 역직렬화 전 상한 검사를 추가해 초과 payload를 `InvalidDataException` → `Error` 단절로 실패 폐쇄한다. 재조립 자체는 LiteNetLib 내부 풀에서 순간적으로 일어나므로 전달 지점에서의 상한으로 충분하다. 회귀 테스트 `OversizeMessage_OnMessageChannel_DisconnectsFailClosed` — 상한 256 설정에 1KB payload 분할 전송 → 단절 + 핸들러 미도달 검증(픽스 없이는 단절 없음 — 10초 타임아웃). **테스트 75 → 76건 통과** → [[../03-Reference/Configuration|Configuration]]·[[../04-Guides/Security|Security]]

## 2026-09-05 (후반 3)

- **메시지 단위 채널(RUDP) 수신 백프레셔 공백 해소 — 흐름 제어 단절** — `MaxPendingMessages` 상한을 **슬롯 대기(메시지 보유)까지 포함**해 강제한다(`MessagePipeline.EnqueueForDispatchAsync`의 `_pendingReceives` 카운터). 기존엔 핸들러가 밀리면 대기자가 상한을 넘어 **무제한 누적**(문서화된 한계)되어 느린 핸들러 + 빠른 peer 조합이 메모리 압박 경로였다. UDP는 상대방을 늦출 수 없으므로 초과 시 `DisconnectReason.Error`(메시지에 「흐름 제어」 기재)로 실패 폐쇄한다. 바이트 채널(TCP) 경로는 수신 루프의 순차 대기(동시 대기 ≤ 1)라 기존 백프레셔(추가 읽기 중단)가 그대로 유지된다. 이전 단계(InlineDispatch 강제 무시)와 합쳐져 수신 누적이 선언된 상한 안에 완전히 묶인다. 회귀 테스트 `ReceiveOverflow_OnMessageChannel_DisconnectsFailClosed` — 원시 채널 200건 폭주 + 메시지당 100ms 핸들러로 상한 8을 초과시켜 단절을 검증(픽스 없이는 단절 없음 — 10초 타임아웃). **테스트 74 → 75건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 2)

- **메시지 단위 채널(RUDP) 경로에서 `InlineDispatch` 강제 무시** — `MessageQueueOptions.InlineDispatch=true`를 요청해도 `IMessageChannel` 경로는 항상 큐 디스패치를 강제한다(`MessagePipeline.Start`의 디스패치 루프 기동 조건도 함께 변경). 수신 콜백이 **세션 간 공유 폴링 스레드**에서 실행되므로, 핸들러를 그 자리에서 돌리면 느린 핸들러 하나가 다른 세션의 수신·수락·`MaxConnections` 상한 강제까지 막는 구조적 취약점이었다(호스트의 설계 계약 「앱 핸들러는 폴링 스레드에서 실행되지 않는다」를 공개 옵션으로 우회 가능). 바이트 채널(TCP) 경로는 기존 인라인 동작 유지. 회귀 테스트 `InlineDispatchOnRUDP_ForcesQueuedDispatch_OtherSessionsUnstalled` — 세션 A 핸들러 3초 점유 중 세션 B 왕복이 1.5초 안에 완료되는지 검증(픽스 없이는 실패 확인). **테스트 73 → 74건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반)

- **RUDP `MaxConnections` 고갈 공격 회귀 테스트 2건 추가** (`Test/Communication.Tests/RudpLoopbackTests.cs`) — ① `HostileStalledHandshake_ReturnsSlotAfterTimeout`: LiteNetLib 2.1.4 **와이어 형식을 직접 구성한 접속 요청 패킷**(프로토콜 ID 13, ConnectRequest=6, IPv4 SocketAddress + 키 문자열)으로 검증 키 수락 후 **침묵하는 공격자**가 슬롯을 잡아도 상한 초과 거부가 유지되고 `DisconnectTimeout` 후 슬롯이 반환되며 정상 클라이언트가 재수락되는지 4단계로 검증. ② `WrongKeyFlood_LeavesNoSlotResidue`: 틀린 키 접속 폭주(전파 거절)가 `ActiveConnectionCount`에 파편을 남기지 않고 이후 올바른 키 수락이 가능한지 검증. **테스트 71 → 73건 통과** → [[../04-Guides/Security|Security]]

## 2026-09-05

- **로드맵 4단계 RUDP 구현 완료** — `Communication.Network.RUDP.Shared`(RudpSession·RudpMessageChannel·RudpSendOptions/RudpDeliveryMethod·RudpTransportOptions·내부 RudpNetHost)·`.Server`(RudpListener)·`.Client`(RudpConnector) 신설. 네임스페이스는 셋 다 `Communication.Network.RUDP`, Server·Client는 RUDP.Shared의 `InternalsVisibleTo`로 내부 `RudpNetHost` 공유. **LiteNetLib 2.1.4** 고정, `PackageReference`는 RUDP.Shared에만 — LiteNetLib 타입은 `RudpNetHost`·`RudpMessageChannel` 두 파일에만 등장하고 공개 API에는 노출되지 않는다. 버전 1.0.0, **NuGet 배포 트리거는 이번 범위 밖** → [[../02-Architecture/Code-Structure|Code-Structure]]·[[../03-Reference/Packages|Packages]]·[[../00-AI/CONTEXT|CONTEXT]]
- ADR [[0007-rudp-three-way-split-and-polling]] 신규 — RUDP 3분할, **호스트당 전용 폴링 스레드 1개**(접속 수와 무관한 스레드 수 + 세션별 디스패치 큐로 멀티클라이언트 병목 차단), `RudpDeliveryMethod` 5값(LiteNetLib `DeliveryMethod`와 같은 이름·값) + `RudpSendOptions` 불변·공용 인스턴스 5개, 분할 불가 방식 MTU 초과 `ArgumentException`, 수락 전 슬롯 예약 `MaxConnections`, 클라이언트 채널의 호스트 소유, `NetManager.Stop(true)` graceful 정지, `AutoRecycle=false` → 수신마다 `Recycle()`. 대안 기각: `UnsyncedEvents=true`·peer당 스레드·1 패키지·`DeliveryMethod` 직접 노출
- **메시지 채널 경로 끊김 관측 해결** — `IMessageChannel`에는 수신 루프가 없어 원격 끊김을 스스로 감지하지 못한다. `RudpMessageChannel`의 internal `TransportDisconnected`를 `RudpSession`이 구독해 `Session.MarkDisconnected`로 이어 붙여 **`Communication.Shared` 무수정**으로 `Disconnected(Remote)` 계약을 지킴. LiteNetLib → Shared `DisconnectReason` 매핑(Timeout→Timeout, RemoteConnectionClose→Remote, DisconnectPeerCalled→Local, 나머지→Error) → [[../03-Reference/Public-API|Public-API]]·[[../02-Architecture/Data-Flow|Data-Flow]]·[[../02-Architecture/Channel|Channel]]
- **알려진 한계 기록**: 분할 불가 방식의 MTU 초과 `ArgumentException`은 `MessagePipeline`이 채널 오류로 취급해 해당 항목 flush 예외 완료 + **세션 `Disconnected(Error)`**로 끊는다(예외는 `DisconnectedEventArgs.Exception` 보존). 「송신 실패 항목 격리」는 직렬화 실패에만 적용 — 항목 격리로 내리려면 Shared 수정이 필요해 범위 밖으로 둠 → [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]]·[[../03-Reference/Public-API|Public-API]]
- **RUDP 테스트 5건 추가** (`Test/Communication.Tests/RudpLoopbackTests.cs`) — 양방향 왕복·끊김 원인(Local/Remote), 리스너 1대에 동시 클라이언트 4개 + 접속 수 회수, 전송 방식 5종 메시지별 왕복, 분할 불가 방식 3종 MTU 초과 `ArgumentException` + `ReliableOrdered` 8KB 분할·재조립, `MaxConnections` 초과 거부·슬롯 회수 후 재수락. **테스트 66 → 71건 통과**
- **`Sandbox/Chat.RUDP` 신설** — Chat.TCP와 동일한 server/client 채팅 샘플에 `--selftest` 모드 추가(프로세스 내 서버+클라이언트로 전송 방식 5종 1회씩 왕복 후 exit 0). 대화형 모드에서 `'!'` 접두 줄은 Unreliable로 전송해 메시지별 옵션을 수동 검증할 수 있다 → [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]·[[../04-Guides/Getting-Started|Getting-Started]]
- **`RudpTransportOptions` 신규** — `MaxConnections`(기본 무제한)·`DisconnectTimeout`(기본 5000ms, UDP half-open 감지의 유일한 신호)·`ConnectionKey`(기본 `"DS_Communication.RUDP"`)·`IPv6`(기본 false) 네 항목만 노출. poll 간격·`UnsyncedEvents`는 의도적으로 비노출 → [[../03-Reference/Configuration|Configuration]]
- **「스택당 1 패키지·3분할 금지」 규칙 폐기** — TCP(2.0.0)에 이어 RUDP도 3분할. 남은 스택(TCP_IOCP·IPC)만 1 패키지 → [[../00-AI/CONVENTIONS|CONVENTIONS]]·[[../03-Reference/Packages|Packages]]·[[../02-Architecture/Overview|Overview]]
- 기존 노트 동기화 — [[../01-Overview/Feature-Spec|Feature-Spec]](F1-4·F1-5·F4-2~F4-5 구현, F4-5 MTU 가드 신규, F5-2 패키지 구성, 구현 상태 2026-09-05), [[../02-Architecture/Components|Components]](`Network.RUDP` 실제 타입 표 + 내부 `RudpNetHost`), [[../01-Overview/Home|Home]](테스트 53 → 71, RUDP 완료), `DisconnectReason` 표에 누락됐던 `Timeout` 보강(Components·Data-Flow), vault 내 모호한 `[[WikiLink]]`(Legacy 동명 파일과 충돌) 12곳을 상대 경로 링크로 수정
- **RUDP 3종 NuGet 2.0.0 배포** — `nuget-publish.yml`에 `rudp/v*` 태그 잡(`publish-rudp`) 추가: `rudp/v2.0.0` → RUDP 3종 팩·푸시. `publish-shared`의 실행 조건을 `${{ !contains(github.ref_name, '/') }}`로 단순화해 `tcp/`·`rudp/` 태그에서 Shared 재배포 시도를 원천 차단(기존엔 `--skip-duplicate`로 회피). 액션 버전 주석(`# v5.4.0` 등)을 80자 린트 때문에 `uses:` 위 줄로 이동, RUDP 3종 csproj `Version` 1.0.0 → **2.0.0**(Shared·TCP 2.0.0 통합 릴리스 정렬) → [[../03-Reference/Packages|Packages]]·`README.md`

## 2026-09-04

- **TCP 3분할 + NuGet 2.0.0 배포** — `Communication.Network.TCP` 단일 프로젝트를 `Communication.Network.TCP.Shared`(TcpSession·StreamByteChannel·TcpTransportOptions)·`.Server`(TcpListener)·`.Client`(TcpConnector)로 분할, 네임스페이스는 `Communication.Network.TCP` 유지. Server·Client는 TCP.Shared의 `InternalsVisibleTo`로 내부 API 공유. `Communication.sln`·`Test`·`Sandbox/Chat.TCP` 참조 이전, 기존 프로젝트 삭제. 「스택당 1 패키지」 규칙은 TCP에 한해 폐기 → [[../03-Reference/Packages|Packages]]·[[../02-Architecture/Code-Structure|Code-Structure]]·[[../00-AI/CONTEXT|CONTEXT]]
- **GitHub Actions TCP 배포 트리거** — `nuget-publish.yml`에 `tcp/v*` 태그 잡 추가: `tcp/v2.0.0` → TCP 3종 팩·푸시, 기존 `v*` → Communication.Shared 경로 유지
- **CONTEXT 테스트 수 동기화** — 「테스트 53 통과」를 실제 스위트 규모인 「테스트 66 통과」로 갱신 (2026-09-01 프레임 상한 회귀 6건 추가 이후 미반영분)

## 2026-09-01

- **[[../04-Guides/Security|Security & Production Checklist]] 노트 신규** — 컨버터 안전 제약(금지 직렬화기·다형 타입 `$type` 위험·고정 타입 역직렬화 권장 패턴)과 프로덕션 투입 체크리스트(암호화·인증 미제공, 타임아웃·상한·연결 수 하드닝 현황, 앱 책임 목록). [[../01-Overview/Home|Home]]·[[../00-AI/CONTEXT|CONTEXT]] 읽기 맵에 연결
- **프레임 길이 상한 옵션화** — 고정 상수 64MB를 `MessageQueueOptions.MaxFrameLength`로 이동하고 기본값 **4MB**로 하향(메모리 증폭 표면 축소). 64MB는 `LengthPrefixFramer.MaxFrameLength` 절대 상한으로 잔류(초과 설정은 거부); 수신 초과 프레임은 `Error` 단절, 송신 초과 항목은 격리. 기본 상한 적용·커스텀 상한 거부(수신 단절·송신 격리)·경계값 회귀 테스트 6건 추가(테스트 66건 통과) → [[../03-Reference/Configuration|Configuration]]·[[../02-Architecture/Pipeline|Pipeline]]
- **`MaxConnections` 수락 상한 추가** — 연결 고갈 공격 방어. `TcpTransportOptions.MaxConnections`(기본 `null` 무제한) 상한 도달 시 수락된 연결을 즉시 닫고 수락 계속(거부 연결은 `Accepted` 통지 없음); 채널 Dispose 시 슬롯 회수(`StreamByteChannel` 내부 Dispose 훅), `TcpListener.ActiveConnectionCount`로 현황 노출. 상한 초과 거부·슬롯 회수 후 재수락 회귀 테스트 추가(테스트 60건 통과) → [[../02-Architecture/Components|Components]]·[[../03-Reference/Configuration|Configuration]]
- **읽기 유휴 타임아웃(`FrameTimeout`) 추가** — 슬로로리스(부분 프레임 끌어안기) 방어. 프레임 첫 바이트 도착 시 마감 시작(기본 30초, `null`/`0` 비활성화), 미완성 시 `TimeoutException` → `DisconnectReason.Timeout`(신규 열거 멤버) 단절; 완전 유휴 연결은 대상 아님(하트비트는 앱 책임). 드립 공급 단절·유휴 무영향·비활성 회귀 테스트 4건 추가(테스트 59건 통과) → [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]·[[../02-Architecture/Pipeline|Pipeline]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-9)
- `LengthPrefixFrameReader` **메모리 증폭 공격 차단** — 버퍼 성장을 선언된 프레임 길이 기준 사전 할당에서 **실제 누적 데이터 기준**(버퍼 가득 찰 때만 2배 성장)으로 변경. 헤더 4바이트만으로 67MB 선할당 불가; 선언 64MB·부분 도착 시 버퍼 상한 + 성장 경로 정상 재조립 회귀 테스트 2건 추가(테스트 55건 통과) → [[../02-Architecture/Pipeline|Pipeline]](수신)
- `MessagePipeline` Dispose 경쟁 수정 — 송신 성공 경로의 순서를 뒤집어 **Flush 완료가 슬롯 해제보다 먼저**(바이트 코얼리스 배치·메시지 경로 각 1곳). Dispose가 쓰기 완료와 슬롯 해제 사이에 끼어 슬롯이 정리돼도 `SendAndFlushAsync` 호출자가 hang하지 않음; 채널별 훅으로 경쟁 창을 결정적으로 재현하는 회귀 테스트 2건 추가, 테스트 수 표기 53건 통과로 동기화 → [[../02-Architecture/Pipeline|Pipeline]](송신)
- 회귀 커버리지 보강 — `MessageHandler`(`ConcurrentDictionary` 교체 후 테스트 부재)에 미등록 타입 skip·지연 등록·동시 등록+디스패치 무유실 3건, `MessagePipeline` 코얼리스 배치 중간 직렬화 실패 부분 되감기(`RewindTo(frameStart > 0)`) 핀 1건 추가; `MessageQueueOptions` XML 문서 오타(`수진은`→`수신은`) 수정, 테스트 수 표기 51건 통과로 동기화 → [[../00-AI/CONTEXT|CONTEXT]]·[[../01-Overview/Home|Home]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F6)
- `SendAndFlushAsync` 사전 취소 토큰 처리 — 토큰이 이미 취소됐으면 큐잉하지 않고 즉시 취소 완료 Task 반환(메시지 미송신), 회귀 테스트 포함 47건 통과 → [[../03-Reference/Public-API|Public-API]] 런타임 의미 갱신
- `TcpListener.Accepted` 최신 구독 반영 — 수락 루프가 수락마다 최신 구독자를 읽어 `Start` 이후 구독자도 채널을 받음(옛 스냅샷 방식은 구독 전 수락 채널을 폐기), 회귀 테스트 포함 46건 통과 → [[../03-Reference/Public-API|Public-API]] 노트 추가
- `TcpTransportOptions.NoDelay` 추가(기본 `true`) — `TcpConnector`·`TcpListener`가 연결·수락 소켓에 적용; 라이브러리 coalesce와 중복되는 Nagle 지연 제거, 회귀 테스트 포함 45건 통과 → [[../03-Reference/Configuration|Configuration]](NoDelay 섹션)·[[../03-Reference/Public-API|Public-API]] 동기화
- 메시지 채널 수신 백프레셔 한계 문서화 — `IMessageChannel` 경로는 콜백 차단 방지를 위해 슬롯 대기를 비동기로 넘기므로 핸들러가 밀리면 대기자가 상한 넘어 무제한 누적 가능(바이트 채널은 상한 유지); 동작 변경 없음 → `MessageQueueOptions` XML 문서·[[../03-Reference/Public-API|Public-API]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-3) 기록
- 세션 미부착 파이프라인 송신의 동기 throw 제거 — 끊김과 동일하게 **예외 완료 Task** 반환 (메시지는 구분), `SessionTests` 회귀 테스트 추가 → [[../03-Reference/Public-API|Public-API]] 런타임 의미 갱신
- `MessageHandler` 등록 테이블을 `ConcurrentDictionary`로 교체 — 세션 시작 후 지연 등록 시 디스패치 스레드와의 읽기/쓰기 경쟁 제거 → [[../02-Architecture/Handler|Handler]] 동기화
- 송신 직렬화 실패 **항목 격리**: 실패한 메시지의 `flush`만 예외 완료하고 송신 루프는 계속 — 세션 끊김(`Disconnected(Error)`)으로 격상하지 않음(수신 핸들러 예외 격리와 대칭). 바이트 배치 경로는 부분 프레임을 되감아 폐기, 격리된 항목의 백프레셔 슬롯은 반환 → [[../02-Architecture/Pipeline|Pipeline]]·[[../03-Reference/Public-API|Public-API]] 동기화, 회귀 테스트 포함 43건 통과

## 2026-08-31

- [[Feature-Spec]] 신규 — 레거시에서 이어받을 기능 명세: F1 연결·수락, F2 세션 수명·송신, F3 메시지 파이프라인, F4 전송별 기능, F5 플랫폼·패키지, F6 검증, 이어받지 않음(레거시와 차이)
- [[../00-AI/CONTEXT|CONTEXT]] 관련 노트 · [[../01-Overview/Home|Home]] 읽기 맵에 연결
- 런타임 계약 확정: 끊김·Dispose 후 송신은 **예외로 완료된 Task**, 백프레셔 상한 시 **비동기 대기**, `InlineDispatch` 기본 `false`, 핸들러 `Action` 예외는 **Trace 후 수신 루프 계속** → [[../01-Overview/Feature-Spec|Feature-Spec]]·[[../03-Reference/Public-API|Public-API]]·[[../03-Reference/Configuration|Configuration]] 동기화; [[../05-Decisions/0004-send-options-and-handler-api|ADR 0004]] `SendOptions` 마커 클래스 확정
- 로드맵 1~3단계 구현 완료: `Communication.Shared` 전체 + `Communication.Network.TCP` + `Test/Communication.Tests` (xUnit 23건) + `Sandbox/Chat.TCP` 실행 검증; [[../02-Architecture/Code-Structure|Code-Structure]]·[[../03-Reference/Packages|Packages]]·[[../04-Guides/Getting-Started|Getting-Started]]·[[../01-Overview/Home|Home]]·[[../00-AI/CONTEXT|CONTEXT]] 실제 구현과 동기화, [[../01-Overview/Feature-Spec|Feature-Spec]] 구현 상태 표 추가, keep-alive 플랫폼 적용 방식(Windows IOControl / Unix 원시 옵션) 문서화
- 리뷰 수정 동기화: 수신 경로를 `LengthPrefixFrameReader` **단일 누적 버퍼 + 제로카피 슬라이스**로 재작성, 송신 프레임 검증(빈 페이로드 거부·상한)·끊김 시 파이프라인 정지·`Disconnected` 구독자 격리 문서화 → [[../02-Architecture/Pipeline|Pipeline]]·[[../02-Architecture/Session|Session]]·[[../03-Reference/Configuration|Configuration]](`CoalesceLimitBytes` 추가)·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-8 재서술, 테스트 41건) 갱신

## 2026-07-11 (후반)

- ADR [[0003-connection-lifecycle-options]]: **재접속·하트비트 앱 책임**, `DisconnectReason`, TCP **`SocketKeepAliveOptions`**
- 라이브러리에서 `ReconnectOptions`·재접속 이벤트·Channel 재바인딩 제거
- [[../04-Guides/Getting-Started|Getting-Started]] § 앱 재접속·keep-alive 예시; [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]·Components·Overview·Roadmap·Packages 동기화

## 2026-07-11

- ADR [[0006-session-ownership-and-converter]]: 앱이 Session 생성, 끊김은 Session만, Converter `IBufferWriter`/`Span`
- [[../03-Reference/Public-API|Public-API]]·[[../04-Guides/Getting-Started|Getting-Started]]·Handler/Session/Pipeline 동기화
- [[../04-Guides/Getting-Started|Getting-Started]] 사용 예시; ADR 0003–0005; [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]; 핵심 개념·Packages
