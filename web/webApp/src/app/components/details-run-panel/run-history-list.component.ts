import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { RequeueItem } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { DownloadHintStore } from '../../download-hint.store';
import { TPipe, t } from '../../i18n/i18n';
import { I18nKey } from '../../i18n/ru';
import {
  GatewayDelivery,
  HistoryFileRow,
  HistoryRunNode,
  HistoryScriptNode,
  HistoryTaskCode,
  buildConfirmedSentPaths,
  buildHistoryNodes,
  summarizeRequeue,
} from '../../state/utils/history-projection.util';
import { resolveSenderStatusLabel } from '../../state/utils/labels.util';
import { formatFileCount } from '../../state/utils/pluralize.util';
import { formatTimestamp } from '../../state/utils/time.util';

@Component({
  selector: 'app-run-history-list',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, Button, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './run-history-list.component.html',
  styleUrls: ['./details-shared.css', './run-history-list.component.css'],
})
export class RunHistoryListComponent {
  readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);
  private readonly confirmationService = inject(ConfirmationService);

  readonly selectedHistoryFiles = signal<HistoryFileRow[]>([]);
  /** Снапшот выделения на момент инициирования requeue — основание для сверки ответа API. */
  private readonly requeueSnapshot = signal<HistoryFileRow[] | null>(null);

  private readonly confirmedSentPaths = computed(() =>
    buildConfirmedSentPaths(this.store.requeueResult(), this.requeueSnapshot()),
  );

  /** Опциональный фильтр по типу задачи: панель extra показывает только extra-узлы. */
  readonly taskCodeFilter = input<HistoryTaskCode | null>(null);

  readonly historyNodes = computed<HistoryRunNode[]>(() => {
    const nodes = buildHistoryNodes({
      todayRuns: this.store.todayRuns(),
      todayHistory: this.store.todayHistory(),
      allTodayRuns: this.store.allTodayRuns(),
      outputFilesByPath: this.store.outputFilesByPath() ?? {},
      knownMemberNames: this.store.historyMemberNames(),
      confirmedSentPaths: this.confirmedSentPaths(),
    });
    const filter = this.taskCodeFilter();
    return filter ? nodes.filter((node) => node.taskCode === filter) : nodes;
  });

  readonly canRequeue = computed(() => this.selectedHistoryFiles().length > 0);

  private readonly openHistoryRuns = signal(new Set<string>());
  private readonly openHistoryMembers = signal(new Set<string>());
  // Раскрытие уровней extra: скрипт и банк (для дерева выгрузка → скрипт → банк → файлы).
  private readonly openHistoryScripts = signal(new Set<string>());
  private readonly openHistoryBanks = signal(new Set<string>());

  formatTimestamp = formatTimestamp;
  formatFileCount = formatFileCount;
  resolveSenderStatusLabel = resolveSenderStatusLabel;

  private static readonly DELIVERY_LABEL_KEYS: Record<GatewayDelivery, I18nKey> = {
    delivered: 'history.deliveryDelivered',
    partial: 'history.deliveryPartial',
    failed: 'history.deliveryFailed',
    notSent: 'history.deliveryNotSent',
    off: 'history.deliveryOff',
  };

  private static readonly DELIVERY_TITLE_KEYS: Record<GatewayDelivery, I18nKey> = {
    delivered: 'history.deliveryDeliveredTitle',
    partial: 'history.deliveryPartialTitle',
    failed: 'history.deliveryFailedTitle',
    notSent: 'history.deliveryNotSentTitle',
    off: 'history.deliveryOffTitle',
  };

  deliveryLabelKey(delivery: GatewayDelivery): I18nKey {
    return RunHistoryListComponent.DELIVERY_LABEL_KEYS[delivery];
  }

  deliveryTitleKey(delivery: GatewayDelivery): I18nKey {
    return RunHistoryListComponent.DELIVERY_TITLE_KEYS[delivery];
  }

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  isHistoryRunOpen(key: string): boolean {
    return this.openHistoryRuns().has(key);
  }

  onHistoryRunToggle(event: Event, runKey: string): void {
    const opened = (event.target as HTMLDetailsElement).open;
    const runs = new Set(this.openHistoryRuns());
    const members = new Set(this.openHistoryMembers());
    if (opened) {
      runs.add(runKey);
    } else {
      runs.delete(runKey);
      for (const memberKey of Array.from(members)) {
        if (memberKey.startsWith(`${runKey}|`)) {
          members.delete(memberKey);
        }
      }
    }
    this.openHistoryRuns.set(runs);
    this.openHistoryMembers.set(members);
  }

  isHistoryMemberOpen(key: string): boolean {
    return this.openHistoryMembers().has(key);
  }

  onHistoryMemberToggle(event: Event, memberKey: string): void {
    const opened = (event.target as HTMLDetailsElement).open;
    const members = new Set(this.openHistoryMembers());
    if (opened) {
      members.add(memberKey);
    } else {
      members.delete(memberKey);
    }
    this.openHistoryMembers.set(members);
  }

  isHistoryFileSelected(key: string): boolean {
    return this.selectedHistoryFiles().some((file) => file.key === key);
  }

  toggleHistoryFile(row: HistoryFileRow, checked: boolean): void {
    const next = checked
      ? [...this.selectedHistoryFiles().filter((file) => file.key !== row.key), row]
      : this.selectedHistoryFiles().filter((file) => file.key !== row.key);
    this.selectedHistoryFiles.set(next);
  }

  historyMembers(node: HistoryRunNode) {
    return node.memberNames.map((memberName) => ({
      key: `${node.key}|${memberName.toLowerCase()}`,
      memberName,
      files: node.memberFiles[memberName] ?? [],
    }));
  }

  historyScripts(node: HistoryRunNode): HistoryScriptNode[] {
    return node.scripts ?? [];
  }

  scriptKey(node: HistoryRunNode, script: HistoryScriptNode): string {
    return `${node.key}|script|${script.scriptCode.toLowerCase()}`;
  }

  bankKey(node: HistoryRunNode, script: HistoryScriptNode, bankName: string): string {
    return `${node.key}|script|${script.scriptCode.toLowerCase()}|bank|${bankName.toLowerCase()}`;
  }

  isHistoryScriptOpen(key: string): boolean {
    return this.openHistoryScripts().has(key);
  }

  onHistoryScriptToggle(event: Event, scriptKey: string): void {
    const opened = (event.target as HTMLDetailsElement).open;
    const scripts = new Set(this.openHistoryScripts());
    if (opened) {
      scripts.add(scriptKey);
    } else {
      scripts.delete(scriptKey);
    }
    this.openHistoryScripts.set(scripts);
  }

  isHistoryBankOpen(key: string): boolean {
    return this.openHistoryBanks().has(key);
  }

  onHistoryBankToggle(event: Event, bankKey: string): void {
    const opened = (event.target as HTMLDetailsElement).open;
    const banks = new Set(this.openHistoryBanks());
    if (opened) {
      banks.add(bankKey);
    } else {
      banks.delete(bankKey);
    }
    this.openHistoryBanks.set(banks);
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  confirmRequeue(): void {
    if (!this.canRequeue() || this.store.requeueRunning()) {
      return;
    }

    this.confirmationService.confirm({
      header: t('confirm.header'),
      message: t('history.confirmMessage'),
      acceptLabel: t('history.confirmAccept'),
      rejectLabel: t('confirm.reject'),
      accept: () => this.emitRequeue(),
    });
  }

  requeueFileSummary(result: ReturnType<WorkflowStore['requeueResult']>) {
    return result ? summarizeRequeue(result, this.selectedHistoryFiles()) : null;
  }

  private emitRequeue(): void {
    this.requeueSnapshot.set([...this.selectedHistoryFiles()]);

    const grouped = new Map<string, { taskCode: string; correlationId: string; filePaths: Set<string> }>();
    for (const row of this.selectedHistoryFiles()) {
      const key = `${row.taskCode}|${row.correlationId}`;
      const existing = grouped.get(key);
      if (existing) {
        existing.filePaths.add(row.filePath);
      } else {
        grouped.set(key, {
          taskCode: row.taskCode,
          correlationId: row.correlationId,
          filePaths: new Set([row.filePath]),
        });
      }
    }

    const items: RequeueItem[] = Array.from(grouped.values()).map((item) => ({
      taskCode: item.taskCode,
      correlationId: item.correlationId,
      filePaths: Array.from(item.filePaths),
    }));

    if (items.length > 0) {
      void this.store.requeueToGatewayAsync(items);
    }
  }
}
