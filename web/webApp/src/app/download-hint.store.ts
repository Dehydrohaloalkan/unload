import { Injectable, signal } from '@angular/core';
import { t } from './i18n/i18n';

@Injectable({ providedIn: 'root' })
export class DownloadHintStore {
  readonly message = signal<string | null>(null);
  readonly visible = signal(false);

  private hideTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly exitMs = 200;

  notifyDownloadStarted(): void {
    this.message.set(t('downloadHint.started'));
    this.visible.set(false);
    setTimeout(() => {
      this.visible.set(true);
      this.restartTimer();
    }, 0);
  }

  clear(): void {
    this.clearTimer();
    this.visible.set(false);
    setTimeout(() => {
      this.message.set(null);
    }, this.exitMs);
  }

  private restartTimer(): void {
    this.clearTimer();
    this.hideTimer = setTimeout(() => {
      this.visible.set(false);
      setTimeout(() => {
        this.message.set(null);
      }, this.exitMs);
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

