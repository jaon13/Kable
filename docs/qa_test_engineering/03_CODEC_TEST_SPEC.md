# 03. Codec & Framing Layer Test Specification

> **Target Components**: `Kable.Codecs`  
> **Interfaces**: `IProtocolCodec<TMessage>`  
> **Key Implementations**: `AsciiLineCodec`, `BinaryLengthPrefixedCodec`  
> **Related Design**: [SYSTEM_DESIGN.md (Section 2)](file:///d:/Johnny/Kable/docs/SYSTEM_DESIGN.md)

---

## 1. 개요 및 계층의 역할

Codec 계층은 Transport 파이프라인에서 인입되는 불완전하고 파편화된 원시 바이트 시퀀스(`ReadOnlySequence<byte>`)를 타입화된 온전한 메시지 객체로 디코딩하고, 송신 메시지를 바이트 버퍼(`IBufferWriter<byte>`)로 직렬화하는 0-GC 프레이밍 계층입니다.  
하드웨어 노이즈, 무한 스트림 공격(OOM), 버퍼 경계 분할(Split Segment) 등에 대한 견고성을 제공합니다.

---

## 2. 현재 구현된 테스트 현황 (Existing Tests)

| 테스트 ID | 테스트 메서드명 | 검증 내용 |
| :--- | :--- | :--- |
| `TC_COD_01` | `TC_COD_01_MultiSegmentSequence_CorrectlyDecodesAndReturnsPooledArray` | 다중 세그먼트 버퍼에서 정상 문자열 디코딩 및 풀링 반환 |
| `TC_COD_02` | `TC_COD_02_DelimiterSplitAcrossSegments_SplitsCleanly` | 구분자(`\r\n`)가 세그먼트 경계에 걸쳐 분할된 경우 정상 분할 |
| `TC_COD_03` | `TC_COD_03_EmptyLinesBetweenMessages_DecodesAsEmptyString` | 연속된 빈 라인(`\n`, `\r\n`) 인입 시 빈 문자열 방출 |
| `TC_COD_04` | `TC_COD_04_LargePayload_ExceedingSingleChunk_AssemblesCorrectly` | 8KB 이상의 대용량 단일 페이로드 정상 조립 |
| `TC_COD_05` | `TC_COD_05_CustomAlarmPrefix_CorrectlyIdentified` | `$`, `#` 자율 알람 접두사 판별 여부 |
| `TC_COD_06` | `TC_COD_06_Utf8MultibyteSplitAcrossSegments_DecodesWithoutLoss` | UTF-8 유니코드 다중 바이트가 경계에 걸쳐 잘린 경우 복원 |
| `TC_COD_101` | `TC_COD_101_Codec_InfiniteStreamWithoutDelimiter_ThrowsProtocolViolationException` | 구분자 없이 `MaxFrameSize` 초과 시 프로토콜 위반 예외 |
| `TC_COD_102` | `TC_COD_102_Codec_SingleByteSlidingWindow_ReassemblesCompleteMessage` | 1바이트씩 들어오는 극한의 슬라이딩 윈도우 조립 |
| `TC_COD_103` | `TC_COD_103_Codec_ConsecutiveDelimiters_EmitsEmptyFramesWithoutException` | 다중 연속 구분자 인입 시 예외 없이 빈 프레임 연속 처리 |
| `TC_COD_104` | `TC_COD_104_Codec_MultiByteUtf8SplitAcrossSegments_PreservesCharacters` | 3개 이상 세그먼트로 나뉜 다국어 문자 무손실 디코딩 |
| `TC_COD_105` | `TC_COD_105_Codec_ArrayPoolRentAndReturn_MaintainsPerfectBalance` | ArrayPool 대여 및 반환 균형 유지 |
| `TC_COD_106` | `TC_COD_106_Codec_BinaryLengthPrefixedHeader_WaitsForFullBody` | 길이 접두사 바이너리 코덱에서 불완전 바디 대기 검증 |
| `TC_COD_107` | `TC_COD_107_Codec_TwoByteDelimiterSplitAcrossSegments_DecodesCleanly` | 2바이트 구분자 분할 처리 검증 |
| `TC_COD_108` | `TC_COD_108_Codec_IncompleteFrame_PreservesBufferCursorAndReturnsFalse` | 미완성 프레임 인입 시 버퍼 커서 보존 및 false 반환 |

---

## 3. 신규 보강 필요 테스트 케이스 명세 (Required New Test Cases)

### 📌 TC_COD_201: AsciiLineCodec_EmbeddedNullBytes_DecodesPreservingBinaryContent
- **목적**: 일부 구형 분석 장비(예: 질량분석기 제어기)에서 텍스트 프레임 내부에 상태 플래그로서 `0x00`(Null byte)이나 제어문자(`0x01` SOH, `0x02` STX)를 포함하여 전송하는 경우가 있습니다. 코덱이 문자열 중간의 Null Byte를 만나 문자열을 조기 종료(Truncation)하지 않고 끝 구분자까지 완벽하게 디코딩하는지 검증.
- **실행 단계**:
  1. 원시 바이트: `HEADER\0PAYLOAD\0EXTRA\n`
  2. `AsciiLineCodec.TryDecode()` 실행.
- **기대 결과**:
  - 디코딩된 문자열 길이가 원본과 정확히 일치하며 내부 널 문자가 유지됨.

### 📌 TC_COD_202: AsciiLineCodec_FrameExceedingLimit_ConsumesCorruptedFrameToPreventDeadlock
- **목적**: 하드웨어 노이즈로 인해 특정 프레임의 바이트 길이가 `MaxFrameSize`를 초과하여 `ProtocolViolationException`이 발생했을 때, 버퍼 커서가 해당 오염된 프레임 구분자까지 건너뛰어(Slice advance) 다음 유효한 정상 프레임을 정상적으로 수신할 수 있는지 확인.
- **기대 결과**:
  - 오염 프레임에 대해서는 예외 발생.
  - 버퍼가 그 자리에 멈춰서 영구 에러 루프에 빠지지 않고, 다음 구분자 이후의 정상 프레임을 성공적으로 디코딩함.

### 📌 TC_COD_203: AsciiLineCodec_CustomDelimiterZero_WorksCorrectly
- **목적**: 구분자로 `0x00`(C-String 널 종료자) 또는 `0x04`(EOT), `0x03`(ETX) 등 비표준 제어문자를 단일 구분자로 지정했을 때의 정상 분할 검증.
- **기대 결과**:
  - 지정된 단일 바이트 제어문자 기준으로 완벽한 프레이밍 수행.
