# Architecture Invariants

1. Never mutate a record owned by MasterMemory; edit clones and store overrides.
2. Add/change/delete Patch semantics live in `MasterDataPatchEngine`.
3. Failed Patch preflight changes nothing; `replaceExisting` removal is part of the same atomic transaction.
4. Atomic changes carry expected state; concurrent writes become conflicts.
5. MasterMemory generated API naming/reflection conventions belong in `MasterMemoryV3Adapter`.
6. Dependency direction is Core <- Remote <- UI <- optional InputSystem.
7. Protocol v6 Welcome remains metadata-only.
8. Frames, incoming/outgoing queues, pending table requests and replay caches are bounded.
9. Recent operation/patch request ids are replayable; old cache entries are evicted instead of requiring restart.
10. Protocol changes require round-trip, loopback and large-table tests.
11. UI controller element names must exist in UXML.
12. Debugger UI/settings assets must not ship in non-Development builds.
13. Runtime reflection must remain IL2CPP-safe; no runtime code generation.
14. Use `MasterMemoryReferences.Register` when automatic `IValidatable.Exists` discovery is not stable enough.
15. New package source/assets/folders require Unity `.meta` files.
