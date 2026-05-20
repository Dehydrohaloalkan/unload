import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TaskRecord, TaskUiState } from '../app.models';
import { WorkflowStore } from '../app.store';
import { DownloadHintStore } from '../download-hint.store';
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

const COPY: Record<DetailsTaskKind, TaskKindCopy> = {
  preset: {
    emptyHistory: 'Скрипты этапа 2 пока не запускались.',
    emptyRecentRun: 'Файлы для этого запуска не найдены.',
    noRunsToday: 'Сегодня запусков пока не было.',
    archivePrefix: 'preset-result-',
    fallbackCorrelationId: 'preset-run',
  },
  extra: {
    emptyHistory: 'Скрипты этапа 4 пока не запускались.',
    emptyRecentRun: 'Файлы для этой выгрузки не найдены.',
    noRunsToday: 'Сегодня запусков пока не было.',
    archivePrefix: 'extra-result-',
    fallbackCorrelationId: 'extra-run',
  },
};

@Component({
  selector: 'app-details-task-panel',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './details-task-panel.component.html',
  styleUrl: './details-task-panel.component.css',
})
export class DetailsTaskPanelComponent {
  readonly kind = input.required<DetailsTaskKind>();

  private readonly store = inject(WorkflowStore);
  private readonly downloadHint = inject(DownloadHintStore);

  readonly copy = computed<TaskKindCopy>(() => COPY[this.kind()]);
  readonly task = computed<TaskUiState>(() =>
    this.kind() === 'preset' ? this.store.presetTask() : this.store.extraTask(),
  );

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
