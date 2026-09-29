import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MemberRunLifecycleStatus, MemberRunStatusInfo, RunnerFailureInfo } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { TPipe, t } from '../../i18n/i18n';
import { resolveRunStatusLabel } from '../../state/utils/labels.util';
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

  readonly run = this.store.activeRun;
  readonly members = computed<MemberRunStatusInfo[]>(() =>
    Object.values(this.run()?.memberStatuses ?? {}).sort((left, right) =>
      left.memberName.localeCompare(right.memberName),
    ),
  );

  resolveRunStatusLabel = resolveRunStatusLabel;
  formatTimestamp = formatTimestamp;

  stopRun(): void {
    void this.store.stopRunAsync();
  }

  memberStatusLabel(member: MemberRunStatusInfo): string {
    switch (member.status) {
      case MemberRunLifecycleStatus.Running:
        return t('activeRun.memberProcessing');
      case MemberRunLifecycleStatus.Completed:
        return t('activeRun.memberCompleted');
      case MemberRunLifecycleStatus.Failed:
        return t('activeRun.memberFailed');
      case MemberRunLifecycleStatus.Cancelled:
        return t('activeRun.memberCancelled');
      default:
        return t('activeRun.memberWaiting');
    }
  }

  memberMessage(member: MemberRunStatusInfo): string | null {
    return member.failure?.message ?? member.message ?? null;
  }

  failureContext(failure: RunnerFailureInfo): string {
    return [
      failure.stage,
      failure.code,
      failure.memberName,
      failure.scriptCode,
      failure.filePath,
      failure.batchId,
    ]
      .filter((value): value is string => !!value)
      .join(' · ');
  }
}
