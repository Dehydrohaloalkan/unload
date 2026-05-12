import { Injectable, inject, isDevMode, signal } from '@angular/core';
import { OutputFileInfo, TaskRecord } from '../app.models';
import { ApiClientService } from './api-client.service';

@Injectable({ providedIn: 'root' })
export class OutputFilesStore {
  private readonly api = inject(ApiClientService);

  readonly filesByOutputPath = signal<Record<string, OutputFileInfo[]>>({});

  async refreshForHistory(history: TaskRecord[]): Promise<void> {
    const paths = new Set<string>();
    for (const record of history) {
      if ((record.taskCode === 'extra' || record.taskCode === 'preset') && record.outputPath) {
        paths.add(record.outputPath);
      }
    }

    if (paths.size === 0) {
      return;
    }

    const cache = { ...this.filesByOutputPath() };
    for (const path of paths) {
      if (cache[path]) {
        continue;
      }

      try {
        cache[path] = await this.api.fetchOutputFiles(path);
      } catch (error) {
        cache[path] = [];
        if (isDevMode()) {
          console.error(error);
        }
      }
    }

    this.filesByOutputPath.set(cache);
  }
}
