import { PresetGateState, RunLifecycleStatus, RunStatusInfo } from '../../app.models';
import {
  buildExtraBankNamesByCode,
  canUseMainOrExtra,
  resolveExtraLastCompletedAt,
  resolveWorkflowPhase,
} from './workflow-view-state.util';

describe('workflow view state', () => {
  it('builds extra bank names and ignores empty codes', () => {
    expect(
      buildExtraBankNamesByCode([
        { nrBank: 'B01', bankName: 'Альфа' },
        { nrBank: '', bankName: 'Без кода' },
        { nrBank: 'B02', bankName: 'Бета' },
      ]),
    ).toEqual({ B01: 'Альфа', B02: 'Бета' });
  });

  it('prefers a completed active extra timestamp over a stale dashboard timestamp', () => {
    const completed = createRun({
      status: RunLifecycleStatus.Completed,
      updatedAt: '2026-08-07T11:00:00Z',
    });

    expect(resolveExtraLastCompletedAt(completed, '2026-08-07T10:00:00Z')).toBe(
      '2026-08-07T11:00:00Z',
    );
    expect(
      resolveExtraLastCompletedAt(
        createRun({ status: RunLifecycleStatus.Running }),
        '2026-08-07T10:00:00Z',
      ),
    ).toBe('2026-08-07T10:00:00Z');
  });

  it.each([
    { state: null, expected: false },
    { state: createPreset({ requiresPresetExecution: false }), expected: true },
    { state: createPreset({ requiresPresetExecution: true, presetCompleted: false }), expected: false },
    { state: createPreset({ requiresPresetExecution: true, presetCompleted: true }), expected: true },
  ])('resolves main and extra availability to $expected', ({ state, expected }) => {
    expect(canUseMainOrExtra(state)).toBe(expected);
  });

  it('switches to tasks only after preset completion', () => {
    expect(resolveWorkflowPhase(null)).toBe('gate');
    expect(resolveWorkflowPhase(createPreset({ presetCompleted: false }))).toBe('gate');
    expect(resolveWorkflowPhase(createPreset({ presetCompleted: true }))).toBe('tasks');
  });
});

function createPreset(overrides: Partial<PresetGateState> = {}): PresetGateState {
  return {
    enabled: true,
    pollingStarted: true,
    requiresPresetExecution: true,
    readyForPreset: true,
    presetCompleted: false,
    lastProbeValue: 1,
    lastProbeAt: '2026-08-07T09:30:00Z',
    message: '',
    ...overrides,
  };
}

function createRun(overrides: Partial<RunStatusInfo> = {}): RunStatusInfo {
  return {
    correlationId: 'extra-1',
    taskCode: 'extra',
    status: RunLifecycleStatus.Running,
    publishToGateway: false,
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
