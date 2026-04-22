import {
  ApplicationConfig,
  ErrorHandler,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { definePreset } from '@primeuix/themes';
import Material from '@primeuix/themes/material';

import { routes } from './app.routes';
import { GlobalAppErrorHandler } from './app.error-store';
import { httpLoggingInterceptor } from './http-logging.interceptor';

const UnloadTheme = definePreset(Material, {
  semantic: {
    primary: {
      50: '#eff6ff',
      100: '#dbeafe',
      200: '#bfdbfe',
      300: '#93c5fd',
      400: '#60a5fa',
      500: '#3b82f6',
      600: '#2563eb',
      700: '#1d4ed8',
      800: '#1e40af',
      900: '#1e3a8a',
      950: '#172554',
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalAppErrorHandler },
    { provide: LOCALE_ID, useValue: 'ru-RU' },
    provideHttpClient(withFetch(), withInterceptors([httpLoggingInterceptor])),
    provideRouter(routes),
    providePrimeNG({
      ripple: true,
      theme: {
        preset: UnloadTheme,
        options: {
          darkModeSelector: false,
        },
      },
    }),
  ],
};
