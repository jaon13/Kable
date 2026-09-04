# 04. Transport Fault Injection Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Transports`, `TcpConnectionContext`, `TcpListener`, `NamedPipeConnectionContext`, `SerialPortConnectionContext`  

---

## 1. 개요 및 검증 목표

물리적 전송 계층은 현장 설치 환경의 가혹한 전기적/물리적 요인(케이블 탈락, 전원 공급 중단, 포트 점유 충돌 등)에 직접 노출됩니다.
전송 계층의 비정상 단절이 상위 세션 엔진과 파이프라인으로 안전하고 지체 없이 전파(Fail-Fast)되는지 검증합니다.

---

## 2. 심층 테스트 케이스 명세

### TC-TRN-101: TCP 강제 리셋(RST) 패킷 주입 즉시 단선 감지 (기존 검증 완료)
- **우선순위**: P0
- **목표**: `LingerState(true, 0)`로 소켓을 강제 차단할 때 파이프 리더가 즉각 종료되고 대기 중인 모든 요청이 수 밀리초 내에 `DeviceDisconnectedException`으로 중단됨을 검증.

### TC-TRN-102: NamedPipe 서버 비정상 프로세스 크래시 감지 (기존 검증 완료)
- **우선순위**: P1
- **목표**: 로컬 IPC 파이프 서버 스트림이 기습적으로 폐쇄될 때 클라이언트가 EOF를 감지하여 안전하게 세션을 종료함을 검증.

### TC-TRN-107: [NEW] TcpConnectionListener 중단(Stop/Dispose) 시 바인딩 해제 및 재시작
- **우선순위**: P1
- **목표**: `TcpConnectionListener` 인스턴스에 대해 `Stop()` 또는 `DisposeAsync()` 호출 시 대기 중인 `AcceptAsync`가 취소되고, 동일한 IP/Port에 새로운 리스너가 즉시 바인딩(`SocketException: Address already in use` 방지)될 수 있는지 검증.

### TC-TRN-108: [NEW] 미개방 NamedPipe 대상 ConnectAsync 타임아웃 만료 검증
- **우선순위**: P1
- **목표**: 서버가 기동되지 않은 가상의 NamedPipe 이름으로 연결 시도 시, 지정된 `timeoutMs` 이후 프로세스가 무한 블로킹되지 않고 `TimeoutException` 또는 적절한 연결 실패 예외를 방출하는지 검증.

### TC-TRN-109: [NEW] 소켓 송신 버퍼 배압(Backpressure) 및 Flush 취소
- **우선순위**: P2
- **목표**: 원격 수신측이 데이터를 읽지 않아 송신 버퍼가 꽉 찬 상태에서 `FlushAsync` 호출 시 CancellationToken에 의해 깨끗하게 취소되는지 검증.
