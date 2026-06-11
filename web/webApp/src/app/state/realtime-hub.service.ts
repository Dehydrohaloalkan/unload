import { Injectable, inject, isDevMode, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { PresetGateState, RunStatusInfo, RunnerEvent } from '../app.models';
import { API_BASE_URL } from './api-base-url.token';
import { joinApiUrl } from './utils/api-url.util';

const RECONNECT_DELAY_MS = 5000;

@Injectable({ providedIn: 'root' })
export class RealtimeHubService {
  private readonly baseUrl = inject(API_BASE_URL);
  private connection: HubConnection | null = null;
  private currentCorrelationId: string | null = null;
  private restartTimerId: ReturnType<typeof setTimeout> | null = null;

  readonly connectionReady = signal(false);

  private readonly statusEventsSubject = new Subject<RunnerEvent>();
  private readonly runStatusEventsSubject = new Subject<RunStatusInfo>();
  private readonly presetStateEventsSubject = new Subject<PresetGateState>();
  private readonly reconnectedSubject = new Subject<void>();

  readonly statusEvents$ = this.statusEventsSubject.asObservable();
  readonly runStatusEvents$ = this.runStatusEventsSubject.asObservable();
  readonly presetStateEvents$ = this.presetStateEventsSubject.asObservable();
  readonly reconnected$ = this.reconnectedSubject.asObservable();

  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(joinApiUrl(this.baseUrl, '/hubs/status'))
      // Бесконечные ретраи: стандартная политика сдаётся после ~40 секунд, и после
      // гибернации/долгого обрыва соединение умирало навсегда до ручного refresh.
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => RECONNECT_DELAY_MS })
      .build();

    // Доставляем все события всем подписчикам; разделение run/extra делают сторы по taskCode/correlationId.
    connection.on('status', (event: RunnerEvent) => {
      this.statusEventsSubject.next(event);
    });

    connection.on('run_status', (status: RunStatusInfo) => {
      this.runStatusEventsSubject.next(status);
    });

    connection.on('preset_state', (state: PresetGateState) => {
      this.presetStateEventsSubject.next(state);
    });

    connection.onreconnecting(() => this.connectionReady.set(false));
    // Закрытие — страховка на случай, если соединение всё же закрылось (например,
    // сервер явно разорвал его): перезапускаем вручную, авто-reconnect здесь уже не работает.
    connection.onclose(() => {
      this.connectionReady.set(false);
      this.scheduleRestart();
    });
    connection.onreconnected(async () => {
      this.connectionReady.set(true);
      await this.subscribeRun(this.currentCorrelationId);
      this.reconnectedSubject.next();
    });

    this.connection = connection;
    await this.startWithRecoveryAsync(connection);
  }

  /**
   * Стартует соединение; при неудаче планирует повтор. После успешного ручного
   * (пере)запуска эмитит reconnected$, чтобы сторы добрали пропущенное состояние.
   */
  private async startWithRecoveryAsync(connection: HubConnection): Promise<void> {
    try {
      await connection.start();
      this.connectionReady.set(true);
      await this.subscribeRun(this.currentCorrelationId);
      this.reconnectedSubject.next();
    } catch (error) {
      this.connectionReady.set(false);
      if (isDevMode()) {
        console.error(error);
      }
      this.scheduleRestart();
    }
  }

  private scheduleRestart(): void {
    if (this.restartTimerId !== null) {
      return;
    }
    this.restartTimerId = setTimeout(() => {
      this.restartTimerId = null;
      const connection = this.connection;
      if (!connection || connection.state !== HubConnectionState.Disconnected) {
        return;
      }
      void this.startWithRecoveryAsync(connection);
    }, RECONNECT_DELAY_MS);
  }

  async subscribeRun(correlationId: string | null): Promise<void> {
    this.currentCorrelationId = correlationId;
    if (!correlationId || !this.connection || this.connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('SubscribeRun', correlationId);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

}
