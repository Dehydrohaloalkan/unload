# План сопровождаемости Unload

Статус: черновик рабочего плана  
Создан: 2026-08-03  
Цель: обеспечить понятную и безопасную поддержку проекта в течение пяти лет.

## 1. Исходное состояние

Сильные стороны проекта:

- backend разделён по зонам ответственности;
- graphify не обнаруживает циклических зависимостей;
- включены nullable reference types и строгий TypeScript;
- есть единая точка регистрации runtime-сервисов;
- README и архитектурная документация описывают основные бизнес-потоки.

Основные риски:

- backend практически не покрыт автоматическими тестами;
- frontend имеет только минимальные smoke-тесты приложения;
- документация уже расходится с кодом: `extra` описан как синхронный, но фактически выполняется как deferred-задача;
- `RunStateStore.cs` изначально совмещал хранение, конкурентные обновления, persistence и сложную проекцию статусов;
- существует много отдельных приложений и способов запуска;
- бизнес-логика напрямую использует системные часы;
- C# и TypeScript вручную дублируют API-контракты;
- ошибки JSON persistence логируются, но не передаются вызывающему коду.

## 2. Принципы изменений

1. Сначала зафиксировать текущее поведение тестами, затем менять структуру.
2. Делать небольшие изменения с зелёной сборкой после каждого шага.
3. Не вводить микросервисы, event sourcing, MediatR и интерфейсы для каждого класса.
4. Оставлять один очевидный путь для типовой операции.
5. Комментариями объяснять причины и инварианты, а не пересказывать код.
6. Не менять форматы output-файлов и пользовательские данные без отдельной задачи.
7. Не очищать `output/` и `output/_state` во время разработки и тестов.

## 3. Этап 0 — зафиксировать базовую линию

Приоритет: критический.

- [ ] Выполнить backend и frontend build через project skill `$build-check`.
- [ ] Выполнить основные live-сценарии через `$run-and-test-app`.
- [ ] Зафиксировать известные сценарии и текущее поведение `probe`, `preset`, `run`, `extra`.
- [ ] Зафиксировать текущее содержимое API-контрактов и SignalR-событий.
- [ ] Составить список реально используемых production-приложений.
- [ ] Не изменять и не удалять существующий `output/`.

Критерий готовности: проект собирается, базовые сценарии воспроизводимы, известна официальная production-конфигурация.

### Результат baseline-прохода 2026-08-07

- [x] Backend: `dotnet build` выполнен успешно, 0 warnings, 0 errors.
- [x] Frontend production build: канонический `npm run build` выполнен успешно 2026-08-07.
- [ ] Live-сценарии: API и Angular по отдельности доходят до состояния listening/build complete,
  но текущий sandbox запрещает bind дочерним процессам (`SocketException: Permission denied`,
  `listen EPERM`) и изолирует localhost между terminal-сессиями. Playwright-сценарии не запускались;
  повторить вне этого ограничения.
- [x] Историческая live-проверка от 2026-08-04 подтверждала Chromium-сценарии main/extra,
  FTP delivery `12/12`, восстановление активной Extra после refresh и отмену со статусом
  «отменено пользователем». Это полезная исходная точка, но не заменяет повторную проверку
  текущего рабочего дерева.
- [x] `output/` и `output/_state` не очищались и не изменялись тестовой очисткой.
- [x] Перед началом обнаружены пользовательские изменения в `AGENTS.md` и
  `docs/ARCHITECTURE.md`; они сохранены без перезаписи.

Baseline пока не считается завершённым: остаются live-сценарии и фиксация production-конфигурации.

### Точная матрица первых characterization-тестов

`DailyWindowPolicy`:

- [x] disabled gate: окно открыто, preset запускать нельзя, состояние сообщает об отключении;
- [x] значения `StartHour` и `StartMinute` ограничиваются диапазонами `0..23` и `0..59`;
- [x] `StartPolling` меняет состояние один раз, повторный вызов идемпотентен;
- [x] probe `0` сохраняет закрытое состояние, probe `1` разрешает preset;
- [x] preset запрещён до старта polling, до начала окна, при probe `0` и после выполнения;
- [x] границы окна включительны: ровно в start time и в `23:59`;
- [x] `MarkPresetCompleted` открывает main/extra только на текущий день;
- [x] смена даты сбрасывает completion, probe и polling, вновь закрывая main/extra;
- [x] `Get` и `RefreshDailyWindowState` не сообщают изменение без фактической смены состояния;
- [x] после дневного completion `StartPolling` не запускает polling повторно.

Для сценариев `CanRunPreset`, `MarkPresetCompleted` и смены даты требовалось сначала внедрить
стандартный `.NET TimeProvider` как минимальную тестовую точку, не меняя бизнес-правила: до этого
эти ветки напрямую читали `DateTime.Now`.

Реализовано 2026-08-07: `DailyWindowPolicy` получает `TimeProvider` через DI, runtime использует
`TimeProvider.System`, а 15 test cases используют ручное время. Текущее правило конца окна
зафиксировано без изменения: `23:59:00` входит в окно, `23:59:01` уже не входит. Требуется
отдельное бизнес-решение, должна ли вся последняя минута считаться открытой.

Стандартный xUnit-проект добавлен в solution и компилируется общим `dotnet build`. Штатный
`dotnet test` выполнен 2026-08-07: все 15 относящихся к этой итерации test cases
`DailyWindowPolicy` прошли.

`TaskWorkflow`:

- [x] неизвестный task code возвращает `VALIDATION_ERROR` и не вызывает задачу;
- [x] закрытое дневное окно возвращает `PRESET_GATE_BLOCKED` для main/extra;
- [x] закрытое preset-окно возвращает `PRESET_GATE_BLOCKED` с причиной policy;
- [x] отсутствующие `RequiresCompleted` возвращаются в `requiredTaskCodes`;
- [x] зависимости считаются только за текущую локальную дату;
- [x] активный main run блокирует второй run и задачи, конфликтующие с run;
- [x] активный extra блокирует второй extra и задачи, конфликтующие с extra;
- [x] foreground-конфликт проверяется симметрично по обеим декларациям `ConflictsWith`;
- [x] из двух конкурентных foreground-запусков конфликтующих задач проходит только один;
- [x] foreground-слот освобождается после success, exception и cancellation;
- [x] deferred-задача не удерживает foreground-слот после `ExecuteAsync` и полагается на activation channel;
- [x] `AdminOverride` обходит gate/dependency/conflict проверки, сохраняя фактическое выполнение задачи;
- [x] task code и конфликтные коды сравниваются без учёта регистра;
- [x] исходные request, cancellation token и execution result передаются без подмены.

Все файловые fixtures для `TaskExecutionHistoryStore` должны создаваться в отдельном scratch-каталоге;
реальный `output/` в этих тестах не используется.

`PresetCompletionRecovery`:

- [x] выполненный сегодня `preset` восстанавливает открытое дневное окно после рестарта;
- [x] вчерашний `preset` не восстанавливает окно;
- [x] disabled gate игнорирует историю;
- [x] повторное восстановление идемпотентно.

Реализовано 2026-08-07: правило извлечено из бесконечного цикла `ProbeSchedulerHostedService`,
использует общий `TimeProvider` и тестовый scratch-файл истории. Scheduler также использует
`TimeProvider` вместо прямого `DateTime.Now` при проверке времени запуска probe.

`RunStateStore` перед разделением:

- [x] `SetStarted` создаёт pending members и idle workers;
- [x] runner events проецируют worker, member и artifact без дубликатов;
- [x] `Completed`, `Failed`, `Cancelled` фиксируют terminal-состояние и сбрасывают workers;
- [x] запрос отмены игнорирует промежуточный progress, но принимает terminal event;
- [x] terminal state игнорирует поздние runner events и `SetRunning`;
- [x] при `PublishToGateway = false` запуск завершается сразу и создаёт skipped batches;
- [x] при включённом gateway runner completion ждёт полного sender feedback;
- [x] `FileSent` без `BatchCompleted` и `BatchCompleted` без `FileSent` оставляют запуск активным;
- [x] все artifacts всех members должны иметь завершённую доставку;
- [x] failed sender batch переводит запуск в `Failed`;
- [x] повторный `FileSent` идемпотентен;
- [x] неизвестный correlation ID получает task code по текущему правилу префиксов;
- [x] отсутствующий snapshot даёт пустое состояние;
- [x] повреждённый snapshot не перезаписывается автоматически;
- [x] `Running` и `CancellationRequested` после рестарта становятся `Cancelled`;
- [x] terminal snapshots сохраняют бизнес-поля после JSON round-trip;
- [x] persistence сохраняет version, task code и `PublishToGateway`.

Реализовано 2026-08-07: добавлены 27 test cases через публичный API `RunStateStore`.
Fixtures используют отдельный scratch-каталог; `output/` и `output/_state` не читаются и не очищаются.
Этот набор стал страховочной сеткой для последующего механического выделения projector и policy.

## 4. Этап 1 — автоматическая страховочная сетка

Приоритет: критический. Выполняется до архитектурного рефакторинга.

### 4.1 Backend-тесты

Создать один тестовый проект `tests/Unload.Backend.Tests` и покрыть:

- [x] `DailyWindowPolicy`: время до окна, начало окна, конец дня, смена даты;
- [x] восстановление выполненного `preset` после рестарта;
- [x] `TaskWorkflow`: зависимости задач;
- [x] конфликты `preset`, `run`, `extra`;
- [x] single-active для `run` и `extra`;
- [x] `AdminOverride`;
- [x] конкурентные попытки запуска;
- [ ] отмену deferred-задач;
- [x] `RunStateProjector`: все terminal-переходы;
- [x] завершение после sender-feedback;
- [x] `PublishToGateway = false`;
- [x] failed и неполный sender-feedback;
- [x] восстановление state после рестарта;
- [x] повреждённый или отсутствующий JSON snapshot;
- [ ] правила формирования имён и output-путей;
- [ ] catalog/script resolution.

Требования к тестам:

- имена отражают бизнес-сценарий;
- Arrange вынесен в небольшие builders/fixtures;
- тесты не используют реальный `output/`;
- время и файловая система контролируются тестом;
- для каждого исправленного бага сначала создаётся воспроизводящий тест.

### 4.2 Frontend-тесты

- [ ] Покрыть `history-projection.util.ts` табличными тестами;
- [ ] покрыть проекции main и extra history;
- [ ] покрыть gateway delivery и requeue summary;
- [ ] покрыть восстановление store после refresh;
- [ ] покрыть fallback polling при недоступном SignalR;
- [ ] добавить component-тесты запуска, отмены и отображения ошибки.

### 4.3 Сквозные проверки

- [ ] `probe -> preset -> run`;
- [ ] `probe -> preset -> extra`;
- [ ] параллельные `run` и `extra`;
- [ ] отмена активной задачи;
- [ ] refresh UI во время активной задачи;
- [ ] восстановление после рестарта API;
- [ ] gateway success и gateway failure.

Критерий готовности: критические бизнес-инварианты защищены тестами, тесты стабильно выполняются локально.

## 5. Этап 2 — документация быстрого входа

Приоритет: высокий.

- [x] Исправить описание deferred-поведения `extra` в `README.md` и `docs/ARCHITECTURE.md`.
- [x] Создать `docs/START_HERE.md` с первым запуском и точками входа.
- [x] Создать `docs/GLOSSARY.md` с терминами `run`, `main unload`, `runner`, `workflow`, `activation`, `preset gate`.
- [ ] Создать `docs/TROUBLESHOOTING.md`.
- [ ] Описать сценарии «run завис», «preset закрыт», «FTP не ответил», «state повреждён».
- [ ] Добавить recipes: новая задача, новый SQL, новый endpoint, изменение расписания, изменение UI-истории.
- [ ] Добавить диаграмму состояния `run/extra`.
- [ ] Добавить карту директорий с указанием, какие файлы обычно менять не нужно.
- [ ] Проверять метаданные задач тестом, чтобы документация не расходилась с кодом.

Критерий готовности: разработчик может запустить проект и найти место изменения без чтения всего репозитория.

## 6. Этап 3 — явная модель времени

Приоритет: высокий.

- [ ] Внедрить стандартный `.NET TimeProvider`.
- [ ] Убрать прямые обращения к `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` из бизнес-логики.
- [ ] Задать business timezone в конфигурации.
- [ ] Отдельно использовать UTC для persistence/events и business-local time для дневного окна.
- [ ] Перевести scheduler delays/timers на тестируемую абстракцию времени там, где это оправдано.
- [ ] Добавить тесты смены дня и рестарта около границы окна.

Критерий готовности: все временные сценарии воспроизводятся в тестах без ожидания реального времени.

## 7. Этап 4 — разделить RunStateStore

Приоритет: высокий. Начинать только после этапа 1.

Целевая структура:

```text
RunStateStore              — небольшой публичный фасад
RunStatePersistence        — загрузка и сохранение snapshot
RunStateProjector          — координация применения RunnerEvent
RunMemberProjector         — состояния мемберов
RunArtifactProjector       — список созданных файлов
RunWorkerProjector         — состояния workers
GatewayFeedbackProjector   — применение sender-feedback
RunCompletionPolicy        — чистое правило terminal-перехода
```

Шаги:

- [x] Перенести nested `RunStateProjector` в отдельный файл без изменения поведения.
- [x] Выделить `RunCompletionPolicy` и покрыть таблицей переходов.
- [x] Выделить gateway feedback projection.
- [x] Выделить простые member, artifact и worker projections.
- [ ] Оставить один публичный способ изменения state.
- [ ] Убрать распознавание task type по префиксу correlation ID или изолировать его в одном месте.
- [ ] Привести внутренние имена к единой терминологии execution/run/extra.

Критерий готовности: каждый класс решает одну задачу, правила завершения читаются отдельно и полностью покрыты тестами.

## 8. Этап 5 — надёжная persistence

Приоритет: высокий.

- [ ] Сериализовать mutations и сохранения через один writer/очередь.
- [ ] Гарантировать порядок сохранения snapshots.
- [ ] Не скрывать ошибку записи от приложения.
- [ ] Добавить backup последнего корректного snapshot.
- [ ] Повреждённый файл перемещать в quarantine с диагностикой.
- [ ] Не перезаписывать повреждённый state пустым snapshot автоматически.
- [ ] Добавить health check для state и истории.
- [ ] Добавить тесты конкурентных обновлений и аварийного восстановления.
- [ ] Оценить SQLite только после измерения объёма данных и частоты записей.

Критерий готовности: сбой persistence виден оператору, данные восстанавливаются предсказуемо, порядок событий не регрессирует.

## 9. Этап 6 — упростить API и frontend orchestration

Приоритет: средний.

### Backend

- [ ] Разделить `RunsController` по пользовательским операциям.
- [ ] Оставить endpoint-методы короткими: validation, вызов сервиса, mapping результата.
- [ ] Убрать повторяющееся преобразование `TaskLaunchException` из endpoint-ов.
- [ ] Не вводить отдельный use-case/interface для каждого метода без реальной необходимости.

Возможное разделение:

```text
RunLaunchController
RunStatusController
RunHistoryController
GatewayRequeueController
```

### Frontend

- [ ] Разделить history projection для `run`, `extra` и gateway.
- [ ] Сохранить orchestration в facade, но вынести чистые вычисления.
- [ ] Уменьшить количество обязанностей `workflow.facade.ts`.
- [ ] Не переносить бизнес-правила с backend во frontend.
- [ ] Помечать generated API-код как нередактируемый.

Критерий готовности: изменение одного пользовательского сценария затрагивает ограниченный набор очевидных файлов.

## 10. Этап 7 — единый API-контракт

Приоритет: средний.

- [ ] Включить публикацию OpenAPI schema.
- [ ] Генерировать TypeScript DTO и API client из OpenAPI.
- [ ] Исключить generated-файлы из ручного форматирования и review шума.
- [ ] Зафиксировать совместимость SignalR event names и payloads тестами.
- [ ] Добавить contract-тесты API/frontend.

Критерий готовности: C# и TypeScript модели не расходятся при ручном редактировании.

## 11. Этап 8 — определить поддерживаемые приложения

Приоритет: средний.

Для каждого приложения определить статус: production, diagnostic, development-only или obsolete.

- [ ] `Unload.Api`;
- [ ] Angular `webApp`;
- [ ] `Unload.Console`;
- [ ] `Unload.WebConsole`;
- [ ] `Unload.FtpServer`;
- [ ] `Unload.GatewayHandler`.

После решения:

- [ ] оставить один официальный production-путь;
- [ ] development-инструменты перенести в `tools/`;
- [ ] удалить неиспользуемые приложения;
- [ ] не дублировать бизнес-логику в клиентах;
- [ ] явно указать уровень поддержки каждого инструмента.

Критерий готовности: разработчик знает, какие приложения обязательны, а какие можно не учитывать при обычной задаче.

## 12. Этап 9 — автоматическая проверка и зависимости

Приоритет: средний.

- [ ] Создать единый `verify`-скрипт.
- [ ] Добавить CI для backend build/test и frontend build/test.
- [ ] Добавить format/analyzer checks.
- [ ] Добавить `Directory.Build.props`.
- [ ] Добавить `Directory.Packages.props` и выровнять версии `Microsoft.Extensions.*`.
- [ ] Добавить `global.json` для фиксации .NET SDK.
- [ ] Проверять lockfile через `npm ci`.
- [ ] Настроить контролируемые dependency-update PR.
- [ ] После source-изменений обновлять graphify через `./.tools/bin/graphify update .`.

Критерий готовности: потенциально опасное изменение нельзя незаметно передать дальше с красными тестами или сборкой.

## 13. Этап 10 — сократить количество проектов

Приоритет: низкий. Выполнять последним и только если это действительно упростит навигацию.

Возможная целевая структура:

```text
backend/
  Unload.Api
  Unload.Application
  Unload.Domain
  Unload.Infrastructure

web/
  webApp

tools/
  Unload.FtpServer
  Unload.GatewayHandler

tests/
  Unload.Backend.Tests
  Unload.Web.Tests
```

- [ ] Сначала построить текущий dependency map.
- [ ] Объединять проекты механически, без изменения поведения.
- [ ] Сохранять feature-папки внутри крупных проектов.
- [ ] После каждого объединения выполнять полную проверку.
- [ ] Не объединять проекты только ради уменьшения их числа.

Критерий готовности: структура уменьшает количество решений, которые должен принять разработчик при поиске нужного файла.

## 14. Этап 11 — эксплуатация и восстановление

Приоритет: средний.

- [ ] Добавить health endpoints для database, state, output и gateway.
- [ ] Документировать backup/restore `output/_state`.
- [ ] Проверить восстановление backup на тестовой копии.
- [ ] Добавить корреляцию логов по execution ID.
- [ ] Добавить наблюдаемую ошибку для зависшей deferred-задачи.
- [ ] Описать безопасную ручную разблокировку run/extra.
- [ ] Описать действия при недоступной БД и FTP.
- [ ] Задать retention логов, state и output отдельно.

Критерий готовности: типовую production-проблему можно диагностировать по готовому runbook.

## 15. Рекомендуемый порядок первых итераций

### Итерация 1

- [ ] Проверить build и live-сценарии.
- [x] Исправить документацию про deferred `extra`.
- [x] Создать test project.
- [x] Покрыть `DailyWindowPolicy`.
- [x] Внедрить `TimeProvider` только в `DailyWindowPolicy` и связанные тесты.

### Итерация 2

- [x] Покрыть `TaskWorkflow`.
- [x] Покрыть single-active и конкурентные запуски.
- [x] Добавить тесты restart recovery.
- [x] Создать `START_HERE.md` и `GLOSSARY.md`.

### Итерация 3

- [x] Покрыть `RunStateProjector` и completion rules.
- [x] Вынести projector и completion policy без изменения поведения.
- [ ] Проверить gateway success/failure live-сценариями.

### Итерация 4

- [ ] Усилить JSON persistence.
- [ ] Добавить health check.
- [ ] Добавить recovery/quarantine.
- [ ] Создать `TROUBLESHOOTING.md`.

### Итерация 5

- [ ] Добавить frontend projection/store tests.
- [ ] Разделить крупные frontend projections.
- [ ] Настроить OpenAPI generation.

### Итерация 6

- [ ] Определить статус Console/WebConsole/FTP tools.
- [ ] Добавить CI и единый verify.
- [ ] Оценить необходимость объединения проектов.

## 16. Точка продолжения

Состояние на 2026-08-07 после первого шага разделения `RunStateStore`:

Этот раздел — канонический самодостаточный checkpoint для продолжения после перезапуска.
Запись отдельной ad-hoc заметки в долговременную папку Codex была запрещена sandbox, поэтому
восстанавливать контекст нужно отсюда.

- Первая тестовая итерация зафиксирована коммитом `a48ac04` (`test: cover daily window policy`).
- Создан `tests/Unload.Backend.Tests` и подключён к `unload.slnx`.
- `DailyWindowPolicy` получает `TimeProvider`; DI по умолчанию регистрирует `TimeProvider.System`.
- Добавлены 15 test cases для `DailyWindowPolicy` и `ManualTimeProvider`.
- `TaskWorkflow` получает тот же `TimeProvider`; прямой `DateTime.Now` заменён без изменения правил.
- Добавлены 16 test cases для validation, gate, dependencies, active run/extra, симметричных
  конфликтов, конкурентного запуска, освобождения foreground-слота, deferred и `AdminOverride`.
- Добавлены 4 test cases `PresetCompletionRecovery`: today, yesterday, disabled и idempotency.
- `ProbeSchedulerHostedService` использует общий `TimeProvider` для восстановления и расписания.
- Добавлены 27 test cases `RunStateStore`: runner projection, terminal transitions, gateway
  completion, sender feedback и persistence/restart recovery.
- `RunStateProjector` механически перенесён из nested-класса в отдельный internal-файл.
- Чистый `RunCompletionPolicy` выделен отдельно и покрыт таблицей из 10 test cases.
- `GatewayFeedbackProjector` выделен отдельно и покрыт 7 test cases для mapping, путей,
  дедупликации, terminal feedback и неизменности исходной карты.
- `RunMemberProjector`, `RunArtifactProjector` и `RunWorkerProjector` выделены в небольшие
  самостоятельные правила и покрыты 12 прямыми test cases; recovery использует тот же worker reset.
- Общий `dotnet build` проходит: 0 warnings, 0 errors, тестовый проект компилируется.
- Штатный VSTest проходит: 91/91 относящихся к плану tests passed.
- Production frontend `npm run build` проходит.
- Поведение конца окна только зафиксировано: `23:59:00` разрешено, `23:59:01` запрещено.
  Не исправлять без отдельного бизнес-решения.
- `output/` и `output/_state` не очищались.

Следующая рекомендуемая задача:

> Завершить фасад `RunStateStore`: проверить и сократить внутренние пути изменения state,
> затем изолировать распознавание task type. Persistence оставить отдельным следующим этапом.

Восстановление `preset` теперь изолировано в `PresetCompletionRecovery`; не возвращать это правило
обратно в hosted service. Сквозной restart-сценарий API остаётся отдельной live-проверкой.

Не перезаписывать изменения пользователя, которые уже находились или появились в рабочем дереве:

- `AGENTS.md`;
- `backend/Unload.Api/Services/SenderFeedbackProjectionBackgroundService.cs`;
- `backend/Unload.Api/nlog.config`;
- основная переработка `docs/ARCHITECTURE.md` — наши добавления про `TimeProvider` сделаны поверх неё.

Файлы текущей итерации:

- `backend/Unload.Tasks/DailyWindowPolicy.cs`;
- `backend/Unload.Tasks/PresetCompletionRecovery.cs`;
- `backend/Unload.Tasks/TaskWorkflow.cs`;
- `backend/Unload.Tasks/UnloadTask.cs`;
- `backend/Unload.Tasks/DependencyInjection/ServiceCollectionExtensions.cs`;
- `backend/Unload.Api/Services/ProbeSchedulerHostedService.cs`;
- `tests/Unload.Backend.Tests/*`;
- `README.md`;
- `unload.slnx`;
- дополнения в `docs/MAINTAINABILITY_PLAN.md` и `docs/ARCHITECTURE.md` про тесты и время.

Перед началом изменений проверить:

```bash
git status --short
./.tools/bin/graphify query "Как после рестарта восстанавливается выполненный preset и какие классы и методы нужно покрыть characterization-тестом?"
```

После добавления тестов выполнить:

```bash
dotnet build
MSBUILDDISABLENODEREUSE=1 dotnet test tests/Unload.Backend.Tests/Unload.Backend.Tests.csproj \
  --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

Если VSTest снова завершится на `SocketServer: Permission denied`, считать это ограничением
sandbox, а не результатом test cases; не подменять постоянный тестовый фреймворк самодельным.

После каждого изменения source-кода:

```bash
./.tools/bin/graphify update .
```

Этот файл является рабочим журналом плана: выполненные пункты отмечаются здесь, а существенные решения дописываются рядом с соответствующим этапом.
