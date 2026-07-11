---
project: DS_Communication
type: reference
status: draft
tags: [configuration]
updated: 2026-07-11
---

# Configuration

빌드·패키징 설정. 런타임 옵션은 대부분 생성자 인자(host, port, connectionKey)이며 별도 config 파일은 없다.

## Directory.Build.props

### 루트 (`Directory.Build.props`)

| 속성 | 값 | 의미 |
|------|-----|------|
| `IsPackable` | `false` | Sandbox·솔루션 기본은 NuGet 제외 |

### Source (`Source/Directory.Build.props`)

| 속성 | 값 | 의미 |
|------|-----|------|
| `TargetFramework` | `netstandard2.1` | Unity / 다중 런타임 |
| `ImplicitUsings` | enable | |
| `Nullable` | enable | |
| `LangVersion` | latest | |
| `IsPackable` | `true` | Source만 패키징 |
| `GenerateDocumentationFile` | `true` | XML 문서 |
| `NoWarn` | +1591 | missing XML comment 억제 |
| `PackageProjectUrl` | GitHub 저장소 | |
| README | 루트 README.md Pack | 패키지 설명 파일 |

## 런타임 옵션

| 스택 | 파라미터 | 비고 |
|------|----------|------|
| TCP / TCP_IOCP Client | `host`, `port` | |
| TCP / TCP_IOCP Server | `IPAddress`, `port` | |
| RUDP Client/Server | `host`/`IPAddress`, `port`, `connectionKey` | 키 불일치 시 연결 실패 |
| RUDP Client | 연결 대기 ~5초 | `RUDPConnector` 내부 |
| RUDP Send | `MessageSendContext.Reliable` | 기본 ReliableOrdered |

직렬화·핸들러·세션 ID 등은 앱/`IMessageConverter`·Sandbox에서 구성.

## 관련

- [[Packages]]
- [[Getting-Started]]
- [[Overview]]
