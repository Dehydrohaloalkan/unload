---
name: run-and-test-app
description: Start the Unload .NET API and Angular application locally and verify behavior in a real Playwright browser. Use for live UI checks, extra-unload/history/main-run workflows, screenshots, refresh recovery, or confirming that a UI fix works end to end. A real database is not required because development uses StubDatabaseClient.
---

# Run and Test Unload

## Runtime facts

- Backend: `http://localhost:5000`.
- Angular: `http://localhost:4200`; open this URL because it proxies `/api` and `/hubs` to the backend.
- `StubDatabaseClient` seeds development data from SQL markers:
  - `EXTRA_BANKS`: six banks, `B01` through `B06`.
  - `EXTRA_UNLOAD`: 50 rows per bank and honors `IN ('B01', ...)` filters.
  - `PRESET_READY_PROBE`: nondeterministic `0` or `1`; use admin override for reliable tests.
  - Other queries: about 2,500 rows with a short delay, so a main run takes roughly 25 seconds.
- Output and persisted run state live under `output/`. They may contain real user runs. Never delete or reset `output/` or `output/_state`.
- Admin mode bypasses the preset gate and daily window. Its password is the current local time as `HHMM`.

## Start the stack

From the repository root, build and start the API:

```bash
dotnet build backend/Unload.Api/Unload.Api.csproj -clp:NoSummary
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 \
  dotnet run --no-build --project backend/Unload.Api/Unload.Api.csproj
```

Start Angular separately from `web/webApp`:

```bash
npx ng serve --port 4200
```

Run long-lived servers in separate execution sessions, retain their session IDs, and stop only the processes started for this test. Do not kill unrelated processes that happened to be using the same ports.

Poll readiness instead of waiting blindly:

```bash
for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/runs/today)
  [ "$code" = 200 ] && break
  sleep 2
done
curl -s http://localhost:5000/api/runs/extra/banks
```

The banks endpoint should return six banks.

## Prepare Playwright

Prefer an existing Playwright installation. Otherwise create a scratch directory with `mktemp -d` under `/tmp`, install `playwright` there, and copy the required bundled script from this skill's `scripts/` directory. Dependency and browser downloads require network approval.

```bash
npm init -y
npm install --save-dev playwright@latest
npx playwright install chromium
npx playwright install chromium-headless-shell
```

Write screenshots into the scratch directory and inspect them with the local image-viewing tool.

## Choose a scenario

- Run `scripts/extra-smoke.js` to check the Extra drawer, bank selection, gateway checkbox, launch flow, and bank names in history.
- Run `scripts/extra-recovery.js` to check that an active Extra run remains visible and stoppable after a fresh page load.
- For the green delivered gateway badge, also start `console/Unload.FtpServer` using development FTP settings.

Reliable Extra start through the API:

```bash
curl -s -X POST http://localhost:5000/api/runs/extra \
  -H "Content-Type: application/json" \
  -d '{"adminOverride":true,"publishToGateway":false,"selectedBanks":null}'
```

Use `selectedBanks: null` for all banks or an array such as `["B01", "B02"]` for a subset. Read status through `GET /api/runs/today`; stop only the test run through `POST /api/runs/{correlationId}/stop`.

## UI selectors

- Admin button: `Админ-режим`; password input: `#admin-password`; submit: `Войти`.
- Details button within `app-extra-card` or `app-run-card`: aria-label `Подробнее`.
- Drawer: `aside.details-drawer`; tabs: `Выгрузка` and `История`.
- Checkbox row: `.details-check-row`; Material checkbox: `mat-checkbox`.
- Bank rows: `.bank-item`; launch: `Запустить extra`; stop: `Остановить выгрузку`.
- History hierarchy: `.history-run__summary`, then `.history-script .history-member__summary`, then `.history-bank .history-member__summary`.

## Recovery timing

Extra normally finishes too quickly to observe refresh recovery. If a delayed stub is necessary, first inspect existing changes to `StubDatabaseClient.cs`. Do not overwrite user edits. Add only a small temporary delay with `apply_patch`, test, and remove only that exact hunk with `apply_patch` afterward. Never use `git checkout`, reset, or broad cleanup for restoration.

## Finish safely

Stop the two server sessions started for the test. Preserve runtime output and history. Remove only the exact scratch directory created for Playwright when safe and authorized. Check `git status --short` and ensure only intentional changes remain.
