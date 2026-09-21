import {
  FileRunStage,
  MemberRunLifecycleStatus,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';
import {
  resolveBatchTone,
  resolveFileTone,
  resolveMemberTone,
  resolveScriptTone,
  resolveStageTone,
} from './process-tone.util';

describe('process presentation tones', () => {
  it('maps lifecycle values to deterministic semantic tones', () => {
    expect(resolveMemberTone(MemberRunLifecycleStatus.Running)).toBe('active');
    expect(resolveMemberTone(MemberRunLifecycleStatus.Completed)).toBe('success');
    expect(resolveMemberTone(MemberRunLifecycleStatus.Failed)).toBe('danger');
    expect(resolveMemberTone(MemberRunLifecycleStatus.Cancelled)).toBe('neutral');
    expect(resolveMemberTone(99)).toBe('neutral');

    expect(resolveScriptTone(ScriptRunStage.AwaitingWorker)).toBe('active');
    expect(resolveScriptTone(ScriptRunStage.Completed)).toBe('success');
    expect(resolveScriptTone(ScriptRunStage.Failed)).toBe('danger');
    expect(resolveScriptTone(ScriptRunStage.Cancelled)).toBe('neutral');

    expect(resolveFileTone(FileRunStage.QueuedForWrite)).toBe('active');
    expect(resolveFileTone(FileRunStage.Written)).toBe('success');
    expect(resolveFileTone(FileRunStage.Failed)).toBe('danger');
    expect(resolveFileTone(FileRunStage.Cancelled)).toBe('neutral');

    expect(resolveBatchTone(SenderBatchStatus.Ready)).toBe('active');
    expect(resolveBatchTone(SenderBatchStatus.Completed)).toBe('success');
    expect(resolveBatchTone(SenderBatchStatus.Failed)).toBe('danger');
    expect(resolveBatchTone(SenderBatchStatus.SkippedByRequest)).toBe('neutral');
  });

  it('prioritizes failures over active and completed cards for a stage', () => {
    expect(resolveStageTone([])).toBe('neutral');
    expect(resolveStageTone(['success', 'neutral'])).toBe('success');
    expect(resolveStageTone(['success', 'active'])).toBe('active');
    expect(resolveStageTone(['active', 'danger', 'success'])).toBe('danger');
  });
});
