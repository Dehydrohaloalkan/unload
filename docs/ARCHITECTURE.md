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
  - Для каждого скрипта генерирует поток строк (`IAsyncEnumerable`) как будто это ответ из базы.

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
  - Реализует pipeline на `System.Threading.Tasks.Dataflow`.
  - Шаги: resolve профилей -> запуск запроса -> разбиение на чанки до 10MB -> запись файлов.
  - После каждого шага создается `RunnerEvent`.
  - Диагностика: пишет полный лог событий и метрики длительности шагов в CSV через `IRunDiagnosticsSink`.

- `backend/Unload.Api`
  - ASP.NET Core API + SignalR.
  - `GET /api/catalog` — отдает структуру каталога (группы, участники, профили).
  - `POST /api/runs` — запускает выгрузку и возвращает `correlationId`.
  - SignalR Hub: `/hubs/status`, подписка на конкретный запуск через `SubscribeRun(correlationId)`, события: `status`.

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
4. В `Dataflow`-пайплайне:
   - `TransformBlock`: выполнить запрос в БД (заглушка) и получить строки.
   - `TransformBlock`: разбить строки на чанки по лимиту размера.
   - `TransformManyBlock`: распаковать наборы чанков.
   - `ActionBlock`: записать чанки в файлы.
5. На каждом шаге публикуется событие в MQ-заглушку и отправляется в консоль.
6. В конце эмитится `Completed` или `Failed`.

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

Подписка клиента SignalR:

- Подключиться к `/hubs/status`.
- Вызвать `SubscribeRun(correlationId)`.
- Слушать событие `status` с payload `RunnerEvent`.

## Run

Из корня solution:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

С указанием профилей:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- QQW,QQE
```
