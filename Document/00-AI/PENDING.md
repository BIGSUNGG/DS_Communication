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

- **①RUDP 수용 경칠 창구의 통지 유실(P3)** — `TransportDisconnected`가 폴링 스레드에서 발생하는 순간과 앱이 `RudpSession` 생성(구독)하는 순간 사이 창구에 피어가 끊기면 통지가 영영 유실된다. 완화 설계: 채널에 마지막 전송 단절 이유를 레치(Interlocked)하고 세션 생성자가 구독 직후 소비(`TryConsumeLatchedDisconnect`). 미실시 사유: 매우 狭은 창구 + 2026-09-09 P2 수정(정지 통지)이 주요 경로를 이미 막음 — 실측 사례 시 우선 구현.
- **②TCP Stop 후 TLS 핸드셰이크 지연 콜백(P3)** — fire-and-forget 핸드셰이크 태스크가 Stop 이후 최대 15초 내 `Accepted`를 발생시킬 수 있다(슬롯 회수는 정확, 타이밍만 무한하지 않음). 완화 설계: 진행 중 핸드셰이크 추적 + Stop에서 공유 CTS 취소. 미실시 사유: 종료 중 셧다운 레이스이며 채널은 정상 동작 — 실측 사례·요구 시 추적 도입.
- **③프레이머 경계 CTS 할당(P3)** — 프레임이 읽기 경계를 넘을 때마다 linked CTS+타이머 할당. 재사용 설계: 리더 수명 CTS 1개 + 반복 `CancelAfter` 재무장, 프레임 완료 시 `CancelAfter(Infinite)` 해제 — **주의: 해제를 빠뜨리면 유휴 연결에서 가짜 Timeout 단절(현 결함보다 나쁨)**. 미실시 사유: 미세 할당(스트래들 프레임당 ~2건) 대비 위험 비대칭 — 마이크로벤치로 유의미성 입증 시 신중 구현.
