# Unload Architecture

## Solution modules

- `backend/Unload.Core`
  - Общие контракты и модели домена.
  - `Domain`: `RunRequest`, `ScriptDefinition`, `DatabaseRow`, `FileChunk`, `WrittenFile`, `RunnerEvent`, `RunnerStep`.
  - `Abstractions`: интерфейсы `IRunner`, `ICatalogService`, `IDatabaseClient`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

- `backend/Unload.Catalog`
  - Читает `configs/catalog.json`.
  - Находит SQL-файлы профиля в `scripts` по шаблону `<PROFILE>_*.sql`.
  - Проверяет код профиля (только `A-Z`, `0-9`, `_`) и защищает от выхода за границы директории скриптов.

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

- `backend/Unload.Api`
  - `RunRequestFactory` для создания входной заявки запуска.

- `console/Unload.Console`
  - Точка входа.
  - DI через `Microsoft.Extensions.DependencyInjection`.
  - Отображение событий в терминале через `Spectre.Console`.
  - Автоматически определяет корень workspace (ищет `configs/catalog.json` и папку `scripts` вверх по дереву директорий).
  - Если профили не переданы аргументами, интерактивно показывает профили по группам из `catalog.json` и позволяет выбрать выгрузку через мультиселект.

## Execution flow

1. Консоль формирует `RunRequest` из аргументов или интерактивного выбора профилей по группам.
2. `RunnerEngine` эмитит `RequestAccepted`.
3. `JsonCatalogService` возвращает скрипты для выбранных профилей.
4. В `Dataflow`-пайплайне:
   - `TransformBlock`: выполнить запрос в БД (заглушка) и получить строки.
   - `TransformBlock`: разбить строки на чанки по лимиту размера.
   - `TransformManyBlock`: распаковать наборы чанков.
   - `ActionBlock`: записать чанки в файлы.
5. На каждом шаге публикуется событие в MQ-заглушку и отправляется в консоль.
6. В конце эмитится `Completed` или `Failed`.

## Run

Из корня solution:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

С указанием профилей:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- QQW,QQE
```
