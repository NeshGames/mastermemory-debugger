# mastermemory-debugger

Unity UI Toolkit runtime debugger for [Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory) v3.

This repository is a Unity 6 project that hosts the UPM package
[`Packages/com.nesh.mastermemory-debugger`](Packages/com.nesh.mastermemory-debugger/README.md).

## Install the package in a game project

1. Install MasterMemory 3.x with [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Package Manager → Add package from git URL:

   ```
   https://github.com/NeshGames/mastermemory-debugger.git?path=/Packages/com.nesh.mastermemory-debugger
   ```

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
