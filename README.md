# Unload

Платформа выгрузки данных, в которой запуск формируется в `Application`, выполняется в `Runner`, а статусы отдаются через API/SignalR и консольные клиенты.

## Что делает приложение

Приложение получает выборку (по участникам или target-кодам), находит подходящие SQL-скрипты, читает данные из БД потоково, режет результат на чанки, пишет файлы в `output`, публикует события и ведет статус запуска.

Ключевая идея: в системе одновременно может выполняться только один активный run.

Дополнительно появился отдельный поток задач:
- `preset`-этап (скрипты из `scripts/preset`);
- `extra`-этап (скрипты из корня `scripts`, без подпапок).

## Общий поток выполнения

1. Клиент (`Unload.Api` или `Unload.Console`) инициирует запуск.
2. `Unload.Application`:
   - валидирует входные данные;
   - создает `RunRequest` с `correlationId`;
   - резервирует слот единственного активного запуска;
   - сохраняет стартовый статус.
3. Фоновый воркер API (`RunProcessingBackgroundService`) получает активацию run.
4. `Unload.Runner` выполняет pipeline:
   - резолвит скрипты по target-кодам;
   - читает данные из БД;
   - формирует чанки;
   - пишет файлы;
   - отправляет события.
5. Статусы обновляются в `IRunStateStore`, клиенты получают обновления через REST и SignalR.
6. Run завершается статусом `Completed`, `Failed` или `Cancelled`, слот освобождается.
7. Для preset-потока API после `PresetGate.StartHour:StartMinute` раз в минуту выполняет probe SQL:
   - пока probe возвращает `0` — запуск preset закрыт;
   - когда probe возвращает `1` — клиент получает `preset_state`, можно запускать preset;
   - после успешного preset разблокируются обычный run и extra-задача;
   - обычный run и extra-задача доступны только в окне `StartHour:StartMinute`-`23:59`;
   - после смены даты требуется повторное выполнение preset.

## Состав проекта и роли модулей

### `backend/Unload.Core`

Базовые доменные модели и контракты:
- модели: `RunRequest`, `RunnerEvent`, `FileChunk`, `CatalogInfo` и др.;
- интерфейсы: `IRunner`, `ICatalogService`, `IDatabaseClient`, `IDatabaseClientFactory`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

Это слой, от которого зависят остальные backend-модули.

### `backend/Unload.Application`

Use-case слой запуска и состояния run:
- `IRunOrchestrator` / `RunOrchestrator` — вход в процесс запуска;
- `IRunRequestFactory` / `RunRequestFactory` — формирование `RunRequest`;
- `IRunCoordinator` / `InMemoryRunCoordinator` — контроль одного активного запуска и отмена;
- `IRunStateStore` / `InMemoryRunStateStore` — in-memory статусы run и мемберов;
- `AddUnloadRuntime(...)` — сборка всех зависимостей в DI.

### `backend/Unload.Catalog`

Читает `configs/catalog.json`, строит связи групп/мемберов/target-кодов и определяет набор SQL-скриптов. Опционально: `bigScripts` — target-выборки (memberId+groupId), чьи скрипты выполняются в n-1 потоках (1 поток всегда для легких).

### `backend/Unload.DataBase`

Клиент доступа к БД (сейчас заглушка `StubDatabaseClient`), возвращает `DbDataReader` для потокового чтения.

### `backend/Unload.FileWriter`

Пишет чанки в выходные файлы c разделителем `|`, формирует служебный заголовок файла и соблюдает naming convention.

### `backend/Unload.MQ`

Публикация событий в MQ (сейчас in-memory заглушка `InMemoryMqPublisher`).

### `backend/Unload.Cryptography`

Хеширование параметров запроса (`Sha256RequestHasher`).

### `backend/Unload.Runner`

Исполнитель пайплайна выгрузки:
- N worker-потоков (n-1 для больших скриптов из `bigScripts`, 1 для легких), 1 клиент БД на поток;
- потоковое чтение из БД и прямая запись чанков worker-потоком;
- target-коды в очередях big/light, скрипты по `firstCodeDigit`, единый MQ;
- генерирует `RunnerEvent`, формирует `run-report.csv`.

### `backend/Unload.Api`

HTTP + SignalR транспорт:
- старт/остановка/чтение run-статусов;
- трансляция событий `status` и `run_status`;
- фоновый worker, который исполняет активированные run.
- единый контракт ошибок через `ProblemDetails` (включая `errorCode`, `traceId`, `correlationId`);

### `console/Unload.Console`

Локальный консольный запуск runtime (можно интерактивно выбрать target-коды, смотреть события в терминале).

### `console/Unload.WebConsole`

Консольный API-клиент для наблюдения/запуска через HTTP + SignalR.

## Сервисы `Application` простыми словами

- `RunOrchestrator` — "дирижер": проверяет вход и запускает процесс.
- `RunRequestFactory` — "генератор заявки": создает `RunRequest`.
- `RunCoordinator` — "турникет": пускает только один run одновременно.
- `RunStateStore` — "табло": хранит текущее состояние запуска и участников.

## API (основное)

- `POST /api/runs` — старт run по `memberCodes`.
- `GET /api/runs/preset/state` — текущее состояние preset-гейта.
- `POST /api/runs/preset` — запуск preset-задачи (`scripts/preset`).
- `POST /api/runs/extra` — запуск extra-задачи (`scripts/*.sql`, без подпапок), группировка `NrBank -> LineFile` в файлы.
- `POST /api/runs/{correlationId}/stop` — запрос остановки активного run.
- `GET /api/runs` — список запусков.
- `GET /api/runs/active` — активный запуск (если есть).
- `GET /api/runs/{correlationId}` — статус конкретного run.
- SignalR hub: `/hubs/status`, события `status` и `run_status`.

Формат ошибок API:
- при бизнес-ошибках и валидации возвращается `application/problem+json`;
- общий payload: `type`, `title`, `status`, `detail`, `instance`, `errorCode`, `traceId`, `correlationId`.

## Структура выходных данных

- Папка запуска: `output/<dd_MM_yyyy_HHmmss>/`
- Файлы: `output/<dd_MM_yyyy_HHmmss>/output-files/`
- CSV-отчет: `output/<dd_MM_yyyy_HHmmss>/run-report.csv`

## Быстрый старт

Из корня репозитория:

```powershell
dotnet run --project .\backend\Unload.Api\Unload.Api.csproj
```

Пример запуска выгрузки:

```powershell
curl -X POST http://localhost:5000/api/runs -H "Content-Type: application/json" -d "{\"memberCodes\":[\"M\"]}"
```

Консольный запуск runtime:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

Локальный запуск preset-задачи:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- --preset
```

Локальный запуск extra-задачи:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- --extra
```

WebConsole-клиент:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --members M
```

## Postman tests

Ready-to-use Postman collection with smoke and edge-case checks:

- `postman/unload-api.postman_collection.json`

What is covered:

- smoke checks for `members`, `preset state`, `active run`;
- main run lifecycle (`start`, duplicate start conflict, status by id, stop, list);
- `preset` and `extra` endpoints (success/conflict branches);
- edge cases (`empty memberCodes`, unknown member, unknown correlation id).

How to run:

1. Import collection from `postman/unload-api.postman_collection.json`.
2. Verify collection variable `baseUrl` (default `http://localhost:5000`).
3. Run all requests via Collection Runner.
4. Inspect failed assertions in the `Tests` tab.

Preset-задача через WebConsole:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --preset
```

Extra-задача через WebConsole:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --extra
```

## Конфигурация

### `configs/catalog.json`

- `bigScripts` (опционально): список `{memberId, groupId}` — target-выборки, чьи скрипты считаются «большими». Выполняются в n-1 потоках; 1 поток всегда для легких скриптов.

### `appsettings` (Runner)

- `WorkerCount`: количество worker-потоков (по умолчанию 4).

### `appsettings` (PresetGate)

- `Enabled`: включение/выключение механизма preset-гейта.
- `StartHour` / `StartMinute`: локальное время старта проверки готовности.
- `PollIntervalSeconds`: интервал probe-проверки БД.
- `ProbeSql`: SQL probe-запрос (должен возвращать `0` или `1` в первой колонке первой строки).
- Main/Extra run policy: запуск разрешен только после `StartHour:StartMinute`, до `23:59` и только после успешного preset текущего дня.

## Ограничения и важные замечания

- Очереди ожидания запусков нет: если run уже активен, новый старт получает `409 Conflict`.
- `IRunStateStore` и `IRunCoordinator` in-memory: после перезапуска процесса состояние не сохраняется.
- Текущие реализации БД/MQ являются заглушками и рассчитаны на development/testing сценарии.
- Остановка run двухфазная: `CancellationRequested` -> `Cancelled` (финальный terminal-статус после остановки worker).

## Где подробности

Детальная архитектура, диаграммы и форматы имен: `docs/ARCHITECTURE.md`.
