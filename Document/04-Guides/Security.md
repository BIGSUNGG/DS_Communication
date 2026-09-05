---
project: DS_Communication
type: guide
status: stable
tags: [guide, security, production]
updated: 2026-09-05
---

# Security & Production Checklist

직렬화 선택은 앱의 책임이다(`IMessageConverter` 주입 — 라이브러리는 내장 직렬화기가 없다). **라이브러리의 안전성은 컨버터 선택에 그대로 의존한다.** 샌드박스 `JsonChatConverter`는 고정 타입 패턴의 예시다.

## 컨버터 안전 제약

### 금지 직렬화기

페이로드에서 CLR 타입을 선택·인스턴스화하는 직렬화기는 **원격 코드 실행(RCE)** 경로다. `IMessageConverter` 구현에 사용하지 않는다.

| 금지 | 이유 |
| ------ | ------ |
| `BinaryFormatter` | 임의 타입 인스턴스화 = RCE. .NET 8+에서 기본 제거됨(`SYSLIB0011`) |
| `NetDataContractSerializer` | 위와 동일 계열 |
| `ObjectStateFormatter`·`LosFormatter` | 동일 계열 |
| `SoapFormatter` | 동일 계열 |
| `XmlSerializer`(신뢰 못 하는 루트 타입) | 타입 지정이 느슨하면 같은 류의 인스턴스화 공격면이 생긴다 |

### 다형 타입 허용의 위험

페이로드가 타입 식별자(`$type` 등)를 통해 **자료를 만들 타입을 스스로 고르게 하면** 타입 혼동·가젯 인스턴스화 공격면이 열린다.

- System.Text.Json: 기본 설정은 다형 역직렬화를 **허용하지 않는다** — 안전. 위험해지는 건 `JsonPolymorphic`/`JsonDerivedType`으로 다형을 명시 허용할 때다. 허용해야 한다면 **파생 타입을 코드에서 명시 등록한 허용목록**만 사용한다.
- Newtonsoft.Json: `TypeNameHandling.All`/`Auto` 금지. `None` 유지가 원칙.

### 권장 패턴 — 고정 타입 역직렬화

타입은 **컨버터·와이어 프로토콜이 결정**하고, 페이로드는 타입 선택에 관여하지 못하게 한다.

1. 채널·메시지 종류당 고정 타입 `Deserialize<TMessage>()` 1개.
2. 여러 타입이 필요하면 앱 정의 식별자(열거형·접두어)로 **신뢰 범위 안의 `switch`** — 페이로드가 타입 이름을 직접 지정하지 않는다.
3. 역직렬화 실패는 예외 → 파이프라인이 `Error` 단절로 격리한다(손상 세션 유지 금지).

## 프로덕션 투입 체크리스트

| 상태 | 항목 |
| ------ | ------ |
| ❌ 미제공 | **암호화·인증 없음** — 평문 TCP / 평문 UDP(RUDP). 신뢰 네트워크 내부 전용이며, 공개망 투입 시 반드시 별도 암호화 레이어(SslStream 등)를 겹쳐야 한다(로드맵 항목). RUDP는 LiteNetLib `PacketLayerBase`(XorEncryptLayer·Crc32cLayer) 자리가 있지만 **현재 `null`** — 미사용 |
| ✅ 옵션 | `FrameTimeout`(기본 30초) — 슬로로리스(부분 프레임 끌어안기) 방어 |
| ✅ 옵션 | `MaxFrameLength`(기본 4MB, 절대 상한 64MB) — 수신 메모리 증폭 방어. 바이트 경로는 프레이머, **메시지 단위 채널(RUDP)은 역직렬화 전 거부**(LiteNetLib 재조립 자체 상한 ≒90MB) |
| ✅ 옵션 | `MaxConnections` — 연결 고갈 방어. TCP는 수락 후 즉시 닫음, **RUDP는 접속 요청 시점에 슬롯을 예약**해 같은 폴링 배치의 다수 요청이 상한을 함께 넘지 못하게 하고 `Reject()` |
| ✅ 옵션 | RUDP `DisconnectTimeout`(기본 5000ms) — UDP는 스트림 끝이 없어 **half-open 감지의 유일한 신호**. 앱 하트비트와 별개 |
| ⚠️ 기본값 주의 | RUDP `ConnectionKey` 기본값(`"DS_Communication.RUDP"`)은 **공개 상수**다 — 그대로 두면 키 검증이 사실상 방어 역할을 못 하므로 공개망에서는 앱별 값으로 교체해야 한다(인증 대체는 아님) |
| ✅ 구조 | RUDP 분할 불가 전송 방식(`Sequenced`·`ReliableSequenced`·`Unreliable`)의 MTU 초과 송신은 와이어에 나가기 전에 `ArgumentException`으로 거부 — 조용한 유실 없음. 단 이 예외는 **세션을 `Disconnected(Error)`로 끊는다**(자기 자신에 대한 서비스 거부 가능성이 있으므로 메시지 크기 상한은 앱이 관리) |
| ✅ 구조 | 수신 버퍼는 누적 데이터 기준 성장(선언 길이 선할당 없음) |
| ✅ 구조 | 변형 프레임(길이 0·음수·상한 초과) 수신 시 `InvalidDataException` → 단절(fail-closed) |
| ✅ 구조 | RUDP 클라이언트 호스트는 **들어오는 접속 요청을 무조건 거부** — 임시 로컬 포트로의 역방향 접속 차단 |
| ✅ 테스트 | RUDP `MaxConnections` 고갈 공격 회귀 테스트 2건 — ①검증 키로 **핸드셰이크를 완성하지 않는 공격자**(와이어 패킷 직접 구성)가 슬롯을 잡아도 상한 초과 거부가 유지되고 `DisconnectTimeout` 후 슬롯이 반환됨, ②**틀린 키 접속 폭주**가 슬롯 파편을 남기지 않고 이후 정상 수락이 가능 |
| ⚠️ 앱 책임 | 하트비트·재접속, 인증·세션 관리, 메시지 레벨 속도 제한(방송 증폭), 로그 수집 시 예외 메시지 새니타이즈(공격자 제어 콘텐츠가 Trace에 남을 수 있음) |

## 관련

- [[../02-Architecture/Pipeline|Pipeline]](수신·송신 검증) · [[../03-Reference/Configuration|Configuration]](타임아웃·상한 옵션)
- [[../05-Decisions/0006-session-ownership-and-converter|ADR 0006]] — Converter는 앱 주입
- [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]] — RUDP 수락 정책·MTU 가드·접속 수 예약
