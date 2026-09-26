#!/usr/bin/env bash
# Builds the package in every scripting define configuration and runs the tests outside Unity.
set -euo pipefail
cd "$(dirname "$0")"

configs=(
  ""
  "UNITY_EDITOR"
  "UNITY_EDITOR;MMDEBUGGER_INPUT_SYSTEM;ENABLE_INPUT_SYSTEM"
  "ENABLE_LEGACY_INPUT_MANAGER"
  "UNITY_WEBGL"
  "MMDEBUGGER_DISABLE"
)

for defines in "${configs[@]}"; do
  echo "::group::Build Runtime [${defines:-no defines}]"
  dotnet build Runtime/Runtime.csproj -nologo -v q -p:ExtraDefines="${defines//;/%3B}"
  echo "::endgroup::"
done

echo "::group::Build Editor and Sample"
dotnet build Editor/Editor.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build Sample/Sample.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
dotnet build EditorTests/EditorTests.csproj -nologo -v q -p:ExtraDefines="UNITY_EDITOR"
echo "::endgroup::"

dotnet test Tests/Tests.csproj -nologo -p:ExtraDefines="UNITY_EDITOR" < /dev/null
