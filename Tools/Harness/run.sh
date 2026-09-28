#!/usr/bin/env bash
# Builds the package against the same assembly boundaries as the Unity asmdefs, then runs tests outside Unity.
set -euo pipefail
cd "$(dirname "$0")"

configs=(
  ""
  "UNITY_EDITOR"
  "ENABLE_LEGACY_INPUT_MANAGER"
  "UNITY_WEBGL"
  "MMDEBUGGER_DISABLE"
)

for defines in "${configs[@]}"; do
  encoded="${defines//;/%3B}"
  echo "::group::Build Core / Remote / UI [${defines:-no defines}]"
  dotnet build Core/Core.csproj -nologo -v q -p:ExtraDefines="$encoded"
  dotnet build Remote/Remote.csproj -nologo -v q -p:ExtraDefines="$encoded"
  dotnet build UI/UI.csproj -nologo -v q -p:ExtraDefines="$encoded"
  echo "::endgroup::"
done

input_defines="UNITY_EDITOR;MMDEBUGGER_INPUT_SYSTEM;ENABLE_INPUT_SYSTEM"
echo "::group::Build optional Input System adapter"
dotnet build InputSystem/InputSystem.csproj -nologo -v q -p:ExtraDefines="${input_defines//;/%3B}"
echo "::endgroup::"

echo "::group::Build consumer dependency smoke tests"
dotnet build Consumers/NoInputSystem/NoInputSystem.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build Consumers/WithInputSystem/WithInputSystem.csproj -nologo -v q -p:ExtraDefines="${input_defines//;/%3B}"
echo "::endgroup::"

echo "::group::Build Editor, Sample, Editor tests and Remote CLI"
dotnet build Editor/Editor.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build Sample/Sample.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build EditorTests/EditorTests.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build ../RemoteCli/RemoteCli.csproj -nologo -v q
echo "::endgroup::"

dotnet test Tests/Tests.csproj -nologo -p:ExtraDefines="UNITY_EDITOR" < /dev/null
dotnet test ../RemoteCli.Tests/RemoteCli.Tests.csproj -nologo < /dev/null
