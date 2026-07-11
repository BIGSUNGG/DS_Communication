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

- Known-Issues P0/P1/P2 코드 수정 반영: Semaphore 시그널, Session context, IOCP Accept·송신, TCP Flush/단일 Write, RUDP Poll 1ms·AcceptIfKey, EF Tools 제거; Open 항목(할당·스택 통합)만 문서에 잔여
- Known-Issues: 구조·성능·병목(P0 Semaphore/Session context/IOCP Accept·송신 레이스, P1 할당·Flush·Poll 15ms, P2 스택 복제·의존 비대칭 등) 문서화
- 프로젝트·패키지·코드 구조 분석 반영: Architecture(Overview/Components/Data-Flow), Reference(Packages/Public-API/Configuration), Guides, FAQ, Glossary 초안 작성
- Document Obsidian Vault 공통 스켈레톤 초기화
