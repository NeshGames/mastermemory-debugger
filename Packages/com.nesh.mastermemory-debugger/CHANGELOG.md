# Changelog

All notable changes to this package are documented in this file.

## [0.4.0] - 2026-09-26

### Added

- Record grid with frozen and hidden columns: frozen columns stay on the left while the others scroll horizontally (scrollbar, Shift + wheel); a Columns popup shows / hides and freezes each column; drag a header edge to resize; settings are kept per table. The state and primary key columns are frozen by default.
- Pinned table tabs above the grid (`+ Pin`, `×` to unpin), saved in PlayerPrefs.
- Header tabs Data / Changes / Patches. The Patches tab lists every saved patch (records, fields, master version, save time), previews its changes and can Apply (replace), Merge, Overwrite, Rename, Export or Delete it; the current overrides can be saved, exported or reset, and patch files imported.
- `MasterMemoryDebugLocalization`: display names and tips per language for tables and fields (API or tab separated text). A header dropdown switches between the code names and each language; used by the table list, tabs, grid headers, inspector and search completion.
- `Font` setting for the debugger UI (fonts with CJK glyphs for localized names).
- `MasterDataPatchStorage.Rename` / `GetSavedTime`.
- Sample: wide `ExampleWeaponMaster` table (27 columns, 300 records) and Chinese labels / tips.

### Changed

- The record grid no longer uses `MultiColumnListView` (it rebuilt the whole list for every column added or removed, which could leave it half built when switching tables).
- The bottom patch toolbar was replaced by the Patches tab.

### Fixed

- `InvalidCastException` when switching tables in the record grid.

## [0.3.1] - 2026-09-26

### Changed

- Database viewer layout: tables | record grid | record detail side by side, with draggable dividers (`TwoPaneSplitView`). The inspector field rows put the name, badges, original value and reference button above a full width editor; Apply / Revert / Reset / Copy JSON moved to the bottom of the detail panel.
- Record grid: dark, readable column headers; a `●` state column; primary key members as their own `(PK)` columns; the `Display` column only when the project supplies display names; numbers right aligned; `NULL` for null values; grid lines, alternating rows and a clearer selection.

## [0.3.0] - 2026-09-26

### Added

- `MasterMemoryDebugRebuild`: `Apply(db)` builds a copy of a generated MemoryDatabase with the overrides through `ToImmutableBuilder().Diff().Build()` (found by reflection); `AutoRebuild(db, apply)` keeps the project's database reference up to date and restores the original on Dispose, so secondary key / range / `All` queries see the overrides.
- Validation after rebuild: MasterMemory `Validate()` (`IValidatable<T>`) runs on the rebuilt database and failures that the original database does not have are reported in the log.
- Reference jump: references declared with `GetReferenceSet<T>().Exists(...)` in `IValidatable<T>.Validate` are discovered automatically (`MasterMemoryReferences`); the inspector shows a `→ Table` button that opens the referenced record.
- Array / List editing: `T[]`, `List<T>` and list interfaces of simple element types get per-element editors with add / remove. Every change creates a new collection (copy-on-write); patches store the whole list as a JSON array.
- Search completion popup: field names, and enum / bool values after an operator; Up / Down select, Tab / Enter / click accept, Esc closes the popup.
- Touch toggle: holding 3 fingers for 1 second toggles the debugger on touch screens (`Touch Toggle Fingers` / `Touch Toggle Seconds`, 0 fingers disables it).
- Development builds preserve `[MemoryTable]` records, `MemoryDatabase` and `ImmutableBuilder` from managed code stripping (`IUnityLinkerProcessor`); the sample ships a `link.xml` for manual setups.

### Changed

- The Basic Example uses `MasterMemoryDebugRebuild.AutoRebuild` (enabled by default) and demonstrates `IValidatable` references; `ExampleDatabaseRebuilder` was removed.
- Tab no longer moves the focus out of the search box.

## [0.2.0] - 2026-09-26

### Added

- Record table: `MultiColumnListView` with a column per member, sortable headers (sorting covers every match before the result limit) and highlighted overridden cells.
- Search conditions: space separated terms combined with AND, `Field op Value` with `= != > >= < <= ~`, quoted values, `null`; invalid terms are reported and ignored (`MasterRecordQuery`).
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
