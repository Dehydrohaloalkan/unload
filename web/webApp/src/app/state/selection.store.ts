import { computed, inject } from '@angular/core';
import {
  signalStore,
  withComputed,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import { CatalogInfo } from '../app.models';
import { BROWSER_STORAGE } from './storage.token';
import { sortCodes } from './utils/sort.util';
import { readJson, writeJson } from './utils/storage.util';

const STORAGE_KEY = 'unload.web.target-selection';

interface SelectionState {
  selectedTargetCodes: string[];
}

const INITIAL: SelectionState = { selectedTargetCodes: [] };

export const SelectionStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ selectedTargetCodes }) => ({
    selectedCount: computed(() => selectedTargetCodes().length),
  })),
  withMethods((store) => {
    const storage = inject(BROWSER_STORAGE);

    const persist = (next: string[]): void => {
      patchState(store, { selectedTargetCodes: next });
      writeJson(storage, STORAGE_KEY, next);
    };

    const readStored = (): string[] | null => {
      const payload = readJson<unknown>(storage, STORAGE_KEY);
      return Array.isArray(payload)
        ? payload.filter((item): item is string => typeof item === 'string')
        : null;
    };

    return {
      toggleMember(targetCodes: string[], selected: boolean): void {
        const next = new Set(store.selectedTargetCodes());
        for (const code of targetCodes) {
          if (selected) {
            next.add(code);
          } else {
            next.delete(code);
          }
        }
        persist(sortCodes(next));
      },

      selectAll(allCodes: string[]): void {
        persist(sortCodes(allCodes));
      },

      clearAll(): void {
        persist([]);
      },

      reconcileFromCatalog(catalog: CatalogInfo): void {
        const availableCodes = catalog.targets.map((target) => target.targetCode);
        const stored = readStored();
        const filtered =
          stored === null ? availableCodes : availableCodes.filter((code) => stored.includes(code));
        const nextCodes = filtered.length > 0 ? filtered : availableCodes;
        persist(sortCodes(nextCodes));
      },

      resolveSelectedMemberCodes(catalog: CatalogInfo | null): string[] {
        if (!catalog) {
          return [];
        }
        const selected = new Set(store.selectedTargetCodes());
        const memberCodes = catalog.targets
          .filter((target) => selected.has(target.targetCode))
          .map((target) => target.memberCode);
        return sortCodes(memberCodes);
      },
    };
  }),
);

export type SelectionStore = InstanceType<typeof SelectionStore>;
