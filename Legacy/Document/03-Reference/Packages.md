---
project: DS_Communication
type: reference
status: draft
tags: [packages, nuget]
updated: 2026-07-11
---

# Packages

모든 Source 패키지 Version **1.0.2**, TargetFramework **netstandard2.1**.

| PackageId | 설명 | 프로젝트 참조 / 외부 패키지 |
|-----------|------|---------------------------|
| `Communication.Shared` | 공통 메시지·세션 추상화 | System.Buffers 4.5.1, System.Memory 4.5.5 |
| `Communication.Network.TCP.Shared` | TCP Session·Sender·Receiver | → Communication.Shared |
| `Communication.Network.TCP.Client` | `TCPConnector` | (없음) |
| `Communication.Network.TCP.Server` | `TCPListener` | (없음) |
| `Communication.Network.TCP_IOCP.Shared` | IOCP TCP Session·메시지 | → Communication.Shared |
| `Communication.Network.TCP_IOCP.Client` | IOCP `TCPConnector` | → Shared, TCP_IOCP.Shared |
| `Communication.Network.TCP_IOCP.Server` | IOCP `TCPListener` | → Shared, TCP_IOCP.Shared |
| `Communication.Network.RUDP.Shared` | RUDP Session·메시지·Dispatcher | → Shared, LiteNetLib 1.3.5 |
| `Communication.Network.RUDP.Client` | `RUDPConnector` | → RUDP.Shared (LiteNetLib) |
| `Communication.Network.RUDP.Server` | `RUDPListener` | → RUDP.Shared (LiteNetLib) |

## 설치

```bash
# 예: TCP 클라이언트 앱
dotnet add package Communication.Shared
dotnet add package Communication.Network.TCP.Shared
dotnet add package Communication.Network.TCP.Client
```

NuGet.org / 로컬 `dotnet pack` 산출물. 패키지 README는 루트 `README.md`를 포함한다 (`Source/Directory.Build.props`).

## 버전·패키징

| 설정 | 위치 | 값 |
|------|------|-----|
| 기본 IsPackable | 루트 `Directory.Build.props` | `false` (Sandbox 등) |
| Source IsPackable | `Source/Directory.Build.props` | `true` |
| TFM / Nullable / LangVersion | `Source/Directory.Build.props` | netstandard2.1, enable, latest |
| 개별 Version | 각 `.csproj` | 1.0.2 |

## 관련

- [[Public-API]]
- [[Configuration]]
- [[Components]]
- [[Scope]]
