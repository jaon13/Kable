# 📏 Kable - Conventions & Strict Rules

> **문서 상태**: 단일 진실 공급원 (Single Source of Truth)  
> **최종 갱신**: 2026-09-04  
> **적용 대상**: 전체 `Kable` 솔루션 코드 작성, 신규 드라이버 연동, AI 코드 생성

---

## 1. 파일 크기 및 모듈 분리 규칙 (Modular Isolation)

1. **단일 파일당 300~500줄 초과 엄격 금지**:
   - 파일이 300줄을 초과하면 책임 분할(Partial 클래스 또는 하위 서비스 추출)을 검토합니다.
   - 500줄을 초과하는 코드는 즉시 분리 대상입니다.
2. **코드 생략 표기 금지**:
   - `// ... existing code ...`, `// todo: rest of logic` 등 축약 및 생략 표기 일체 금지. 항상 완전하고 명시적인 코드를 작성합니다.
3. **3단 계층의 철저한 관심사 분리**:
   - `Transports`: 소켓/시리얼/파이프 I/O만 담당 (프로토콜 해석 금지).
   - `Codecs`: 바이트 분할/합성만 담당 (소켓/세션 상태 조작 금지).
   - `Engine`: 상호작용 라우팅만 담당 (파일 I/O, 디스크 로깅 금지).

---

## 2. 네이밍 규칙 및 C# 코딩 표준 (Naming & Style)

| 요소 | 표기법 | 예시 |
|---|---|---|
| **클래스, 구조체, 레코드** | PascalCase | `KableSession`, `PacketTraceRecord` |
| **인터페이스** | IPascalCase | `IConnectionContext`, `IProtocolCodec` |
| **메서드** | PascalCase | `RequestAsync`, `TryDecode`, `FormatWireMessage` |
| **비동기 메서드** | Async 접미사 | `StartAsync`, `ConnectAsync`, `FlushAsync` |
| **프로퍼티** | PascalCase | `IsConnected`, `SupportsCorrelationId` |
| **필드 (private/protected)** | `_camelCase` | `_fifoLock`, `_pendingRequests`, `_context` |
| **매개변수 및 지역변수** | camelCase | `timeout`, `urgentMessage`, `buffer` |

### C# 13 / .NET 10 권장 스타일
- **Zero-Allocation**: 불필요한 `byte[]` 복사를 지양하고 `ReadOnlySequence<byte>`, `ReadOnlySpan<byte>`, `IBufferWriter<byte>` 적극 활용.
- **ValueTask 최적화**: I/O가 즉시 완료될 수 있는 경로에는 `Task` 대신 `ValueTask` 사용.
- **최신 언어 기능**: File-scoped namespaces, Pattern matching, Primary constructors.

---

## 3. 엄격한 금지 규칙 (Strict Prohibitions)

> [!CAUTION]
> 다음 규칙을 위반한 코드는 CI 및 PR 단계에서 즉시 거부됩니다.

1. **동기식 I/O 및 블로킹 호출 금지**:
   - `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` 호출 금지. 모든 네트워크/I/O는 `async`/`await` 및 `CancellationToken`을 기반으로 처리합니다.
2. **인라인 예외 무시(Swallowed Catch) 금지**:
   - `catch { }` 빈 블록으로 하드웨어 예외를 은폐하는 행위 금지. 단선 또는 장애 감지 시 반드시 `OnConnectionClosed()` 및 Fail-Fast 정책을 트리거해야 합니다.
3. **Core 라이브러리 내 무거운 외부 의존성 추가 금지**:
   - `Kable.Core` 및 `Kable` 코어 패키지는 Serilog, EntityFramework, UI 프레임워크 등에 직접 의존할 수 없습니다.

---

## 4. TDD 및 프롬프트 파이프라인

1. **테스트 우선 (Test-First & Spec-Driven)**:
   - 신규 통신 기능 추가 또는 버그 수정 시 `tests/Kable.Tests/`에 정상/경계값/결함 주입 테스트를 먼저 작성합니다.
2. **단계적 프롬프트 파이프라인 (Plan $\rightarrow$ Review $\rightarrow$ Execute)**:
   - **Plan**: 구현 계획과 변경될 파일 목록을 먼저 제시.
   - **Review**: 사용자 검토 및 승인 대기.
   - **Execute**: 승인된 계획에 따라 지정된 파일만 점진적 구현.
3. **Git 마이크로 커밋**:
   - 빌드 검증 및 96개 테스트 통과 완료 단위마다 `feat:`, `fix:`, `docs:`, `test:` 커밋을 생성합니다.
