# Changelog

All notable changes to this package are documented in this file.

## [0.2.0] - 2026-09-26

### Added

- Record table: `MultiColumnListView` with a column per member, sortable headers (sorting covers every match before the result limit) and highlighted overridden cells.
- Search conditions: space separated terms combined with AND, `Field op Value` with `= != > >= < <= ~`, quoted values, `null`; invalid terms are reported and ignored (`MasterRecordQuery`).
- Tab completion in the search box: field names, and enum / bool values after an operator; common prefix first, then Tab / Shift+Tab cycles; candidates shown under the search box.
- Changes view (header button): every overridden record with its changed fields, Open / Reset, overrides without an original record or table flagged (`MasterMemoryChangeSummary`).
- Read-only foldout tree for arrays, lists, dictionaries and nested objects in the inspector.
- Copy JSON of a record (clipboard; WebGL download) (`MasterDataRecordJson`).
- Import patches into the patch list: Editor file dialog, WebGL browser upload, paste dialog on other platforms (`MasterDataPatchImporter`).
- Apply / Discard / Cancel when leaving a record with unapplied edits; Enter applies, Esc closes the dialog or the debugger.
- Log panel with the latest 50 messages (status, warnings, changes) for devices without a Console (`MasterMemoryDebuggerMessages`).

### Changed

- Package depends on `com.unity.modules.imgui` (system clipboard).

## [0.1.0] - 2026-09-26

Requires Unity 6000.0 or newer and MasterMemory 3.x (NuGetForUnity). Tested on Unity 6000.6.

### Added

- Generic table registry: `RegisterDatabase` (MasterMemory `MetaDatabase`, composite keys supported) and `RegisterTable<TRecord, TKey>`.
- Table groups: `MasterMemoryDebugRegistry.SetTableGroup` shows tables in foldable groups (TreeView).
- Runtime override store and `MasterMemoryDebugRuntime` API (`TryGetOverride`, `Resolve`, `SetOverride`, `RemoveOverride`, `IsOverridden`, `ClearAllOverrides`, `GetOverrides`, `OverridesChanged`).
- Reflection cache with primary / secondary key detection and editable field detection.
- Clone utility (registered clone provider, then shallow member-wise clone).
- UI Toolkit runtime debugger: table list, record list with search / Modified Only filter / ListView virtualization, record inspector, Apply / Revert / Reset Record / Reset All.
- Field-level JSON patch with original values: named patches (Save / Load / Delete from a dropdown), Export (WebGL browser download), master version check with Force Load, optional auto load of the default patch.
- Console log of override changes (field: old → new, colored in the Editor) on Apply / Reset / Reset All / Load Patch; public `MasterDataDiffUtility`.
- Project Settings page, `Tools > MasterMemory Debugger` menu, F8 toggle (Input System and legacy Input Manager).
- Development builds are detected with `Debug.isDebugBuild` (the `DEVELOPMENT_BUILD` define is deprecated in Unity 6.6).
- Basic Example sample with grouped tables, including an optional `ImmutableBuilder` rebuild pattern.
