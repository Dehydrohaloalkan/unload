# Unload

Платформа выгрузки данных с явным пайплайном задач:

- четыре задачи: `probe`, `preset`, `run`, `extra`;
- единственный контролёр запусков `TaskWorkflow` — проверяет все зависимости, конфликты и дневное окно;
- API и Angular WebApp для запуска и наблюдения;
- локальные FTP Server и GatewayHandler для проверки интеграции.

Этот `README.md` описывает прикладную логику: как устроен бизнес-пайплайн, какие сервисы за что отвечают, как менять правила и куда добавлять новые задачи.

## Что делает приложение

Приложение:

- получает выборку по участникам или target-кодам;
- находит подходящие SQL-скрипты;
- читает данные из БД потоково;
- режет результат на чанки;
- пишет файлы в `output`;
- публикует файлы в gateway (FTP) и получает sender-feedback;
- хранит и отдает live-статусы;
- поддерживает отдельные задачи `preset` и `extra`.

## Бизнес-логика

Текущая бизнес-логика такая:

1. После `StartHour:StartMinute` (настраивается, по умолчанию `11:25`) `ProbeSchedulerHostedService` начинает polling и автоматически запускает задачу `probe`.
2. Задача `probe` выполняет SQL из настройки `PresetGate.ProbeSql`.
3. Пока probe возвращает `0`, запуск `preset` запрещён.
4. Когда probe возвращает `1`, `DailyWindowPolicy` отмечает готовность, и пользователь может запустить `preset`.
5. Пользователь запускает `preset`.
6. После успешного `preset` становятся доступны `run` и `extra` (требуется, чтобы дневное окно было открыто).
7. `run` и `extra` могут выполняться параллельно.
8. `preset` не может выполняться одновременно с `run` или `extra`.
9. После смены даты `DailyWindowPolicy` сбрасывает состояние: нужен новый `probe` и `preset` для нового дня.

## Задачи

| Задача | Код | Синхронная? | RequiresCompleted | ConflictsWith | Дневное окно |
|---|---|---|---|---|---|
| `ProbeTask` | `probe` | Да | — | — | Нет |
| `PresetTask` | `preset` | Да | `probe` | `run`, `extra` | Особое (probe=1 + время) |
| `MainUnloadTask` | `run` | Нет (deferred) | `preset` | `preset` | Да |
| `ExtraUnloadTask` | `extra` | Да | `preset` | `preset` | Да |

Все задачи запускаются через единственный метод `TaskWorkflow.LaunchAsync`.

## Полный поток выполнения

### Поток `probe -> preset -> run/extra`

1. `ProbeSchedulerHostedService` отслеживает локальное время.
2. После `StartHour:StartMinute` начинает polling (по умолчанию каждые 60 секунд).
3. Вызывает `TaskWorkflow.LaunchAsync(probe)`.
4. `ProbeTask` выполняет SQL probe, применяет результат в `DailyWindowPolicy`.
5. Если probe вернул `1`, `DailyWindowPolicy.ReadyForPreset` становится `true`.
6. Пользователь вызывает `preset` через Angular WebApp или API.
7. `TaskWorkflow` проверяет: probe пройден + время в пределах окна + нет конфликтов.
8. `PresetTask` выполняет SQL-скрипты из `scripts/preset`.
9. После успешного `preset`:
   - `DailyWindowPolicy.MarkPresetCompleted()` вызывается внутри `PresetTask`;
   - в `TaskExecutionHistoryStore` появляется запись с кодом `preset`;
   - открывается дневное окно для `run` и `extra`.
10. Пользователь может запускать `run` и `extra` независимо.

### Поток `run`

1. Пользователь запускает выгрузку в Angular WebApp, который вызывает `POST /api/runs`.
2. `TaskWorkflow.LaunchAsync(run)` проверяет все ограничения.
3. `MainUnloadTask`:
   - нормализует входные коды;
   - при режиме `MemberCodes` через `ICatalogService` переводит их в `targetCodes`;
   - создает `RunRequest` через `RunRequestFactory`;
   - резервирует слот в `RunActivationChannel` (single-active);
   - создает стартовый статус в `RunStateStore`.
4. Задача возвращает `TaskExecutionStatus.Accepted` — выполнение продолжается в фоне.
5. `MainUnloadHostedService` читает активацию из `RunActivationChannel`.
6. `MainUnloadEngine` выполняет выгрузку, эмитит `RunnerEvent`.
7. Статусы накапливаются в `RunStateStore`.
8. После завершения записи файлов `FtpGatewayPublisher` публикует batch-ready событие в FTP gateway.
9. `SenderFeedbackProjectionBackgroundService` проецирует gateway-feedback в `RunStateStore` и публикует `run_status` в SignalR.
10. Когда все batches получили feedback (или `PublishToGateway = false`), `TryPromoteToCompleted` переводит run в `Completed`.
11. `MainUnloadHostedService` записывает в `TaskExecutionHistoryStore` и освобождает слот.

### Поток `extra`

1. Пользователь запускает Extra в Angular WebApp, который вызывает `POST /api/runs/extra`.
2. `TaskWorkflow.LaunchAsync(extra)` проверяет все ограничения.
3. `ExtraUnloadTask`:
   - находит SQL-скрипты в корне `scripts` (без подпапок);
   - выполняет их параллельно;
   - агрегирует результат по `NrBank`;
   - пишет файлы через `ExtraOutputWriter`.
4. Задача возвращает `TaskExecutionStatus.Completed` — выполнение синхронное.
5. Запись результата в `TaskExecutionHistoryStore`.

## Кто за что отвечает

### `backend/Unload.Core`

Базовый слой контрактов:

- модели домена: `RunRequest`, `ScriptDefinition`, `DatabaseRow`, `FileChunk`, `WrittenFile`, `RunnerEvent`, `RunnerStep`;
- контракты: `ICatalogService`, `IDatabaseClient`, `IDatabaseClientFactory`, `IFileChunkWriter`, `IRequestHasher`.

Меняется редко. Общие контракты без логики оркестрации.

### `backend/Unload.Catalog`

Отвечает за каталог и правила поиска скриптов.

Главное:

- читает `configs/catalog.json`;
- строит связи `group -> member -> target`;
- находит SQL-файлы;
- определяет большие target-выборки через `bigScripts`.

### `backend/Unload.DataBase`

Отвечает за доступ к БД.

Главное:

- создает клиентов БД (`DatabaseClientFactory`);
- выполняет SQL;
- возвращает `DbDataReader` для потокового чтения.
- `connectionString` может быть plain-text строкой или строкой формата `dpapi:<base64>`, которая расшифровывается через Windows DPAPI (`CurrentUser`).

### `backend/Unload.FileWriter`

Отвечает за запись чанков в файлы.

Главное:

- формирует output-файлы;
- пишет заголовки;
- гарантирует корректную параллельную запись по файлам.

### `backend/Unload.Gateway`

Отвечает за публикацию файлов через FTP gateway и получение sender-feedback.

Главное:

- `FtpGatewayPublisher` — публикует файлы на FTP-сервер, отдаёт batch-ready события;
- `FtpGatewayBackgroundService` — фоновый обработчик;
- `GatewayUploadService` — обработка ручной загрузки файлов через API endpoint; чистка staging-каталогов по retention;
- конфигурация через секцию `Gateway.Ftp` в appsettings.

Заменяет in-memory MQ-заглушку из прежней архитектуры.

### `backend/Unload.Cryptography`

Хеширование запросов: `Sha256RequestHasher`.

### `backend/Unload.Store`

Единое хранилище состояний и истории.

Главное:

- `RunStateStore` — потокобезопасное in-memory хранилище статусов запусков с JSON-персистентностью. Хранит `RunStatusInfo` (статус, members, workers, artifacts, sender batches). При рестарте незавершённые запуски переводятся в `Cancelled`.
- `TaskExecutionHistoryStore` — история завершённых задач (`TaskRecord`). Используется воркфлоу для проверки `RequiresCompleted` (метод `HasRunToday`).
- `JsonFileStore<T>` — атомарная JSON-персистентность (write-temp + move). Сбой записи логируется как `Error`.
- `GatewaySenderFeedbackConsumer` — принимает sender-feedback из gateway.
- `RequeueService` — повторная публикация результатов прошлых запусков в gateway.

### `backend/Unload.Tasks`

Ядро задач — модели, контроллер и политика окна.

Главное:

- `UnloadTask` — абстрактный базовый класс. Каждая задача декларирует `Code`, `RequiresCompleted`, `ConflictsWith`, `RequiresDailyWindowOpen`, `RequiresPresetWindow`, `IsDeferred` — воркфлоу их исполняет.
- `TaskWorkflow` — единственный класс оркестрации. Контролирует все ограничения запуска и вызывает `task.ExecuteAsync`. Проверка конфликтов и захват foreground-слота атомарны (`ClaimSlot`). Без интерфейса — одна реализация.
- `DailyWindowPolicy` — политика дневного окна (заменяет `PresetGateService`). Хранит in-memory состояние; без интерфейса.
- `WorkflowQueryService` — агрегированные представления для UI (`today` / `dashboard` / `history`); выносит агрегацию из контроллеров API.
- `TaskLaunchRequest` / `TaskExecutionResult` — единые модели запроса и результата.
- `TaskLaunchException` — единый тип бизнес-ошибки (заменяет `WorkflowTaskDispatchException`). Поля: `FailureKind`, `ErrorCode`, `Extensions`.
- `TaskCodes` — константы `run`, `preset`, `extra`, `probe`.
- `TaskCorrelationId` — единая генерация корреляционных id исполнений задач.
- `RunActivationChannel` — in-memory канал single-active задачи `run`.
- `RunActivation` / `PresetGateOptions` / `PresetGateState` — вспомогательные модели.

### `backend/Unload.Tasks.MainUnload`

Задача и движок основной выгрузки.

Главное:

- `MainUnloadTask` — задача `run`. Deferred: активирует `RunActivationChannel` и возвращает `Accepted`.
- `MainUnloadEngine` — движок выгрузки. N worker-потоков, `ScriptDistributor` (big/light очереди), `RunnerEventEmitter`.
- `RunRequestFactory` — создаёт `RunRequest` с корреляционным ID.
- `RunnerOptions` — `WorkerCount`, `ChunkSizeBytes`.

### `backend/Unload.Tasks.ExtraUnload`

Задача дополнительной выгрузки.

Главное:

- `ExtraUnloadTask` — задача `extra`. Синхронная.
- `ExtraScriptExecutor` — выполняет один extra-скрипт с агрегацией строк.
- `ExtraOutputWriter` — пишет агрегированные файлы.

### `backend/Unload.Tasks.Preset`

Задачи probe и preset.

Главное:

- `ProbeTask` — задача `probe`. Выполняет SQL probe, применяет результат к `DailyWindowPolicy`, фиксирует в `TaskExecutionHistoryStore`.
- `PresetTask` — задача `preset`. Синхронная. Выполняет SQL-скрипты из `scripts/preset`, вызывает `DailyWindowPolicy.MarkPresetCompleted()`.
- `PresetScriptExecutor` — выполняет один preset-скрипт.

### `backend/Unload.Bootstrapper`

Единая DI-композиция и вся работа с конфигурацией.

Главное:

- `AddUnloadRuntime(IServiceCollection, IConfiguration)` — единственная точка регистрации runtime-сервисов API.
- `UnloadConfiguration` — агрегат всех настроек (`Paths`, `Database`, `Runner`, `PresetGate`, `HistoryRetention`).
- `UnloadConfigurationLoader` — читает `IConfiguration`, резолвит корень workspace (ищет `configs/catalog.json` + `scripts/` вверх по дереву).
- `UnloadRuntimePaths` — пути к каталогу, скриптам и output.

### `backend/Unload.Api`

Транспортный слой (ASP.NET Core + SignalR).

Главное:

- run-контроллеры разделены по операциям: `RunLaunchController`, `RunStatusController`,
  `RunHistoryController`, `GatewayRequeueController`; публичные маршруты остаются под `/api/runs`;
- `CatalogController` и `SystemController` обслуживают каталог и системные операции;
- `RunStatusHub` — SignalR hub `/hubs/status`;
- error handling: `GlobalExceptionHandler`, `ApiProblemDetailsFactory`, `ApiProblemException`;
- фоновые сервисы:
  - `MainUnloadHostedService` — читает активации из `RunActivationChannel`, гоняет `MainUnloadEngine`, публикует SignalR;
  - `ProbeSchedulerHostedService` — по расписанию вызывает `TaskWorkflow.LaunchAsync(probe)`, публикует `preset_state` в SignalR;
  - `SenderFeedbackProjectionBackgroundService` — проецирует gateway-feedback в `RunStateStore`;
  - `HistoryRetentionBackgroundService` — удаляет старые записи из `TaskExecutionHistoryStore` и `RunStateStore`.

### `web/webApp`

Браузерный Angular-клиент к API через HTTP + SignalR.

Главное:

- главная страница показывает состояние 4 этапов (`сервер`, `пресет`, `выгрузка`, `extra`) и правую панель деталей;
- часы синхронизируются через `GET /api/system/time`;
- подписывается на SignalR: `status`, `run_status`, `preset_state`;
- в `admin mode` передаёт `adminOverride` для обхода gate-зависимостей.

### Вспомогательные приложения

- `console/Unload.FtpServer` — поддерживаемый development-only FTP-сервер для локальной проверки доставки;
- `console/Unload.GatewayHandler` — поддерживаемый development-only обработчик файлов локального gateway.

Официальный production-путь один: `Unload.Api` + Angular `web/webApp`. Удалённые
`Unload.Console` и `Unload.WebConsole` не являются поддерживаемыми способами запуска.

## Где управлять пайплайном

### Изменить порядок задач и правила запуска

Файлы — реализации `UnloadTask` в проектах задач:

- `backend/Unload.Tasks.Preset/ProbeTask.cs`
- `backend/Unload.Tasks.Preset/PresetTask.cs`
- `backend/Unload.Tasks.MainUnload/MainUnloadTask.cs`
- `backend/Unload.Tasks.ExtraUnload/ExtraUnloadTask.cs`

Свойства `RequiresCompleted`, `ConflictsWith`, `RequiresDailyWindowOpen` на классе задачи — декларация ограничений. Воркфлоу `TaskWorkflow` их исполняет.

### Изменить бизнес-условия дневного окна

Файл:

- `backend/Unload.Tasks/DailyWindowPolicy.cs`

Здесь задаются: время старта окна, daily reset, правила `CanRunPreset` и `IsOpen`.

### Изменить расписание probe

Файлы:

- `backend/Unload.Api/Services/ProbeSchedulerHostedService.cs`
- `backend/Unload.Tasks.Preset/ProbeTask.cs`

`ProbeSchedulerHostedService` — расписание и polling; `ProbeTask` — выполнение SQL probe и обновление `DailyWindowPolicy`.

### Изменить исполнение `run`

Файлы:

- `backend/Unload.Tasks.MainUnload/MainUnloadTask.cs`
- `backend/Unload.Tasks.MainUnload/Services/MainUnloadEngine.cs`
- `backend/Unload.Api/Services/MainUnloadHostedService.cs`
- `backend/Unload.Store/RunStateStore.cs`

### Изменить исполнение `preset` или `extra`

Файлы:

- `backend/Unload.Tasks.Preset/PresetTask.cs`
- `backend/Unload.Tasks.Preset/PresetScriptExecutor.cs`
- `backend/Unload.Tasks.ExtraUnload/ExtraUnloadTask.cs`
- `backend/Unload.Tasks.ExtraUnload/ExtraScriptExecutor.cs`
- `backend/Unload.Tasks.ExtraUnload/ExtraOutputWriter.cs`

## Как добавить новую задачу

### Сценарий: новая задача, запускаемая пользователем

Что нужно сделать:

1. Добавить новый код задачи в `backend/Unload.Tasks/TaskCodes.cs`.
2. Создать новый класс-наследник `UnloadTask` (в существующем или новом проекте задачи).
3. Объявить `RequiresCompleted`, `ConflictsWith`, `RequiresDailyWindowOpen` на классе.
4. Реализовать `ExecuteAsync` — либо синхронно (как preset/probe), либо deferred (как run/extra).
5. Зарегистрировать задачу как `UnloadTask` в DI (добавить в `AddUnload*` метод проекта задачи).
6. Если задача deferred — добавить фоновый воркер в `Unload.Api`.
7. Добавить API endpoint и действие в Angular WebApp.
8. Обновить:
   - `README.md`
   - `docs/ARCHITECTURE.md`
   - `postman/unload-api.postman_collection.json`, если это API-задача.

## API

Основные endpoint'ы:

- `POST /api/runs` — старт `run` по `memberCodes` (или `targetCodes`)
- `GET /api/runs/preset/state` — состояние дневного окна (`PresetGateState`)
- `POST /api/runs/preset` — запуск `preset`
- `POST /api/runs/extra` — запуск `extra`
- `POST /api/runs/{correlationId}/stop` — остановка активного `run`
- `POST /api/runs/requeue` — повторная публикация результатов в gateway
- `GET /api/runs` — список запусков (`RunStatusInfo[]`)
- `GET /api/runs/today` — список запусков `run` за текущий день
- `GET /api/runs/dashboard` — snapshot для UI (preset-state, today flags, последние timestamps, история за день)
- `GET /api/runs/history` — история запусков за N дней (`?days=N`, по умолчанию из настроек)
- `GET /api/runs/active` — активный `run` (или `{ correlationId: null }`)
- `GET /api/runs/{correlationId}` — статус конкретного `run`
- `GET /api/catalog` — структура каталога
- `GET /api/members` — список мемберов с target-кодами и активным статусом
- `GET /api/system/time` — серверное время и timezone для синхронизации UI
- `GET /api/system/health` — состояние записи run-state и task history (`200` или `503`)
- `GET /api/system/download?path=...` — скачивание файла из output
- `GET /api/system/output-files?path=...` — листинг файлов в output-папке
- `GET /api/system/download-archive?path=...` — скачивание ZIP-архива output-папки
- `POST /api/system/sender-feedback` — ручная подача sender-feedback (интеграционный endpoint)
- `POST /api/system/gateway-upload` — загрузка файлов в gateway через API

SignalR:

- hub: `/hubs/status`
- события:
  - `status` — события раннера активного запуска (`RunnerEvent`)
  - `run_status` — обновления статуса запуска (`RunStatusInfo`)
  - `preset_state` — состояние дневного окна (`PresetGateState`)
  - `preset_replayed` — результат повторного запуска уже выполненного preset (`ScriptTaskRunResult`)

Формат ошибок API:

- `application/problem+json`
- поля: `type`, `title`, `status`, `detail`, `instance`
- расширения: `errorCode`, `traceId`
- дополнительные поля по ситуации: например `activeCorrelationId`
- примеры `errorCode`: `RUN_ALREADY_IN_PROGRESS`, `VALIDATION_ERROR`, `PRESET_GATE_BLOCKED`, `TASK_DEPENDENCY_NOT_SATISFIED`, `TASK_ALREADY_RUNNING`, `RUN_NOT_FOUND`, `UNKNOWN_MEMBER_CODES`

## Конфигурация

### `configs/catalog.json`

- `bigScripts` — target-выборки (memberId+groupId), которые считаются большими и выполняются в `n-1` потоках

### `appsettings` -> `Database`

- `TimeoutSeconds`
- `ConnectionString` (plain-text или `dpapi:<base64>`)

### `appsettings` -> `Runner`

- `WorkerCount` (по умолчанию 4)
- `ChunkSizeBytes` (по умолчанию 10 МБ)

### `appsettings` -> `PresetGate`

- `Enabled`
- `StartHour`
- `StartMinute`
- `PollIntervalSeconds`
- `ProbeSql`

### `appsettings` -> `HistoryRetention`

- `RetentionDays`

### `appsettings` -> `Gateway.Ftp`

- `Host`, `Port`, `Username`, `Password`
- `RemoteDirectory`, `StagingDirectory`
- `ConnectTimeoutMs`

## Структура output

- Папка запуска: `output/<dd_MM_yyyy_HHmmss>/`
- Файлы чанков: `output/<dd_MM_yyyy_HHmmss>/output-files/`
- CSV-отчет: `output/<dd_MM_yyyy_HHmmss>/run-report.csv`

## Быстрый старт

Запуск API:

```powershell
dotnet run --project .\backend\Unload.Api\Unload.Api.csproj
```

Запуск `run` через API:

```powershell
curl -X POST http://localhost:5000/api/runs -H "Content-Type: application/json" -d "{\"memberCodes\":[\"M\"]}"
```

Angular WebApp:

```powershell
cd .\web\webApp
npm start
```

Полная проверка backend и frontend перед передачей изменений:

```bash
./tools/verify.sh
```

Команда использует зафиксированные версии SDK и lockfile, запускает format/analyzers, dependency
audit, обе сборки, backend/frontend tests и проверку актуальности сгенерированного API client.

## Ограничения

- Одновременно может выполняться только один активный `run`.
- `run` и `extra` могут выполняться параллельно.
- `preset` конфликтует с `run` и `extra`.
- Состояние запусков и история задач сохраняются в JSON-файлы в `output/_state/` и восстанавливаются после рестарта backend.
- Незавершённые запуски при рестарте автоматически переводятся в `Cancelled`.
- Gateway реализован через FTP; конфигурируется в секции `Gateway.Ftp`.

## Где смотреть подробнее

- Единая карта документации: [docs/README.md](docs/README.md)
- Первый запуск и навигация по репозиторию: [docs/START_HERE.md](docs/START_HERE.md)
- Прикладная логика и быстрый вход: `README.md`
- Пользовательские действия и наблюдаемые состояния: [docs/USER_GUIDE.md](docs/USER_GUIDE.md)
- Детальная архитектура, диаграммы и naming rules: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Термины проекта: [docs/GLOSSARY.md](docs/GLOSSARY.md)
- План сопровождения: [docs/MAINTAINABILITY_PLAN.md](docs/MAINTAINABILITY_PLAN.md)
- API smoke/edge tests: `postman/unload-api.postman_collection.json`
