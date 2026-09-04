# 🤝 Contributing to Kable

Thank you for contributing to **Kable (High-Performance Reactive Hardware Communication Engine for .NET)**!

---

## 1. Development Guidelines

1. **Target Frameworks**:
   - Multi-targeting: `net10.0`, `net8.0`, and `netstandard2.0`.
   - Ensure all public APIs and implementations remain fully compatible across all targets.
2. **Zero-Allocation & Performance (0-GC)**:
   - Always prefer `System.IO.Pipelines`, `ReadOnlySequence<byte>`, and `ArrayPool<byte>.Shared` over heap allocations.
   - Streaming ingestion must not trigger Gen2 GC collections.
3. **Fail-Fast Safety Policy**:
   - Hardware communication faults must immediately dispatch `DeviceDisconnectedException`.
   - Never implement silent swallowed catches on critical I/O paths.
4. **Test-Driven Development (TDD & Spec-Driven)**:
   - **Test-First**: For any new protocol codec, transport connection, or session router feature, add test cases in `tests/Kable.Tests/` covering happy path, timeout edge cases, and fault injection.
   - Run tests before pushing:
     ```bash
     dotnet test
     ```
5. **Code Size & Modular Isolation**:
   - Keep files strictly within **300~500 lines** of code. Avoid monolithic classes.
   - Never omit code (`// ... existing code ...` or similar shortcuts are strictly prohibited).
   - Follow the **Plan $\rightarrow$ Review $\rightarrow$ Execute** pipeline and micro-commit frequently.

---

## 2. Git Commit Convention

We adhere to the Conventional Commits specification:
* `feat:` New feature or protocol codec
* `fix:` Bug fix or connection recovery fix
* `docs:` Documentation updates
* `refactor:` Code refactoring without behavioral changes
* `test:` Adding or updating unit/fault-injection tests
* `perf:` Performance and zero-allocation optimizations
