# Changelog

All notable changes to this package are documented in this file.

## [Unreleased]

### Added

- Table groups: `MasterMemoryDebugRegistry.SetTableGroup` shows tables in foldable groups (TreeView).
- Named patches: save under a name, pick saved patches from a dropdown to load or delete.

### Changed

- Setting `Patch File Name` became `Default Patch Name` (files are `<name>.patch.json`).
- Field editors apply values while typing; Apply Override could previously miss the last edit.
- Development builds are detected with `Debug.isDebugBuild` (the `DEVELOPMENT_BUILD` define is deprecated in Unity 6.6).

### Fixed

- Dropdown menus (EnumField / DropdownField) used the light default theme with unreadable text.

## [0.1.0] - 2026-09-26

### Added

- Generic table registry: `RegisterDatabase` (MasterMemory `MetaDatabase`, composite keys supported) and `RegisterTable<TRecord, TKey>`.
- Runtime override store and `MasterMemoryDebugRuntime` API (`TryGetOverride`, `Resolve`, `SetOverride`, `RemoveOverride`, `IsOverridden`, `ClearAllOverrides`, `GetOverrides`, `OverridesChanged`).
- Reflection cache with primary / secondary key detection and editable field detection.
- Clone utility (registered clone provider, then shallow member-wise clone).
- UI Toolkit runtime debugger: table list, record list with search / Modified Only filter / ListView virtualization, record inspector, Apply / Revert / Reset Record / Reset All.
- Field-level JSON patch with original values: Save / Load / Export (WebGL browser download), master version check with Force Load, optional auto load.
- Project Settings page, `Tools > MasterMemory Debugger` menu, F8 toggle (Input System and legacy Input Manager).
- Basic Example sample, including an optional `ImmutableBuilder` rebuild pattern.
