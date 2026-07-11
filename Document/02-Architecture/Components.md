---
project: DS_Communication
type: architecture
status: stub
tags: [architecture, components]
updated: 2026-07-11
---

# Components

패키지/어셈블리 단위 컴포넌트 맵.

| 패키지 | 설명 |
|--------|------|
| `Communication.Shared` | 공통 메시지·세션 추상화 |
| `Communication.Network.TCP.*` | TCP 소켓 클라이언트/서버 및 공유 타입 |
| `Communication.Network.TCP_IOCP.*` | Windows IOCP 기반 TCP 스택 |
| `Communication.Network.RUDP.*` | LiteNetLib 기반 RUDP 클라이언트/서버 및 공유 타입 |

## 상세

| 컴포넌트 | 책임 | 의존 |
|----------|------|------|
| (추가 예정) | | |

## 관련

- [[Overview]]
- [[Packages]]
- [[Data-Flow]]