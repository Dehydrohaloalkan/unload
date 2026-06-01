import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from 'primeng/checkbox';
import { ExtraBankInfo, TaskRecord, TaskUiState } from '../app.models';
import { WorkflowStore } from '../app.store';
import { DownloadHintStore } from '../download-hint.store';
import { TPipe, t } from '../i18n/i18n';
import { byDescDate } from '../state/utils/compare.util';
import { formatTimestamp } from '../state/utils/time.util';

export type DetailsTaskKind = 'preset' | 'extra';

interface TaskKindCopy {
  emptyHistory: string;
  emptyRecentRun: string;
  noRunsToday: string;
  archivePrefix: string;
  fallbackCorrelationId: string;
}

function buildCopy(): Record<DetailsTaskKind, TaskKindCopy> {
  return {
    preset: {
      emptyHistory: t('details.task.emptyHistoryPreset'),
      emptyRecentRun: t('details.task.emptyRecentPreset'),
      noRunsToday: t('details.task.noRunsToday'),
      archivePrefix: 'preset-result-',
      fallbackCorrelationId: 'preset-run',
    },
    extra: {
      emptyHistory: t('details.task.emptyHistoryExtra'),
      emptyRecentRun: t('details.task.emptyRecentExtra'),
      noRunsToday: t('details.task.noRunsToday'),
      archivePrefix: 'extra-result-',
      fallbackCorrelationId: 'extra-run',
    },
  };
}

const COPY = buildCopy();

@Component({
  selector: 'app-details-task-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './details-task-panel.component.html',
  styleUrl: './details-task-panel.component.css',
})
export class DetailsTaskPanelComponent implements OnInit {
  readonly kind = input.required<DetailsTaskKind>();

  private readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);

  readonly copy = computed<TaskKindCopy>(() => COPY[this.kind()]);
  readonly task = computed<TaskUiState>(() =>
    this.kind() === 'preset' ? this.store.presetTask() : this.store.extraTask(),
  );

  readonly isExtra = computed<boolean>(() => this.kind() === 'extra');
  readonly banks = computed<ExtraBankInfo[]>(() => this.store.extraBanks());
  readonly banksLoading = computed<boolean>(() => this.store.extraBanksLoading());

  ngOnInit(): void {
    // Подгружаем справочник банков при открытии настроек extra (панель создаётся при открытии дровера).
    if (this.isExtra()) {
      void this.store.loadExtraBanksAsync();
    }
  }

  isBankSelected(code: string): boolean {
    return this.store.isExtraBankSelected(code);
  }

  allBanksSelected(): boolean {
    return this.store.allExtraBanksSelected();
  }

  toggleBank(code: string, checked: boolean): void {
    this.store.toggleExtraBank(code, checked);
  }

  selectAllBanks(): void {
    this.store.selectAllExtraBanks();
  }

  readonly records = computed<TaskRecord[]>(() => {
    const taskCode = this.kind();
    return this.store
      .todayHistory()
      .filter((record) => record.taskCode === taskCode)
      .sort(byDescDate<TaskRecord>((record) => record.completedAt));
  });

  formatTimestamp = formatTimestamp;

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  recordFiles(record: TaskRecord): { fileName: string; filePath: string }[] {
    if (!record.outputPath) {
      return [];
    }
    return this.store.outputFilesByPath()[record.outputPath] ?? [];
  }

  buildDownloadUrl(path: string): string {
    return this.store.buildSystemDownloadUrl(path);
  }

  buildArchiveUrl(path: string): string {
    return this.store.buildSystemArchiveUrl(path);
  }

  archiveDownloadName(record: TaskRecord): string {
    const id = record.correlationId ?? 'run';
    return `${this.copy().archivePrefix}${id}.zip`;
  }
}
