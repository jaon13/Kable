# 🧭 Kable - Antigravity Agent Rules

> This file contains the top-level workspace rules automatically recognized and strictly adhered to by Antigravity AI during all `Kable` engineering tasks.

---

## 1. Single Source of Truth (SSOT) Enforcement
- Before writing, designing, or modifying code, always strictly consult and adhere to the following baseline documents:
  - `docs/PROJECT_SPEC.md`: Project mission, 3-tier architecture, and zero-allocation (0-GC) design principles.
  - `docs/SYSTEM_DESIGN.md`: Core interfaces (`IConnectionContext`, `IProtocolCodec`, `IDeviceSession`), and pipeline interaction flows.
  - `docs/CONVENTIONS.md`: Coding standards, modular file limits, prohibition of synchronous blocking, and TDD principles.

## 2. Modular Isolation, File Size Control & Complexity-Driven Decomposition
- A single file **MUST NOT exceed 300~500 lines**.
- **Proactive Decomposition upon Rising Complexity**:
  - Whenever a component's complexity grows (multiple distinct responsibilities, branching protocols, or growing test suites), **proactively split the file and organize into dedicated domain subdirectories** (e.g., `Cases/Transports/`, `Cases/Codecs/`, `Cases/Engine/`).
  - Do not allow a single folder or file to become a catch-all flat dumping ground. Sibling projects in `src/` and `tests/` must maintain 1:1 structural symmetry.
- Code omissions (`// ... existing code ...`, `// todo: rest of logic`, etc.) are strictly prohibited. Always produce complete and explicit code.
- Strict 3-tier boundary isolation: Physical I/O (`Transports`) $\rightarrow$ Framing & Serialization (`Codecs`) $\rightarrow$ Reactive Interaction (`Engine`).

## 3. Spec-Driven & Test-Driven Development (TDD)
- When introducing new codecs, transport adapters, or session capabilities, always write unit and fault-injection tests in `tests/Kable.Tests/` or `tests/Kable.Generators.Tests/` first.
- Implement only the minimal zero-allocation production code required to satisfy the tests.

## 4. Phased Prompt Pipeline
- Avoid monolithic, all-at-once modifications. Adhere strictly to the **Plan $\rightarrow$ Review $\rightarrow$ Execute** pipeline:
  1. **Plan**: Present an implementation plan and the list of target files (under 300 lines each) without compromising existing architecture.
  2. **Review**: Await user feedback and explicit approval.
  3. **Execute**: Incrementally implement and verify only the approved scope.

## 5. Git Micro-Commits
- Create micro-commits (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`) for every verified atomic unit of work after passing builds and tests.
- When an issue or context drift occurs, immediately reset (`git reset`) to the previous clean commit and restart the step cleanly instead of engaging in prolonged debugging dialogues.
