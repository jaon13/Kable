# 🧭 Kable - Antigravity Agent Rules

> 이 파일은 Antigravity AI가 `Kable` 통신 엔진 작업 시 자동으로 인지하고 항시 준수하는 워크스페이스 최상위 규칙입니다.

---

## 1. 단일 진실 공급원 (SSOT) 강제 참조
- 코드 작성/설계/수정 전 반드시 아래 문서를 최우선 기준으로 준수합니다:
  - `docs/PROJECT_SPEC.md`: 프로젝트 개요, 아키텍처 계층 구조, 0-GC 설계 원칙
  - `docs/SYSTEM_DESIGN.md`: 핵심 인터페이스(`IConnectionContext`, `IProtocolCodec`, `IDeviceSession`), 파이프라인 흐름도
  - `docs/CONVENTIONS.md`: 코딩 표준, 파일 크기 제한, 동기 블로킹 금지 및 TDD 원칙

## 2. 모듈 분리 및 파일 크기 엄격 제어
- 단일 파일은 **300~500줄을 초과할 수 없습니다**.
- 코드 생략 표기(`// ... existing code ...`, `// todo: rest of logic` 등)는 절대 금지되며, 항상 온전하고 명시적인 코드를 작성해야 합니다.
- Bedrock 추상화 계층 분리: 물리 I/O(Transports) $\rightarrow$ 직렬화(Codecs) $\rightarrow$ 상호작용(Engine)의 3단 계층 책임을 엄격히 유지합니다.

## 3. 스펙 기반 구현 (TDD & Spec-Driven)
- 신규 코덱, 프로토콜, 세션 기능 추가 시 `tests/Kable.Tests/`에 단위/결함 주입 테스트를 먼저 작성합니다.
- 작성된 테스트를 통과하는 0-Allocation 최소 구현 코드만 작성합니다.

## 4. 단계적 프롬프트 파이프라인
- 한 번에 대규모 수정을 하지 않고 **Plan $\rightarrow$ Review $\rightarrow$ Execute** 파이프라인을 준수합니다.
  1. **Plan**: 기존 아키텍처를 훼손하지 않는 구현 계획과 수정/생성 파일 목록(300줄 이내) 제시.
  2. **Review**: 사용자 검토 및 승인 대기.
  3. **Execute**: 승인된 계획에 따라 지정된 파일만 점진적 구현 및 테스트 검증.

## 5. Git 마이크로 커밋
- 테스트 통과 및 빌드 검증이 완료된 최소 작업 단위마다 `feat:`, `fix:`, `docs:`, `test:` 커밋을 남깁니다.
- 문제 발생 시 긴 디버깅 대신 직전 정상 커밋으로 리셋(`git reset`) 후 세션을 재시작합니다.
