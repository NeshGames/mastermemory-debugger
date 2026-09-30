# AGENTS.md

Repository instructions for Codex and other coding agents.

## Architecture

Dependency direction is enforced by Unity asmdefs:

```
Core <- Remote <- UI <- InputSystem (optional adapter)
   ^       ^       ^
   +-------+-------+-- Editor / Samples / Tests
```

- Core owns registry, session, overrides, reflection, MasterMemory v3 adapter, patches, validation and diagnostics.
- Remote owns protocol/network/discovery/remote operations only. It must not depend on UI.
- UI owns UI Toolkit controllers/assets and the remote editor host.
- InputSystem is optional. Direct `UnityEngine.InputSystem` usage belongs only in `Runtime/InputSystem`.
- Editor may depend on Core, Remote and UI.

Read `docs/ARCHITECTURE.md`, `docs/INVARIANTS.md` and `docs/TEST_MATRIX.md` before cross-layer changes.

## Non-negotiable invariants

- Patch mutation semantics live in `MasterDataPatchEngine`. Local and remote apply paths must not implement separate mutation rules.
- Patch application is preflighted before writes and committed with `MasterDataOverrideStore.ApplyAtomic`.
- MasterMemory generated API assumptions belong in `MasterMemoryV3Adapter`.
- Remote protocol v6 starts with metadata only. Table records are lazy, chunked and backpressured.
- Remote queues and replay caches are bounded.
- Release builds must not include debugger UI/settings assets.
- No `Reflection.Emit` / `DynamicMethod`; IL2CPP and WebGL must remain supported where documented.
- Every Unity package source/asset file and new package folder needs a `.meta`.
- Do not replace `MasterRecordGrid` with `MultiColumnListView`.

## Verification

Before committing code:

```bash
Tools/verify-change.sh
```

At minimum:

```bash
python3 Tools/verify-architecture.py
```

For UI/build changes also follow `Tools/Harness/SMOKE_TEST.md` in Unity.

## Change scope

Prefer one commit per complete functional item. Do not produce one commit per file or mechanical sub-step.
