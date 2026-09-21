import { FileRunStage, ScriptRunStage } from '../../app.models';
import { RU } from '../../i18n/ru';
import { resolveFileStageLabel, resolveScriptStageLabel } from './labels.util';

describe('process lifecycle labels', () => {
  it('resolves every script lifecycle stage through i18n', () => {
    expect(resolveScriptStageLabel(ScriptRunStage.AwaitingWorker)).toBe(
      RU['status.script.awaitingWorker'],
    );
    expect(resolveScriptStageLabel(ScriptRunStage.Running)).toBe(RU['status.script.running']);
    expect(resolveScriptStageLabel(ScriptRunStage.Completed)).toBe(RU['status.script.completed']);
    expect(resolveScriptStageLabel(ScriptRunStage.Failed)).toBe(RU['status.script.failed']);
    expect(resolveScriptStageLabel(ScriptRunStage.Cancelled)).toBe(RU['status.script.cancelled']);
  });

  it('resolves file lifecycle stages and degrades unknown values safely', () => {
    expect(resolveFileStageLabel(FileRunStage.QueuedForWrite)).toBe(
      RU['status.file.queuedForWrite'],
    );
    expect(resolveFileStageLabel(FileRunStage.Written)).toBe(RU['status.file.written']);
    expect(resolveFileStageLabel(FileRunStage.Failed)).toBe(RU['status.file.failed']);
    expect(resolveFileStageLabel(FileRunStage.Cancelled)).toBe(RU['status.file.cancelled']);
    expect(resolveFileStageLabel(99)).toBe(RU['status.unknown']);
  });
});
