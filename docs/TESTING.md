# Verification

Run `scripts/build.ps1 -Test`. The runner performs storage tests in fresh workspace-local vaults and desktop tests on a single STA thread. It enables illegal cross-thread control-access checks, returns nonzero on any failure, and retains fixtures for inspection. No test opens a real user vault.

## Automated component coverage

| Component | Observable checks |
| --- | --- |
| Paths / vault | Traversal, reserved names, device paths, junctions, Unicode, UTF-8 BOM, malformed/binary input, size limits |
| Writes | Round-trip, duplicate creation, no-op save, prior versions, external modifications/deletions, stale revisions, locked/read-only files, temporary cleanup |
| Lifecycle | Second-writer rejection, disposal, reopen, rename/move, safe trash and collision-safe restore |
| Editor session | Dirty tracking, save-before-navigation, conflict retention, failed open/copy, recovery copy, explicit reload |
| Markdown | Formatting, lists/tasks, fences, Unicode RTF, link ranges, scheme allowlist, literal HTML/RTF payloads, incomplete-markup performance |
| Search / links | Phrases, AND/exclusion, path/nested tags, sibling/root/unique/ambiguous resolution, aliases/anchors, backlink deduplication |
| Scale | Build/search/backlink lookup for 10,000 connected notes with a bounded execution time |
| Settings | Atomic round-trip, defaults, font bounds, malformed XML and external entity rejection |
| Dialogs | Prompt accept/cancel and filtered picker selection |
| Desktop | First run, indexing, real editor/preview controls, graph and themes, minimum-size layout, timed autosave, navigation persistence, conflict close prevention, external edits, unreadable initial notes |

`scripts/smoke.ps1` independently launches the packaged executable with explicit workspace-local vault/settings paths and `--smoke-test`. This diagnostic runs the normal `Application.Run` loop, waits for the real editor/preview controls and completed index, checks for startup alerts, then requests normal form closure. The script checks exit status, starter notes, and preferences. It never relies on the test runner's alternative entrypoint or cross-process access to a desktop. The diagnostic requires both explicit paths to prevent accidentally testing against the default vault.

The screenshot artifacts use native offscreen control rendering because the build sandbox does not provide desktop capture. Inspect `torp-dark.png`, `torp-light.png`, and `torp-graph.png` under the latest `artifacts/tests/run-*` directory. The component suite does not replace manual keyboard, mouse, accessibility, or OS acceptance testing.

## XP release gate — outstanding

1. Build with `scripts/package.ps1 -StrictNet40` using actual .NET 4.0 reference assemblies. Retain output and compiler identity.
2. Use a clean Windows XP SP3 x86 VM with .NET Framework 4.0, normal user permissions, and no development tools installed.
3. Copy the package in, disconnect networking, launch `Torp.exe`, and confirm the initial editor and preview at 1024×768 and 120 DPI.
4. Create nested and Unicode notes; type, undo/redo, paste plain text, save, close/reopen, and verify the underlying files with a separate text editor.
5. Exercise every menu command, quick switcher, palette, preview link, backlink, tag filter, graph node, daily note, find, navigation, and font/theme switch.
6. Edit the same note externally while Torp has unsaved changes. Confirm the external file survives and both reload and save-copy recovery work. Repeat with deletion, a read-only file, and a full test volume.
7. Restore versions and trash entries; verify collisions never overwrite existing notes. Verify independent backups include `.torp`.
8. Terminate the process during repeated saves to a disposable vault. Validate that the prior or new complete file survives and inspect any temporary siblings. Power-failure durability also depends on the storage device and filesystem.
9. Exercise a 10,000-note test vault, large permitted notes, long-path failures, permissions errors, watcher overflow, and removal of an external vault drive.
10. Record VM build, runtime version, filesystem, screenshot evidence, failures, and sign-off before calling the build production-ready for XP.

## CI

`.github/workflows/build.yml` builds and packages on Windows with the pinned Microsoft v4.0 reference-assembly package, runs the suite, and retains results. This workflow has been authored but has not run remotely in the current environment. Its successful execution still would not substitute for the XP VM gate.
