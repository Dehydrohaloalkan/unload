import { formatDate } from '@angular/common';

export function formatTimestamp(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('ru-RU');
}

export function formatMoment(value: string | Date | null | undefined): string {
  if (!value) {
    return '...';
  }

  return formatDate(value, 'HH:mm:ss', 'ru-RU');
}

export function isTodayDate(value: string | null | undefined): boolean {
  if (!value) {
    return false;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return false;
  }

  const now = new Date();
  return (
    parsed.getFullYear() === now.getFullYear() &&
    parsed.getMonth() === now.getMonth() &&
    parsed.getDate() === now.getDate()
  );
}
