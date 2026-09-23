import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Injector,
  PLATFORM_ID,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { WorkflowStore } from '../../app.store';
import { MemberRunLifecycleStatus, ScriptRunStage, SenderBatchStatus } from '../../app.models';
import { TPipe } from '../../i18n/i18n';
import {
  ProcessBatchCard,
  ProcessDispatchFile,
  ProcessFailureDetail,
  ProcessFileCard,
  ProcessFileGroup,
  ProcessMemberCard,
  ProcessScriptCard,
  ProcessWorkerSlot,
} from '../../state/utils/process-projection.models';
import { PROCESS_PAGE_SIZE, buildProcessPipeline } from '../../state/utils/process-projection.util';
import {
  formatProcessBytes,
  formatProcessCount,
  formatProcessDuration,
} from '../../state/utils/process-display.util';
import {
  resolveFileStageLabel,
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveScriptStageLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import {
  resolveFileTone,
  resolveMemberTone,
  resolveScriptTone,
} from '../../state/utils/process-tone.util';
import { formatTimestamp } from '../../state/utils/time.util';

type PageTarget =
  | 'members'
  | 'resolver'
  | 'scripts'
  | 'groups'
  | 'groupFiles'
  | 'sender'
  | 'senderFiles'
  | 'delivered'
  | 'deliveredFiles';

interface ProcessPage<T> {
  items: readonly T[];
  page: number;
  first: number;
  last: number;
  hasPrevious: boolean;
  hasNext: boolean;
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private focusReturnTarget: HTMLElement | null = null;
  private fullscreenReturnTarget: HTMLElement | null = null;

  readonly run = this.store.activeRun;
  readonly pipeline = computed(() => buildProcessPipeline(this.run()));
  readonly selectedFailure = signal<ProcessFailureDetail | null>(null);
  readonly memberPage = signal(0);
  readonly resolverPage = signal(0);
  readonly scriptPage = signal(0);
  readonly workerFailurePages = signal<number[]>([0, 0, 0, 0]);
  readonly expandedFileGroup = signal<string | null>(null);
  readonly fileDetailPage = signal(0);
  readonly fileGroupPage = signal(0);
  readonly senderPage = signal(0);
  readonly expandedSenderBatch = signal<string | null>(null);
  readonly senderFilePage = signal(0);
  readonly deliveredExpanded = signal(false);
  readonly deliveredPage = signal(0);
  readonly expandedDeliveredBatch = signal<string | null>(null);
  readonly deliveredFilePage = signal(0);
  readonly isFullscreen = signal(false);
  readonly fullscreenSupported =
    this.isBrowser && typeof document.documentElement.requestFullscreen === 'function';

  readonly runFailure = computed(
    () => this.pipeline().failures.find((item) => item.reference.type === 'run') ?? null,
  );
  readonly hasProcessCards = computed(() => {
    const value = this.pipeline();
    return (
      value.members.length +
        value.scripts.length +
        value.fileGroups.length +
        value.senderBatches.length +
        value.delivered.length >
      0
    );
  });
  readonly activeCardCount = computed(() => {
    const value = this.pipeline();
    return (
      value.memberInput.filter((item) => item.status === MemberRunLifecycleStatus.Pending).length +
      value.resolver.filter((item) => item.status === MemberRunLifecycleStatus.Running).length +
      value.scriptQueue.filter((item) => item.stage === ScriptRunStage.AwaitingWorker).length +
      value.workers.filter((item) => item.assignment?.stage === ScriptRunStage.Running).length +
      value.fileGroups.reduce((sum, item) => sum + item.statusCounts.queued, 0) +
      value.senderBatches.filter(
        (batch) =>
          batch.status === SenderBatchStatus.Ready || batch.status === SenderBatchStatus.InProgress,
      ).length
    );
  });
  readonly visibleMemberInput = computed(
    () => page(this.pipeline().memberInput, this.memberPage()).items,
  );
  readonly visibleResolver = computed(
    () => page(this.pipeline().resolver, this.resolverPage()).items,
  );
  readonly visibleScriptQueue = computed(
    () => page(this.pipeline().scriptQueue, this.scriptPage()).items,
  );
  readonly visibleFileGroups = computed(
    () => page(this.pipeline().fileGroups, this.fileGroupPage()).items,
  );
  readonly senderBatches = computed(() => this.pipeline().senderBatches);
  readonly visibleSenderBatches = computed(
    () => page(this.senderBatches(), this.senderPage()).items,
  );
  readonly visibleDelivered = computed(
    () => page(this.pipeline().delivered, this.deliveredPage()).items,
  );
  readonly deliveredFileCount = computed(() =>
    this.pipeline().delivered.reduce((sum, batch) => sum + batch.sentFiles.length, 0),
  );

  constructor() {
    let correlationId = '';
    effect(() => {
      const nextCorrelationId = this.pipeline().correlationId;
      if (nextCorrelationId === correlationId) return;
      correlationId = nextCorrelationId;
      this.collapsePagedContent();
    });
    effect(() => {
      const value = this.pipeline();
      this.clampPageState(this.memberPage, value.memberInput.length);
      this.clampPageState(this.resolverPage, value.resolver.length);
      this.clampPageState(this.scriptPage, value.scriptQueue.length);
      this.clampPageState(this.fileGroupPage, value.fileGroups.length);
      this.clampPageState(
        this.fileDetailPage,
        value.fileGroups.find((group) => group.id === this.expandedFileGroup())?.details.length ??
          0,
      );
      this.clampPageState(this.senderPage, value.senderBatches.length);
      this.clampPageState(
        this.senderFilePage,
        value.senderBatches.find((batch) => batch.id === this.expandedSenderBatch())?.senderFiles
          .length ?? 0,
      );
      this.clampPageState(this.deliveredPage, value.delivered.length);
      this.clampPageState(
        this.deliveredFilePage,
        value.delivered.find((batch) => batch.id === this.expandedDeliveredBatch())?.sentFiles
          .length ?? 0,
      );
      const workerPages = this.workerFailurePages();
      const nextWorkerPages = value.workers.map((worker) =>
        clampPage(worker.retainedFailures.length, workerPages[worker.workerId - 1] ?? 0),
      );
      if (nextWorkerPages.some((page, index) => page !== workerPages[index])) {
        this.workerFailurePages.set(nextWorkerPages);
      }
    });
  }

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

  visibleGroupFiles(group: ProcessFileGroup): readonly ProcessFileCard[] {
    return this.expandedFileGroup() === group.id
      ? page(group.details, this.fileDetailPage()).items
      : [];
  }
  visibleSenderFiles(batch: ProcessBatchCard): readonly ProcessDispatchFile[] {
    return this.expandedSenderBatch() === batch.id
      ? page(batch.senderFiles, this.senderFilePage()).items
      : [];
  }
  visibleDeliveredFiles(batch: ProcessBatchCard): readonly ProcessDispatchFile[] {
    return this.expandedDeliveredBatch() === batch.id
      ? page(batch.sentFiles, this.deliveredFilePage()).items
      : [];
  }
  toggleFileGroup(group: ProcessFileGroup): void {
    this.expandedFileGroup.set(this.expandedFileGroup() === group.id ? null : group.id);
    this.fileDetailPage.set(0);
  }
  toggleSenderBatch(batch: ProcessBatchCard): void {
    this.expandedSenderBatch.set(this.expandedSenderBatch() === batch.id ? null : batch.id);
    this.senderFilePage.set(0);
  }
  toggleDelivered(): void {
    this.deliveredExpanded.update((value) => !value);
    this.expandedDeliveredBatch.set(null);
    this.deliveredFilePage.set(0);
  }
  toggleDeliveredBatch(batch: ProcessBatchCard): void {
    this.expandedDeliveredBatch.set(this.expandedDeliveredBatch() === batch.id ? null : batch.id);
    this.deliveredFilePage.set(0);
  }
  next(target: PageTarget, total: number): void {
    const state = this.pageSignal(target);
    const current = clampPage(total, state());
    state.set(current < maxPage(total) ? current + 1 : current);
  }
  previous(target: PageTarget, total: number): void {
    const state = this.pageSignal(target);
    const current = clampPage(total, state());
    state.set(current > 0 ? current - 1 : current);
  }
  pageView<T>(items: readonly T[], target: PageTarget): ProcessPage<T> {
    return page(items, this.pageSignal(target)());
  }
  workerFailurePageView(worker: ProcessWorkerSlot): ProcessPage<ProcessScriptCard> {
    const index = worker.workerId - 1;
    return page(worker.retainedFailures, this.workerFailurePages()[index] ?? 0);
  }
  nextWorkerFailures(worker: ProcessWorkerSlot): void {
    this.changeWorkerFailurePage(worker, 1);
  }
  previousWorkerFailures(worker: ProcessWorkerSlot): void {
    this.changeWorkerFailurePage(worker, -1);
  }

  openFailure(failure: ProcessFailureDetail, event?: Event): void {
    this.focusReturnTarget = (event?.currentTarget as HTMLElement | null) ?? null;
    this.selectedFailure.set(failure);
    afterNextRender(
      {
        write: () =>
          this.host.nativeElement
            .querySelector<HTMLElement>('[data-testid="process-error-close"]')
            ?.focus(),
      },
      { injector: this.injector },
    );
  }
  closeFailure(): void {
    if (!this.selectedFailure()) return;
    this.selectedFailure.set(null);
    const target = this.focusReturnTarget;
    this.focusReturnTarget = null;
    queueMicrotask(() => target?.focus());
  }
  async toggleFullscreen(event: Event): Promise<void> {
    if (!this.fullscreenSupported) return;
    if (document.fullscreenElement) {
      await document.exitFullscreen();
      return;
    }
    this.fullscreenReturnTarget = event.currentTarget as HTMLElement;
    try {
      await this.processRoot()?.requestFullscreen();
    } catch {
      this.fullscreenReturnTarget = null;
    }
  }
  @HostListener('document:fullscreenchange')
  onFullscreenChange(): void {
    const active = document.fullscreenElement === this.processRoot();
    this.isFullscreen.set(active);
    if (!active && this.fullscreenReturnTarget) {
      const target = this.fullscreenReturnTarget;
      this.fullscreenReturnTarget = null;
      queueMicrotask(() => target.focus());
    }
  }
  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeFailure();
  }
  @HostListener('document:keydown', ['$event'])
  trapDialogFocus(event: KeyboardEvent): void {
    if (!this.selectedFailure() || event.key !== 'Tab') return;
    const dialog = this.host.nativeElement.querySelector<HTMLElement>('[role="dialog"]');
    if (!dialog) return;
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
      ),
    );
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;
    if (event.shiftKey && (active === first || !dialog.contains(active))) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && (active === last || !dialog.contains(active))) {
      event.preventDefault();
      first.focus();
    }
  }

  fileName(file: ProcessFileCard | ProcessDispatchFile): string {
    return file.fileName || file.path || file.id;
  }
  memberAriaLabel(item: ProcessMemberCard): string {
    return `${item.name}. ${resolveMemberStatusLabel(item.status)}`;
  }
  workerFailure(item: ProcessWorkerSlot): ProcessFailureDetail | null {
    return item.failure ?? item.retainedFailures.find((script) => script.failure)?.failure ?? null;
  }

  private processRoot(): HTMLElement | null {
    return this.host.nativeElement.querySelector('[data-testid="process-root"]');
  }
  private changeWorkerFailurePage(worker: ProcessWorkerSlot, direction: -1 | 1): void {
    const index = worker.workerId - 1;
    const currentPages = this.workerFailurePages();
    const current = clampPage(worker.retainedFailures.length, currentPages[index] ?? 0);
    const next = clampPage(worker.retainedFailures.length, current + direction);
    if (current === next) return;
    const pages = [...currentPages];
    pages[index] = next;
    this.workerFailurePages.set(pages);
  }
  private clampPageState(state: ReturnType<typeof signal<number>>, total: number): void {
    const current = state();
    const next = clampPage(total, current);
    if (current !== next) state.set(next);
  }
  private pageSignal(target: PageTarget) {
    switch (target) {
      case 'groups':
        return this.fileGroupPage;
      case 'members':
        return this.memberPage;
      case 'resolver':
        return this.resolverPage;
      case 'scripts':
        return this.scriptPage;
      case 'groupFiles':
        return this.fileDetailPage;
      case 'sender':
        return this.senderPage;
      case 'senderFiles':
        return this.senderFilePage;
      case 'delivered':
        return this.deliveredPage;
      case 'deliveredFiles':
        return this.deliveredFilePage;
    }
  }

  private collapsePagedContent(): void {
    this.expandedFileGroup.set(null);
    this.memberPage.set(0);
    this.resolverPage.set(0);
    this.scriptPage.set(0);
    this.workerFailurePages.set([0, 0, 0, 0]);
    this.fileDetailPage.set(0);
    this.fileGroupPage.set(0);
    this.expandedSenderBatch.set(null);
    this.senderFilePage.set(0);
    this.senderPage.set(0);
    this.deliveredExpanded.set(false);
    this.expandedDeliveredBatch.set(null);
    this.deliveredFilePage.set(0);
    this.deliveredPage.set(0);
  }
}

function page<T>(items: readonly T[], index: number): ProcessPage<T> {
  const lastPage = maxPage(items.length);
  const safe = clampPage(items.length, index);
  const first = items.length ? safe * PROCESS_PAGE_SIZE + 1 : 0;
  return {
    items: items.slice(safe * PROCESS_PAGE_SIZE, safe * PROCESS_PAGE_SIZE + PROCESS_PAGE_SIZE),
    page: safe,
    first,
    last: Math.min(items.length, (safe + 1) * PROCESS_PAGE_SIZE),
    hasPrevious: safe > 0,
    hasNext: safe < lastPage,
  };
}

function maxPage(total: number): number {
  return Math.max(0, Math.ceil(total / PROCESS_PAGE_SIZE) - 1);
}

function clampPage(total: number, index: number): number {
  return Math.max(0, Math.min(maxPage(total), index));
}
