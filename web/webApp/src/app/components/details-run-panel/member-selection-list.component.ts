import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MemberGroupViewModel, MemberViewModel } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { TPipe } from '../../i18n/i18n';

@Component({
  selector: 'app-member-selection-list',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCheckboxModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './member-selection-list.component.html',
  styleUrls: ['./details-shared.css', './member-selection-list.component.css'],
})
export class MemberSelectionListComponent {
  readonly store = inject(WorkflowStore);

  readonly selectedMemberKey = signal<string | null>(null);

  readonly memberSelectionStats = computed(() => {
    let total = 0;
    let selected = 0;
    for (const group of this.store.memberGroups()) {
      total += group.members.length;
      for (const member of group.members) {
        if (member.selected) {
          selected++;
        }
      }
    }
    return { total, selected };
  });

  readonly allMembersSelected = computed(() => {
    const { total, selected } = this.memberSelectionStats();
    return total > 0 && selected === total;
  });

  readonly allMembersPartial = computed(() => {
    const { total, selected } = this.memberSelectionStats();
    return total > 0 && selected > 0 && selected < total;
  });

  groupAllSelected(group: MemberGroupViewModel): boolean {
    return group.members.length > 0 && group.members.every((m) => m.selected);
  }

  groupPartial(group: MemberGroupViewModel): boolean {
    const selectedCount = group.members.filter((m) => m.selected).length;
    return selectedCount > 0 && selectedCount < group.members.length;
  }

  toggleAll(selected: boolean): void {
    if (selected) {
      this.store.selectAllMembers();
    } else {
      this.store.clearMemberSelection();
    }
  }

  toggleGroup(group: MemberGroupViewModel, selected: boolean): void {
    for (const member of group.members) {
      this.store.toggleMember(member.targetCodes, selected);
    }
  }

  toggleMember(member: MemberViewModel, selected: boolean): void {
    this.store.toggleMember(member.targetCodes, selected);
  }

  selectMember(key: string): void {
    this.selectedMemberKey.update((current) => (current === key ? null : key));
  }

  setPublishToGateway(checked: boolean): void {
    this.store.setPublishRunToGateway(checked);
  }

  startSelected(): void {
    void this.store.startRunAsync();
  }
}
