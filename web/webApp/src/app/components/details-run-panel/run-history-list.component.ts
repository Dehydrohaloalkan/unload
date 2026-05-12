import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { ConfirmDialog } from 'primeng/confirmdialog';
import {
  RequeueItem,
  RequeueToMqResponse,
  RunStatusInfo,
  SenderBatchStatus,
  SenderFileDispatchStateInfo,
} from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { DownloadHintStore } from '../../download-hint.store';
import {
  resolveRunStatusLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import { formatFileCount } from '../../state/utils/pluralize.util';
import { sortNames } from '../../state/utils/sort.util';
import { formatTimestamp } from '../../state/utils/time.util';

type HistoryTaskCode = 'run' | 'extra';

type HistoryFileRow = {
  key: string;
  taskCode: HistoryTaskCode;
  correlationId: string;
  memberName: string;
  fileName: string;
  filePath: string;
  occurredAt: string;
  sentToMq: boolean;
};

type HistoryRunNode = {
  key: string;
  taskCode: HistoryTaskCode;
  correlationId: string;
  status: string;
  startedAt: string;
  completedAt: string;
  occurredAt: string;
  publishToMq: boolean;
  memberNames: string[];
  memberFiles: Record<string, HistoryFileRow[]>;
};

type HistoryMemberEntry = {
  key: string;
  memberName: string;
  files: HistoryFileRow[];
};

type RequeueFileSummary = {
  total: number;
  sent: number;
  notSent: number;
};

@Component({
  selector: 'app-run-history-list',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, Button, ConfirmDialog],
  providers: [ConfirmationService],
  templateUrl: './run-history-list.component.html',
  styleUrls: ['./details-shared.css', './run-history-list.component.css'],
})
export class RunHistoryListComponent {
  readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);
  private readonly confirmationService = inject(ConfirmationService);

  readonly selectedHistoryFiles = signal<HistoryFileRow[]>([]);
  readonly historyNodes = computed(() => this.buildHistoryNodes());
  readonly canRequeue = computed(() => this.selectedHistoryFiles().length > 0);

  private readonly openHistoryRuns = signal(new Set<string>());
  private readonly openHistoryMembers = signal(new Set<string>());

  formatTimestamp = formatTimestamp;
  formatFileCount = formatFileCount;
  resolveSenderStatusLabel = resolveSenderStatusLabel;

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
    return this.selectedHistoryFiles().some((f) => f.key === key);
  }

  toggleHistoryFile(row: HistoryFileRow, checked: boolean): void {
    const next = checked
      ? [...this.selectedHistoryFiles().filter((f) => f.key !== row.key), row]
      : this.selectedHistoryFiles().filter((f) => f.key !== row.key);
    this.selectedHistoryFiles.set(next);
  }

  historyMembers(node: HistoryRunNode): HistoryMemberEntry[] {
    return node.memberNames.map((memberName) => ({
      key: `${node.key}|${memberName.toLowerCase()}`,
      memberName,
      files: node.memberFiles[memberName] ?? [],
    }));
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  confirmRequeue(): void {
    if (!this.canRequeue() || this.store.requeueRunning()) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Подтверждение',
      message: 'Отправить выбранные результаты в MQ?',
      acceptLabel: 'Отправить',
      rejectLabel: 'Отмена',
      accept: () => this.emitRequeue(),
    });
  }

  requeueFileSummary(result: RequeueToMqResponse): RequeueFileSummary {
    const selected = this.selectedHistoryFiles();
    const total = selected.length;
    if (!result?.results || total === 0) {
      return { total, sent: 0, notSent: total };
    }

    const selectedByItemKey = new Map<string, HistoryFileRow[]>();
    for (const row of selected) {
      const key = `${row.taskCode}|${row.correlationId}`;
      const bucket = selectedByItemKey.get(key);
      if (bucket) {
        bucket.push(row);
      } else {
        selectedByItemKey.set(key, [row]);
      }
    }

    let sent = 0;
    for (const [itemKey, selectedRows] of selectedByItemKey.entries()) {
      const [taskCode, correlationId] = itemKey.split('|', 2) as [HistoryTaskCode, string];
      const itemResult = result.results.find(
        (r) =>
          (r.taskCode ?? '').trim().toLowerCase() === taskCode &&
          (r.correlationId ?? '').trim() === correlationId,
      );
      if (!itemResult) {
        continue;
      }

      const failed =
        (itemResult.failedBatches ?? 0) > 0 ||
        itemResult.batches.some((b) => b.status === SenderBatchStatus.Failed);
      if (failed) {
        continue;
      }

      const acceptedMembers = new Set(
        (itemResult.batches ?? [])
          .filter((b) => b.status !== SenderBatchStatus.Failed)
          .map((b) => (b.memberName ?? '').trim().toLowerCase())
          .filter((name) => name.length > 0),
      );

      for (const row of selectedRows) {
        if (acceptedMembers.has((row.memberName ?? '').trim().toLowerCase())) {
          sent++;
        }
      }
    }

    return { total, sent, notSent: Math.max(0, total - sent) };
  }

  private emitRequeue(): void {
    const grouped = new Map<
      string,
      { taskCode: HistoryTaskCode; correlationId: string; filePaths: Set<string> }
    >();

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
      void this.store.requeueToMqAsync(items);
    }
  }

  private buildHistoryNodes(): HistoryRunNode[] {
    const nodeMap = new Map<string, HistoryRunNode>();
    const knownMemberNames = this.store.historyMemberNames();

    for (const run of this.store.todayRuns()) {
      const correlationId = run.correlationId?.trim();
      if (!correlationId) {
        continue;
      }

      const memberMap = this.buildRunMemberFiles(run, correlationId);
      nodeMap.set(`run|${correlationId}`, {
        key: `run|${correlationId}`,
        taskCode: 'run',
        correlationId,
        status: resolveRunStatusLabel(run.status),
        startedAt: run.createdAt,
        completedAt: run.updatedAt,
        occurredAt: run.updatedAt,
        publishToMq: run.publishToMq ?? true,
        memberNames: this.runMemberNames(run, knownMemberNames),
        memberFiles: Object.fromEntries(memberMap.entries()),
      });
    }

    for (const record of this.store.todayHistory()) {
      if (record.taskCode !== 'extra' || !record.correlationId || !record.outputPath) {
        continue;
      }

      const correlationId = record.correlationId;
      const memberMap = new Map<string, HistoryFileRow[]>();
      const files = this.store.outputFilesByPath()?.[record.outputPath] ?? [];
      const extraRun = this.store.allTodayRuns().find(
        (r) => (r.correlationId ?? '').trim() === correlationId,
      );

      for (const file of files) {
        const memberName = memberNameFromFile(file.fileName);
        const bucket = memberMap.get(memberName) ?? [];
        bucket.push({
          key: `extra|${correlationId}|${file.filePath}`,
          taskCode: 'extra',
          correlationId,
          memberName,
          fileName: file.fileName,
          filePath: file.filePath,
          occurredAt: file.modifiedAt || record.completedAt,
          sentToMq: this.resolveFileSentToMq(file.filePath, memberName, extraRun?.senderBatches ?? null),
        });
        memberMap.set(memberName, bucket);
      }

      const memberFiles = Object.fromEntries(memberMap.entries());
      nodeMap.set(`extra|${correlationId}`, {
        key: `extra|${correlationId}`,
        taskCode: 'extra',
        correlationId,
        status: 'Completed',
        startedAt: record.startedAt,
        completedAt: record.completedAt,
        occurredAt: record.completedAt,
        publishToMq: false,
        memberNames: sortNames([...knownMemberNames, ...Object.keys(memberFiles)]),
        memberFiles,
      });
    }

    return Array.from(nodeMap.values()).sort(
      (a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime(),
    );
  }

  private buildRunMemberFiles(run: RunStatusInfo, correlationId: string) {
    const memberMap = new Map<string, HistoryFileRow[]>();
    for (const artifact of run.outputArtifacts ?? []) {
      if (!artifact.filePath || !artifact.fileName) {
        continue;
      }
      const memberName = artifact.memberName || 'GLOBAL';
      const bucket = memberMap.get(memberName) ?? [];
      bucket.push({
        key: `run|${correlationId}|${artifact.filePath}`,
        taskCode: 'run',
        correlationId,
        memberName,
        fileName: artifact.fileName,
        filePath: artifact.filePath,
        occurredAt: artifact.occurredAt || run.updatedAt,
        sentToMq: this.resolveFileSentToMq(artifact.filePath, memberName, run.senderBatches),
      });
      memberMap.set(memberName, bucket);
    }
    return memberMap;
  }

  private resolveFileSentToMq(
    filePath: string,
    memberName: string,
    senderBatches: RunStatusInfo['senderBatches'],
  ): boolean {
    const key = memberName.toLowerCase();
    const batch = Object.values(senderBatches ?? {}).find(
      (b) => b.memberName.toLowerCase() === key,
    );
    if (!batch) {
      return false;
    }
    return this.isFileSentToMqPath(filePath, batch.sentFiles);
  }

  private isFileSentToMqPath(filePath: string, sentFiles: SenderFileDispatchStateInfo[]): boolean {
    const normalize = (v: string) => v.trim().toLowerCase().replaceAll('\\', '/');
    const target = normalize(filePath);
    return sentFiles.some((f) => normalize(f.filePath) === target);
  }

  private runMemberNames(run: RunStatusInfo, knownMemberNames: string[]): string[] {
    const names = new Set<string>(knownMemberNames);

    for (const status of Object.values(run.memberStatuses ?? {})) {
      if (status.memberName?.trim()) {
        names.add(status.memberName);
      }
    }
    for (const batch of Object.values(run.senderBatches ?? {})) {
      if (batch.memberName?.trim()) {
        names.add(batch.memberName);
      }
    }
    for (const artifact of run.outputArtifacts ?? []) {
      if (artifact.memberName?.trim()) {
        names.add(artifact.memberName);
      }
    }

    return sortNames(names);
  }
}

function memberNameFromFile(fileName: string): string {
  if (!fileName) {
    return 'UNKNOWN';
  }
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
}
