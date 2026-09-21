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
import { ProcessFileCard, ProcessMemberRow } from '../../state/utils/process-projection.models';
import { WorkflowStore } from '../../app.store';
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
import { formatTimestamp } from '../../state/utils/time.util';

interface ProcessFileView {
  file: ProcessFileCard;
  parentScriptCode: string | null;
}

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
  readonly rows = computed(() => buildProcessMemberRows(this.run(), this.now()));
  readonly hasProcessCards = computed(() =>
    this.rows().some(
      (row) => row.scripts.length > 0 || row.orphanFiles.length > 0 || row.batches.length > 0,
    ),
  );
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

  /** Files are rendered once in their own stage; the parent script remains visible as lineage. */
  filesFor(row: ProcessMemberRow): ProcessFileView[] {
    const attached = row.scripts.flatMap((script) =>
      script.files.map((file) => ({
        file,
        parentScriptCode: script.scriptCode || script.id,
      })),
    );
    return [...attached, ...row.orphanFiles.map((file) => ({ file, parentScriptCode: null }))];
  }

  fileName(file: ProcessFileCard): string {
    return file.fileName || file.path || file.id;
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
