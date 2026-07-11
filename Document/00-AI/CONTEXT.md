---
project: DS_Communication
type: context
status: draft
tags: [ai, entry]
updated: 2026-07-11
---

# CONTEXT — DS_Communication

> **AI: 이 vault를 다룰 때 먼저 이 파일을 읽는다.**

## 한 줄 요약

TCP / RUDP 네트워크 통신용 .NET 라이브러리 모음. Unity 및 .NET Standard 2.1 호환.

## 저장소

- GitHub: https://github.com/BIGSUNGG/DS_Communication
- 문서 vault 루트: `Document/` (이 폴더가 Obsidian Vault)

## 읽을 순서

1. [[CONTEXT]] (지금)
2. [[GLOSSARY]]
3. [[Scope]]
4. [[Overview]] (Architecture)
5. [[Packages]]
6. `05-Decisions/` ADR (있을 경우)
7. [[CONVENTIONS]]

## 패키지 요약

| 패키지 | 설명 |
|--------|------|
| `Communication.Shared` | 공통 메시지·세션 추상화 |
| `Communication.Network.TCP.*` | TCP 소켓 클라이언트/서버 및 공유 타입 |
| `Communication.Network.TCP_IOCP.*` | Windows IOCP 기반 TCP 스택 |
| `Communication.Network.RUDP.*` | LiteNetLib 기반 RUDP 클라이언트/서버 및 공유 타입 |

## 형제 프로젝트

- DS_Communication — 네트워크 전송 (TCP/RUDP)
- DS_MessageProtocol — 메시지 직렬화
- DS_RPC — 분산 RPC (위 둘에 의존)

의존 방향: **DS_RPC → DS_MessageProtocol, DS_Communication**

## 관련 노트

- 사람용 시작: [[Home]]
- 범위: [[Scope]]
- 규칙: [[CONVENTIONS]]