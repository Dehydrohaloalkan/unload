# Unload WebApp

Angular-клиент для текущего `Unload.Api`.

## Что умеет UI

- показывает реальное время и live-состояние SignalR-подключения;
- синхронизирует часы через `GET /api/system/time`, чтобы UI показывал backend-время;
- отображает `preset_state` и открывает запуск `preset`, когда probe разрешает переход;
- после успешного `preset` открывает рабочий экран с карточками `run` и `extra`;
- запускает `run` по выбранным `memberCodes`;
- восстанавливает активный `run` и состояние preset-гейта после перезагрузки страницы;
- красит мемберов по статусам и показывает сокращенные логи по hover;
- запускает `extra` и показывает локальный индикатор выполнения с таймером.

## Стек

- Angular 21 standalone
- PrimeNG 21
- Tailwind CSS 4
- SignalR client `@microsoft/signalr`

## Локальный запуск

1. Подними API:

```powershell
dotnet run --project .\backend\Unload.Api\Unload.Api.csproj
```

2. В отдельном терминале запусти web-клиент:

```powershell
cd .\web\webApp
npm start
```

3. Открой `http://localhost:4200`.

Dev-server использует `proxy.conf.json`, поэтому запросы к `/api` и `/hubs/status` проксируются на `http://localhost:5000` без отдельной CORS-настройки backend.

## Архитектура frontend

- `src/app/app.store.ts` — единый store для REST, SignalR, localStorage и derived state.
- `src/app/app.error-store.ts` — глобальная обработка непойманных runtime-ошибок Angular и единый сигнал для отображения в UI.
- `src/app/components/live-clock.component.ts` — часы и индикатор live-канала.
- `src/app/components/preset-stage.component.ts` — стартовый экран `probe -> preset`.
- `src/app/components/run-card.component.ts` — запуск `run`, группировка мемберов и live-статусы.
- `src/app/components/extra-card.component.ts` — упрощенная карточка `extra`.
- `src/app/components/liquid-transition.component.ts` — анимированный переход между экранами.

## Контракты API

UI использует только текущие backend-контракты:

- `GET /api/catalog`
- `GET /api/members`
- `GET /api/system/time`
- `GET /api/runs/preset/state`
- `POST /api/runs/preset`
- `POST /api/runs`
- `POST /api/runs/extra`
- `GET /api/runs/active`
- `GET /api/runs/{correlationId}`
- `POST /api/runs/{correlationId}/stop`
- SignalR hub `/hubs/status` и события `preset_state`, `run_status`, `status`

## Ограничение текущего backend-контракта

Для `extra` сейчас нет отдельного live-state endpoint/event. Поэтому после перезагрузки страницы UI может честно восстановить только последнее локально известное состояние extra-задачи, но не подтвердить её фактический progress на backend без расширения API.
