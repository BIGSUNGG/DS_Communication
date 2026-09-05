# DS_Communication

연결형 통신 전송 계층 라이브러리. Session·Channel·Framing·Pipeline을 제공하고,
직렬화 포맷은 `IMessageConverter`로 앱이 주입합니다. .NET Standard 2.1 (Unity 호환).

## 패키지

| 패키지 | 내용 | 의존 |
| -------- | ------ | ------ |
| `Communication.Shared` | Session, Pipeline, Channel 계약, Framing, `SendOptions`, `DisconnectReason` | (없음) |
| `Communication.Network.TCP.Shared` | `TcpSession`, `StreamByteChannel`, `TcpTransportOptions` | Shared |
| `Communication.Network.TCP.Server` | `TcpListener` 수락 루프 | TCP.Shared |
| `Communication.Network.TCP.Client` | `TcpConnector` 연결 | TCP.Shared |

```
dotnet add package Communication.Shared
dotnet add package Communication.Network.TCP.Server   # 서버만 필요하면
dotnet add package Communication.Network.TCP.Client   # 클라이언트만 필요하면
```

## 주요 동작

- 길이 프레이밍: 4바이트 little-endian 길이 + payload. 기본 프레임 상한 4MB(절대 상한 64MB).
- 송신: 큐 + 백프레셔(상한 도달 시 비동기 대기) + coalesce 배치 쓰기(기본 64KB).
  직렬화 실패 항목은 격리(해당 플러시만 예외)하고 송신은 계속.
- 수신: 프레임 완료 마감(기본 30초, 첫 바이트 도착 시 시작) 초과 시 `DisconnectReason.Timeout` 단절.
- 끊김은 `Session.Disconnected(DisconnectReason)` 1회 통지. 재접속·하트비트는 앱 책임.

## 빠른 시작

```csharp
// 서버 — 수락된 채널 위에 세션은 앱이 만든다.
using var listener = new TcpListener(IPAddress.Any, 32000);
listener.Accepted += channel =>
{
    var session = new TcpSession(channel, new MyConverter(), s => new MyHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"끊김: {e.Reason}");
};
listener.Start(new TcpTransportOptions { MaxConnections = 1000 });

// 클라이언트
var connector = new TcpConnector();
if (await connector.ConnectAsync("127.0.0.1", 32000))
{
    var session = new TcpSession(connector.Channel!, new MyConverter(), s => new MyHandler(s));
    await session.SendAndFlushAsync(new MyMessage());
}
```

옵션 상세는 [`Document/03-Reference/Configuration.md`](Document/03-Reference/Configuration.md) 참고.

## 저장소 구조

| 경로 | 설명 |
| ------ | ------ |
| `Source/` | 라이브러리 7 패키지 |
| `Test/Communication.Tests` | xUnit 테스트 (`dotnet test`) |
| `Sandbox/Chat.TCP` | TCP 수동 검증 채팅 샘플 |
| `Sandbox/Chat.RUDP` | RUDP 수동 검증 채팅 샘플 (`--selftest`) |
| `Document/` | Obsidian 문서 vault — 입구 [`Document/01-Overview/Home.md`](Document/01-Overview/Home.md) |
| `Legacy/` | 이전 스택·문서 아카이브 (유지보수 대상 아님) |

## 배포

`v*` 태그 → `Communication.Shared`, `tcp/v*` 태그 → TCP 3종, `rudp/v*` 태그 → RUDP 3종 (GitHub Actions `nuget-publish.yml`).
