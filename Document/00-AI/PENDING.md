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
