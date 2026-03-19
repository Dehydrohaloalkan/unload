import { registerLocaleData } from '@angular/common';
import localeRu from '@angular/common/locales/ru';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

registerLocaleData(localeRu, 'ru-RU');

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
