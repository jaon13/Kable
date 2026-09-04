# 02. Agilent Protocol Specification (애질런트 프로토콜 명세서)

> 본 문서는 Agilent MassHunter ICP-MS의 ExtDevice RS-232C 프로토콜(SAS NvA-MW300 추출 규격)에 대한 송수신 데이터 규격 및 프레임 명세를 정의합니다.

---

## 1. 물리 계층 및 프레임 특성

- **통신 매체**: RS-232C 시리얼 COM 포트 (BaudRate: 9600, DataBits: 8, Parity: None, StopBits: 1)
- **프레임 종료 구분자 (Delimiter)**: CR (`\r`, Hex `0x0D`)
- **특징**:
  - 패킷 내부 시퀀스 번호(Correlation ID) 미지원 $\rightarrow$ 엔진의 선점형 FIFO 락 활성화 대상.
  - 제어 명령 응답(`AperiodicCommand`)과 자발적 결과 통지(`SpontaneousAlarm`), 주기 계측(`PeriodicTelemetry`)이 단일 전송선에서 공존.

---

## 2. 송수신 데이터 명세표 (Protocol Data Specification)

| 구분 | 명령 / 응답 명칭 | 와이어 바이트 (Hex / ASCII) | 방향 | 트래픽 종류 (`TrafficKind`) | 설명 및 페이로드 규격 |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **TX** | **플라즈마 점등** | `6F 50 4F 4E 0D`<br>(`oPON\r`) | LIMS $\rightarrow$ HW | `AperiodicCommand` | RF Generator 전원 인가 및 플라즈마 점등 명령 |
| **TX** | **플라즈마 소등** | `6F 50 4F 46 46 0D`<br>(`oPOFF\r`) | LIMS $\rightarrow$ HW | `AperiodicCommand` | 플라즈마 안전 소등 및 쿨다운 시퀀스 명령 |
| **TX** | **신규 배치 로드** | `6F 42 41 54 43 48 2E ... 0D`<br>(`oBATCH.{ScriptPath}\r`) | LIMS $\rightarrow$ SW | `AperiodicCommand` | MassHunter 시퀀스 템플릿 스크립트 실행 명령 |
| **TX** | **시료 주입 등록** | `6F 41 50 50 45 4E 44 31 30 0D`<br>(`oAPPEND10\r`) | LIMS $\rightarrow$ SW | `AperiodicCommand` | ASAS 오토샘플러 10번 위치 시료 주입 등록 |
| **TX** | **배치 긴급 중단** | `6F 52 45 53 55 4D 45 0D`<br>(`oRESUME\r`) | LIMS $\rightarrow$ SW | `AperiodicCommand` | 진행 중인 시퀀스 즉시 중단 및 원위치 복귀 |
| **TX** | **상태/인터록 폴링** | `71 53 54 41 54 0D`<br>(`qSTAT\r`) | LIMS $\rightarrow$ HW | `PeriodicTelemetry` | 아르곤 가스 압력, 진공도, RF파워 조회 주기 폴링 |
| **RX** | **명령 수락/완료 응답** | `41 43 4B 0D` 또는 `4F 4B 0D`<br>(`ACK\r` / `OK\r`) | HW $\rightarrow$ LIMS | `AperiodicCommand` | 전송한 제어 명령에 대한 하드웨어 응답 |
| **RX** | **측정 완료 CSV 통지** | `24 46 69 6C 65 4E 61 6D 65 2C ... 0D`<br>(`$FileName,{CsvPath}\r`) | SW $\rightarrow$ LIMS | `SpontaneousAlarm` | 계측 완료 후 MassHunter가 자발적으로 쏘는 결과 CSV 경로 |
| **RX** | **상태 텔레메트리 응답** | `23 53 54 41 54 2C 35 35 30 ... 0D`<br>(`#STAT,550,1.2E-3,1500\r`) | HW $\rightarrow$ LIMS | `PeriodicTelemetry` | 압력(kPa), 진공(Pa), RF파워(W) 실측치 데이터 |
