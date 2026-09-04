# ICP-MS Multi-Vendor Integration Case Study (INDEX)

> This directory indexes the reference architectural case study for semiconductor ultra-pure chemical analysis instruments (**ICP-MS: Inductively Coupled Plasma Mass Spectrometry**), detailing multi-vendor expansion strategies and production integrations for **Agilent MassHunter vs. PerkinElmer Syngistix** using `Kable`.

---

## 🎯 Core Integration Principles

1. **Unified Domain Abstraction**:
   - The controlling host couples exclusively to a vendor-neutral `IIcpmsDriver` domain contract (`IgnitePlasmaAsync`, `StartBatchAsync`, `AbortBatchAsync`).
2. **Modular Project Isolation**:
   - `src/Icpms.MassHunter`: Agilent 7900/8900 (RS-232C line framing, FIFO transaction serialization).
   - `src/Icpms.PerkinElmer`: PerkinElmer NexION (TCP/NamedPipe RPC, parallel interleaving).
3. **Unified Communication Conduit (`Kable`)**:
   - Controlled via identical `KableSession` runtime instances simply by injecting transport factories and protocol codecs.

---

## 📚 Document Index

### 1. [01. Multi-Vendor Strategy](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/01_MULTI_VENDOR_STRATEGY.md)
- 3-step standard expansion procedure for new instruments (1. Verify contract $\rightarrow$ 2. Isolate module $\rightarrow$ 3. Register DI factory).
- Agilent MassHunter vs. PerkinElmer Syngistix architectural comparison table.

### 2. [02. Agilent Protocol Spec](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/02_AGILENT_PROTOCOL_SPEC.md)
- Agilent 7900/8900 ExtDevice RS-232C wire byte format and command catalogue.
- Carriage return (`\r`) delimiter framing and `TrafficKind` mapping.

### 3. [03. Agilent Architecture & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/03_AGILENT_ARCHITECTURE_DESIGN.md)
- 3-tier class diagrams, data flow models, and FIFO serialization timing charts.

### 4. [04. Agilent Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/04_AGILENT_IMPLEMENTATION_CODE.md)
- Complete production code for `MassHunterDeviceDriver`, protocol codecs, and DI extensions.

### 5. [05. PerkinElmer Syngistix Spec & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/05_PERKINELMER_SYNGISTIX.md)
- Reverse-engineered RPC contracts and correlation sequence diagrams.

### 6. [06. PerkinElmer Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/06_PERKINELMER_IMPLEMENTATION.md)
- Driver implementation, RPC client proxies, and DI extensions.
