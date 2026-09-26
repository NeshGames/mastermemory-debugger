# Test harness

Builds the package and runs its tests with the .NET SDK, without Unity. `Stubs/` holds compile-only stand-ins for
the Unity APIs the package uses (signatures only; UI Toolkit behavior is not emulated), and the real MasterMemory
NuGet package (with its source generator) compiles the test tables.

```sh
Tools/Harness/run.sh
```

- Builds `Runtime/` in each scripting define configuration (no defines, Editor, Input System, legacy input,
  WebGL, `MMDEBUGGER_DISABLE`), then `Editor/`, the sample and the editor tests.
- Runs the tests of `Tests/Runtime` (NUnit). `RuntimeDebuggerTests` (PlayMode, needs a panel) and the editor tests
  are only compiled; run them in the Unity Test Runner.

Requires the .NET 8 SDK. CI (`.github/workflows/ci.yml`) runs the same script on every pull request.

When the package starts using a Unity API the stubs do not have, the build fails with a missing member error: add the
member to `Stubs/` with the same signature as Unity's.
