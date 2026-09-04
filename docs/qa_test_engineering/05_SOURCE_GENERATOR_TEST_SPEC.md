# 05. Roslyn Source Generator Test Specification

> **Target Components**: `Kable.Generators`  
> **Source Generator**: `ProtocolSourceGenerator`  
> **Key Interfaces & Attributes**: `IDeviceWireCommand`, `[DeviceCommand]`  
> **Related Design**: [PROJECT_SPEC.md (Section 3)](file:///d:/Johnny/Kable/docs/PROJECT_SPEC.md)

---

## 1. 개요 및 계층의 역할

`Kable.Generators`는 .NET의 Roslyn 증분 소스 생성기(`IIncrementalGenerator`) 기술을 사용하여, 런타임 리플렉션이나 수동 문자열 포맷팅 없이 컴파일 타임에 하드웨어 와이어 명령 구조체(`IDeviceWireCommand`)의 직렬화 코드를 0-오버헤드로 자동 생성합니다.  
문자열 보간, 긴급 명령 마커(`IsUrgent`), 특수 문자 이스케이프의 정확성을 보장해야 합니다.

---

## 2. 현재 구현된 테스트 현황 (Existing Tests)

| 테스트 ID | 테스트 메서드명 | 검증 내용 |
| :--- | :--- | :--- |
| `TC_GEN_01` | `GeneratedCommand_StaticWireTemplate_FormatsCorrectly` | 파라미터가 없는 정적 템플릿(예: `oPON`) 정상 포맷팅 및 인터페이스 구현 |
| `TC_GEN_02` | `GeneratedCommand_WithParameters_InterpolatesCorrectly` | 단일 파라미터(경로, 정수) 포함 명령의 정확한 문자열 보간 |
| `TC_GEN_03` | `GeneratedCommand_UrgentMarker_SetsIsUrgentTrue` | `IsUrgent = true` 속성 지정 시 `IsUrgent` 프로퍼티가 true로 생성되는지 검증 |
| `TC_GEN_101` | `TC_GEN_101_Generator_DriverExecution_GeneratesExpectedSourcesWithoutDiagnostics` | `CSharpGeneratorDriver`를 통한 인메모리 컴파일 및 Diagnostic 클린 검증 |
| `TC_GEN_102` | `TC_GEN_102_Generator_MultiParamRecords_InterpolatesAllParametersCorrectly` | 다중 파라미터(밸브 ID, 상태값 등)의 복합 보간 검증 |

---

## 3. 신규 보강 필요 테스트 케이스 명세 (Required New Test Cases)

### 📌 TC_GEN_201: Generator_EscapedBracesInTemplate_GeneratesValidInterpolation
- **목적**: 템플릿 문자열에 정규식이나 JSON 구조 등 리터럴 중괄호(`{{` 또는 `}}`)가 포함된 경우, Roslyn 소스 생성기가 보간 구문과 리터럴 중괄호를 오인하지 않고 정상 컴파일 가능한 코드를 산출하는지 검증.
- **테스트 케이스 코드 시나리오**:
  ```csharp
  [DeviceCommand("SET_CONFIG:{{{Key}:{Value}}}")]
  public readonly partial record struct ConfigCommand(string Key, string Value);
  ```
- **기대 결과**:
  - 생성된 C# 코드에서 구문 컴파일 오류(CS1003 등)가 발생하지 않음.
  - 실행 시 `SET_CONFIG:{"PARAM":100}` 형태로 올바르게 산출됨.

### 📌 TC_GEN_202: Generator_SpecialCharactersInNamespace_GeneratesValidCode
- **목적**: 전역 네임스페이스(`global::`), 중첩 클래스 내부의 커맨드 정의, 특수 기호가 포함된 식별자 선언 시에도 문법 오류 없는 `.g.cs` 코드를 생성하는지 검증.
- **기대 결과**:
  - Roslyn Diagnostics 결과가 비어있고 컴파일이 성공함.

### 📌 TC_GEN_203: Generator_ClassInsteadOfStruct_ImplementsInterfaceCorrectly
- **목적**: 사용자가 `readonly partial record struct` 대신 일반 `partial class`로 커맨드를 선언했을 때에도 `IDeviceWireCommand`를 올바르게 구현하는지 검증.
- **기대 결과**:
  - `partial class` 키워드로 온전한 소스 코드가 생성됨.
