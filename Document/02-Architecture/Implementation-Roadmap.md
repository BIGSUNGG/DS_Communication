---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, roadmap]
updated: 2026-07-11
---

# Implementation Roadmap

합의된 구현·검증 순서. 코드 착수 시 이 순서를 따른다.

## 순서

```text
1. Communication.Shared
2. Test (Shared 단위 — Pipeline 계약·큐·Framing·DisconnectReason 등)
3. Communication.Network.TCP
   → Test (TCP) + Sandbox/Chat.TCP (간단 채팅)
4. Communication.Network.RUDP  (LiteNetLib — [[0005-rudp-litenetlib-interim]])
   → Test (RUDP) + Sandbox/Chat.RUDP
5. Communication.Network.TCP_IOCP
   → Test (TCP_IOCP) + Sandbox/Chat.TCP_IOCP
```

IPC·자체 RUDP 교체는 이 로드맵 **이후**.

## 단계별 완료 기준

| 단계 | 완료 조건 |
|------|-----------|
| Shared | 계약·Pipeline·Session·`DisconnectReason` 컴파일; Document와 타입명 정합 |
| Shared Test | Framing·큐 백프레셔·Disconnect 플래그 등 순수 로직 테스트 통과 |
| 각 전송 | Connector/Listener/Session/Channel 동작; loopback 또는 로컬 통합 테스트 |
| 각 Sandbox | 서버+클라 채팅으로 연결·송수신·끊김 수동 확인 (재접속은 샘플 앱 로직) |
| 전송 Test | 프레이밍/세션/keep-alive 옵션(해당 시) 자동 테스트 추가 |

## Sandbox

- 스택마다 **별 채팅 샘플** (`Sandbox/Chat.TCP`, `Sandbox/Chat.RUDP`, `Sandbox/Chat.TCP_IOCP` 등).
- 목적: “제대로 작동하는지” 빠른 수동 검증. GUI 복잡도보다 연결·메시지·끊김이 우선.
- 하트비트가 필요하면 **샘플 앱 메시지**로 넣는다 (라이브러리 기능 아님).

## Test

- `Test/`에 Shared + 전송별 프로젝트(또는 한 테스트 어셈블리 내 폴더) .
- 전송 완료 시점마다 해당 스택 테스트를 **반드시** 추가한 뒤 다음 스택으로 진행.

## 관련

- [[Scope]]
- [[Packages]]
- [[0005-rudp-litenetlib-interim]]
- [[Public-API]]
