# TODO

Planned work that is not scheduled yet. Released changes are in
[CHANGELOG.md](Packages/com.nesh.mastermemory-debugger/CHANGELOG.md).

## Features

- [ ] **Remote editing from the Editor**: browse and override the master data of a Development Build running on a
  device from a window in the Unity Editor (same tables, grid, inspector and patches as the runtime UI).
  - Transport: `PlayerConnection` (Editor ↔ player, already used by the Profiler) or a small TCP / WebSocket server
    in the player; WebGL needs WebSocket.
  - The player sends the table list, record pages and override changes; the Editor sends override / reset / patch
    commands. Values travel as the existing patch JSON.
  - Only in Development Builds, like everything else; the connection must be opt-in.
- [ ] Compare two saved patches, or a patch with the current overrides (Patches tab).

## Verification

- [ ] IL2CPP Development Build (Android / iOS): reflection, `link.xml` template, MasterMemory resolver.
- [ ] Touch toggle gesture and the UI on a real phone / tablet.
