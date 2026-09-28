#!/usr/bin/env python3
from __future__ import annotations
import json, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PKG = ROOT / "Packages" / "com.nesh.mastermemory-debugger"
errors = []

def fail(message):
    errors.append(message)

def load_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"{path.relative_to(ROOT)}: invalid JSON: {exc}")
        return {}

tracked_ext = {".cs", ".asmdef", ".uxml", ".uss", ".tss", ".json"}
for path in PKG.rglob("*"):
    if not path.is_file() or path.suffix == ".meta" or path.suffix not in tracked_ext:
        continue
    if not pathlib.Path(str(path) + ".meta").exists():
        fail(f"missing Unity meta: {path.relative_to(ROOT)}")

for folder in ["Runtime/Core","Runtime/Override","Runtime/Reflection","Runtime/Patch","Runtime/Remote","Runtime/UI","Runtime/InputSystem","Runtime/Settings"]:
    path = PKG / folder
    if path.exists() and not pathlib.Path(str(path) + ".meta").exists():
        fail(f"missing Unity folder meta: {path.relative_to(ROOT)}")

core = load_json(PKG / "Runtime/Nesh.MasterMemoryDebugger.Core.asmdef")
remote = load_json(PKG / "Runtime/Remote/Nesh.MasterMemoryDebugger.Remote.asmdef")
ui = load_json(PKG / "Runtime/UI/Nesh.MasterMemoryDebugger.UI.asmdef")
input_asm = load_json(PKG / "Runtime/InputSystem/Nesh.MasterMemoryDebugger.InputSystem.asmdef")

if core.get("name") != "Nesh.MasterMemoryDebugger.Core": fail("Core asmdef name changed")
if core.get("references"): fail("Core asmdef must not reference feature assemblies")
if set(remote.get("references", [])) != {"Nesh.MasterMemoryDebugger.Core"}: fail("Remote must reference Core only")
if set(ui.get("references", [])) != {"Nesh.MasterMemoryDebugger.Core","Nesh.MasterMemoryDebugger.Remote"}: fail("UI must reference Core + Remote only")
if "Unity.InputSystem" not in input_asm.get("references", []): fail("InputSystem adapter must reference Unity.InputSystem")
if "MMDEBUGGER_INPUT_SYSTEM" not in input_asm.get("defineConstraints", []): fail("InputSystem adapter needs package define constraint")

for asmdef in PKG.rglob("*.asmdef"):
    if "Nesh.MasterMemoryDebugger.Runtime" in load_json(asmdef).get("references", []):
        fail(f"legacy Runtime assembly reference: {asmdef.relative_to(ROOT)}")

for cs in PKG.rglob("*.cs"):
    text = cs.read_text(encoding="utf-8")
    rel = cs.relative_to(PKG).as_posix()
    if "UnityEngine.InputSystem" in text and not rel.startswith("Runtime/InputSystem/"):
        fail(f"direct Input System dependency outside adapter: {rel}")
    if rel.startswith("Runtime/Remote/") and ("RuntimeMasterMemoryDebugger" in text or "UnityEngine.UIElements" in text):
        fail(f"Remote depends on UI: {rel}")

patch_service = (PKG/"Runtime/Patch/MasterDataPatchService.cs").read_text(encoding="utf-8")
remote_patch = (PKG/"Runtime/Remote/MasterMemoryRemotePatch.cs").read_text(encoding="utf-8")
if "MasterDataPatchEngine.Build" not in patch_service: fail("local patch apply bypasses MasterDataPatchEngine")
if "MasterDataPatchEngine.Build" not in remote_patch: fail("remote patch apply bypasses MasterDataPatchEngine")

protocol = (PKG/"Runtime/Remote/MasterMemoryRemoteProtocol.cs").read_text(encoding="utf-8")
m = re.search(r"public const int Version\s*=\s*(\d+)", protocol)
if not m or int(m.group(1)) < 6: fail("remote protocol must remain v6+")
m = re.search(r"public const int MaxFrameBytes\s*=\s*(\d+)\s*\*\s*1024\s*\*\s*1024", protocol)
if not m or int(m.group(1)) > 64: fail("remote frame hard limit must be <= 64 MiB")
if "TableRequest" not in protocol or "TableChunk" not in protocol: fail("lazy table protocol messages missing")

connection = (PKG/"Runtime/Remote/MasterMemoryRemoteConnection.cs").read_text(encoding="utf-8")
if "MaxQueuedFrames" not in connection or "BlockingCollection<byte[]>" not in connection:
    fail("remote queues must remain bounded")


# The outside-Unity harness must mirror the same runtime assembly boundaries.
harness_projects = {
    "Core": ROOT / "Tools/Harness/Core/Core.csproj",
    "Remote": ROOT / "Tools/Harness/Remote/Remote.csproj",
    "UI": ROOT / "Tools/Harness/UI/UI.csproj",
    "InputSystem": ROOT / "Tools/Harness/InputSystem/InputSystem.csproj",
    "NoInputSystemConsumer": ROOT / "Tools/Harness/Consumers/NoInputSystem/NoInputSystem.csproj",
    "WithInputSystemConsumer": ROOT / "Tools/Harness/Consumers/WithInputSystem/WithInputSystem.csproj",
}
for name, path in harness_projects.items():
    if not path.exists():
        fail(f"missing harness project: {name} ({path.relative_to(ROOT)})")
if (ROOT / "Tools/Harness/Runtime/Runtime.csproj").exists():
    fail("legacy monolithic Harness Runtime.csproj must not return")

if harness_projects["Core"].exists():
    text = harness_projects["Core"].read_text(encoding="utf-8")
    for forbidden in ["Runtime/Remote", "Runtime/UI", "Runtime/InputSystem"]:
        if forbidden in text:
            fail(f"Core harness project includes feature layer {forbidden}")
if harness_projects["UI"].exists():
    text = harness_projects["UI"].read_text(encoding="utf-8")
    if "../InputSystem/" in text or "InputSystemStubs" in text:
        fail("UI harness project must not depend on InputSystem")
if harness_projects["NoInputSystemConsumer"].exists():
    text = harness_projects["NoInputSystemConsumer"].read_text(encoding="utf-8")
    if "InputSystem" in text:
        fail("no-InputSystem consumer project must not reference InputSystem")

if errors:
    print("Architecture verification failed:")
    for error in errors: print("  -", error)
    sys.exit(1)
print("Architecture verification passed.")
