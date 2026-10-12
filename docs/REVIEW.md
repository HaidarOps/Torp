# Implementation review — 2026-10-07

## Result

A working local evaluation build is available in `dist/Torp-1.0.0/Torp.exe` and the companion ZIP. The executable contains the core and desktop code, targets x86, and requires .NET Framework 4.x. **111 automated checks passed with zero failures.** A separate invocation of the packaged executable passed startup verification and normal shutdown.

This is not an XP production certification. The v4.0 reference-assembly gate, actual XP runtime acceptance, and GitHub delivery are outstanding environmental gates.

## Review performed

The implementing agent reviewed the core, UI, tests, build/package/smoke scripts, CI definition, and documentation. This was a source review supported by automated tests, not an independent external audit. The review focused on data preservation, filesystem boundaries, external-change races, thread affinity, form disposal, rendering safety, resource limits, and reproducible delivery.

Issues found and corrected:

- Replaced direct note creation with a flushed temporary and non-overwriting move so failed creation cannot leave a partially written note.
- Kept failed saves dirty, blocked navigation/closing on conflicts, and added recovery-copy behavior verified through real editor controls.
- Added pre-commit revision revalidation and verification of the exact replaced backup to expose a racing external replacement while retaining both versions.
- Moved reparse-point validation before vault directory creation and tested junction rejection without touching a target outside the test fixtures.
- Rejected binary NUL and invalid UTF-8 before loading notes into native controls; this prevents native text truncation from losing note content.
- Fixed indexing completion that could run on a thread-pool thread after another form closed. Explicit UI dispatch, completion-generation tracking, deferred vault disposal, and illegal cross-thread access checks cover the lifecycle.
- Replaced repeated vault scans for link resolution/backlinks with lookup dictionaries and precomputed incoming references. The 10,000-note index/search test completed in approximately 63–68 ms on this machine; this is not a benchmark for XP hardware.
- Bounded scans for incomplete Markdown and debounced preview/search to reduce UI stalls.
- Routed normal clipboard/undo commands to the focused text control.
- Used native offscreen RichEdit rendering for visual checks after discovering that ordinary bitmap capture omitted rich-text content and desktop capture was unavailable.
- Added a packaged-entrypoint diagnostic using the normal WinForms event loop because cross-process hidden-window automation was unreliable in this sandbox.

## Evidence

| Check | Evidence / status |
| --- | --- |
| Compiler | Microsoft C# compiler 4.8.9221.0; `/langversion:4`, `/warn:4`, `/warnaserror+`, `/platform:x86` |
| Components | `scripts/package.ps1` → **111 passed, 0 failed**; detailed results in `artifacts/test-results.txt` |
| Packaged process | `scripts/smoke.ps1` → editor/preview handles, completed index, no startup alert, normal close, saved preferences, exit code 0 |
| Visual checks | Inspected native dark/light/graph previews under `artifacts/tests/run-*`; also exercised a 900×600 window |
| Filesystem | Tests cover locked/read-only files, stale writes, external deletion, Unicode, binary input, junctions, atomic-save cleanup, version history, trash and restore collisions |
| Package | ZIP contents/integrity, executable byte match, and SHA-256 manifest checked |
| Executable | PE32 x86, Windows GUI subsystem, subsystem version 4.0; no separate Torp.Core DLL required by the package |
| Strict v4.0 gate | Correctly fails because the v4.0 targeting pack is absent |
| Project skill | Frontmatter/name/description/placeholders manually checked; bundled skill validator could not run because PyYAML is not installed |
| Remote CI | Authored, not yet executed remotely |

## Outstanding gates and intentional limits

- Windows XP SP3 execution has not been tested. The local compiler used installed newer 4.x runtime assemblies. Run the strict reference build and the VM checklist in `TESTING.md` before production release for XP.
- Git and GitHub CLI were not available on the command path; standard Git installation paths checked were absent. Network attempts to GitHub and NuGet failed with Windows socket access error 10013. No Git repository initialization, commit, push, or remote-history inspection is claimed. The original plan's linked gist was also inaccessible; its supplied local instructions were followed.
- Renames do not rewrite links or migrate history. This is explicitly explained before a rename, and older history remains in the vault under the original path hash.
- Markdown is a documented native-rendering subset. Preview/index/graph limits, retention behavior, and external-file concurrency semantics are described in README and ARCHITECTURE.
- Automated component tests and native preview inspection do not establish every mouse/keyboard/accessibility behavior, power-loss scenario, network filesystem behavior, or legacy OS configuration. Those remain release acceptance work, not assumed passes.

Before committing in a Git-enabled environment, inspect the remote's existing history, review the final source diff, run the build/test commands, and preserve existing commits. Do not commit generated fixtures, downloaded tools, or local vaults.
