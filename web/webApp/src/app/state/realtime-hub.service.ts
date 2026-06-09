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

@Injectable({ providedIn: 'root' })
export class RealtimeHubService {
  private readonly baseUrl = inject(API_BASE_URL);
  private connection: HubConnection | null = null;
  private currentCorrelationId: string | null = null;

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
      .withAutomaticReconnect()
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
    connection.onclose(() => this.connectionReady.set(false));
    connection.onreconnected(async () => {
      this.connectionReady.set(true);
      await this.subscribeRun(this.currentCorrelationId);
      this.reconnectedSubject.next();
    });

    try {
      await connection.start();
      this.connection = connection;
      this.connectionReady.set(true);
      await this.subscribeRun(this.currentCorrelationId);
    } catch (error) {
      this.connectionReady.set(false);
      if (isDevMode()) {
        console.error(error);
      }
    }
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
