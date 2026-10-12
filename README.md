# Torp

A native Windows notebook for connected ideas. Torp stores ordinary UTF-8 Markdown files in a folder you control and runs offline without an account.

## Run

Open **`dist/Torp-1.0.0/Torp.exe`**, or extract `dist/Torp-1.0.0-windows-x86.zip` and open `Torp.exe`. Keep the accompanying configuration file beside it. No installation, administrator rights, browser, or third-party runtime packages are required. The executable contains the application and its core code.

**Prerequisite:** Microsoft .NET Framework 4.0 or a compatible 4.x runtime. The intended minimum OS is Windows XP SP3 (x86). Microsoft's [.NET Framework 4 installer](https://www.microsoft.com/en-us/download/details.aspx?id=17718) supports XP SP3. On modern Windows, .NET Framework 4.8 runs this application.

**Build qualification:** the supplied local build passed the component suite on the available modern Windows runtime. It was compiled with C# 4 syntax against installed 4.x libraries. Actual XP execution and strict .NET 4.0 reference-assembly compilation remain unverified; this is a local evaluation build, not a certified XP production release. See `docs/REVIEW.md`.

On first launch Torp creates `Documents\Torp Notes` with three starter notes. **Open vault** chooses an existing Markdown folder. An empty vault receives the starter notes. Preferences are stored in `%APPDATA%\Torp\settings.xml`.

To keep a run entirely within a chosen folder:

```powershell
.\dist\Torp-1.0.0\Torp.exe --vault "C:\Notes\My vault" --settings "C:\Notes\preferences.xml"
```

## Included

- Folder explorer, note creation, rename/move, full-text search, and quick switcher.
- Markdown editor beside a native reading view; autosave after 900 ms and explicit Save.
- `[[wiki links]]`, aliases, heading targets, backlinks, outgoing links, tags, and clickable graph.
- Daily notes, command palette, navigation history, light/dark themes, and adjustable editor size.
- Atomic saves, previous versions, recoverable trash, external-change detection, and conflict recovery by saving a copy.
- Export to Markdown; no proprietary note database.

Reading view supports headings, bold/italic/strike, inline code, fenced code, lists, tasks, quotations, horizontal rules, and safe external links. This is a documented Markdown subset rather than a full CommonMark implementation. HTML stays literal; images, tables, embeds, plugins, cloud synchronization, and Obsidian extension compatibility are outside this version's scope. Tables remain readable as source text. Task boxes in reading view are display-only; edit their Markdown to toggle them.

## Use

| Action | Shortcut |
| --- | --- |
| New note | Ctrl+N |
| Quick switcher | Ctrl+O |
| Command palette | Ctrl+P |
| Save / save a copy | Ctrl+S / Ctrl+Shift+S |
| Daily note | Ctrl+D |
| Insert wiki link | Ctrl+K |
| Find in note / search vault | Ctrl+F / Ctrl+Shift+F |
| Reading view / graph | Ctrl+E / Ctrl+G |
| Light/dark theme | Ctrl+L |
| Back / forward | Alt+Left / Alt+Right |
| Rename or move | F2 |
| Refresh external changes | F5 |
| Help | F1 |

Name a note `Projects/Idea` to create it in a folder. Search accepts plain words, `"quoted phrases"`, `tag:ideas`, `#ideas`, `path:Projects`, and `-exclude`. Terms combine with AND. Parent tags also match their descendants.

Links support `[[Note]]`, `[[Folder/Note|label]]`, and `[[Note#Heading]]`. Matching tries a sibling path, a vault-root path, then a unique filename. Ambiguous filenames do not silently choose a note. `../` paths are deliberately unsupported. Click links in reading view or double-click sidebar entries; sidebar lists also support Enter.

Renaming does not rewrite incoming links or move earlier history. The rename dialog explains this. Use explicit paths for duplicate titles and update links after moving notes.

## Your files

Notes must be UTF-8 (a UTF-8 BOM is accepted). Saves use UTF-8 without a BOM and editor line endings may be normalized. Binary NUL, invalid UTF-8, and notes larger than 2 MiB are refused to avoid corruption. Unreadable or oversized notes are reported by the index warning indicator.

Existing notes are replaced atomically after revision checks. Torp retains the previous file in `.torp/history/<path-hash>/`. File → Version history previews and restores older versions. Deletion moves a note to `.torp/trash/`; restoring never overwrites an existing note. History and trash are not automatically purged. Back up the **whole vault**, including `.torp`, to another device.

If a note changes outside Torp while you edit it, your buffer stays open and saving stops. Choose **Save a copy** to keep both versions or **Reload disk version** to explicitly discard local edits. A failed save prevents closing or switching away with unsaved changes. A process crash can lose edits made since the last successful autosave; version history is not a substitute for independent backups.

Use a local disk with atomic file-replacement support. Only one Torp window may open a given vault. Other applications may still edit notes; revision checks and preserved replacement backups detect conflicts, including a concurrent replacement in the final save interval. Reparse points and hidden dot folders are excluded. Individual note paths are limited to 210 characters for legacy Windows APIs; deep vault paths can also hit Windows limits in metadata storage.

Reading view displays up to 200,000 characters per note. The index is limited to 10,000 notes / 24 million text characters and reports truncation. The graph shows the first 120 indexed notes. Use a smaller vault if these limits are reached.

## Build and test

No package restore is needed for a local build. Use Windows PowerShell 5.1 and the C# compiler installed with .NET Framework 4.x:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

Outputs are in `artifacts/bin` and `dist`. Tests use only isolated fixtures under `artifacts/tests` and produce `artifacts/test-results.txt` plus dark/light/graph previews. The test runner returns a nonzero exit code on failure. Build warnings are errors.

The smoke script launches the actual packaged executable with `--smoke-test`, explicit test vault/settings paths, and a hidden window. That diagnostic runs the normal UI event loop, verifies controls and indexing, then closes normally and reports success through its process exit code.

For strict XP API qualification, install .NET Framework 4.0 reference assemblies or set `TORP_NET40_REFS` to their directory, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -StrictNet40
```

This mode fails when the targeting pack is absent; it never quietly uses newer libraries. The CI workflow obtains Microsoft's pinned .NET 4.0 reference-assembly package for this gate. Tests run on a modern Windows runner; the separate XP acceptance gate in `docs/TESTING.md` is still necessary. Building requires modern PowerShell; the resulting app does not require PowerShell on XP.

Development guidance: `AGENTS.md`, `skills/torp-development/SKILL.md`, and `docs/ARCHITECTURE.md`. The original brief is preserved in `ProjectPlan.md`.

## Repository delivery

The authorized destination is [HaidarOps/Torp](https://github.com/HaidarOps/Torp). Git was not available in this environment and outbound tool connections were denied, so no repository initialization, commit, or push is claimed. The source and review report are ready for a Git-enabled environment. Inspect the remote's history before importing this project; preserve existing commits and never force-push over them.
