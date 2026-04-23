---
name: build-check
description: Build backend (.slnx) + frontend (web/webApp) and fail on errors
---

# Build-check skill (repo canonical builds)

Use this skill whenever you need to validate the repo “works”, verify a fix, or triage build failures.

## Canonical build steps (PowerShell, Windows)

Run from the **repo root**:

```powershell
dotnet build
pushd .\web\webApp\
npm run build
popd
```

## Optional single command

If present, you may run:

```powershell
.\.cursor\skills\build-check\build-check.ps1
```

## Failure handling

- If any command returns a non-zero exit code, treat it as a failure and fix before proceeding.
- If frontend build shows budget warnings but exits 0, treat as a warning (not a build failure) unless explicitly asked to fix budgets.

