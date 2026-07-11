---
project: DS_Communication
type: adr
status: draft
tags: [adr, architecture, transport]
updated: 2026-07-11
---

# ADR 0001: Transport pipeline unification

## Status

Accepted

## Context

TCP / TCP_IOCP / RUDP each duplicate Session·Sender·Receiver patterns (signal gate, queue, dispose). Bugs and performance fixes were applied three times. Full merge into one pipeline would be a large rewrite and risk breaking Unity / netstandard2.1 consumers.

`IMessageConverter` still returns `byte[]` from `Serialize`, which forces per-message heap allocation. Changing it to `IBufferWriter` is a breaking major.

## Decision

1. **Short term (done in this effort):** Extract shared `SignalGate` and `MessageQueueOptions` in `Communication.Shared`. Keep three transport stacks; share utilities only.
2. **Medium term:** Introduce internal `ITransportSend` / `ITransportReceive` only if a fourth stack appears or duplication cost exceeds utility sharing.
3. **Long term / major:** Unify framing/send loops behind one pipeline; separately, introduce Converter `IBufferWriter` / pooled Serialize in a **major** version. Until then Converter allocations remain app/protocol responsibility.
4. **Namespaces:** Canonical names are `Communication.Network.*`. Legacy `Communication.TCP.*` and `Communication.Shared.Sessions.RUDPSession` remain for compatibility (RUDP legacy marked Obsolete).

## Consequences

### Positive

- Fixes and options (backpressure, SendAndFlush, poll interval) land once in Shared where possible.
- No forced consumer migration for Converter or TCP usings.

### Negative

- Three send/receive implementations remain; coalesce/ArrayPool logic still duplicated per stack.
- Converter GC pressure remains until a major.

### Neutral

- Stack choice (TCP vs IOCP vs RUDP) stays an app decision; see Known-Issues stack guide.

## Alternatives considered

- Immediate full pipeline merge — rejected as high risk without tests/major bump.
- Breaking Converter now — rejected (explicit non-breaking constraint).

## 관련

- [[Known-Issues]]
- [[Overview]]
- [[Components]]
- [[CONTEXT]]
