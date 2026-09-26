# Smoke test (Unity)

The harness does not run UI Toolkit, so layout and interaction are checked by hand before a release: about 5 minutes
in Play Mode with the **Basic Example** sample (`ExampleDebuggerLauncher`, F8). Run the Unity Test Runner (Edit Mode
and Play Mode) as well.

## Data tab

- [ ] Switch between several tables (list and pinned tabs); no error in the Console.
- [ ] `ExampleManyColumnsMaster`: scroll horizontally (scrollbar, Shift + wheel); headers stay aligned with the cells;
      frozen columns stay on the left; Columns ▾ hides / freezes a column.
- [ ] `ExampleLargeMaster` (50,000 records): opening the table, scrolling, searching `Value>900` and sorting stay responsive.
- [ ] Search: type `da` → completion popup; ↑ / ↓ / Tab. Empty the box → recent searches are listed.
- [ ] Edit a value in the inspector, Enter → ● in the grid, orange cell, `Original:` shown.
- [ ] `→ Table` reference button and **Referenced by → Show** open the related records.
- [ ] **Batch Edit…**: `Damage` × 1.1 on a search; enum field shows a dropdown.
- [ ] Ctrl+Z undoes the batch edit, Ctrl+Y redoes it; Undo / Redo buttons enable and disable.
- [ ] **Copy** and **Labels TSV** paste correctly into a spreadsheet.

## Other tabs

- [ ] **Changes**: Open / Reset; Copy TSV; Paste TSV… preview and apply.
- [ ] **Patches**: Save As…, Apply, Merge, Rename, Delete.
- [ ] **Validation**: set `ExampleCharacterMaster.StartSkillId` to 99 → NEW failure, `Validation (1 new)`, Open jumps to it.

## Window

- [ ] Language dropdown switches labels; A+ / A- scale without overlapping; a narrow window wraps the header and the
      search toolbar.
- [ ] Esc closes the dialog / popup first, then the debugger; F8 opens it again at the same table.
