# TODO

Active staged work is tracked in [docs/WORK_PLAN.md](docs/WORK_PLAN.md).

Released changes are in [CHANGELOG.md](Packages/com.nesh.mastermemory-debugger/CHANGELOG.md).

## Immediate next step

- [x] Make the outside-Unity harness compile the new Core / Remote / UI / optional InputSystem assembly topology separately.
- [x] Get the hardening branch's outside-Unity CI fully green.
- [x] Verify new package files/folders have Unity `.meta` files and document the intentional assembly migration.
- [x] Integrate local v0.15.0 converters with architecture hardening; full harness passes (239 runtime + 7 CLI tests).
- [x] Unity 6000.6 asmdef import/compile and automated tests (7 EditMode + 244 PlayMode).
- [ ] Complete the visual/build smoke checklist in `Tools/Harness/SMOKE_TEST.md`.
- [x] Push the combined integration branch and update Draft PR #19.
- [ ] Review Draft PR #19 and complete the manual smoke checks before merging/releasing; remote CI must pass on its current head.
- [x] Run the required pull-request CI against the latest `main` (Draft PR #19).

## Device verification

- [ ] IL2CPP Development Build (Android / iOS): reflection, `link.xml` template, MasterMemory resolver.
- [ ] Touch toggle gesture and the UI on a real phone / tablet.
- [ ] Remote editing: Windows tool ↔ Android (Wi-Fi and adb forward), iOS (local network permission), IL2CPP
      `SerializerOptions`.

## Explicit non-goals

- Remote editing of WebGL builds.
- Remote record transport as JSON (dropped 2026-09-27: ~16× slower to serialize, +37% size).
