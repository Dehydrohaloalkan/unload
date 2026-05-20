import { isPlatformBrowser } from '@angular/common';
import { InjectionToken, PLATFORM_ID, inject } from '@angular/core';

export const BROWSER_STORAGE = new InjectionToken<Storage | null>('BROWSER_STORAGE', {
  providedIn: 'root',
  factory: () => (isPlatformBrowser(inject(PLATFORM_ID)) ? window.localStorage : null),
});
