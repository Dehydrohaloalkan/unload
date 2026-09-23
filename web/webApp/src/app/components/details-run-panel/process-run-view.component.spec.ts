import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  FileRunStage,
  MemberRunLifecycleStatus,
  RunLifecycleStatus,
  RunStatusInfo,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { RU } from '../../i18n/ru';
import { ProcessRunViewComponent } from './process-run-view.component';

const AT = '2026-09-23T10:00:00Z';
const DONE_AT = '2026-09-23T10:02:00Z';

describe('ProcessRunViewComponent', () => {
  const activeRun = signal<RunStatusInfo | null>(null);

  beforeEach(async () => {
    activeRun.set(null);
    await TestBed.configureTestingModule({
      imports: [ProcessRunViewComponent],
      providers: [{ provide: WorkflowStore, useValue: { activeRun, isRunBusy: signal(false) } }],
    }).compileComponents();
  });

  it('renders empty states and one vertical pipeline with visibly distinct zones and handlers', () => {
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(RU['process.emptyNoRun']);

    activeRun.set(
      baseSnapshot({
        memberStatuses: {
          input: {
            memberName: 'Input',
            status: MemberRunLifecycleStatus.Pending,
            lastStep: null,
            message: null,
            queuePosition: 1,
            sequence: 1,
            updatedAt: AT,
          },
          resolver: {
            memberName: 'Resolver',
            status: MemberRunLifecycleStatus.Running,
            lastStep: null,
            message: null,
            queuePosition: 2,
            sequence: 2,
            updatedAt: AT,
          },
        },
        scriptStatuses: {
          queued: script('queued', ScriptRunStage.AwaitingWorker, null),
          running: script('running', ScriptRunStage.Running, 2),
        },
      }),
    );
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('[data-testid="process-pipeline"]')).not.toBeNull();
    expect(host.querySelectorAll('.process-zone')).toHaveLength(5);
    expect(host.querySelectorAll('.process-handler')).toHaveLength(2);
    expect(host.querySelectorAll('[data-testid^="process-worker-"]')).toHaveLength(4);
    expect(host.querySelector('[data-testid="process-worker-2"]')?.textContent).toContain(
      'RUNNING',
    );
    expect(host.textContent).not.toContain('→');
  });

  it('keeps a completed batch with unsent files visible without counting it as active', () => {
    activeRun.set(
      baseSnapshot({
        senderBatches: {
          completed: batch('completed', SenderBatchStatus.Completed, [
            planned('/completed/pending.txt', null),
          ]),
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.hasProcessCards()).toBe(true);
    expect(fixture.componentInstance.activeCardCount()).toBe(0);
    expect(fixture.nativeElement.textContent).not.toContain(RU['process.emptyNoCards']);
    expect(
      fixture.nativeElement.querySelector('[data-testid="process-node-sender"]')?.textContent,
    ).toContain('completed');
  });

  it('does not count failed sender batches as active cards', () => {
    activeRun.set(
      baseSnapshot({
        senderBatches: {
          failed: batch('failed', SenderBatchStatus.Failed, [planned('/failed/pending.txt', null)]),
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.hasProcessCards()).toBe(true);
    expect(fixture.componentInstance.activeCardCount()).toBe(0);
  });

  it('bounds resolver and worker failure cards while keeping paged reasons reachable', async () => {
    const memberStatuses = Object.fromEntries(
      Array.from({ length: 100 }, (_, index) => {
        const number = index + 1;
        return [
          `resolver-${number}`,
          {
            memberName: `Resolver ${number}`,
            status: MemberRunLifecycleStatus.Failed,
            lastStep: null,
            message: null,
            queuePosition: number,
            sequence: number,
            updatedAt: DONE_AT,
            failure: failure(
              'member',
              'resolver',
              `resolver-${number}`,
              `Resolver reason ${number}`,
            ),
          },
        ];
      }),
    );
    const scriptStatuses = Object.fromEntries(
      Array.from({ length: 100 }, (_, index) => {
        const number = index + 1;
        const failed = {
          ...script(`worker-${number}`, ScriptRunStage.Failed, 1),
          failure: failure('script', 'query', `worker-${number}`, `Worker reason ${number}`),
        };
        failed.workOrder = number;
        failed.sequence = number;
        failed.startedAt = AT;
        return [failed.id, failed];
      }),
    );
    activeRun.set(baseSnapshot({ memberStatuses, scriptStatuses }));
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const resolver = host.querySelector<HTMLElement>('[data-testid="process-node-resolver"]')!;
    const worker = host.querySelector<HTMLElement>('[data-testid="process-worker-1"]')!;

    expect(resolver.querySelectorAll('.process-card--button')).toHaveLength(20);
    expect(worker.querySelectorAll('.process-card--button')).toHaveLength(20);
    buttonWithText(resolver, 'Следующие 20').click();
    fixture.detectChanges();
    expect(resolver.textContent).toContain('21–40 из 100');
    expect(resolver.textContent).toContain('Resolver 21');
    buttonWithText(resolver, 'Показать причину ошибки').click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(host.querySelector('[data-testid="process-error-details"]')?.textContent).toContain(
      'resolver-21',
    );
    fixture.componentInstance.closeFailure();
    fixture.detectChanges();

    buttonWithText(worker, 'Следующие 20').click();
    fixture.detectChanges();
    expect(worker.textContent).toContain('21–40 из 100');
    buttonWithText(worker, 'Показать причину ошибки').click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(host.querySelector('[data-testid="process-error-details"]')?.textContent).toContain(
      'worker-21',
    );
  });

  it('keeps created, sender and delivered lists collapsed and limits each expanded list to 20 of 1000 files', () => {
    const fileStatuses = Object.fromEntries(
      Array.from({ length: 1_000 }, (_, index) => {
        const number = index + 1;
        return [
          `created-${number}`,
          {
            id: `created-${number}`,
            parentScriptId: 'script-created',
            memberName: 'Created member',
            scriptCode: 'SCRIPT_CREATED',
            chunkNumber: number,
            sequence: number,
            stage: FileRunStage.Written,
            createdAt: AT,
            queuedAt: AT,
            stageEnteredAt: AT,
            updatedAt: DONE_AT,
            completedAt: DONE_AT,
            fileName: `created-${number}.txt`,
            filePath: `/created/${number}.txt`,
            rows: 1,
            estimatedBytes: 10,
          },
        ];
      }),
    );
    const senderFiles = Array.from({ length: 1_000 }, (_, index) =>
      planned(`/sender/${index + 1}.txt`, null),
    );
    const deliveredFiles = Array.from({ length: 1_000 }, (_, index) =>
      planned(`/delivered/${index + 1}.txt`, DONE_AT),
    );
    activeRun.set(
      baseSnapshot({
        fileStatuses,
        senderBatches: {
          sending: batch('sending', SenderBatchStatus.InProgress, senderFiles),
          delivered: batch('delivered', SenderBatchStatus.Completed, deliveredFiles),
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('.process-file-card')).toHaveLength(0);
    expect(host.querySelectorAll('.process-dispatch-file')).toHaveLength(0);
    const fileGroup = host.querySelector<HTMLElement>('[data-testid^="process-file-group-"]')!;
    buttonWithText(fileGroup, 'Показать файлы').click();
    fixture.detectChanges();
    expect(fileGroup.querySelectorAll('.process-file-card')).toHaveLength(20);
    expect(fileGroup.textContent).toContain('1–20 из 1000');
    buttonWithText(fileGroup, 'Следующие 20').click();
    fixture.detectChanges();
    expect(fileGroup.querySelectorAll('.process-file-card')).toHaveLength(20);
    expect(fileGroup.textContent).toContain('21–40 из 1000');

    const sender = host.querySelector<HTMLElement>('[data-testid="process-node-sender"]')!;
    sender.querySelector<HTMLButtonElement>('.process-card')!.click();
    fixture.detectChanges();
    expect(sender.querySelectorAll('.process-dispatch-file')).toHaveLength(20);

    buttonWithText(
      host.querySelector('[data-testid="process-zone-delivered"]')!,
      'Показать отправленные',
    ).click();
    fixture.detectChanges();
    const delivered = host.querySelector<HTMLElement>('[data-testid="process-zone-delivered"]')!;
    delivered.querySelector<HTMLButtonElement>('.process-delivery-group__summary')!.click();
    fixture.detectChanges();
    expect(delivered.querySelectorAll('.process-dispatch-file')).toHaveLength(20);
    expect(
      host.querySelectorAll('.process-file-card, .process-dispatch-file').length,
    ).toBeLessThanOrEqual(60);
  });

  it('clamps every live-shrunk queue page to the current data instead of requiring repeated Previous clicks', () => {
    const members = Object.fromEntries(
      Array.from({ length: 21 }, (_, index) => [
        `member-${index + 1}`,
        {
          memberName: `Member ${index + 1}`,
          status: MemberRunLifecycleStatus.Pending,
          lastStep: null,
          message: null,
          queuePosition: index + 1,
          sequence: index + 1,
          updatedAt: AT,
        },
      ]),
    );
    activeRun.set(baseSnapshot({ memberStatuses: members }));
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();

    fixture.componentInstance.next('members', 21);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Member 21');

    activeRun.set(
      baseSnapshot({
        memberStatuses: { first: members['member-1'] },
      }),
    );
    fixture.detectChanges();
    expect(fixture.componentInstance.memberPage()).toBe(0);

    activeRun.set(baseSnapshot({ memberStatuses: members }));
    fixture.detectChanges();

    expect(fixture.componentInstance.memberPage()).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('Member 1');
    expect(fixture.nativeElement.textContent).not.toContain('Member 21');
  });

  it('renders a partially sent batch as distinct sender and delivered groups with disjoint files', () => {
    activeRun.set(
      baseSnapshot({
        senderBatches: {
          partial: batch('partial', SenderBatchStatus.Completed, [
            planned('/partial/sent.txt', DONE_AT),
            planned('/partial/waiting.txt', null),
          ]),
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const sender = host.querySelector<HTMLElement>('[data-testid="process-node-sender"]')!;
    const delivered = host.querySelector<HTMLElement>('[data-testid="process-zone-delivered"]')!;

    expect(sender.querySelectorAll('.process-card')).toHaveLength(1);
    expect(delivered.querySelectorAll('.process-delivery-group')).toHaveLength(0);
    sender.querySelector<HTMLButtonElement>('.process-card')!.click();
    fixture.detectChanges();
    expect(sender.textContent).toContain('waiting.txt');
    expect(sender.textContent).not.toContain('sent.txt');

    buttonWithText(delivered, 'Показать отправленные').click();
    fixture.detectChanges();
    const summary = delivered.querySelector<HTMLElement>('.process-delivery-group__summary')!;
    expect(summary.textContent).toContain('Member');
    summary.click();
    fixture.detectChanges();
    expect(delivered.textContent).toContain('sent.txt');
    expect(delivered.textContent).not.toContain('waiting.txt');
    expect(sender.querySelectorAll('.process-card')).toHaveLength(1);
    expect(delivered.querySelectorAll('.process-delivery-group')).toHaveLength(1);
    expect(
      sender.querySelector('.process-card')?.classList.contains('process-delivery-group'),
    ).toBe(false);
  });

  it('shows static timestamps instead of per-card ticking clocks and a final end-to-end timeline', () => {
    activeRun.set(
      baseSnapshot({
        memberStatuses: {
          waiting: {
            memberName: 'Waiting',
            status: MemberRunLifecycleStatus.Pending,
            lastStep: null,
            message: null,
            queuePosition: 1,
            sequence: 1,
            updatedAt: AT,
          },
        },
        senderBatches: {
          delivered: batch('delivered', SenderBatchStatus.Completed, [
            planned('/done/result.txt', DONE_AT),
          ]),
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Ожидает с');
    expect(host.textContent).not.toMatch(/\d\d:\d\d:\d\d назад/);
    buttonWithText(host, 'Показать отправленные').click();
    fixture.detectChanges();
    expect(host.textContent).toContain('В очереди с');
    expect(host.textContent).toContain('Отправка с');
    expect(host.textContent).toContain('Завершено');
    expect(host.textContent).toContain('Всего:');
  });

  it('keeps a failure on its handler and opens text-only accessible details', async () => {
    activeRun.set(
      baseSnapshot({
        status: RunLifecycleStatus.Failed,
        memberStatuses: {
          failed: {
            memberName: 'Resolver failed',
            status: MemberRunLifecycleStatus.Failed,
            lastStep: null,
            message: null,
            queuePosition: 1,
            sequence: 1,
            updatedAt: DONE_AT,
            failure: {
              ...failure('member', 'resolver', 'resolver-code', '<img src=x> safe text'),
              memberName: 'Resolver <img src=member>',
              scriptCode: 'SELECT <svg onload=x>',
              workerId: 2,
              filePath: '/out/<img src=file>.txt',
              chunkNumber: 7,
            },
          },
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const resolver = host.querySelector<HTMLElement>('[data-testid="process-node-resolver"]')!;

    resolver.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[data-testid="process-error-details"]')!;
    expect(dialog.querySelector('[role="dialog"]')).not.toBeNull();
    expect(dialog.textContent).toContain('resolver-code');
    expect(dialog.textContent).toContain('<img src=x> safe text');
    expect(dialog.textContent).toContain('Resolver <img src=member>');
    expect(dialog.textContent).toContain('SELECT <svg onload=x>');
    expect(dialog.textContent).toContain('2');
    expect(dialog.textContent).toContain('/out/<img src=file>.txt');
    expect(dialog.textContent).toContain('7');
    expect(dialog.querySelector('img')).toBeNull();
    expect(dialog.querySelector('svg')).toBeNull();
    expect(document.activeElement).toBe(host.querySelector('[data-testid="process-error-close"]'));
  });

  it('uses native fullscreen on the process root and restores the control state on exit', async () => {
    activeRun.set(
      baseSnapshot({
        memberStatuses: {
          input: {
            memberName: 'Input',
            status: MemberRunLifecycleStatus.Pending,
            lastStep: null,
            message: null,
            updatedAt: AT,
          },
        },
      }),
    );
    const rootPrototype = document.documentElement as HTMLElement & {
      requestFullscreen?: () => Promise<void>;
    };
    const originalRequest = rootPrototype.requestFullscreen;
    rootPrototype.requestFullscreen = () => Promise.resolve();
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const root = host.querySelector<HTMLElement>('[data-testid="process-root"]')!;
    const button = host.querySelector<HTMLButtonElement>('[data-testid="process-fullscreen"]')!;
    const request = vi.fn().mockResolvedValue(undefined);
    root.requestFullscreen = request;

    await fixture.componentInstance.toggleFullscreen({ currentTarget: button } as unknown as Event);
    expect(request).toHaveBeenCalledOnce();
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: root });
    fixture.componentInstance.onFullscreenChange();
    expect(fixture.componentInstance.isFullscreen()).toBe(true);
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
    fixture.componentInstance.onFullscreenChange();
    expect(fixture.componentInstance.isFullscreen()).toBe(false);
    rootPrototype.requestFullscreen = originalRequest;
  });

  it('resets expanded content and pages when a different run replaces the snapshot', () => {
    activeRun.set(
      baseSnapshot({
        fileStatuses: {
          file: {
            id: 'file',
            parentScriptId: 'script',
            memberName: 'Member',
            scriptCode: 'SCRIPT',
            chunkNumber: 1,
            stage: FileRunStage.Written,
            createdAt: AT,
            queuedAt: AT,
            stageEnteredAt: AT,
            updatedAt: DONE_AT,
            completedAt: DONE_AT,
            fileName: 'file.txt',
            filePath: '/file.txt',
          },
        },
      }),
    );
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const group = fixture.componentInstance.pipeline().fileGroups[0];
    fixture.componentInstance.toggleFileGroup(group);
    fixture.componentInstance.next('groupFiles', 100);
    fixture.componentInstance.toggleDelivered();

    activeRun.update((run) => ({ ...run!, correlationId: 'run-2' }));
    fixture.detectChanges();
    expect(fixture.componentInstance.expandedFileGroup()).toBeNull();
    expect(fixture.componentInstance.fileDetailPage()).toBe(0);
    expect(fixture.componentInstance.deliveredExpanded()).toBe(false);
  });
});

function baseSnapshot(overrides: Partial<RunStatusInfo>): RunStatusInfo {
  return {
    correlationId: 'run-1',
    taskCode: 'run',
    status: RunLifecycleStatus.Running,
    targetCodes: [],
    createdAt: AT,
    updatedAt: DONE_AT,
    ...overrides,
  };
}

function script(id: string, stage: ScriptRunStage, workerId: number | null) {
  return {
    id,
    memberName: 'Member',
    scriptCode: id.toUpperCase(),
    stage,
    workOrder: 1,
    sequence: 1,
    discoveredAt: AT,
    stageEnteredAt: AT,
    startedAt: stage === ScriptRunStage.Running ? AT : null,
    updatedAt: DONE_AT,
    completedAt: null,
    workerId,
    records: 10,
  };
}

function planned(filePath: string, sentAt: string | null) {
  return {
    fileName: filePath.split('/').at(-1)!,
    filePath,
    queuedAt: AT,
    sentAt,
    estimatedBytes: 10,
    actualBytes: sentAt ? 9 : null,
  };
}

function batch(id: string, status: SenderBatchStatus, plannedFiles: ReturnType<typeof planned>[]) {
  return {
    batchId: id,
    memberName: 'Member',
    status,
    updatedAt: DONE_AT,
    queuedAt: AT,
    startedAt: status === SenderBatchStatus.Ready ? null : AT,
    fileCount: plannedFiles.length,
    sequence: 1,
    plannedFiles,
    sentFiles: plannedFiles
      .filter((item) => item.sentAt)
      .map((item) => ({ filePath: item.filePath, sentAt: item.sentAt! })),
  };
}

function failure(entityType: string, stage: string, code: string, message: string) {
  return {
    entityType,
    entityId: `${entityType}-id`,
    stage,
    code,
    message,
    occurredAt: DONE_AT,
    memberName: null,
    scriptCode: null,
    workerId: null,
    filePath: null,
    chunkNumber: null,
    batchId: null,
  };
}

function buttonWithText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find((item) =>
    item.textContent?.includes(text),
  );
  if (!button) throw new Error(`Button not found: ${text}`);
  return button;
}
