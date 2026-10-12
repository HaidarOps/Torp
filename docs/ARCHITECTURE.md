# Architecture

Torp uses native WinForms on CLR 4 with an x86 target. There are no runtime NuGet dependencies, browser controls, network requests, background services, or proprietary note formats. Source is independently written; Obsidian is the interaction reference, not a source dependency.

## Boundaries

| Component | Responsibility |
| --- | --- |
| `Vault` | Path validation, single-writer lease, strict UTF-8 reads, note CRUD, history and trash |
| `AtomicFile` | Flush a sibling temporary file, then non-overwriting move or atomic replacement; cleanup on failure |
| `EditorSession` | Original revision, editor buffer, dirty state, safe transitions, reload and recovery copies |
| `Markdown` | Bounded native RTF rendering, clickable character ranges, wiki links, tags, word count |
| `VaultIndex` | Immutable snapshots, path/name lookup, precomputed incoming links, search predicates and index warnings |
| `AppSettings` | Small atomic XML preferences file; DTDs and external entities disabled |
| `MainForm` | WinForms presentation, commands, debounced autosave/preview/search, navigation and conflict UI |
| `GraphView` | Native GDI+ graph and note hit testing; keyboard alternative is the quick switcher |
| `Dialogs` / `Theme` | Native filtered pickers, prompts and consistent color palettes |
| `TestRunner` | Dependency-free component and desktop integration tests with isolated fixtures |

The release executable compiles the core and app sources together. The build also compiles a separate core library for the test harness. `Torp.exe` does not require that test library at runtime.

## Persistence protocol

1. Normalize a relative note path; reject rooted paths, dot segments, reserved device names, invalid characters, and reparse-point ancestors.
2. Keep the original SHA-256 revision alongside the editor buffer.
3. On save, compare the on-disk revision. A mismatch leaves the buffer dirty.
4. Write strict UTF-8 to a unique sibling temporary and flush it to disk.
5. Revalidate immediately before `File.Replace`, which retains the exact previous file as a version.
6. Verify the replaced backup's revision too. If another atomic writer won the final interval, expose a conflict while retaining both versions.
7. Read the committed file back before clearing dirty state. Failures remain visible. There is no delete-then-write fallback.

This is optimistic local-file concurrency, not a cross-process compare-and-swap filesystem primitive. Path checks protect normal vault operations; they are not a security boundary against a malicious process racing changes to the filesystem namespace. A same-vault lease prevents two Torp instances from writing concurrently. An interrupted write can leave a `.torp-*.tmp` sibling; it is ignored by discovery, not mistaken for a note.

New notes use a non-overwriting atomic move from a flushed temporary. Trash is a same-vault move, and restore refuses destination collisions. History is keyed by case-insensitive normalized path; rename intentionally does not rewrite other notes or migrate previous path history.

## Threading and lifecycle

The UI owns the editor and all controls. Index builds run on a `BackgroundWorker`, publish a completed immutable snapshot through explicit UI dispatch, and use dictionaries for link resolution and backlinks. A pending-completion flag prevents overlapping generations. On vault changes or shutdown, an in-use vault is retired only after its reader finishes; callbacks ignore closed forms.

The watcher callback only sets an interlocked refresh flag. A UI timer consumes it, refreshes clean buffers, flags dirty conflicts, and schedules indexing. Watcher overflow requests a complete rescan. F5 is the manual recovery path if monitoring is unavailable.

Autosave waits 900 ms after the last edit, preview 300 ms, and search 180 ms. Rendering uses rich text rather than executable HTML. Inline scans are bounded to avoid quadratic behavior on incomplete markup under .NET 4.0, which lacks regex timeouts. Size limits are explained in README and surfaced through index warnings or the preview cutoff notice.

## Compatibility boundary

Source is restricted to C# 4 and intended .NET 4.0 APIs. `FileStream.Flush(bool)`, `BackgroundWorker`, WinForms, GDI+, and CLR 4 are used; there are no async/await or modern .NET runtime APIs. XP certification requires compilation against actual v4.0 reference assemblies and a test on XP SP3. Assembly target metadata alone does not prove API compatibility.

The native control screenshot helper lives only in tests. `DrawToBitmap` does not paint RichEdit content, so tests combine ordinary control drawing with RichEdit's documented native `EM_FORMATRANGE` rendering. These are offscreen component previews, not interactive desktop screenshots.
