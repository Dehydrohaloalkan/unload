# Отчёт по аварийным сценариям Unload

Дата проверки: 2026-08-21.

Проверки выполняются только на тестовых двойниках и в уникальных каталогах системного `temp`.
Рабочие `output/` и `output/_state` не используются и не очищаются. Жёсткое завершение всей WSL
не является частью набора: перезапуск сервера воспроизводится повторным созданием хранилища из
того же snapshot.

## Итоги

| Сценарий | Как проверено | Фактический результат | Оценка |
| --- | --- | --- | --- |
| Каталог скриптов недоступен | `MainUnloadChaosTests.FailureAfterEventStreamStarted_EndsWithFailedEvent` | Запуск получает терминальное событие `Failed`, `Completed` отсутствует | OK |
| SQL-запрос отказал после старта | тот же theory-тест | Запуск получает `Failed` с исходной причиной, `Completed` отсутствует | OK |
| Запись файла завершилась ошибкой | тот же theory-тест | Запуск получает `Failed`, ложного успеха нет | OK |
| Публикация в шлюз завершилась ошибкой | theory-тест и `GatewayFailure_LeavesWrittenArtifactAndEndsWithFailedEvent` | Локальный файл остаётся доступен, итог запуска — `Failed`, ложного `Completed` нет | OK, возможен ручной повтор отправки |
| Пользователь отменил долгий SQL-запрос | `CancellationDuringDatabaseQuery_CancelsEventStreamForHostedService` | Чтение event stream завершается `OperationCanceledException`; hosted service ловит его и сохраняет `Cancelled` | OK |
| БД уже отключена до запуска event stream | `DisconnectedDatabaseBeforeEventStream_CurrentlyProducesNoTerminalEvent` | Поток завершается без `Failed` и без `Completed` | Критический пробел: агрегат может остаться `Running` |
| Нельзя создать корневой каталог результата | `UnwritableOutputRootBeforeEventStream_CurrentlyProducesNoTerminalEvent` | Поток завершается без терминального события | Критический пробел: агрегат может остаться `Running` |
| API перезапущен во время активной выгрузки | `RunStateStorePersistenceTests.ActiveStateAfterRestart_BecomesCancelledAndResetsWorkers` | После загрузки snapshot запуск становится `Cancelled`, worker-слоты сбрасываются | OK; вычисление автоматически не продолжается |
| API перезапущен после терминального состояния | `RunStateStorePersistenceTests.TerminalStateAfterRestart_IsPreserved` | `Completed`, `Failed` и `Cancelled` сохраняются без изменения | OK |
| Snapshot состояния повреждён | `RunStateStorePersistenceTests.CorruptedSnapshot_IsQuarantinedAndBlocksEmptyStateOverwrite` | Файл помещается в quarantine, store блокирует запись поверх данных | OK, запуск новых задач заблокирован до восстановления |
| State-файл недоступен для записи | `PersistenceDegradedModeTests` | Первая mutation остаётся в памяти, дальнейшие изменения блокируются, health становится `Degraded` | Защитное поведение; нужен рестарт после устранения причины |
| Параллельные записи состояния | `RunStateStorePersistenceTests.ConcurrentStarts_PersistEveryRunWithoutSnapshotRegression` | После рестарта восстановлены все записи | OK |
| SignalR оборван во время запуска | `RunStore` включает polling каждые 2,5 секунды при активном запуске и отключённом hub | Состояние должно добираться по HTTP, после reconnect выполняется повторная подписка | Реализовано, но отдельного автоматического fault-теста пока нет |

## Главные выводы

1. Отказы после создания `RunnerEventEmitter` корректно доходят до UI и хранилища как терминальный
   результат.
2. Два отказа происходят слишком рано: проверка подключения к БД и создание каталогов выполняются
   до создания emitter. Исключение поглощается внутри движка, поэтому hosted service не получает ни
   событие, ни исключение и может оставить запуск в `Running`.
3. После падения процесса незавершённая выгрузка не продолжается с середины. При следующем запуске
   API persisted-состояние помечается `Cancelled` с причиной
   `Run was interrupted due to server restart.`; частично созданные файлы не удаляются автоматически.

## Последний воспроизводимый прогон

- `MainUnloadChaosTests`: 8/8.
- `RunStateStorePersistenceTests` и `PersistenceDegradedModeTests`: 11/11.
- Полный `Unload.Backend.Tests`: 134/134.
- Полный frontend test suite: 32/32.
- Backend build: 0 ошибок, 0 предупреждений.
- Frontend build: успешно; сохранено существующее предупреждение бюджета `app.css`.

## Рекомендуемый следующий шаг

Создавать `RunnerEventEmitter` до проверки БД и каталогов либо пробрасывать раннее исключение в
`MainUnloadHostedService`, затем заменить два characterization-теста на ожидание терминального
`Failed`. Отдельно стоит добавить frontend-тест, который принудительно разрывает SignalR и проверяет
HTTP polling и восстановление подписки.
