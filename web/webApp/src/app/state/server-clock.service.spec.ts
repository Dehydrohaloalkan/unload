import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ApiClientService } from './api-client.service';
import { ServerClockService } from './server-clock.service';

describe('ServerClockService', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-21T20:59:59.000Z'));
    TestBed.configureTestingModule({
      providers: [
        ServerClockService,
        {
          provide: ApiClientService,
          useValue: { fetchServerTime: vi.fn() },
        },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('emits one day change when the server-local clock crosses midnight', () => {
    const clock = TestBed.inject(ServerClockService);
    const dayChanged = vi.fn();
    clock.dayChanged$.subscribe(dayChanged);

    clock.init();
    clock.applyFromResponse('2026-09-21T23:59:59+03:00', 'Europe/Minsk', 180);

    vi.advanceTimersByTime(1_000);
    expect(dayChanged).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(5_000);
    expect(dayChanged).toHaveBeenCalledTimes(1);
  });

  it('uses the first server response as a baseline without reporting a false day change', () => {
    vi.setSystemTime(new Date('2026-09-21T21:30:00.000Z'));
    const clock = TestBed.inject(ServerClockService);
    const dayChanged = vi.fn();
    clock.dayChanged$.subscribe(dayChanged);

    clock.init();
    clock.applyFromResponse('2026-09-22T00:30:00+03:00', 'Europe/Minsk', 180);

    expect(dayChanged).not.toHaveBeenCalled();
  });
});
