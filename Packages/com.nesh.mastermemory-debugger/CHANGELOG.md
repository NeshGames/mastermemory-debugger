# Changelog

All notable changes to this package are documented in this file.

## [0.6.0] - 2026-09-26

### Added

- Batch Edit: Set / Add / Multiply one field of every record that matches the search (all matches, not only the shown rows), as overrides. Integers are rounded; keys, lists and complex members are excluded; records that end up equal to the original lose their override; failures (null, overflow) are listed in the Console. `MasterMemoryBatchEdit`.
- Undo / Redo (status bar, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, Cmd on macOS) of Apply, Reset, Reset All, patch Apply / Merge and Batch Edit, up to 50 steps. Changes made by game code clear the history. `MasterMemoryDebugHistory.Record` / `Undo` / `Redo`.
- Changes tab: Copy TSV of every changed field (table, key, name, field, original, current) to copy tuned values back into the master data source. `MasterMemoryChangeSummary.ToTsv`.

### Changed

- The search toolbar wraps its buttons below the search box when the panel is narrow.

## [0.5.0] - 2026-09-26

### Added

- Validation tab: every failure of MasterMemory `Validate()` on the database rebuilt by `MasterMemoryDebugRebuild.AutoRebuild`, the ones caused by the overrides first (NEW, `Validation (N new)` in the tab title). Open jumps to the failing record; New only filter. `MasterMemoryDebugValidation.Run()` / `NewFailureCount`.
- Referenced by: the inspector lists the members of other tables that reference the record (from the IValidatable `Exists()` references) with the number of records, and Show opens them. `MasterMemoryReferences.GetIncoming` / `FindReferencing` / `GetReferencedValue`.
- Copy: the shown rows and columns of the record grid as tab separated text for spreadsheets (WebGL: `.tsv` download).
- Labels TSV: a label template of every table and field (with the labels already set for the selected language) for `MasterMemoryDebugLocalization.LoadTsv`. `MasterMemoryDebugLocalization.CreateTsvTemplate`.
- `Tools/Harness`: builds the package and runs the tests with the .NET SDK, without Unity. CI runs it on every pull request; pushing a new `package.json` version to `main` creates the tag and the GitHub Release.

### Changed

- Column visibility, freezing and dragged widths are saved in PlayerPrefs per table and survive restarts (they were kept for the play session only).
- `LoadTsv` skips lines without a label and a tip (unfilled template lines).
- `MasterDataPatchExporter.CopyToClipboard` takes an optional MIME type for the WebGL download.

## [0.4.0] - 2026-09-26

### Added

- Record grid with frozen and hidden columns: frozen columns stay on the left while the others scroll horizontally (scrollbar, Shift + wheel); a Columns popup shows / hides and freezes each column; drag a header edge to resize; settings are kept per table. The state and primary key columns are frozen by default.
- Pinned table tabs above the grid (`+ Pin`, `×` to unpin), saved in PlayerPrefs.
- Header tabs Data / Changes / Patches. The Patches tab lists every saved patch (records, fields, master version, save time), previews its changes and can Apply (replace), Merge, Overwrite, Rename, Export or Delete it; the current overrides can be saved, exported or reset, and patch files imported.
- `MasterMemoryDebugLocalization`: display names and tips per language for tables and fields (API or tab separated text). A header dropdown switches between the code names and each language; used by the table list, tabs, grid headers, inspector and search completion.
- `Font` setting for the debugger UI (fonts with CJK glyphs for localized names).
- `MasterDataPatchStorage.Rename` / `GetSavedTime`.
- Automatic column widths from the titles and the values of the first 200 rows; double click a header edge to go back to the automatic width.
- The state and primary key columns are always shown and frozen.
- Inspector text fields wrap and grow to show long values.
- Sample: `ExampleManyColumnsMaster` (75 columns, 120 records).
- Sample: wide `ExampleWeaponMaster` table (27 columns, 300 records) and Chinese labels / tips.

### Changed

- The record grid no longer uses `MultiColumnListView` (it rebuilt the whole list for every column added or removed, which could leave it half built when switching tables).
- The bottom patch toolbar was replaced by the Patches tab.

### Fixed

- `InvalidCastException` when switching tables in the record grid.
- Grid cells were 6px wider than their headers (default label margins), so the columns drifted to the right and frozen values were cut; frozen columns also lost 2px to the divider and rows could be offset by the default list item padding.
- "Runtime cursors other than the default cursor need to be defined using a texture" warnings: the default theme's resize / text cursors are reset for the debugger UI.
- Header items overlapped when the UI was scaled up: the header wraps and the version text is truncated; the language dropdown is wide enough for its choices; the saved patch list sizes to the panel and truncates long lines.

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
