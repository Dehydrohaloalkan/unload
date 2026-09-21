import {
  FileRunStage,
  FileRunStatusInfo,
  RunLifecycleStatus,
  RunStatusInfo,
  ScriptRunStage,
  ScriptRunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import { buildProcessMemberRows } from './process-projection.util';

const NOW = new Date('2026-09-21T12:00:00.000Z');

describe('process projection', () => {
  it('unions member names case-insensitively and keeps one stable row per member', () => {
    const rows = buildProcessMemberRows(
      run({
        memberStatuses: {
          first: memberStatus('Bank A', '2026-09-21T11:00:00Z'),
          second: memberStatus('bank a', '2026-09-21T11:01:00Z'),
        },
        scriptStatuses: { script: scriptStatus('SCRIPT-1', 'BANK A') },
        fileStatuses: { file: fileStatus('Bank B', 'missing-script', 'file') },
        senderBatches: { batch: batchStatus('bank c', 'batch-1') },
      }),
      NOW,
    );

    expect(rows.map((row) => row.key)).toEqual(['bank a', 'bank b', 'bank c']);
    expect(rows[0].name).toBe('bank a');
    expect(rows[0].status).toBe(1);
  });

  it('attaches files to their script and exposes files without a parent as orphans', () => {
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: { script: scriptStatus('SCRIPT-1', 'Bank A') },
        fileStatuses: {
          child: fileStatus('Bank A', 'script-1', 'file-1'),
          orphan: fileStatus('Bank A', 'missing-script', 'file-2'),
        },
      }),
      NOW,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].scripts[0].files.map((file) => file.id)).toEqual(['file-1']);
    expect(rows[0].orphanFiles.map((file) => file.id)).toEqual(['file-2']);
  });

  it('accepts empty and legacy-null maps without inventing cards', () => {
    expect(buildProcessMemberRows(null, NOW)).toEqual([]);
    expect(buildProcessMemberRows(undefined, NOW)).toEqual([]);
    expect(
      buildProcessMemberRows(
        run({
          memberStatuses: null,
          scriptStatuses: null,
          fileStatuses: null,
          senderBatches: null,
        }),
        NOW,
      ),
    ).toEqual([]);
  });

  it('sorts rows, scripts, files and batches deterministically with numeric-friendly names', () => {
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: {
          z: scriptStatus('SCRIPT 10', 'Member 1', 'script-z'),
          a: scriptStatus('SCRIPT 2', 'Member 1', 'script-a'),
        },
        fileStatuses: {
          f10: fileStatus('Member 1', 'script-a', 'file-10', 10),
          f2: fileStatus('Member 1', 'script-a', 'file-2', 2),
        },
        senderBatches: {
          late: batchStatus('Member 1', 'batch-2', '2026-09-21T11:30:00Z'),
          early: batchStatus('Member 1', 'batch-1', '2026-09-21T11:00:00Z'),
        },
      }),
      NOW,
    );

    expect(rows.map((row) => row.name)).toEqual(['Member 1']);
    expect(rows[0].scripts.map((script) => script.scriptCode)).toEqual(['SCRIPT 2', 'SCRIPT 10']);
    expect(rows[0].scripts[0].files.map((file) => file.chunk)).toEqual([2, 10]);
    expect(rows[0].batches.map((batch) => batch.id)).toEqual(['batch-1', 'batch-2']);
  });

  it('computes active and terminal durations from the explicit now', () => {
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: {
          waiting: scriptStatus(
            'WAITING',
            'Bank A',
            'waiting',
            ScriptRunStage.AwaitingWorker,
            '2026-09-21T11:00:00Z',
          ),
          done: scriptStatus(
            'DONE',
            'Bank A',
            'done',
            ScriptRunStage.Completed,
            '2026-09-21T10:00:00Z',
            '2026-09-21T11:45:00Z',
            '2026-09-21T11:45:00Z',
            '2026-09-21T11:45:00Z',
            '2026-09-21T10:30:00Z',
          ),
        },
        fileStatuses: {
          active: fileStatus(
            'Bank A',
            'waiting',
            'active-file',
            1,
            FileRunStage.QueuedForWrite,
            '2026-09-21T11:30:00Z',
          ),
        },
      }),
      NOW,
    );

    const waiting = rows[0].scripts.find((script) => script.id === 'waiting');
    const done = rows[0].scripts.find((script) => script.id === 'done');
    expect(waiting?.queueWaitMs).toBe(3_600_000);
    expect(waiting?.stageElapsedMs).toBe(3_600_000);
    expect(done?.queueWaitMs).toBe(1_800_000);
    expect(done?.stageElapsedMs).toBe(4_500_000);
    expect(rows[0].activeItemSummary.count).toBe(2);
    expect(rows[0].oldestActiveAt).toBe('2026-09-21T11:00:00Z');
  });

  it('keeps queue wait for a terminal script that never started', () => {
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: {
          failed: scriptStatus(
            'FAILED',
            'Bank A',
            'failed',
            ScriptRunStage.Failed,
            '2026-09-21T10:00:00Z',
            '2026-09-21T10:00:00Z',
            '2026-09-21T11:30:00Z',
          ),
        },
      }),
      NOW,
    );

    expect(rows[0].scripts[0].queueWaitMs).toBe(5_400_000);
  });

  it('keeps queue wait for a terminal batch without a started timestamp', () => {
    const failedBatch = batchStatus('Bank A', 'failed-batch', '2026-09-21T10:00:00Z');
    failedBatch.status = SenderBatchStatus.Failed;
    failedBatch.updatedAt = '2026-09-21T11:30:00Z';
    const rows = buildProcessMemberRows(run({ senderBatches: { failed: failedBatch } }), NOW);

    expect(rows[0].batches[0].queueWaitMs).toBe(5_400_000);
  });

  it('clamps out-of-order timestamps and returns null for invalid duration inputs', () => {
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: {
          backwards: scriptStatus(
            'BACKWARDS',
            'Bank A',
            'backwards',
            ScriptRunStage.Completed,
            'not-a-date',
            '2026-09-21T11:30:00Z',
            '2026-09-21T11:00:00Z',
            '2026-09-21T10:00:00Z',
          ),
        },
        fileStatuses: {
          backwards: fileStatus(
            'Bank A',
            'backwards',
            'file',
            1,
            FileRunStage.Written,
            '2026-09-21T11:00:00Z',
            '2026-09-21T10:00:00Z',
          ),
        },
      }),
      NOW,
    );

    expect(rows[0].scripts[0].queueWaitMs).toBeNull();
    expect(rows[0].scripts[0].stageElapsedMs).toBe(0);
    expect(rows[0].scripts[0].files[0].writeElapsedMs).toBe(0);
  });

  it('keeps multiple batches for one member and counts sent files safely', () => {
    const rows = buildProcessMemberRows(
      run({
        senderBatches: {
          first: batchStatus('Bank A', 'batch-1'),
          second: batchStatus('Bank A', 'batch-2', '2026-09-21T11:00:00Z', 2),
        },
      }),
      NOW,
    );

    expect(rows[0].batches).toHaveLength(2);
    expect(rows[0].batches[1].sentCount).toBe(2);
    expect(rows[0].activeItemSummary.batchCount).toBe(2);
  });

  it('does not treat unknown or null enum values as active and rejects whitespace numbers', () => {
    const unknownBatch = batchStatus('Bank A', 'unknown-batch');
    unknownBatch.status = 99;
    const partialScript = scriptStatus('PARTIAL', 'Bank A', 'partial-script');
    setLegacyField(partialScript, 'stage', null);
    setLegacyField(partialScript, 'workerId', '   ');
    setLegacyField(partialScript, 'records', '   ');
    const partialFile = fileStatus('Bank A', 'partial-script', 'partial-file');
    setLegacyField(partialFile, 'stage', null);
    setLegacyField(partialFile, 'chunkNumber', '   ');
    setLegacyField(partialFile, 'estimatedBytes', '   ');
    const rows = buildProcessMemberRows(
      run({
        scriptStatuses: {
          unknown: scriptStatus('UNKNOWN', 'Bank A', 'unknown-script', 99),
          partial: partialScript,
        },
        fileStatuses: {
          unknown: fileStatus('Bank A', 'unknown-script', 'unknown-file', 99, 99),
          partial: partialFile,
        },
        senderBatches: { unknown: unknownBatch },
      }),
      NOW,
    );

    expect(rows[0].activeItemSummary).toMatchObject({
      count: 0,
      scriptCount: 0,
      fileCount: 0,
      batchCount: 0,
    });
    expect(rows[0].scripts.find((script) => script.id === 'partial-script')).toMatchObject({
      workerId: null,
      records: null,
    });
    expect(
      rows[0].scripts
        .find((script) => script.id === 'partial-script')
        ?.files.find((file) => file.id === 'partial-file'),
    ).toMatchObject({ chunk: null, bytes: null });
  });
});

function run(overrides: Partial<RunStatusInfo>): RunStatusInfo {
  return {
    correlationId: 'run-1',
    taskCode: 'run',
    status: RunLifecycleStatus.Running,
    targetCodes: [],
    createdAt: '2026-09-21T10:00:00Z',
    updatedAt: '2026-09-21T12:00:00Z',
    ...overrides,
  };
}

function memberStatus(memberName: string, updatedAt: string) {
  return { memberName, status: 1, lastStep: null, message: null, updatedAt };
}

function scriptStatus(
  scriptCode: string,
  memberName: string,
  id = scriptCode.toLowerCase(),
  stage: ScriptRunStage = ScriptRunStage.Running,
  discoveredAt = '2026-09-21T11:00:00Z',
  stageEnteredAt = discoveredAt,
  updatedAt = stageEnteredAt,
  completedAt: string | null = null,
  startedAt: string | null = stage === ScriptRunStage.Running || stage === ScriptRunStage.Completed
    ? stageEnteredAt
    : null,
): ScriptRunStatusInfo {
  return {
    id,
    memberName,
    scriptCode,
    stage,
    discoveredAt,
    stageEnteredAt,
    updatedAt,
    startedAt,
    completedAt,
    workerId: stage === ScriptRunStage.Running ? '2' : null,
    records: '42',
  };
}

function fileStatus(
  memberName: string,
  parentScriptId: string,
  id: string,
  chunkNumber = 1,
  stage: FileRunStage = FileRunStage.Written,
  queuedAt = '2026-09-21T11:00:00Z',
  updatedAt = queuedAt,
): FileRunStatusInfo {
  return {
    id,
    parentScriptId,
    memberName,
    scriptCode: parentScriptId,
    chunkNumber,
    stage,
    createdAt: queuedAt,
    queuedAt,
    stageEnteredAt: queuedAt,
    updatedAt,
    completedAt: stage === FileRunStage.Written ? updatedAt : null,
    workerId: '1',
    rows: '10',
    estimatedBytes: '20',
    fileName: `${id}.txt`,
    filePath: `/tmp/${id}.txt`,
  };
}

function batchStatus(
  memberName: string,
  batchId: string,
  queuedAt = '2026-09-21T11:00:00Z',
  sentCount = 0,
): SenderBatchStatusInfo {
  return {
    batchId,
    memberName,
    status: SenderBatchStatus.Ready,
    updatedAt: queuedAt,
    queuedAt,
    startedAt: null,
    fileCount: sentCount || 1,
    sentFiles: Array.from({ length: sentCount }, (_, index) => ({
      filePath: `/tmp/file-${index}.txt`,
      sentAt: queuedAt,
    })),
  };
}

function setLegacyField(target: object, key: string, value: unknown): void {
  Reflect.set(target, key, value);
}
