---
name: run-and-test-app
description: Запустить это приложение (Unload) локально и проверить изменения в реальном браузере. Используй, когда нужно поднять backend+frontend, прокликать UI экстра-выгрузки/истории/основного запуска, снять скриншоты или подтвердить, что фикс работает живьём. БД не нужна — backend всегда использует стаб (StubDatabaseClient).
---

# Запуск и e2e-проверка приложения Unload

Поднимает .NET API + Angular SPA и гоняет headless-браузер (Playwright) для визуальной
проверки UI. **Реальная БД не нужна:** `DatabaseClientFactory` всегда отдаёт
`StubDatabaseClient`, который сам сеет данные.

## Ключевые факты об окружении

- **Стаб-БД** (`backend/Unload.DataBase/Services/StubDatabaseClient.cs`) генерит всё по маркерам в SQL:
  - `EXTRA_BANKS` → 6 банков: `B01 Альфа-Банк, B02 Бета-Банк, B03 Гамма-Банк, B04 Дельта-Банк, B05 Эпсилон-Банк, B06 Дзета-Банк`.
  - `EXTRA_UNLOAD` → 50 строк на банк; уважает `IN ('B01',...)`, т.е. снятие банков реально уменьшает вывод.
  - `PRESET_READY_PROBE` → случайно 0/1 (поэтому preset-гейт недетерминирован — обходи админ-режимом).
  - прочие запросы → 2500 строк с `Thread.Sleep(10мс)`/строку (основной run идёт ~25с).
  - extra-набор **без** задержки → выгрузка завершается <1с (см. трюк «замедлить стаб» ниже).
- **Порты:** backend `http://localhost:5000`, Angular `http://localhost:4200` (проксирует `/api` и `/hubs` на 5000 через `web/webApp/proxy.conf.json`). Заходить надо на **4200**.
- **Корень workspace** ищется вверх по дереву до папки с `configs/catalog.json` + `scripts/`. Вывод — в `output/` (runtime, под gitignore; **там лежат реальные прогоны пользователя — не удалять**).
- **Админ-режим** (обходит preset-гейт и дневное окно): пароль = текущие `HHMM` (часы+минуты, локальное время). В UI: кнопка «Админ-режим» → ввод пароля → «Войти».
- **Состояние** запусков персистится в `output/_state/runs.json`. Если сегодня уже была extra — кнопка «Запустить extra» спросит подтверждение (диалог `p-confirmdialog`, кнопка «Запустить»). Учитывай это в скриптах. Чистить `_state` не стоит — затрёшь историю пользователя.

## Шаг 1. Поднять стек

```bash
# backend (из репозитория)
dotnet build backend/Unload.Api/Unload.Api.csproj -clp:NoSummary
# запускать в фоне, рабочая папка = backend/Unload.Api:
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 \
  dotnet run --no-build --project backend/Unload.Api/Unload.Api.csproj

# frontend (в фоне)
cd web/webApp && npx ng serve --port 4200
```

Дождаться готовности опросом (не спать вслепую):
```bash
for i in $(seq 1 30); do c=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/runs/today); [ "$c" = 200 ] && echo READY && break; sleep 2; done
curl -s http://localhost:5000/api/runs/extra/banks   # должно вернуть 6 банков
```

## Шаг 2. Поставить Playwright (одноразово, в скретч-папке вне репо-исходников)

```bash
mkdir -p .test-e2e && cd .test-e2e && npm init -y >/dev/null
npm i -D playwright@latest
npx playwright install chromium
npx playwright install chromium-headless-shell   # ВАЖНО: headless-shell ставится отдельно, иначе launch() падает
```
Скопируй сюда скрипты из `scripts/` этого скила и запускай `node <script>.js`.
Скриншоты — в `.test-e2e/shots/`, смотри их через Read.

## Шаг 3. Рецепты проверки

- **Вид панели экстра / выбор банков** (`scripts/extra-smoke.js`): открыть app → админ-режим → открыть карточку «4. Extra» (кнопка «Подробнее» внутри `app-extra-card`) → скриншот. Проверяет: чекбокс «Отправлять в шлюз» сверху, «Выбрать все» с indeterminate, банки одним списком.
- **Имя банка в истории** (`scripts/extra-smoke.js`): запустить extra (шлюз можно выключить, чтобы не зависеть от FTP) → вкладка «История» → развернуть запуск→скрипт→банк. В дереве должны быть названия (Альфа-Банк), а не коды (B01).
- **Восстановление после refresh** (`scripts/extra-recovery.js`): стартовать долгую extra через API с `adminOverride`, затем открыть страницу заново и убедиться, что активная выгрузка видна и останавливается (а не «пропадает»).
- **Бейдж шлюза**: для зелёного «доставлено» нужен живой FTP-шлюз — поднять `console/Unload.FtpServer` (порт/учётка из `appsettings.Development.json` → `Gateway:Ftp`). Жёлтый «частично»/красный «ошибка» теперь только при реально упавшей партии (`SenderBatchStatus.Failed`); скрипты с 0 файлов на бейдж не влияют.

### Старт extra в обход гейта (надёжнее кнопки)
```bash
curl -s -X POST http://localhost:5000/api/runs/extra \
  -H "Content-Type: application/json" \
  -d '{"adminOverride":true,"publishToGateway":false,"selectedBanks":null}'
# selectedBanks: null = все банки (базовые скрипты); ["B01","B02"] = подмножество (atomic).
```
Статус запуска: `GET /api/runs/today` (extra = `taskCode:"extra"`, `status`: 0 Running, 1 Completed, 2 Failed, 3 Cancelled, 4 CancellationRequested). Остановить: `POST /api/runs/{correlationId}/stop`.

### Трюк «поймать refresh во время запуска»
Extra-стаб быстрый. Чтобы выгрузка длилась ~20с, временно добавь задержку в
`StubDatabaseClient.CreateExtraUnloadReader` (в цикл по строкам — `Thread.Sleep(40);`),
пересобери и перезапусти backend. **После теста обязательно откати:**
`git checkout -- backend/Unload.DataBase/Services/StubDatabaseClient.cs`.

## Шаг 4. Селекторы/подписи (i18n, `web/webApp/src/app/i18n/ru.ts`)

- Кнопка админа: `Админ-режим` / `Админ-режим: ВКЛ`; submit — `Войти`; поле — `#admin-password`.
- Открыть детали карточки: внутри `app-extra-card` (или `app-run-card`) кнопка с aria-label `Подробнее`.
- Drawer: `aside.details-drawer`; вкладки `Выгрузка` / `История`.
- Чекбоксы PrimeNG: кликать `label.details-check-row` (по тексту `Отправлять в шлюз` / `Выбрать все`) → внутренний `.p-checkbox`.
- Банк-строки: `.bank-item` (внутри `.p-checkbox`). Кнопка запуска: `Запустить extra`. Стоп: `Остановить выгрузку`.
- История: `.history-run__summary` → `.history-script .history-member__summary` → `.history-bank .history-member__summary` (раскрываются как `<details>`).

## Шаг 5. Уборка

```bash
# остановить серверы (Windows): убить процессы на портах 5000 и 4200
# откатить любые временные правки стаба:
git checkout -- backend/Unload.DataBase/Services/StubDatabaseClient.cs
# удалить скретч-папку:
rm -rf .test-e2e
```
Проверить чистоту: `git status --porcelain` — должны остаться только намеренные изменения.
Прогоны добавят папки в `output/` — это runtime (gitignore), трогать не обязательно.
