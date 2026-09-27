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
- [ ] `ExampleCharacterMaster` → Growth: the foldout lists the members; edit `CritRate` and `DamageRange.Max`, Enter →
      the grid shows `{HpPerLevel: …}`, `Original:` shown; Reset restores it; Save As… a patch, Reset All, Apply the patch
      → the values come back.
- [ ] **+ New…** on `ExampleSkillMaster`: the key dialog suggests the next Id; an existing Id is refused with the reason;
      the new row shows `+` and opens in the inspector; edit it, Apply; **Duplicate…** copies a record with a new key.
- [ ] **Delete** a skill: `×` in the grid, the inspector is read-only with **Restore**; Changes lists ADDED / DELETED;
      Ctrl+Z undoes the delete and the add; with AutoRebuild, `StartSkillId` of a character pointing at a deleted skill
      shows a NEW validation failure.
- [ ] `→ Table` reference button and **Referenced by → Show** open the related records.
- [ ] **Batch Edit…**: `Damage` × 1.1 on a search; enum field shows a dropdown.
- [ ] Ctrl+Z undoes the batch edit, Ctrl+Y redoes it; Undo / Redo buttons enable and disable.
- [ ] **Copy** and **Labels TSV** paste correctly into a spreadsheet.

## Other tabs

- [ ] **Changes**: Open / Reset; Copy TSV; Paste TSV… preview and apply.
- [ ] **Patches**: Save As…, Apply, Merge, Rename, Delete.
- [ ] **Find**: `1001` → hits in several tables (Skill Id, Character StartSkillId…); Whole value on / off changes the
      hits; Open jumps to the record; `ExampleLargeMaster` included, the time shown stays well under a second;
      Ctrl+Z in the Find box does not undo.
- [ ] **Validation**: set `ExampleCharacterMaster.StartSkillId` to 99 → NEW failure, `Validation (1 new)`, Open jumps to it.

## Remote editing

- [ ] Tools > MasterMemory Debugger > Remote Editing > Create Example Game Scene → Play: the Remote dialog shows the
      addresses, port and pairing code.
- [ ] Build Remote Editor Tool… → run it, connect with the code →
      the tables appear; an edit in the tool changes the game (Console log of the sample) and an edit in the game shows
      in the tool; editing `Growth.CritRate`, adding and deleting a record in the tool reach the game; a wrong code is refused.
- [ ] Tool: fills the window, no Close; the Validation tab shows the game's failures and Open jumps to the record.
- [ ] Tool: "Games on the network" lists the running game; stop and restart the game's Play Mode → the tool reconnects
      by itself (fixed Remote Pairing Code); a wrong code is not retried.

## Builds

- [ ] A Development Build: the debugger opens (F8); `Assets/MasterMemoryDebuggerBuild` is gone after the build.
- [ ] Include Debugger UI off: the build runs the remote server (tool connects), F8 does nothing.
- [ ] A release build: the Editor log's build report lists no MasterMemoryDebugger.uxml / .uss / settings asset.
- [ ] Patches: Compare… with the current overrides and with another patch; Copy TSV.

## Window

- [ ] Language dropdown switches labels; A+ / A- scale without overlapping; a narrow window wraps the header and the
      search toolbar.
- [ ] Esc closes the dialog / popup first, then the debugger; F8 opens it again at the same table.
