# Architecture Hardening Work Plan

This is the active checklist for the architecture-hardening branch. Work should be completed in functional batches:
finish the whole item, verify it, then make one commit.

## Completed baseline

- [x] Transactional shared Patch engine for local and remote apply.
- [x] Remote Patch O(P×N) table-scan fix.
- [x] Deterministic SchemaHash in patches and remote handshake.
- [x] MasterMemoryDebugSession composition root.
- [x] MasterMemoryV3Adapter for generated MasterMemory API assumptions.
- [x] Explicit MasterMemoryReferences.Register API.
- [x] Remote protocol v6: metadata-only Welcome + lazy TableRequest/TableChunk transport.
- [x] Bounded remote frame size, incoming/outgoing queues and chunk backpressure.
- [x] Bounded remote operation/Patch replay caches.
- [x] Core / Remote / UI / optional InputSystem assembly boundaries.
- [x] Optional Input System adapter without a hard dependency from Core/UI.
- [x] Diagnostics ring and Diagnostics UI tab.
- [x] AGENTS/architecture/invariants/test-matrix documentation and CI architecture checker.
- [x] Release workflow reads release notes from the requested historical tag.

Clean baseline commits:

1. `a9328ab5` — Harden patch transactions and remote protocol v6
2. `79fcb629` — Split runtime into core, remote, UI, and optional input assemblies
3. `3098d3c5` — Add runtime diagnostics and observability
4. `837f3174` — Bound remote replay caches without restart limits
5. `00bd7cfb` — Add architecture guardrails for AI-maintained changes

---

## P0 — Verification blockers before PR

### 1. Make the outside-Unity harness mirror the asmdef split

Current risk: `Tools/Harness/Runtime/Runtime.csproj` still compiles `Runtime/**/*.cs` as one assembly, so it can miss
Core/Remote/UI dependency mistakes that Unity asmdefs would catch.

- [x] Split the harness so Core, Remote and UI compile as separate projects/assemblies.
- [x] Compile the optional InputSystem adapter only in the Input System configuration.
- [x] Add a consumer compile smoke test with **no Input System package/reference**.
- [x] Add a consumer compile smoke test with Input System enabled.
- [x] Keep WebGL / legacy input / disable-symbol configurations.
- [x] Ensure Editor, Samples and Tests reference the same logical assembly topology as Unity.

Implementation is complete on the branch. The runtime execution checks below remain part of P0.2 because this chat
environment cannot execute the repository's full .NET/Unity toolchain directly.

**Definition of done**

- `Tools/Harness/run.sh` passes with the split topology.
- A no-InputSystem consumer compiles without resolving `Unity.InputSystem`.
- Architecture checker and harness agree on dependency direction.

### 2. Final CI / consistency pass

- [x] Run `python3 Tools/verify-architecture.py` (GitHub CI).
- [x] Run the full harness through GitHub Actions on the hardening branch.
- [x] Fix every compile/test failure caused by the architecture rewrite.
- [x] Review the final diff for accidental API or serialization compatibility breaks.
  - Protocol v6 intentionally makes Welcome metadata-only; table records **and override snapshots** are lazy/chunked.
  - The Core / Remote / UI assembly split is an intentional migration from the 0.14.x `Nesh.MasterMemoryDebugger.Runtime` assembly and is documented in the package README / architecture guide.
- [x] Verify all newly added package files/folders have valid `.meta` files.
- [ ] Run the required PR CI against the latest `main`.
- [ ] Confirm package import/asmdef resolution in Unity 6000.6.

**Definition of done**

- Hardening-branch GitHub CI is green at `de887ff6` (architecture verification, version consistency, all outside-Unity builds/tests).
- Unity opens the project with no asmdef/compiler errors.
- Existing public API behavior remains compatible unless intentionally documented; the assembly split is the documented exception.

---

## P1 — Maintainability refactors

### 3. Modularize Remote CLI

Current state: `Tools/RemoteCli/Program.cs` still owns argument parsing, transport, records, validation, operations and
Patch commands.

Target structure:

```
Tools/RemoteCli/
  Program.cs
  CliOptions.cs
  CliConnection.cs
  CliQueryCommands.cs
  CliOperationCommands.cs
  CliPatchCommands.cs
  CliOutput.cs
```

- [x] Keep `Program.cs` as entry point/orchestration only.
- [x] Move option parsing and usage validation into `CliOptions`.
- [x] Move connection/Welcome/frame waiting into `CliConnection`.
- [x] Move records/changes/tables/validate into query commands.
- [x] Move operation invocation into operation commands.
- [x] Move patch-export/plan/apply into Patch commands.
- [x] Preserve JSON `schemaVersion = 1`, exit codes and command-line flags.
- [x] Add focused CLI tests for option validation and protocol-response routing.

Implementation and focused test coverage are complete. Execution of the full CLI/Harness suite remains part of the
P0.2 validation pass.

**Definition of done**

- Existing CLI commands and JSON contracts are unchanged.
- No command handler needs to know unrelated command options.
- Full harness/CLI build passes.

### 4. Reduce MasterMemoryDebuggerController responsibilities

Keep the controller as the UI composition root, but move unrelated behavior into focused controllers.

Suggested extraction:

- [x] Header/status/remote-state presentation → `MasterDebuggerHeaderController`.
- [x] Tab/navigation visibility/state → `MasterDebuggerNavigationController`.
- [x] Root keyboard shortcuts → `MasterDebuggerShortcutController`.
- [x] Keep edit-guard and cross-feature orchestration at the root.
- [x] Preserve the existing UXML contract and session restore behavior.
- [x] Do **not** replace `MasterRecordGrid` with `MultiColumnListView`.

The responsibility split is implemented. Compilation and the Unity UI smoke test remain part of P0.2.

**Definition of done**

- Root controller is primarily composition + cross-controller coordination.
- No user-facing behavior changes.
- `DebuggerAssetTests` and UI smoke test pass.

---

## P2 — Test depth and protocol hardening

### 5. Add protocol golden / malformed-input tests

- [x] Golden round-trip fixtures for protocol v6 message types.
- [x] Invalid counts/lengths and truncated payloads.
- [x] Out-of-order/duplicate TableChunk behavior.
- [x] Duplicate request id with different Patch fingerprint.
- [x] Queue/backpressure saturation behavior.
- [x] Handshake timeout for clients that connect but never send Hello.
- [x] Table-transfer cancellation/retry when connection changes.

Implementation and regression coverage are complete; execution remains part of P0.2.

**Definition of done**

- Malformed peers fail predictably without large allocations or unbounded queues.
- Protocol behavior is deterministic and covered independently of UI.

### 6. Tighten code-quality gates

Introduce these only after the rewritten codebase is warning-clean.

- [ ] Enable warnings-as-errors in harness projects.
- [ ] Add `dotnet format --verify-no-changes` or equivalent formatting check.
- [ ] Add test coverage reporting for Core/Patch/Remote.
- [ ] Add property/fuzz-style tests for Patch JSON, query parsing and protocol framing where practical.

**Definition of done**

- CI blocks new warnings/format drift.
- Coverage artifacts are available without making coverage percentage an arbitrary release gate.

---

## P2 — Product/debugger features

### 7. Patch Dry Run / Impact Preview

The shared Patch engine already produces a preflight plan; expose it to users before applying/importing a Patch.

- [x] Show changed/added/deleted target counts.
- [x] Show warnings/errors before any mutation.
- [x] Show master-version/schema mismatch clearly.
- [x] Use the same `MasterDataPatchEngine` plan as the actual commit.
- [x] Remote CLI `patch-plan` already provides the non-mutating target/error plan used for equivalent dry-run inspection.

Apply/Merge now always presents the shared-engine impact preview before commit. Automated execution remains part of P0.2.

**Definition of done**

- A user can inspect the exact Patch impact before committing.
- Preview and commit cannot drift into separate business-rule implementations.

### 8. Saved Views

Persist development-only table browsing state:

- [x] Table.
- [x] Query.
- [x] Sort.
- [x] Visible/frozen columns and user-sized widths.
- [x] Modified-only toggle.
- [x] Named save/load/delete UI.
- [x] Keep storage development/debug-only and version-tolerant.

Saved Views implementation is complete, including schema-tolerant column/sort restore and remote lazy-table reapply.
Execution/UI smoke validation remains part of P0.2.

**Definition of done**

- A saved view restores the same browsing state after reopening the debugger.
- Invalid/missing fields after schema changes degrade gracefully.

### 9. Optional master-data fingerprint

SchemaHash describes structure, not the exact original data contents.

- [x] Decide whether a data fingerprint is needed in addition to project MasterVersion.
- [x] Keep fingerprinting optional/project-owned; do not force an expensive full-table hash.
- [x] Decision: do not add an automatic fingerprint now. Use meaningful MasterVersion + SchemaHash + field preconditions; only revisit with a project-supplied cached identity provider.

**Definition of done**

- The feature has measurable value over MasterVersion + SchemaHash and does not add large startup cost.

---

## Decision gates

### 10. Decide whether Add / Duplicate / Delete records should remain

These features create cross-cutting complexity in override markers, rebuild, Patch format, Remote, validation, history and UI.

- [x] Review current released behavior and the post-hardening implementation cost.
- [x] Decision: retain Add/Duplicate/Delete; do not remove an already released capability during architecture cleanup.
- [x] Keep them as first-class Patch operations covered by the shared engine and tests.
- [x] Document the decision in `docs/ARCHITECTURE.md`.

**Do not extend record-creation behavior before this decision.**

### 11. Decide the scope of Remote Operations

- [x] Retain the operation framework as a narrow development command surface.
- [x] Explicitly forbid growing it into a second general-purpose RPC framework.
- [x] Document request/replay/context/revision semantics in `docs/ARCHITECTURE.md`.

---

## Real Unity / device verification

These cannot be proven by the outside-Unity harness.

- [ ] Unity 6000.6 package import and Editor compilation.
- [ ] Development Build contains debugger UI/settings; non-Development Build does not.
- [ ] Android IL2CPP: reflection/link.xml/MessagePack generated resolver.
- [ ] iOS IL2CPP: reflection/link.xml/MessagePack generated resolver.
- [ ] Touch toggle and debugger UI on a real phone/tablet.
- [ ] Windows remote tool ↔ Android over Wi-Fi.
- [ ] Windows remote tool ↔ Android with `adb forward`.
- [ ] Desktop remote tool ↔ iOS with local-network permission.
- [ ] Large-table remote load under realistic device memory/network conditions.

---

## Explicit non-goals

- Remote editing on WebGL.
- JSON transport for remote records; MessagePack remains the selected transport.
- Replacing the custom `MasterRecordGrid` with `MultiColumnListView`.
- Adding a DI framework solely for this package; constructor injection/composition root is sufficient.

## Recommended execution order

```
P0.1 Harness mirrors asmdefs
  -> P0.2 CI + Unity consistency pass
  -> P1.3 Remote CLI modularization
  -> P1.4 Root UI controller split
  -> P2.5 Protocol hardening tests
  -> P2.6 Quality gates
  -> P2.7 Patch Dry Run
  -> P2.8 Saved Views
  -> Decision 10/11
  -> Real device verification
```

Each arrow should normally end in one complete functional commit.
