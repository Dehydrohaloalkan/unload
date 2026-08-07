import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunLifecycleStatus,
  RunOutputArtifactInfo,
  RunWorkerStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
  SenderFileDispatchStateInfo,
} from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { DownloadHintStore } from '../../download-hint.store';
import { TPipe, t } from '../../i18n/i18n';
import {
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveRunnerStepLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import {
  buildRunMemberIndex,
  isFileSentViaBatch,
  memberKey,
} from '../../state/utils/member-index.util';
import { isTerminalRunStatus } from '../../state/utils/run-status.util';
import { formatTimestamp } from '../../state/utils/time.util';

@Component({
  selector: 'app-active-run-view',
  standalone: true,
  imports: [CommonModule, MatButtonModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
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

  /** Индексы по member-name, считаются один раз на каждое изменение run'а. */
  private readonly index = computed(() => buildRunMemberIndex(this.run()));

  readonly activeMemberNames = computed<string[]>(() =>
    Array.from(this.index().memberNames).sort((left, right) => left.localeCompare(right)),
  );

  resolveRunStatusLabel = resolveRunStatusLabel;
  resolveSenderStatusLabel = resolveSenderStatusLabel;
  resolveRunnerStepLabel = resolveRunnerStepLabel;
  resolveMemberStatusLabel = resolveMemberStatusLabel;
  formatTimestamp = formatTimestamp;

  /** Мембер завершён, но не записал ни одного файла — подсветить «0 файлов». */
  isCompletedWithoutFiles(member: MemberRunStatusInfo): boolean {
    return (
      member.status === MemberRunLifecycleStatus.Completed &&
      this.memberArtifacts(member.memberName).length === 0
    );
  }

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  stopRun(): void {
    void this.store.stopRunAsync();
  }

  memberStatus(memberName: string): MemberRunStatusInfo | null {
    return this.index().statuses.get(memberKey(memberName)) ?? null;
  }

  memberWorkers(memberName: string): RunWorkerStatusInfo[] {
    return this.index().workers.get(memberKey(memberName)) ?? [];
  }

  senderBatchForMember(memberName: string): SenderBatchStatusInfo | null {
    return this.index().batches.get(memberKey(memberName)) ?? null;
  }

  memberArtifacts(memberName: string): RunOutputArtifactInfo[] {
    return this.index().artifacts.get(memberKey(memberName)) ?? [];
  }

  isFileSentToGateway(artifactPath: string, sentFiles: SenderFileDispatchStateInfo[]): boolean {
    return isFileSentViaBatch(artifactPath, sentFiles);
  }

  resolveMemberEffectiveStatusLabel(member: MemberRunStatusInfo): string {
    const batch = this.senderBatchForMember(member.memberName);
    if (!batch) {
      if (member.status === MemberRunLifecycleStatus.Running && member.lastStep != null) {
        return resolveRunnerStepLabel(member.lastStep);
      }
      return resolveMemberStatusLabel(member.status);
    }

    if (batch.status === SenderBatchStatus.Failed) {
      return t('status.sender.failed');
    }

    if (batch.status === SenderBatchStatus.SkippedByRequest) {
      return resolveMemberStatusLabel(member.status);
    }

    if (batch.status !== SenderBatchStatus.Completed) {
      return member.status === MemberRunLifecycleStatus.Failed ||
        member.status === MemberRunLifecycleStatus.Cancelled
        ? resolveMemberStatusLabel(member.status)
        : t('activeRun.publishing');
    }

    return resolveMemberStatusLabel(member.status);
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  protected readonly RunLifecycleStatus = RunLifecycleStatus;
}
