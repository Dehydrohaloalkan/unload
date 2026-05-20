import { signalStore, withMethods, withState, patchState } from '@ngrx/signals';

export const WorkflowErrorStore = signalStore(
  { providedIn: 'root' },
  withState<{ errorMessage: string | null }>({ errorMessage: null }),
  withMethods((store) => ({
    setError(message: string | null): void {
      patchState(store, { errorMessage: message });
    },
    clear(): void {
      patchState(store, { errorMessage: null });
    },
  })),
);

export type WorkflowErrorStore = InstanceType<typeof WorkflowErrorStore>;
