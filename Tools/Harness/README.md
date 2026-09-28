# Test harness

Builds the package and runs its tests with the .NET SDK, without Unity. The harness mirrors the Unity asmdef topology
instead of compiling all Runtime code into one assembly:

```
Core <- Remote <- UI <- InputSystem (optional)
```

`Stubs/` contains compile-only UnityEngine/UI Toolkit APIs. `InputSystemStubs/` is a separate fake
`Unity.InputSystem` assembly so the no-InputSystem consumer test genuinely compiles without that dependency.
The real MasterMemory NuGet package (with its source generator) compiles the test tables.

```sh
Tools/Harness/run.sh
```

The script:

- Builds Core / Remote / UI separately under no defines, Editor, legacy input, WebGL and `MMDEBUGGER_DISABLE`.
- Builds the optional InputSystem adapter only with `MMDEBUGGER_INPUT_SYSTEM;ENABLE_INPUT_SYSTEM`.
- Compiles a consumer with no Input System assembly/reference.
- Compiles a second consumer with the Input System adapter enabled.
- Builds Editor, the Basic Example sample, editor tests and the Remote CLI.
- Runs `Tests/Runtime` (NUnit). `RuntimeDebuggerTests` and editor tests are compile-only here; run them in Unity.

Requires the .NET 8 SDK. CI runs the same script on every pull request.

When the package starts using a Unity API the stubs do not have, the build fails with a missing member error. Add the
member with Unity's exact signature. Input System API additions belong in `InputSystemStubs/`, not `Stubs/`.
