import { CommonModule } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from 'primeng/checkbox';
import { Button } from 'primeng/button';
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
} from '../app.models';

@Component({
  selector: 'app-details-run-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, Button],
  templateUrl: './details-run-panel.component.html',
  styleUrl: './details-run-panel.component.css',
})
export class DetailsRunPanelComponent {
  readonly senderStatus = SenderBatchStatus;
  readonly presetTask = input.required<TaskUiState>();
  readonly memberGroups = input.required<MemberGroupViewModel[]>();
  readonly selectedMemberCode = input<string | null>(null);
  readonly canStartRun = input(false);
  readonly activeRun = input<RunStatusInfo | null>(null);
  readonly runBusy = input(false);
  readonly todayRuns = input.required<RunStatusInfo[]>();
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly buildArchiveUrl = input.required<(path: string) => string>();

  readonly toggleAll = output<boolean>();
  readonly toggleGroup = output<{ groupId: number; selected: boolean }>();
  readonly toggleMember = output<{ code: string; selected: boolean }>();
  readonly selectMember = output<string>();
  readonly startSelected = output<void>();
  readonly stopRun = output<void>();

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
    const code = this.selectedMemberCode();
    if (!code) {
      return null;
    }

    for (const group of this.memberGroups()) {
      const member = group.members.find((item) => item.code === code);
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
