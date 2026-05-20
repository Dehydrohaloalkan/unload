import { computed } from '@angular/core';
import { signalStore, withComputed, withMethods, withState, patchState } from '@ngrx/signals';
import { CatalogInfo, MemberCatalogItem } from '../app.models';
import { buildHistoryMemberNames } from './utils/member-projections.util';

interface CatalogState {
  catalog: CatalogInfo | null;
  members: MemberCatalogItem[];
}

const INITIAL: CatalogState = { catalog: null, members: [] };

export const CatalogStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ catalog, members }) => ({
    historyMemberNames: computed(() => buildHistoryMemberNames(catalog(), members())),
  })),
  withMethods((store) => ({
    setCatalog(value: CatalogInfo): void {
      patchState(store, { catalog: value });
    },
    setMembers(value: MemberCatalogItem[]): void {
      patchState(store, { members: value });
    },
  })),
);

export type CatalogStore = InstanceType<typeof CatalogStore>;
