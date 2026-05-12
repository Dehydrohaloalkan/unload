import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Button } from 'primeng/button';
import {
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunLifecycleStatus,
  RunOutputArtifactInfo,
  RunStatusInfo,
  RunWorkerStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
  SenderFileDispatchStateInfo,
} from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { DownloadHintStore } from '../../download-hint.store';
import {
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveRunnerStepLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import { isTerminalRunStatus } from '../../state/utils/run-status.util';

@Component({
  selector: 'app-active-run-view',
  standalone: true,
  imports: [CommonModule, Button],
  templateUrl: './active-run-view.component.html',
  styleUrls: ['./details-shared.css', './active-run-view.component.css'],
})
export class ActiveRunViewComponent {
  readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);

  readonly run = this.store.activeRun;
  readonly isRunActive = computed(() => {
    const run = this.run();
    return !!run && !isTerminalRunStatus(run.status);
  });

  resolveRunStatusLabel = resolveRunStatusLabel;
  resolveSenderStatusLabel = resolveSenderStatusLabel;
  resolveRunnerStepLabel = resolveRunnerStepLabel;
  resolveMemberStatusLabel = resolveMemberStatusLabel;

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  stopRun(): void {
    void this.store.stopRunAsync();
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
    const key = memberName.toLowerCase();
    return (
      Object.values(run.memberStatuses ?? {}).find(
        (item) => item.memberName.toLowerCase() === key,
      ) ?? null
    );
  }

  memberWorkers(run: RunStatusInfo, memberName: string): RunWorkerStatusInfo[] {
    const key = memberName.toLowerCase();
    return Object.values(run.workerStatuses ?? {})
      .filter((item) => item.memberName?.toLowerCase() === key)
      .sort((left, right) => left.workerId - right.workerId);
  }

  senderBatchForMember(run: RunStatusInfo, memberName: string): SenderBatchStatusInfo | null {
    const key = memberName.toLowerCase();
    return (
      Object.values(run.senderBatches ?? {}).find(
        (item) => item.memberName.toLowerCase() === key,
      ) ?? null
    );
  }

  memberArtifacts(run: RunStatusInfo, memberName: string): RunOutputArtifactInfo[] {
    const key = memberName.toLowerCase();
    return (run.outputArtifacts ?? []).filter(
      (artifact) => artifact.memberName?.toLowerCase() === key,
    );
  }

  isFileSentToMq(artifactPath: string, sentFiles: SenderFileDispatchStateInfo[]): boolean {
    const normalize = (value: string) => value.trim().toLowerCase().replaceAll('\\', '/');
    const target = normalize(artifactPath);
    return sentFiles.some((file) => normalize(file.filePath) === target);
  }

  resolveMemberEffectiveStatusLabel(run: RunStatusInfo, member: MemberRunStatusInfo): string {
    const batch = this.senderBatchForMember(run, member.memberName);
    if (!batch) {
      const runTerminal = isTerminalRunStatus(run.status);
      if (runTerminal && member.status === MemberRunLifecycleStatus.Running) {
        return resolveRunnerStepLabel(member.lastStep);
      }
      return resolveMemberStatusLabel(member.status);
    }

    if (batch.status === SenderBatchStatus.Failed) {
      return 'Failed';
    }

    if (batch.status === SenderBatchStatus.SkippedByRequest) {
      return resolveMemberStatusLabel(member.status);
    }

    if (batch.status !== SenderBatchStatus.Completed) {
      return member.status === MemberRunLifecycleStatus.Failed ||
        member.status === MemberRunLifecycleStatus.Cancelled
        ? resolveMemberStatusLabel(member.status)
        : 'Publishing to MQ';
    }

    return resolveMemberStatusLabel(member.status);
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  protected readonly RunLifecycleStatus = RunLifecycleStatus;
}
