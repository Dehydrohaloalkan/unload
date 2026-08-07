# Словарь Unload

Словарь фиксирует единые значения терминов, используемых в интерфейсе, API, исходном коде и
документации. Технический поток каждого понятия раскрыт в [ARCHITECTURE.md](ARCHITECTURE.md),
а наблюдаемое пользователем поведение — в [USER_GUIDE.md](USER_GUIDE.md).

## Задачи и порядок запуска

### Task / задача

Одна операция pipeline с уникальным `TaskCode`. Задача объявляет зависимости и конфликты и
реализует `ExecuteAsync`. Текущие основные коды: `probe`, `preset`, `run`, `extra`.

### Task code

Стабильный машинный код задачи. Используется в API, истории, зависимостях, конфликтах и логах.
Сравнивается без учёта регистра, но в документации записывается строчными буквами.

### Workflow

Правила допуска и запуска задач, сосредоточенные в `TaskWorkflow`. Workflow проверяет gate,
`RequiresCompleted`, `ConflictsWith`, single-active и только затем вызывает задачу. Это не
отдельный background process и не Angular-компонент.

### Probe

Автоматическая проверка готовности данных к `preset`. Scheduler запускает SQL probe после начала
настроенного окна. Результат `1` разрешает `preset`, результат `0` оставляет его недоступным.

### Preset

Подготовительная синхронная задача текущего дня. Она выполняется после успешного `probe`, запускает
SQL из `scripts/preset` и открывает дневное окно для `run` и `extra`.

### Preset gate / дневное окно

Серверное правило доступности `preset`, `run` и `extra`. Состояние хранит `DailyWindowPolicy`.
Gate учитывает включённость, время, probe и выполнение preset в текущую локальную дату.

### Run / main unload / основная выгрузка

Основная выгрузка выбранных members или targets. `run` принимается быстро, а затем выполняется в
фоне через `RunActivationChannel`, hosted service и `MainUnloadEngine`.

### Extra / дополнительная выгрузка

Отдельная фоновая выгрузка по extra-скриптам. Может работать параллельно с `run`, но имеет свой
single-active slot, activation channel, hosted service, engine и правила выбора банков.

### Foreground-задача

Задача, выполнение которой завершается внутри вызова `TaskWorkflow.LaunchAsync`. Workflow держит
её локальный slot до success, exception или cancellation. Текущие примеры: `probe` и `preset`.

### Deferred-задача

Задача, которая принимает запрос, создаёт серверное состояние, помещает activation в канал и
возвращает `Accepted`, не ожидая полного результата. Жизненным циклом дальше управляет hosted
service. Текущие примеры: `run` и `extra`.

### Admin override

Явный режим обхода gate, dependency и conflict checks для административного запуска. Он не меняет
само исполнение задачи и не превращает ошибочный результат в успешный.

## Выполнение и состояние

### Execution

Один конкретный запуск задачи. Не путать с task: task описывает тип операции, execution — один её
экземпляр со временем, статусом и идентификатором.

### Correlation ID / execution ID

Идентификатор запуска, связывающий HTTP-ответ, состояние, события, логи, output и gateway feedback.
В существующих моделях встречаются оба названия; смысл — проследить один execution через систему.

### Activation

Сообщение о принятом deferred-запуске: correlation ID, payload и cancellation token. Activation
передаётся из задачи фоновому hosted service.

### Activation channel

In-memory канал между принимающей задачей и hosted service. `RunActivationChannel` и
`ExtraActivationChannel` одновременно обеспечивают single-active и маршрутизацию отмены.
Activation channel не является durable queue и очищается при рестарте процесса.

### Single-active

Инвариант «не более одного активного execution данного типа». Для `run` и `extra` он хранится в
их activation channels. Это не запрещает `run` и `extra` работать параллельно друг с другом.

### Status / lifecycle

Текущее состояние execution: например `Accepted`, `Running`, `Completed`, `Failed` или `Cancelled`.
Terminal status означает, что execution больше не продолжает работу.

### Snapshot

Сериализованное состояние на диске, из которого приложение восстанавливает представление после
рестарта. Основные snapshots лежат в `output/_state`.

### Run state

Полная серверная проекция `run` или `extra`: lifecycle, workers, scripts/members, artifacts и
gateway delivery. Источник истины — `RunStateStore` и `runs.json`.

### Task history

Компактная история завершённых задач в `TaskExecutionHistoryStore` и `task-history.json`.
Используется для dashboard, зависимостей «выполнено сегодня» и восстановления preset.

### Projection

Преобразование событий или сохранённых данных в состояние, удобное для чтения UI и API.
Projection не выполняет SQL-выгрузку и не должна повторно запускать задачу.

## Исполнение выгрузки

### Runner / engine

Компонент, который выполняет принятую выгрузку. В main-потоке эту роль выполняет
`MainUnloadEngine`; слово runner также встречается в настройках, событиях и исторических именах.
Workflow решает, можно ли начать, а engine выполняет уже разрешённую работу.

### Worker

Один параллельный исполнитель внутри engine. Worker получает script/target, читает данные и
формирует события выполнения. Количество workers задаётся настройками runner.

### Script

SQL-файл или логическая SQL-операция, которую выполняет задача. Scripts организованы по назначению
и группам в каталоге `scripts/`.

### Catalog

Описание groups, members, targets, SQL-сопоставлений и больших scripts в `configs/catalog.json`.
Catalog отвечает за состав доступных выборок, но не запускает их.

### Group

Логическая группа данных и scripts. Участвует в построении target-кодов и поиске SQL.

### Member

Пользовательская единица выбора основной выгрузки. Один member может соответствовать нескольким
targets в разных groups.

### Target

Конкретная исполняемая выборка, полученная из сочетания group и member. Engine работает с targets,
даже если пользователь выбрал members.

### Artifact / output file

Файл, созданный execution. Artifact имеет путь, размер, checksum и состояние отправки. Не путать
с snapshot: artifact является результатом выгрузки, snapshot — внутренним состоянием приложения.

## Gateway и доставка

### Gateway

Подсистема передачи сформированных файлов во внешний FTP-контур. Она отделена от выполнения SQL:
файл может быть успешно создан, но ещё не подтверждён gateway.

### Publish to gateway

Признак, нужно ли ставить artifacts текущего execution на отправку. При `false` файлы остаются
локальными и отсутствие sender feedback не мешает завершению.

### Sender batch

Группа файлов, отправляемая gateway как одна операция учёта. Batch имеет собственный идентификатор
и статусы доставки отдельных items.

### Sender feedback

Подтверждение результата отправки от gateway: успешно, ошибка или неполный результат. Feedback
проецируется обратно в run state и участвует в определении окончательного статуса.

### Requeue

Повторная постановка уже существующих artifacts в gateway без повторного SQL и создания файлов.

## Клиент и транспорт

### API

ASP.NET Core transport: принимает HTTP-запросы, преобразует ошибки и публикует состояние. API не
должен дублировать бизнес-решения `TaskWorkflow`.

### SignalR

Канал быстрых server-to-client обновлений состояния. Если он временно недоступен, frontend
использует HTTP polling; SignalR не является единственным хранилищем состояния.

### Store / frontend store

Клиентское состояние Angular. Stores загружают API snapshots, применяют SignalR updates и дают
компонентам готовое представление. Не путать с backend `Unload.Store`, который сохраняет серверное
состояние и историю.

### Selection mode

Способ интерпретации входных кодов основной выгрузки: `MemberCodes` разворачиваются через catalog,
а `TargetCodes` используются как точные исполняемые выборки.
