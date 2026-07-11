---
project: DS_Communication
type: guide
status: draft
tags: [guide]
updated: 2026-07-11
---

# Getting Started

## 사전 요구

- .NET SDK (netstandard2.1 호환 앱 또는 .NET 6+로 Sandbox 실행)
- Unity 사용 시 해당 Unity/.NET 프로필이 netstandard2.1 API와 호환되는지 확인
- RUDP 사용 시 LiteNetLib는 패키지 전이 의존으로 포함

## 빠른 시작 (소스)

1. 저장소 클론: https://github.com/BIGSUNGG/DS_Communication
2. `Communication.sln` 열기 또는 `dotnet build`
3. 필요한 스택 선택 → [[Packages]]
4. Sandbox로 동작 확인:
   - TCP: `Sandbox/Chat` (Server + ClientGUI)
   - RUDP: `Sandbox/RUDP_Chat`
   - IOCP TCP: `Sandbox/TCP_IOCP_Chat`

## NuGet으로 추가

```bash
dotnet add package Communication.Shared
# TCP 예
dotnet add package Communication.Network.TCP.Shared
dotnet add package Communication.Network.TCP.Client   # 또는 .Server
```

최소 앱 흐름:

1. `IMessageConverter` 구현
2. `Session` 상속 + `MessageHandler` 등록
3. `*Connector` / `*Listener`로 연결 후 세션 생성
4. `SendAsync` / Handler로 송수신

상세 레시피는 [[How-To]], API는 [[Public-API]].

## 관련

- [[How-To]]
- [[Packages]]
- [[Home]]
- [[Configuration]]
