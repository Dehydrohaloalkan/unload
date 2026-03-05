# Unload

Платформа выгрузки данных, в которой запуск формируется в `Application`, выполняется в `Runner`, а статусы отдаются через API/SignalR и консольные клиенты.

## Что делает приложение

Приложение получает выборку (по участникам или target-кодам), находит подходящие SQL-скрипты, читает данные из БД потоково, режет результат на чанки, пишет файлы в `output`, публикует события и ведет статус запуска.

Ключевая идея: в системе одновременно может выполняться только один активный run.

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

## Состав проекта и роли модулей

### `backend/Unload.Core`

Базовые доменные модели и контракты:
- модели: `RunRequest`, `RunnerEvent`, `FileChunk`, `CatalogInfo` и др.;
- интерфейсы: `IRunner`, `ICatalogService`, `IDatabaseClient`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

Это слой, от которого зависят остальные backend-модули.

### `backend/Unload.Application`

Use-case слой запуска и состояния run:
- `IRunOrchestrator` / `RunOrchestrator` — вход в процесс запуска;
- `IRunRequestFactory` / `RunRequestFactory` — формирование `RunRequest`;
- `IRunCoordinator` / `InMemoryRunCoordinator` — контроль одного активного запуска и отмена;
- `IRunStateStore` / `InMemoryRunStateStore` — in-memory статусы run и мемберов;
- `AddUnloadRuntime(...)` — сборка всех зависимостей в DI.

### `backend/Unload.Catalog`

Читает `configs/catalog.json`, строит связи групп/мемберов/target-кодов и определяет набор SQL-скриптов для запуска.

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
- управляет шагами выполнения;
- распараллеливает чтение/запись через Dataflow;
- генерирует `RunnerEvent`;
- формирует `run-report.csv`.

### `backend/Unload.Api`

HTTP + SignalR транспорт:
- старт/остановка/чтение run-статусов;
- трансляция событий `status` и `run_status`;
- фоновый worker, который исполняет активированные run.

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
- `POST /api/runs/{correlationId}/stop` — запрос остановки активного run.
- `GET /api/runs` — список запусков.
- `GET /api/runs/active` — активный запуск (если есть).
- `GET /api/runs/{correlationId}` — статус конкретного run.
- SignalR hub: `/hubs/status`, события `status` и `run_status`.

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

WebConsole-клиент:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --members M
```

## Ограничения и важные замечания

- Очереди ожидания запусков нет: если run уже активен, новый старт получает `409 Conflict`.
- `IRunStateStore` и `IRunCoordinator` in-memory: после перезапуска процесса состояние не сохраняется.
- Текущие реализации БД/MQ являются заглушками и рассчитаны на development/testing сценарии.

## Где подробности

Детальная архитектура, диаграммы и форматы имен: `docs/ARCHITECTURE.md`.
