---
project: DS_Communication
type: overview
status: stable
tags: [review, risk, production, security, performance]
updated: 2026-09-13
---

# Commercial Risk Scan — 상용 유니티 서버 엔진 투입 시 문제점 스캔 (2026-09-13)

> 질문: "이 프로젝트를 실제 상용 유니티 서버 네트워크 엔진 라이브러리로 사용했을 때 보안·속도 등에서 생길 수 있는 문제는?"
> 방법: `Source/` 전 파일(8패키지, ~4.3k줄) 재열독 — [[Production-Readiness-Review|2026-09-09 투입 검토]] 이후 코드 기준으로 위협을 다시 분류. 문서 주장이 아닌 코드에서 직접 확인한 것만 기재.

## 결론 요약

코어(프레이밍·세션 수명·송신 격리·수용 강건성)는 견고하나, **기본값 그대로 공개망에 내놓으면 안 되는 항목**과 **확장 시 병목이 예측 가능한 항목**이 있다. 아래 표 기준으로 상용 배포 체크리스트로 쓴다.

| # | 분류 | 심각도 | 한 줄 | 코드 근거 |
| --- | --- | --- | --- | --- |
| R1 | 보안 | 높음 | TCP 침묵 연결이 슬롯을 **영구 점유** (기본값 배포 시) | `FrameTimeout`은 부분 프레임 도착 후에만 개시(`LengthPrefixFrameReader.ReadFrameAsync`의 `_end-_offset>0` 조건), `KeepAlive` 기본 null, idle 타임아웃 부재 |
| R2 | 보안 | 높음 → **2.7.0 완화** | RUDP TLS `TargetHost` 검증은 **이름만 비교** — 체인·유효기간 미검사 → 자체서명 인증서로 MITM 가능. **→ 2.7.0: 옵트인제+만료검사로 폐쇄(핀닝 권장은 유지)** | `RudpDtlsHandshake.MatchesHost` (SAN/CN 일치 검사만 존재) |
| R3 | 보안 | 높음 | RUDP 기본 평문 + **공개 기본 연결 키** — 키는 연결 요청에 평문 송신 | `RudpTransportOptions.DefaultConnectionKey = "DS_Communication.RUDP"`(공개 상수), LiteNetLib 와이어 프로토콜 |
| R4 | 보안 | 높음 | **인증 전 자원 소모** — 클라이언트 인증 전 ECDHE 핸드셰이크 CPU·역직렬화 CPU, per-IP/레이트 제한 없음, `MaxConnections` 기본 null | TCP `clientCertificateRequired:false`, DTLS `GetClientCredentials()=>null!`, `OnConnectionRequest` 무제한 수용(옵션 미설정 시) |
| R5 | 속도 | 높음 | RUDP 송신 **바이트 기반 백프레셔 부재** — 느린 피어에 GB급 누적 가능 | `SendLoopMessageAsync`가 `_peer.Send`(LiteNetLib 내부 큐 즉시 삽입) 후 슬롯 반환; LiteNetLib 내부 송신 큐는 바이트 상한 없음 |
| R6 | 속도 | 높음 | 평문 RUDP **역직렬화가 공유 폴링 스레드에서 실행** — 단일 코어 수신 한계, 전 세션 지연 전파 | `MessagePipeline.OnMessageChannelReceived`(폴링 스레드에서 `_converter.Deserialize`) |
| R7 | 보안 | 중간 | RUDP 수신 **메모리 증폭이 파이프라인 검사 아래 계층**에서 이미 발생 | LiteNetLib 재조립 상한(≈90MB, `MaxFragmentsCount` 미튜닝·미노출) > `MaxFrameLength` 4MB; DTLS 봉투 재조립도 64MB(`RudpTlsChannel.MaxMessageLength`) 1차 조립 후 4MB 검사 |
| R8 | 속도 | 중간 | DTLS 송신 동기 암호화(BC 순수 C#) — 대형 메시지 = 세션 송신 루프 장기 점유, Mono에서 심화 | `RudpTlsChannel.SendAsync`의 `lock(_gate)` 안 16KB 레코드 청킹 + `_dtls.Send` 동기 |
| R9 | 속도 | 중간 | 1ms 고정 폴링 루프 — 접속 중 상시 1000 wakeup/s/호스트, Windows 타이머 해상도 의존 | `RudpNetHost.PollLoop`(`ActivePollIntervalMs=1`, ponytail 주석) |
| R10 | 속도 | 중간 | 세션당 수신 버퍼 64KB 선할당 — 대규모 CCU 메모리 배수 | `LengthPrefixFrameReader.DefaultBufferSize = 64*1024` |
| R11 | 운영 | 중간 | 관측성 Trace뿐 — 메트릭·카운터 없음, Unity 서버 기본 무출력 | 전 코드베이스 `Trace.Trace*` 만 존재 |
| R12 | 보안 | 낮음 | TCP 클라이언트 `checkCertificateRevocation:false` — 폐기 검사 생략(핀닝 쓰면 무관) | `TcpConnector.ConnectAsync`의 `AuthenticateAsClientAsync(..., false)` |
| R13 | 속도 | 낮음 | DTLS 핸드셰이크 연결마다 개인키 내보내기·`SecureRandom` 재생성(캐싱 없음), 수신 레코드당 `ToArray()` 할당 | `RudpDtlsServer` ctor, `RudpDtlsTransport.OnRecordReceived` |

## 높음 상세

### R1 — TCP 침묵 연결 영구 점유

`FrameTimeout`(기본 30초)은 "프레임의 첫 바이트가 도착한 순간"부터만 측정한다 — 바이트가 전혀 없는 유휴 연결은 마감 대상이 아니다(하트비트는 앱 책임으로 설계·문서화됨). OS keep-alive도 `TcpTransportOptions.KeepAlive` 기본 `null`로 미적용. 따라서 **아무것도 안 보내는 연결은 라이브러리 수준에서 절대 끊기지 않는다.** `MaxConnections`(기본 `null`=무제한)를 설정해도 침묵 연결 쌓기만으로 상한을 채워 정상 플레이어의 접속을 막을 수 있다(슬롯 점유 DoS).

- 완화: 배포 시 `KeepAlive.Enabled=true` + 앱 하트비트 응답 타임아웃 필수. 또는 라이브러리 차기 후보로 "유휴 연결 절대 타임아웃" 옵션.
- 근거: `LengthPrefixFrameReader.ReadFrameAsync`(`_frameTimeout.HasValue && _end - _offset > 0`), `TcpTransportOptions.KeepAlive` 문서.

### R2 - RUDP TLS TargetHost 검증 격차 — **2.7.0 완료**

`RudpDtlsAuthentication.NotifyServerCertificate`은 (1) 핀닝 콜백 → (2) `TargetHost` SAN/CN 일치 → (3) 기본 거부 순서다. 문제는 (2): **이름 일치만 검사하고 신뢰 체인·유효기간을 전혀 보지 않는다.** 공격자는 CN만 같은 이름으로 맞춘 자체서명 인증서를 제시하면 통과한다(DNS 스푸핑/MITM 조건에서). TCP 클라이언트는 OS 기본 검증(체인+만료+이름)을 거치므로 RUDP `TargetHost` 단독 사용이 상대적으로 약하다.

- 완화: 프로덕션 클라이언트는 `RemoteCertificateValidation`(SHA-256 핀닝) 필수 — 가이드는 이미 그 방향이나 코드가 이름 일치를 통과시키는 것이 위험. 차기 후보로 `TargetHost` 경로에 만료 검사 추가·경고.
- **→ 2.7.0 완료**: `AllowNameOnlyCertificateMatch` 옵트인(기본 거부, fail-closed) + 유효기간(NotBefore/NotAfter) 검사 추가 — 이름일치 단독 MITM 경로 폐쇄. 회귀 테스트 3종(옵트인 미설정 거부·옵트인+만료 거부·옵트인+유효 성공).
- 근거: `RudpDtlsHandshake.MatchesHost`.

### R3 — RUDP 평문 기본·공개 키

DTLS 미설정(기본)은 평문 UDP + 키 없는 CRC32c(`Crc32cEnabled`는 손상 검출용). 연결 키는 LiteNetLib 연결 요청 패킷에 **평문**으로 실려 가며 기본값은 공개 상수 — 스니핑 가능한 위치의 공격자는 키를 얻어 주입·세션 가로채기가 가능하다. 시작 시 기본 키 경고(`RudpListener.Start`)는 있으나 강제는 아니다.

- 완화: 공개망 RUDP는 DTLS 필수 + `ConnectionKey` 교체. 신뢰망/VPN 전제만 평문 허용.

### R4 — 인증 전 자원 소모 (CPU DoS 표면)

전송 계층에 클라이언트 인증이 없다(설계상 앱 책임). 그러나 인증 완료 **이전에** 서버가 이미 치르는 비용이 있다:

1. TLS/DTLS 핸드셰이크 ECDHE 연산 — 연결당. per-IP 접속 제한·핸드셰이크 레이트 제한 없음. `MaxConnections` 기본 `null`이면 동시 핸드셰이크도 무제한(TCP는 연결별 태스크, RUDP는 연결별 `Task.Run`).
2. 메시지 역직렬화 — 인증 여부와 무관하게 파이프라인이 역직렬화 후 디스패치. 메시지 레벨 속도 제한은 앱 책임으로 문서화됨. 4MB 프레임을 라인레이트로 밀면 파서 CPU가 먼저 고갈된다.

- 완화: 배포 시 `MaxConnections` 명시 + 초당 접속 수 상한(앱/방화벽) + 최대한 빠른 앱 인증 게이트(인증 실패 즉시 단절) + 인증 전 허용 메시지 수 제한.

## 속도 상세

### R5 — RUDP 송신 바이트 백프레셔 부재

`MessagePipeline`의 `MaxPendingMessages`(기본 10,000)는 **개수** 상한이다. 그런데 RUDP 경로는 `SendLoopMessageAsync`에서 `_messageChannel.SendAsync` → `NetPeer.Send`(LiteNetLib 내부 큐 동기 삽입)가 즉시 완료되므로 슬롯은 곧바로 반환된다. 실제 무제한 버퍼는 **LiteNetLib 내부 송신 큐(바이트 상한 없음)**다 — 느린 클라이언트에 대량 송신하면 개수 상한과 무관하게 GB급 누적이 가능하다(프레임 상한 4MB × 다수). TCP는 `WriteAsync`가 OS 송신 버퍼 포화 시 블록되어 자연 백프레셔가 성립한다 — **이 격차는 RUDP에만 있다.**

- 완화: 앱에서 피어별 송신 예산(미확인 바이트 상한) 구현, 또는 차기 후보로 파이프라인에 직렬화 바이트 기반 상한. `SendAndFlushAsync` 완료 = "와이어 기록"이 아니라 "큐 삽입"임도 인지할 것.

### R6 — 평문 RUDP 역직렬화가 폴링 스레드에 집중

폴링 스레드(호스트당 1개)는 모든 세션의 수신 이벤트를 드레인한다. 평문 RUDP 경로는 `OnMessageChannelReceived`에서 `_converter.Deserialize`가 **그 스레드 위에서** 실행된다(핸들러만 세션별 큐로 분리됨). 역직렬화가 비싼 컨버터(JSON 등)라면 한 세션의 무거운 페이로드가 **모든 세션의 수신·수용·타임아웃 판정**을 지연시킨다. 흥미로운 점: DTLS 경로는 복호화·봉투 해체가 세션별 펌프 태스크에서 일어나 역직렬화도 세션별로 병렬화된다 — **평문 경로가 오히려 확장성이 나쁘다.**

- 완화: RUDP 고CCU 서비스는 (a) 가벼운 컨버터, (b) DTLS 경로(세션별 펌프), 또는 (c) 차기 후보로 원시 payload 복사 후 세션별 역직렬화.

### R9 — 1ms 폴링

접속 1개 이상 시 항상 1ms 슬립 루프(호스트당 1000 wakeup/s). 게임 지연 최소화 목적의 공개된 트레이드오프(ponytail 주석)지만: (a) Windows에서는 타이머 해상도 미조정 시 실제 간격이 커질 수 있음, (b) 이벤트 드레인이 직렬화되어 있어 대규모 CCU에서 폴링 스레드가 처짐(하나의 코어가 RUDP 전체 수신 상한). 2.6.0의 폴링 백오프(idle 15ms)는 접속 0개에만 적용된다.

## 기타 확인된 중간·낮음

- **R7**: RUDP 수신 거부(4MB)는 파이프라인 계층에서 일어나지만, LiteNetLib 재조립 계층은 그 위에서 이미 ≈90MB까지 버퍼링 가능(`MaxFragmentsCount` 미노출·미튜닝). DTLS 봉투 재조립도 64MB 상한이 4MB 검사보다 앞선다. 완화: `MaxFragmentsCount` 옵션 노출.
- **R8**: DTLS 송신은 채널 락 안에서 16KB 레코드로 청킹되며 각 레코드를 BC(순수 C#) AES-GCM으로 동기 암호화한다. 4MB 메시지 = 레코드 256개 — Mono에서 세션 송신 루프 장기 점유. 대형 메시지 자체를 제한하는 앱 규칙 권장.
- **R10**: `LengthPrefixFrameReader`가 연결당 64KB 버퍼를 풀에서 빌린다 — 1만 접속 시 수신 버퍼만 ~640MB. 고CCU TCP에서 초기 버퍼 축소 옵션이 필요할 수 있다.
- **R11**: 메트릭(접속 수·큐 깊이·끊김 원인·재시도율) 없음. `ActiveConnectionCount`만 노출. 상용 운영·용량 계획에 필수인 수치를 앱이 전부 자작해야 한다.
- **R13**: DTLS 서버가 연결마다 `ExportPkcs8PrivateKey`·`new SecureRandom()`을 재수행(핸드셰이크당 비용), 수신 레코드마다 `record.ToArray()`(pps 비례 GC). Unity Boehm GC 서버에서 고부하 시 관찰 필요.
- **부하·런타임 실측 부재**(기존 검토 지적 유지): 152 테스트는 정합성 중심. CCU·처리량 스펙, 24시간 소크, Unity(Mono) 전용 서버 빌드 실측 없음. `TcpListener.Start()` 백로그 인자 미지정 — Mono 구현의 기본 백로그 차이 확인 필요.

## 앱이 반드시 이행할 것 (이전 검토와 동일, 유효)

인증·세션 관리(가급적 빠른 게이트), 하트비트·재접속(+응답 타임아웃), 메시지 속도 제한, 안전한 `IMessageConverter`(RCE 유발 직렬화기 금지), 공개망 `ConnectionKey` 교체, RUDP `Accepted`에서 세션 동기 생성(지연 시 유실).

## 배포 권고 (이 문서 기준 갱신)

1. TCP + TLS + `MaxConnections` 명시 + `KeepAlive.Enabled=true`(또는 앱 하트비트) — **R1·R4 봉쇄**.
2. RUDP는 DTLS + 핀닝 클라이언트(`TargetHost` 단독 금지) + 키 교체 — **R2·R3 봉쇄**.
3. 피어별 송신 바이트 예산·수신 레이트 제한은 앱 계층에서 — **R4·R5 봉쇄**.
4. 스테이징 24시간 소크 + Unity 서버 빌드 실측 후 투입.

## 관련

- [[Production-Readiness-Review]] · [[Audit-Full-Scan]] · [[../04-Guides/Security|Security]] · [[../05-Decisions/0008-tcp-tls-sslstream|ADR 0008]] · [[../05-Decisions/0009-rudp-tls-dtls|ADR 0009]]
