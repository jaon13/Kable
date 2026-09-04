# QA Test Engineering Master Plan (INDEX)

> **Role**: World-Class QA Test Engineering Specialist & Lead Architect  
> **Target Solution**: `Kable` (Bedrock Transport + Reactive Interaction Engine)  
> **Documentation Version**: 2.0 (Deep Reliability & Failure Injection Edition)  

---

## 📌 Executive Summary & Quality Strategy

`Kable`은 산업용 분석 장비(ICP-MS, HPLC 등) 및 고신뢰성 제어 환경을 타깃으로 하는 **Zero-GC 지향 통신 프레임워크**입니다. 일반적인 웹/엔터프라이즈 환경과 달리, 하드웨어 통신 영역에서는 다음과 같은 극한 조건이 빈번하게 발생합니다:
1. **물리적 신호 잡음 및 단편화 (Fragmentation)**: 1바이트 단위 지연 유입, 패킷 분할, 쓰레기 데이터 노이즈
2. **비대칭 트랜잭션 및 타임아웃 지연 응답 (Phantom Responses)**: 하드웨어 응답 지연으로 인한 세션 오염
3. **돌발적 하드웨어 단선 (Physical Detachment)**: USB/RS-232C 강제 발거, 네트워크 전원 차단, TCP RST
4. **고빈도 주기적 텔레메트리 폭주**: 초당 수천 개의 계측 센서 데이터 수신 중 제어 명령 송수신

본 QA 엔지니어링 스위트는 기존 해피 패스(Happy-Path) 테스트를 넘어, **"시스템이 실패할 수 있는 모든 경계선(Dead Zones)"**을 선제적으로 찾아내고 검증하기 위해 구성되었습니다.

```mermaid
graph TD
    Root[Kable QA Engineering Master Plan] --> Doc1[01. Gap Analysis 01_GAP_ANALYSIS.md]
    Root --> Doc2[02. Codec & Framing 02_CODEC_AND_FRAMING_TESTS.md]
    Root --> Doc3[03. Session Concurrency 03_SESSION_CONCURRENCY_TESTS.md]
    Root --> Doc4[04. Transport Fault Injection 04_TRANSPORT_FAULT_INJECTION_TESTS.md]
    Root --> Doc5[05. All New Test Cases Catalog 05_ALL_NEW_TEST_CASES_CATALOG.md]
```

---

## 📚 Section Breakdown (관심사별 분리 명세)

### 1. [01. Test Suite Audit & Gap Analysis](file:///d:/Johnny/Kable/docs/qa_test_engineering/01_GAP_ANALYSIS.md)
* 현행 96개 테스트 케이스의 도메인별 커버리지 분석
* 산업용 장비 통신 관점에서 도출된 **8대 핵심 테스트 공백(Dead Zones)**
* 우선순위 매트릭스 (P0/P1/P2) 및 위험도 분석

### 2. [02. Codec & Framing Test Specification](file:///d:/Johnny/Kable/docs/qa_test_engineering/02_CODEC_AND_FRAMING_TESTS.md)
* `AsciiLineCodec` 및 커스텀 `IProtocolCodec<T>` 검증 스펙
* `MaxFrameSize` 초과 시 OOM 차단, 1바이트 슬라이딩 윈도우 단편화
* Multi-byte UTF-8 분할 경계, `ArrayPool<byte>` 메모리 대여/반환 무결성

### 3. [03. Session Concurrency & Resilience Specification](file:///d:/Johnny/Kable/docs/qa_test_engineering/03_SESSION_CONCURRENCY_TESTS.md)
* `KableSession<T>`의 RSocket 인터랙션 모델 및 상태 머신 검증
* 100+ 동시 FIFO 요청 경합 및 공정성(Fairness), 지연 팬텀 응답 격리
* 긴급 명령 OOB 우회, 대량 연결 단선 시 Fail-Fast, 취소 토큰 방출 후 세션 복원

### 4. [04. Transport Fault Injection Specification](file:///d:/Johnny/Kable/docs/qa_test_engineering/04_TRANSPORT_FAULT_INJECTION_TESTS.md)
* 물리 계층 장애 시뮬레이션 (TCP RST 패킷 강제 주입, NamedPipe 크래시, Serial 단선)
* 파이프라인 백프레셔(Backpressure) 및 Reader 지연 시나리오
* 10,000건 연속 텔레메트리 수신 시 Gen2 GC 제로 검증

### 5. [05. All New Test Cases Catalog](file:///d:/Johnny/Kable/docs/qa_test_engineering/05_ALL_NEW_TEST_CASES_CATALOG.md)
* 신규 제안 및 구현 대상 테스트 케이스 종합 카탈로그
* TC ID, 카테고리, 우선순위, 테스트 함수명, 핵심 검증 조건 정의

---

## 🛠️ Execution & Verification Baseline

```bash
# 전체 테스트 스위트 실행
dotnet test

# Kable 코어 런타임 테스트 상세 실행
dotnet test tests/Kable.Tests/Kable.Tests.csproj --logger "console;verbosity=detailed"

# Roslyn 소스 생성기 독립 테스트 실행
dotnet test tests/Kable.Generators.Tests/Kable.Generators.Tests.csproj
```
