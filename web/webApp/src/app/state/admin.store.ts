import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AdminStore {
  readonly adminMode = signal(false);

  setAdminMode(enabled: boolean): void {
    this.adminMode.set(enabled);
  }
}
