# 02. Transport Layer Test Specification

> **Target Components**: `Kable.Transports`, `Kable.Transport.Ipc`, `Kable.Host`  
> **Interfaces**: `IConnectionContext`, `IConnectionFactory`, `IConnectionListener`  
> **Related Design**: [SYSTEM_DESIGN.md (Section 1)](file:///d:/Johnny/Kable/docs/SYSTEM_DESIGN.md)

---

## 1. 개요 및 계층의 역할

Transport 계층은 하드웨어와의 물리적 I/O 바이트 스트림을 `System.IO.Pipelines` (`PipeReader Input` / `PipeWriter Output`)으로 추상화하는 최하단 계층입니다.  
OS 소켓, Windows 명명된 파이프(NamedPipe), 시리얼 포트(RS-232C) 간의 인터페이스 통일성과 케이블 단선, 포트 점유 충돌, 데몬 크래시 등 물리 장애에 대한 방어력을 책임집니다.

---

## 2. 현재 구현된 테스트 현황 (Existing Tests)

| 테스트 ID | 테스트 메서드명 | 검증 내용 |
| :--- | :--- | :--- |
| `TC_TRN_01` | `TcpTransport_LoopbackClientAndServer_TransmitsAndReceivesBytesCorrectly` | TCP 루프백 연결 및 양방향 기본 송수신 |
| `TC_TRN_02` | `NamedPipeTransport_LoopbackClientAndServer_TransmitsAndReceivesCorrectly` | 단일 프로세스 내 NamedPipe 클라이언트/서버 송수신 |
| `TC_TRN_03` | `TcpConnectionListener_AcceptsClientAndTransfersData` | `TcpConnectionListener`의 Accept 및 에코 검증 |
| `TC_TRN_04` | `TC_TRN_107_TcpConnectionListener_Stop_ReleasesSocketAndAllowsPortReuse` | Listener 중단 후 동일 포트 즉시 재바인딩 가능 여부 |
| `TC_TRN_05` | `TC_TRN_05_SerialPortContext_DisposeAsync_ClosesBaseStreamAndCancelsToken` | SerialPort 컨텍스트 해제 시 CancellationToken 취소 여부 |
| `TC_TRN_101` | `TC_TRN_101_Tcp_ForceResetRstPacket_AbortsConnectionContextInstantly` | TCP Hard RST(Linger 0) 패킷 수신 시 즉시 세션 중단 |
| `TC_TRN_102` | `TC_TRN_102_NamedPipe_ServerProcessAbruptTermination_DetectsPipeBroken` | NamedPipe 서버 크래시 시 파이프 파손 즉각 감지 |
| `TC_TRN_103` | `TC_TRN_103_SerialPort_PhysicalCableRemoval_HandlesBaseStreamDisposed` | USB-시리얼 케이블 강제 단선 시 Abort 및 재진입 안전성 |
| `TC_TRN_106` | `TC_TRN_106_Tcp_ConnectionTimeoutToNonRoutableIp_ThrowsCleanTimeoutException` | 미수신 포트 접속 시 깨끗한 소켓 예외 발생 |
| `TC_TRN_108` | `TC_TRN_108_NamedPipe_NonExistentServer_ConnectAsyncTimesOutCleanly` | 존재하지 않는 파이프 접속 시 TimeoutException 발생 |
| `TC_TRN_IPC_01` | `IpcNamedPipe_ClientServerRoundTrip_TransfersDataSeamlessly` | `Kable.Transport.Ipc` 라이브러리의 파이프 송수신 |
| `TC_TRN_IPC_02` | `KableSession_OverIpcNamedPipe_CompletesRequestAsyncSuccessfully` | IPC NamedPipe 상에서의 KableSession 정상 구동 |

---

## 3. 신규 보강 필요 테스트 케이스 명세 (Required New Test Cases)

### 📌 TC_TRN_201: SerialPortFactory_NonExistentPort_ThrowsArgumentOrIOException
- **목적**: 장비에 존재하지 않는 시리얼 포트를 지정하여 연결 시도 시, 라이브러리가 무한 블로킹되지 않고 즉시 적절한 `IOException` 또는 OS 오류를 방출하는지 검증.
- **포트 하드코딩 배제 원칙 (Dynamic Port Discovery)**:
  - `"COM99"`와 같이 특정 번호를 하드코딩하는 방식은 **테스트 실행 환경(가상 COM 포트 드라이버, 다른 OS 환경 등)에 따라 이미 할당되어 있을 수 있어 취약한 안티패턴(Fragile Test)**입니다.
  - 따라서 테스트 실행 시 `SerialPort.GetPortNames()`를 통해 **현재 호스트에 등록된 실제 포트 목록을 동적으로 스캔**한 후, 해당 목록에 절대 존재하지 않는 고유 GUID/해시 기반 식별자(`$"NON_EXISTENT_PORT_{Guid.NewGuid():N}"`) 또는 유효 포트 목록 밖의 미사용 인덱스를 안전하게 계산하여 주입합니다.
- **실행 단계 (Dynamic Execution)**:
  ```csharp
  // 1. 현재 호스트의 실제 포트 목록 동적 조회
  var existingPorts = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
  
  // 2. 현재 시스템에 절대 존재하지 않는 동적 가상 포트명 도출
  string nonExistentPort = $"COM_UNASSIGNED_{Guid.NewGuid():N}";
  while (existingPorts.Contains(nonExistentPort))
  {
      nonExistentPort = $"COM_UNASSIGNED_{Guid.NewGuid():N}";
  }

  // 3. 팩토리 연결 시도 및 페일패스트 검증
  var factory = new SerialPortConnectionFactory(nonExistentPort);
  Func<Task> act = async () => await factory.ConnectAsync();
  
  // 4. 무한 블로킹 없이 플랫폼 표준 I/O 예외 즉시 방출 확인
  await act.Should().ThrowAsync<IOException>();
  ```
- **기대 결과**:
  - `IOException` 계열 예외 즉시 발생.
  - 스레드 블로킹이나 OS 핸들 누수가 발생하지 않음.

### 📌 TC_TRN_202: IpcDaemon_ClientProcessCrash_HostContinuesWithoutCrashing
- **목적**: 프로세스 분리 아키텍처(`Kable.Host`)에서 다중 클라이언트 중 특정 클라이언트 프로세스가 예기치 않게 비정상 종료(Crash/Kill)되어 파이프가 끊겼을 때, 호스트 데몬의 청취 루프가 다운되지 않고 다른 정상 세션 및 신규 접속을 지속 수락하는지 검증.
- **격리 원칙 (Dynamic Pipe Isolation)**:
  - 고정된 파이프명(`"Kable_Daemon"`) 대신, 병렬 테스트 충돌을 방지하기 위해 GUID 기반 고유 파이프명(`$"Kable_Daemon_Test_{Guid.NewGuid():N}"`)을 동적으로 생성하여 사용.
- **실행 단계**:
  1. 동적 파이프명으로 `IpcNamedPipeServerListener`를 실행.
  2. 클라이언트 1, 클라이언트 2 동시 접속.
  3. 클라이언트 1의 스트림을 예고 없이 강제 `Dispose` 또는 하드 셧다운.
  4. 클라이언트 2가 정상적으로 메시지를 송수신할 수 있는지 확인.
  5. 신규 클라이언트 3이 성공적으로 접속되는지 확인.
- **기대 결과**:
  - 클라이언트 1 세션 리소스가 깨끗하게 정리됨.
  - 호스트 리스너 및 다른 클라이언트에 전파 에러 없음.

### 📌 TC_TRN_203: TcpConnection_LargeWriteBackpressure_CancellationDuringFlush
- **목적**: 수신단이 소켓 윈도우를 닫아 데이터 전송(`FlushAsync`)이 지연될 때, 호출자가 전달한 `CancellationToken`이 트리거되면 즉시 블로킹을 해제하고 취소 예외를 발생하는지 검증.
- **포트 할당 원칙 (Dynamic Port 0 Binding)**:
  - 고정 포트 번호(예: `5000`, `8080` 등) 하드코딩을 엄격히 금지하고, `new TcpListener(IPAddress.Loopback, 0)`을 통해 OS가 가용한 임의의 빈 포트(Ephemeral Port)를 동적으로 할당받아 테스트 실행.
- **기대 결과**:
  - `OperationCanceledException` 발생 및 소켓 잠김 현상 없음.
