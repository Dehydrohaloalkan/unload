import {
  FileRunStage,
  FileRunStatusInfo,
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunLifecycleStatus,
  RunStatusInfo,
  ScriptRunStage,
  ScriptRunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  PROCESS_PAGE_SIZE,
  PROCESS_WORKER_COUNT,
  buildProcessPipeline,
  resolveProcessMemberIdentity,
} from './process-projection.util';

const QUEUED_AT = '2026-09-23T10:00:00Z';
const SENT_AT = '2026-09-23T10:02:00Z';

describe('process pipeline projection', () => {
  it('projects only active and failed work into the physical pipeline and exposes four workers', () => {
    const pipeline = buildProcessPipeline(
      snapshot({
        memberStatuses: {
          input: member('Input', MemberRunLifecycleStatus.Pending, 1),
          resolver: member('Resolver', MemberRunLifecycleStatus.Running, 2),
          completed: member('Completed ghost', MemberRunLifecycleStatus.Completed, 3),
        },
        scriptStatuses: {
          queued: script('queued', 'Queued', ScriptRunStage.AwaitingWorker, null, 1),
          running: script('running', 'Running', ScriptRunStage.Running, 3, 2),
          completed: script('completed', 'Completed ghost', ScriptRunStage.Completed, 1, 3),
        },
        workerStatuses: {
          three: {
            workerId: 3,
            state: 'Running',
            memberName: 'Running',
            scriptCode: 'RUNNING',
            sequence: 4,
            updatedAt: QUEUED_AT,
          },
        },
      }),
    );

    expect(pipeline.memberInput.map((item) => item.name)).toEqual(['Input']);
    expect(pipeline.resolver.map((item) => item.name)).toEqual(['Resolver']);
    expect(pipeline.scriptQueue.map((item) => item.id)).toEqual(['queued']);
    expect(pipeline.workers).toHaveLength(PROCESS_WORKER_COUNT);
    expect(pipeline.workers[2].assignment?.id).toBe('running');
    expect(pipeline.members.some((item) => item.name === 'Completed ghost')).toBe(true);
    expect(pipeline.memberInput.some((item) => item.name === 'Completed ghost')).toBe(false);
    expect(pipeline.scriptQueue.some((item) => item.id === 'completed')).toBe(false);
    expect(pipeline.workers.some((worker) => worker.assignment?.id === 'completed')).toBe(false);
  });

  it('moves 1000 canonical files through created, sender and delivered with no file in two zones', () => {
    const fileStatuses = Object.fromEntries(
      Array.from({ length: 1_000 }, (_, index) => {
        const number = index + 1;
        return [`file-${number}`, file(`file-${number}`, `/safe/file-${number}.txt`, number)];
      }),
    );
    const plannedFiles = Array.from({ length: 1_000 }, (_, index) => {
      const number = index + 1;
      return {
        fileName: `canonical-${number}.txt`,
        filePath: `/safe/file-${number}.txt`,
        queuedAt: QUEUED_AT,
        sentAt: number <= 400 ? SENT_AT : null,
        estimatedBytes: 100,
        actualBytes: number <= 400 ? 90 : null,
      };
    });
    const pipeline = buildProcessPipeline(
      snapshot({
        fileStatuses,
        senderBatches: { batch: batch('batch-1', SenderBatchStatus.InProgress, plannedFiles) },
      }),
    );

    expect(pipeline.fileGroups).toHaveLength(0);
    expect(pipeline.senderInProgress[0].senderFiles).toHaveLength(600);
    expect(pipeline.delivered[0].sentFiles).toHaveLength(400);
    expect(pipeline.delivered[0].sentFiles[0].fileName).toBe('canonical-1.txt');
    const created = new Set(
      pipeline.fileGroups.flatMap((group) => group.details.map((item) => item.path)),
    );
    const sending = new Set(
      pipeline.senderInProgress.flatMap((item) => item.senderFiles.map((entry) => entry.path)),
    );
    const delivered = new Set(
      pipeline.delivered.flatMap((item) => item.sentFiles.map((entry) => entry.path)),
    );
    expect([...created].filter((path) => sending.has(path!) || delivered.has(path!))).toEqual([]);
    expect([...sending].filter((path) => delivered.has(path))).toEqual([]);
  });

  it('keeps failures at resolver, worker, created-file and sender stages', () => {
    const failedMember = member('Resolver failed', MemberRunLifecycleStatus.Failed, 1);
    failedMember.failure = failure('member', 'resolver', 'resolver-failed');
    const failedScript = script('query-failed', 'Worker failed', ScriptRunStage.Failed, 2, 2);
    failedScript.startedAt = QUEUED_AT;
    failedScript.failure = failure('script', 'query', 'query-failed');
    const failedFile = file('file-failed', '/safe/file-failed.txt', 3, FileRunStage.Failed);
    failedFile.failure = failure('file', 'writer', 'disk-full');
    const failedBatch = batch('batch-failed', SenderBatchStatus.Failed, [
      planned('/safe/send-failed.txt', null),
    ]);
    failedBatch.startedAt = QUEUED_AT;
    failedBatch.failure = failure('batch', 'sender', 'connection-lost');
    const pipeline = buildProcessPipeline(
      snapshot({
        memberStatuses: { failedMember },
        scriptStatuses: { failedScript },
        fileStatuses: { failedFile },
        senderBatches: { failedBatch },
      }),
    );

    expect(pipeline.resolver[0].failure?.code).toBe('resolver-failed');
    expect(pipeline.workers[1].retainedFailures[0].failure?.code).toBe('query-failed');
    expect(pipeline.fileGroups[0].failure?.code).toBe('disk-full');
    expect(pipeline.senderInProgress[0].failure?.code).toBe('connection-lost');
    expect(pipeline.failures.map((item) => item.code)).toEqual([
      'resolver-failed',
      'query-failed',
      'disk-full',
      'connection-lost',
    ]);
  });

  it('keeps 1000 created files grouped with a bounded 20-file detail page', () => {
    const fileStatuses = Object.fromEntries(
      Array.from({ length: 1_000 }, (_, index) => {
        const number = index + 1;
        return [`file-${number}`, file(`file-${number}`, `/safe/file-${number}.txt`, number)];
      }),
    );
    const group = buildProcessPipeline(snapshot({ fileStatuses })).fileGroups[0];

    expect(group.count).toBe(1_000);
    expect(group.details.slice(0, PROCESS_PAGE_SIZE)).toHaveLength(20);
    expect(group.details.slice(980, 980 + PROCESS_PAGE_SIZE)).toHaveLength(20);
    expect(group.details.slice(995, 995 + PROCESS_PAGE_SIZE)).toHaveLength(5);
  });

  it('uses confirmed sent files for legacy snapshots without inventing unsent file cards', () => {
    const legacy = batch('legacy-batch', SenderBatchStatus.InProgress, null);
    legacy.fileCount = 3;
    legacy.sentFiles = [{ filePath: '/legacy/RY_RKH_2_RO', sentAt: SENT_AT }];
    const pipeline = buildProcessPipeline(snapshot({ senderBatches: { legacy } }));

    expect(pipeline.senderInProgress[0].senderFiles).toEqual([]);
    expect(pipeline.senderInProgress[0].hasLegacyUnsentAmbiguity).toBe(true);
    expect(pipeline.delivered[0].sentFiles.map((item) => item.fileName)).toEqual(['RY_RKH_2_RO']);
    expect(pipeline.delivered[0].sentFiles).toHaveLength(1);
  });

  it('enriches a legacy sent path from its canonical file without making a second card', () => {
    const legacy = batch('legacy-batch', SenderBatchStatus.Completed, null);
    legacy.sentFiles = [{ filePath: '/legacy/RY_RKH_2_RO', sentAt: SENT_AT }];
    const canonical = file('friendly', '/legacy/RY_RKH_2_RO', 1);
    canonical.fileName = 'Мембер 1 (YFI.OF)';
    canonical.memberName = 'Friendly member';
    canonical.estimatedBytes = 512;
    const pipeline = buildProcessPipeline(
      snapshot({ fileStatuses: { canonical }, senderBatches: { legacy } }),
    );

    expect(pipeline.fileGroups).toHaveLength(0);
    expect(pipeline.delivered[0].sentFiles[0]).toMatchObject({
      fileName: 'Мембер 1 (YFI.OF)',
      memberName: 'Friendly member',
      estimatedBytes: 512,
    });
  });

  it('keeps a mixed batch as one sender item and globally orders sender work by sequence', () => {
    const ready = batch('ready-late', SenderBatchStatus.Ready, [planned('/ready.txt', null)]);
    ready.sequence = 20;
    const partial = batch('partial-first', SenderBatchStatus.Completed, [
      planned('/partial/sent.txt', SENT_AT),
      planned('/partial/waiting.txt', null),
    ]);
    partial.sequence = 10;
    const skipped = batch('skipped', SenderBatchStatus.SkippedByRequest, null);
    skipped.fileCount = 10;
    const pipeline = buildProcessPipeline(
      snapshot({ senderBatches: { ready, partial, skipped } }),
    );

    expect(pipeline.senderBatches.map((item) => item.id)).toEqual(['partial-first', 'ready-late']);
    expect(pipeline.senderBatches[0]).toMatchObject({ unsentCount: 1, sentCount: 1 });
    expect(pipeline.delivered[0].sentFiles.map((item) => item.path)).toEqual(['/partial/sent.txt']);
    expect(pipeline.senderBatches[0].senderFiles.map((item) => item.path)).toEqual([
      '/partial/waiting.txt',
    ]);
  });

  it('uses stable explicit order, deterministic identities and never mutates a snapshot', () => {
    const input = snapshot({
      memberStatuses: {
        second: member('Member 2', MemberRunLifecycleStatus.Pending, 2),
        first: member('Member 1', MemberRunLifecycleStatus.Pending, 1),
      },
      scriptStatuses: {
        second: script('second', 'Member 2', ScriptRunStage.AwaitingWorker, null, 2),
        first: script('first', 'Member 1', ScriptRunStage.AwaitingWorker, null, 1),
      },
    });
    const before = structuredClone(input);
    const pipeline = buildProcessPipeline(input);

    expect(pipeline.memberInput.map((item) => item.queuePosition)).toEqual([1, 2]);
    expect(pipeline.scriptQueue.map((item) => item.workOrder)).toEqual([1, 2]);
    expect(resolveProcessMemberIdentity(' Member 1 ')).toEqual(
      resolveProcessMemberIdentity('member 1'),
    );
    expect(input).toEqual(before);
  });

  it('assigns a requeued path to only the latest dispatch owner', () => {
    const first = batch('first', SenderBatchStatus.InProgress, [planned('/safe/same.txt', null)]);
    first.sequence = 1;
    const second = batch('second', SenderBatchStatus.Completed, [
      planned('/safe/same.txt', SENT_AT),
    ]);
    second.sequence = 2;
    const pipeline = buildProcessPipeline(snapshot({ senderBatches: { first, second } }));

    expect(pipeline.senderInProgress).toEqual([]);
    expect(pipeline.delivered.flatMap((item) => item.sentFiles).map((item) => item.path)).toEqual([
      '/safe/same.txt',
    ]);
  });

  it('keeps a newer pending requeue in sender even when an older batch already sent the path', () => {
    const olderSent = batch('older-sent', SenderBatchStatus.Completed, [
      planned('/safe/requeued.txt', SENT_AT),
    ]);
    olderSent.sequence = 1;
    const newerPending = batch('newer-pending', SenderBatchStatus.Ready, [
      planned('/safe/requeued.txt', null),
    ]);
    newerPending.sequence = 2;
    const pipeline = buildProcessPipeline(snapshot({ senderBatches: { olderSent, newerPending } }));

    expect(pipeline.senderBatches.map((item) => item.id)).toEqual(['newer-pending']);
    expect(pipeline.senderBatches[0].senderFiles.map((item) => item.path)).toEqual([
      '/safe/requeued.txt',
    ]);
    expect(pipeline.delivered).toEqual([]);
  });

  it('uses the newer batch timestamp when the requeued batch has no sequence', () => {
    const olderSent = batch('older-sent', SenderBatchStatus.Completed, [
      planned('/safe/legacy-requeue.txt', SENT_AT),
    ]);
    olderSent.sequence = 1;
    const newerPending = {
      ...batch('newer-pending', SenderBatchStatus.Ready, [
        planned('/safe/legacy-requeue.txt', null),
      ]),
      sequence: null,
      updatedAt: '2026-09-23T10:03:00Z',
    };
    const pipeline = buildProcessPipeline(
      snapshot({ senderBatches: { olderSent, newerPending } }),
    );

    expect(pipeline.senderBatches.map((item) => item.id)).toEqual(['newer-pending']);
    expect(pipeline.senderBatches[0].senderFiles.map((item) => item.path)).toEqual([
      '/safe/legacy-requeue.txt',
    ]);
    expect(pipeline.delivered).toEqual([]);
  });
});

function snapshot(overrides: Partial<RunStatusInfo>): RunStatusInfo {
  return {
    correlationId: 'run-1',
    taskCode: 'run',
    status: RunLifecycleStatus.Running,
    targetCodes: [],
    createdAt: QUEUED_AT,
    updatedAt: SENT_AT,
    ...overrides,
  };
}

function member(
  name: string,
  status: MemberRunLifecycleStatus,
  queuePosition: number,
): MemberRunStatusInfo {
  return {
    memberName: name,
    status,
    lastStep: null,
    message: null,
    queuePosition,
    sequence: queuePosition,
    updatedAt: QUEUED_AT,
  };
}

function script(
  id: string,
  memberName: string,
  stage: ScriptRunStage,
  workerId: number | null,
  workOrder: number,
): ScriptRunStatusInfo {
  return {
    id,
    memberName,
    scriptCode: id.toUpperCase(),
    stage,
    workOrder,
    sequence: workOrder,
    discoveredAt: QUEUED_AT,
    stageEnteredAt: QUEUED_AT,
    startedAt: stage === ScriptRunStage.Running ? QUEUED_AT : null,
    updatedAt: SENT_AT,
    completedAt: stage === ScriptRunStage.Completed ? SENT_AT : null,
    workerId,
    records: 10,
  };
}

function file(
  id: string,
  filePath: string,
  sequence: number,
  stage: FileRunStage = FileRunStage.Written,
): FileRunStatusInfo {
  return {
    id,
    parentScriptId: 'script-1',
    memberName: 'Member 1',
    scriptCode: 'SCRIPT_1',
    chunkNumber: sequence,
    sequence,
    stage,
    createdAt: QUEUED_AT,
    queuedAt: QUEUED_AT,
    stageEnteredAt: QUEUED_AT,
    updatedAt: SENT_AT,
    completedAt: SENT_AT,
    workerId: 1,
    rows: 10,
    estimatedBytes: 100,
    fileName: `created-${sequence}.txt`,
    filePath,
  };
}

function batch(
  id: string,
  status: SenderBatchStatus,
  plannedFiles: SenderBatchStatusInfo['plannedFiles'],
): SenderBatchStatusInfo {
  const sentFiles =
    plannedFiles
      ?.filter((item) => item.sentAt)
      .map((item) => ({ filePath: item.filePath, sentAt: item.sentAt! })) ?? [];
  return {
    batchId: id,
    memberName: 'Member 1',
    status,
    updatedAt: SENT_AT,
    queuedAt: QUEUED_AT,
    startedAt: status === SenderBatchStatus.Ready ? null : QUEUED_AT,
    fileCount: plannedFiles?.length ?? null,
    sequence: 1,
    sentFiles,
    plannedFiles,
  };
}

function planned(filePath: string, sentAt: string | null) {
  return {
    fileName: filePath.split('/').at(-1)!,
    filePath,
    queuedAt: QUEUED_AT,
    sentAt,
    estimatedBytes: 10,
    actualBytes: sentAt ? 9 : null,
  };
}

function failure(entityType: string, stage: string, code: string) {
  return {
    entityType,
    entityId: `${entityType}-id`,
    stage,
    code,
    message: `<unsafe> ${code}`,
    occurredAt: SENT_AT,
    memberName: null,
    scriptCode: null,
    workerId: null,
    filePath: null,
    chunkNumber: null,
    batchId: null,
  };
}
