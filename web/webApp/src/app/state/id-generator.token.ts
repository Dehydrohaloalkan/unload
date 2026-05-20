import { InjectionToken } from '@angular/core';

export type IdGenerator = () => string;

function defaultId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `id-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

export const ID_GENERATOR = new InjectionToken<IdGenerator>('ID_GENERATOR', {
  providedIn: 'root',
  factory: () => defaultId,
});
