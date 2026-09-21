/** Форматтеры представления процесса не зависят от времени системы и безопасны для snapshot-данных. */
export function formatProcessDuration(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value) || value < 0) {
    return '';
  }

  const totalSeconds = Math.floor(value / 1_000);
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  const seconds = totalSeconds % 60;
  const clock = [hours, minutes, seconds].map((part) => String(part).padStart(2, '0')).join(':');
  return days > 0 ? `${days}д ${clock}` : clock;
}

export function formatProcessBytes(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value) || value < 0) {
    return '';
  }

  const units = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
  let size = value;
  let unit = 0;
  while (size >= 1_024 && unit < units.length - 1) {
    size /= 1_024;
    unit += 1;
  }

  const digits = unit === 0 || size >= 100 ? 0 : size >= 10 ? 1 : 2;
  return `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: digits }).format(size)} ${units[unit]}`;
}

export function formatProcessCount(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value) || value < 0) {
    return '';
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 }).format(value);
}

export function getElapsedSince(timestamp: string | null | undefined, now: Date): number | null {
  if (!timestamp || !Number.isFinite(now.getTime())) {
    return null;
  }

  const startedAt = Date.parse(timestamp);
  return Number.isFinite(startedAt) ? Math.max(0, now.getTime() - startedAt) : null;
}
