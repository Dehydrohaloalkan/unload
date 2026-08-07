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
import { routes } from './app.routes';
import { GlobalAppErrorHandler } from './app.error-store';
import { httpLoggingInterceptor } from './http-logging.interceptor';
import { WorkflowStore } from './state/workflow.facade';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalAppErrorHandler },
    { provide: LOCALE_ID, useValue: 'ru-RU' },
    provideHttpClient(withFetch(), withInterceptors([httpLoggingInterceptor])),
    provideRouter(routes),
    provideAppInitializer(() => {
      inject(WorkflowStore).init();
    }),
  ],
};
