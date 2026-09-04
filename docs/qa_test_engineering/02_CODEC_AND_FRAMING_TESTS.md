# 02. Codec & Framing Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Codecs`, `AsciiLineCodec`, `IProtocolCodec<T>`

---

## 1. Overview & Verification Goals

The framing layer serves as the primary line of defense, safely transforming fragmented or noisy byte streams into typed domain messages without heap allocations. This specification verifies zero-copy decoding across segment boundaries, extreme 1-byte fragmentation, multi-byte UTF-8 preservation, and `MaxFrameSize` overflow protection.

---

## 2. Test Cases Specification

### TC-COD-01 / TC-COD-101: Infinite Stream Without Delimiter (OOM Defense)
- **Priority**: P0
- **Objective**: Verify that continuous stream intake without a valid delimiter throws `ProtocolViolationException` once `MaxFrameSize` (default 64KB) is exceeded, preventing unbounded heap consumption.
- **Assertion**: `action.Should().Throw<ProtocolViolationException>().WithMessage("*Frame size limit exceeded*");`

### TC-COD-102: Single-Byte Sliding Window Fragmentation
- **Priority**: P1
- **Objective**: Verify that a complete message split into 1-byte chunks across individual `ReadOnlySequenceSegment` links is correctly reassembled without byte loss or state corruption.
- **Assertion**: Decoded message exactly matches the source string and sequence cursor advances cleanly.

### TC-COD-103: Consecutive Delimiters & Empty Frames
- **Priority**: P2
- **Objective**: Verify that consecutive delimiters (e.g., `\r\n\r\n\n\n`) emit empty string messages without raising index or slice bounds exceptions.

### TC-COD-104: Multi-Byte UTF-8 Characters Across Segment Boundaries
- **Priority**: P1
- **Objective**: Verify that 3-byte or 4-byte UTF-8 characters split across separate buffer segments decode without mojibake or byte corruption.

### TC-COD-105: ArrayPool Rent & Return Balance
- **Priority**: P0
- **Objective**: Verify that temporary buffers rented from `ArrayPool<byte>.Shared` during multi-segment sequence decoding are guaranteed to return via `finally` blocks even during abnormal terminations.

### TC-COD-106: Binary Length-Prefixed Header Codec
- **Priority**: P1
- **Objective**: Verify that custom length-prefixed codecs correctly inspect header size fields and defer decoding until full payload buffers arrive from the transport.
