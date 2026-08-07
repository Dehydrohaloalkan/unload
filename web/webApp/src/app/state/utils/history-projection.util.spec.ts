import {
  RequeueToGatewayResponse,
  RunLifecycleStatus,
  RunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  buildConfirmedSentPaths,
  buildHistoryNodes,
  GatewayDelivery,
  HistoryFileRow,
  summarizeRequeue,
} from './history-projection.util';

describe('history projection', () => {
  it.each([
    { publish: false, batchStatus: SenderBatchStatus.Completed, sent: false, expected: 'off' },
    { publish: true, batchStatus: SenderBatchStatus.Completed, sent: false, expected: 'delivered' },
    { publish: true, batchStatus: SenderBatchStatus.Failed, sent: true, expected: 'partial' },
    { publish: true, batchStatus: SenderBatchStatus.Failed, sent: false, expected: 'failed' },
  ] satisfies Array<{
    publish: boolean;
    batchStatus: SenderBatchStatus;
    sent: boolean;
    expected: GatewayDelivery;
  }>)('maps main gateway delivery to $expected', ({ publish, batchStatus, sent, expected }) => {
    const filePath = 'output/run/member-a.csv';
    const run = createRun({
      publishToGateway: publish,
      outputArtifacts: [
        {
          fileName: 'member-a.csv',
          filePath,
          memberName: 'Member A',
          scriptCode: 'SCRIPT_A',
          occurredAt: '2026-08-07T10:01:00Z',
        },
      ],
      senderBatches: {
        batch: createBatch('Member A', batchStatus, sent ? [filePath] : []),
      },
    });

    const nodes = buildHistoryNodes(createInput({ todayRuns: [run] }));

    expect(nodes).toHaveLength(1);
    expect(nodes[0].gatewayDelivery).toBe(expected);
    expect(nodes[0].memberFiles['Member A'][0].sentToGateway).toBe(sent);
  });

  it('groups extra files by sorted script and readable bank name', () => {
    const extra = createRun({
      correlationId: 'extra-1',
      taskCode: 'extra',
      outputPath: 'extra-output',
      publishToGateway: false,
    });

    const nodes = buildHistoryNodes(
      createInput({
        allTodayRuns: [extra],
        outputFilesByPath: {
          'extra-output': [
            {
              fileName: 'b.csv',
              filePath: 'extra/output-files/SCRIPT_B/B02/b.csv',
              modifiedAt: '2026-08-07T10:02:00Z',
              sizeBytes: 20,
            },
            {
              fileName: 'a.csv',
              filePath: 'extra/output-files/SCRIPT_A/B01/a.csv',
              modifiedAt: '2026-08-07T10:01:00Z',
              sizeBytes: 10,
            },
          ],
        },
        bankNamesByCode: { B01: 'Альфа', B02: 'Бета' },
      }),
    );

    expect(nodes).toHaveLength(1);
    expect(nodes[0].taskCode).toBe('extra');
    expect(nodes[0].scripts?.map((script) => script.scriptCode)).toEqual(['SCRIPT_A', 'SCRIPT_B']);
    expect(nodes[0].scripts?.map((script) => script.banks[0].bankName)).toEqual([
      'Альфа',
      'Бета',
    ]);
    expect(nodes[0].scripts?.map((script) => script.fileCount)).toEqual([1, 1]);
  });

  it('confirms only rows from accepted requeue batches', () => {
    const runRow = createHistoryRow('run', 'run-1', 'Member A', 'run-a.csv');
    const extraRow = createHistoryRow('extra', 'extra-1', 'SCRIPT_A', 'extra-a.csv');
    const result = createRequeueResult({
      results: [
        {
          taskCode: 'run',
          correlationId: 'run-1',
          acceptedBatches: 1,
          failedBatches: 0,
          batches: [createRequeueBatch('Member A', SenderBatchStatus.Completed)],
        },
        {
          taskCode: 'extra',
          correlationId: 'extra-1',
          acceptedBatches: 0,
          failedBatches: 1,
          batches: [createRequeueBatch('SCRIPT_A', SenderBatchStatus.Failed)],
        },
      ],
    });

    const paths = buildConfirmedSentPaths(result, [runRow, extraRow]);

    expect(Array.from(paths)).toEqual(['run-a.csv']);
  });

  it('summarizes selected requeue files by accepted member batches', () => {
    const selected = [
      createHistoryRow('run', 'run-1', 'Member A', 'a.csv'),
      createHistoryRow('run', 'run-1', 'Member B', 'b.csv'),
      createHistoryRow('extra', 'extra-1', 'SCRIPT_A', 'extra.csv'),
    ];
    const result = createRequeueResult({
      results: [
        {
          taskCode: 'run',
          correlationId: 'run-1',
          acceptedBatches: 1,
          failedBatches: 0,
          batches: [createRequeueBatch('Member A', SenderBatchStatus.Completed)],
        },
        {
          taskCode: 'extra',
          correlationId: 'extra-1',
          acceptedBatches: 0,
          failedBatches: 1,
          batches: [createRequeueBatch('SCRIPT_A', SenderBatchStatus.Failed)],
        },
      ],
    });

    expect(summarizeRequeue(result, selected)).toEqual({ total: 3, sent: 1, notSent: 2 });
  });
});

function createInput(overrides: Partial<Parameters<typeof buildHistoryNodes>[0]> = {}) {
  return {
    todayRuns: [],
    todayHistory: [],
    allTodayRuns: [],
    outputFilesByPath: {},
    knownMemberNames: [],
    confirmedSentPaths: new Set<string>(),
    ...overrides,
  };
}

function createRun(overrides: Partial<RunStatusInfo> = {}): RunStatusInfo {
  return {
    correlationId: 'run-1',
    taskCode: 'run',
    status: RunLifecycleStatus.Completed,
    publishToGateway: true,
    targetCodes: [],
    createdAt: '2026-08-07T10:00:00Z',
    updatedAt: '2026-08-07T10:05:00Z',
    lastStep: null,
    message: null,
    outputPath: null,
    memberStatuses: null,
    outputArtifacts: null,
    workerStatuses: null,
    senderBatches: null,
    ...overrides,
  };
}

function createBatch(
  memberName: string,
  status: SenderBatchStatus,
  sentPaths: string[],
): SenderBatchStatusInfo {
  return {
    batchId: `batch-${memberName}`,
    memberName,
    status,
    updatedAt: '2026-08-07T10:04:00Z',
    sentFiles: sentPaths.map((filePath) => ({
      filePath,
      sentAt: '2026-08-07T10:04:00Z',
    })),
    message: null,
  };
}

function createHistoryRow(
  taskCode: 'run' | 'extra',
  correlationId: string,
  memberName: string,
  filePath: string,
): HistoryFileRow {
  return {
    key: `${taskCode}|${correlationId}|${filePath}`,
    taskCode,
    correlationId,
    memberName,
    fileName: filePath,
    filePath,
    occurredAt: '2026-08-07T10:00:00Z',
    sentToGateway: false,
  };
}

function createRequeueBatch(memberName: string, status: SenderBatchStatus) {
  return {
    memberName,
    batchId: `batch-${memberName}`,
    status,
    message: null,
  };
}

function createRequeueResult(
  overrides: Partial<RequeueToGatewayResponse>,
): RequeueToGatewayResponse {
  return {
    requestId: 'request-1',
    acceptedBatches: 0,
    failedBatches: 0,
    results: [],
    ...overrides,
  };
}
