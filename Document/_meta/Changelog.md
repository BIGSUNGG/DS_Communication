---
project: DS_Communication
type: overview
status: draft
tags: [meta, changelog]
updated: 2026-09-04
---

# Changelog

Document vault 변경 기록 (코드 릴리스 노트 아님).

## 2026-09-04

- **TCP 3분할 + NuGet 2.0.0 배포** — `Communication.Network.TCP` 단일 프로젝트를 `Communication.Network.TCP.Shared`(TcpSession·StreamByteChannel·TcpTransportOptions)·`.Server`(TcpListener)·`.Client`(TcpConnector)로 분할, 네임스페이스는 `Communication.Network.TCP` 유지. Server·Client는 TCP.Shared의 `InternalsVisibleTo`로 내부 API 공유. `Communication.sln`·`Test`·`Sandbox/Chat.TCP` 참조 이전, 기존 프로젝트 삭제. 「스택당 1 패키지」 규칙은 TCP에 한해 폐기 → [[../03-Reference/Packages|Packages]]·[[../02-Architecture/Code-Structure|Code-Structure]]·[[../00-AI/CONTEXT|CONTEXT]]
- **GitHub Actions TCP 배포 트리거** — `nuget-publish.yml`에 `tcp/v*` 태그 잡 추가: `tcp/v2.0.0` → TCP 3종 팩·푸시, 기존 `v*` → Communication.Shared 경로 유지

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
