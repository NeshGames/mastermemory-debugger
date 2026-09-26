# TODO

Planned work that is not scheduled yet. Released changes are in
[CHANGELOG.md](Packages/com.nesh.mastermemory-debugger/CHANGELOG.md).

## Features

- [ ] Remote editing of WebGL builds (the game would connect out to the tool over WebSocket).
- [ ] Validation tab in the remote editor tool (run Validate in the game and send the results).
- [ ] Compare two saved patches, or a patch with the current overrides (Patches tab).

## Verification

- [ ] IL2CPP Development Build (Android / iOS): reflection, `link.xml` template, MasterMemory resolver.
- [ ] Touch toggle gesture and the UI on a real phone / tablet.
- [ ] Remote editing: Windows tool ↔ Android (Wi-Fi and adb forward), iOS (local network permission), IL2CPP
      `SerializerOptions`.
