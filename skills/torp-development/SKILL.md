---
name: torp-development
description: Build, test, review, and package the Torp Windows notebook in this repository, including its XP compatibility and Markdown vault safety constraints.
---

# Torp development

Read the repository `AGENTS.md` and `docs/ARCHITECTURE.md` before editing. Keep all project work in this workspace.

Use `scripts/build.ps1 -Test` to compile the dependency-free core, desktop app, and component runner. Test vaults and screenshots belong under `artifacts`, never in a user's actual vault. Read the test output: the script must fail when any component fails.

For storage work, exercise conflicts, failed writes, traversal, Unicode, backups, deletion, and restore. A save failure must leave the editor dirty and retain the original file. For rendering, exercise literal HTML and hostile URLs; preview is native rich text, not a browser. For UI work, inspect the smoke-test screenshot and exercise navigation with unsaved changes.

Use `-StrictNet40` for release qualification; this requires the .NET 4.0 targeting pack or `TORP_NET40_REFS`. A compiler from a newer Framework runtime is useful for local verification but does not establish XP API compatibility. Follow `docs/TESTING.md` for the XP VM gate.

Before a commit, review the diff for data loss, control lifecycle issues, unbounded work, and unsupported APIs. Update `docs/REVIEW.md` with actual findings and test evidence. Packaging is `scripts/package.ps1`; public release and remote push require the already-authorized destination and working Git/authentication. Report any blocked step accurately.
