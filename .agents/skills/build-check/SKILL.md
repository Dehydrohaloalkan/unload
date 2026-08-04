---
name: build-check
description: Build and validate the Unload backend and Angular frontend. Use after source changes, before handoff, commit, or deployment, and whenever the user asks whether the project builds or requests build-error triage.
---

# Build Check

Run both canonical builds independently from the repository root:

1. Backend: `dotnet build`
2. Frontend: run `npm run build` with working directory `web/webApp`

Always run both commands even if the first fails. Treat every non-zero exit code as a failure. Frontend budget warnings with exit code 0 are warnings, not failures, unless the user asks to fix them.

Report results concisely:

- If both succeed, state that backend and frontend builds passed and include warning counts when relevant.
- If either fails, identify Backend, Frontend, or Both and include the exact relevant errors.
- Distinguish missing tools, dependencies, or directories as environment errors rather than build errors.

When the task is validation or review only, do not modify files. When the user asked to implement or fix code, resolve failures caused by the requested changes and rerun both builds before handoff.
