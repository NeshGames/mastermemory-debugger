# Architecture

## Assembly boundaries

```
Nesh.MasterMemoryDebugger.Core
  Runtime/Core, Override, Reflection, Patch, Settings

Nesh.MasterMemoryDebugger.Remote -> Core
  Runtime/Remote

Nesh.MasterMemoryDebugger.UI -> Core + Remote
  Runtime/UI

Nesh.MasterMemoryDebugger.InputSystem -> UI + Unity.InputSystem
  Runtime/InputSystem
```

The Input System adapter is guarded by a package version define and `ENABLE_INPUT_SYSTEM`; projects without the package do not acquire a hard dependency.

## Runtime composition

`MasterMemoryDebugRuntime` is the public static facade used by game code. Mutable services are owned by `MasterMemoryDebugSession`, which owns the override store and MasterMemory adapter.

Generated MasterMemory assumptions are isolated in `IMasterMemoryAdapter` / `MasterMemoryV3Adapter`: `GetRawDataUnsafe`, `PrimaryKeySelector`, `ToImmutableBuilder`, `Diff`, `RemoveXxx` and `Validate` must not spread into unrelated systems.

## Patch transaction

```
JSON / UI / Remote CLI
        |
MasterDataPatchEngine.Build
        |
MasterDataPatchPlan
        |
MasterDataOverrideStore.ApplyAtomic
```

Local patch loading and remote transactional patching share this engine. Remote adds request identity, SHA/PlanSHA and post-commit state hashes only.

## Remote protocol v6

Welcome contains table metadata, schema hash, overrides and operations, but no table records. A client sends `TableRequest`; the server emits ordered `TableChunk` frames of roughly 1 MiB through bounded connection queues. UI loads a table when first opened. CLI metadata/patch/operation commands do not download all master records.

## Identity

`MasterVersion` is project-supplied and may be `unknown`. `SchemaHash` is deterministically derived from registered table names, record/key types, fields and key declarations. Patches carry both.

## Diagnostics

`MasterMemoryDiagnostics` keeps a small development-only in-memory ring. The Diagnostics tab exposes schema/protocol/override/remote state and recent Patch/Remote timings.
