# Coding agent instructions

## Start

Read `README.md` and `CONTRIBUTING.md`. Use `docs/architecture.md` for module ownership and `docs/verification.md` for checks. Preserve unrelated working-tree changes. Work on a task branch and keep each change focused.

## Contracts

- Keep FastAPI/Jinja2 and the existing `/doc?path=...` routes, document identities and retrieval references compatible.
- Treat archived originals as immutable input. Reading views are derived output. Match document identity and hashes; preserve repeated image occurrences and order.
- Show unknown compatibility or verification status honestly. Synthetic tests and desktop browser checks do not prove real game behavior or physical mobile gestures.
- Separate corpus indexes from user/session state. Tests create temporary data and must not read or write a real installation or production database.
- Keep credentials in local configuration or environment variables. Publish only code, synthetic fixtures and reviewed documentation; never copy private chat logs, user content or machine-specific deployment details.
- Review dependency advisories and the staged diff before a commit/push. Test behavior affected by the change; report failures and skips accurately.

## Completion

Use the documented verification command, update the relevant docs, and open a PR explaining the concrete change and its tests. Keep a user-readable task record in the Issue/PR. A GitHub push or merge is not a deployment; production and game changes require their own scope and validation.
