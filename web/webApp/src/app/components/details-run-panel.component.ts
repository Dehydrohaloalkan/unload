import { CommonModule } from '@angular/common';
import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from 'primeng/checkbox';
import { Button } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import {
  MemberGroupViewModel,
  MemberRunStatusInfo,
  RunOutputArtifactInfo,
  RunLifecycleStatus,
  MemberRunLifecycleStatus,
  RunnerStep,
  RunWorkerStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
  SenderFileDispatchStateInfo,
  RunStatusInfo,
  TaskUiState,
  TaskRecord,
  RequeueItem,
  RequeueToMqResponse,
  MqUploadResponse,
  OutputFileInfo,
} from '../app.models';
import { DownloadHintStore } from '../download-hint.store';

type HistoryFileRow = {
  key: string;
  taskCode: 'run' | 'extra';
  correlationId: string;
  memberName: string;
  fileName: string;
  filePath: string;
  occurredAt: string;
};

type HistoryRunNode = {
  key: string;
  taskCode: 'run' | 'extra';
  correlationId: string;
  status: string;
  occurredAt: string;
  members: { memberName: string; files: HistoryFileRow[] }[];
};

@Component({
  selector: 'app-details-run-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    Checkbox,
    Button,
    TabsModule,
    ConfirmDialog,
  ],
  providers: [ConfirmationService],
  templateUrl: './details-run-panel.component.html',
  styleUrl: './details-run-panel.component.css',
})
export class DetailsRunPanelComponent {
  readonly downloadHint = inject(DownloadHintStore);
  private readonly confirmationService = inject(ConfirmationService);
  readonly senderStatus = SenderBatchStatus;
  readonly presetTask = input.required<TaskUiState>();
  readonly memberGroups = input.required<MemberGroupViewModel[]>();
  readonly selectedMemberKey = input<string | null>(null);
  readonly canStartRun = input(false);
  readonly activeRun = input<RunStatusInfo | null>(null);
  readonly runBusy = input(false);
  readonly todayRuns = input.required<RunStatusInfo[]>();
  readonly todayHistory = input.required<TaskRecord[]>();
  readonly filesByOutputPath = input<Record<string, OutputFileInfo[]>>({});
  readonly historyMemberNames = input<string[]>([]);
  readonly publishToMq = input(true);
  readonly requeueRunning = input(false);
  readonly requeueResult = input<RequeueToMqResponse | null>(null);
  readonly uploadRunning = input(false);
  readonly uploadResult = input<MqUploadResponse | null>(null);
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly buildArchiveUrl = input.required<(path: string) => string>();

  readonly toggleAll = output<boolean>();
  readonly toggleGroup = output<{ groupId: number; selected: boolean }>();
  readonly toggleMember = output<{ targetCodes: string[]; selected: boolean }>();
  readonly selectMember = output<string>();
  readonly startSelected = output<void>();
  readonly stopRun = output<void>();
  readonly publishToMqChange = output<boolean>();
  readonly requeueToMq = output<RequeueItem[]>();
  readonly uploadToMq = output<{ files: File[]; memberName: string | null }>();

  selectedHistoryFiles: HistoryFileRow[] = [];
  readonly historyNodes = computed(() => this.buildHistoryNodes());

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  canRequeue(): boolean {
    return this.selectedHistoryFiles.length > 0;
  }

  emitRequeue(): void {
    const grouped = new Map<string, { taskCode: 'run' | 'extra'; correlationId: string; filePaths: Set<string> }>();
    for (const row of this.selectedHistoryFiles) {
      const key = `${row.taskCode}|${row.correlationId}`;
      const existing = grouped.get(key);
      if (existing) {
        existing.filePaths.add(row.filePath);
        continue;
      }

      grouped.set(key, {
        taskCode: row.taskCode,
        correlationId: row.correlationId,
        filePaths: new Set([row.filePath]),
      });
    }

    const items: RequeueItem[] = Array.from(grouped.values()).map((item) => ({
      taskCode: item.taskCode,
      correlationId: item.correlationId,
      filePaths: Array.from(item.filePaths),
    }));

    if (items.length > 0) {
      this.requeueToMq.emit(items);
    }
  }

  confirmRequeue(): void {
    if (!this.canRequeue() || this.requeueRunning()) {
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

  onFileInputChange(event: Event, inputEl: HTMLInputElement): void {
    const files = Array.from((event.target as HTMLInputElement).files ?? []);
    if (files.length === 0) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Подтверждение',
      message: `Отправить в MQ выбранные файлы (${files.length})?`,
      acceptLabel: 'Отправить',
      rejectLabel: 'Отмена',
      accept: () => {
        this.uploadToMq.emit({ files, memberName: null });
        inputEl.value = '';
      },
      reject: () => {
        inputEl.value = '';
      },
    });
  }

  isHistoryFileSelected(key: string): boolean {
    return this.selectedHistoryFiles.some((f) => f.key === key);
  }

  toggleHistoryFile(row: HistoryFileRow, checked: boolean): void {
    if (checked) {
      if (!this.selectedHistoryFiles.some((f) => f.key === row.key)) {
        this.selectedHistoryFiles = [...this.selectedHistoryFiles, row];
      }
    } else {
      this.selectedHistoryFiles = this.selectedHistoryFiles.filter((f) => f.key !== row.key);
    }
  }

  private buildHistoryNodes(): HistoryRunNode[] {
    const nodeMap = new Map<string, HistoryRunNode>();
    const knownMemberNames = this.allKnownMemberNames();

    for (const run of this.todayRuns()) {
      const correlationId = run.correlationId?.trim();
      if (!correlationId) {
        continue;
      }

      const memberMap = new Map<string, HistoryFileRow[]>();
      for (const memberName of this.runMemberNames(run, knownMemberNames)) {
        memberMap.set(memberName, []);
      }

      for (const artifact of run.outputArtifacts ?? []) {
        if (!artifact.filePath || !artifact.fileName) {
          continue;
        }
        const memberName = artifact.memberName || 'GLOBAL';
        if (!memberMap.has(memberName)) {
          memberMap.set(memberName, []);
        }
        memberMap.get(memberName)!.push({
          key: `run|${correlationId}|${artifact.filePath}`,
          taskCode: 'run',
          correlationId,
          memberName,
          fileName: artifact.fileName,
          filePath: artifact.filePath,
          occurredAt: artifact.occurredAt || run.updatedAt,
        });
      }

      nodeMap.set(`run|${correlationId}`, {
        key: `run|${correlationId}`,
        taskCode: 'run',
        correlationId,
        status: this.resolveRunStatusLabel(run.status),
        occurredAt: run.updatedAt,
        members: Array.from(memberMap.entries()).map(([memberName, files]) => ({ memberName, files })),
      });
    }

    for (const record of this.todayHistory()) {
      if (record.taskCode !== 'extra' || !record.correlationId || !record.outputPath) {
        continue;
      }

      const correlationId = record.correlationId;
      const memberMap = new Map<string, HistoryFileRow[]>();
      for (const memberName of knownMemberNames) {
        memberMap.set(memberName, []);
      }
      const files = this.filesByOutputPath()?.[record.outputPath] ?? [];

      for (const file of files) {
        const memberName = this.memberNameFromFile(file.fileName);
        if (!memberMap.has(memberName)) {
          memberMap.set(memberName, []);
        }
        memberMap.get(memberName)!.push({
          key: `extra|${correlationId}|${file.filePath}`,
          taskCode: 'extra',
          correlationId,
          memberName,
          fileName: file.fileName,
          filePath: file.filePath,
          occurredAt: file.modifiedAt || record.completedAt,
        });
      }

      nodeMap.set(`extra|${correlationId}`, {
        key: `extra|${correlationId}`,
        taskCode: 'extra',
        correlationId,
        status: 'Completed',
        occurredAt: record.completedAt,
        members: Array.from(memberMap.entries()).map(([memberName, files]) => ({ memberName, files })),
      });
    }

    return Array.from(nodeMap.values()).sort(
      (a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime(),
    );
  }

  private allKnownMemberNames(): string[] {
    return this.historyMemberNames();
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

    return [...names].sort((left, right) => left.localeCompare(right));
  }

  private memberNameFromFile(fileName: string): string {
    if (!fileName) {
      return 'UNKNOWN';
    }

    const dotIndex = fileName.lastIndexOf('.');
    return dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
  }

  formatFileCount(count: number): string {
    const mod10 = count % 10;
    const mod100 = count % 100;

    if (mod10 === 1 && mod100 !== 11) {
      return `${count} файл`;
    }

    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
      return `${count} файла`;
    }

    return `${count} файлов`;
  }

  allMembersSelected(): boolean {
    const groups = this.memberGroups();
    const total = groups.reduce((sum, group) => sum + group.members.length, 0);
    const selected = groups.reduce(
      (sum, group) => sum + group.members.filter((member) => member.selected).length,
      0,
    );
    return total > 0 && selected === total;
  }

  allMembersPartial(): boolean {
    const groups = this.memberGroups();
    const total = groups.reduce((sum, group) => sum + group.members.length, 0);
    const selected = groups.reduce(
      (sum, group) => sum + group.members.filter((member) => member.selected).length,
      0,
    );
    return total > 0 && selected > 0 && selected < total;
  }

  groupAllSelected(group: MemberGroupViewModel): boolean {
    return group.members.length > 0 && group.members.every((member) => member.selected);
  }

  groupPartial(group: MemberGroupViewModel): boolean {
    const selectedCount = group.members.filter((member) => member.selected).length;
    return selectedCount > 0 && selectedCount < group.members.length;
  }

  selectedMember() {
    const memberKey = this.selectedMemberKey();
    if (!memberKey) {
      return null;
    }

    for (const group of this.memberGroups()) {
      const member = group.members.find((item) => item.key === memberKey);
      if (member) {
        return member;
      }
    }

    return null;
  }

  formatTimestamp(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString('ru-RU');
  }

  runMembers(run: RunStatusInfo): MemberRunStatusInfo[] {
    return Object.values(run.memberStatuses ?? {});
  }

  activeMemberNames(run: RunStatusInfo): string[] {
    const names = new Set<string>();

    for (const status of Object.values(run.memberStatuses ?? {})) {
      names.add(status.memberName);
    }

    for (const batch of Object.values(run.senderBatches ?? {})) {
      names.add(batch.memberName);
    }

    for (const artifact of run.outputArtifacts ?? []) {
      if (artifact.memberName) {
        names.add(artifact.memberName);
      }
    }

    return [...names].sort((left, right) => left.localeCompare(right));
  }

  memberStatus(run: RunStatusInfo, memberName: string): MemberRunStatusInfo | null {
    const found = Object.values(run.memberStatuses ?? {}).find(
      (item) => item.memberName.toLowerCase() === memberName.toLowerCase(),
    );
    return found ?? null;
  }

  memberWorkers(run: RunStatusInfo, memberName: string): RunWorkerStatusInfo[] {
    return Object.values(run.workerStatuses ?? {})
      .filter((item) => item.memberName?.toLowerCase() === memberName.toLowerCase())
      .sort((left, right) => left.workerId - right.workerId);
  }

  senderBatchForMember(run: RunStatusInfo, memberName: string): SenderBatchStatusInfo | null {
    const found = Object.values(run.senderBatches ?? {}).find(
      (item) => item.memberName.toLowerCase() === memberName.toLowerCase(),
    );
    return found ?? null;
  }

  isFileSentToMq(artifactPath: string, sentFiles: SenderFileDispatchStateInfo[]): boolean {
    const normalize = (value: string) => value.trim().toLowerCase().replaceAll('\\', '/');
    const target = normalize(artifactPath);
    return sentFiles.some((file) => normalize(file.filePath) === target);
  }

  memberArtifacts(run: RunStatusInfo, memberName: string): RunOutputArtifactInfo[] {
    return (run.outputArtifacts ?? []).filter(
      (artifact) => artifact.memberName?.toLowerCase() === memberName.toLowerCase(),
    );
  }

  globalRunArtifacts(run: RunStatusInfo): RunOutputArtifactInfo[] {
    return (run.outputArtifacts ?? []).filter((artifact) => !artifact.memberName);
  }

  isRunActive(run: RunStatusInfo): boolean {
    return (
      run.status !== RunLifecycleStatus.Completed &&
      run.status !== RunLifecycleStatus.Failed &&
      run.status !== RunLifecycleStatus.Cancelled
    );
  }

  senderBatches(run: RunStatusInfo): SenderBatchStatusInfo[] {
    return Object.values(run.senderBatches ?? {}).sort((left, right) =>
      left.memberName.localeCompare(right.memberName),
    );
  }

  resolveSenderStatusLabel(status: SenderBatchStatus): string {
    switch (status) {
      case SenderBatchStatus.Ready:
        return 'Ready';
      case SenderBatchStatus.InProgress:
        return 'In progress';
      case SenderBatchStatus.Completed:
        return 'Completed';
      case SenderBatchStatus.Failed:
        return 'Failed';
      case SenderBatchStatus.SkippedByRequest:
        return 'Skipped';
      default:
        return 'Unknown';
    }
  }

  resolveMemberStatusLabel(status: MemberRunLifecycleStatus | null | undefined): string {
    switch (status) {
      case MemberRunLifecycleStatus.Pending:
        return 'Pending';
      case MemberRunLifecycleStatus.Running:
        return 'Running';
      case MemberRunLifecycleStatus.Completed:
        return 'Completed';
      case MemberRunLifecycleStatus.Failed:
        return 'Failed';
      case MemberRunLifecycleStatus.Cancelled:
        return 'Cancelled';
      default:
        return 'Unknown';
    }
  }

  resolveMemberEffectiveStatusLabel(run: RunStatusInfo, member: MemberRunStatusInfo): string {
    const batch = this.senderBatchForMember(run, member.memberName);
    if (!batch) {
      return this.resolveMemberStatusLabel(member.status);
    }

    if (batch.status === SenderBatchStatus.Failed) {
      return 'Failed';
    }

    if (batch.status === SenderBatchStatus.SkippedByRequest) {
      return this.resolveMemberStatusLabel(member.status);
    }

    // If sender didn't complete, member is not complete yet (even if runner says Completed).
    if (batch.status !== SenderBatchStatus.Completed) {
      return member.status === MemberRunLifecycleStatus.Failed || member.status === MemberRunLifecycleStatus.Cancelled
        ? this.resolveMemberStatusLabel(member.status)
        : 'Publishing to MQ';
    }

    return this.resolveMemberStatusLabel(member.status);
  }

  senderStatusLabelForMember(run: RunStatusInfo, memberName: string): string | null {
    const batch = this.senderBatchForMember(run, memberName);
    return batch ? this.resolveSenderStatusLabel(batch.status) : null;
  }

  resolveRunStatusLabel(status: RunLifecycleStatus | null | undefined): string {
    switch (status) {
      case RunLifecycleStatus.Running:
        return 'Running';
      case RunLifecycleStatus.Completed:
        return 'Completed';
      case RunLifecycleStatus.Failed:
        return 'Failed';
      case RunLifecycleStatus.Cancelled:
        return 'Cancelled';
      case RunLifecycleStatus.CancellationRequested:
        return 'Cancellation requested';
      default:
        return 'Unknown';
    }
  }

  resolveRunnerStepLabel(step: RunnerStep | null | undefined): string {
    switch (step) {
      case RunnerStep.RequestAccepted:
        return 'Request accepted';
      case RunnerStep.TargetsResolved:
        return 'Targets resolved';
      case RunnerStep.ScriptDiscovered:
        return 'Script discovered';
      case RunnerStep.QueryStarted:
        return 'Query started';
      case RunnerStep.QueryCompleted:
        return 'Query completed';
      case RunnerStep.ChunkCreated:
        return 'Chunk created';
      case RunnerStep.FileWritten:
        return 'File written';
      case RunnerStep.ScriptCompleted:
        return 'Script completed';
      case RunnerStep.PublishedToMq:
        return 'Published to MQ';
      case RunnerStep.Completed:
        return 'Completed';
      case RunnerStep.Failed:
        return 'Failed';
      default:
        return 'Unknown';
    }
  }
}
