import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Button } from 'primeng/button';
import {
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunOutputArtifactInfo,
  SenderBatchStatusInfo,
} from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { DownloadHintStore } from '../../download-hint.store';
import { TPipe } from '../../i18n/i18n';
import {
  resolveMemberStatusLabel,
  resolveRunStatusLabel,
  resolveSenderStatusLabel,
} from '../../state/utils/labels.util';
import { buildRunMemberIndex, memberKey } from '../../state/utils/member-index.util';
import { isTerminalRunStatus } from '../../state/utils/run-status.util';
import { formatTimestamp } from '../../state/utils/time.util';

/**
 * Активная/последняя extra-выгрузка: пер-скриптовый статус, время старта, отмена.
 * Скрипт проецируется как «мембер» (MemberName = код скрипта) — переиспользуем индекс run'а.
 */
@Component({
  selector: 'app-active-extra-view',
  standalone: true,
  imports: [CommonModule, Button, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './active-extra-view.component.html',
  styleUrls: ['../details-run-panel/details-shared.css', '../details-run-panel/active-run-view.component.css'],
})
export class ActiveExtraViewComponent {
  readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);

  readonly run = this.store.activeExtraRun;
  readonly isBusy = this.store.isExtraBusy;

  private readonly index = computed(() => buildRunMemberIndex(this.run()));

  readonly scriptNames = computed<string[]>(() =>
    Array.from(this.index().memberNames).sort((left, right) => left.localeCompare(right)),
  );

  resolveRunStatusLabel = resolveRunStatusLabel;
  resolveMemberStatusLabel = resolveMemberStatusLabel;
  resolveSenderStatusLabel = resolveSenderStatusLabel;
  formatTimestamp = formatTimestamp;

  isTerminal(): boolean {
    const run = this.run();
    return !!run && isTerminalRunStatus(run.status);
  }

  scriptStatus(scriptCode: string): MemberRunStatusInfo | null {
    return this.index().statuses.get(memberKey(scriptCode)) ?? null;
  }

  scriptArtifacts(scriptCode: string): RunOutputArtifactInfo[] {
    return this.index().artifacts.get(memberKey(scriptCode)) ?? [];
  }

  senderBatchForScript(scriptCode: string): SenderBatchStatusInfo | null {
    return this.index().batches.get(memberKey(scriptCode)) ?? null;
  }

  /** #5: скрипт завершён, но ни одного файла. */
  isCompletedWithoutFiles(status: MemberRunStatusInfo): boolean {
    return (
      status.status === MemberRunLifecycleStatus.Completed &&
      this.scriptArtifacts(status.memberName).length === 0
    );
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  stopExtra(): void {
    void this.store.stopExtraAsync();
  }
}
