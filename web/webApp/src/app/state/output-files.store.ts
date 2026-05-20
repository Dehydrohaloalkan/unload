import { inject, isDevMode } from '@angular/core';
import { signalStore, withMethods, withState, patchState } from '@ngrx/signals';
import { OutputFileInfo, TaskRecord } from '../app.models';
import { ApiClientService } from './api-client.service';

interface OutputFilesState {
  filesByOutputPath: Record<string, OutputFileInfo[]>;
}

const INITIAL: OutputFilesState = { filesByOutputPath: {} };

export const OutputFilesStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withMethods((store) => {
    const api = inject(ApiClientService);

    return {
      async refreshForHistory(history: TaskRecord[]): Promise<void> {
        const paths = new Set<string>();
        for (const record of history) {
          if (
            (record.taskCode === 'extra' || record.taskCode === 'preset') &&
            record.outputPath
          ) {
            paths.add(record.outputPath);
          }
        }

        if (paths.size === 0) {
          return;
        }

        const cache = { ...store.filesByOutputPath() };
        for (const path of paths) {
          if (cache[path]) {
            continue;
          }
          try {
            cache[path] = await api.fetchOutputFiles(path);
          } catch (error) {
            cache[path] = [];
            if (isDevMode()) {
              console.error(error);
            }
          }
        }

        patchState(store, { filesByOutputPath: cache });
      },
    };
  }),
);

export type OutputFilesStore = InstanceType<typeof OutputFilesStore>;
