import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MemberRunLifecycleStatus, MemberRunStatusInfo, RunnerFailureInfo } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { TPipe, t } from '../../i18n/i18n';
import { resolveRunStatusLabel } from '../../state/utils/labels.util';
import { formatTimestamp } from '../../state/utils/time.util';

@Component({
  selector: 'app-active-extra-view',
  standalone: true,
  imports: [CommonModule, MatButtonModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './active-extra-view.component.html',
  styleUrls: [
    '../details-run-panel/details-shared.css',
    '../details-run-panel/active-run-view.component.css',
  ],
})
export class ActiveExtraViewComponent {
  readonly store = inject(WorkflowStore);
  readonly run = this.store.activeExtraRun;
  readonly isBusy = this.store.isExtraBusy;
  readonly scripts = computed<MemberRunStatusInfo[]>(() =>
    Object.values(this.run()?.memberStatuses ?? {}).sort((left, right) =>
      left.memberName.localeCompare(right.memberName),
    ),
  );

  resolveRunStatusLabel = resolveRunStatusLabel;
  formatTimestamp = formatTimestamp;

  stopExtra(): void {
    void this.store.stopExtraAsync();
  }

  scriptStatusLabel(script: MemberRunStatusInfo): string {
    switch (script.status) {
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

  scriptMessage(script: MemberRunStatusInfo): string | null {
    return script.failure?.message ?? script.message ?? null;
  }

  failureContext(failure: RunnerFailureInfo): string {
    return [failure.stage, failure.code, failure.scriptCode, failure.filePath, failure.batchId]
      .filter((value): value is string => !!value)
      .join(' · ');
  }
}
