import { Pipe, PipeTransform } from '@angular/core';
import { I18nKey, RU } from './ru';

export type I18nParams = Record<string, string | number>;

/**
 * Возвращает строку по ключу из {@link RU}. Если в шаблоне строки есть
 * плейсхолдеры вида `{name}`, они подставляются из `params`.
 */
export function t(key: I18nKey, params?: I18nParams): string {
  const template = RU[key];
  if (!params) {
    return template;
  }
  return template.replace(/\{(\w+)\}/g, (_, name: string) => {
    const value = params[name];
    return value === undefined || value === null ? '' : String(value);
  });
}

@Pipe({ name: 't', standalone: true, pure: true })
export class TPipe implements PipeTransform {
  transform(key: I18nKey, params?: I18nParams): string {
    return t(key, params);
  }
}
