# TODO

Planned work that is not scheduled yet. Released changes are in
[CHANGELOG.md](Packages/com.nesh.mastermemory-debugger/CHANGELOG.md).

## Features

Not planned: remote editing of WebGL builds; remote record transport as JSON instead of MessagePack (dropped
2026-09-27: ~16× slower to serialize, +37% size).

## Verification

- [ ] IL2CPP Development Build (Android / iOS): reflection, `link.xml` template, MasterMemory resolver.
- [ ] Touch toggle gesture and the UI on a real phone / tablet.
- [ ] Remote editing: Windows tool ↔ Android (Wi-Fi and adb forward), iOS (local network permission), IL2CPP
      `SerializerOptions`.
