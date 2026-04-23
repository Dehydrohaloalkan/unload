import { CommonModule } from '@angular/common';
import { Component, inject, input } from '@angular/core';
import { MessageModule } from 'primeng/message';
import { TaskRecord, TaskUiState } from '../app.models';
import { DownloadHintStore } from '../download-hint.store';

@Component({
  selector: 'app-details-extra-panel',
  standalone: true,
  imports: [CommonModule, MessageModule],
  templateUrl: './details-extra-panel.component.html',
  styleUrl: './details-extra-panel.component.css',
})
export class DetailsExtraPanelComponent {
  readonly downloadHint = inject(DownloadHintStore);
  readonly extraTask = input.required<TaskUiState>();
  readonly records = input.required<TaskRecord[]>();
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly buildArchiveUrl = input.required<(path: string) => string>();
  readonly filesByOutputPath = input.required<Record<string, { fileName: string; filePath: string }[]>>();

  onDownloadClick(): void {
    this.downloadHint.notifyDownloadStarted();
  }

  onDownloadHintClose(): void {
    this.downloadHint.clear();
  }

  formatTimestamp(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString('ru-RU');
  }

  recordFiles(record: TaskRecord): { fileName: string; filePath: string }[] {
    if (!record.outputPath) {
      return [];
    }

    return this.filesByOutputPath()[record.outputPath] ?? [];
  }
}
