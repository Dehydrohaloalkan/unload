import { signalStore, withMethods, withState, patchState } from '@ngrx/signals';

export const AdminStore = signalStore(
  { providedIn: 'root' },
  withState({ adminMode: false }),
  withMethods((store) => ({
    setAdminMode(enabled: boolean): void {
      patchState(store, { adminMode: enabled });
    },
  })),
);

export type AdminStore = InstanceType<typeof AdminStore>;
