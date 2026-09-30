# CLAUDE.md

Guide for working on this repository. The user-facing documentation is the package README
(`Packages/com.nesh.mastermemory-debugger/README.md`, Traditional Chinese); this file is for whoever changes the code.

## What this is

A Unity 6 project hosting one UPM package, `com.nesh.mastermemory-debugger`: a runtime debugger for Cysharp
MasterMemory v3 with a UI Toolkit UI. It browses tables, overrides record values at runtime (never touching the
MemoryDatabase), saves them as JSON patches, and lets a desktop "remote editor tool" edit a running game build.
Everything runs only in the Editor and Development Builds (`MasterMemoryDebugBuild.IsEnabled`; `MMDEBUGGER_DISABLE`
turns it off).

## Working with the owner

- The owner speaks Traditional Chinese: reply in Traditional Chinese; code, comments, commits, CHANGELOG, CLAUDE.md in English.
- The owner verifies UI and device behavior in Unity (6000.6). Nothing here can run Unity: say what was verified
  (harness) and what still needs a check in Unity (`Tools/Harness/SMOKE_TEST.md`).
- Avoid over-design: small, direct solutions; propose bigger ones with a cost estimate first.
- Release flow: work on the session branch, push, wait for the owner's 「推」, then open a PR. `main` is protected by a
  [ruleset](https://github.com/NeshGames/mastermemory-debugger/rules/24073327): no force pushes or deletion, changes
  through PRs, and `Build and test (outside Unity)` must pass against the latest `main`. The owner checks the Unity
  smoke test; squash merge after that and CI pass (the Release workflow tags and publishes). Do not open or merge PRs
  without the owner's 「推」.

## Layout

```
Packages/com.nesh.mastermemory-debugger/
  Runtime/
    Core/        registry/session, MasterMemory v3 adapter, history, rebuild/validation, queries, diagnostics
    Override/    thread-safe override store and atomic compare-and-swap commits
    Reflection/  reflection cache (no Emit), clone, diff, value formatting / JSON values, custom value converters
    Patch/       shared transactional Patch engine, JSON/storage/compare/export/import
    Remote/      protocol v6, lazy table chunks, TCP/discovery, server/client and bounded replay caches
    UI/          UI Toolkit controllers/assets, Diagnostics tab and remote editor host
    InputSystem/ optional Input System adapter assembly
    Settings/    MasterMemoryDebuggerSettings
  Editor/        settings page, menus, link.xml generator, build processor (copies UI + settings into Development Builds)
  Samples~/BasicExample/   example tables (incl. 50,000 record table), registration, launcher
  Tests/Runtime, Tests/Editor
Tools/Harness/   .NET 8 build + test harness with compile-only Unity stubs (see its README)
.github/workflows/ ci.yml (harness on PRs), release.yml (tag + GitHub Release when package.json version changes)
TODO.md          planned / undecided work
```

## Build and test

```sh
Tools/Harness/run.sh
```

Builds Runtime in six define configurations (none, Editor, Input System, legacy input, WebGL, MMDEBUGGER_DISABLE),
Editor, the sample and the editor tests, then runs `Tests/Runtime` (NUnit, ~150 tests incl. loopback TCP / UDP tests
of remote editing and 50,000 record timing checks). Run it before every commit.

- A new Unity API used by the package needs a stub in `Tools/Harness/Stubs/` with Unity's exact signature (check it
  against UnityCsReference); the build error names the missing member.
- `RuntimeDebuggerTests` (PlayMode) and `Tests/Editor` only compile in the harness; they run in the Unity Test Runner.
- Tests touch static state: `DebuggerTestBase` resets it; anything persisted (PlayerPrefs) has `ResetForTests` /
  `EndTests` so tests never write the developer's prefs.

## Architecture guardrails

- Read `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/INVARIANTS.md` and `docs/TEST_MATRIX.md` for cross-layer work.
- Dependency direction is Core <- Remote <- UI <- optional InputSystem.
- Local and remote Patch application share `MasterDataPatchEngine`.
- MasterMemory generated API reflection belongs in `MasterMemoryV3Adapter`.
- Protocol v6 Welcome stays metadata-only; records are lazy chunk requests.

## Conventions

- Every new file or folder in the package needs a `.meta` with a fresh GUID (Unity packages are immutable; missing
  metas break imports). Copy an existing `.cs.meta` and replace the guid.
- Elements the controllers use must exist in `UI/Layout/MasterMemoryDebugger.uxml` and be listed in
  `MasterMemoryDebuggerController.RequiredElementNames` (checked by `DebuggerAssetTests`).
- Every USS selector is scoped under `.mm-debugger`. Default theme quirks: Labels have a 3px margin (grid cells reset
  it), keyword cursors warn at runtime (reset with `cursor: initial`), widths are border-box.
- Operations that change overrides from the UI are wrapped in `MasterMemoryDebugHistory.Record(label)` (one undo step);
  store writes outside a Record clear the history.
- Reflection only (no `Reflection.Emit`): the game side runs on IL2CPP and WebGL.
- A release: bump `version` in `package.json`, add the `CHANGELOG.md` section, update the `#vX.Y.Z` install URL in both
  READMEs (CI checks all three), update `SMOKE_TEST.md` for UI changes.
- Commit messages: imperative subject, body with the why; end with the attribution lines of the session.

## Things learned the hard way

- `MultiColumnListView` rebuilds on every column change; the record grid is a custom virtualized grid
  (`MasterRecordGrid`) with frozen / scrolled parts.
- Added records are overrides without an original (`MasterMemoryRecordDescriptor.IsAdded`, `Original` is null); deleted
  ones are the `MasterDataOverrideStore.Deleted` marker as the store value. `TryGet` never returns the marker (gameplay
  keeps the original), but `GetEntries` / `EntryChanged` / the history carry it: check `IsDeleted` wherever entry values
  are used.
- `MasterMemoryFieldDescriptor.IsObject` (nested object editing) is evaluated lazily: nested types may hold their own
  type, and evaluating it while the reflection cache builds a descriptor would recurse forever.
- MasterMemory `Validate()` compiles the `Exists()` expressions for every record and scans the referenced table:
  tens of thousands of records take seconds (much longer in Mono). `AutoRebuild` stops validating after every change
  when a validation takes over a second.
- Anything in a `Resources` folder ships in every build, release too. The debugger's UI assets and settings stay out of
  Resources; `MasterMemoryDebuggerBuildProcessor` copies them into `Assets/MasterMemoryDebuggerBuild/Resources` for
  Development Builds only and deletes it afterwards.
- Remote editing: records travel as MessagePack (`MasterMemoryDebugRemote.SerializerOptions` for IL2CPP resolvers;
  the server checks each table at start). Protocol v6 sends metadata in Welcome and records lazily in chunks. JSON transport was evaluated (2026-09-27: ~16× slower to serialize, +37%
  size) and dropped: keep MessagePack. The tool mirrors the game's tables into its own registry / store, so the
  whole UI works unchanged; sockets are closed when Play Mode ends.
- The environment's git proxy refuses tag pushes and branch deletion; the Release workflow creates tags.
