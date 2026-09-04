# 📏 Kable - Conventions & Strict Rules

> **Document Status**: Single Source of Truth (SSOT)  
> **Last Updated**: 2026-09-04  
> **Scope**: Applicable to all `Kable` codebase authoring, driver integration, and AI code generation.

---

## 1. Modular Isolation & File Size Limits

1. **300~500 Lines Limit per File**:
   - When a single C# file begins to exceed 300 lines, immediately review modular decomposition (e.g., partial class extraction, sub-service separation).
   - Files exceeding 500 lines will be rejected in PR code reviews.
2. **Strict Prohibition of Code Omission**:
   - Never use placeholder shortcuts such as `// ... existing code ...` or `// todo: rest of logic`. Always produce full, explicit, and self-contained code.
3. **Rigid 3-Tier Layer Responsibilities**:
   - `Transports`: Physical/socket I/O exclusively. No protocol semantics or payload inspection.
   - `Codecs`: Pure byte sequence framing and object serialization. No socket lifecycle management.
   - `Engine`: Interaction dispatch and state routing exclusively. No direct file I/O or disk logging.

---

## 2. Naming Conventions & C# Coding Standards

| Element | Convention | Example |
|---|---|---|
| **Classes, Structs, Records** | PascalCase | `KableSession`, `PacketTraceRecord` |
| **Interfaces** | IPascalCase | `IConnectionContext`, `IProtocolCodec` |
| **Methods** | PascalCase | `RequestAsync`, `TryDecode`, `FormatWireMessage` |
| **Async Methods** | Async suffix | `StartAsync`, `ConnectAsync`, `FlushAsync` |
| **Properties** | PascalCase | `IsConnected`, `SupportsCorrelationId` |
| **Private Fields** | `_camelCase` | `_fifoLock`, `_pendingRequests`, `_context` |
| **Parameters & Local Variables** | camelCase | `timeout`, `urgentMessage`, `buffer` |

### C# 13 / .NET 10 Best Practices
- **Zero-Allocation**: Avoid heap allocations (`new byte[]`). Leverage `ReadOnlySequence<byte>`, `ReadOnlySpan<byte>`, and `IBufferWriter<byte>`.
- **ValueTask Optimization**: Use `ValueTask` on hot paths where synchronous completion is frequent.
- **Modern Language Features**: File-scoped namespaces, pattern matching, primary constructors.

---

## 3. Strict Prohibitions

> [!CAUTION]
> Any code violating these rules will fail continuous integration and PR verification.

1. **Synchronous Blocking Prohibited**:
   - Never call `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`. All I/O must be genuinely asynchronous using `async`/`await` with a propagated `CancellationToken`.
2. **Swallowed Catch Blocks Prohibited**:
   - Never use empty `catch { }` blocks to conceal hardware failures. On physical disconnection, `OnConnectionClosed()` must immediately trigger the fail-fast policy.
3. **External Infrastructure Dependencies in Core Prohibited**:
   - The core `Kable` library must not take dependencies on heavy application frameworks (e.g., Serilog, Entity Framework, or desktop UI libraries).

---

## 4. TDD & Phased Prompt Pipeline

1. **Test-First (Spec-Driven TDD)**:
   - For any new communication feature or bug fix, write unit, edge-case, and fault-injection tests in `tests/Kable.Tests/` or `tests/Kable.Generators.Tests/` first.
2. **Phased Prompt Pipeline (Plan $\rightarrow$ Review $\rightarrow$ Execute)**:
   - **Plan**: Propose changes and target files without compromising existing architecture.
   - **Review**: Await human review and confirmation.
   - **Execute**: Implement incrementally according to the approved plan.
3. **Git Micro-Commits**:
   - Commit atomic units of verified work after successful builds and tests (`feat:`, `fix:`, `docs:`, `test:`).
