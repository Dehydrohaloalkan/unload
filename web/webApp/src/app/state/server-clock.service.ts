import { isPlatformBrowser } from '@angular/common';
import { DestroyRef, Injectable, PLATFORM_ID, inject, isDevMode, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { ApiClientService } from './api-client.service';

const TICK_MS = 1000;
const SYNC_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class ServerClockService {
  private readonly api = inject(ApiClientService);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);
  private readonly destroyRef = inject(DestroyRef);

  private offsetMs = 0;
  private serverUtcOffsetMinutes = 0;
  private trackedServerDay: string | null = null;
  private serverReferenceReady = false;
  private initialized = false;

  private readonly dayChangedSubject = new Subject<void>();

  readonly currentTime = signal(new Date());
  readonly timeZoneId = signal<string | null>(null);
  readonly dayChanged$ = this.dayChangedSubject.asObservable();

  init(): void {
    if (this.initialized || !this.browser) {
      return;
    }

    this.initialized = true;
    this.tick();

    const tickHandle = window.setInterval(() => this.tick(), TICK_MS);
    const syncHandle = window.setInterval(() => void this.syncAsync(), SYNC_MS);

    this.destroyRef.onDestroy(() => {
      window.clearInterval(tickHandle);
      window.clearInterval(syncHandle);
    });
  }

  async syncAsync(): Promise<void> {
    try {
      const serverTime = await this.api.fetchServerTime();
      this.offsetMs = new Date(serverTime.serverLocalTime).getTime() - Date.now();
      this.serverUtcOffsetMinutes = Number(serverTime.utcOffsetMinutes);
      this.timeZoneId.set(serverTime.timeZoneId);
      this.serverReferenceReady = true;
      this.tick();
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  applyFromResponse(
    serverLocalTime: string,
    timeZoneId: string,
    utcOffsetMinutes: number | string,
  ): void {
    this.offsetMs = new Date(serverLocalTime).getTime() - Date.now();
    this.serverUtcOffsetMinutes = Number(utcOffsetMinutes);
    this.timeZoneId.set(timeZoneId);
    this.serverReferenceReady = true;
    this.tick();
  }

  private tick(): void {
    const now = new Date(Date.now() + this.offsetMs);
    this.currentTime.set(now);

    if (!this.serverReferenceReady) {
      return;
    }

    const shifted = new Date(now.getTime() + this.serverUtcOffsetMinutes * 60_000);
    const serverDay = [
      shifted.getUTCFullYear(),
      String(shifted.getUTCMonth() + 1).padStart(2, '0'),
      String(shifted.getUTCDate()).padStart(2, '0'),
    ].join('-');

    if (this.trackedServerDay === null) {
      this.trackedServerDay = serverDay;
      return;
    }
    if (serverDay !== this.trackedServerDay) {
      this.trackedServerDay = serverDay;
      this.dayChangedSubject.next();
    }
  }
}
