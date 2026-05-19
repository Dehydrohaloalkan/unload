# План рефакторинга backend Unload

Документ — спецификация рефакторинга для пошаговой реализации. Опус составил план, реализация — Sonnet.

## 0. Цель и принципы

Привести backend к модели, где **задача — первоклассная сущность**, воркфлоу — единственный
контролёр запретов на запуск, а хранилище запусков и работа с конфигом не размазаны по слоям.

Принципы:

- Один абстрактный класс задачи; под него попадают main-выгрузка, extra-выгрузка, preset, probe.
- Воркфлоу контролирует ВСЕ запреты на запуск (depы, конфликты, single-active, дневное окно).
- Никаких «Runtime»-проектов и разделения Application/Runtime для in-memory реализаций.
- Единое хранилище запусков и исполнений задач в одном проекте, одна реализация персистентности.
- Бутстрапер инкапсулирует всю работу с файлом конфига.
- API — только транспорт: HTTP/SignalR ↔ воркфлоу, без доменной логики.
- Интерфейс заводится только для реальной границы (несколько реализаций / тестовый шов).
  Для типов с единственной реализацией интерфейс не нужен.
- Без backward-compat шимов: старые типы удаляются, а не помечаются `[Obsolete]`.
- **Каждая фаза заканчивается зелёной сборкой и рабочим приложением.**

## 1. Зафиксированные решения

| Вопрос | Решение |
|---|---|
| Дневное окно | Policy-объект `DailyWindowPolicy` внутри воркфлоу-контроллера, не задача. |
| Автопереходы (`IWorkflowTaskTransitionService`/`Handler`) | Удалить полностью. Понадобится — добавить свойством `NextTasks` на классе задачи. |
| preset/probe | Отдельный проект `Unload.Tasks.Preset` (preset-задача + probe-задача). |

## 2. Текущее состояние (что не так)

- Задача размазана по `IWorkflowTaskDefinition` + `WorkflowTaskDefinition<,>` + 3 definition-класса
  + `TaskPipeline`/`TaskPipelineBuilder`/`TaskPipelineConfigurator`/`TaskPipelineDescriptor`
  + `WorkflowTaskRule`/`WorkflowTaskDependencyCatalog` — ~10 типов вместо одного абстрактного класса.
- Запреты на запуск в трёх местах: `InMemoryWorkflowTaskAccessService` (конфликты/depы/single-run),
  `PresetGateService` (окно времени), плюс каждый definition сам дёргает gate.
- `Unload.Run.Runtime` (1 стор) и `Unload.TaskFlow.Runtime` (2 in-memory класса) — пустые по смыслу.
- Два «Run»-проекта: `Run.Application` (фабрика + модели) и `Run.Runtime` (стор).
- Два хранилища со своей JSON-персистентностью: `InMemoryRunStateStore` и `TaskExecutionHistoryStore`,
  плюс `WorkflowInMemoryStateRestorer` восстанавливает состояние из истории.
- API содержит доменную логику: `RequeueToGatewayUseCase` (~300 строк), `gateway-upload`
  в `SystemController`, `TaskExecutionHistoryStore`, `WorkflowInMemoryStateRestorer`.
- Конфиг читается в 3 местах: `Unload.Api/Program.cs`, `Unload.Console/Program.cs`,
  дефолты в `Bootstrapper`.
- `InMemoryRunStateStore.ResolveTaskCodeByCorrelationId` угадывает тип задачи по строковому
  префиксу `correlationId` — хрупко.

## 3. Целевая структура проектов

Было 15 backend-проектов → станет 13. Главное — чистые зоны ответственности.

Инфраструктура (без структурных изменений):

- `Unload.Core` — доменные модели + инфраструктурные контракты. `IRunner` отсюда уезжает.
- `Unload.Catalog`, `Unload.DataBase`, `Unload.FileWriter`, `Unload.Cryptography`, `Unload.Gateway`.

Новые / консолидированные:

- `Unload.Store` — **единое хранилище**. Записи `TaskExecution` по всем задачам (main/extra/preset/probe),
  «живая» детализация main-выгрузки (workers/members/artifacts/sender), одна реализация
  JSON-персистентности. Поглощает `Unload.Run.Runtime` и `TaskExecutionHistoryStore` из API.
- `Unload.Tasks` — **ядро**: абстрактный `UnloadTask`, воркфлоу-контроллер `TaskWorkflow`,
  `DailyWindowPolicy`, модели запуска. Поглощает `Unload.Workflow` + `Unload.TaskFlow`
  + `Unload.TaskFlow.Runtime`.
- `Unload.Tasks.MainUnload` — основная задача выгрузки + движок. Переименование `Unload.Runner`,
  плюс поглощает `Unload.Run.Application`.
- `Unload.Tasks.ExtraUnload` — extra-задача + движок. Extra-часть бывшего `Unload.ScriptTasks`.
- `Unload.Tasks.Preset` — preset-задача + probe-задача. Preset/probe-часть `Unload.ScriptTasks`
  + `PresetProbeService`.
- `Unload.Bootstrapper` — DI-композиция + **вся работа с конфигом**.
- `Unload.Api` — тонкий транспорт.

Удаляются как отдельные проекты: `Unload.Workflow`, `Unload.TaskFlow`, `Unload.TaskFlow.Runtime`,
`Unload.Run.Application`, `Unload.Run.Runtime`, `Unload.ScriptTasks`.
`Unload.Runner` переименовывается в `Unload.Tasks.MainUnload`.

Граф зависимостей (упрощённо):

```
Api ──> Bootstrapper ──> Tasks ──> Store ──> Core
                          │  └─> Tasks.MainUnload ──> Tasks, Store, Catalog, DataBase, FileWriter, Gateway
                          │  └─> Tasks.ExtraUnload ──> Tasks, Store, DataBase, Gateway
                          │  └─> Tasks.Preset      ──> Tasks, Store, DataBase
                          └─> Gateway, Cryptography
```

`Unload.Tasks` НЕ зависит от конкретных проектов задач — задачи регистрируются в DI как
`IEnumerable<UnloadTask>`, контроллер их не знает поимённо.

## 4. Модель задачи

### 4.1. Абстрактный класс

```csharp
namespace Unload.Tasks;

public abstract class UnloadTask
{
    public abstract string Code { get; }

    // Запреты на запуск ДЕКЛАРИРУЮТСЯ задачей, ЕНФОРСЯТСЯ воркфлоу.
    public virtual IReadOnlyCollection<string> RequiresCompleted => [];
    public virtual IReadOnlyCollection<string> ConflictsWith => [];

    // Должно ли дневное окно быть открыто для запуска (run/extra — true; preset/probe — false).
    public virtual bool RequiresDailyWindowOpen => false;

    // Задача завершается синхронно внутри ExecuteAsync (preset/extra/probe)
    // либо стартует deferred-выполнение и сразу возвращает Accepted (main-выгрузка).
    public abstract Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken);
}
```

### 4.2. Единый запрос и результат

Вместо `EmptyWorkflowTaskRequest` / `StartRunTaskRequest` / `StartRunTaskResult` /
`ScriptTaskRunResult` — две записи. Набор входов фиксированный и маленький, поэтому без дженериков
и без per-task request-типов.

```csharp
public enum RunSelectionMode { MemberCodes, TargetCodes }

public record TaskLaunchRequest(
    string TaskCode,
    bool AdminOverride = false,
    bool PublishToGateway = true,
    IReadOnlyCollection<string>? Codes = null,           // только main-выгрузка
    RunSelectionMode SelectionMode = RunSelectionMode.MemberCodes);

public enum TaskExecutionStatus { Accepted, Running, Completed, Failed, Cancelled, Blocked }

public record TaskExecutionResult(
    string TaskCode,
    string ExecutionId,
    TaskExecutionStatus Status,
    string Message,
    int? ScriptsExecuted = null,
    int? FilesWritten = null,
    string? OutputPath = null);
```

main-выгрузка читает `Codes`/`SelectionMode`, остальные задачи их игнорируют.

### 4.3. Четыре задачи

| Задача | Code | Проект | RequiresCompleted | ConflictsWith | DailyWindow |
|---|---|---|---|---|---|
| `ProbeTask` | `probe` | Tasks.Preset | — | — | false |
| `PresetTask` | `preset` | Tasks.Preset | `probe` | `run`, `extra` | false (особое окно — см. 5.2) |
| `MainUnloadTask` | `run` | Tasks.MainUnload | `preset` | `preset` | true |
| `ExtraUnloadTask` | `extra` | Tasks.ExtraUnload | `preset` | `preset` | true |

Понятие system-stage (`WorkflowStageCodes.PresetProbeReady`) исчезает: probe — обычная задача,
её завершение фиксируется в `Unload.Store` как `TaskExecution` с кодом `probe`.

## 5. Воркфлоу-контроллер

### 5.1. `TaskWorkflow`

Единственный класс оркестрации в `Unload.Tasks`. Без интерфейса (одна реализация).
Поглощает `WorkflowTaskDispatcher`, `WorkflowTaskRegistry`, `InMemoryWorkflowTaskAccessService`,
`WorkflowTaskDependencyCatalog`, `TaskFlowRegistryInvariant`.

```csharp
public class TaskWorkflow
{
    public TaskWorkflow(IEnumerable<UnloadTask> tasks, DailyWindowPolicy window,
                        TaskExecutionStore store, RunActivationChannel runChannel);

    public Task<TaskExecutionResult> LaunchAsync(TaskLaunchRequest request, CancellationToken ct);
}
```

`LaunchAsync` делает строго по порядку:

1. Резолв задачи по `request.TaskCode` (нет — `BLOCKED`/исключение валидации).
2. **Проверка запретов на запуск** (если не `AdminOverride`):
   - дневное окно: если `task.RequiresDailyWindowOpen` и `!window.IsOpen(now)` → блок;
   - `RequiresCompleted`: каждый код должен иметь успешный `TaskExecution` за сегодня в сторе;
   - `ConflictsWith`: ни одна из конфликтующих задач не должна быть активна;
   - single-active для `run`: проверка `RunActivationChannel`.
3. Запись стартового `TaskExecution` в стор.
4. `task.ExecuteAsync(request, ct)`.
5. Фиксация завершения в сторе (для `run` — фиксирует фоновый воркер, см. 6.2).

Все коды ошибок (`PRESET_GATE_BLOCKED`, `TASK_DEPENDENCY_NOT_SATISFIED`, `TASK_ALREADY_RUNNING`,
`RUN_ALREADY_IN_PROGRESS`, `VALIDATION_ERROR`) кидаются ОДНИМ типом `TaskLaunchException`
(замена `WorkflowTaskDispatchException`) с полями `FailureKind`, `ErrorCode`, `Extensions`.
Задачи больше НЕ дёргают gate сами — это делает только воркфлоу.

### 5.2. `DailyWindowPolicy`

Перенос `PresetGateService` в `Unload.Tasks`, без интерфейса `IPresetGateService`.
Отвечает за: время старта окна, daily reset по смене даты, признак «probe пройден»,
признак «preset выполнен сегодня», правила `IsOpen(now)` и `CanRunPreset(now)`.

Особенность preset: preset доступен после probe=1 и до конца дня, но не участвует в обычном
`RequiresDailyWindowOpen` (run/extra). Поэтому `PresetTask.RequiresDailyWindowOpen = false`,
а проверку «probe пройден + время» воркфлоу делает через `window.CanRunPreset(now)` —
по факту это `RequiresCompleted: [probe]` + чек времени окна. Реализовать как отдельную ветку
в `LaunchAsync` для `preset` ИЛИ как метод `window.EnsureCanRun(task, now)`.

DTO состояния окна (`PresetGateState`) сохраняется для UI (см. 7) — можно переименовать
в `DailyWindowState`, но это опционально (повлияет на frontend-контракт).

### 5.3. Что удаляется из воркфлоу-слоя

Удалить полностью (single-impl интерфейсы и pipeline-машинерия):

`IWorkflowTaskDefinition`, `WorkflowTaskDefinition<,>`, `IWorkflowTaskDispatcher`,
`WorkflowTaskDispatcher`, `IWorkflowTaskRegistry`, `WorkflowTaskRegistry`,
`ISingleActiveWorkflow<>`, `IWorkflowTaskDependencyCatalog`, `WorkflowTaskDependencyCatalog`,
`WorkflowTaskRule`, `TaskPipeline`, `TaskPipelineBuilder`, `TaskPipelineConfigurator`,
`TaskPipelineDescriptor`, `IWorkflowTaskAccessService`, `InMemoryWorkflowTaskAccessService`,
`IWorkflowStageStateStore`, `InMemoryWorkflowStageStateStore`, `WorkflowStageCodes`,
`ITaskFlowRegistryInvariant`, `TaskFlowRegistryInvariant`, `IWorkflowTaskTransitionService`,
`WorkflowTaskTransitionService`, `IWorkflowTaskTransitionHandler`, `WorkflowTaskCompletionContext`,
`IPresetGateService`, `IPresetProbeService`, `IScriptTaskOrchestrator`.

`WorkflowTaskCodes` сохраняется как `TaskCodes` (константы `run`/`preset`/`extra`/`probe`).
`InMemorySingleActiveWorkflow<RunRequest>` де-дженерикизируется в `RunActivationChannel`
и переезжает в `Unload.Tasks.MainUnload` (см. 6.2).

## 6. Единое хранилище — `Unload.Store`

### 6.1. Модель

Одна запись `TaskExecution` на любое исполнение задачи (run/extra/preset/probe).
Тип задачи хранится ЯВНО — `ResolveTaskCodeByCorrelationId` (угадывание по префиксу) удаляется.

```csharp
public record TaskExecution(
    string ExecutionId,
    string TaskCode,
    TaskExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? Message,
    int? ScriptsExecuted,
    int? FilesWritten,
    string? OutputPath,
    RunDetail? RunDetail);   // не null только для main-выгрузки
```

`RunDetail` — текущая «живая» структура из `RunStatusInfo` (members/workers/artifacts/
senderBatches). Переносится из `Unload.Run.Application/Models/RunStatusModels.cs`.

### 6.2. Классы

- `TaskExecutionStore` — единственный стор. Поглощает `InMemoryRunStateStore`
  (включая `RunStateProjector`) и `TaskExecutionHistoryStore`. Методы: `StartExecution`,
  `ApplyRunnerEvent`, `ApplySenderFeedback`, `SetCompleted/Failed/Cancelled/CancellationRequested`,
  `Get`, `List`, `ListByDay/Range`, `HasCompletedToday(taskCode, day)`, `PruneTerminal`.
  `HasCompletedToday` — это и есть проверка `RequiresCompleted` воркфлоу: стор —
  единственный источник правды, отдельное in-memory восстановление не нужно.
- `JsonFileStore<T>` — одна реализация атомарной JSON-персистентности (write-temp + move).
  Заменяет `RunStatePersistence` и `TaskHistorySnapshot` (две дублирующие реализации).
- `GatewaySenderFeedbackConsumer` — переносится из `Unload.Run.Runtime` как есть.

Интерфейс `IRunStateStore` удаляется — стор один. Если для тестов нужен шов, оставить
интерфейс `ITaskExecutionStore` опционально (low-priority, по усмотрению).

### 6.3. Следствие

`WorkflowInMemoryStateRestorer` и `WorkflowStateRestoreHostedService` (оба в API) **удаляются**:
воркфлоу спрашивает у стора «что завершено сегодня» напрямую, восстанавливать in-memory нечего.

## 7. API — тонкий транспорт

Переезжает ИЗ API:

- `RequeueToGatewayUseCase` → сервис `RequeueService` в `Unload.Tasks` (или `Unload.Store`,
  ближе к данным). Логика «republish прошлых результатов» — доменная, не транспортная.
- `gateway-upload` (логика из `SystemController.UploadFilesToGateway`) → `GatewayUploadService`
  в `Unload.Gateway`. Контроллер только принимает `IFormFile` и зовёт сервис.
- `TaskExecutionHistoryStore` → `Unload.Store` (см. 6).
- `WorkflowInMemoryStateRestorer` + `WorkflowStateRestoreHostedService` → удалить (см. 6.3).

Остаётся в API (легитимный транспорт):

- Контроллеры (тонкие), `RunStatusHub`, error-handling (`GlobalExceptionHandler`,
  `ApiProblemDetailsFactory`, `ApiProblemException`), HTTP-контракты в `Models/`.
- Hosted-сервисы как хостинг (логика — в задачах/воркфлоу):
  - `RunProcessingBackgroundService` → `MainUnloadHostedService`: читает активации
    `RunActivationChannel`, гоняет движок, обновляет стор, шлёт SignalR. Фиксацию
    завершения `run` делает здесь (а не в `TaskWorkflow.LaunchAsync`).
  - `PresetGateBackgroundService` → `ProbeSchedulerHostedService`: по расписанию зовёт
    `workflow.LaunchAsync(probe)`, публикует состояние окна в SignalR.
  - `SenderFeedbackProjectionBackgroundService`, `HistoryRetentionBackgroundService` —
    остаются, но prune-логика дергает `TaskExecutionStore.PruneTerminal`.

Удаляются single-impl интерфейсы API: `IApiProblemDetailsFactory`, `IOutputFilesService`,
`ITaskExecutionHistoryStore`, `IWorkflowInMemoryStateRestorer`, и 5 интерфейсов `UseCases/`
(`IStartRunUseCase` и др. — use-case'ы заменяются прямым вызовом `TaskWorkflow`).
Контроллеры зовут `TaskWorkflow` напрямую и мапят результат в HTTP.

## 8. Бутстрапер — работа с конфигом

Сейчас разбросано: `Unload.Api/Program.cs` читает секции `Database`/`Runner`/`PresetGate`/
`HistoryRetention` + резолвит пути; `Unload.Console/Program.cs` делает своё; дефолты в
`Bootstrapper`.

Сделать в `Unload.Bootstrapper`:

- `UnloadConfiguration` — одна запись со всеми настройками (paths, database, runner,
  daily window, history retention, gateway).
- `UnloadConfigurationLoader` — читает `IConfiguration` + резолвит корень workspace
  (поиск `configs/catalog.json` + `scripts/` вверх по дереву — сейчас дублируется в
  `ApiWorkspacePathResolver` и `Unload.Console/WorkspacePathResolver`).
- `AddUnloadRuntime(IServiceCollection, IConfiguration)` — единственная точка: внутри
  грузит `UnloadConfiguration`, регистрирует все сервисы. `Program.cs` API и Console
  больше не парсят секции сами.

## 9. Фазы реализации

Каждая фаза завершается **зелёной сборкой** (`dotnet build unload.slnx`) и работающим
приложением. После каждой фазы — коммит. `unload.slnx` обновлять по ходу.

### Прогресс

- [x] Фаза 1 — `Unload.Store` (коммит `37c4c3f`)
- [x] Фаза 2 — `Unload.Tasks` ядро (коммит `c0c8fd4`)
- [x] Фаза 3 — `Unload.Tasks.MainUnload` (коммит `dc58da2`)
- [x] Фаза 4 — `Unload.Tasks.ExtraUnload` (коммит `90ab9ee`)
- [x] Фаза 5 — `Unload.Tasks.Preset`
- [ ] Фаза 6 — слим API
- [ ] Фаза 7 — конфиг в Bootstrapper
- [ ] Фаза 8 — Console и документация

### Фаза 1 — `Unload.Store` ✅ ВЫПОЛНЕНО

- Создать проект `backend/Unload.Store`.
- Перенести `RunStatusModels.cs` (из `Run.Application`) → модели `RunDetail` + `TaskExecution`.
- Перенести `InMemoryRunStateStore` + `RunStateProjector` (из `Run.Runtime`) → `TaskExecutionStore`.
- Перенести `TaskExecutionHistoryStore` + `TaskRecord` (из `Api`) → влить в `TaskExecutionStore`.
- Ввести `JsonFileStore<T>`, выкинуть `RunStatePersistence` и `TaskHistorySnapshot`.
- Перенести `GatewaySenderFeedbackConsumer` из `Run.Runtime`.
- Обновить ссылки в `Bootstrapper` и `Api`. Удалить проект `Unload.Run.Runtime`.
- На этой фазе `Run.Application` остаётся (фабрика + опции), задачи ещё старые.
  Чтобы сборка была зелёной — временно адаптировать сигнатуры (`IRunStateStore` →
  `TaskExecutionStore`) у текущих definition'ов и hosted-сервисов.

### Фаза 2 — `Unload.Tasks` (ядро) ✅ ВЫПОЛНЕНО

- Создать `backend/Unload.Tasks`, влить `Unload.Workflow` + `Unload.TaskFlow`
  + `Unload.TaskFlow.Runtime`.
- Ввести `UnloadTask`, `TaskLaunchRequest`, `TaskExecutionResult`, `TaskCodes`,
  `TaskLaunchException`.
- Ввести `TaskWorkflow` (раздел 5.1) и `DailyWindowPolicy` (раздел 5.2).
- 3 текущих definition'а переписать как подклассы `UnloadTask` — **временно оставить
  внутри `Unload.Tasks`** (они ещё ссылаются на `Runner`/`ScriptTasks`/`Run.Application`).
- Удалить всё из раздела 5.3.
- DI: задачи регистрируются как `IEnumerable<UnloadTask>`; `AddUnloadTaskFlow*` заменяется
  на `AddUnloadTasks`.
- Удалить проекты `Unload.Workflow`, `Unload.TaskFlow`, `Unload.TaskFlow.Runtime`.

### Фаза 3 — `Unload.Tasks.MainUnload`

- Переименовать проект `Unload.Runner` → `Unload.Tasks.MainUnload` (csproj, namespace, папка).
- Влить `Unload.Run.Application` (`RunRequestFactory`, `RunApplicationOptions`,
  нормализация target-кодов из `StartRunWorkflowTaskDefinition`).
- Перенести сюда `MainUnloadTask` (бывший `StartRunWorkflowTaskDefinition`) из `Unload.Tasks`.
- Де-дженерикизировать `InMemorySingleActiveWorkflow<RunRequest>` → `RunActivationChannel`.
  **Уточнение:** `RunActivationChannel` оставить в `Unload.Tasks` (ядро), НЕ переносить в
  `MainUnload` — `TaskWorkflow` зависит от него для single-active проверки, а `Unload.Tasks`
  не может ссылаться на `MainUnload`. Generic-интерфейс `ISingleActiveWorkflow<>` и
  `WorkflowActivation<>` удалить, заменив на не-generic `RunActivationChannel`/`RunActivation`.
- `IRunner` (из `Core`) → `MainUnloadEngine` здесь; `RunnerEngine` переименовать
  в `MainUnloadEngine`. Опционально (low-priority, ограничить churn): `RunnerEvent`/
  `RunnerStep` можно оставить с текущими именами.
- Удалить проект `Unload.Run.Application`.

### Фаза 4 — `Unload.Tasks.ExtraUnload`

- Создать `backend/Unload.Tasks.ExtraUnload`.
- Перенести extra-движок из `ScriptTasks`: `ExtraScriptExecutor`, `ExtraOutputWriter`,
  `ExtraScriptExecutionResult`, `ExtraOutputWriteResult` (слить single-impl интерфейсы
  `IExtraScriptExecutor`/`IExtraOutputWriter` в классы).
- Перенести сюда `ExtraUnloadTask` (бывший `RunExtraWorkflowTaskDefinition`) из `Unload.Tasks`.

### Фаза 5 — `Unload.Tasks.Preset`

- Создать `backend/Unload.Tasks.Preset`.
- Перенести `PresetScriptExecutor` из `ScriptTasks` (слить `IPresetScriptExecutor`).
- Создать `PresetTask` (бывший `RunPresetWorkflowTaskDefinition`).
- Создать `ProbeTask` из `PresetProbeService` — probe становится обычной задачей.
- Перенести `ScriptTaskEventPublisher`/`ScriptTaskDatabaseClientDisposer` (общие хелперы)
  туда, где они нужны, или в `Unload.Core`.
- Удалить проект `Unload.ScriptTasks`.

### Фаза 6 — слим API

- Перенести `RequeueToGatewayUseCase` → `RequeueService` (раздел 7).
- Перенести `gateway-upload` → `GatewayUploadService` в `Unload.Gateway`.
- Удалить `WorkflowInMemoryStateRestorer`, `WorkflowStateRestoreHostedService`.
- Контроллеры → тонкие, зовут `TaskWorkflow`/сторы напрямую. Удалить `UseCases/` интерфейсы
  и классы, single-impl интерфейсы API.
- Hosted-сервисы переименовать/перенацелить (раздел 7).

### Фаза 7 — конфиг в бутстрапер

- Ввести `UnloadConfiguration` + `UnloadConfigurationLoader` (раздел 8).
- `AddUnloadRuntime(IServiceCollection, IConfiguration)` — единая точка.
- Убрать парсинг секций из `Unload.Api/Program.cs` и `Unload.Console/Program.cs`.
- Убрать дублирующий `ApiWorkspacePathResolver` / `WorkspacePathResolver`.

### Фаза 8 — Console и документация

- Обновить `Unload.Console`, `Unload.WebConsole` под новые namespace/типы
  (`TaskWorkflow.LaunchAsync` вместо `IWorkflowTaskDispatcher`).
- Переписать `README.md` и `docs/ARCHITECTURE.md` (сейчас устарели — упоминают `Unload.MQ`,
  sender-stub; реальность — `Unload.Gateway`/FTP).
- Обновить `postman/unload-api.postman_collection.json` если менялись контракты.
- `Unload.FtpServer`, `Unload.GatewayHandler` — проверить, что не сломаны (зависят от Gateway).

## 10. Риски

- **`RunStateProjector.TryPromoteToCompleted`** — ~100 строк логики завершения run по
  sender-feedback. Переносить **как есть**, не переписывать в рамках рефакторинга
  (отдельная задача). Любая правка здесь меняет момент перехода run в `Completed`.
- **Контракт `PresetGateState`/SignalR `preset_state`** — фронтенд (`web/webApp`) на него
  завязан. Если переименовывать в `DailyWindowState` — синхронно править Angular. Безопаснее
  имя DTO и событие SignalR не трогать.
- **Асимметрия main vs остальные**: `run` — deferred (канал + фоновый воркер), preset/extra/
  probe — синхронные. `TaskWorkflow` это учитывает: для `run` фиксацию завершения делает
  `MainUnloadHostedService`, не `LaunchAsync`.
- **Console-проекты** легко отстают — проверять сборку всего `unload.slnx`, не только API.
- Фазы 2–5 крупные; если фаза не помещается в зелёную сборку одним шагом — разрешено
  разбить, но не оставлять полусостояние между коммитами.

## 11. Вне скоупа

- Реальная БД/Gateway вместо development-заглушек.
- Переписывание логики завершения run (раздел 10).
- Изменение формата выходных файлов, каталога, bigScripts.
- UI/Angular кроме вынужденной синхронизации контрактов.
