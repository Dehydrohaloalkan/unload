import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { CatalogInfo } from '../app.models';
import { browserStorage, readJson, writeJson } from './utils/storage.util';
import { sortCodes } from './utils/sort.util';

const STORAGE_KEY = 'unload.web.target-selection';

@Injectable({ providedIn: 'root' })
export class SelectionStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);
  private readonly storage = browserStorage(this.browser);

  readonly selectedTargetCodes = signal<string[]>([]);
  readonly selectedCount = computed(() => this.selectedTargetCodes().length);

  toggleMember(targetCodes: string[], selected: boolean): void {
    const next = new Set(this.selectedTargetCodes());
    for (const targetCode of targetCodes) {
      if (selected) {
        next.add(targetCode);
      } else {
        next.delete(targetCode);
      }
    }

    this.selectedTargetCodes.set(sortCodes(next));
    this.persist();
  }

  selectAll(allCodes: string[]): void {
    this.selectedTargetCodes.set(sortCodes(allCodes));
    this.persist();
  }

  clearAll(): void {
    this.selectedTargetCodes.set([]);
    this.persist();
  }

  reconcileFromCatalog(catalog: CatalogInfo): void {
    const availableCodes = catalog.targets.map((target) => target.targetCode);
    const storedSelection = this.readStored();
    const filtered =
      storedSelection === null
        ? availableCodes
        : availableCodes.filter((code) => storedSelection.includes(code));
    const nextCodes = filtered.length > 0 ? filtered : availableCodes;

    this.selectedTargetCodes.set(sortCodes(nextCodes));
    this.persist();
  }

  resolveSelectedMemberCodes(catalog: CatalogInfo | null): string[] {
    if (!catalog) {
      return [];
    }

    const selected = new Set(this.selectedTargetCodes());
    const memberCodes = catalog.targets
      .filter((target) => selected.has(target.targetCode))
      .map((target) => target.memberCode);

    return sortCodes(memberCodes);
  }

  private persist(): void {
    writeJson(this.storage, STORAGE_KEY, this.selectedTargetCodes());
  }

  private readStored(): string[] | null {
    const payload = readJson<unknown>(this.storage, STORAGE_KEY);
    return Array.isArray(payload)
      ? payload.filter((item): item is string => typeof item === 'string')
      : null;
  }
}
