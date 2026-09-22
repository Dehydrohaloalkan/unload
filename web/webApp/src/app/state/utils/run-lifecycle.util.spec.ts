import { RunLifecycleStatus, RunStatusInfo } from '../../app.models';
import { shouldAcceptRunStatus } from './run-lifecycle.util';
import { AsyncEpoch } from './async-epoch.util';

function run(overrides: Partial<RunStatusInfo> = {}): RunStatusInfo {
  return {
    correlationId: 'req-owned',
    taskCode: 'run',
    status: RunLifecycleStatus.Running,
    createdAt: '2026-09-22T10:00:00.000Z',
    updatedAt: '2026-09-22T10:00:01.000Z',
    ...overrides,
  } as RunStatusInfo;
}

describe('run lifecycle projection', () => {
  it('lets only the latest bootstrap generation commit', () => {
    const epoch = new AsyncEpoch();
    const first = epoch.begin();
    const second = epoch.begin();

    expect(epoch.isCurrent(first)).toBe(false);
    expect(epoch.isCurrent(second)).toBe(true);
  });

  it('rejects a foreign broadcast instead of adopting its correlation', () => {
    expect(
      shouldAcceptRunStatus(
        run(),
        run({ correlationId: 'req-foreign', updatedAt: '2026-09-22T10:00:02.000Z' }),
        'req-owned',
      ),
    ).toBe(false);
  });

  it('rejects a stale non-terminal snapshot after terminal status', () => {
    expect(
      shouldAcceptRunStatus(
        run({ status: RunLifecycleStatus.Completed, updatedAt: '2026-09-22T10:00:03.000Z' }),
        run({ status: RunLifecycleStatus.Running, updatedAt: '2026-09-22T10:00:04.000Z' }),
        'req-owned',
      ),
    ).toBe(false);
  });

  it('rejects an older terminal snapshot returned by a stale poll', () => {
    expect(
      shouldAcceptRunStatus(
        run({ status: RunLifecycleStatus.Running, updatedAt: '2026-09-22T10:00:05.000Z' }),
        run({ status: RunLifecycleStatus.Completed, updatedAt: '2026-09-22T10:00:04.000Z' }),
        'req-owned',
      ),
    ).toBe(false);
  });
});
