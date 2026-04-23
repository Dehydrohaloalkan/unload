import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class DownloadHintStore {
  readonly message = signal<string | null>(null);

  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  notifyDownloadStarted(): void {
    this.message.set('Скачивание началось. Загрузка начнется через пару секунд.');
    this.restartTimer();
  }

  clear(): void {
    this.message.set(null);
    this.clearTimer();
  }

  private restartTimer(): void {
    this.clearTimer();
    this.hideTimer = setTimeout(() => {
      this.message.set(null);
      this.hideTimer = null;
    }, 5000);
  }

  private clearTimer(): void {
    if (this.hideTimer) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
  }
}

