# Test Matrix

| Change area | Automated checks | Unity/manual checks |
| --- | --- | --- |
| Core / Override / Reflection | architecture checker + full harness | Unity API changes only |
| Patch engine / JSON | PatchTests + randomized round-trip robustness + full harness | patch UI smoke when flow changes |
| MasterMemory adapter / rebuild | Registry/Rebuild/Reference + full harness | IL2CPP Development Build for reflection/linking changes |
| Remote protocol/network | RemoteTests + randomized frame robustness + LargeTableTests + full harness | desktop tool ↔ target device for platform socket changes |
| UI Toolkit | DebuggerAssetTests + full harness | `SMOKE_TEST.md` |
| Input adapter | all harness define configurations | project with and without Input System |
| Build/linker processor | Editor tests + full harness | Development/non-Development inclusion check |
| Docs/workflow only | architecture checker + changed-file whitespace check | none |

CI uploads Cobertura coverage artifacts for Core/Patch/Remote and CLI execution paths; coverage is reported for visibility, not used as a percentage gate.

Real-device verification still required for Android/iOS IL2CPP, touch UI, and Windows tool ↔ Android/iOS connectivity.
