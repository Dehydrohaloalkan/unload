# Карта кода Unload по типам логики

Этот справочник отвечает на вопрос: **где искать и куда класть изменение определённого типа**.
Он дополняет [архитектурное описание](ARCHITECTURE.md), но группирует код не по пользовательским
сценариям, а по его роли: чистые правила, прикладная оркестрация, API, инфраструктура и UI.

## 1. Как различать слои

| Тип кода | Как его узнать | Где он находится |
|---|---|---|
| Чистая логика | Получает значения, возвращает новое значение; не обращается к HTTP, БД, файлам, SignalR или глобальному состоянию | `Unload.Core`, проекторы `Unload.Store`, `state/utils` во frontend |
| Бизнес-правила с состоянием | Принимает бизнес-решение, но хранит состояние или зависит от времени | `Unload.Tasks` |
| Прикладная оркестрация | Выстраивает шаги сценария и вызывает порты/адаптеры | `Unload.Tasks.*`, частично hosted services |
| Порт | Интерфейс, через который логика просит внешний мир выполнить операцию | `Unload.Core/Abstractions` |
| Инфраструктурный адаптер | Реально читает БД/JSON, пишет файл, ходит в FTP | `Unload.DataBase`, `Unload.Store`, `Unload.FileWriter`, `Unload.Gateway`, `Unload.Catalog` |
| Транспортная обвязка | Преобразует HTTP/SignalR-запрос в вызов приложения и результат обратно в контракт | `Unload.Api/Controllers`, `Models`, `Hubs`, `ErrorHandling` |
| Composition root | Создаёт приложение: читает конфигурацию и связывает реализации с интерфейсами | `Unload.Bootstrapper`, `Unload.Api/Program.cs` |
| UI | Хранит клиентское состояние, вызывает API и отображает представление | `web/webApp/src/app` |

Главная зависимость направлена примерно так:

```text
Angular UI -> HTTP / SignalR -> Unload.Api
                                  |
                                  v
                            Unload.Tasks.*
                             /     |      \
                            v      v       v
                     Unload.Core  Store  инфраструктурные адаптеры
```

`Unload.Core` не знает об ASP.NET Core, Angular, FTP, JSON-персистентности и конкретной БД.
Внешние слои знают о внутренних контрактах, а не наоборот.

## 2. Чистая и почти чистая логика backend

### Модели и контракты предметной области

`backend/Unload.Core/Domain/` содержит значения, которыми обмениваются части пайплайна:

- `RunRequest`, `ScriptDefinition`, `DatabaseRow` — вход и данные исполнения;
- `FileChunk`, `WrittenFile` — результат нарезки и записи;
- `RunnerEvent`, `RunnerStep`, `RunnerFailureInfo` — события, стадии и ошибки;
- `SenderMqContracts` — события готовности batch и sender-feedback;
- `CatalogInfo` — представление каталога.

Это в основном структуры данных, а не сценарии исполнения. Общие сообщения и их очистка находятся
в `backend/Unload.Core/RunnerFailureMessages.cs`.

### Форматирование значений

`backend/Unload.Core/Formatting/` содержит правила текстового формата:

- `PipeDelimitedFormatter` — преобразование строки данных в pipe-delimited представление;
- `OutputFormatConstants` и `OutputTextEncoding` — параметры формата и кодировки;
- `OutputFileNaming` — правила имён, но этот класс **не полностью чистый**: операции открытия
  уникального файла касаются файловой системы.

### Проекции состояния запуска

В `backend/Unload.Store/` рядом с персистентностью лежит отдельная группа почти чистых функций
`старое состояние + событие -> новое состояние`:

- `RunStateProjector.cs` — центральное применение runner/sender событий;
- `RunMemberProjector.cs` — статусы участников;
- `RunArtifactProjector.cs` — список созданных файлов;
- `GatewayFeedbackProjector.cs` — состояния gateway batch;
- `RunCompletionPolicy.cs` — правило финального `Completed`/`Failed`;
- `RunTaskCodeResolver.cs` — определение кода задачи по correlation id.

Они не читают snapshots и не публикуют события. Доступ к диску начинается в `RunStatePersistence`,
`JsonFileStore`, `RunStateStore` и `TaskExecutionHistoryStore`.

### Небольшие правила main-run

В `backend/Unload.Tasks.MainUnload/Services/` есть локальные вычислительные правила, например
`RunnerEngineGuard`. Остальные классы этой папки надо оценивать отдельно: `MainUnloadEngine`,
`RunnerEngineDataReader`, `RunnerOutputDirectoryFactory`, `RunReportCsvWriter` и
`SenderBatchBuilder` уже работают с состоянием, БД или файловой системой.

## 3. Бизнес-логика и прикладные сценарии backend

### Общие правила задач

`backend/Unload.Tasks/` — главное место бизнес-правил запуска:

- `UnloadTask.cs` задаёт декларацию задачи: зависимости, конфликты, окно и deferred-режим;
- `TaskWorkflow.cs` проверяет правила и является единой точкой запуска задачи;
- `DailyWindowPolicy.cs` решает, доступны ли `probe`, `preset`, `run` и `extra` сегодня;
- `PresetCompletionRecovery.cs` восстанавливает дневное состояние после рестарта;
- `WorkflowQueryService.cs` собирает dashboard/history-представления для клиента;
- `RunActivationChannel.cs` и `ExtraActivationChannel.cs` держат single-active активации;
- `TaskCodes`, `TaskLaunchRequest`, `TaskExecutionResult`, `TaskLaunchException` — общий язык
  прикладного слоя.

`DailyWindowPolicy` — бизнес-логика, но не чистая функция: объект хранит состояние и использует
`TimeProvider`. Channels — технический механизм исполнения, а не бизнес-правило.

### Конкретные сценарии

| Сценарий | Вход и правила | Исполнение |
|---|---|---|
| `probe` | `Unload.Tasks.Preset/ProbeTask.cs` | Один запрос готовности через `IDatabaseClient` |
| `preset` | `Unload.Tasks.Preset/PresetTask.cs` | `PresetScriptExecutor.cs` исполняет SQL из `scripts/preset` |
| `run` | `Unload.Tasks.MainUnload/MainUnloadTask.cs` | `Services/MainUnloadEngine.cs` и его вспомогательные классы |
| `extra` | `Unload.Tasks.ExtraUnload/ExtraUnloadTask.cs` | `ExtraUnloadEngine`, `ExtraScriptExecutor`, `ExtraOutputWriter` |

Класс `*Task` отвечает за границу сценария и его результат. `*Engine`/`*Executor` содержит сам
алгоритм исполнения, но обычно уже вызывает внешние порты, поэтому это прикладная логика, а не
чистая функция.

## 4. Порты и инфраструктура backend

### Порты

Интерфейсы внешнего мира находятся в `backend/Unload.Core/Abstractions/`:

- каталог — `ICatalogService`;
- БД — `IDatabaseClient`, `IDatabaseClientFactory`, `IDatabaseCredentialStore`;
- файлы — `IFileChunkWriter`;
- gateway — `IGatewayPublisher`, `IGatewayBatchSource`,
  `IGatewaySenderFeedbackSource`, `IGatewaySenderFeedbackConsumer`.

Если новый алгоритм можно выразить через такой интерфейс, его не нужно связывать напрямую с
конкретным FTP-клиентом, JSON-файлом или провайдером БД.

### Реализации портов и прочий I/O

| Проект | Внешний ресурс | Ключевые места |
|---|---|---|
| `Unload.Catalog` | `configs/catalog.json`, дерево `scripts/` | `JsonCatalogService`, `CatalogScriptPathHelper` |
| `Unload.DataBase` | подключение и запросы к БД | `DatabaseClientFactory`, `DatabaseCredentialStore`, `StubDatabaseClient` |
| `Unload.FileWriter` | main output-файлы | `PipeSeparatedFileChunkWriter` |
| `Unload.Gateway` | FTP, staging и ручная загрузка | `FtpGatewayPublisher`, `FtpGatewayBackgroundService`, `GatewayUploadService` |
| `Unload.Store` | in-memory state и JSON snapshots | `RunStateStore`, `TaskExecutionHistoryStore`, `RunStatePersistence`, `JsonFileStore` |

`RequeueService` и `GatewaySenderFeedbackConsumer` также находятся в `Unload.Store`: первый
координирует повторную отправку сохранённых результатов, второй принимает feedback. Это не чистые
проекторы, хотя лежат рядом с ними.

## 5. API-обвязка и фоновые процессы

### HTTP

`backend/Unload.Api/Controllers/` — тонкая граница HTTP:

- `RunLaunchController` — запуск/остановка `run`, `preset`, `extra`;
- `RunStatusController` — текущие статусы и файлы запуска;
- `RunHistoryController` — dashboard и история;
- `GatewayRequeueController` — повторная отправка;
- `CatalogController`, `DatabaseController`, `SystemController` — каталог, подключение к БД и
  системные операции.

HTTP request/response DTO находятся в `backend/Unload.Api/Models/`. Преобразование исключений в
Problem Details — в `backend/Unload.Api/ErrorHandling/`. Контроллер не должен становиться местом
нового бизнес-правила: он валидирует транспортный контракт, вызывает прикладной сервис и формирует
HTTP-ответ.

### SignalR

- `Hubs/RunStatusHub.cs` — endpoint хаба;
- `Hubs/RunStatusHubContract.cs` — имена событий;
- `Services/RunStatusLivePublisher.cs` — публикация live-снимков.

### Hosted services

`backend/Unload.Api/Services/` содержит процессы, жизненным циклом которых управляет host:

- `MainUnloadHostedService` и `ExtraUnloadHostedService` читают activation channels и запускают
  соответствующие engines;
- `ProbeSchedulerHostedService` отвечает за расписание и polling, но само решение о доступности
  остаётся в `DailyWindowPolicy`;
- `SenderFeedbackProjectionBackgroundService` переносит feedback в состояние запуска;
- `HistoryRetentionBackgroundService` запускает периодическую очистку;
- `OutputFilesService` обслуживает файловые операции для API.

Это не просто HTTP-обвязка и не место для переиспользуемой чистой логики: это runtime-координация.

### Сборка приложения

- `backend/Unload.Api/Program.cs` настраивает ASP.NET Core pipeline, middleware, endpoints и host;
- `backend/Unload.Bootstrapper/DependencyInjection/ServiceCollectionExtensions.cs` связывает
  интерфейсы с реализациями;
- остальные классы `Unload.Bootstrapper` читают и нормализуют конфигурацию и runtime-пути;
- `backend/Unload.Api/appsettings*.json` хранит настройки среды, но не секреты.

## 6. Frontend по слоям

### Чистые клиентские преобразования

`web/webApp/src/app/state/utils/` — первое место для вычислений вида `API model -> view model`:

- `history-projection.util.ts`, `run-history-projection.util.ts`,
  `extra-history-projection.util.ts`, `gateway-history-projection.util.ts`;
- `history-selection.util.ts`, `member-projections.util.ts`, `workflow-view-state.util.ts`;
- `run-lifecycle.util.ts`, `run-status.util.ts`, `task-state.util.ts`;
- `compare.util.ts`, `sort.util.ts`, `pluralize.util.ts`, `time.util.ts`, `labels.util.ts`.

Не каждый файл `utils` чистый: `storage.util.ts` обращается к browser storage, `api-url.util.ts`
читает окружение URL, а `task-runner.util.ts` управляет состоянием выполнения.

### Состояние и orchestration

- `state/*.store.ts` — состояние отдельных областей: dashboard, run, preset, extra, catalog,
  selection, output files, admin и errors;
- `state/workflow.facade.ts` — фасад, которым пользуются компоненты;
- `app.store.ts` — состояние оболочки приложения;
- `app.error-store.ts` и `error.store.ts` — разные границы обработки ошибок.

Store может решать, **когда** запросить/обновить данные, но сложное преобразование лучше вынести в
чистую функцию `state/utils`, чтобы тестировать его таблицей входов и выходов.

### HTTP и realtime

- `state/api-client.service.ts` — ручной адаптер над HTTP/API-вызовами;
- `state/realtime-hub.service.ts` — подключение и reconnect SignalR;
- `state/realtime-hub.contract.ts` — клиентские имена событий;
- `generated/api/` — сгенерированный по OpenAPI клиент; вручную его не редактируют;
- `http-logging.interceptor.ts` — транспортное логирование.

При изменении HTTP DTO сначала меняется C#-контракт, затем OpenAPI и generated client, и только
после этого frontend store/component.

### Представление

- `components/` — прикладные карточки, панели деталей, история и выбор участников;
- `ui/` — переиспользуемые диалоги и UI-сервисы;
- `app.html`, `app.css`, `app.ts` — корневая оболочка;
- `theme/` — общие визуальные токены;
- `i18n/` — пользовательские тексты.

Компонент должен в основном связывать view state с шаблоном. Если вычисление можно проверить без
Angular TestBed и DOM, его место обычно в `state/utils`.

## 7. Конфигурация, данные и вспомогательные приложения

- `configs/catalog.json` — прикладная конфигурация groups/members/targets;
- `scripts/` — исполняемые SQL-скрипты; это часть поведения системы, хотя не C#-код;
- `output/` и `output/_state/` — результаты и runtime-состояние, не исходники;
- `console/Unload.FtpServer` и `console/Unload.GatewayHandler` — development-only окружение для
  локальной проверки gateway;
- `tools/Unload.ProjectSync` — самостоятельная утилита синхронизации проекта, не часть runtime
  `Unload.Api`.

## 8. Где искать тесты

| Что меняется | Где искать зеркальный тест |
|---|---|
| Правило workflow или дневного окна | `tests/Unload.Backend.Tests/TaskWorkflowTests.cs`, `DailyWindowPolicyTests.cs` |
| Проектор состояния | `*ProjectorTests.cs`, `RunCompletionPolicyTests.cs`, `RunStateStore*Tests.cs` |
| HTTP/SignalR-контракт | `RunControllerRouteTests.cs`, `OpenApiContractTests.cs`, `SignalRContractTests.cs` |
| Персистентность | `JsonFileStoreTests.cs`, `RunStatePersistenceTests.cs`, `PersistenceDegradedModeTests.cs` |
| Чистая frontend-проекция | соседний `*.util.spec.ts` в `state/utils/` |
| Store или компонент | соседний `*.store.spec.ts` или `*.spec.ts` |
| Реальная геометрия/сценарий UI | `web/webApp/e2e/` |
| ProjectSync | `tests/Unload.ProjectSync.Tests/` |

Тестовые каталоги и fixtures не должны использовать `output/` или `output/_state`.

## 9. Куда класть новый код

| Если новый код... | Предпочтительное место |
|---|---|
| Только преобразует входные значения в результат | `Unload.Core`, чистый projector/policy рядом с моделью или `state/utils` |
| Определяет доступность или конфликт задач | конкретный `UnloadTask`, `TaskWorkflow` или `DailyWindowPolicy` |
| Оркестрирует шаги `run`/`extra`/`preset` | соответствующий `Unload.Tasks.*` |
| Обращается к БД, FTP, JSON или файловой системе | соответствующий infrastructure-проект за интерфейсом из `Unload.Core` |
| Добавляет HTTP endpoint или DTO | `Unload.Api/Controllers` и `Unload.Api/Models` |
| Работает постоянно в фоне | hosted/background service, а переиспользуемое правило вынести из него |
| Хранит клиентское состояние | frontend store |
| Вычисляет клиентское представление | `state/utils` |
| Только отображает уже подготовленное состояние | `components` или `ui` |

Практический критерий: если для теста функции нужны сеть, диск, часы, DI container, Angular
TestBed или браузер, это уже не чистая логика. Зависимость можно оставить осознанно либо отделить
I/O-обвязку от вычисляемого правила.
