import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  PLATFORM_ID,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { WorkflowStore } from '../../app.store';
import {
  MemberRunLifecycleStatus,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';
import { TPipe } from '../../i18n/i18n';
import {
  ProcessBatchCard,
  ProcessFailureDetail,
  ProcessFileCard,
  ProcessFileGroup,
  ProcessMemberCard,
  ProcessScriptCard,
  ProcessWorkerSlot,
} from '../../state/utils/process-projection.models';
import {
  PROCESS_FILE_DETAIL_PAGE_SIZE,
  buildProcessPipeline,
} from '../../state/utils/process-projection.util';
import {
  formatProcessBytes,
  formatProcessCount,
  formatProcessDuration,
  getElapsedSince,
} from '../../state/utils/process-display.util';
import {
  resolveFileStageLabel,
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveScriptStageLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import {
  resolveBatchTone,
  resolveFileTone,
  resolveMemberTone,
  resolveScriptTone,
} from '../../state/utils/process-tone.util';
import { formatTimestamp } from '../../state/utils/time.util';

interface ProcessFileGroupView {
  group: ProcessFileGroup;
  visibleDetails: readonly ProcessFileCard[];
  visibleCount: number;
  firstVisible: number;
  lastVisible: number;
  isExpanded: boolean;
  hasPrevious: boolean;
  hasNext: boolean;
}

interface ProcessFilePageState {
  correlationId: string;
  pages: Readonly<Record<string, number>>;
}

@Component({
  selector: 'app-process-run-view',
  standalone: true,
  imports: [TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './process-run-view.component.html',
  styleUrls: [
    './details-shared.css',
    './process-run-view.component.css',
    './process-run-view-dialog.component.css',
  ],
})
export class ProcessRunViewComponent {
  readonly store = inject(WorkflowStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);
  private intervalId: ReturnType<typeof setInterval> | null = null;
  private focusReturnTarget: HTMLElement | null = null;

  readonly now = signal(new Date());
  readonly run = this.store.activeRun;
  /** Snapshot-only projection: the one-second display clock is intentionally not a dependency. */
  readonly pipeline = computed(() => buildProcessPipeline(this.run()));
  readonly filePageState = signal<ProcessFilePageState>({ correlationId: '', pages: {} });
  readonly selectedFailure = signal<ProcessFailureDetail | null>(null);
  readonly runFailure = computed(
    () => this.pipeline().failures.find((failure) => failure.reference.type === 'run') ?? null,
  );
  readonly hasProcessCards = computed(() => {
    const pipeline = this.pipeline();
    return (
      pipeline.members.length +
        pipeline.scripts.length +
        pipeline.fileGroups.length +
        pipeline.senderQueue.length +
        pipeline.senderInProgress.length +
        pipeline.delivered.length >
      0
    );
  });
  readonly activeCardCount = computed(() => {
    const pipeline = this.pipeline();
    const activeWorkers = pipeline.workers.reduce(
      (count, worker) =>
        count + (worker.assignment?.stage === ScriptRunStage.Running ? 1 : 0),
      0,
    );
    const queuedFiles = pipeline.fileGroups.reduce(
      (count, group) => count + group.statusCounts.queued,
      0,
    );
    return (
      pipeline.memberInput.filter((member) => member.status === MemberRunLifecycleStatus.Pending)
        .length +
      pipeline.resolver.filter((member) => member.status === MemberRunLifecycleStatus.Running).length +
      pipeline.scriptQueue.filter((script) => script.stage === ScriptRunStage.AwaitingWorker).length +
      activeWorkers +
      queuedFiles +
      pipeline.senderQueue.filter((batch) => batch.status === SenderBatchStatus.Ready).length +
      pipeline.senderInProgress.filter((batch) => batch.status === SenderBatchStatus.InProgress)
        .length
    );
  });
  readonly fileGroupViews = computed<readonly ProcessFileGroupView[]>(() => {
    const pipeline = this.pipeline();
    const state = this.filePageState();
    const pages = state.correlationId === pipeline.correlationId ? state.pages : {};
    return pipeline.fileGroups.map((group) => {
      const requestedPage = pages[group.id];
      const maxPage = Math.max(0, Math.ceil(group.count / group.detailPageSize) - 1);
      const page = requestedPage === undefined ? null : Math.min(maxPage, requestedPage);
      const offset = page === null ? 0 : page * group.detailPageSize;
      const visibleDetails =
        page === null ? Object.freeze([]) : group.details.slice(offset, offset + group.detailPageSize);
      return {
        group,
        visibleDetails,
        visibleCount: visibleDetails.length,
        firstVisible: visibleDetails.length ? offset + 1 : 0,
        lastVisible: offset + visibleDetails.length,
        isExpanded: page !== null,
        hasPrevious: page !== null && page > 0,
        hasNext: page !== null && page < maxPage,
      };
    });
  });
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
      const runBusy = this.store.isRunBusy();
      const shouldTick = runBusy || this.activeCardCount() > 0;
      shouldTick ? this.startClock() : this.stopClock();
    });
    effect(() => {
      const pipeline = this.pipeline();
      const current = this.filePageState();
      if (current.correlationId !== pipeline.correlationId) {
        this.filePageState.set({ correlationId: pipeline.correlationId, pages: {} });
        return;
      }
      const pages: Record<string, number> = {};
      for (const group of pipeline.fileGroups) {
        const requested = current.pages[group.id];
        if (requested === undefined) continue;
        const maxPage = Math.max(0, Math.ceil(group.count / group.detailPageSize) - 1);
        pages[group.id] = Math.min(maxPage, Math.max(0, requested));
      }
      if (!samePages(current.pages, pages)) {
        this.filePageState.set({ correlationId: pipeline.correlationId, pages });
      }
    });
    this.destroyRef.onDestroy(() => this.stopClock());
  }

  openFileGroup(group: ProcessFileGroup): void {
    this.setFilePage(group, 0);
  }

  showNextFilePage(group: ProcessFileGroup): void {
    this.setFilePage(group, this.currentFilePage(group) + 1);
  }

  showPreviousFilePage(group: ProcessFileGroup): void {
    this.setFilePage(group, Math.max(0, this.currentFilePage(group) - 1));
  }

  collapseFileGroup(group: ProcessFileGroup): void {
    this.filePageState.update((current) => {
      if (current.correlationId !== this.pipeline().correlationId) return current;
      const { [group.id]: _removed, ...pages } = current.pages;
      return { ...current, pages };
    });
  }

  openFailure(failure: ProcessFailureDetail, event?: Event): void {
    this.focusReturnTarget = (event?.currentTarget as HTMLElement | null) ?? null;
    this.selectedFailure.set(failure);
    if (this.isBrowser) {
      setTimeout(() =>
        this.elementRef.nativeElement
          .querySelector<HTMLElement>('[data-testid="process-error-close"]')
          ?.focus(),
        0,
      );
    }
  }

  closeFailure(): void {
    if (!this.selectedFailure()) return;
    this.selectedFailure.set(null);
    if (this.isBrowser) {
      const target = this.focusReturnTarget;
      this.focusReturnTarget = null;
      setTimeout(() => target?.focus(), 0);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeFailure();
  }

  @HostListener('document:keydown', ['$event'])
  trapDialogFocus(event: Event): void {
    if (!this.selectedFailure()) return;
    const keyboardEvent = event as KeyboardEvent;
    if (keyboardEvent.key !== 'Tab') return;
    const dialog = this.elementRef.nativeElement.querySelector<HTMLElement>('[role="dialog"]');
    if (!dialog) return;
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    ).filter((element) => !element.hasAttribute('hidden'));
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = this.elementRef.nativeElement.ownerDocument.activeElement;
    if (keyboardEvent.shiftKey && (active === first || !dialog.contains(active))) {
      keyboardEvent.preventDefault();
      last.focus();
    } else if (!keyboardEvent.shiftKey && (active === last || !dialog.contains(active))) {
      keyboardEvent.preventDefault();
      first.focus();
    }
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

  memberAriaLabel(member: ProcessMemberCard): string {
    return `${member.name}. ${resolveMemberStatusLabel(member.status)}`;
  }

  scriptAriaLabel(script: ProcessScriptCard): string {
    return `${script.scriptCode || script.id}. ${resolveScriptStageLabel(script.stage)}`;
  }

  batchAriaLabel(batch: ProcessBatchCard): string {
    return `${batch.id}. ${resolveSenderStatusLabel(batch.status)}`;
  }

  workerFailure(worker: ProcessWorkerSlot): ProcessFailureDetail | null {
    return (
      worker.failure ?? worker.retainedFailures.find((script) => script.failure)?.failure ?? null
    );
  }

  private currentFilePage(group: ProcessFileGroup): number {
    const state = this.filePageState();
    if (state.correlationId !== this.pipeline().correlationId) return 0;
    const maxPage = Math.max(0, Math.ceil(group.count / group.detailPageSize) - 1);
    return Math.min(maxPage, Math.max(0, state.pages[group.id] ?? 0));
  }

  private setFilePage(group: ProcessFileGroup, page: number): void {
    const correlationId = this.pipeline().correlationId;
    const maxPage = Math.max(0, Math.ceil(group.count / PROCESS_FILE_DETAIL_PAGE_SIZE) - 1);
    this.filePageState.update((current) => ({
      correlationId,
      pages: {
        ...(current.correlationId === correlationId ? current.pages : {}),
        [group.id]: Math.min(maxPage, Math.max(0, page)),
      },
    }));
  }

  private startClock(): void {
    if (!this.isBrowser || this.intervalId !== null) return;
    this.intervalId = setInterval(() => this.now.set(new Date()), 1_000);
  }

  private stopClock(): void {
    if (this.intervalId === null) return;
    clearInterval(this.intervalId);
    this.intervalId = null;
  }
}

function isTerminalScript(stage: number | null): boolean {
  return (
    stage === ScriptRunStage.Completed ||
    stage === ScriptRunStage.Failed ||
    stage === ScriptRunStage.Cancelled
  );
}

function isTerminalBatch(status: number | null): boolean {
  return (
    status === SenderBatchStatus.Completed ||
    status === SenderBatchStatus.Failed ||
    status === SenderBatchStatus.SkippedByRequest
  );
}

function samePages(
  left: Readonly<Record<string, number>>,
  right: Readonly<Record<string, number>>,
): boolean {
  const leftKeys = Object.keys(left);
  const rightKeys = Object.keys(right);
  return (
    leftKeys.length === rightKeys.length && leftKeys.every((key) => left[key] === right[key])
  );
}
