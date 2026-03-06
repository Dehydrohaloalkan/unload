# Unload Architecture

Краткое и прикладное описание проекта для быстрого старта: `README.md`.

## Solution modules

- `backend/Unload.Core`
  - Общие контракты и модели домена.
  - `Domain`: `RunRequest`, `ScriptDefinition`, `DatabaseRow`, `FileChunk`, `WrittenFile`, `RunnerEvent`, `RunnerStep`.
  - `Abstractions`: интерфейсы `IRunner`, `ICatalogService`, `IDatabaseClient`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

- `backend/Unload.Catalog`
  - Читает `configs/catalog.json`.
  - Понимает структуру `groups` + `members` (у `group` есть `folder` и `code`, у `member` есть `groups` и `file`) и строит target-код как `<GROUP_FOLDER>_<MEMBER_CODE>`.
  - Находит SQL-файлы в `scripts/<GROUP_FOLDER>` и отбирает скрипты target-выборки по формату имени `Y<member><group>_<type>_<codes>_<ext>.sql`.
  - Значения `folder`, `code`, `file` используются как есть, без `trim`/приведения регистра.
  - Проверки формата `group.folder`, `member.code`, `targetCode` отключены; защита от выхода за границы директории скриптов сохранена.
  - Для поддержки читаемости разнесено по файлам: `JsonCatalogService` (оркестрация), `CatalogScriptPathHelper` (правила имен и сортировки скриптов).
  - Построение `CatalogInfo` внутри `JsonCatalogService` декомпозировано на небольшие шаги (`BuildMemberGroupCodes`, `BuildTargets`, `BuildGroups`, `BuildMembers`) вместо длинных LINQ-цепочек.

- `backend/Unload.DataBase`
  - Заглушка БД: `StubDatabaseClient`.
  - `StubDatabaseClient` поддерживает конструктор `StubDatabaseClient(int timeout, string connectionString)`.
  - `connectionString` может быть:
    - plain-text строкой подключения;
    - строкой формата `dpapi:<base64>`, которая расшифровывается через Windows DPAPI (`CurrentUser`).
  - Контракт БД: `IDatabaseClient` с `IsConnected` и `GetDataReaderAsync(query, cancellationToken)`.
  - В раннер передается `DbDataReader`, строки читаются потоково.

- `backend/Unload.FileWriter`
  - Запись чанков в файлы с расширением из имени SQL/`member.file` и разделителем `|`.
  - Первая строка файла — служебный заголовок: `#|{type}|{fileName}|2XMDR|{yyyy-MM-dd}|{rowsCount}|{firstCodeDigit}`.
  - Начиная со второй строки пишутся данные из БД через `|`.
  - Пишет в `output/<dd_MM_yyyy_HHmmss>/output-files/`.
  - Формат имени файла: `{first3charsOfScript}{dayOfYear:D3}{chunkNumber:D2}.{ext}` (без `_`).

- `backend/Unload.MQ`
  - Заглушка MQ: `InMemoryMqPublisher`.
  - Сохраняет события раннера во внутреннюю очередь.

- `backend/Unload.Cryptography`
  - `Sha256RequestHasher` для формирования run hash.

- `backend/Unload.Runner`
  - `RunnerEngine` + `RunnerOptions`.
  - Выполняет пайплайн на `System.Threading.Tasks.Dataflow` с настраиваемыми worker-ами:
    - `MaxDegreeOfParallelism` — чтение SQL и формирование чанков;
    - `FileWriterDegreeOfParallelism` — запись чанков в файлы;
    - `QueuePublisherDegreeOfParallelism` — публикация событий в MQ/канал.
  - Шаги: resolve target-кодов -> очередь скриптов -> потоковое чтение `DbDataReader` и формирование чанков -> параллельная запись файлов -> последовательная публикация событий.
  - Значения по умолчанию в DI: `MaxDegreeOfParallelism = max(CPU/2, 1)`, `FileWriterDegreeOfParallelism = 4`, `QueuePublisherDegreeOfParallelism = 1`, `DataflowBoundedCapacity = 8`.
  - Не держит все строки скрипта в памяти: буфер ограничен текущим чанком.
  - После каждого шага создается `RunnerEvent`.
  - Для каждого записанного файла формирует CSV-отчет запуска `run-report.csv` в папке запуска с полями: `memberName,fileType,operation,outputFileName,rowsCount,mqStatus`.
  - Для каждого записанного файла формирует CSV-отчет запуска `run-report.csv` в папке запуска с полями: `memberName,fileType,operation,outputFileName,rowsCount,mqStatus,executionTimeMs`.
  - `operation` маппится из `firstCodeDigit`: `0 -> предоставление`, `2 -> замена`, остальные значения пишутся как число.
  - `mqStatus` фиксирует факт отправки события в MQ (`отправлен`/`не отправлен`), при ошибке MQ пайплайн продолжает выполнение.
  - События раннера в большинстве шагов ставятся в очередь публикации без ожидания результата MQ (fire-and-forget относительно шага пайплайна); ожидание подтверждения MQ выполняется только для `FileWritten`, так как этот статус попадает в `run-report.csv`.
  - `RunnerEventEmitter` использует токен запуска по умолчанию и позволяет точечно передать другой токен для отдельных шагов dataflow.
  - Внутренние детали разнесены: `RunnerEngine` (пайплайн), `RunnerEventEmitter` (публикация событий), `RunnerEngineGuard` (guard-валидации), `RunnerOutputDirectoryFactory` (создание output-папок), `RunnerEngineDataReader` (чтение колонок/строк из `DbDataReader`).

- `backend/Unload.Application`
  - Application-слой use-case запуска выгрузки.
  - Контракты и реализации orchestration: `IRunOrchestrator`, `IRunRequestFactory`, `IRunCoordinator`, `IRunStateStore`.
  - In-memory диспетчер запусков (один активный run без очереди ожидания) и store статусов, общий `RunStatusInfo`.
  - `IRunCoordinator` поддерживает остановку активного запуска (`TryCancel`) и выдает активацию вместе с токеном отмены конкретного run.
  - `RunStatusInfo` хранит статусы мемберов (`MemberStatuses`) отдельно от общего статуса запуска.
  - Общая DI-композиция через `AddUnloadRuntime(UnloadRuntimePaths, DatabaseRuntimeSettings)` для API и Console.
  - Настройки БД валидируются при старте (`TimeoutSeconds > 0`, непустой `ConnectionString`), fallback-значения не используются.

- `backend/Unload.Api`
  - ASP.NET Core API + SignalR.
  - Тонкий транспортный слой: HTTP/SignalR, без бизнес-оркестрации запуска.
  - HTTP-эндпоинты вынесены в MVC-контроллеры: `CatalogController` (`/api/catalog`, `/api/members`) и `RunsController` (`/api/runs*`).
  - Настройки БД читаются из секции `Database` (`TimeoutSeconds`, `ConnectionString`) в `appsettings.Development.json` / `appsettings.Production.json`; секция обязательна.
  - `GET /api/catalog` — отдает структуру каталога (группы, участники, target-выборки), где:
    - `group.name` отдается в формате `{имя (folder)}`;
    - `member.name` отдается в формате `{имя (Y{memberCode}{groupCode}*.ext)}`.
  - `GET /api/members` — отдает список мемберов для запуска (`code`, `name`, `targetCodes`) и, если есть активный запуск, текущий статус мембера (`activeRunCorrelationId`, `activeRunStatus`).
  - `POST /api/runs` — запускает выгрузку для выбранных мемберов (`memberCodes`) и возвращает `correlationId`.
  - Если запуск уже выполняется, `POST /api/runs` возвращает `409 Conflict` с `activeCorrelationId`.
  - `POST /api/runs/{correlationId}/stop` — останавливает активный запуск по `correlationId`.
  - `GET /api/runs` — список запусков и их статусы.
  - `GET /api/runs/active` — текущий активный запуск (если есть).
  - `GET /api/runs/{correlationId}` — статус конкретного запуска.
  - Запуски обрабатываются фоновым worker (`BackgroundService`) без очереди ожидания: одновременно выполняется только один запуск.
  - SignalR Hub: `/hubs/status`, подписка на конкретный запуск через `SubscribeRun(correlationId)`.
  - SignalR события:
    - `status` — события раннера активного запуска для всех подключенных клиентов;
    - `run_status` — обновления статуса запуска и мемберов для всех подключенных клиентов.
  - `Program` оставлен как точка конфигурации DI/маршрутизации (`AddControllers`, `MapControllers`), резолв путей вынесен в `ApiWorkspacePathResolver`.

- `console/Unload.Console`
  - Точка входа.
  - DI через `Microsoft.Extensions.DependencyInjection`.
  - Переиспользует тот же runtime/use-case слой (`Unload.Application`), что и API.
  - Настройки БД читаются из `appsettings.{Environment}.json` (переменные окружения `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`, по умолчанию `Production`); секция `Database` обязательна.
  - Запуск инициируется через `IRunOrchestrator` и тот же single-run диспетчер (`IRunCoordinator`), без очереди ожидания.
  - Отображение событий в терминале через `Spectre.Console`.
  - После завершения запуска выводит общее время выгрузки (`Total export time`, формат `hh:mm:ss.fff`).
  - Автоматически определяет корень workspace (ищет `configs/catalog.json` и папку `scripts` вверх по дереву директорий).
  - Если target-коды не переданы аргументами, интерактивно показывает target-выборки по группам/участникам через `ICatalogService.GetCatalogAsync()` из `backend/Unload.Catalog` и позволяет выбрать выгрузку через мультиселект.
  - Код разнесен по сущностям: `Program` (точка входа), `WorkspacePathResolver` (пути runtime), `TargetCodePrompter` (интерактивный выбор на основе `CatalogInfo`).

- `console/Unload.WebConsole`
  - Консольный клиент API (замена frontend для тестов).
  - Интерфейс построен на `Spectre.Console` (панель статуса + live-лента событий).
  - Работает через HTTP (`/api/runs`, `/api/runs/active`, `/api/runs/{id}`) и SignalR (`/hubs/status`).
  - Перед стартом проверяет `GET /api/runs/active`; если уже есть активный run, новый запуск из WebConsole блокируется, клиент переключается в режим наблюдения.
  - Умеет стартовать запуск по `memberCodes`, обрабатывать `409 Conflict` при гонке состояний, останавливать активный запуск и подключаться к live-статусам.
  - Показывает отдельную таблицу статусов мемберов (pending/running/completed/failed/cancelled).
  - В live-режиме показывает индикаторы ожидания (спиннер в статусе и плейсхолдерах таблиц) пока не пришли события/статусы.
  - Live-таблицы ограничены по размеру: показывают только последние события и верхние строки мемберов с обрезкой длинных сообщений, чтобы интерфейс помещался в экран.
  - После завершения run live-рендер очищается и выводится отдельный финальный snapshot (`Run Finished`, `Final Members`, `Final Events`), чтобы исключить визуальную путаницу со «старой» динамической таблицей.
  - Ожидание завершения run в клиенте реализовано через встроенный `PeriodicTimer` (.NET), без ручного цикла `Task.Delay`.
  - Если `--members` не передан, показывает интерактивный multi-select мемберов из `GET /api/members`; пустой выбор включает режим наблюдения за активной выгрузкой.

## Module diagram

```mermaid
flowchart LR
    Console["console/Unload.Console"] --> App["backend/Unload.Application"]
    Api["backend/Unload.Api"] --> App

    App --> Core["backend/Unload.Core"]
    App --> Runner["backend/Unload.Runner"]
    App --> Catalog["backend/Unload.Catalog"]
    App --> Db["backend/Unload.DataBase"]
    App --> Writer["backend/Unload.FileWriter"]
    App --> Mq["backend/Unload.MQ"]
    App --> Crypto["backend/Unload.Cryptography"]

    Runner --> Core
    Catalog --> Core
    Db --> Core
    Writer --> Core
    Mq --> Core
    Crypto --> Core
```

## Execution flow

1. Консоль или API вызывает `IRunOrchestrator` из `Unload.Application` для старта запуска.
2. `IRunOrchestrator` валидирует target-коды (полученные из выбранных мемберов), формирует `RunRequest`, резервирует единственный слот выполнения и сохраняет начальный статус.
3. `RunProcessingBackgroundService` в API принимает активированный запуск и запускает `RunnerEngine`.
4. `RunnerEngine` эмитит `RequestAccepted`.
5. `JsonCatalogService` возвращает скрипты для выбранных target-кодов.
6. Для каждого скрипта:
   - worker чтения получает `DbDataReader` из БД и читает строки потоково;
   - worker чтения собирает текущий чанк до лимита размера и отправляет в очередь чанков;
   - worker записи получает чанк из очереди, создает файл и пишет данные;
   - если скрипт вернул `0` строк, выходной файл не создается и события файла (`ChunkCreated`/`FileWritten`) не публикуются;
   - worker публикации отправляет события в MQ и клиентский канал (степень параллелизма задается `QueuePublisherDegreeOfParallelism`, по умолчанию 1).
7. На каждом шаге публикуется событие в MQ-заглушку и обновляется статус запуска/мембера.
8. В конце эмитится `Completed` или `Failed`; при остановке пользователем статус становится `Cancelled`.

## Форматы имен и выходных файлов

### Формат SQL-скрипта

- `Y<memberCode><groupCode>_<type>_<codes>_<extension>.sql`
- `Y` — константный префикс.
- `<memberCode>` — код мембера (2-й символ имени).
- `<groupCode>` — код группы из `catalog.json` (3-й символ имени).
- `<type>` — тип выгрузки, используется в заголовке output-файла.
- `<codes>` — один или несколько числовых кодов, разделенных `_` (например, `01` или `01_2_15`).
- `<extension>` — расширение output-файла без точки (должно совпадать с `member.file` без `.`).

### Формат выходного файла

- Имя: `{first3charsOfScript}{dayOfYear:D3}{chunkNumber:D2}.{extension}`
- При коллизии имени (например, параллельная запись двух файлов с одинаковым шаблоном) автоматически добавляется суффикс `_{NN}`: `{first3charsOfScript}{dayOfYear:D3}{chunkNumber:D2}_{NN}.{extension}`.
- Первая строка:
  - `#|{type}|{outputFileName}|2XMDR|{yyyy-MM-dd}|{rowsCountWithoutHeader}|{firstDigitFromCodes}`
- Остальные строки:
  - данные из БД через `|`.
  - символ `|` не экранируется обратным слешом.

### Структура output и CSV-отчета

- Папка запуска: `output/<dd_MM_yyyy_HHmmss>/`
- Выходные файлы чанков: `output/<dd_MM_yyyy_HHmmss>/output-files/`
- CSV-отчет запуска: `output/<dd_MM_yyyy_HHmmss>/run-report.csv`
- Формат CSV:
  - `memberName,fileType,operation,outputFileName,rowsCount,mqStatus,executionTimeMs`
  - `mqStatus`: `отправлен` / `не отправлен`
  - `executionTimeMs`: время записи конкретного output-файла (чанка) в миллисекундах.

## Run sequence diagram

```mermaid
sequenceDiagram
    participant Client as Console/API Client
    participant Transport as API/Console Transport
    participant App as Unload.Application
    participant Coordinator as IRunCoordinator (single active run)
    participant Worker as BackgroundService
    participant Runner as RunnerEngine
    participant Infra as Catalog/DB/FileWriter/MQ
    participant State as IRunStateStore
    participant SignalR as RunStatusHub

    Client->>Transport: start run(targetCodes)
    Transport->>App: IRunOrchestrator.StartRun(...)
    App->>App: normalize + validate target codes
    App->>Coordinator: TryActivate(RunRequest)
    App->>State: SetStarted(...)
    Transport-->>Client: correlationId

    Worker->>Coordinator: ReadActivationsAsync()
    Worker->>State: SetRunning(correlationId)
    Worker->>Runner: RunAsync(request)
    Runner->>Infra: catalog/db/file/mq operations
    Runner-->>Worker: RunnerEvent stream
    Worker->>State: ApplyEvent(event)
    Worker->>SignalR: status + run_status
```

## Code documentation

- Во всех ключевых классах и методах backend/console добавлены XML-комментарии.
- В `backend/Unload.Application` дополнены XML-комментарии для `IRunCoordinator` и `InMemoryRunCoordinator`.
- В `console/Unload.WebConsole` добавлены XML-комментарии для типов `AppOptions`, `RunApiClient`, `RunDashboardBuilder`, `UiState`, `WebConsoleRunner` и DTO/enum-моделей из `Models.cs`.
- `WebConsoleRunner` декомпозирован на небольшие шаги (`ConnectToHubAsync`, `ResolveTrackedRunAsync`, `RenderLiveDashboardAsync`, `RefreshFinalStateAsync`, `RenderFinalSummary`) для упрощения чтения и сопровождения.
- `RunDashboardBuilder` избавлен от дублирования между live/final режимами через общие builder-методы (`BuildLayout`, `BuildInfoPanel`, `BuildMembersTable`, `BuildEventsTable`) и вынесенные мапперы цветов.
- Комментарии описывают:
  - где используется компонент;
  - как работает метод или класс;
  - входные параметры (`param`) и выход (`returns`) для методов.
- Этот формат документации следует поддерживать при добавлении новых публичных и приватных методов core runtime.
- Для run-моделей рекомендуется поддерживать синхронность API-контрактов: если меняется payload (`memberCodes`, `MemberStatuses`, `stop` endpoint), обновлять docs и WebConsole одновременно.

## API run

Запуск API из корня solution:

```powershell
dotnet run --project .\backend\Unload.Api\Unload.Api.csproj
```

Пример запуска выгрузки:

```powershell
curl -X POST http://localhost:5000/api/runs -H "Content-Type: application/json" -d "{\"memberCodes\":[\"M\"]}"
```

Получение списка доступных мемберов:

```powershell
curl http://localhost:5000/api/members
```

Проверка статусов запусков:
Остановка активной выгрузки:

```powershell
curl -X POST http://localhost:5000/api/runs/{correlationId}/stop
```


```powershell
curl http://localhost:5000/api/runs
```

Проверка активного запуска:

```powershell
curl http://localhost:5000/api/runs/active
```

Подписка клиента SignalR:

- Подключиться к `/hubs/status`.
- Вызвать `SubscribeRun(correlationId)` (опционально для обратной совместимости).
- Слушать событие `status` с payload `RunnerEvent` (событие отправляется всем подключенным клиентам).
- Для общей ленты запусков слушать событие `run_status` с payload `RunStatusInfo`.

## Run

Из корня solution:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

С указанием target-кодов:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- QQW,QQE
```

## WebConsole

Запуск web-клиента для API:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --members M
```

Режим наблюдения за уже активной выгрузкой:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000
```
