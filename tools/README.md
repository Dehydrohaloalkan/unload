# Служебные команды

Все команды запускаются из корня репозитория.

## Windows PowerShell или CMD

```powershell
.\tools\verify.cmd
.\tools\export-openapi.cmd
```

`.cmd`-файлы сами запускают PowerShell с подходящей execution policy. Менять системную политику
PowerShell и открывать Git Bash не требуется.

Если checkout находится на обычном диске Windows (`C:\...`, `D:\...`), выполняется нативный
PowerShell-код с Windows-версиями `dotnet`, `node` и `npm`. Если путь начинается с
`\\wsl.localhost\...` или `\\wsl$\...`, команда автоматически передаётся в соответствующий WSL
дистрибутив, чтобы не смешивать Windows и Linux `obj/` или `node_modules/`.

## Linux, WSL или CI

```bash
./tools/verify.sh
./tools/export-openapi.sh
```

## Назначение

| Команда | Что делает |
|---|---|
| `verify` | Restore, format/analyzers, backend build и tests, `npm ci`, audit, frontend tests и production build |
| `export-openapi` | Собирает API, временно запускает только OpenAPI endpoint на порту `5099`, обновляет `openapi/Unload.Api.json` и останавливает свой процесс |

Требуются .NET SDK из `global.json` и Node.js из `.node-version`. Windows NVM:

```powershell
nvm install 24.19.0
nvm use 24.19.0
```
