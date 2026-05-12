import { Injectable, computed, signal } from '@angular/core';
import { CatalogInfo, MemberCatalogItem } from '../app.models';
import { buildHistoryMemberNames } from './utils/member-projections.util';

@Injectable({ providedIn: 'root' })
export class CatalogStore {
  readonly catalog = signal<CatalogInfo | null>(null);
  readonly members = signal<MemberCatalogItem[]>([]);

  readonly historyMemberNames = computed(() =>
    buildHistoryMemberNames(this.catalog(), this.members()),
  );

  setCatalog(value: CatalogInfo): void {
    this.catalog.set(value);
  }

  setMembers(value: MemberCatalogItem[]): void {
    this.members.set(value);
  }
}
