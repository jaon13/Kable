# 02. Agilent Protocol Specification

> This document defines the wire byte format and frame protocol for Agilent MassHunter ICP-MS instruments communicating over ExtDevice RS-232C serial links.

---

## 1. Physical Layer & Framing Attributes

- **Physical Medium**: RS-232C Serial COM Port (BaudRate: 9600, DataBits: 8, Parity: None, StopBits: 1)
- **Frame Delimiter**: Carriage Return (`\r`, Hex `0x0D`)
- **Key Characteristics**:
  - Uncorrelated protocol (no sequence numbers) $\rightarrow$ Requires preemptive FIFO transaction serialization.
  - Aperiodic commands, spontaneous file ingestion events, and cyclic telemetry co-exist on a single shared duplex line.

---

## 2. Protocol Data Specification

| Direction | Command / Response | Wire Bytes (Hex / ASCII) | Traffic Kind (`TrafficKind`) | Payload Description & Invariant |
| :---: | :--- | :--- | :---: | :--- |
| **TX** | **Ignite Plasma** | `6F 50 4F 4E 0D`<br>(`oPON\r`) | `AperiodicCommand` | RF generator power-up and plasma ignition sequence |
| **TX** | **Extinguish Plasma** | `6F 50 4F 46 46 0D`<br>(`oPOFF\r`) | `AperiodicCommand` | Safe plasma shutdown and cool-down procedure |
| **TX** | **Load Batch** | `6F 42 41 54 43 48 2E ... 0D`<br>(`oBATCH.{ScriptPath}\r`) | `AperiodicCommand` | Executes automated MassHunter batch sequence script |
| **TX** | **Append Sample** | `6F 41 50 50 45 4E 44 31 30 0D`<br>(`oAPPEND10\r`) | `AperiodicCommand` | Registers autosampler vial position injection (e.g. #10) |
| **TX** | **Abort Batch** | `6F 52 45 53 55 4D 45 0D`<br>(`oRESUME\r`) | `AperiodicCommand` | Immediate sequence abort and probe park command |
| **TX** | **Poll Status** | `71 53 54 41 54 0D`<br>(`qSTAT\r`) | `PeriodicTelemetry` | Queries argon pressure, chamber vacuum, and RF power |
| **RX** | **Command ACK** | `41 43 4B 0D` or `4F 4B 0D`<br>(`ACK\r` / `OK\r`) | `AperiodicCommand` | Hardware confirmation response to transmitted commands |
| **RX** | **Measurement CSV** | `24 46 69 6C 65 4E 61 6D 65 2C ... 0D`<br>(`$FileName,{CsvPath}\r`) | `SpontaneousAlarm` | Unsolicited result notification containing raw output CSV path |
| **RX** | **Telemetry Data** | `23 53 54 41 54 2C 35 35 30 ... 0D`<br>(`#STAT,550,1.2E-3,1500\r`) | `PeriodicTelemetry` | Measured argon pressure (kPa), vacuum (Pa), and RF power (W) |
