---
project: DS_Communication
type: overview
status: draft
tags: [meta, changelog]
updated: 2026-09-01
---

# Changelog

Document vault 변경 기록 (코드 릴리스 노트 아님).

## 2026-09-01

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
