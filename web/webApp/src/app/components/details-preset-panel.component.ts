import { CommonModule } from '@angular/common';
import { Component, input } from '@angular/core';
import { TaskRecord, TaskUiState } from '../app.models';

@Component({
  selector: 'app-details-preset-panel',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './details-preset-panel.component.html',
  styleUrl: './details-preset-panel.component.css',
})
export class DetailsPresetPanelComponent {
  readonly presetTask = input.required<TaskUiState>();
  readonly records = input.required<TaskRecord[]>();
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly buildArchiveUrl = input.required<(path: string) => string>();
  readonly filesByOutputPath = input.required<Record<string, { fileName: string; filePath: string }[]>>();

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
