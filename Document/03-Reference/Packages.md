---
project: DS_Communication
type: reference
status: draft
tags: [packages, nuget]
updated: 2026-07-11
---

# Packages

| 패키지 | 설명 |
|--------|------|
| `Communication.Shared` | 공통 메시지·세션 추상화 |
| `Communication.Network.TCP.*` | TCP 소켓 클라이언트/서버 및 공유 타입 |
| `Communication.Network.TCP_IOCP.*` | Windows IOCP 기반 TCP 스택 |
| `Communication.Network.RUDP.*` | LiteNetLib 기반 RUDP 클라이언트/서버 및 공유 타입 |

## 설치

루트 `README.md` 및 NuGet.org 패키지 ID를 참고한다.

## 버전

- 패키지 버전·의존 버전은 저장소 `Directory.Build.props` (및 각 csproj)에서 관리한다.

## 관련

- [[Public-API]]
- [[Configuration]]
- [[Scope]]