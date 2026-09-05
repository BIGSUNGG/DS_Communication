---
project: DS_Communication
type: overview
status: draft
tags: [meta, changelog]
updated: 2026-09-05
---

# Changelog

Document vault 변경 기록 (코드 릴리스 노트 아님).

## 2026-09-05 (후반 17)

- **리스너 재시작 생명주기 회귀 테스트 2건 추가** — `Stop()` 후 `Start()`(재구성용 재시작)가 새 바인딩으로 수락을 재개하는 경로가 양쪽 모두 미검증이었다. ①`TcpListener`: 중지 → 재시작 → 새 포트 접속·수락, ②`RudpListener`: 중지(`_host` 교체) → 재시작 → 새 호스트·바인딩으로 수락 + `ActiveConnectionCount` 갱신 — 두 경로 모두 통과(재시작은 정상 동작, 계약으로 고정). **테스트 87 → 89건 통과**

## 2026-09-05 (후반 16)

- **수신 역직렬화 실패 → Error 단절 계약 회귀 테스트 2건 추가** — 보안 가이드의 「역직렬화 실패는 예외 → 파이프라인이 `Error` 단절로 격리(손상 세션 유지 금지)」가 스위트에 고정돼 있지 않았다. 테스트 더블에 `SelectiveThrowingDeserializer`(지정 본문에서만 역직렬화 예외) 추가, 두 경로 모두 검증 — ①바이트 경로(`ReceiveLoopByteAsync` catch), ②메시지 채널 경로(`OnMessageChannelReceived` catch): `Error` 단절 + 예외 보존 + 실패 프레임은 핸들러에 미도달. 메시지 경로의 단절 호출을 제거하면 해당 테스트만 실패하는 것을 확인(판별 검증). **테스트 85 → 87건 통과**

## 2026-09-05 (후반 15)

- **ADR 설계 기록 드리프트 동기화 (2건)** — ADR은 설계 결정의 사실 기록인데 ①0007의 Negative에 「송신 실패 격리는 직렬화 실패에만 적용」이 남아 5차(MaxFrameLength 양 경로 사전 검사) 이후의 계약과 모순(초대형 송신은 채널 도달 전 항목 격리로 세션 생존 — 채널 예외 경로=MTU 가드에만 세션 단절 잔존), ②0004의 「`InlineDispatch`로 즉시/큐 선택」이 2차의 메시지 채널 큐 강제를 미반영. 두 ADR 모두 후속 수정 표기로 갱신

## 2026-09-05 (후반 14)

- **README 드리프트 동기화 (NuGet 패키지에 실리는 사용자 문서)** — README는 전 패키지에 팩되는 문서인데, ① 패키지 표에 **RUDP 3종 누락**(Shared·TCP 3종만 표시 — 배포 문단의 `rudp/v*` 태그와 모순), ② 저장소 구조의 샌드박스 행이 「수동 검증」으로 남아 13차의 `--selftest` 미반영. RUDP 3종 행(의존 관계 포함)·설치 스니펫 추가, 양 샘플 행을 `--selftest` 표기로 갱신, 설치 코드 블록에 `console` 언어 태그

## 2026-09-05 (후반 13)

- **Chat.TCP `--selftest` 신설 + CI에 샌드박스 스모크 연결** — RUDP 샘플에만 있던 스크립트형 자가 검증(프로세스 내 루프백 서버+클라이언트 왕복, exit 0/1)을 TCP 샘플에도 추가(왕복 3회, 서버 측 수신 검증 — RUDP와 동일 패턴). 12차에 신설한 `.github/workflows/ci.yml`에 **샌드박스 셀프테스트 단계 추가** — `Chat.TCP`·`Chat.RUDP` 순서로 실행, 한쪽이라도 exit≠0이면 CI 실패. 로컬에서 양쪽 셀프테스트 exit 0 확인(전송 스택의 실제 왕복이 CI에 묶임 — 「샘플 실행 검증」이 수동 절차에서 자동 게이트로). 샘플 주석도 `--selftest` 사용법 갱신

## 2026-09-05 (후반 12)

- **CI 워크플로 신설 — 모든 브랜치 push·PR에서 빌드+테스트** — 기존에 검증이 실행되는 곳은 릴리스 태그 push(`nuget-publish.yml`의 `verify` 잡)뿐이라, 일반 push·PR은 **자동 검증 없이** 지나갔다(회귀가 릴리스 시점에만 드러남). `.github/workflows/ci.yml` 신설: push(전 브랜치) + pull_request 트리거, `verify` 잡과 동일한 단계(동일 SHA 핀 액션, dotnet 10.0.x, Release 빌드 + `--no-build` 테스트). 비밀 정보 없음 — secrets 미접촉. 로컬에서 동일 명령으로 Release 빌드·테스트 85건 통과 확인

## 2026-09-05 (후반 11)

- **Feature-Spec(스펙 계약 문서) 드리프트 동기화** — F3-3(백프레셔)·F3-4(핸들러 디스패치)·F3-5(디스패치 모드) 세 행이 실행 코드와 모순된 상태였다(2차 InlineDispatch 강제·3차 흐름 제어·8차 베이스 폴백이 스펙에 미반영). 각 행을 실제 계약으로 갱신 — ①F3-3: 「한계: 무제한 누적」 → 슬롯 대기 포함 상한 + `FlowControl` 실패 폐쇄, ②F3-4: 정확 타입 미등록 시 가장 구체적 베이스 폴백 후에만 skip, ③F3-5: 메시지 채널 경로는 `InlineDispatch` 무시·항상 큐 강제. **F3-10(프레임 길이 상한) 신설** — `MaxFrameLength`의 양 경로 동일 계약(바이트: 프레이머 거부·송신 격리 / 메시지: 송신 사전 격리·수신 역직렬화 전 거부)을 스펙에 명시(4·5차 구현 누락분)

## 2026-09-05 (후반 10)

- **`DisconnectReason.FlowControl` 신설 — 흐름 제어 단절을 일급 원인으로** — 3차 때 만든 수신 백프레셔 단절(선언된 `MaxPendingMessages` 상한 초과)이 `Error` + 예외 메시지 파싱으로만 구분 가능했다. 이제 열거형 멤버로 노출되어 앱이 메시지 파싱 없이 흐름 제어 단절을 감지할 수 있다. 파이프라인의 나머지 오류 경로(송신 루프·수신 역직렬화)는 `Error` 유지. 회귀 테스트 단언을 `FlowControl`로 갱신(콜백이 `Error`로 돌아가면 실패 — 확인). **닥트 드리프트 복구**: 3차 검증 과정의 `git checkout`이 되돌린 `MessageQueueOptions.MaxPendingMessages` 리마크(「무제한 누적」 한계 문구)를 실제 구현(상한 강제 + 흐름 제어 단절)과 일치하도록 복원 — 배포된 코드와 문서가 모순되던 상태 수정. Components의 `DisconnectReason` 표·Configuration의 `MaxPendingMessages` 행 동기화

## 2026-09-05 (후반 9)

- **TCP `ConnectTimeout` 옵션 신설 — 반개방 호스트 연결 실패 상한** — 호스트가 응답하지 않으면 OS SYN 재시도가 수십 초(Windows ≈21초)까지 끌었다(조정 수단 없음, RUDP의 5초 고정보다 더 악화된 UX). `TcpTransportOptions.ConnectTimeout`(ms, 기본 `null`=OS 기본)을 `TcpConnector.ConnectAsync`에 연결 — `Task.WhenAny` 경쟁으로 상한이 먼저 걸리면 `false`를 확정하고 진행 중 연결의 최종 예외는 관찰만 한다(미관찰 방지). 사용자 취소는 기존대로 `OperationCanceledException`. 회귀 테스트 2건 — ①TEST-NET-1(192.0.2.1) 블랙홀: 상한 1000ms 설정 시 2.5초 이내 false 확정(배선 제거 시 OS 기본 수십 초 — 실패 확인), ②상한을 설정해도 빠른 로컬 연결은 영향 없음. **Public-API 문서 동기화**: `TcpTransportOptions` 스니펫에 `MaxConnections`·`ConnectTimeout` 보강, 누락돼 있던 RUDP 옵션 표(`RudpTransportOptions` 5항목, 직전 단계 `ConnectTimeout` 포함) 신설(8차에서 만든 문서 드리프트 수정). **테스트 83 → 85건 통과** → [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]

## 2026-09-05 (후반 8)

- **`MessageHandler` 상속 기반 디스패치 폴백** — 정확 타입 미등록 메시지를 조용히 skip하던 동작이 조용한 메시지 유실의 근원이었다(컨버터가 다형 직렬화를 쓰면 파생 타입이 도착하는데 베이스만 등록한 앱은 도착을 못 본다). 이제 정확 타입이 없으면 **등록된 베이스 타입(상속·인터페이스) 중 가장 구체적인 것**으로 분배하고, 맞는 핸들러가 전혀 없을 때만 Trace 후 skip한다(기존 경고 계약 유지). 동률 후보(인터페이스·클래스 교차)는 먼저 발견된 쪽 — 등록 수가 작다고 보고 miss마다 선형 스캔. 회귀 테스트 4건 — 파생→베이스 폴백, 가장 구체적 베이스 우선, 정확 타입이 폴백보다 우선, 상속 관계 없는 타입은 여전히 skip(픽스 없이 폴백 2건 실패 — 확인). **테스트 79 → 83건 통과**

## 2026-09-05 (후반 7)

- **RUDP 채널 등록부 소유자 확인 회수 — peer id 재사용 교차 제거 버그 수정** — LiteNetLib은 peer id를 회수 후 풀에서 재사용한다. 기존 `ReleaseChannel`은 id만으로 `TryRemove`해서, **끊긴 세션의 늦은 Dispose**(앱이 정리를 미룬 경우)가 같은 id를 물려받은 새 세션의 등록부 항목을 잘못 걷어낼 수 있었다 — 새 세션의 슬롯이 조기 반환돼 `MaxConnections` 상한 강제가 무너지고(초과 수락), 채널이 고아화돼 이후 끊김 통지가 끊긴다. `TryGetValue` + `ReferenceEquals`로 현재 소유자가 이 채널일 때만 회수하도록 수정. 회귀 테스트 `LateDispose_AfterPeerIdReuse_DoesNotCorruptNewSession` — A 타임아웃 → id 풀 반환 → B가 같은 id 수락 → A의 늦은 Dispose(서버 쪽 채널) → B가 슬롯을 계속 점유하므로 (MaxConnections=1) C는 거부되어야 한다(픽스 없으면 C 수락 — 실패 확인). 테스트 작성 중 리스너 `DisconnectTimeout`이 B까지 죽이는 타이밍 함정(B 키핑얼라이브)과 슬롯 예약 vs Accepted 통지의 폴링 간격 경쟁도 함께 해소. **테스트 78 → 79건 통과**

## 2026-09-05 (후반 6)

- **RUDP `ConnectTimeout` 옵션 신설 — 침묵 호스트 연결 실패 상한** — 호스트가 응답하지 않으면(패킷 유실·블랙홀) 연결 실패는 LiteNetLib 재전송 소진으로만 결정되는데 기본값이 약 5초(500ms × 10회) 고정이라 조정 수단이 없었다. `RudpTransportOptions.ConnectTimeout`(ms, 기본 `null`=LiteNetLib 기본 유지)을 설정하면 재전송 간격 100ms × 시도 횟수 상한으로 환산해 그 이내에 연결 실패를 확정한다(`RudpNetHost` ctor). 회귀 테스트 `ConnectTimeout_SilentHost_FailsFastWithinBound` — **로컬 UDP 블랙홀**(수신 즉시 폐기, 응답 없음) 대상 연결이 상한 400ms 설정 시 2.5초 이내 false로 끝나는지 검증(배선 제거 시 기본 ≈5초로 실패 — 확인). **테스트 77 → 78건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 5)

- **메시지 채널(RUDP) 송신에도 `MaxFrameLength` 사전 검사 — 송신 실패 항목 격리 계약 완성** — 직전 단계(수신 거부)와 대칭: 수신 측이 동일 상한으로 세션을 끊으므로 초과 메시지를 와이어에 내보내면 상대방 세션을 죽이는 결과가 됐다(로컬 과실이 원격 단절로). `SendLoopMessageAsync`가 직렬화 직후 상한 검사를 추가해 초과 항목은 flush `ArgumentException`으로 격리하고 세션은 유지한다 — 바이트 경로의 기존 계약(「송신 격리·수신 거부」)이 양 경로에서 동일하게 성립. 회귀 테스트 `OversizeSend_OnMessageChannel_Isolated_SessionStaysConnected` — 상한 256에 1KB 송신 → flush 예외 + 세션 생존 + 서버 미수신 + 이후 정상 왕복 검증(픽스 없으면 예외 없이 나가 서버 단절 — 실패 확인). **테스트 76 → 77건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 4)

- **메시지 단위 채널(RUDP) 수신에도 `MaxFrameLength` 강제 — 수신 메모리 증폭 방어 완성** — 기존엔 상한이 바이트 경로(TCP 프레이머)에만 적용됐고, RUDP는 LiteNetLib 재조립이 자체 상한을 갖지 않아(MaxFragmentsCount 65535 × MTU ≒ **90MB**) 사실상 무제한 재조립·역직렬화가 가능했다(보안 가이드의 「수신 메모리 증폭 방어」가 RUDP에선 성립하지 않음). `MessagePipeline.OnMessageChannelReceived`가 역직렬화 전 상한 검사를 추가해 초과 payload를 `InvalidDataException` → `Error` 단절로 실패 폐쇄한다. 재조립 자체는 LiteNetLib 내부 풀에서 순간적으로 일어나므로 전달 지점에서의 상한으로 충분하다. 회귀 테스트 `OversizeMessage_OnMessageChannel_DisconnectsFailClosed` — 상한 256 설정에 1KB payload 분할 전송 → 단절 + 핸들러 미도달 검증(픽스 없이는 단절 없음 — 10초 타임아웃). **테스트 75 → 76건 통과** → [[../03-Reference/Configuration|Configuration]]·[[../04-Guides/Security|Security]]

## 2026-09-05 (후반 3)

- **메시지 단위 채널(RUDP) 수신 백프레셔 공백 해소 — 흐름 제어 단절** — `MaxPendingMessages` 상한을 **슬롯 대기(메시지 보유)까지 포함**해 강제한다(`MessagePipeline.EnqueueForDispatchAsync`의 `_pendingReceives` 카운터). 기존엔 핸들러가 밀리면 대기자가 상한을 넘어 **무제한 누적**(문서화된 한계)되어 느린 핸들러 + 빠른 peer 조합이 메모리 압박 경로였다. UDP는 상대방을 늦출 수 없으므로 초과 시 `DisconnectReason.Error`(메시지에 「흐름 제어」 기재)로 실패 폐쇄한다. 바이트 채널(TCP) 경로는 수신 루프의 순차 대기(동시 대기 ≤ 1)라 기존 백프레셔(추가 읽기 중단)가 그대로 유지된다. 이전 단계(InlineDispatch 강제 무시)와 합쳐져 수신 누적이 선언된 상한 안에 완전히 묶인다. 회귀 테스트 `ReceiveOverflow_OnMessageChannel_DisconnectsFailClosed` — 원시 채널 200건 폭주 + 메시지당 100ms 핸들러로 상한 8을 초과시켜 단절을 검증(픽스 없이는 단절 없음 — 10초 타임아웃). **테스트 74 → 75건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반 2)

- **메시지 단위 채널(RUDP) 경로에서 `InlineDispatch` 강제 무시** — `MessageQueueOptions.InlineDispatch=true`를 요청해도 `IMessageChannel` 경로는 항상 큐 디스패치를 강제한다(`MessagePipeline.Start`의 디스패치 루프 기동 조건도 함께 변경). 수신 콜백이 **세션 간 공유 폴링 스레드**에서 실행되므로, 핸들러를 그 자리에서 돌리면 느린 핸들러 하나가 다른 세션의 수신·수락·`MaxConnections` 상한 강제까지 막는 구조적 취약점이었다(호스트의 설계 계약 「앱 핸들러는 폴링 스레드에서 실행되지 않는다」를 공개 옵션으로 우회 가능). 바이트 채널(TCP) 경로는 기존 인라인 동작 유지. 회귀 테스트 `InlineDispatchOnRUDP_ForcesQueuedDispatch_OtherSessionsUnstalled` — 세션 A 핸들러 3초 점유 중 세션 B 왕복이 1.5초 안에 완료되는지 검증(픽스 없이는 실패 확인). **테스트 73 → 74건 통과** → [[../03-Reference/Configuration|Configuration]]

## 2026-09-05 (후반)

- **RUDP `MaxConnections` 고갈 공격 회귀 테스트 2건 추가** (`Test/Communication.Tests/RudpLoopbackTests.cs`) — ① `HostileStalledHandshake_ReturnsSlotAfterTimeout`: LiteNetLib 2.1.4 **와이어 형식을 직접 구성한 접속 요청 패킷**(프로토콜 ID 13, ConnectRequest=6, IPv4 SocketAddress + 키 문자열)으로 검증 키 수락 후 **침묵하는 공격자**가 슬롯을 잡아도 상한 초과 거부가 유지되고 `DisconnectTimeout` 후 슬롯이 반환되며 정상 클라이언트가 재수락되는지 4단계로 검증. ② `WrongKeyFlood_LeavesNoSlotResidue`: 틀린 키 접속 폭주(전파 거절)가 `ActiveConnectionCount`에 파편을 남기지 않고 이후 올바른 키 수락이 가능한지 검증. **테스트 71 → 73건 통과** → [[../04-Guides/Security|Security]]

## 2026-09-05

- **로드맵 4단계 RUDP 구현 완료** — `Communication.Network.RUDP.Shared`(RudpSession·RudpMessageChannel·RudpSendOptions/RudpDeliveryMethod·RudpTransportOptions·내부 RudpNetHost)·`.Server`(RudpListener)·`.Client`(RudpConnector) 신설. 네임스페이스는 셋 다 `Communication.Network.RUDP`, Server·Client는 RUDP.Shared의 `InternalsVisibleTo`로 내부 `RudpNetHost` 공유. **LiteNetLib 2.1.4** 고정, `PackageReference`는 RUDP.Shared에만 — LiteNetLib 타입은 `RudpNetHost`·`RudpMessageChannel` 두 파일에만 등장하고 공개 API에는 노출되지 않는다. 버전 1.0.0, **NuGet 배포 트리거는 이번 범위 밖** → [[../02-Architecture/Code-Structure|Code-Structure]]·[[../03-Reference/Packages|Packages]]·[[../00-AI/CONTEXT|CONTEXT]]
- ADR [[0007-rudp-three-way-split-and-polling]] 신규 — RUDP 3분할, **호스트당 전용 폴링 스레드 1개**(접속 수와 무관한 스레드 수 + 세션별 디스패치 큐로 멀티클라이언트 병목 차단), `RudpDeliveryMethod` 5값(LiteNetLib `DeliveryMethod`와 같은 이름·값) + `RudpSendOptions` 불변·공용 인스턴스 5개, 분할 불가 방식 MTU 초과 `ArgumentException`, 수락 전 슬롯 예약 `MaxConnections`, 클라이언트 채널의 호스트 소유, `NetManager.Stop(true)` graceful 정지, `AutoRecycle=false` → 수신마다 `Recycle()`. 대안 기각: `UnsyncedEvents=true`·peer당 스레드·1 패키지·`DeliveryMethod` 직접 노출
- **메시지 채널 경로 끊김 관측 해결** — `IMessageChannel`에는 수신 루프가 없어 원격 끊김을 스스로 감지하지 못한다. `RudpMessageChannel`의 internal `TransportDisconnected`를 `RudpSession`이 구독해 `Session.MarkDisconnected`로 이어 붙여 **`Communication.Shared` 무수정**으로 `Disconnected(Remote)` 계약을 지킴. LiteNetLib → Shared `DisconnectReason` 매핑(Timeout→Timeout, RemoteConnectionClose→Remote, DisconnectPeerCalled→Local, 나머지→Error) → [[../03-Reference/Public-API|Public-API]]·[[../02-Architecture/Data-Flow|Data-Flow]]·[[../02-Architecture/Channel|Channel]]
- **알려진 한계 기록**: 분할 불가 방식의 MTU 초과 `ArgumentException`은 `MessagePipeline`이 채널 오류로 취급해 해당 항목 flush 예외 완료 + **세션 `Disconnected(Error)`**로 끊는다(예외는 `DisconnectedEventArgs.Exception` 보존). 「송신 실패 항목 격리」는 직렬화 실패에만 적용 — 항목 격리로 내리려면 Shared 수정이 필요해 범위 밖으로 둠 → [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]]·[[../03-Reference/Public-API|Public-API]]
- **RUDP 테스트 5건 추가** (`Test/Communication.Tests/RudpLoopbackTests.cs`) — 양방향 왕복·끊김 원인(Local/Remote), 리스너 1대에 동시 클라이언트 4개 + 접속 수 회수, 전송 방식 5종 메시지별 왕복, 분할 불가 방식 3종 MTU 초과 `ArgumentException` + `ReliableOrdered` 8KB 분할·재조립, `MaxConnections` 초과 거부·슬롯 회수 후 재수락. **테스트 66 → 71건 통과**
- **`Sandbox/Chat.RUDP` 신설** — Chat.TCP와 동일한 server/client 채팅 샘플에 `--selftest` 모드 추가(프로세스 내 서버+클라이언트로 전송 방식 5종 1회씩 왕복 후 exit 0). 대화형 모드에서 `'!'` 접두 줄은 Unreliable로 전송해 메시지별 옵션을 수동 검증할 수 있다 → [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]·[[../04-Guides/Getting-Started|Getting-Started]]
- **`RudpTransportOptions` 신규** — `MaxConnections`(기본 무제한)·`DisconnectTimeout`(기본 5000ms, UDP half-open 감지의 유일한 신호)·`ConnectionKey`(기본 `"DS_Communication.RUDP"`)·`IPv6`(기본 false) 네 항목만 노출. poll 간격·`UnsyncedEvents`는 의도적으로 비노출 → [[../03-Reference/Configuration|Configuration]]
- **「스택당 1 패키지·3분할 금지」 규칙 폐기** — TCP(2.0.0)에 이어 RUDP도 3분할. 남은 스택(TCP_IOCP·IPC)만 1 패키지 → [[../00-AI/CONVENTIONS|CONVENTIONS]]·[[../03-Reference/Packages|Packages]]·[[../02-Architecture/Overview|Overview]]
- 기존 노트 동기화 — [[../01-Overview/Feature-Spec|Feature-Spec]](F1-4·F1-5·F4-2~F4-5 구현, F4-5 MTU 가드 신규, F5-2 패키지 구성, 구현 상태 2026-09-05), [[../02-Architecture/Components|Components]](`Network.RUDP` 실제 타입 표 + 내부 `RudpNetHost`), [[../01-Overview/Home|Home]](테스트 53 → 71, RUDP 완료), `DisconnectReason` 표에 누락됐던 `Timeout` 보강(Components·Data-Flow), vault 내 모호한 `[[WikiLink]]`(Legacy 동명 파일과 충돌) 12곳을 상대 경로 링크로 수정
- **RUDP 3종 NuGet 2.0.0 배포** — `nuget-publish.yml`에 `rudp/v*` 태그 잡(`publish-rudp`) 추가: `rudp/v2.0.0` → RUDP 3종 팩·푸시. `publish-shared`의 실행 조건을 `${{ !contains(github.ref_name, '/') }}`로 단순화해 `tcp/`·`rudp/` 태그에서 Shared 재배포 시도를 원천 차단(기존엔 `--skip-duplicate`로 회피). 액션 버전 주석(`# v5.4.0` 등)을 80자 린트 때문에 `uses:` 위 줄로 이동, RUDP 3종 csproj `Version` 1.0.0 → **2.0.0**(Shared·TCP 2.0.0 통합 릴리스 정렬) → [[../03-Reference/Packages|Packages]]·`README.md`

## 2026-09-04

- **TCP 3분할 + NuGet 2.0.0 배포** — `Communication.Network.TCP` 단일 프로젝트를 `Communication.Network.TCP.Shared`(TcpSession·StreamByteChannel·TcpTransportOptions)·`.Server`(TcpListener)·`.Client`(TcpConnector)로 분할, 네임스페이스는 `Communication.Network.TCP` 유지. Server·Client는 TCP.Shared의 `InternalsVisibleTo`로 내부 API 공유. `Communication.sln`·`Test`·`Sandbox/Chat.TCP` 참조 이전, 기존 프로젝트 삭제. 「스택당 1 패키지」 규칙은 TCP에 한해 폐기 → [[../03-Reference/Packages|Packages]]·[[../02-Architecture/Code-Structure|Code-Structure]]·[[../00-AI/CONTEXT|CONTEXT]]
- **GitHub Actions TCP 배포 트리거** — `nuget-publish.yml`에 `tcp/v*` 태그 잡 추가: `tcp/v2.0.0` → TCP 3종 팩·푸시, 기존 `v*` → Communication.Shared 경로 유지
- **CONTEXT 테스트 수 동기화** — 「테스트 53 통과」를 실제 스위트 규모인 「테스트 66 통과」로 갱신 (2026-09-01 프레임 상한 회귀 6건 추가 이후 미반영분)

## 2026-09-01

- **[[../04-Guides/Security|Security & Production Checklist]] 노트 신규** — 컨버터 안전 제약(금지 직렬화기·다형 타입 `$type` 위험·고정 타입 역직렬화 권장 패턴)과 프로덕션 투입 체크리스트(암호화·인증 미제공, 타임아웃·상한·연결 수 하드닝 현황, 앱 책임 목록). [[../01-Overview/Home|Home]]·[[../00-AI/CONTEXT|CONTEXT]] 읽기 맵에 연결
- **프레임 길이 상한 옵션화** — 고정 상수 64MB를 `MessageQueueOptions.MaxFrameLength`로 이동하고 기본값 **4MB**로 하향(메모리 증폭 표면 축소). 64MB는 `LengthPrefixFramer.MaxFrameLength` 절대 상한으로 잔류(초과 설정은 거부); 수신 초과 프레임은 `Error` 단절, 송신 초과 항목은 격리. 기본 상한 적용·커스텀 상한 거부(수신 단절·송신 격리)·경계값 회귀 테스트 6건 추가(테스트 66건 통과) → [[../03-Reference/Configuration|Configuration]]·[[../02-Architecture/Pipeline|Pipeline]]
- **`MaxConnections` 수락 상한 추가** — 연결 고갈 공격 방어. `TcpTransportOptions.MaxConnections`(기본 `null` 무제한) 상한 도달 시 수락된 연결을 즉시 닫고 수락 계속(거부 연결은 `Accepted` 통지 없음); 채널 Dispose 시 슬롯 회수(`StreamByteChannel` 내부 Dispose 훅), `TcpListener.ActiveConnectionCount`로 현황 노출. 상한 초과 거부·슬롯 회수 후 재수락 회귀 테스트 추가(테스트 60건 통과) → [[../02-Architecture/Components|Components]]·[[../03-Reference/Configuration|Configuration]]
- **읽기 유휴 타임아웃(`FrameTimeout`) 추가** — 슬로로리스(부분 프레임 끌어안기) 방어. 프레임 첫 바이트 도착 시 마감 시작(기본 30초, `null`/`0` 비활성화), 미완성 시 `TimeoutException` → `DisconnectReason.Timeout`(신규 열거 멤버) 단절; 완전 유휴 연결은 대상 아님(하트비트는 앱 책임). 드립 공급 단절·유휴 무영향·비활성 회귀 테스트 4건 추가(테스트 59건 통과) → [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]·[[../02-Architecture/Pipeline|Pipeline]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-9)
- `LengthPrefixFrameReader` **메모리 증폭 공격 차단** — 버퍼 성장을 선언된 프레임 길이 기준 사전 할당에서 **실제 누적 데이터 기준**(버퍼 가득 찰 때만 2배 성장)으로 변경. 헤더 4바이트만으로 67MB 선할당 불가; 선언 64MB·부분 도착 시 버퍼 상한 + 성장 경로 정상 재조립 회귀 테스트 2건 추가(테스트 55건 통과) → [[../02-Architecture/Pipeline|Pipeline]](수신)
- `MessagePipeline` Dispose 경쟁 수정 — 송신 성공 경로의 순서를 뒤집어 **Flush 완료가 슬롯 해제보다 먼저**(바이트 코얼리스 배치·메시지 경로 각 1곳). Dispose가 쓰기 완료와 슬롯 해제 사이에 끼어 슬롯이 정리돼도 `SendAndFlushAsync` 호출자가 hang하지 않음; 채널별 훅으로 경쟁 창을 결정적으로 재현하는 회귀 테스트 2건 추가, 테스트 수 표기 53건 통과로 동기화 → [[../02-Architecture/Pipeline|Pipeline]](송신)
- 회귀 커버리지 보강 — `MessageHandler`(`ConcurrentDictionary` 교체 후 테스트 부재)에 미등록 타입 skip·지연 등록·동시 등록+디스패치 무유실 3건, `MessagePipeline` 코얼리스 배치 중간 직렬화 실패 부분 되감기(`RewindTo(frameStart > 0)`) 핀 1건 추가; `MessageQueueOptions` XML 문서 오타(`수진은`→`수신은`) 수정, 테스트 수 표기 51건 통과로 동기화 → [[../00-AI/CONTEXT|CONTEXT]]·[[../01-Overview/Home|Home]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F6)
- `SendAndFlushAsync` 사전 취소 토큰 처리 — 토큰이 이미 취소됐으면 큐잉하지 않고 즉시 취소 완료 Task 반환(메시지 미송신), 회귀 테스트 포함 47건 통과 → [[../03-Reference/Public-API|Public-API]] 런타임 의미 갱신
- `TcpListener.Accepted` 최신 구독 반영 — 수락 루프가 수락마다 최신 구독자를 읽어 `Start` 이후 구독자도 채널을 받음(옛 스냅샷 방식은 구독 전 수락 채널을 폐기), 회귀 테스트 포함 46건 통과 → [[../03-Reference/Public-API|Public-API]] 노트 추가
- `TcpTransportOptions.NoDelay` 추가(기본 `true`) — `TcpConnector`·`TcpListener`가 연결·수락 소켓에 적용; 라이브러리 coalesce와 중복되는 Nagle 지연 제거, 회귀 테스트 포함 45건 통과 → [[../03-Reference/Configuration|Configuration]](NoDelay 섹션)·[[../03-Reference/Public-API|Public-API]] 동기화
- 메시지 채널 수신 백프레셔 한계 문서화 — `IMessageChannel` 경로는 콜백 차단 방지를 위해 슬롯 대기를 비동기로 넘기므로 핸들러가 밀리면 대기자가 상한 넘어 무제한 누적 가능(바이트 채널은 상한 유지); 동작 변경 없음 → `MessageQueueOptions` XML 문서·[[../03-Reference/Public-API|Public-API]]·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-3) 기록
- 세션 미부착 파이프라인 송신의 동기 throw 제거 — 끊김과 동일하게 **예외 완료 Task** 반환 (메시지는 구분), `SessionTests` 회귀 테스트 추가 → [[../03-Reference/Public-API|Public-API]] 런타임 의미 갱신
- `MessageHandler` 등록 테이블을 `ConcurrentDictionary`로 교체 — 세션 시작 후 지연 등록 시 디스패치 스레드와의 읽기/쓰기 경쟁 제거 → [[../02-Architecture/Handler|Handler]] 동기화
- 송신 직렬화 실패 **항목 격리**: 실패한 메시지의 `flush`만 예외 완료하고 송신 루프는 계속 — 세션 끊김(`Disconnected(Error)`)으로 격상하지 않음(수신 핸들러 예외 격리와 대칭). 바이트 배치 경로는 부분 프레임을 되감아 폐기, 격리된 항목의 백프레셔 슬롯은 반환 → [[../02-Architecture/Pipeline|Pipeline]]·[[../03-Reference/Public-API|Public-API]] 동기화, 회귀 테스트 포함 43건 통과

## 2026-08-31

- [[Feature-Spec]] 신규 — 레거시에서 이어받을 기능 명세: F1 연결·수락, F2 세션 수명·송신, F3 메시지 파이프라인, F4 전송별 기능, F5 플랫폼·패키지, F6 검증, 이어받지 않음(레거시와 차이)
- [[../00-AI/CONTEXT|CONTEXT]] 관련 노트 · [[../01-Overview/Home|Home]] 읽기 맵에 연결
- 런타임 계약 확정: 끊김·Dispose 후 송신은 **예외로 완료된 Task**, 백프레셔 상한 시 **비동기 대기**, `InlineDispatch` 기본 `false`, 핸들러 `Action` 예외는 **Trace 후 수신 루프 계속** → [[../01-Overview/Feature-Spec|Feature-Spec]]·[[../03-Reference/Public-API|Public-API]]·[[../03-Reference/Configuration|Configuration]] 동기화; [[../05-Decisions/0004-send-options-and-handler-api|ADR 0004]] `SendOptions` 마커 클래스 확정
- 로드맵 1~3단계 구현 완료: `Communication.Shared` 전체 + `Communication.Network.TCP` + `Test/Communication.Tests` (xUnit 23건) + `Sandbox/Chat.TCP` 실행 검증; [[../02-Architecture/Code-Structure|Code-Structure]]·[[../03-Reference/Packages|Packages]]·[[../04-Guides/Getting-Started|Getting-Started]]·[[../01-Overview/Home|Home]]·[[../00-AI/CONTEXT|CONTEXT]] 실제 구현과 동기화, [[../01-Overview/Feature-Spec|Feature-Spec]] 구현 상태 표 추가, keep-alive 플랫폼 적용 방식(Windows IOControl / Unix 원시 옵션) 문서화
- 리뷰 수정 동기화: 수신 경로를 `LengthPrefixFrameReader` **단일 누적 버퍼 + 제로카피 슬라이스**로 재작성, 송신 프레임 검증(빈 페이로드 거부·상한)·끊김 시 파이프라인 정지·`Disconnected` 구독자 격리 문서화 → [[../02-Architecture/Pipeline|Pipeline]]·[[../02-Architecture/Session|Session]]·[[../03-Reference/Configuration|Configuration]](`CoalesceLimitBytes` 추가)·[[../01-Overview/Feature-Spec|Feature-Spec]](F3-8 재서술, 테스트 41건) 갱신

## 2026-07-11 (후반)

- ADR [[0003-connection-lifecycle-options]]: **재접속·하트비트 앱 책임**, `DisconnectReason`, TCP **`SocketKeepAliveOptions`**
- 라이브러리에서 `ReconnectOptions`·재접속 이벤트·Channel 재바인딩 제거
- [[../04-Guides/Getting-Started|Getting-Started]] § 앱 재접속·keep-alive 예시; [[../03-Reference/Configuration|Configuration]]·[[../03-Reference/Public-API|Public-API]]·Components·Overview·Roadmap·Packages 동기화

## 2026-07-11

- ADR [[0006-session-ownership-and-converter]]: 앱이 Session 생성, 끊김은 Session만, Converter `IBufferWriter`/`Span`
- [[../03-Reference/Public-API|Public-API]]·[[../04-Guides/Getting-Started|Getting-Started]]·Handler/Session/Pipeline 동기화
- [[../04-Guides/Getting-Started|Getting-Started]] 사용 예시; ADR 0003–0005; [[../02-Architecture/Implementation-Roadmap|Implementation-Roadmap]]; 핵심 개념·Packages
