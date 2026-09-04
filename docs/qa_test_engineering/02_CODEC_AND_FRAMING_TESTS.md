# 02. Codec & Framing Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Codecs`, `AsciiLineCodec`, `IProtocolCodec<T>`  

---

## 1. 개요 및 검증 목표

프레이밍 및 코덱 계층은 하드웨어 전송 계층으로부터 유입되는 임의의 바이트 스트림(`ReadOnlySequence<byte>`)을 도메인 메시지로 변환하는 최전방 방어선입니다.
단편화, 패킷 결합, 비정상 바이트 유입 등의 환경에서도 0-GC를 유지하며 완벽하게 메시지를 복원해야 합니다.

---

## 2. 심층 테스트 케이스 명세

### TC-COD-101: Delimiter 없는 무한 스트림 OOM 방어 (기존 검증 완료)
- **우선순위**: P0
- **목표**: 구분자가 유실된 잡음 데이터가 64KB(`MaxFrameSize`)를 초과하여 유입될 때 `ProtocolViolationException`을 발생시켜 메모리 폭주를 차단.

### TC-COD-102: 1바이트 슬라이딩 윈도우 극단적 단편화 (기존 검증 완료)
- **우선순위**: P1
- **목표**: 1바이트씩 쪼개진 `ReadOnlySequenceSegment` 체인을 온전한 하나의 문자열로 누락 없이 복원.

### TC-COD-103: 연속된 Delimiter 및 빈 프레임 처리 (기존 검증 완료)
- **우선순위**: P2
- **목표**: `\n\n\r\n` 등 빈 메시지 연속 수신 시 인덱스 범위 초과 예외 없이 안전하게 빈 문자열 방출.

### TC-COD-107: [NEW] 2바이트 복합 구분자(`\r\n`)의 세그먼트 경계 분할 디코딩
- **우선순위**: P0
- **목표**: 2바이트 구분자(`0x0D, 0x0A`)를 사용하는 프로토콜에서 `\r`이 Segment 1의 마지막 바이트이고 `\n`이 Segment 2의 첫 번째 바이트로 들어올 때 온전하게 프레임을 식별하여 반환하는지 검증.
- **검증 코드**:
  ```csharp
  var seg1 = "COMMAND_DATA\r";
  var seg2 = "\n";
  // 시퀀스 연결 후 TryDecode 성공 및 잘린 잔여 버퍼 정확성 단언
  ```

### TC-COD-108: [NEW] 불완전 프레임 수신 시 AdvanceTo 및 커서 불변성 유지
- **우선순위**: P1
- **목표**: 구분자가 아직 도착하지 않은 경우 `TryDecode`가 `false`를 반환하며, `ReadOnlySequence<byte>` 버퍼 커서가 원본 시작점을 온전히 보존하고 있는지 검증.
- **단언**: `consumed`는 `buffer.Start`, `examined`는 `buffer.End`로 정확히 지정되어 파이프라인 무한 루프 방지.

### TC-COD-109: [NEW] 잘못된 UTF-8 바이트 시퀀스 유입 시 대체 문자(Fallback) 복원력
- **우선순위**: P2
- **목표**: 하드웨어 전압 노이즈로 인해 깨진 바이트(`0xFF, 0xFE` 등 유효하지 않은 UTF-8)가 포함된 프레임이 인입되었을 때, 프로세스 크래시 없이 안전하게 복원 또는 예외 없이 처리되는지 검증.
