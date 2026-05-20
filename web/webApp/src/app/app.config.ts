import {
  ApplicationConfig,
  ErrorHandler,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { definePreset } from '@primeuix/themes';
import Material from '@primeuix/themes/material';

import { routes } from './app.routes';
import { GlobalAppErrorHandler } from './app.error-store';
import { httpLoggingInterceptor } from './http-logging.interceptor';
import { WorkflowStore } from './state/workflow.facade';
import { PRIMARY_PALETTE } from './theme/primary-palette';

const UnloadTheme = definePreset(Material, {
  semantic: {
    primary: PRIMARY_PALETTE,
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
    ConfirmationService,
    provideAppInitializer(() => {
      inject(WorkflowStore).init();
    }),
  ],
};
