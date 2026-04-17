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
  SenderBatchStatus,
  SenderBatchStatusInfo,
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
}
