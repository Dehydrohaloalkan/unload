# Unload Architecture

## Solution modules

- `backend/Unload.Core`
  - Общие контракты и модели домена.
  - `Domain`: `RunRequest`, `ScriptDefinition`, `DatabaseRow`, `FileChunk`, `WrittenFile`, `RunnerEvent`, `RunnerStep`.
  - `Abstractions`: интерфейсы `IRunner`, `ICatalogService`, `IDatabaseClient`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

- `backend/Unload.Catalog`
  - Читает `configs/catalog.json`.
  - Понимает структуру `groups` + `members` + `profiles` и строит код профиля как `<GROUP_FOLDER>_<MEMBER_CODE>`.
  - Находит SQL-файлы в `scripts/<GROUP_FOLDER>` и отбирает скрипты профиля по второй букве имени файла (`member.code`).
  - Валидирует `group.folder`, `member.code`, `profileCode` и защищает от выхода за границы директории скриптов.

- `backend/Unload.DataBase`
  - Заглушка БД: `StubDatabaseClient`.
  - Контракт БД: `IDatabaseClient` с `IsConnected` и `GetDataReaderAsync(query, cancellationToken)`.
  - В раннер передается `DbDataReader`, строки читаются потоково.

- `backend/Unload.FileWriter`
  - Запись чанков в `txt` с разделителем `|`.
  - Первая строка файла — заголовок (имена колонок через `|`), далее строки данных.
  - Пишет в `output/<runHash>/<profile>/<script>_<chunk>.txt`.

- `backend/Unload.MQ`
  - Заглушка MQ: `InMemoryMqPublisher`.
  - Сохраняет события раннера во внутреннюю очередь.

- `backend/Unload.Cryptography`
  - `Sha256RequestHasher` для формирования run hash.

- `backend/Unload.Runner`
  - `RunnerEngine` + `RunnerOptions`.
  - Параллельно выполняет скрипты (`MaxDegreeOfParallelism`) и читает `DbDataReader` потоково.
  - Шаги: resolve профилей -> запуск запроса -> on-the-fly разбиение на чанки до 10MB -> запись файлов.
  - Не держит все строки скрипта в памяти: буфер ограничен текущим чанком.
  - После каждого шага создается `RunnerEvent`.
  - Диагностика: пишет полный лог событий и метрики длительности шагов в CSV через `IRunDiagnosticsSink`.

- `backend/Unload.Api`
  - ASP.NET Core API + SignalR.
  - `GET /api/catalog` — отдает структуру каталога (группы, участники, профили).
  - `POST /api/runs` — ставит запуск в очередь и возвращает `correlationId`.
  - `GET /api/runs` — список запусков и их статусы.
  - `GET /api/runs/{correlationId}` — статус конкретного запуска.
  - Запуски обрабатываются фоновым worker (`BackgroundService`) из общей in-memory очереди.
  - SignalR Hub: `/hubs/status`, подписка на конкретный запуск через `SubscribeRun(correlationId)`.
  - SignalR события:
    - `status` — события раннера конкретного запуска;
    - `run_status` — обновления статуса запуска для всех подключенных клиентов.

- `console/Unload.Console`
  - Точка входа.
  - DI через `Microsoft.Extensions.DependencyInjection`.
  - Отображение событий в терминале через `Spectre.Console`.
  - Автоматически определяет корень workspace (ищет `configs/catalog.json` и папку `scripts` вверх по дереву директорий).
  - Если профили не переданы аргументами, интерактивно показывает профили по группам/участникам из `catalog.json` и позволяет выбрать выгрузку через мультиселект.

## Execution flow

1. Консоль или API формирует `RunRequest` из профилей.
2. `RunnerEngine` эмитит `RequestAccepted`.
3. `JsonCatalogService` возвращает скрипты для выбранных профилей.
4. Worker извлекает задачу из очереди и запускает `RunnerEngine`.
5. Для каждого скрипта:
   - получить `DbDataReader` из БД;
   - читать строки потоково;
   - собирать текущий чанк до лимита размера;
   - записывать чанк в файл и продолжать чтение.
6. На каждом шаге публикуется событие в MQ-заглушку, сохраняется диагностика и обновляется статус запуска.
7. В конце эмитится `Completed` или `Failed`.

## Observability

- Базовая папка диагностики по умолчанию: `observability` в корне workspace.
- Можно переопределить через переменную окружения `UNLOAD_DIAGNOSTICS_DIR`.
- Для каждого запуска (`correlationId`) создается отдельная папка:
  - `events.csv`: полный лог событий (`RunnerEvent`).
  - `metrics.csv`: длительность шагов (`duration_ms`) и итог (`outcome`).
- Формат CSV безопасно экранируется, чтобы избежать CSV formula injection при открытии в табличных редакторах.

## API run

Запуск API из корня solution:

```powershell
dotnet run --project .\backend\Unload.Api\Unload.Api.csproj
```

Пример запуска выгрузки:

```powershell
curl -X POST http://localhost:5000/api/runs -H "Content-Type: application/json" -d "{\"profileCodes\":[\"YSB_M\"]}"
```

Проверка статусов запусков:

```powershell
curl http://localhost:5000/api/runs
```

Подписка клиента SignalR:

- Подключиться к `/hubs/status`.
- Вызвать `SubscribeRun(correlationId)`.
- Слушать событие `status` с payload `RunnerEvent`.
- Для общей ленты запусков слушать событие `run_status` с payload `RunStatusInfo`.

## Run

Из корня solution:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

С указанием профилей:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- QQW,QQE
```
