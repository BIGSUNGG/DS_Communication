---
project: DS_Communication
type: troubleshoot
status: draft
tags: [faq]
updated: 2026-07-11
---

# FAQ

## Q: TCP 클라이언트만 참조하면 Session/Sender가 없다

A: `Communication.Network.TCP.Client` / `.Server`는 Connector·Listener만 제공한다. Session·프레이밍은 `Communication.Network.TCP.Shared`(+ `Communication.Shared`)를 함께 참조한다. Sandbox Chat 참고.

## Q: RUDP 연결이 항상 실패한다

A: `connectionKey`가 클라이언트·서버에서 일치하는지 확인. 서버는 `AcceptIfKey`로 키를 검증한다. Client는 약 5초 대기 후 실패한다. 방화벽/포트·LiteNetLib poll 루프가 유지되는지도 본다.

## Q: 메시지 바이트가 깨지거나 길이 오류가 난다

A: TCP는 **4바이트 length-prefix** 계약을 양측이 동일하게 써야 한다. Converter 출력과 상대측 framing이 다르면 Deserialize가 실패한다. RUDP는 라이브러리가 패킷 경계를 담당하고 payload만 Converter로 넘긴다.

## Q: TCP와 TCP_IOCP의 차이는?

A: TCP 스택은 `TcpClient`/`NetworkStream`. TCP_IOCP는 `Socket`/`SocketAsyncEventArgs`. 앱 콜백 시그니처(`TcpClient` vs `Socket`)가 다르다. 프레이밍(length-prefix) 개념은 같다.

## Q: RUDP에서 peer 패킷이 사라진다

A: `NetworkReceiveEvent`를 세션마다 중복 구독하지 말고 `RUDPNetworkReceiveDispatcher`로 peer별 Receiver에 분배한다. 미등록 peer 패킷은 Recycle된다.

## Q: Unity에서 쓸 수 있나?

A: 타깃이 netstandard2.1이다. Unity 버전이 해당 API·의존(LiteNetLib 등)을 지원하는지 확인한다. 직렬화는 Unity 쪽 `IMessageConverter` 구현으로 맞춘다.

## 관련

- [[Known-Issues]] — 구조·성능·병목 (코드 분석)
- [[How-To]]
- [[Getting-Started]]
- [[Data-Flow]]
- [[Packages]]
