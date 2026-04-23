# /build — build backend + frontend (fail on errors)

## Goal
Run the canonical builds for this repo and surface any build errors.

## Instructions
Run these commands **exactly** (PowerShell). Stop if any step fails (non-zero exit code) and report the failure output.

```powershell
dotnet build
pushd .\web\webApp\
npm run build
popd
```

## Output
- Build succeeded for backend and frontend, or a minimal actionable error summary with the failing command.

## Note
This command mirrors the project-local `build-check` skill.

