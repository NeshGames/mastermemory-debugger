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

This split intentionally changes the public assembly layout from the 0.14.x single `Nesh.MasterMemoryDebugger.Runtime` assembly. The old Runtime asmdef GUID is retained by `Core`, so GUID-based Core references survive the rename. Name-based references to `Nesh.MasterMemoryDebugger.Runtime` must migrate, and consumers that use Remote/UI APIs must explicitly reference those assemblies because Unity custom-assembly references are not transitive. This is an intentional hardening migration and must be called out in the user-facing README/release notes.

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

`MasterDataPatchService` is the public create/preview/apply facade. The engine must not depend back on that facade: shared master-version, table and primary-key resolution lives in the internal `MasterDataPatchResolver`. This keeps the dependency direction one-way (`Service -> Engine -> Resolver`) and prevents a second mutation implementation from accumulating in the facade.

## Remote protocol v6

Welcome contains table metadata, schema hash and operations, but **no table records or override payloads**. A client sends `TableRequest`; the server emits ordered `TableChunk` frames of roughly 1 MiB through bounded connection queues. The chunks contain the selected table's original records plus its override snapshot. Live `Changes` received before that table finishes loading are buffered and replayed after the snapshot so the newest state wins. UI loads a table when first opened. Validation failures can trigger loading of the affected table so Open can resolve the actual record. CLI metadata/patch/operation commands do not download all master records.

## Identity

`MasterVersion` is project-supplied and may be `unknown`. `SchemaHash` is deterministically derived from registered table names, record/key types, fields and key declarations. Patches carry both.

## Diagnostics

`MasterMemoryDiagnostics` keeps a small development-only in-memory ring. The Diagnostics tab exposes schema/protocol/override/remote state and recent Patch/Remote timings.


## Architecture decisions after hardening

### Master-data identity

Do not compute a mandatory full-database fingerprint inside the package. Projects should provide a meaningful
`MasterVersion` when exact data revision identity matters. The package always supplies `SchemaHash`, and changed
Patch fields carry original-value preconditions. A future fingerprint is only justified as an explicit project-supplied,
cached identity provider; it must not introduce an automatic O(total master data) startup/UI cost.

### Add / Duplicate / Delete records

Retain Add, Duplicate and Delete as supported debugger operations. They are already part of the released Patch format
and are now first-class operations in the shared `MasterDataPatchEngine`, with rebuild, history, validation and remote
tests. Do not add a second creation/deletion path or expand the feature into runtime schema editing. Any future removal
is a deliberate breaking release, not incidental cleanup.

### Remote Operations

Retain Remote Operations as a narrow development command surface for game-specific actions that cannot be expressed as
master-data edits. It is not a general RPC layer.

The contract remains:

- The game advertises `Id`, display label, opaque context and revision.
- The tool must echo the advertised context/revision with a unique request id.
- Context/revision changes make old requests stale.
- Recent request results are replayed from the bounded cache for idempotency.
- Operations return status/message plus optional old/new state hashes.
- New transport concepts, arbitrary object graphs, streaming calls or gameplay networking belong outside this package.
