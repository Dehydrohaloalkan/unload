import { Injectable, isDevMode, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class AppErrorStore {
  readonly unhandledErrorMessage = signal<string | null>(null);

  clearUnhandledError(): void {
    this.unhandledErrorMessage.set(null);
  }

  setUnhandledError(error: unknown): void {
    this.unhandledErrorMessage.set(this.toErrorMessage(error));
  }

  private toErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim()) {
        return error.error;
      }

      const details = error.error as { detail?: string } | null;
      if (details?.detail && details.detail.trim()) {
        return details.detail;
      }
    }

    if (error instanceof Error && error.message.trim()) {
      return error.message;
    }

    return 'Произошла непредвиденная ошибка. Обновите страницу и повторите действие.';
  }
}

@Injectable()
export class GlobalAppErrorHandler {
  constructor(private readonly appErrorStore: AppErrorStore) {}

  handleError(error: unknown): void {
    this.appErrorStore.setUnhandledError(error);

    if (isDevMode()) {
      console.error(error);
    }
  }
}
