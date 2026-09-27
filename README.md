# mastermemory-debugger

Unity UI Toolkit runtime debugger for [Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory) v3.

This repository is a Unity 6 project that hosts the UPM package
[`Packages/com.nesh.mastermemory-debugger`](Packages/com.nesh.mastermemory-debugger/README.md).
Everything runs only in the Editor and in Development Builds; release builds contain none of the debugger's assets.

## Features

- **Browse** every MasterMemory table: database viewer layout, frozen / hidden / resizable columns, sorting, pinned
  tabs, condition search with completion and recent searches, localized table / field names with tips, and Find:
  a value in every table (lists and nested objects included).
- **Override** values at runtime without touching the MemoryDatabase: inspector editing (lists and the members of
  nested objects / structs included), add / duplicate / delete records, batch edit
  (set / add / multiply over a search), undo / redo, relation jump and "Referenced by" from `IValidatable` `Exists()`.
- **Rebuild** the gameplay database with the overrides (`MasterMemoryDebugRebuild.AutoRebuild`) and see MasterMemory
  `Validate()` failures caused by them in the Validation tab.
- **Patches**: save, apply, merge, compare, import / export the changed fields as JSON; copy the changes as TSV for a
  spreadsheet and paste edited values back.
- **Remote editing**: a desktop build of the project (the remote editor tool) finds a running game build on the
  network, connects with a pairing code and edits its master data with the same UI; changes sync both ways.
  The game can run the remote server without the debugger UI.

## Install the package in a game project

1. Install MasterMemory 3.x with [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Package Manager → Add package from git URL:

   ```
   https://github.com/NeshGames/mastermemory-debugger.git?path=/Packages/com.nesh.mastermemory-debugger#v0.10.1
   ```

   `#v0.10.1` pins a release (see [Releases](https://github.com/NeshGames/mastermemory-debugger/releases)); without it you get the latest `main`.

See the [package README](Packages/com.nesh.mastermemory-debugger/README.md) for usage.

## Develop the package in this repository

1. Open the repository root with Unity 6000.0 or newer.
2. NuGetForUnity restores MasterMemory and its dependencies from `Assets/packages.config`
   (menu `NuGet > Restore Packages` if the Console reports missing `MasterMemory` / `MessagePack`).
3. Player Settings > Active Input Handling: `Input System Package (New)` or `Both`
   (the debugger supports both backends).
4. Window > Package Manager > MasterMemory Runtime Debugger > Samples > import **Basic Example**,
   add `ExampleDebuggerLauncher` to a GameObject, enter Play Mode and press **F8**.
5. Window > General > Test Runner runs the package tests (Edit Mode and Play Mode).
6. Remote editing: Tools > MasterMemory Debugger > Remote Editing > Create Example Game Scene (Play), then
   Build Remote Editor Tool… and run it; it lists the game under "Games on the network".

Working with Claude Code: [CLAUDE.md](CLAUDE.md) describes the layout, the conventions and the release flow.

## Tests without Unity and CI

`Tools/Harness/run.sh` builds the package with the .NET 8 SDK in every scripting define configuration (against
compile-only Unity stubs) and runs the tests that do not need a UI panel. See [Tools/Harness](Tools/Harness/README.md).

- **CI** (`.github/workflows/ci.yml`) runs it on every pull request and push to `main`, and checks that `CHANGELOG.md`
  has a section for the version in `package.json`.
- **Release** (`.github/workflows/release.yml`): when a push to `main` changes the version in `package.json`, the
  `vX.Y.Z` tag and a GitHub Release with that version's CHANGELOG section are created automatically.

Releasing is therefore: bump `version` in `package.json`, add the CHANGELOG section (and update the `#vX.Y.Z` in the
READMEs), run the [smoke test](Tools/Harness/SMOKE_TEST.md) in Unity, merge to `main`.

To create the GitHub Release of an older tag, run the Release workflow by hand (Actions → Release → Run workflow) with
the tag, for example `v0.4.0`; the notes come from that version's CHANGELOG section.
