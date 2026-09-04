# 04. 전송 계층 결함 주입 테스트 스펙 (Transport Fault Injection Tests)

> **문서 상태**: Approved Technical Specification  
> **대상 모듈**: `Kable.Transports`, `TcpConnectionContext`, `NamedPipeConnectionContext`, `SerialPortConnectionContext`

---

## 1. 개요 및 테스트 목표

산업용 장비(ICP-MS, 로봇 암, 센서 네트워크)와 실험실 PC 간의 통신은 열악한 전기적 환경, 노이즈, 작업자의 케이블 접촉 불량, OS 드라이버 크래시 등 다양한 물리적 결함에 노출됩니다.
본 스펙은 인위적인 통신 결함 주입(Fault Injection)을 통해 `Kable`의 물리 전송 어댑터들이 시스템 자원(소켓, 파일 핸들, 스레드)을 누수시키지 않고 견고하게 생존하거나 안전하게 종료되는지 검증합니다.

---

## 2. 상세 테스트 케이스 정의

### TC-TRN-01: TCP RST(강제 리셋) 및 Silent Half-Open 결함
- **우선순위**: P0
- **테스트 목적**:
  - 원격 서버가 정상적인 4-Way Handshake(FIN) 없이 소켓 옵션(`LingerState(true, 0)`)을 통해 강제로 `RST` 패킷을 전송할 때.
  - OS 레벨의 `SocketException(ConnectionReset)`이 발생했을 때 `Kable`이 `PipeReader/PipeWriter`를 깨끗하게 완결하고 `DeviceDisconnectedException`으로 래핑하는지 검증.
- **기대 결과**:
  - 소켓 리소스 및 파이프가 안전하게 반환되고, 연결 단절 이벤트가 발생함.

### TC-TRN-02: NamedPipe 서버 프로세스 불시 강제 종료 (Process Abrupt Kill)
- **우선순위**: P1
- **테스트 목적**: 로컬 IPC 파트너 프로세스가 작업 관리자나 OS에 의해 강제 kill 되었을 때, 클라이언트 측 `NamedPipeConnectionContext`가 블로킹되지 않고 즉각 연결 닫힘을 인지하는지 검증.
- **기대 결과**:
  - `PipeReader.ReadAsync()`가 EOF(`result.IsCompleted == true`)를 즉각 수신하고 세션이 정리됨.

### TC-TRN-03: 시리얼 포트(SerialPort) 물리적 분리 및 가상 COM 포트 강제 소멸
- **우선순위**: P1
- **테스트 목적**:
  - USB-to-Serial 컨버터가 물리적으로 뽑혔을 때 드라이버 레벨에서 발생하는 예외 시뮬레이션.
  - `SerialPort.BaseStream`이 닫히거나 예외를 던질 때 `Abort()`가 안전하게 호출되어 스레드 풀 행(Hang) 현상이 방지되는지 검증.
- **기대 결과**:
  - `DisposeAsync()`가 데드락 없이 완료되며 백그라운드 ReadLoop가 안전하게 종료됨.

### TC-TRN-04: 송신 파이프 버퍼 포화 및 배압(Backpressure) 처리
- **우선순위**: P1
- **테스트 목적**: 수신측이 데이터를 전혀 읽지 않고 TCP 수신 윈도우 버퍼를 채웠을 때, 송신측 `PipeWriter.FlushAsync()`가 메모리를 무제한 할당하지 않고 배압 제어를 수행하는지 검증.
- **기대 결과**:
  - `PipeOptions`의 PauseWriterThreshold / ResumeWriterThreshold 메커니즘이 정상 작동하여 송신 태스크가 안전하게 일시 중지되고, 수신이 재개되면 다시 전송됨.

### TC-TRN-05: 10,000건 초고속 텔레메트리 스트리밍 0-GC 벤치마크
- **우선순위**: P1
- **테스트 목적**: 10,000건의 텔레메트리 패킷 스트리밍 수신 중 Gen2 가비지 컬렉션(GC)이 0회 발생함을 검증하여 장시간 운영 시 메모리 단편화 및 GC Pause 방지.
- **기대 결과**:
  - `GC.CollectionCount(2) == 0` 달성.
