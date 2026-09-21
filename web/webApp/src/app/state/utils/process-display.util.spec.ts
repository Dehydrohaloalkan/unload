import {
  formatProcessBytes,
  formatProcessCount,
  formatProcessDuration,
  getElapsedSince,
} from './process-display.util';

describe('process display helpers', () => {
  it('formats durations without reading the system clock', () => {
    expect(formatProcessDuration(0)).toBe('00:00:00');
    expect(formatProcessDuration(61_000)).toBe('00:01:01');
    expect(formatProcessDuration(90_061_000)).toBe('1д 01:01:01');
    expect(formatProcessDuration(null)).toBe('');
  });

  it('formats bytes and counts safely', () => {
    expect(formatProcessBytes(999)).toBe('999 Б');
    expect(formatProcessBytes(1_536)).toBe('1,5 КБ');
    expect(formatProcessBytes(-1)).toBe('');
    expect(formatProcessCount(12_345)).toBe('12 345');
    expect(formatProcessCount(Number.NaN)).toBe('');
  });

  it('uses an explicit instant for snapshot freshness', () => {
    const now = new Date('2026-09-21T12:00:00.000Z');
    expect(getElapsedSince('2026-09-21T11:59:42.000Z', now)).toBe(18_000);
    expect(getElapsedSince('2026-09-21T12:01:00.000Z', now)).toBe(0);
    expect(getElapsedSince('not-a-date', now)).toBeNull();
  });
});
