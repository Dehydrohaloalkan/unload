import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  PLATFORM_ID,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import {
  ProcessBatchCard,
  ProcessFileCard,
  ProcessMemberRow,
  ProcessScriptCard,
} from '../../state/utils/process-projection.models';
import { WorkflowStore } from '../../app.store';
import { FileRunStage, ScriptRunStage, SenderBatchStatus } from '../../app.models';
import { TPipe } from '../../i18n/i18n';
import {
  resolveFileStageLabel,
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveScriptStageLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import {
  formatProcessBytes,
  formatProcessCount,
  formatProcessDuration,
  getElapsedSince,
} from '../../state/utils/process-display.util';
import { buildProcessMemberRows } from '../../state/utils/process-projection.util';
import {
  ProcessTone,
  resolveBatchTone,
  resolveFileTone,
  resolveMemberTone,
  resolveScriptTone,
} from '../../state/utils/process-tone.util';
import { formatTimestamp } from '../../state/utils/time.util';

@Component({
  selector: 'app-process-run-view',
  standalone: true,
  imports: [TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './process-run-view.component.html',
  styleUrls: ['./details-shared.css', './process-run-view.component.css'],
})
export class ProcessRunViewComponent {
  readonly store = inject(WorkflowStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);
  private intervalId: ReturnType<typeof setInterval> | null = null;

  /** Единственный обновляемый clock: pure projection никогда не вызывает Date.now самостоятельно. */
  readonly now = signal(new Date());
  readonly run = this.store.activeRun;
  /** Static projection changes only when a server snapshot changes, not on every clock tick. */
  readonly rows = computed(() => {
    const run = this.run();
    return buildProcessMemberRows(run, snapshotDate(run?.updatedAt));
  });
  readonly hasProcessCards = computed(() => this.rows().length > 0);
  readonly activeCardCount = computed(() =>
    this.rows().reduce((total, row) => total + row.activeItemSummary.count, 0),
  );
  readonly hasActiveWork = computed(() => this.activeCardCount() > 0);
  readonly snapshotAgeMs = computed(() => getElapsedSince(this.run()?.updatedAt, this.now()));

  resolveRunStatusLabel = resolveRunStatusLabel;
  resolveMemberStatusLabel = resolveMemberStatusLabel;
  resolveScriptStageLabel = resolveScriptStageLabel;
  resolveFileStageLabel = resolveFileStageLabel;
  resolveSenderStatusLabel = resolveSenderStatusLabel;
  formatTimestamp = formatTimestamp;
  formatProcessDuration = formatProcessDuration;
  formatProcessBytes = formatProcessBytes;
  formatProcessCount = formatProcessCount;
  memberTone = resolveMemberTone;
  scriptTone = resolveScriptTone;
  fileTone = resolveFileTone;
  batchTone = resolveBatchTone;

  constructor() {
    effect(() => {
      const shouldTick = this.store.isRunBusy() || this.hasActiveWork();
      if (shouldTick) {
        this.startClock();
      } else {
        this.stopClock();
      }
    });
    this.destroyRef.onDestroy(() => this.stopClock());
  }

  fileName(file: ProcessFileCard): string {
    return file.fileName || file.path || file.id;
  }

  scriptQueueWait(script: ProcessScriptCard): number | null {
    return script.startedAt || isTerminalScript(script.stage)
      ? script.queueWaitMs
      : getElapsedSince(script.discoveredAt, this.now());
  }

  scriptExecution(script: ProcessScriptCard): number | null {
    return isTerminalScript(script.stage)
      ? script.stageElapsedMs
      : getElapsedSince(script.startedAt ?? script.stageEnteredAt, this.now());
  }

  fileWriteElapsed(file: ProcessFileCard): number | null {
    return isTerminalFile(file.stage)
      ? file.writeElapsedMs
      : getElapsedSince(file.queuedAt, this.now());
  }

  batchQueueWait(batch: ProcessBatchCard): number | null {
    return batch.startedAt || isTerminalBatch(batch.status)
      ? batch.queueWaitMs
      : getElapsedSince(batch.queuedAt, this.now());
  }

  batchSendElapsed(batch: ProcessBatchCard): number | null {
    return isTerminalBatch(batch.status)
      ? batch.sendElapsedMs
      : getElapsedSince(batch.startedAt, this.now());
  }

  oldestActiveElapsed(row: ProcessMemberRow): number | null {
    return getElapsedSince(row.oldestActiveAt, this.now());
  }

  scriptsStageTone(row: ProcessMemberRow): ProcessTone {
    return stageTone(row.scripts, scriptToneOf);
  }

  filesStageTone(row: ProcessMemberRow): ProcessTone {
    if (row.fileFailureCount > 0) return 'danger';
    if (row.fileStatusCounts.queued > 0) return 'active';
    if (row.fileStatusCounts.written > 0) return 'success';
    return stageTone(row.files, fileViewToneOf);
  }

  gatewayStageTone(row: ProcessMemberRow): ProcessTone {
    return stageTone(row.batches, batchToneOf);
  }

  private startClock(): void {
    if (!this.isBrowser || this.intervalId !== null) {
      return;
    }
    this.intervalId = setInterval(() => this.now.set(new Date()), 1_000);
  }

  private stopClock(): void {
    if (this.intervalId === null) {
      return;
    }
    clearInterval(this.intervalId);
    this.intervalId = null;
  }
}

function scriptToneOf(script: ProcessScriptCard): ProcessTone {
  return resolveScriptTone(script.stage);
}

function fileViewToneOf(view: ProcessMemberRow['files'][number]): ProcessTone {
  return resolveFileTone(view.file.stage);
}

function batchToneOf(batch: ProcessBatchCard): ProcessTone {
  return resolveBatchTone(batch.status);
}

function stageTone<T>(items: readonly T[], toneOf: (item: T) => ProcessTone): ProcessTone {
  let highest: ProcessTone = 'neutral';
  for (const item of items) {
    const tone = toneOf(item);
    if (tone === 'danger') return 'danger';
    if (tone === 'active') highest = 'active';
    else if (tone === 'success' && highest === 'neutral') highest = 'success';
  }
  return highest;
}

function snapshotDate(value: string | null | undefined): Date {
  const epoch = value ? Date.parse(value) : Number.NaN;
  return new Date(Number.isFinite(epoch) ? epoch : 0);
}

function isTerminalScript(stage: number | null): boolean {
  return (
    stage === ScriptRunStage.Completed ||
    stage === ScriptRunStage.Failed ||
    stage === ScriptRunStage.Cancelled
  );
}

function isTerminalFile(stage: number | null): boolean {
  return (
    stage === FileRunStage.Written ||
    stage === FileRunStage.Failed ||
    stage === FileRunStage.Cancelled
  );
}

function isTerminalBatch(status: number | null): boolean {
  return (
    status === SenderBatchStatus.Completed ||
    status === SenderBatchStatus.Failed ||
    status === SenderBatchStatus.SkippedByRequest
  );
}
