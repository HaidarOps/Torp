# Torp development

Torp is a local-first Windows notebook. Read `ProjectPlan.md` for the original brief and `docs/ARCHITECTURE.md` for implementation boundaries.

## Scope and compatibility

- Work only in this repository. Do not inspect unrelated projects or personal files. Installed compiler/runtime tools may be invoked; they are not reference implementations.
- Keep the executable compatible with Windows XP SP3 and .NET Framework 4.0. Use C# 4 syntax and .NET 4.0 APIs. No Electron, WebView, network services, telemetry, or runtime package downloads.
- Use native WinForms and the restrained sidebar/editor/backlinks layout familiar from Obsidian. Torp is an independent implementation, not copied Obsidian source or branding.
- Keep domain and storage code in `src/Torp.Core`, controls in `src/Torp.App`, and component tests in `tests`. Use four spaces, Allman braces, PascalCase public members, camelCase private fields, explicit access modifiers, and descriptive names.

## Data safety

- Notes are UTF-8 Markdown in a user-selected vault. Never silently overwrite concurrent external changes, discard a dirty buffer after failure, follow reparse points, or accept paths outside a vault.
- Replace existing files atomically, retain previous versions, and move deleted notes into vault-local trash. Do not replace these operations with delete-then-write fallbacks.
- Keep persistence errors visible and actionable. Log exception details without note content. Background workers must never touch controls or disposed vaults.
- Rendering must not execute HTML, JavaScript, embedded files, or remote resources. External URLs require an explicit user action and an allowed scheme.

## Verification and delivery

- Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test` after meaningful changes. The test runner creates isolated vaults under `artifacts/tests` and must not touch real notes.
- Storage changes require failure/conflict/round-trip tests. UI changes require the component smoke suite and visual inspection of its screenshot.
- Run `scripts/build.ps1 -Test -StrictNet40` against real .NET 4.0 reference assemblies before claiming XP compatibility is verified. Run the acceptance checklist on an actual XP SP3 machine or VM before an XP release.
- Review the entire change before committing; record evidence and open limitations in `docs/REVIEW.md`. Never claim unrun checks passed.
- Commit and push only changes for this project, to the authorized `https://github.com/HaidarOps/Torp` repository. Do not force push or replace existing remote history. Missing Git, credentials, or network access is a delivery limitation, not a reason to invent a successful push.
- The project skill at `skills/torp-development/SKILL.md` supplies the task-specific build and review workflow.
