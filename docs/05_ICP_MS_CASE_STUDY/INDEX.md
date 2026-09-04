# ICP-MS 다중 벤더 통합 통신 실사례 (ICP_MS Case Study INDEX)

> 본 디렉터리는 반도체 고순도 케미컬 분석의 핵심 장비인 **ICP-MS (유도결합 플라즈마 질량분석기)**를 대상으로, **다중 벤더(Agilent MassHunter vs PerkinElmer Syngistix) 확장 전략**과 실제 실무 연동 구현을 완전 통합 정리한 사양서 색인입니다.

---

## 🎯 핵심 통합 철학

1. **상위 도메인 인터페이스 단일화**:
   - 상위 LIMS 코어는 벤더에 종속되지 않는 `IIcpmsDriver` 인터페이스만 바라봅니다.
   - 플라즈마 제어(`IgnitePlasmaAsync`), 배치 시퀀스(`StartBatchAsync`), 긴급 정지(`AbortBatchAsync`)의 비즈니스 계약을 공유합니다.
2. **벤더별 독립 프로젝트 모듈 분리**:
   - `src/Icpms.MassHunter`: Agilent 7900/8900 (RS-232C 개행 프레이밍, 선점형 FIFO 락)
   - `src/Icpms.PerkinElmer`: PerkinElmer NexION (TCP/gRPC Syngistix RPC, 병렬 인터리빙)
3. **통신 엔진 단일화 (`Kable`)**:
   - 물리/논리 전송선(Serial, Socket)과 코덱만 교체 주입하여 동일한 `KableSession` 엔진으로 제어합니다.

---

## 📚 세부 사양서 목차

아래 링크를 통해 세부 주제별 사양서로 바로 이동하실 수 있습니다:

### 1. [01. Multi-Vendor Strategy (다중 벤더 확장 전략)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/01_MULTI_VENDOR_STRATEGY.md)
- 신규 장비 도입 시 3단계 표준 확장 절차 (1. 추상화 계약 확인 $\rightarrow$ 2. 독립 모듈 생성 $\rightarrow$ 3. DI 팩토리 등록)
- Agilent MassHunter vs PerkinElmer Syngistix 핵심 아키텍처 비교표

### 2. [02. Agilent Protocol Spec (애질런트 프로토콜 명세서)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/02_AGILENT_PROTOCOL_SPEC.md)
- Agilent 7900/8900 ExtDevice RS-232C 명령어/이벤트 와이어 바이트 명세표
- CR(`\r`) 프레임 종료 구분자 및 `TrafficKind` 분류

### 3. [03. Agilent Architecture & Design (애질런트 아키텍처 및 설계)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/03_AGILENT_ARCHITECTURE_DESIGN.md)
- `Icpms.MassHunter` 독립 모듈 폴더 구성표
- 3계층 클래스 다이어그램, 데이터 흐름도, 선점형 FIFO 락 & E-STOP 시퀀스 타이밍 차트

### 4. [04. Agilent Implementation Code (애질런트 프로덕션 소스코드)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/04_AGILENT_IMPLEMENTATION_CODE.md)
- `Icpms.MassHunter.Protocol`: `MassHunterCommands`, `MassHunterPackets`, `MassHunterProtocolCodec.g`
- `Icpms.MassHunter`: `IIcpmsDriver` 구현체 `MassHunterDeviceDriver`
- `Microsoft.Extensions.DependencyInjection`: `MassHunterServiceExtensions`

### 5. [05. PerkinElmer Syngistix Spec & Design (퍼킨엘머 사양 및 설계)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/05_PERKINELMER_SYNGISTIX.md)
- PerkinElmer Syngistix Remote 원천 코드(`RemoteSyngistix.cs`) 역설계 분석
- RPC 명령 명세표, 클래스 다이어그램, 시퀀스 타이밍 차트

### 6. [06. PerkinElmer Implementation Code (퍼킨엘머 프로덕션 소스코드)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/06_PERKINELMER_IMPLEMENTATION.md)
- `Icpms.PerkinElmer.Protocol`: `ISyngistixRpcClient`, `PeStatusResponse`, `PeInstrumentStatusEvent`
- `Icpms.PerkinElmer`: `IIcpmsDriver` 구현체 `PerkinElmerDeviceDriver`
- `Microsoft.Extensions.DependencyInjection`: `PerkinElmerServiceExtensions`

