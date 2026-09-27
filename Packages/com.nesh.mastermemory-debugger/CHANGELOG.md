# Changelog

All notable changes to this package are documented in this file.

## [0.13.0] - 2026-09-28

### Added

- Data table queries support `&&`, `||`, and parentheses. AND has higher precedence than OR, adjacent terms remain implicit AND, and quoted values and Flags enum values retain their existing meanings.
- Field and enum/bool value completion works inside grouped queries and immediately after boolean operators.
- The illustrated Pages search guide explains boolean syntax, precedence, grouping, and invalid-query behavior.

### Changed

- Invalid boolean or grouped expressions match no records and show a warning, preventing Batch Edit from using an unexpectedly broad result set. Legacy invalid terms without boolean syntax continue to be ignored.

## [0.12.0] - 2026-09-27

### Added

- Standalone .NET 8 Remote Debugger CLI for inspecting tables and records, reviewing changes and validation, invoking registered operations, and exporting, planning, or applying JSON patches without Unity Editor or game record assemblies.
- Remote protocol v5 patch planning and application with server epoch, master version, patch and plan hashes, per-record state checks, and request IDs for safe retries. Patch application preflights the whole batch and commits overrides atomically; conflicts and validation results are returned to the caller.
- Traditional Chinese and Japanese UI localization for the remote editor, plus an illustrated single-page guide covering search, setup, and everyday workflows.

### Changed

- Remote protocol v5 requires the game and desktop tool to use this package version together.

## [0.11.0] - 2026-09-27

### Added

- Remote protocol v4: a paired tool can list registered game operations, send a request with the target context and candidate revision, and receive a status, message and old/new fingerprints. Requests run on the game's main thread; stale contexts are rejected, and repeated request IDs return the original result. The Remote dialog shows the available operations and their latest result.
- `MasterMemoryDebugRemote.RegisterOperation` returns an owner token; disposing it removes the action. `NotifyOperationsChanged` publishes a new context or revision without replacing the registration.

### Fixed

- The build processor removes its temporary Debugger Resources assets during the build postprocess callback, including batchmode builds that quit before a delayed editor callback can run.

## [0.10.1] - 2026-09-27

### Fixed

- Layout: the search box took the width of its placeholder, so + New… pushed Copy to a second line; it now leaves the room to the buttons. Copy JSON moved to the inspector header, so Apply / Revert / Reset / Duplicate… / Delete fit on one line. The games found by the remote editor tool wrap instead of running past the dialog.

## [0.10.0] - 2026-09-27

### Added

- Find tab: finds a value in every registered table: the current values (overrides included) of every member, list elements, members of nested objects and dictionary keys / values, compared as the shown text (case insensitive; Whole value for exact matches such as IDs). Hits are grouped by table and record; Open jumps to the record. `MasterMemoryGlobalSearch.Find`.
- Inspector: the members of nested objects and structs (a class or struct with writable public members, held by a record) are edited one by one, up to 4 levels; every edit replaces the object with a changed copy. Patches store the value as a JSON object of its editable members and read it into a copy of the original value, so members a patch does not contain keep their values. `MasterMemoryFieldDescriptor.IsObject` / `HasSetter`, `MasterDataValueUtility.IsEditableObject`, `FromJson(json, type, baseValue)`.
- Sample: `ExampleCharacterMaster.Growth` (a nested class with a nested struct).
- Add, duplicate and delete records: **+ New…** (search toolbar), **Duplicate…** and **Delete** (inspector) with a primary key dialog. An added record exists as an override without an original (`TryGetOverride` returns it); a deleted one is a store entry that a rebuilt database leaves out (`RemoveXxx(keys)`), while `TryGetOverride` / `Resolve` keep returning the original: check `MasterMemoryDebugRuntime.IsDeleted` to honor it. Restore / Reset brings a deleted record back; Undo covers all of it. The grid marks added (+) and deleted (×) records; Changes, patches (format version 2: `"added"` / `"deleted"`; patches without them stay version 1), Copy / Paste TSV, Find and remote editing (protocol version 3) include them. `IMasterDataOverrideStore.Delete` / `IsDeleted`, `MasterMemoryRecordFactory`, `MasterDataOverrideEntry.IsDeleted`, `GetDeletedKeys`.

### Changed

- Nested objects are compared member by member (`MasterDataValueUtility.AreEqual`) and shown as `{Member: value, …}` in the grid, Changes, the Console log and TSV, unless the type has its own `ToString`.
- Development Builds also keep the nested object types of the records from managed code stripping.

### Fixed

- Development Builds: the UI assets copied into `Assets/MasterMemoryDebuggerBuild/Resources` got the package's minimal `.meta` files, which Unity 6.6 rejects ("contains a <unknown> object at version 1, below the supported minimum (2)"), so the build showed a broken debugger. Only the files are copied now; Unity imports them with new `.meta` files.
- Development Builds: the debugger had other colors and styles than in the Editor. The copies of the UXML and the USS had the same Resources name, and loading the style sheet could return the UXML's inline style sheet instead of the USS; the copies have distinct names now. A missing style sheet or theme is reported in the log.

## [0.9.0] - 2026-09-27

### Added

- Remote editor tool: the Validation tab shows the game's `Validate()` results. The game pushes whether it validates and the number of failures caused by the overrides (tab title), and validates when the tab asks; Open jumps to the tool's copy of the record. The game needs `AutoRebuild` with validation.
- Patches tab: Compare… a saved patch with the current overrides or another patch: the fields changed to different values or by one side only, with their original values; Copy TSV. `MasterDataPatchCompare.Compare` / `ToTsv`.
- `MasterMemoryDebugValidation.IsPending` / `Invalidate()`.
- Remote editor tool: "Games on the network" lists the game builds waiting for the tool (UDP broadcast on port 7787 answered by the game's remote server: product, device, address, port, master version); clicking one fills the address.
- Remote editor tool: reconnects automatically (every 3 seconds) to the same game after the connection was lost, until it works, the game refuses or Disconnect is pressed. `MasterMemoryDebugRemote.AutoReconnect` / `IsReconnecting`.
- Remote server: checks at start (and when tables are registered) that MessagePack can serialize a record of every table, and explains `SerializerOptions` in the Console when it can not.
- Include Debugger UI setting: Development Builds that only run the remote server leave the UI assets (and the settings' font / PanelSettings) out.
- `CLAUDE.md`: the layout, conventions and release flow of the repository for Claude Code sessions.

### Changed

- Remote editor tool: the debugger fills the whole window and has no Close button (Esc and the toggle key / gesture do not close it).
- Remote protocol version 2 (validation messages): update the game and the tool together.
- The debugger's UI assets (UXML / USS / theme) moved out of the package's Resources folder, and the settings asset is created outside Resources: before, every build (release too) contained them and the font / PanelSettings the settings reference. The Editor loads them directly; a build processor copies them into a temporary `Assets/MasterMemoryDebuggerBuild/Resources` for Development Builds only. The settings page offers to move an existing settings asset out of Resources.

## [0.8.0] - 2026-09-26

### Added

- Remote editing: a desktop build of the project (a scene with the `MasterMemoryRemoteEditor` component, Windows / macOS, or the Editor in Play Mode) connects to a running game build (Development Build: Windows, macOS, Android, iOS) and edits its master data with the full debugger UI. The tool mirrors the game's tables, labels, groups, display names and overrides; override changes are synced both ways (batch edits, Paste TSV, Undo and patches included).
  - Game: `MasterMemoryDebugRemote.StartServer(port, pairingCode)`, the Remote Server settings (auto start, port, fixed pairing code) or the header's Remote → Start. A 6 digit pairing code is required; one tool at a time.
  - Tool: `MasterMemoryDebugRemote.Connect(host, port, code)` or the connect dialog. Disconnecting keeps the last received tables.
  - TCP with length prefixed frames; records as MessagePack (`MasterMemoryDebugRemote.SerializerOptions` for IL2CPP resolvers). Not on WebGL.
- Header: Remote button (status colored) and dialog: addresses, port and pairing code in the game; address, port, code, Connect / Disconnect in the tool.
- Sample: Start Remote Server option of `ExampleDebuggerLauncher`.
- Tools > MasterMemory Debugger > Remote Editing: Create Example Game Scene, Create Remote Editor Tool Scene, and Build Remote Editor Tool… (a windowed Mono Development Build of the tool scene for this desktop, with its own product name; the project settings are restored after the build).

### Changed

- `MasterMemoryDebugRebuild.AutoRebuild`: when one validation takes more than a second, the database is no longer validated after every change (it froze the game on each edit); the Validation tab still validates on demand.
- Sample: `ExampleLargeMaster` has no `IValidatable` any more. MasterMemory's `Validate()` compiles the `Exists()` expressions for every record, so its 50,000 records made every edit of the sample freeze for seconds.

### Fixed

- Remote editing: the sockets are closed when Play Mode ends or scripts reload in the Editor.

## [0.7.0] - 2026-09-26

### Added

- Changes tab: Paste TSV… imports values edited in a spreadsheet (the Copy TSV format: table, key, field, current; columns found by name) as overrides, after a preview of every change and of the lines that can not be imported. Lines whose original value changed since the copy are flagged. One undo step. `MasterMemoryTsvImport.Read` / `Apply`.
- Recent searches: the last 10 searches without errors (Enter, leaving the search box, closing) are listed when the search box is empty, or with Down when it has text. Saved in PlayerPrefs.
- Sample: `ExampleLargeMaster` (50,000 records, Test group) to check large tables.
- `LargeTableTests`: every whole table operation (filter, sort, batch edit, changes, patch, TSV, rebuild, references) on 50,000 records, with the timings printed.
- `Tools/Harness/SMOKE_TEST.md`: the checks to run in Unity before a release, and a pull request template.
- The Release workflow can be run by hand with an existing tag to create its GitHub Release.

## [0.6.0] - 2026-09-26

### Added

- Batch Edit: Set / Add / Multiply one field of every record that matches the search (all matches, not only the shown rows), as overrides. Enum and bool values are picked from a dropdown. Integers are rounded; keys, lists and complex members are excluded; records that end up equal to the original lose their override; failures (null, overflow) are listed in the Console. `MasterMemoryBatchEdit`.
- Undo / Redo (status bar, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, Cmd on macOS) of Apply, Reset, Reset All, patch Apply / Merge and Batch Edit, up to 50 steps. Changes made by game code clear the history. `MasterMemoryDebugHistory.Record` / `Undo` / `Redo`.
- Changes tab: Copy TSV of every changed field (table, key, name, field, original, current) to copy tuned values back into the master data source. `MasterMemoryChangeSummary.ToTsv`.

### Changed

- The search toolbar wraps its buttons below the search box when the panel is narrow.
- README: the Open / Close section documents `IsAvailable`, `OpenStateChanged` and how to turn off the built-in hotkey and touch gesture.

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
