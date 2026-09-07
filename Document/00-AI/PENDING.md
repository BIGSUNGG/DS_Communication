---
project: DS_Communication
type: meta
status: draft
tags: [ai, pending]
updated: 2026-09-09
---

# PENDING — 보류 질문·블로커 기록

> 자율성 규칙 3·4: 사용자 판단이 필요하거나 블로커는 여기에 기록하고 계속 진행한다.

## [기록] 검증 파이프라인의 오래된 실패 보고 재생 (2026-09-09)

- **무엇**: 개발 세션의 자동 검증 파이프라인이 이전 편집 시점의 오래된 테스트 실패 결과를 반복 재보고했다 — ①`TcpTlsTests.Tls_DefaultValidation_RejectsSelfSignedCertificate`(TimeoutException, 수정 이전 상태) ②`RudpLoopbackTests.Crc32c_PacketWithoutChecksum_DroppedBeforeSlotReservation`("L661" — 실제 파일 L661은 다른 테스트 본문, 스냅샷 불일치) ③`Crc32c_Enabled_RoundTrip`(TimeoutException).
- **임시 조치(완료)**: ①은 수정(채널 소유 계약 — TLS 1.3에서 클라이언트 검증 실패 후에도 서버 `Accepted` 발생, 구독자가 채널 정리) 후 전체 스위트 통과로 확인. ②③은 격리 13회 + 전체 스위트(120/120) 6회 연속 재현 불가 — 오래된/편집 중 스냅샷 산출로 판단, `dotnet test` 전체 결과를 기준으로 진행. GitHub Actions ubuntu CI에서도 전수 통과.
- **왜 기록**: ③은 스냅샷 표기가 없어 희귀 플레이크 가능성을 완전히 배제할 수 없다. 추후 RUDP 테스트가 다시 무작위 타임아웃을 보이면 `Crc32c_Enabled_RoundTrip`의 WaitUntil(기본 15초)과 LiteNetLib 폴링 스레드의 병렬 부하 경합을 의심하고 재현 조건(코어 수·동시 테스트 클래스 수)부터 조사한다.

## [해결] 네트워크 테스트 클래스 순차 실행으로 간섭 제거 (2026-09-09, 사이클 6)

- **원인 판정**: xUnit 기본 동작은 **클래스(=컬렉션) 병렬 실행**이다 — 실 소켓·폴링 스레드·타이밍 마감을 쓰는 3개 클래스(TCP·RUDP·TLS 루프백)가 서로, 그리고 동시에 돌던 빌드·분석기·검증 파이프라인의 자체 테스트 실행과 CPU를 두고 경쟁해 마감(15초)을 드물게 놓치는 것이 재보고된 타임아웃의 메커니즘이다(파이프라인 보고는 항상 내 명시 실행과 동시 구간 — 검증기 자체 테스트 실행이 내 `dotnet test`와 같은 머신에서 충돌).
- **조치**: 세 클래스를 `[Collection("network-loopback")]` 한 컬렉션으로 묶어 순차 실행(순수 로직 클래스는 병렬 유지). 전체 소요 8초→12초로 안정적 통과(121/121 연속).
- **잔여**: 파이프라인 자체 실행이 내 명시 실행과 겹치는 동안의 보고는 여전히 신뢰할 수 없다 — `dotnet test` 명시 결과만 기준으로 판단한다(스펙 검증 관례 유지).

## [보류 질문] NuGet 신뢰 게시(OIDC trusted publishing) 전환 여부 (2026-09-09)

- **무엇**: zizmor `use-trusted-publishing` 권고 — 현재 `NUGET_API_KEY` 시크릿 대신 GitHub OIDC 신뢰 게시로 NuGet 인증 전환.
- **왜 보류**: NuGet.org 계정·패키지 소유자 측 신뢰 게시 바인딩(리포지토리·워크플로 지문 등록)이 필요해 저장소 밖 조작이다 — 루프가 단독 수행 불가.
- **임시 조치**: 현행 API 키 방식 유지(시크릿 처리 경화 완료 — `persist-credentials: false`·템플릿 확장 제거). 전환 결정 시 워크플로는 `id-token: write` 권한 + `dotnet nuget push` 인증 교체로 좁은 diff로 가능.

## [보류] 독립 리뷰 P3 3건 — 처치 설계 노트 (2026-09-09, 사이클 17)

- **①RUDP 수용 경칠 창구의 통지 유실(P3) — 해결(2026-09-09, 사이클 18)**: 채널 래치(`NotifyTransportDisconnected`가 원인 선행 기록 → `TryConsumeLatchedDisconnect` 회수) + `RudpSession` 생성자가 구독 직후 회수 + `Session.Disconnected` 커스텀 접근자로 **늦은 구독자 즉시 재생**(구독당 1회, 표준 이벤트 의미론). 테스트 3건: 늦은 구독 재생·구독당 1회·수용 창구 래치 경로(내부 통지 시뮬). 앱 계층까지 전 경로 폐쇄.
- **②TCP Stop 후 TLS 핸드셰이크 지연 콜백(P3) — 해결(2026-09-09, 사이클 26 종결 시점)**: 보류 사유가 재분석으로 붕괴 — ①늦은 ClientHello 클라이언트로 결정적 재현 가능(무완료 클라이언트만 고려해 "테스트 불가"로 오판했었음) ②등록부 없이 수락 루프 토큰(Stop이 이미 취소)을 핸드셰이크 완료→HandOff 사이에 검사하는 것으로 충분. 구현: 정지 후 완료된 핸드셰이크는 폐기+슬롯 회수, `Accepted` 미발화. 테스트 +1(135). 잔여: 검사→발화 나노초 창구(통상 클래스).
- **③프레이머 경계 CTS 할당(P3)** — 프레임이 읽기 경계를 넘을 때마다 linked CTS+타이머 할당. 재사용 설계: 리더 수명 CTS 1개 + 반복 `CancelAfter` 재무장, 프레임 완료 시 `CancelAfter(Infinite)` 해제 — **주의: 해제를 빠뜨리면 유휴 연결에서 가짜 Timeout 단절(현 결함보다 나쁨)**. 미실시 사유: 미세 할당(스트래들 프레임당 ~2건) 대비 위험 비대칭 — 마이크로벤치로 유의미성 입증 시 신중 구현.
