# Chat.TCP — TCP 스택 수동 검증 샘플

`Communication.Network.TCP`로 만든 최소 채팅. 연결·메시지·끊김 확인이 목적이다.

## 실행

```bash
dotnet build Sandbox/Chat.TCP

# 터미널 1 — 서버 (기본 포트 32000)
dotnet Sandbox/Chat.TCP/bin/Debug/net10.0/Chat.TCP.dll server [port]

# 터미널 2 — 클라이언트
dotnet Sandbox/Chat.TCP/bin/Debug/net10.0/Chat.TCP.dll client [port] [이름]
```

입력한 줄은 서버 경유로 방 전체에 방송된다. `exit`로 종료.

## 확인 포인트

- 서버: `client accepted — total N` / `client left (Remote)`
- 클라이언트: 접속 후 `disconnected: Local` (exit 시) 또는 `Remote` (서버 종료 시)
- 직렬화는 샘플 로컬 `JsonChatConverter` — 라이브러리는 `IMessageConverter` 주입만 계약한다.
