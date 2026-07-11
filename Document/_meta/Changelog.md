---
project: DS_Communication
type: overview
status: draft
tags: [meta, changelog]
updated: 2026-07-11
---

# Changelog

Document vault 변경 기록 (코드 릴리스 노트 아님).

## 2026-07-11 (후반)

- ADR [[0003-connection-lifecycle-options]]: **재접속·하트비트 앱 책임**, `DisconnectReason`, TCP **`SocketKeepAliveOptions`**
- 라이브러리에서 `ReconnectOptions`·재접속 이벤트·Channel 재바인딩 제거
- [[Getting-Started]] § 앱 재접속·keep-alive 예시; [[Configuration]]·[[Public-API]]·Components·Overview·Roadmap·Packages 동기화

## 2026-07-11

- ADR [[0006-session-ownership-and-converter]]: 앱이 Session 생성, 끊김은 Session만, Converter `IBufferWriter`/`Span`
- [[Public-API]]·[[Getting-Started]]·Handler/Session/Pipeline 동기화
- [[Getting-Started]] 사용 예시; ADR 0003–0005; [[Implementation-Roadmap]]; 핵심 개념·Packages
