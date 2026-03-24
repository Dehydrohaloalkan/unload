# Unload

Платформа выгрузки данных с явным workflow-пайплайном:

- есть системная стадия готовности `probe_preset_ready`;
- есть пользовательские задачи `preset`, `run`, `extra`;
- есть правила порядка, конфликтов и будущих автопереходов;
- есть API, Console, WebConsole и Angular WebApp для запуска и наблюдения.

Этот `README.md` описывает прикладную логику проекта: как устроен бизнес-пайплайн, какие сервисы за что отвечают, как менять правила и куда добавлять новые задачи.

## Что делает приложение

Приложение:

- получает выборку по участникам или target-кодам;
- находит подходящие SQL-скрипты;
- читает данные из БД потоково;
- режет результат на чанки;
- пишет файлы в `output`;
- публикует события;
- хранит и отдает live-статусы;
- поддерживает отдельные задачи `preset` и `extra`.

## Бизнес-логика

Текущая бизнес-логика такая:

1. После `15:00` автоматически запускается системная стадия `probe_preset_ready`.
2. Стадия выполняет SQL probe из настройки `PresetGate.ProbeSql`.
3. Пока probe возвращает `0`, запуск `preset` запрещен.
4. Когда probe возвращает `1`, стадия `probe_preset_ready` считается завершенной, и пользователь может запускать `preset`.
5. Пользователь запускает `preset`.
6. После успешного `preset` становятся доступны:
   - `run`
   - `extra`
7. Пользователь может запустить `run` и `extra` параллельно.
8. `preset` при этом не может выполняться одновременно с `run` и `extra`.
9. После смены даты окно сбрасывается:
   - стадия `probe_preset_ready` сбрасывается;
   - выполненные задачи сбрасываются;
   - нужен новый `preset` для нового дня.

## Как читать пайплайн

Пайплайн разделен на 3 уровня.

### 1. System stages

Это автоматические стадии, которые не запускает пользователь напрямую.

Сейчас есть одна стадия:

- `probe_preset_ready`

Назначение:

- автоматически выполняется по расписанию;
- проверяет готовность системы к запуску `preset`;
- открывает следующий шаг пайплайна.

### 2. User tasks

Это задачи, которые запускает пользователь.

Сейчас есть:

- `preset`
- `run`
- `extra`

### 3. Auto transitions

Это будущий слой автоматических переходов после завершения задач.

Сейчас инфраструктура уже подготовлена, но handlers еще не зарегистрированы:

- после `preset`, `run` или `extra` вызывается `IWorkflowTaskTransitionService`;
- в будущем можно будет подключить автоматическую задачу после `extra` через отдельный transition handler.

## Полный поток выполнения

### Поток `probe -> preset -> run/extra`

1. `PresetGateBackgroundService` отслеживает локальное время.
2. После `StartHour:StartMinute` он начинает polling.
3. По расписанию вызывает `IPresetProbeWorkflowStage`.
4. `PresetProbeWorkflowStage` выполняет SQL probe.
5. Если probe вернул `1`, stage `probe_preset_ready` помечается completed.
6. Пользователь вызывает `preset` через API или Console.
7. `IWorkflowTaskDispatcher` находит `RunPresetWorkflowTaskDefinition`.
8. `IWorkflowTaskAccessService` проверяет:
   - завершена ли `probe_preset_ready`;
   - нет ли конфликтующей задачи;
   - допускает ли `IPresetGateService` выполнение `preset`.
9. `ScriptTaskOrchestrator` выполняет preset-скрипты из `scripts/preset`.
10. После успешного `preset`:
    - `PresetGateService` помечает preset completed;
    - task `preset` помечается completed;
    - открываются `run` и `extra`.
11. Пользователь может запускать `run` и `extra` независимо.
12. `run` и `extra` совместимы и могут выполняться одновременно.

### Поток `run`

1. Пользователь вызывает `POST /api/runs` или запускает Console.
2. `IWorkflowTaskDispatcher` находит `StartRunWorkflowTaskDefinition`.
3. Definition:
   - валидирует входные данные;
   - при запуске по `memberCodes` через `ICatalogService` переводит их в `targetCodes`;
   - проверяет доступность задачи;
   - вызывает `IRunOrchestrator`.
4. `IRunOrchestrator`:
   - нормализует коды;
   - создает `RunRequest`;
   - резервирует слот единственного активного `run`;
   - создает стартовый статус.
5. `RunProcessingBackgroundService` читает активацию из `IRunCoordinator`.
6. `RunnerEngine` выполняет выгрузку.
7. Статусы попадают в `IRunStateStore`.
8. API публикует `status` и `run_status` в SignalR.
9. После завершения `run`:
   - слот освобождается;
   - задача `run` помечается completed;
   - вызывается `IWorkflowTaskTransitionService`.

### Поток `extra`

1. Пользователь вызывает `POST /api/runs/extra` или запускает Console/WebConsole с `--extra`.
2. `IWorkflowTaskDispatcher` находит `RunExtraWorkflowTaskDefinition`.
3. `IWorkflowTaskAccessService` проверяет:
   - завершен ли `preset`;
   - нет ли конфликта с `preset`.
4. `ScriptTaskOrchestrator`:
   - находит SQL-скрипты в корне `scripts`;
   - выполняет их;
   - группирует результат по `NrBank`;
   - пишет файлы.
5. После завершения:
   - задача `extra` помечается completed;
   - вызывается `IWorkflowTaskTransitionService`.

## Кто за что отвечает

### `backend/Unload.Core`

Базовый слой контракта runtime:

- модели домена и событий;
- контракты `IRunner`, `ICatalogService`, `IDatabaseClient`, `IDatabaseClientFactory`, `IFileChunkWriter`, `IMqPublisher`, `IRequestHasher`.

Меняется редко. Обычно сюда выносятся общие контракты и модели без orchestration.

### `backend/Unload.Catalog`

Отвечает за каталог и правила поиска скриптов.

Главное:

- читает `configs/catalog.json`;
- строит связи `group -> member -> target`;
- находит SQL-файлы;
- определяет большие target-выборки через `bigScripts`.

Если меняется логика выбора SQL-файлов, структура каталога или naming rule, смотреть сюда.

### `backend/Unload.DataBase`

Отвечает за доступ к БД.

Главное:

- создает клиентов БД;
- выполняет SQL;
- возвращает `DbDataReader` для потокового чтения.

Если меняется драйвер БД, стратегия подключения или расшифровка connection string, смотреть сюда.

### `backend/Unload.FileWriter`

Отвечает за запись чанков в файлы.

Главное:

- формирует output-файлы;
- пишет заголовки;
- гарантирует корректную параллельную запись по файлам.

Если меняется формат выходного файла или правила именования output, смотреть сюда.

### `backend/Unload.MQ`

Отвечает за публикацию событий раннера.

Сейчас это in-memory заглушка.

### `backend/Unload.Cryptography`

Отвечает за хеширование запросов.

### `backend/Unload.Runner`

Исполняет основной pipeline выгрузки.

Главные сервисы:

- `RunnerEngine` — основной engine;
- `ScriptDistributor` — раздает скрипты worker-потокам;
- `RunnerEventEmitter` — публикует события;
- `RunnerEngineDataReader` — читает строки и колонки;
- `RunnerOutputDirectoryFactory` — создает output-директории;
- `RunReportCsvWriter` — пишет итоговый CSV-отчет.

Если меняется параллельность, порядок обработки скриптов, чанки, отчет или MQ-эмиссия, смотреть сюда.

### `backend/Unload.Run.Application`

Здесь живет прикладной слой основного `run`.

Главные сервисы:

- `IRunOrchestrator` / `RunOrchestrator`
- `IRunRequestFactory` / `RunRequestFactory`
- `IRunCoordinator`
- `IRunStateStore`
- `RunStatusInfo`

Если меняется orchestration основного запуска, нормализация target-кодов, контракт статусов или бизнес-семантика одного активного `run`, смотреть в первую очередь сюда.

### `backend/Unload.Run.Runtime`

Здесь лежат in-memory runtime реализации для основного `run`.

Главные сервисы:

- `InMemoryRunCoordinator`
- `InMemoryRunStateStore`

Если меняется process-local хранение статусов, активный слот запуска или runtime-механика `run`, смотреть сюда.

### `backend/Unload.TaskFlow`

Здесь живет orchestration пользовательских задач и прозрачная конфигурация pipeline.

Главные группы:

- task models и codes:
  - `WorkflowTaskCodes`
  - `WorkflowStageCodes`
  - `StartRunTaskRequest`
  - `StartRunTaskResult`
  - `EmptyWorkflowTaskRequest`
  - `WorkflowTaskDispatchException`

- workflow-task definitions:
  - `StartRunWorkflowTaskDefinition`
  - `RunPresetWorkflowTaskDefinition`
  - `RunExtraWorkflowTaskDefinition`

- pipeline configuration:
  - `TaskPipelineConfigurator`
  - `TaskPipelineBuilder`
  - `TaskPipeline`

- policy и transitions:
  - `WorkflowTaskDependencyCatalog`
  - `IWorkflowTaskTransitionService`
  - `IWorkflowTaskTransitionHandler`

- preset gate:
  - `IPresetGateService`
  - `PresetGateService`

Если меняется порядок задач, зависимости, конфликты, post-actions или бизнес-правила окна `preset`, смотреть сюда.

### `backend/Unload.TaskFlow.Runtime`

Здесь лежат in-memory runtime реализации task orchestration.

Главные сервисы:

- `InMemoryWorkflowTaskAccessService`
- `InMemoryWorkflowStageStateStore`

Если меняется process-local состояние completed tasks/stages или enforcement правил запуска задач, смотреть сюда.

### `backend/Unload.ScriptTasks`

Здесь лежат инфраструктурные реализации дополнительных задач.

Главные сервисы:

- `ScriptTaskOrchestrator`
- `PresetScriptExecutor`
- `ExtraScriptExecutor`
- `ExtraOutputWriter`
- `ScriptTaskEventPublisher`

Если меняется исполнение `preset` или `extra` через SQL, файловую систему и DB/MQ интеграцию, смотреть сюда.

### `backend/Unload.Bootstrapper`

Единая DI-композиция runtime.

Главное:

- `AddUnloadRuntime(...)`

Если добавляется новый проект runtime-слоя или меняется общая сборка зависимостей для API/Console, смотреть сюда. Регистрация самих task definitions теперь собирается через `TaskPipelineConfigurator`, а не вручную по одной строке.

### `backend/Unload.Workflow`

Низкоуровневый каркас workflow.

Главное:

- `ISingleActiveWorkflow<TPayload>` / `InMemorySingleActiveWorkflow<TPayload>`
- `IWorkflowTaskRegistry`
- `IWorkflowTaskDispatcher`

Если меняется низкоуровневый runtime dispatch/activation, смотреть сюда.

### `backend/Unload.Api`

Транспортный слой.

Главное:

- контроллеры;
- ProblemDetails и error handling;
- SignalR hub;
- background services;
- системная стадия `PresetProbeWorkflowStage`.

Если меняются HTTP-контракты, события SignalR, transport-level background services или API-ошибки, смотреть сюда.

### `console/Unload.Console`

Локальный запуск через DI того же runtime.
По умолчанию работает как единая интерактивная сессия стадий `probe -> preset -> run -> extra` без перезапуска процесса.
Для совместимости оставлены one-shot флаги `--preset` и `--extra`.

### `console/Unload.WebConsole`

CLI-клиент к API через HTTP + SignalR.

### `web/webApp`

Браузерный Angular-клиент к API через HTTP + SignalR.

Главное:

- стартовый экран показывает live-часы и состояние `preset_state`;
- часы синхронизируются через backend endpoint `GET /api/system/time`, а не по локальному времени браузера;
- после успешного `preset` без дерганой жидкой анимации открывается экран запуска `run` и `extra`;
- активный `run` восстанавливается после перезагрузки страницы через `GET /api/runs/active` и `GET /api/runs/{correlationId}`;
- `run`-карточка показывает console-like таблицу worker-потоков (`Worker #n` + `running <script>` / `idle`), компактные цветные карточки мемберов и модальное окно деталей по выбранному мемберу;
- внутри модального окна мембера показываются последние логи, target-коды, абсолютные пути файлов и ссылки на скачивание артефактов через system API;
- выбранные мемберы сохраняются локально в браузере;
- для `extra` UI использует текущий API-контракт без отдельного backend live-state.

## Где управлять пайплайном

Ниже краткая карта: что менять и в каком файле.

### Изменить порядок задач

Файл:

- `backend/Unload.TaskFlow/Pipeline/TaskPipelineConfigurator.cs`

Здесь задается:

- какие задачи входят в pipeline;
- какие задачи/стадии должны быть завершены до запуска другой задачи;
- какие задачи конфликтуют друг с другом;
- какие transition handlers запускать после completion.

Примеры:

- сделать `preset` зависимым от новой system-stage;
- сделать новую задачу зависимой от `extra`;
- запретить или разрешить параллельное выполнение задач.

### Изменить бизнес-условия доступности

Файл:

- `backend/Unload.TaskFlow/Services/PresetGateService.cs`

Здесь задаются:

- окно времени;
- daily reset;
- правила `CanRunPreset`;
- правила `CanRunMainAndExtra`.

Если правило связано именно с бизнес-состоянием окна выгрузки, менять нужно здесь.

### Изменить system-stage probe

Файлы:

- `backend/Unload.Api/Services/PresetGateBackgroundService.cs`
- `backend/Unload.Api/Services/PresetProbeWorkflowStage.cs`

Разделение такое:

- `PresetGateBackgroundService` — только расписание и polling;
- `PresetProbeWorkflowStage` — только сама стадия probe.

### Изменить исполнение `run`

Файлы:

- `backend/Unload.TaskFlow/Definitions/StartRunWorkflowTaskDefinition.cs`
- `backend/Unload.Run.Application/Services/RunOrchestrator.cs`
- `backend/Unload.Run.Runtime/Services/InMemoryRunStateStore.cs`
- `backend/Unload.Api/Services/RunProcessingBackgroundService.cs`
- `backend/Unload.Runner/Services/RunnerEngine.cs`

### Изменить исполнение `preset` или `extra`

Файлы:

- `backend/Unload.TaskFlow/Definitions/RunPresetWorkflowTaskDefinition.cs`
- `backend/Unload.TaskFlow/Definitions/RunExtraWorkflowTaskDefinition.cs`
- `backend/Unload.ScriptTasks/Services/ScriptTaskOrchestrator.cs`
- связанные executors/writers внутри `backend/Unload.ScriptTasks`

### Изменить автоматические переходы после завершения задач

Файлы:

- `backend/Unload.TaskFlow/Abstractions/IWorkflowTaskTransitionService.cs`
- `backend/Unload.TaskFlow/Services/WorkflowTaskTransitionService.cs`
- `backend/Unload.TaskFlow/Pipeline/TaskPipelineConfigurator.cs`

Для новой автоматической реакции нужен новый `IWorkflowTaskTransitionHandler`.

## Как добавить новую задачу

### Сценарий 1. Новая ручная задача, которую запускает пользователь

Что нужно сделать:

1. Добавить новый код задачи в `backend/Unload.TaskFlow/Models/WorkflowTaskCodes.cs`.
2. Создать request/result модели при необходимости.
3. Создать новый `WorkflowTaskDefinition`.
4. Добавить задачу в `backend/Unload.TaskFlow/Pipeline/TaskPipelineConfigurator.cs`.
5. Если задача зависит от другой задачи или стадии, описать это в `TaskPipelineConfigurator`.
6. Если задача конфликтует с другими задачами, тоже описать это в `TaskPipelineConfigurator`.
7. Реализовать прикладной или инфраструктурный исполнитель:
   - либо в `Unload.TaskFlow`,
   - либо в `Unload.ScriptTasks`,
   - либо в новом infra-проекте, если это отдельный большой поток.
8. Если нужен transition handler, подключить его через `TaskPipelineConfigurator`.
9. Добавить transport-вход:
   - API endpoint/use-case;
   - и/или Console/WebConsole режим.
10. Обновить:
   - `README.md`
   - `docs/ARCHITECTURE.md`
   - `postman/unload-api.postman_collection.json`, если это API-задача.

### Сценарий 2. Новая автоматическая задача после `extra`

Что нужно сделать:

1. Добавить новый `TaskCode`.
2. Создать новый `WorkflowTaskDefinition`.
3. При необходимости описать зависимость в `TaskPipelineConfigurator`.
4. Создать `IWorkflowTaskTransitionHandler`, например `PostExtraTransitionHandler`.
5. В handler:
   - указать `SourceTaskCode = WorkflowTaskCodes.Extra`;
   - через `IWorkflowTaskDispatcher` запустить новую задачу.
6. Подключить handler через `TaskPipelineConfigurator`.

Важно:

- текущая инфраструктура уже готова для такого расширения;
- текущая бизнес-логика не изменится, пока handler не зарегистрирован.

### Сценарий 3. Новая системная стадия до пользовательской задачи

Что нужно сделать:

1. Добавить новый stage code в `backend/Unload.TaskFlow/Models/WorkflowStageCodes.cs`.
2. Реализовать stage executor, по аналогии с `PresetProbeWorkflowStage`.
3. Если нужен отдельный scheduler, добавить background service или встроить в существующий scheduler.
4. Добавить зависимость нужной пользовательской задачи на новую stage в `TaskPipelineConfigurator`.
5. Если stage должна сбрасываться по расписанию, сбрасывать ее через `IWorkflowStageStateStore`.

## API

Основные endpoint'ы:

- `POST /api/runs` — старт `run` по `memberCodes`
- `GET /api/runs/preset/state` — состояние preset-гейта
- `POST /api/runs/preset` — запуск `preset`
- `POST /api/runs/extra` — запуск `extra`
- `POST /api/runs/{correlationId}/stop` — остановка активного `run`
- `GET /api/runs` — список запусков
- `GET /api/runs/active` — активный `run`
- `GET /api/runs/{correlationId}` — статус конкретного `run`, включая `workerStatuses` и `outputArtifacts`
- `GET /api/system/download?path=...` — безопасная выдача файла из директории `output` для скачивания из UI

SignalR:

- hub: `/hubs/status`
- события:
  - `status`
  - `run_status`
  - `preset_state`

Формат ошибок API:

- `application/problem+json`
- поля: `type`, `title`, `status`, `detail`, `instance`
- расширения: `errorCode`, `traceId`
- дополнительные поля по ситуации, например `activeCorrelationId`

## Конфигурация

### `configs/catalog.json`

- `bigScripts` — target-выборки, которые считаются большими и выполняются в `n-1` потоках

### `appsettings` -> `Database`

- `TimeoutSeconds`
- `ConnectionString`

### `appsettings` -> `Runner`

- `WorkerCount`
- `ChunkSizeBytes`

### `appsettings` -> `PresetGate`

- `Enabled`
- `StartHour`
- `StartMinute`
- `PollIntervalSeconds`
- `ProbeSql`

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

Локальный Console:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj
```

Эта команда открывает stage-меню и позволяет последовательно выполнить `probe`, `preset`, `run`, `extra` в одном процессе.

Локальный `preset`:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- --preset
```

Локальный `extra`:

```powershell
dotnet run --project .\console\Unload.Console\Unload.Console.csproj -- --extra
```

WebConsole:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --members M
```

WebConsole `preset`:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --preset
```

WebConsole `extra`:

```powershell
dotnet run --project .\console\Unload.WebConsole\Unload.WebConsole.csproj -- --api http://localhost:5000 --extra
```

Angular WebApp:

```powershell
cd .\web\webApp
npm start
```

## Ограничения

- Одновременно может выполняться только один активный `run`.
- `run` и `extra` могут выполняться параллельно.
- `preset` конфликтует с `run` и `extra`.
- `IRunCoordinator`, `IRunStateStore`, workflow-stage state и completed tasks сейчас in-memory и живут в `Unload.Run.Runtime` + `Unload.TaskFlow.Runtime`.
- После перезапуска процесса состояние pipeline не сохраняется.
- Реализации БД и MQ сейчас development-oriented.

## Где смотреть подробнее

- Прикладная логика и быстрый вход: `README.md`
- Детальная архитектура, диаграммы и naming rules: `docs/ARCHITECTURE.md`
- API smoke/edge tests: `postman/unload-api.postman_collection.json`
