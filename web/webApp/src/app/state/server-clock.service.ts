import { isPlatformBrowser } from '@angular/common';
import { DestroyRef, Injectable, PLATFORM_ID, inject, isDevMode, signal } from '@angular/core';
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
  private initialized = false;

  readonly currentTime = signal(new Date());
  readonly timeZoneId = signal<string | null>(null);

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
      this.timeZoneId.set(serverTime.timeZoneId);
      this.tick();
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  applyFromResponse(serverLocalTime: string, timeZoneId: string): void {
    this.offsetMs = new Date(serverLocalTime).getTime() - Date.now();
    this.timeZoneId.set(timeZoneId);
    this.tick();
  }

  private tick(): void {
    this.currentTime.set(new Date(Date.now() + this.offsetMs));
  }
}
