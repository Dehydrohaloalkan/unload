import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class WorkflowErrorStore {
  readonly errorMessage = signal<string | null>(null);

  setError(message: string | null): void {
    this.errorMessage.set(message);
  }

  clear(): void {
    this.errorMessage.set(null);
  }
}
