# Быстрый вход в проект Unload

Этот документ ведёт от исходного кода до работающего локального приложения и показывает,
где искать место для типового изменения. Подробные пользовательские сценарии находятся в
[USER_GUIDE.md](USER_GUIDE.md), внутренние процессы — в [ARCHITECTURE.md](ARCHITECTURE.md),
термины — в [GLOSSARY.md](GLOSSARY.md).

## 1. Что понадобится

- .NET SDK из `global.json`;
- Node.js из `.node-version` и npm, совместимый с `web/webApp/package-lock.json`;
- свободные порты `5000` для API и `4200` для Angular dev-server;
- запуск команд из корня репозитория.

Development-сборка использует `StubDatabaseClient`, поэтому реальная база данных для первого
локального запуска не нужна. FTP требуется только для проверки фактической доставки в gateway.

## 2. Выполнить полную проверку

Из корня репозитория:

```bash
./tools/verify.sh
```

Скрипт восстанавливает backend-зависимости, проверяет формат и analyzers, собирает backend,
запускает оба backend test project, устанавливает frontend-зависимости через `npm ci`, выполняет
dependency audit, проверяет frontend tests и актуальность сгенерированного API client, затем
выполняет production-сборку Angular.
Успешный признак — сообщение `Verification passed.` и exit code `0`.

`npm ci` использует зафиксированный lockfile и не должен переписывать версии зависимостей.

## 3. Разобрать отдельную ошибку

Если полная проверка упала, нужную часть можно повторить независимо:

```bash
dotnet build
```

```bash
cd web/webApp
npm run build
```

Ожидаемый результат: обе команды завершаются с exit code `0`. Предупреждение frontend о бюджете
не равно ошибке сборки, если команда завершилась успешно.

Backend-тесты:

```bash
dotnet test unload.slnx
```

Регрессионная проверка геометрии интерфейса в реальном Chromium:

```bash
cd web/webApp
npx playwright install chromium
npm run test:ui
```

Playwright сам поднимает API и Angular, если порты `5000` и `4200` свободны, либо использует уже
запущенный локальный стек. Эти тесты проверяют выравнивание иконок, границы хедера при открытой
боковой панели и отсутствие вложенного скролла во вкладке истории.

Тесты обязаны использовать собственные временные каталоги. Не направляйте тестовые fixtures в
`output/` или `output/_state`: там могут находиться реальные результаты и состояние запусков.

## 4. Запустить приложение

Откройте два терминала.

Терминал 1 — API:

```bash
dotnet run --project backend/Unload.Api/Unload.Api.csproj
```

Успешный признак: в логе появляется адрес API; стандартный локальный адрес проекта —
`http://localhost:5000`.

Терминал 2 — Angular:

```bash
cd web/webApp
npm start
```

Успешный признак: Angular сообщает о завершении сборки, а `http://localhost:4200` открывает
интерфейс. Запросы `/api` и `/hubs/status` dev-server проксирует на API через `proxy.conf.json`.

Первый пользовательский проход выполняйте по [USER_GUIDE.md](USER_GUIDE.md): дождитесь состояния
`probe`, выполните `preset`, затем проверьте `run` или `extra`.

## 5. Где начинается выполнение

| Что происходит | Первая точка входа |
|---|---|
| Запуск API и регистрация HTTP/SignalR | `backend/Unload.Api/Program.cs` |
| Общая регистрация runtime-сервисов | `backend/Unload.Bootstrapper/DependencyInjection/ServiceCollectionExtensions.cs` |
| Решение, можно ли запустить задачу | `backend/Unload.Tasks/TaskWorkflow.cs` |
| Дневное окно и доступность `preset` | `backend/Unload.Tasks/DailyWindowPolicy.cs` |
| Расписание автоматического `probe` | `backend/Unload.Api/Services/ProbeSchedulerHostedService.cs` |
| Основная выгрузка | `backend/Unload.Tasks.MainUnload/` |
| Дополнительная выгрузка | `backend/Unload.Tasks.ExtraUnload/` |
| Состояние, история и JSON snapshots | `backend/Unload.Store/` |
| Отправка и подтверждение gateway | `backend/Unload.Gateway/` |
| Angular orchestration и stores | `web/webApp/src/app/state/` |
| Пользовательские компоненты | `web/webApp/src/app/components/` |

Если связь между компонентами неочевидна, сначала используйте локальный граф:

```bash
./.tools/bin/graphify query "Какой компонент отвечает за нужное поведение?"
./.tools/bin/graphify path "ПервыйКомпонент" "ВторойКомпонент"
./.tools/bin/graphify explain "НазваниеКонцепции"
```

## 6. Где менять типовые вещи

| Изменение | Сначала смотреть |
|---|---|
| Правила зависимостей и конфликтов | конкретный `UnloadTask`, затем `TaskWorkflow` |
| Время `probe` и дневное окно | `PresetGate` в appsettings, `ProbeSchedulerHostedService`, `DailyWindowPolicy` |
| SQL и состав выгрузки | `configs/catalog.json`, `scripts/`, соответствующий task/engine |
| Статусы и восстановление | `RunStateStore`, `TaskExecutionHistoryStore`, projections |
| HTTP endpoint или error code | controllers, `TaskLaunchException`, error mapping, frontend API client |
| HTTP DTO или client | C# contract, `openapi/Unload.Api.json`, generated API, затем UI adapter |
| Событие реального времени | `RunStatusHubContract`, `realtime-hub.contract.ts`, store-получатель |
| Отображение истории | backend history DTO, `history-projection.util.ts`, соответствующий store/component |
| FTP-доставка | `Gateway.Ftp`, `GatewayUploadService`, background service и sender feedback |

Перед широкой правкой прочитайте соответствующий раздел [ARCHITECTURE.md](ARCHITECTURE.md).

## 7. Безопасный порядок изменения

1. Выполнить `git status --short` и не смешивать чужие изменения со своей задачей.
2. Найти текущий поток через Graphify и подтвердить его чтением исходников.
3. Сначала добавить тест, фиксирующий бизнес-сценарий, если меняется нетривиальное правило.
4. Внести минимальное изменение без смены форматов `output` и snapshots.
5. После изменения HTTP-контракта выполнить `tools/export-openapi.sh`, затем
   `cd web/webApp && npm run generate:api`; generated-файлы вручную не исправлять.
6. Обновить затронутую документацию.
7. После source-изменений выполнить `./.tools/bin/graphify update .`.
8. Запустить `./tools/verify.sh`.

Для live-проверки UI запускайте API и Angular и проверяйте наблюдаемый результат в браузере.

## 8. Конфигурация и данные

- `configs/catalog.json` — groups, members, targets и большие скрипты;
- `scripts/` — SQL по типам задач и группам;
- `backend/Unload.Api/appsettings*.json` — database, runner, preset gate, retention и gateway;
- `output/<run>/` — сформированные файлы и отчёты;
- `output/_state/runs.json` — состояния `run` и `extra`;
- `output/_state/task-history.json` — завершённые задачи и зависимости текущего дня.

Секреты и реальные connection strings не добавляются в документацию или тестовые fixtures.

## 9. Куда идти дальше

- [Документация проекта](README.md) — карта всех документов;
- [Пользовательское руководство](USER_GUIDE.md) — действия, состояния и признаки успеха;
- [Архитектура](ARCHITECTURE.md) — компоненты, потоки, persistence и контракты;
- [Словарь](GLOSSARY.md) — единые определения;
- [План сопровождения](MAINTAINABILITY_PLAN.md) — приоритеты и точка продолжения.
