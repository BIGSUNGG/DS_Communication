---
project: DS_Communication
type: overview
status: draft
tags: [changelog]
updated: 2026-07-11
---

# Changelog (Document)

문서 vault 변경만 기록한다. 제품 릴리스 노트는 저장소 릴리스/태그를 따른다.

## 2026-07-11

- Test: `Test/Communication.Tests` 추가 (SignalGate, MessageHandler 미등록 skip, TCP loopback SendAndFlush); 솔루션에 등록
- Known-Issues: 완료된 P1/P2/P3를 Fixed로 이동; Open은 Converter `IBufferWriter`/`byte[]` Serialize 할당(+ Sandbox ToArray pending)만 잔여
- Public-API / Configuration / Data-Flow: `SendAndFlushAsync`, `MessageQueueOptions`, `PollIntervalMs`, `SignalGate`, `IsConnected` 로컬 플래그, coalesce·ArrayPool 반영
- Home / CONTEXT: ADR [[0001-transport-pipeline-unification]] 링크
- Known-Issues: 구조·성능·병목 **전면 재작성** — Open 이슈별 해결 방안, P1~P3 로드맵, 스택 선택 가이드 추가 (할당/복사, 세션당 Task, coalesce, 백프레셔, 네임스페이스, 테스트 부재 등)
- 패키지 Version **1.0.2** (NuGet 태그 `v1.0.2`; 1.0.1 csproj Description 인코딩 손상 보정 포함)
- 패키지 Version 1.0.0 → **1.0.1** (NuGet 태그 `v1.0.1` 게시용)
- Known-Issues P0/P1/P2 코드 수정 반영: Semaphore 시그널, Session context, IOCP Accept·송신, TCP Flush/단일 Write, RUDP Poll 1ms·AcceptIfKey, EF Tools 제거; Open 항목(할당·스택 통합)만 문서에 잔여
- Known-Issues: 구조·성능·병목(P0 Semaphore/Session context/IOCP Accept·송신 레이스, P1 할당·Flush·Poll 15ms, P2 스택 복제·의존 비대칭 등) 문서화
- 프로젝트·패키지·코드 구조 분석 반영: Architecture(Overview/Components/Data-Flow), Reference(Packages/Public-API/Configuration), Guides, FAQ, Glossary 초안 작성
- Document Obsidian Vault 공통 스켈레톤 초기화
